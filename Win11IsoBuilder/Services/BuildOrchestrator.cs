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

            // 5b. Resolve storage drivers (Intel VMD catalog + user folders) and service
            //     boot.wim so Setup can see NVMe disks behind Intel VMD (11th-gen+ platforms).
            Report(progress, 33, "Drivers", "Resolving storage driver packages...");
            var drivers = new DriverInjectionService(_runner, _log);
            var driverFolders = await drivers.ResolveDriverFoldersAsync(cfg.Drivers, cfg.CacheDir, ct)
                .ConfigureAwait(false);
            // Win11 24H2+ ships the new ConX Setup, which silently drops the specialize +
            // oobeSystem passes of autounattend (interactive OOBE, no app install). Patching
            // boot.wim to launch the legacy setup.exe restores full unattended behavior.
            var conx = LegacySetupPatcher.IsConXMedia(sources);
            if (conx) _log.Info("Win11 24H2+ (ConX Setup) media detected — forcing legacy Setup " +
                                "so OOBE automation and first-boot app install work.");

            if (driverFolders.Count > 0 || conx)
            {
                Report(progress, 36, "Servicing boot.wim", "Injecting drivers / legacy-Setup patch...");
                try
                {
                    await drivers.ServiceBootWimAsync(wim, Path.Combine(sources, "boot.wim"),
                        cfg.MountDir, driverFolders, conx, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) when (!conx)
                {
                    // Driver-only failure keeps the warn-not-fail contract: the ISO still
                    // installs everywhere except VMD machines, and install.wim still gets
                    // the drivers below. The failed mount was already discarded.
                    _log.Warn($"boot.wim driver injection failed ({ex.Message}) — " +
                              "Setup may not see NVMe disks behind Intel VMD. Continuing.");
                }
                // On ConX media the patch is NOT optional: without it the built ISO silently
                // loses OOBE automation and offline app install, so the failure must surface.
            }

            // 6. Service install.wim in one mount: remove bloatware + inject drivers, then commit.
            if (cfg.Windows.AppxToRemove.Count > 0 || driverFolders.Count > 0)
            {
                Report(progress, 40, "Servicing image", $"Mounting image (index {index})...");
                await wim.MountWimAsync(wimPath, index, cfg.MountDir, ct).ConfigureAwait(false);
                mounted = true;

                if (cfg.Windows.AppxToRemove.Count > 0)
                {
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
                }

                if (driverFolders.Count > 0)
                {
                    Report(progress, 50, "Drivers", "Injecting storage drivers into install.wim...");
                    await wim.AddDriversAsync(cfg.MountDir, driverFolders, ct).ConfigureAwait(false);
                }

                Report(progress, 55, "Servicing image", "Committing image changes...");
                await wim.UnmountWimAsync(cfg.MountDir, commit: true, ct).ConfigureAwait(false);
                mounted = false;
            }

            // 7. autounattend.xml (zero-touch needs the post-export image index for ImageInstall).
            Report(progress, 60, "Configuring Windows", "Writing autounattend.xml...");
            new UnattendBuilder(_log).Write(cfg.Windows, cfg.MediaDir, index);

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
    /// Delete a workspace tree robustly. Extracted media is read-only (inherited from the ISO
    /// mount) on both files AND directories — RemoveDirectory rejects a read-only-flagged
    /// directory outright, so a leftover one (e.g. from an interrupted prior build) makes every
    /// retry below fail identically forever instead of actually being transient. Clear the
    /// attribute on files and directories alike before deleting; freshly-written boot files can
    /// also be briefly locked by an AV scan or a lagging handle release, so retry with backoff too.
    /// </summary>
    internal static void ForceDeleteDirectory(string dir)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(dir, "*", SearchOption.AllDirectories)
                     .Append(dir))
        {
            try
            {
                var attrs = File.GetAttributes(entry);
                if (attrs.HasFlag(FileAttributes.ReadOnly))
                    File.SetAttributes(entry, attrs & ~FileAttributes.ReadOnly);
            }
            catch (IOException) { /* locked entry — the retry loop below handles it */ }
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
