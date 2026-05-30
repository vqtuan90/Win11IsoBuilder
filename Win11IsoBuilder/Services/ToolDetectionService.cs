using System.IO;
using Win11IsoBuilder.Models;

namespace Win11IsoBuilder.Services;

/// <summary>
/// Locates the external tools the pipeline needs: DISM (always in System32) and
/// oscdimg. oscdimg resolution order: bundled copy under the app's tools\ folder
/// (default — user does not need the ADK), then a scan of installed Windows Kits.
/// </summary>
public sealed class ToolDetectionService
{
    private readonly ILogSink _log;
    private readonly string _baseDir;

    public ToolDetectionService(ILogSink log, string? baseDir = null)
    {
        _log = log;
        _baseDir = baseDir ?? AppContext.BaseDirectory;
    }

    public ToolPaths Detect()
    {
        var paths = new ToolPaths
        {
            DismPath = ResolveDism(),
        };

        var (oscdimg, source) = ResolveOscdimg();
        paths.OscdimgPath = oscdimg;
        paths.OscdimgSource = source;

        _log.Info($"DISM: {paths.DismPath ?? "NOT FOUND"}");
        _log.Info($"oscdimg: {paths.OscdimgPath ?? "NOT FOUND"} (source={paths.OscdimgSource})");
        if (paths.IsAdkMissing)
            _log.Warn("oscdimg not found — bundle it under tools\\oscdimg or install Windows ADK.");

        return paths;
    }

    private string? ResolveDism()
    {
        var winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var dism = Path.Combine(winDir, "System32", "dism.exe");
        return File.Exists(dism) ? dism : null;
    }

    private (string? path, OscdimgSource source) ResolveOscdimg()
    {
        // 1) Bundled copy shipped with the app (preferred, offline, no ADK install).
        var bundled = Path.Combine(_baseDir, "tools", "oscdimg", "oscdimg.exe");
        if (File.Exists(bundled)) return (bundled, OscdimgSource.Bundled);

        // 2) Fall back to an installed Windows ADK (Deployment Tools).
        var adk = ScanAdkForOscdimg();
        if (adk is not null) return (adk, OscdimgSource.Adk);

        return (null, OscdimgSource.None);
    }

    private string? ScanAdkForOscdimg()
    {
        foreach (var pf in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                 })
        {
            if (string.IsNullOrEmpty(pf)) continue;
            var deployRoot = Path.Combine(pf,
                "Windows Kits", "10", "Assessment and Deployment Kit", "Deployment Tools");
            if (!Directory.Exists(deployRoot)) continue;

            try
            {
                // Layout: Deployment Tools\<arch>\Oscdimg\oscdimg.exe — prefer amd64.
                var matches = Directory
                    .EnumerateFiles(deployRoot, "oscdimg.exe", SearchOption.AllDirectories)
                    .OrderByDescending(p => p.Contains("amd64", StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (matches.Count > 0) return matches[0];
            }
            catch (Exception ex)
            {
                _log.Warn($"ADK scan failed under {deployRoot}: {ex.Message}");
            }
        }
        return null;
    }
}
