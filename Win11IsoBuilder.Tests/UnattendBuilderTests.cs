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

    [Fact]
    public void Build_ZeroTouchDefault_HasImageInstallEulaAndPartitionCommand()
    {
        var doc = new UnattendBuilder().Build(new WinCustomizationOptions(), imageIndex: 3);
        var pe = doc.Descendants(U + "settings").First(s => s.Attribute("pass")?.Value == "windowsPE");

        // Edition applied without prompting, to the partition the script creates.
        var metaValue = pe.Descendants(U + "MetaData").Descendants(U + "Value").Single().Value;
        Assert.Equal("3", metaValue);
        Assert.Equal("true", pe.Descendants(U + "InstallToAvailablePartition").Single().Value);

        // EULA + product-key prompts suppressed.
        Assert.Equal("true", pe.Descendants(U + "AcceptEula").Single().Value);
        Assert.Contains(pe.Descendants(U + "ProductKey").Descendants(U + "WillShowUI"),
            e => e.Value == "OnError");

        // WinPE runs the staged disk-preparation script found on the media. Assert the full
        // command so a quoting regression cannot slip past a substring check: it must scan
        // the drive letters and stop after the first hit (exit /b).
        var cmd = pe.Descendants(U + "Path").Select(p => p.Value)
            .Single(p => p.Contains("auto-partition.cmd"));
        Assert.Equal(
            @"cmd.exe /c ""for %d in (C D E F G H I J K L M N O P Q R S T U V W X Y Z) do " +
            @"@if exist %d:\auto-partition.cmd (call %d:\auto-partition.cmd & exit /b)""",
            cmd);
    }

    [Fact]
    public void Build_ZeroTouch_RunSynchronousOrdersAreUniqueAndSequential()
    {
        var doc = new UnattendBuilder().Build(new WinCustomizationOptions());
        var pe = doc.Descendants(U + "settings").First(s => s.Attribute("pass")?.Value == "windowsPE");

        var orders = pe.Descendants(U + "RunSynchronousCommand")
            .Select(c => int.Parse(c.Element(U + "Order")!.Value)).ToList();
        Assert.Equal(orders.OrderBy(x => x).ToList(), orders);
        Assert.Equal(orders.Count, orders.Distinct().Count());
        Assert.Equal(1, orders.First());
    }

    [Fact]
    public void Build_AutoPartitionOff_RestoresDrivePickerBehavior()
    {
        var doc = new UnattendBuilder().Build(new WinCustomizationOptions { AutoPartition = false });

        Assert.Empty(doc.Descendants(U + "ImageInstall"));
        Assert.Empty(doc.Descendants(U + "UserData"));
        Assert.DoesNotContain(doc.Descendants(U + "Path"), p => p.Value.Contains("auto-partition.cmd"));
    }

    [Fact]
    public void Write_StagesPartitionScriptOnlyWhenZeroTouch()
    {
        var onDir = NewTempDir();
        var offDir = NewTempDir();

        new UnattendBuilder().Write(new WinCustomizationOptions(), onDir);
        new UnattendBuilder().Write(new WinCustomizationOptions { AutoPartition = false }, offDir);

        Assert.True(File.Exists(Path.Combine(onDir, "autounattend.xml")));
        Assert.True(File.Exists(Path.Combine(onDir, "auto-partition.cmd")));
        Assert.True(File.Exists(Path.Combine(offDir, "autounattend.xml")));
        Assert.False(File.Exists(Path.Combine(offDir, "auto-partition.cmd")));
    }

    [Fact]
    public void PartitionScriptAsset_CoversBothFirmwareLayouts()
    {
        var script = File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Assets", "auto-partition.cmd"));

        Assert.Contains("PEFirmwareType", script);
        Assert.Contains("convert gpt", script);
        Assert.Contains("create partition efi size=300", script);
        Assert.Contains("create partition msr size=128", script);
        Assert.Contains("active", script); // MBR branch marks System Reserved bootable
        Assert.Contains("diskpart /s", script);
        Assert.Contains("\r\n", script);   // batch parsing is only reliable with CRLF
        // Unknown firmware must leave the disk untouched (no wrong-layout wipe).
        Assert.Contains(@"else if ""%FW%""==""0x1""", script);
        Assert.Contains("exit /b 1", script);
    }

    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "w11test", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
