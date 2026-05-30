using System.IO;
using System.Text.Json.Serialization;

namespace Win11IsoBuilder.Models;

/// <summary>
/// One installable app — either a built-in catalog entry or a user-added installer.
/// Acquired into the cache then staged into the offline payload.
/// </summary>
public sealed class AppEntry
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;

    /// <summary>Direct download URL for the installer (catalog apps). Empty → must be user-supplied.</summary>
    public string? SourceUrl { get; set; }

    /// <summary>Local installer path (user-added apps or pre-cached files).</summary>
    public string? LocalPath { get; set; }

    /// <summary>Installer file name written into the cache/payload, e.g. chrome.msi.</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>Silent-install arguments (exe) or msiexec args (msi).</summary>
    public string SilentArgs { get; set; } = string.Empty;

    /// <summary>Optional SHA-256 to verify a downloaded installer.</summary>
    public string? Sha256 { get; set; }

    /// <summary>Human note (e.g. why a catalog entry needs a manual installer).</summary>
    public string? Note { get; set; }

    [JsonIgnore] public bool IsUserAdded { get; set; }
    [JsonIgnore] public bool IsSelected { get; set; }

    /// <summary>.msi installers run through msiexec; everything else runs directly.</summary>
    [JsonIgnore]
    public bool IsMsi => FileName.EndsWith(".msi", StringComparison.OrdinalIgnoreCase);

    /// <summary>True when there is something to acquire (URL or local file).</summary>
    [JsonIgnore]
    public bool IsAcquirable =>
        !string.IsNullOrWhiteSpace(SourceUrl) ||
        (!string.IsNullOrWhiteSpace(LocalPath) && File.Exists(LocalPath));
}
