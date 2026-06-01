using System.IO;
using Win11IsoBuilder.Models;

namespace Win11IsoBuilder.Services;

/// <summary>
/// Runs the end-to-end build as an ordered, cancellable, progress-reporting pipeline.
/// All shared state lives in the <see cref="BuildConfig"/>. On any failure the WIM mount
/// is discarded so the workspace is never left in a stuck state.
/// </summary>
public sealed class BuildOrchestrator
{
    private readonly LogService _log;
    private readonly ProcessRunner _runner;
    private readonly ToolDetectionService _tools;

    public BuildOrchestrator(LogService log)
    {
        _log = log;
        _runner = new ProcessRunner(log);
        _tools = new ToolDetectionService(log);
    }

    public async Task RunAsync(BuildConfig cfg, IProgress<BuildProgress> progress, CancellationToken ct)
    {
        var iso = new IsoService(_runner, _log);
        WimService? wim = null;
        var mounted = false;

        try
        {
            // 1. Resolve tools.
            Report(progress, 2, "Detecting tools", "Locating DISM and oscdimg...");
            cfg.Tools = _tools.Detect();
            if (!cfg.Tools.HasDism) throw new InvalidOperationException("DISM not found.");
            if (cfg.Tools.IsAdkMissing) throw new InvalidOperationException(
                "oscdimg not found. Bundle it under tools\\oscdimg or install the Windows ADK.");
            wim = new WimService(_runner, _log, cfg.Tools.DismPath!);

            // 2. Clear any orphaned mounts from a previous crashed run.
            await wim.CleanupOrphanMountsAsync(cfg.MountDir, ct).ConfigureAwait(false);

            // 3. Validate source ISO.
            Report(progress, 5, "Validating", "Checking source ISO...");
            if (string.IsNullOrWhiteSpace(cfg.SourceIsoPath) || !File.Exists(cfg.SourceIsoPath))
                throw new FileNotFoundException("Select a valid source ISO first.");
            PrepareWorkspace(cfg);

            // 4. Extract ISO → media.
            Report(progress, 10, "Extracting ISO", "Copying ISO contents...");
            await iso.ExtractIsoAsync(cfg.SourceIsoPath!, cfg.MediaDir, ct).ConfigureAwait(false);
            if (!iso.ValidateMedia(cfg.MediaDir, out var reason))
                throw new InvalidOperationException($"Invalid Windows media: {reason}");

            // 5. Ensure an editable WIM (export from ESD if needed). The returned index reflects
            //    the post-export layout (ESD export collapses the chosen edition to index 1).
            Report(progress, 30, "Preparing image", "Ensuring editable install.wim...");
            var sources = Path.Combine(cfg.MediaDir, "sources");
            var (wimPath, index) = await wim.EnsureEditableWimAsync(
                sources, cfg.SelectedEdition?.Index ?? 1, ct).ConfigureAwait(false);

            // 6. Remove bloatware (mount → remove → commit). try/finally guards the unmount.
            if (cfg.Windows.AppxToRemove.Count > 0)
            {
                Report(progress, 40, "Removing bloatware", $"Mounting image (index {index})...");
                await wim.MountWimAsync(wimPath, index, cfg.MountDir, ct).ConfigureAwait(false);
                mounted = true;

                // Map selected family patterns → actual versioned PackageNames in this image.
                var installed = await wim.GetProvisionedAppxAsync(cfg.MountDir, ct).ConfigureAwait(false);
                // Match the exact provisioned family: PackageName is "<Family>_<version>_...",
                // so require the underscore boundary to avoid prefixes like People→PeopleExperienceHost.
                var toRemove = installed
                    .Where(p => cfg.Windows.AppxToRemove.Any(pat =>
                        p.PackageName.StartsWith(pat + "_", StringComparison.OrdinalIgnoreCase) ||
                        p.DisplayName.Equals(pat, StringComparison.OrdinalIgnoreCase)))
                    .Select(p => p.PackageName)
                    .ToList();
                _log.Info($"Bloatware: {toRemove.Count} package(s) matched for removal.");
                await wim.RemoveProvisionedAppxAsync(cfg.MountDir, toRemove, ct).ConfigureAwait(false);

                Report(progress, 55, "Removing bloatware", "Committing image changes...");
                await wim.UnmountWimAsync(cfg.MountDir, commit: true, ct).ConfigureAwait(false);
                mounted = false;
            }

            // 7. autounattend.xml.
            Report(progress, 60, "Configuring Windows", "Writing autounattend.xml...");
            new UnattendBuilder(_log).Write(cfg.Windows, cfg.MediaDir);

            // 8. Office payload.
            if (cfg.Office.Enabled)
            {
                Report(progress, 65, "Office", "Downloading + staging Microsoft 365 (offline)...");
                var office = new OfficeOdtService(_runner, _log);
                await office.DownloadOfflineAsync(cfg.Office, Path.Combine(cfg.CacheDir, "office"), ct)
                    .ConfigureAwait(false);
                await office.StageToPayloadAsync(cfg.Office, Path.Combine(cfg.CacheDir, "office"),
                    cfg.PayloadDir, ct).ConfigureAwait(false);
            }

            // 9. App payload + first-boot scripts.
            Report(progress, 85, "Apps", "Staging selected installers...");
            var apps = new AppCatalogService(_log);
            var commands = await apps.StageToPayloadAsync(
                cfg.SelectedApps.Where(a => a.IsSelected), cfg.PayloadDir, cfg.CacheDir, ct)
                .ConfigureAwait(false);
            new FirstBootScriptBuilder(_log).Write(cfg, cfg.Office.Enabled, commands);

            // 10. Repack ISO.
            Report(progress, 92, "Repacking ISO", $"Building {cfg.OutputIsoName}...");
            await iso.RepackIsoAsync(cfg.MediaDir, cfg.OutputIsoPath, cfg.Tools.OscdimgPath!,
                ct: ct).ConfigureAwait(false);

            // 11. Reclaim the multi-GB scratch tree; keep the Office/app cache for fast rebuilds.
            Report(progress, 98, "Cleanup", "Removing temporary workspace...");
            CleanupWorkspace(cfg);

            Report(progress, 100, "Done", $"ISO created: {cfg.OutputIsoPath}");
        }
        catch (Exception ex)
        {
            if (mounted && wim is not null)
            {
                _log.Warn("Failure mid-mount — discarding WIM mount to keep the workspace clean.");
                try { await wim.UnmountWimAsync(cfg.MountDir, commit: false, CancellationToken.None)
                    .ConfigureAwait(false); }
                catch (Exception e) { _log.Error($"Cleanup unmount failed: {e.Message}"); }
            }
            Report(progress, 0, "Failed", ex.Message, isError: true);
            throw;
        }
    }

