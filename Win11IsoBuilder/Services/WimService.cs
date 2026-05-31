using System.IO;
using Win11IsoBuilder.Models;
using Win11IsoBuilder.Services.Dism;

namespace Win11IsoBuilder.Services;

/// <summary>
/// Wraps DISM image servicing: enumerate editions, convert ESD→WIM, mount/unmount,
/// list and remove provisioned Appx (bloatware). Callers MUST pair MountWim with
/// UnmountWim in a try/finally so a failure never leaves a stuck mount.
/// </summary>
public sealed class WimService
{
    private static readonly TimeSpan DismTimeout = TimeSpan.FromMinutes(30);

    private readonly ProcessRunner _runner;
    private readonly ILogSink _log;
    private readonly string _dismPath;

    public WimService(ProcessRunner runner, ILogSink log, string dismPath)
    {
        _runner = runner;
        _log = log;
        _dismPath = dismPath;
    }

    public async Task<List<WindowsEdition>> GetImageInfoAsync(string wimPath, CancellationToken ct = default)
    {
        var r = await Dism($"/Get-ImageInfo /ImageFile:\"{wimPath}\"", ct).ConfigureAwait(false);
        EnsureSuccess(r, "Get-ImageInfo");
        return DismOutputParser.ParseImageInfo(r.StdOut);
    }

    /// <summary>
    /// install.esd cannot be edited in place. If the source is an ESD, export the chosen
    /// index to a max-compressed WIM (which collapses to a single image at index 1) and return
    /// that path plus the index to use downstream. For an existing WIM the original index stands.
    /// </summary>
    public async Task<(string WimPath, int Index)> EnsureEditableWimAsync(
        string sourcesDir, int index, CancellationToken ct = default)
    {
        var wim = Path.Combine(sourcesDir, "install.wim");
        if (File.Exists(wim)) return (wim, index);

        var esd = Path.Combine(sourcesDir, "install.esd");
        if (!File.Exists(esd))
            throw new FileNotFoundException("Neither install.wim nor install.esd found.", sourcesDir);

        _log.Info($"Exporting ESD index {index} → WIM (max compression)...");
        var r = await Dism(
            $"/Export-Image /SourceImageFile:\"{esd}\" /SourceIndex:{index} " +
            $"/DestinationImageFile:\"{wim}\" /Compress:max /CheckIntegrity", ct).ConfigureAwait(false);
        EnsureSuccess(r, "Export-Image (ESD→WIM)");

        TryDelete(esd); // remove ESD so Setup uses the editable WIM
        // The exported WIM holds exactly one image → it is now index 1.
        return (wim, 1);
    }

    public async Task MountWimAsync(string wimPath, int index, string mountDir, CancellationToken ct = default)
    {
        Directory.CreateDirectory(mountDir);
        ClearReadOnly(wimPath); // a read-only WIM cannot be mounted for modify (DISM 0xc1510111)
        _log.Info($"Mounting image index {index} → {mountDir}");
        var r = await Dism(
            $"/Mount-Wim /WimFile:\"{wimPath}\" /Index:{index} /MountDir:\"{mountDir}\"", ct)
            .ConfigureAwait(false);
        EnsureSuccess(r, "Mount-Wim");
    }

    /// <summary>Commit (save edits) or discard, then unmount. Always call in a finally.</summary>
    public async Task UnmountWimAsync(string mountDir, bool commit, CancellationToken ct = default)
    {
        var mode = commit ? "/Commit" : "/Discard";
        _log.Info($"Unmounting {mountDir} ({mode})");
        // Do not propagate caller cancellation here: unmount must finish to free the mount.
        var r = await Dism($"/Unmount-Wim /MountDir:\"{mountDir}\" {mode}", CancellationToken.None)
            .ConfigureAwait(false);
        if (!r.Success) _log.Error($"Unmount-Wim failed ({r.ExitCode}). Run Cleanup if mount is stuck.");
    }

    public async Task<List<ProvisionedAppx>> GetProvisionedAppxAsync(string mountDir, CancellationToken ct = default)
    {
        var r = await Dism($"/Image:\"{mountDir}\" /Get-ProvisionedAppxPackages", ct).ConfigureAwait(false);
        EnsureSuccess(r, "Get-ProvisionedAppxPackages");
        return DismOutputParser.ParseProvisionedAppx(r.StdOut);
    }

    /// <summary>Remove the given provisioned packages; one failure does not stop the rest.</summary>
    public async Task RemoveProvisionedAppxAsync(string mountDir, IEnumerable<string> packageNames, CancellationToken ct = default)
    {
        foreach (var pkg in packageNames)
        {
            ct.ThrowIfCancellationRequested();
            var r = await Dism(
                $"/Image:\"{mountDir}\" /Remove-ProvisionedAppxPackage /PackageName:\"{pkg}\"", ct)
                .ConfigureAwait(false);
            if (r.Success) _log.Info($"Removed appx: {pkg}");
            else _log.Warn($"Could not remove appx {pkg} (exit {r.ExitCode}) — continuing.");
        }
    }

    /// <summary>Clear orphaned mounts left by a previous crashed run. Best-effort.</summary>
    public async Task CleanupOrphanMountsAsync(CancellationToken ct = default)
    {
        _log.Info("Cleaning up any orphaned WIM mounts...");
        var r = await Dism("/Cleanup-Wim", ct).ConfigureAwait(false);
        if (!r.Success) _log.Warn($"Cleanup-Wim returned {r.ExitCode} (usually harmless).");
    }

    private Task<ProcessResult> Dism(string args, CancellationToken ct) =>
        _runner.RunAsync(_dismPath, "/English " + args, timeout: DismTimeout, cancellationToken: ct);

    private void EnsureSuccess(ProcessResult r, string op)
    {
        if (!r.Success)
            throw new InvalidOperationException(
                $"DISM {op} failed (exit {r.ExitCode}). {FirstError(r.StdErr, r.StdOut)}");
    }

    private static string FirstError(string stderr, string stdout)
    {
        var text = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
        return text.Replace("\r\n", "\n").Split('\n').FirstOrDefault(l => l.Contains("Error"))
               ?? text.Trim();
    }

    private void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) { _log.Warn($"Could not delete {path}: {ex.Message}"); }
    }

    private void ClearReadOnly(string path)
    {
        try
        {
            if (!File.Exists(path)) return;
            var attrs = File.GetAttributes(path);
            if (attrs.HasFlag(FileAttributes.ReadOnly))
                File.SetAttributes(path, attrs & ~FileAttributes.ReadOnly);
        }
        catch (Exception ex) { _log.Warn($"Could not clear read-only on {path}: {ex.Message}"); }
    }
}
