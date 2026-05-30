using Win11IsoBuilder.Models;
using Win11IsoBuilder.Services;

namespace Win11IsoBuilder.Tests;

public class FirstBootScriptBuilderTests
{
    private readonly FirstBootScriptBuilder _builder = new();

    // ---- NetBIOS sanitize (mirrors set-computername.ps1) --------------------

    [Theory]
    [InlineData("", false, "")]
    [InlineData("   ", false, "")]
    [InlineData("To be filled by O.E.M.", false, "")]
    [InlineData("Default string", false, "")]
    [InlineData("PC-12345", true, "PC-12345")]
    [InlineData("VM_Serial@#01", true, "VMSerial01")]
    public void TrySanitizeNetbiosName_HandlesEdgeCases(string raw, bool expectedOk, string expectedName)
    {
        var ok = FirstBootScriptBuilder.TrySanitizeNetbiosName(raw, out var name);
        Assert.Equal(expectedOk, ok);
        if (expectedOk) Assert.Equal(expectedName, name);
    }

    [Fact]
    public void TrySanitizeNetbiosName_TruncatesTo15Chars()
    {
        var ok = FirstBootScriptBuilder.TrySanitizeNetbiosName("ABCDEFGHIJKLMNOPQRSTUV", out var name);
        Assert.True(ok);
        Assert.Equal(15, name.Length);
        Assert.Equal("ABCDEFGHIJKLMNO", name);
    }

    // ---- App install lines --------------------------------------------------

    [Fact]
    public void BuildAppsBlock_MsiUsesMsiexec()
    {
        var cmds = new List<AppInstallCommand>
        {
            new("chrome", @"Apps\chrome\chrome.msi", "/qn /norestart", IsMsi: true),
        };
        var block = _builder.BuildAppsBlock(cmds);

        Assert.Contains("msiexec /i \"%PD%\\Apps\\chrome\\chrome.msi\" /qn /norestart", block);
        Assert.Contains("start /wait", block);
    }

    [Fact]
    public void BuildAppsBlock_ExeRunsDirectly()
    {
        var cmds = new List<AppInstallCommand>
        {
            new("7zip", @"Apps\7zip\7zip.exe", "/S", IsMsi: false),
        };
        var block = _builder.BuildAppsBlock(cmds);

        Assert.Contains("start /wait \"\" \"%PD%\\Apps\\7zip\\7zip.exe\" /S", block);
        Assert.DoesNotContain("msiexec", block);
    }

    [Fact]
    public void BuildAppsBlock_EmptyList_EmitsRem()
    {
        Assert.Contains("rem No apps", _builder.BuildAppsBlock(new List<AppInstallCommand>()));
    }

    // ---- Template token replacement ----------------------------------------

    [Fact]
    public void BuildSetupCompleteCmd_ReplacesBlocks()
    {
        const string template = "head\n{{OFFICE_BLOCK}}\n{{APPS_BLOCK}}\ntail";
        var cmds = new List<AppInstallCommand> { new("vlc", @"Apps\vlc\vlc.exe", "/S", false) };

        var result = _builder.BuildSetupCompleteCmd(template, officeEnabled: true, cmds);

        Assert.DoesNotContain("{{OFFICE_BLOCK}}", result);
        Assert.DoesNotContain("{{APPS_BLOCK}}", result);
        Assert.Contains("setup.exe", result);   // office block present
        Assert.Contains("vlc", result);          // app block present
    }

    [Fact]
    public void BuildComputerNameScript_InjectsModeAndSanitizedFixedName()
    {
        const string template = "$mode = '{{MODE}}'; $fixed = '{{FIXEDNAME}}'";
        var opts = new WinCustomizationOptions
        {
            ComputerNameMode = ComputerNameMode.Fixed,
            FixedComputerName = "Office_PC#1",
        };

        var ps1 = _builder.BuildComputerNameScript(template, opts);

        Assert.Contains("$mode = 'Fixed'", ps1);
        Assert.Contains("$fixed = 'OfficePC1'", ps1);
    }
}
