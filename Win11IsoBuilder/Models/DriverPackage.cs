namespace Win11IsoBuilder.Models;

/// <summary>
/// One pinned driver package from Assets/driver-catalog.json. Intel ships storage drivers
/// only as the SetupRST.exe installer (the F6 zip was discontinued), so a package is
/// downloaded, SHA-256 verified, then driver .inf files are extracted from it with the
/// documented "-extractdrivers" switch before DISM injection.
/// </summary>
public sealed class DriverPackage
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    /// <summary>Direct download URL (pinned; verified against <see cref="Sha256"/>).</summary>
    public string SourceUrl { get; set; } = string.Empty;

    /// <summary>File name written into the cache, e.g. SetupRST.exe.</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>SHA-256 of the downloaded file (required — catalog URLs must be immutable).</summary>
    public string Sha256 { get; set; } = string.Empty;

    /// <summary>Human note (supported platforms / generations).</summary>
    public string? Note { get; set; }
}
