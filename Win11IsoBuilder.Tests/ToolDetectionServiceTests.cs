using System.IO;
using Win11IsoBuilder.Models;
using Win11IsoBuilder.Services;

namespace Win11IsoBuilder.Tests;

public class ToolDetectionServiceTests
{
    [Fact]
    public void Detect_FindsBundledOscdimg_WhenPresent()
    {
        var baseDir = NewTempDir();
        var oscdimgDir = Path.Combine(baseDir, "tools", "oscdimg");
        Directory.CreateDirectory(oscdimgDir);
        File.WriteAllText(Path.Combine(oscdimgDir, "oscdimg.exe"), "stub");

        var paths = new ToolDetectionService(new NoOpLog(), baseDir).Detect();

        Assert.True(paths.HasOscdimg);
        Assert.False(paths.IsAdkMissing);
        Assert.Equal(OscdimgSource.Bundled, paths.OscdimgSource);
    }

    [Fact]
    public void Detect_FlagsAdkMissing_WhenNoBundleAndNoAdk()
    {
        // Empty base dir → no bundled oscdimg. (CI machines lack the ADK.)
        var paths = new ToolDetectionService(new NoOpLog(), NewTempDir()).Detect();

        Assert.True(paths.IsAdkMissing);
        Assert.False(paths.HasOscdimg);
    }

    [Fact]
    public void Detect_ResolvesDism()
    {
        // DISM is always present on Windows under System32.
        var paths = new ToolDetectionService(new NoOpLog(), NewTempDir()).Detect();
        Assert.True(paths.HasDism);
    }

    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "w11tools", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
