using System.IO;

namespace Win11IsoBuilder.Models;

/// <summary>
/// Single source of truth carried through the whole build pipeline. Every user choice
/// (ISO, edition, Windows tweaks, Office, apps) and every derived workspace path lives here.
/// Services read/write this object rather than sharing ad-hoc state.
/// </summary>
public sealed class BuildConfig
{
    // ---- Inputs -------------------------------------------------------------
    /// <summary>User-supplied source Windows 11 ISO.</summary>
    public string? SourceIsoPath { get; set; }

    /// <summary>Root scratch folder for extraction/mount/output.</summary>
    public string WorkDir { get; set; } =
        Path.Combine(Path.GetTempPath(), "Win11IsoBuilder", "work");

    /// <summary>Reusable cache for Office source + downloaded app installers (survives builds).</summary>
    public string CacheDir { get; set; } =
        Path.Combine(Path.GetTempPath(), "Win11IsoBuilder", "cache");

    /// <summary>Where the final .iso is written.</summary>
    public string OutputDirectory { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Desktop");

    public string OutputIsoName { get; set; } = "Win11-Custom.iso";

    // ---- Selections ---------------------------------------------------------
    /// <summary>Chosen Windows edition (index into install.wim/esd).</summary>
    public WindowsEdition? SelectedEdition { get; set; }

    public WinCustomizationOptions Windows { get; set; } = new();
    public OfficeOptions Office { get; set; } = new();

    /// <summary>Catalog picks + user-added installers that will be staged offline.</summary>
    public IList<AppEntry> SelectedApps { get; set; } = new List<AppEntry>();

    // ---- Resolved at build start -------------------------------------------
    public ToolPaths? Tools { get; set; }

    // ---- Derived workspace paths -------------------------------------------
    /// <summary>Extracted ISO tree that gets repacked (the future ISO root).</summary>
    public string MediaDir => Path.Combine(WorkDir, "media");

    /// <summary>DISM WIM mount point.</summary>
    public string MountDir => Path.Combine(WorkDir, "mount");

    /// <summary>Offline payload root inside the media (Office + app installers).</summary>
    public string PayloadDir => Path.Combine(MediaDir, "Payload");

    public string OutputIsoPath => Path.Combine(OutputDirectory, OutputIsoName);
}
