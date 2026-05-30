namespace Win11IsoBuilder.Models;

/// <summary>
/// One selectable Windows image inside install.wim / install.esd
/// (as reported by <c>dism /Get-ImageInfo</c>).
/// </summary>
public sealed class WindowsEdition
{
    /// <summary>1-based image index used by DISM operations.</summary>
    public int Index { get; set; }

    /// <summary>Image name, e.g. "Windows 11 Pro".</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Edition id, e.g. "Professional".</summary>
    public string? Edition { get; set; }

    /// <summary>Architecture string if reported, e.g. "x64".</summary>
    public string? Architecture { get; set; }

    /// <summary>Image size in bytes if reported.</summary>
    public long SizeBytes { get; set; }

    public override string ToString() =>
        string.IsNullOrEmpty(Edition) ? $"{Index}: {Name}" : $"{Index}: {Name} ({Edition})";
}
