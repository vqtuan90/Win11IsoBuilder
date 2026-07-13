using System.Xml.Linq;
using Win11IsoBuilder.Models;
using Win11IsoBuilder.Services;

namespace Win11IsoBuilder.Tests;

public class UnattendBuilderTests
{
    private static readonly XNamespace U = "urn:schemas-microsoft-com:unattend";

    [Fact]
    public void Build_ProducesWellFormedThreePassDocument()
    {
        var doc = new UnattendBuilder().Build(new WinCustomizationOptions());

        var passes = doc.Descendants(U + "settings")
            .Select(s => s.Attribute("pass")?.Value).ToList();
        Assert.Contains("windowsPE", passes);
        Assert.Contains("specialize", passes);
        Assert.Contains("oobeSystem", passes);
    }

    [Fact]
    public void Build_IncludesAllBypassKeys_WhenAllEnabled()
    {
        var doc = new UnattendBuilder().Build(new WinCustomizationOptions());
        var paths = doc.Descendants(U + "Path").Select(p => p.Value).ToList();

        foreach (var key in new[]
                 { "BypassTPMCheck", "BypassSecureBootCheck", "BypassRAMCheck", "BypassStorageCheck", "BypassCPUCheck" })
            Assert.Contains(paths, p => p.Contains(key));
    }

    [Fact]
    public void Build_OmitsDisabledBypassKey()
    {
        var opts = new WinCustomizationOptions { BypassTpm = false };
        var doc = new UnattendBuilder().Build(opts);
        var paths = doc.Descendants(U + "Path").Select(p => p.Value);

        Assert.DoesNotContain(paths, p => p.Contains("BypassTPMCheck"));
    }

    [Fact]
    public void Build_CreatesLocalAdminAccount()
    {
        var opts = new WinCustomizationOptions { LocalUsername = "Tester" };
        var doc = new UnattendBuilder().Build(opts);

        var name = doc.Descendants(U + "LocalAccount")
            .Descendants(U + "Name").FirstOrDefault()?.Value;
        var group = doc.Descendants(U + "Group").FirstOrDefault()?.Value;
        Assert.Equal("Tester", name);
        Assert.Equal("Administrators", group);
    }

    [Fact]
    public void Build_SetsLocaleAndTimeZone()
    {
        var opts = new WinCustomizationOptions { UiLanguage = "vi-VN", TimeZone = "SE Asia Standard Time" };
        var doc = new UnattendBuilder().Build(opts);

        Assert.Contains(doc.Descendants(U + "UILanguage"), e => e.Value == "vi-VN");
        Assert.Contains(doc.Descendants(U + "TimeZone"), e => e.Value == "SE Asia Standard Time");
    }

    [Fact]
    public void Build_ComputerNameIsPlaceholder()
    {
        var doc = new UnattendBuilder().Build(new WinCustomizationOptions());
        Assert.Contains(doc.Descendants(U + "ComputerName"), e => e.Value == "*");
    }

    [Fact]
    public void Build_SpecializePassExplicitlyInvokesSetupComplete()
    {
        // Windows Setup skips its native SetupComplete.cmd auto-run on OEM-keyed editions
        // (retail/prebuilt PCs) — this RunSynchronousCommand is the OEM-key-safe trigger.
        var doc = new UnattendBuilder().Build(new WinCustomizationOptions());
        var specialize = doc.Descendants(U + "settings")
            .First(s => s.Attribute("pass")?.Value == "specialize");

        var path = specialize.Descendants(U + "Path").Select(p => p.Value)
            .FirstOrDefault(p => p.Contains("SetupComplete.cmd"));
        Assert.NotNull(path);
        Assert.Contains(@"%WINDIR%\Setup\Scripts\SetupComplete.cmd", path);
    }
}
