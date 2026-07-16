using System.IO;
using System.Xml.Linq;
using Win11IsoBuilder.Models;

namespace Win11IsoBuilder.Services;

/// <summary>
/// Generates a well-formed autounattend.xml from <see cref="WinCustomizationOptions"/>.
/// Built with XDocument (not string templating) so namespaces and password escaping are
/// always valid. By default the install is MDT-style zero-touch: a WinPE script wipes and
/// partitions Disk 0 (GPT on UEFI, MBR on BIOS) and ImageInstall picks the edition, so Setup
/// never prompts. With <see cref="WinCustomizationOptions.AutoPartition"/> off, partitioning
/// is omitted → Setup shows the drive picker. Computer name is "*" here, set at first boot.
/// </summary>
public sealed class UnattendBuilder
{
    private const string PartitionScriptName = "auto-partition.cmd";
    private static readonly XNamespace U = "urn:schemas-microsoft-com:unattend";
    private static readonly XNamespace Wcm = "http://schemas.microsoft.com/WMIConfig/2002/State";

    private readonly ILogSink? _log;

    public UnattendBuilder(ILogSink? log = null) => _log = log;

    /// <summary>Build the document in memory (used by tests and the pipeline).</summary>
    /// <param name="imageIndex">install.wim index Setup should apply (post-ESD-export layout).</param>
    public XDocument Build(WinCustomizationOptions o, int imageIndex = 1)
    {
        var unattend = new XElement(U + "unattend",
            new XAttribute(XNamespace.Xmlns + "wcm", Wcm.NamespaceName),
            WindowsPePass(o, imageIndex),
            SpecializePass(),
            OobePass(o));
        return new XDocument(new XDeclaration("1.0", "utf-8", null), unattend);
    }

    /// <summary>
    /// Build and write to <c>media\autounattend.xml</c> (ISO root). Zero-touch mode also
    /// stages the disk-preparation script next to it so WinPE can find it on the media.
    /// </summary>
    public string Write(WinCustomizationOptions o, string mediaDir, int imageIndex = 1)
    {
        var path = Path.Combine(mediaDir, "autounattend.xml");
        Build(o, imageIndex).Save(path);
        _log?.Info($"Wrote autounattend.xml → {path}");

        if (o.AutoPartition)
        {
            var asset = Path.Combine(AppContext.BaseDirectory, "Assets", PartitionScriptName);
            File.Copy(asset, Path.Combine(mediaDir, PartitionScriptName), overwrite: true);
            _log?.Info($"Staged {PartitionScriptName} → media root (zero-touch install).");
        }
        return path;
    }

    private XElement WindowsPePass(WinCustomizationOptions o, int imageIndex)
    {
        var setupContent = new List<object> { new XElement(U + "RunSynchronous", RunSyncCommands(o)) };
        if (o.AutoPartition)
        {
            setupContent.Add(ImageInstall(imageIndex));
            setupContent.Add(UserData());
        }
        var setup = Component("Microsoft-Windows-Setup", setupContent.ToArray());

        var intl = Component("Microsoft-Windows-International-Core-WinPE",
            new XElement(U + "SetupUILanguage", new XElement(U + "UILanguage", o.UiLanguage)),
            new XElement(U + "InputLocale", o.InputLocale),
            new XElement(U + "SystemLocale", o.UiLanguage),
            new XElement(U + "UILanguage", o.UiLanguage),
            new XElement(U + "UserLocale", o.UiLanguage));

        return Pass("windowsPE", setup, intl);
    }

    private XElement SpecializePass() =>
        Pass("specialize",
            Component("Microsoft-Windows-Shell-Setup",
                new XElement(U + "ComputerName", "*")), // real name set at first boot
            Component("Microsoft-Windows-Deployment",
                new XElement(U + "RunSynchronous", RunSetupCompleteCommand())));

    /// <summary>
    /// Windows Setup only auto-runs %WINDIR%\Setup\Scripts\SetupComplete.cmd on Enterprise/Server
    /// editions; it silently skips it whenever a firmware-embedded OEM product key is present
    /// (the case on almost every retail/prebuilt PC) — Office/app install then never happens, with
    /// no error anywhere. Explicitly invoking the same script from an unattend RunSynchronousCommand
    /// is unaffected by that OEM-key gate (Microsoft's own guidance), so this is the reliable trigger;
    /// the native auto-run becomes a harmless no-op duplicate guarded by a marker file in the script.
    /// </summary>
    private static XElement RunSetupCompleteCommand() =>
        new XElement(U + "RunSynchronousCommand",
            new XAttribute(Wcm + "action", "add"),
            new XElement(U + "Order", 1),
            new XElement(U + "Path", @"cmd.exe /c ""%WINDIR%\Setup\Scripts\SetupComplete.cmd"""));

