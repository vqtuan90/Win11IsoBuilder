using System.IO;
using Win11IsoBuilder.Services;

namespace Win11IsoBuilder.Tests;

public class LegacySetupPatcherTests
{
    [Theory]
    [InlineData(22621, false)] // Win11 22H2 — legacy Setup, must stay untouched
    [InlineData(22631, false)] // Win11 23H2
    [InlineData(26100, true)]  // Win11 24H2 — first ConX build
    [InlineData(26200, true)]  // Win11 25H2
    public void IsConXBuild_ThresholdIs24H2(int build, bool expected) =>
        Assert.Equal(expected, LegacySetupPatcher.IsConXBuild(build));

    [Fact]
    public void WinpeshlIni_RunsWpeinitThenRootLegacySetupWithCrlf()
    {
        var ini = LegacySetupPatcher.WinpeshlIni;
        Assert.StartsWith("[LaunchApps]\r\n", ini);
        // wpeinit must run first (startnet.cmd no longer runs once winpeshl.ini exists).
        Assert.Contains(@"\Windows\System32\wpeinit.exe", ini);
        // Must target the media-root launcher stub, NOT sources\setup.exe (the engine).
        Assert.Contains("%SYSTEMDRIVE%\\setup.exe, /legacy", ini);
        Assert.DoesNotContain(@"\sources\setup.exe", ini);
        // wpeinit line must precede the setup line.
        Assert.True(ini.IndexOf("wpeinit", StringComparison.Ordinal)
                    < ini.IndexOf("setup.exe, /legacy", StringComparison.Ordinal));
        Assert.EndsWith("\r\n", ini);
    }

    [Fact]
    public void TryPatchMountedImage_PatchesOnlyTheSetupImage()
    {
        // Plain WinPE image (no sources\setup.exe) → untouched.
        var winpe = NewTempDir();
        Directory.CreateDirectory(Path.Combine(winpe, "Windows", "System32"));
        Assert.False(LegacySetupPatcher.TryPatchMountedImage(winpe));
        Assert.False(File.Exists(Path.Combine(winpe, "Windows", "System32", "winpeshl.ini")));

        // Setup image → winpeshl.ini written with the legacy launch line.
        var setup = NewTempDir();
        Directory.CreateDirectory(Path.Combine(setup, "sources"));
        Directory.CreateDirectory(Path.Combine(setup, "Windows", "System32"));
        File.WriteAllText(Path.Combine(setup, "sources", "setup.exe"), "stub");

        Assert.True(LegacySetupPatcher.TryPatchMountedImage(setup));
        var ini = File.ReadAllText(Path.Combine(setup, "Windows", "System32", "winpeshl.ini"));
        Assert.Equal(LegacySetupPatcher.WinpeshlIni, ini);
    }

    [Fact]
    public void IsConXMedia_FalseWhenSetupExeMissing() =>
        Assert.False(LegacySetupPatcher.IsConXMedia(NewTempDir()));

    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "w11test", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
