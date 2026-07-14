namespace Win11IsoBuilder.Models;

/// <summary>
/// Storage-driver injection choices. Drivers are added offline (DISM /Add-Driver) into
/// boot.wim — so Windows Setup can see NVMe disks behind Intel VMD on 11th-gen+ platforms —
/// and into install.wim so the installed OS boots without a missing-storage-driver BSOD.
/// </summary>
public sealed class DriverOptions
{
    /// <summary>Download + inject the pinned Intel RST/VMD driver from the driver catalog.</summary>
    public bool IncludeIntelVmd { get; set; } = true;

    /// <summary>Extra user-supplied driver folders (each scanned recursively for .inf files).</summary>
    public List<string> DriverFolders { get; set; } = new();
}