    private XElement OobePass(WinCustomizationOptions o)
    {
        var intl = Component("Microsoft-Windows-International-Core",
            new XElement(U + "InputLocale", o.InputLocale),
            new XElement(U + "SystemLocale", o.UiLanguage),
            new XElement(U + "UILanguage", o.UiLanguage),
            new XElement(U + "UserLocale", o.UiLanguage));

        var shell = Component("Microsoft-Windows-Shell-Setup",
            new XElement(U + "OOBE",
                new XElement(U + "HideEULAPage", "true"),
                new XElement(U + "HideOnlineAccountScreens", "true"),
                new XElement(U + "HideWirelessSetupInOOBE", "true"),
                new XElement(U + "ProtectYourPC", "3")),
            new XElement(U + "TimeZone", o.TimeZone),
            LocalAccount(o),
            AutoLogon(o));

        return Pass("oobeSystem", intl, shell);
    }

    private XElement LocalAccount(WinCustomizationOptions o) =>
        new(U + "UserAccounts",
            new XElement(U + "LocalAccounts",
                new XElement(U + "LocalAccount",
                    new XAttribute(Wcm + "action", "add"),
                    new XElement(U + "Name", o.LocalUsername),
                    new XElement(U + "DisplayName", o.LocalUsername),
                    new XElement(U + "Group", "Administrators"),
                    new XElement(U + "Password",
                        new XElement(U + "Value", o.LocalPassword),
                        new XElement(U + "PlainText", "true")))));

    /// <summary>
    /// First boot lands straight on the desktop (MDT-style) instead of the login screen.
    /// One logon only — later boots ask for credentials as usual. App install is NOT tied
    /// to this: SetupComplete.cmd runs as SYSTEM before any logon (PRD decision #6 stands).
    /// </summary>
    private XElement AutoLogon(WinCustomizationOptions o) =>
        new(U + "AutoLogon",
            new XElement(U + "Enabled", "true"),
            new XElement(U + "Username", o.LocalUsername),
            new XElement(U + "Password",
                new XElement(U + "Value", o.LocalPassword),
                new XElement(U + "PlainText", "true")),
            new XElement(U + "LogonCount", 1));

    private IEnumerable<XElement> RunSyncCommands(WinCustomizationOptions o)
    {
        var keys = new List<string>();
        if (o.BypassTpm) keys.Add("BypassTPMCheck");
        if (o.BypassSecureBoot) keys.Add("BypassSecureBootCheck");
        if (o.BypassRam) keys.Add("BypassRAMCheck");
        if (o.BypassStorage) keys.Add("BypassStorageCheck");
        if (o.BypassCpu) keys.Add("BypassCPUCheck");

        var order = 1;
        foreach (var key in keys)
        {
            yield return RunSync(order++,
                $@"reg add HKLM\System\Setup\LabConfig /v {key} /t REG_DWORD /d 1 /f");
        }

        // WinPE assigns the install media a letter we cannot know up-front, so scan for the
        // staged script; exit /b stops after the first hit so a stray copy on another volume
        // cannot run the wipe twice. RunSynchronous executes before Setup's disk/ImageInstall
        // handling, which is exactly when the disk must be wiped and partitioned.
        if (o.AutoPartition)
        {
            yield return RunSync(order++,
                @"cmd.exe /c ""for %d in (C D E F G H I J K L M N O P Q R S T U V W X Y Z) do " +
                $@"@if exist %d:\{PartitionScriptName} (call %d:\{PartitionScriptName} & exit /b)""");
        }
    }

    private static XElement RunSync(int order, string path) =>
        new(U + "RunSynchronousCommand",
            new XAttribute(Wcm + "action", "add"),
            new XElement(U + "Order", order),
            new XElement(U + "Path", path));

    /// <summary>Apply the chosen edition to the partition the script just created — no prompts.</summary>
    private static XElement ImageInstall(int imageIndex) =>
        new(U + "ImageInstall",
            new XElement(U + "OSImage",
                new XElement(U + "InstallFrom",
                    new XElement(U + "MetaData",
                        new XAttribute(Wcm + "action", "add"),
                        new XElement(U + "Key", "/IMAGE/INDEX"),
                        new XElement(U + "Value", imageIndex))),
                // EFI/MSR are too small to qualify, so "first available" is the OS partition
                // on both GPT and MBR layouts (whose partition ids differ — hence no InstallTo).
                new XElement(U + "InstallToAvailablePartition", "true"),
                new XElement(U + "WillShowUI", "OnError")));

    /// <summary>Accept the EULA and suppress the product-key prompt for full zero-touch.</summary>
    private static XElement UserData() =>
        new(U + "UserData",
            new XElement(U + "AcceptEula", "true"),
            new XElement(U + "ProductKey",
                new XElement(U + "WillShowUI", "OnError")));

    private static XElement Pass(string passName, params object[] components) =>
        new(U + "settings", new XAttribute("pass", passName), components);

    private static XElement Component(string name, params object[] content) =>
        new(U + "component",
            new XAttribute("name", name),
            new XAttribute("processorArchitecture", "amd64"),
            new XAttribute("publicKeyToken", "31bf3856ad364e35"),
            new XAttribute("language", "neutral"),
            new XAttribute("versionScope", "nonSxS"),
            content);
}
