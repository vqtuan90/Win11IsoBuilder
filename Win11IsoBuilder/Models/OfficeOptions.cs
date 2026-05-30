namespace Win11IsoBuilder.Models;

/// <summary>
/// Office Deployment Tool (ODT) settings. Defaults match the locked decision:
/// Microsoft 365 Apps for business, en-us, 64-bit, Monthly Enterprise channel.
/// Activation is left to the user (sign in) — no MAK/KMS is ever embedded.
/// </summary>
public sealed class OfficeOptions
{
    /// <summary>When false, Office is skipped entirely.</summary>
    public bool Enabled { get; set; } = true;

    public string ProductId { get; set; } = "O365BusinessRetail";
    public string Channel { get; set; } = "MonthlyEnterprise";

    /// <summary>32 or 64.</summary>
    public int Bitness { get; set; } = 64;

    /// <summary>ODT language ids, e.g. "en-us".</summary>
    public List<string> Languages { get; set; } = new() { "en-us" };

    /// <summary>Apps to exclude, e.g. "Access", "Publisher", "Lync", "Groove", "OneDrive".</summary>
    public List<string> ExcludedApps { get; set; } = new();
}