    private static void PrepareWorkspace(BuildConfig cfg)
    {
        // Fresh media + mount dir each build; cache (Office/app downloads) is preserved. DISM
        // /Mount-Image requires an empty mount dir, but the prior run's end cleanup is best-effort
        // and can leave it behind (delayed handle release), so reset it here too. Any live mount was
        // already discarded by CleanupOrphanMountsAsync above, so the dir is now deletable.
        if (Directory.Exists(cfg.MediaDir)) ForceDeleteDirectory(cfg.MediaDir);
        if (Directory.Exists(cfg.MountDir)) ForceDeleteDirectory(cfg.MountDir);
        Directory.CreateDirectory(cfg.MediaDir);
        Directory.CreateDirectory(cfg.CacheDir);
    }

    private void CleanupWorkspace(BuildConfig cfg)
    {
        foreach (var dir in new[] { cfg.MediaDir, cfg.MountDir })
        {
            try { if (Directory.Exists(dir)) ForceDeleteDirectory(dir); }
            catch (Exception ex) { _log.Warn($"Could not remove {dir}: {ex.Message}"); }
        }
    }

    /// <summary>
    /// Delete a workspace tree robustly. Extracted media files are read-only (inherited from the
    /// ISO) so clear that first; freshly-written boot files can also be briefly locked by an AV
    /// scan or a lagging handle release, so retry the delete with backoff before giving up.
    /// </summary>
    private static void ForceDeleteDirectory(string dir)
    {
        foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
        {
            try
            {
                var attrs = File.GetAttributes(file);
                if (attrs.HasFlag(FileAttributes.ReadOnly))
                    File.SetAttributes(file, attrs & ~FileAttributes.ReadOnly);
            }
            catch (IOException) { /* locked file — the retry loop below handles it */ }
            catch (UnauthorizedAccessException) { }
        }

        for (var attempt = 1; ; attempt++)
        {
            try { Directory.Delete(dir, recursive: true); return; }
            catch (Exception) when (attempt < 5 && (Directory.Exists(dir)))
            {
                Thread.Sleep(attempt * 500); // 0.5s, 1s, 1.5s, 2s
            }
        }
    }

    private void Report(IProgress<BuildProgress> p, int pct, string stage, string msg, bool isError = false)
    {
        if (isError) _log.Error($"{stage}: {msg}"); else _log.Info($"{stage}: {msg}");
        p.Report(new BuildProgress(pct, stage, msg, isError));
    }
}
