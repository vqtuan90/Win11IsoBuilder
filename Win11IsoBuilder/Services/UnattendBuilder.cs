using System.IO;
using System.Xml.Linq;
using Win11IsoBuilder.Models;

namespace Win11IsoBuilder.Services;

/// <summary>
/// Generates a well-formed autounattend.xml from <see cref="WinCustomizationOptions"/>.
/// Built with XDocument (not string templating) so namespaces and password escaping are
/// always valid. Disk partitioning is intentionally omitted → Setup shows the drive picker
/// (safe; avoids wiping the wrong disk). Computer name is "*" here and set for real at first boot.
/// </summary>
public sealed class UnattendBuilder
{
    private static readonly XNamespace U = "urn:schemas-microsoft-com:unattend";
    private static readonly XNamespace Wcm = "http://schemas.microsoft.com/WMIConfig/2002/State";

    private readonly ILogSink? _log;

    public UnattendBuilder(ILogSink? log = null) => _log = log;

    /// <summary>Build the document in memory (used by tests and the pipeline).</summary>
    public XDocument Build(WinCustomizationOptions o)
    {
        var unattend = new XElement(U + "unattend",
            new XAttribute(XNamespace.Xmlns + "wcm", Wcm.NamespaceName),
            WindowsPePass(o),
            SpecializePass(),
            OobePass(o));
        return new XDocument(new XDeclaration("1.0", "utf-8", null), unattend);
    }

    /// <summary>Build and write to <c>media\autounattend.xml</c> (ISO root).</summary>
    public string Write(WinCustomizationOptions o, string mediaDir)
    {
        var path = Path.Combine(mediaDir, "autounattend.xml");
        Build(o).Save(path);
        _log?.Info($"Wrote autounattend.xml → {path}");
        return path;
    }

    private XElement WindowsPePass(WinCustomizationOptions o)
    {
        var setup = Component("Microsoft-Windows-Setup",
            new XElement(U + "RunSynchronous", BypassCommands(o)));

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
                new XElement(U + "ComputerName", "*"))); // real name set at first boot

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
            LocalAccount(o));

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

    private IEnumerable<XElement> BypassCommands(WinCustomizationOptions o)
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
            yield return new XElement(U + "RunSynchronousCommand",
                new XAttribute(Wcm + "action", "add"),
                new XElement(U + "Order", order++),
                new XElement(U + "Path",
                    $@"reg add HKLM\System\Setup\LabConfig /v {key} /t REG_DWORD /d 1 /f"));
        }
    }

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
