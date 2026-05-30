namespace Win11IsoBuilder.Models;

/// <summary>
/// Resolved locations of the external tools the build pipeline depends on.
/// Produced by ToolDetectionService; consumed by Iso/Wim/Office services.
/// </summary>
public sealed class ToolPaths
{
    /// <summary>Full path to dism.exe (always present on Windows: %WINDIR%\System32).</summary>
    public string? DismPath { get; set; }

    /// <summary>Full path to oscdimg.exe (bundled copy preferred, ADK as fallback).</summary>
    public string? OscdimgPath { get; set; }

    /// <summary>Where oscdimg was found: bundled tools dir, ADK, or none.</summary>
    public OscdimgSource OscdimgSource { get; set; } = OscdimgSource.None;

    public bool HasDism => !string.IsNullOrEmpty(DismPath);
    public bool HasOscdimg => !string.IsNullOrEmpty(OscdimgPath);

    /// <summary>True when oscdimg could not be located anywhere → block build, guide user.</summary>
    public bool IsAdkMissing => !HasOscdimg;
}

public enum OscdimgSource { None, Bundled, Adk }
