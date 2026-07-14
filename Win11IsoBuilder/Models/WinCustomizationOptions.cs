namespace Win11IsoBuilder.Models;

/// <summary>How the post-install computer name is decided (applied at first boot).</summary>
public enum ComputerNameMode
{
    /// <summary>Use the machine BIOS serial number (sanitized).</summary>
    Serial,
    /// <summary>Use a fixed name the user typed.</summary>
    Fixed,
    /// <summary>Generate WIN-xxxxxx.</summary>
    Random,
}

/// <summary>
/// All Windows-side tweaks that flow into autounattend.xml (and the first-boot
/// computer-name step). Defaults match the locked decisions: bypass everything,
/// en-US, SE Asia time zone, US keyboard, name-from-serial.
/// </summary>
public sealed class WinCustomizationOptions
{
    // Hardware requirement bypasses (LabConfig registry keys, windowsPE pass).
    public bool BypassTpm { get; set; } = true;
    public bool BypassSecureBoot { get; set; } = true;
    public bool BypassRam { get; set; } = true;
    public bool BypassStorage { get; set; } = true;
    public bool BypassCpu { get; set; } = true;

    // Local administrator account created during OOBE (no Microsoft account).
    public string LocalUsername { get; set; } = "Admin";
    public string LocalPassword { get; set; } = string.Empty;

    // Regional settings.
    public string UiLanguage { get; set; } = "en-US";
    public string TimeZone { get; set; } = "SE Asia Standard Time";
    /// <summary>Input locale / keyboard layout id (00000409 = US).</summary>
    public string InputLocale { get; set; } = "0409:00000409";

    /// <summary>
    /// MDT-style zero-touch install (default): Setup wipes Disk 0, auto-partitions
    /// (GPT on UEFI, MBR on legacy BIOS) and installs without any prompt. Turn off to
    /// get the interactive drive picker instead. Supersedes the original "always show
    /// the drive picker" decision (user decision, 2026-07-14).
    /// </summary>
    public bool AutoPartition { get; set; } = true;

    // Computer name.
    public ComputerNameMode ComputerNameMode { get; set; } = ComputerNameMode.Serial;
    public string FixedComputerName { get; set; } = string.Empty;

    /// <summary>PackageNames of provisioned Appx (bloatware) the user chose to remove.</summary>
    public List<string> AppxToRemove { get; set; } = new();

    public bool AnyBypassEnabled =>
        BypassTpm || BypassSecureBoot || BypassRam || BypassStorage || BypassCpu;
}
