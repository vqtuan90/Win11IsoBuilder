using System.IO;
using System.Net.Http;
using System.Text.Json;
using Win11IsoBuilder.Models;

namespace Win11IsoBuilder.Services;

/// <summary>
/// Resolves storage-driver folders for offline injection: acquires pinned catalog packages
/// (download → SHA-256 verify → "SetupRST.exe -extractdrivers" into the cache) and validates
/// user-supplied driver folders. Also services boot.wim so Windows Setup can see NVMe disks
/// behind Intel VMD (11th-gen+ platforms). A failed catalog download only warns — user
/// folders remain the offline fallback, so the build never hard-fails on a dead URL.
/// </summary>
public sealed class DriverInjectionService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(30) };
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };
    private static readonly TimeSpan ExtractTimeout = TimeSpan.FromMinutes(5);

    private readonly ProcessRunner _runner;
    private readonly ILogSink _log;
    private readonly string _catalogPath;

    public DriverInjectionService(ProcessRunner runner, ILogSink log, string? catalogPath = null)
    {
        _runner = runner;
        _log = log;
        _catalogPath = catalogPath ?? Path.Combine(AppContext.BaseDirectory, "Assets", "driver-catalog.json");
    }

    public List<DriverPackage> LoadCatalog()
    {
        if (!File.Exists(_catalogPath))
        {
            _log.Warn($"Driver catalog not found at {_catalogPath}.");
            return new List<DriverPackage>();
        }
        var json = File.ReadAllText(_catalogPath);
        return JsonSerializer.Deserialize<List<DriverPackage>>(json, JsonOpts) ?? new List<DriverPackage>();
    }

    /// <summary>
    /// Resolve every driver folder to inject: extracted catalog packages (when enabled)
    /// plus valid user folders. Folders without any .inf are skipped with a warning.
    /// </summary>
    public async Task<List<string>> ResolveDriverFoldersAsync(
        DriverOptions options, string cacheDir, CancellationToken ct = default)
    {
        var folders = new List<string>();

        if (options.IncludeIntelVmd)
        {
            // A broken catalog file must not fail the build — user folders still work.
            List<DriverPackage> catalog;
            try { catalog = LoadCatalog(); }
            catch (Exception ex)
            {
                _log.Warn($"Driver catalog unreadable ({ex.Message}) — continuing without catalog drivers.");
                catalog = new List<DriverPackage>();
            }

            foreach (var pkg in catalog)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    folders.Add(await AcquireAndExtractAsync(pkg, cacheDir, ct).ConfigureAwait(false));
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    _log.Warn($"Driver package '{pkg.Id}' unavailable ({ex.Message}) — " +
                              "continuing without it. Add a custom driver folder if this platform needs it.");
                }
            }
        }

        foreach (var dir in options.DriverFolders)
        {
            if (Directory.Exists(dir) && ContainsInf(dir)) folders.Add(dir);
            else _log.Warn($"Driver folder skipped (missing or contains no .inf): {dir}");
        }

        _log.Info($"Drivers: {folders.Count} folder(s) resolved for injection.");
        return folders;
    }

    /// <summary>
    /// Ensure the package installer is cached + hash-verified, then extract its drivers.
    /// Returns the extracted folder (reused across builds when it already holds .inf files).
    /// </summary>
    public async Task<string> AcquireAndExtractAsync(
        DriverPackage pkg, string cacheDir, CancellationToken ct = default)
    {
        // Unlike catalog apps (only staged onto media), this file is EXECUTED elevated on
        // the build machine — an unpinned download is never acceptable here.
        if (string.IsNullOrWhiteSpace(pkg.Sha256))
            throw new InvalidOperationException(
                $"Driver package '{pkg.Id}' has no SHA-256 pin — refusing to run an unverified download.");

        var pkgDir = Path.Combine(cacheDir, "drivers", pkg.Id);
        Directory.CreateDirectory(pkgDir);
        var exe = Path.Combine(pkgDir, pkg.FileName);
        var extractDir = Path.Combine(pkgDir, "extracted");

        if (Directory.Exists(extractDir) && ContainsInf(extractDir))
        {
            _log.Info($"Using cached extracted driver: {pkg.Id}");
            return extractDir;
        }

        if (!File.Exists(exe) || !await HashVerifier.IsHashOkAsync(exe, pkg.Sha256, ct).ConfigureAwait(false))
        {
            _log.Info($"Downloading driver package {pkg.Id}: {pkg.SourceUrl}");
            using var resp = await Http.GetAsync(pkg.SourceUrl, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            await using (var fs = File.Create(exe))
                await resp.Content.CopyToAsync(fs, ct).ConfigureAwait(false);

            if (!await HashVerifier.IsHashOkAsync(exe, pkg.Sha256, ct).ConfigureAwait(false))
                throw new InvalidOperationException($"SHA-256 mismatch for driver package {pkg.Id}.");
        }

        // Intel's documented switch extracts the raw .inf driver files without installing.
        if (Directory.Exists(extractDir)) Directory.Delete(extractDir, recursive: true);
        var r = await _runner.RunAsync(exe, $"-extractdrivers \"{extractDir}\"",
            timeout: ExtractTimeout, cancellationToken: ct).ConfigureAwait(false);
        if (!ContainsInf(extractDir))
            throw new InvalidOperationException(
                $"Driver extraction produced no .inf files for {pkg.Id} (exit {r.ExitCode}).");

        _log.Info($"Extracted driver package {pkg.Id} → {extractDir}");
        return extractDir;
    }

    /// <summary>
    /// Service every image inside boot.wim (WinPE + Setup) in a single pass per index:
    /// inject the resolved driver folders and, on ConX media (Win11 24H2+), write the
    /// legacy-Setup winpeshl.ini into the Setup image so autounattend's specialize and
    /// oobeSystem passes are honored. Each index is mounted, serviced, and committed only
    /// when changed; a failure discards that mount so the workspace never keeps a stuck mount.
    /// </summary>
    public async Task ServiceBootWimAsync(
        WimService wim, string bootWimPath, string mountDir,
        IReadOnlyList<string> driverFolders, bool forceLegacySetup, CancellationToken ct = default)
    {
        if (!File.Exists(bootWimPath))
        {
            _log.Warn($"boot.wim not found at {bootWimPath} — Setup may not see VMD disks.");
            return;
        }

        var patched = false;
        var images = await wim.GetImageInfoAsync(bootWimPath, ct).ConfigureAwait(false);
        foreach (var image in images)
        {
            _log.Info($"Servicing boot.wim index {image.Index} ({image.Name})...");
            await wim.MountWimAsync(bootWimPath, image.Index, mountDir, ct).ConfigureAwait(false);
            try
            {
                var changed = false;
                if (driverFolders.Count > 0)
                {
                    await wim.AddDriversAsync(mountDir, driverFolders, ct).ConfigureAwait(false);
                    changed = true;
                }
                if (forceLegacySetup && LegacySetupPatcher.TryPatchMountedImage(mountDir))
                {
                    _log.Info($"Wrote winpeshl.ini (legacy Setup) into boot.wim index {image.Index}.");
                    patched = changed = true;
                }
                await wim.UnmountWimAsync(mountDir, commit: changed, ct).ConfigureAwait(false);
            }
            catch
            {
                // Frees the mount dir whether servicing or the commit itself failed, so the
                // caller (and the next index) never inherits a stuck mount. Best-effort.
                await wim.UnmountWimAsync(mountDir, commit: false, CancellationToken.None).ConfigureAwait(false);
                throw;
            }
        }

        if (forceLegacySetup && !patched)
            throw new InvalidOperationException(
                "No boot.wim image contained sources\\setup.exe — could not apply the legacy-Setup patch.");
    }

    /// <summary>True when the folder holds at least one .inf anywhere below it.</summary>
    public static bool ContainsInf(string dir) =>
        Directory.Exists(dir) &&
        Directory.EnumerateFiles(dir, "*.inf", SearchOption.AllDirectories).Any();
}
