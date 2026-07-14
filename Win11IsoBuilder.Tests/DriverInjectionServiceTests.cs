using System.IO;
using Win11IsoBuilder.Models;
using Win11IsoBuilder.Services;

namespace Win11IsoBuilder.Tests;

public class DriverInjectionServiceTests
{
    [Fact]
    public void LoadCatalog_ReadsPinnedPackages()
    {
        var json = """
            [
              { "id": "intel-rst-vmd-19", "name": "Intel RST VMD 19.5",
                "sourceUrl": "https://example/SetupRST.exe", "fileName": "SetupRST.exe",
                "sha256": "8AF488ECD76258A0A539ADA28098A2961472D670572900AED216F48C712BC45B" }
            ]
            """;
        var svc = NewService(WriteTemp("driver-catalog.json", json));

        var pkgs = svc.LoadCatalog();

        Assert.Single(pkgs);
        Assert.Equal("intel-rst-vmd-19", pkgs[0].Id);
        Assert.NotEmpty(pkgs[0].Sha256);
        Assert.StartsWith("https://", pkgs[0].SourceUrl);
    }

    [Fact]
    public void LoadCatalog_MissingFile_ReturnsEmpty()
    {
        var svc = NewService(Path.Combine(Path.GetTempPath(), "no-driver-catalog.json"));
        Assert.Empty(svc.LoadCatalog());
    }

    [Fact]
    public void BundledCatalog_HasPinnedIntelVmdEntry()
    {
        // The real shipped catalog must stay pinned: URL + SHA-256 both present.
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "driver-catalog.json");
        var svc = NewService(path);

        var pkgs = svc.LoadCatalog();

        var vmd = Assert.Single(pkgs, p => p.Id.StartsWith("intel-rst-vmd"));
        Assert.Matches("^[0-9A-Fa-f]{64}$", vmd.Sha256);
        Assert.Contains("downloadmirror.intel.com", vmd.SourceUrl);
        Assert.Equal("SetupRST.exe", vmd.FileName);
    }

    [Fact]
    public async Task ResolveDriverFolders_SkipsFolderWithoutInf()
    {
        var empty = NewTempDir();
        var options = new DriverOptions { IncludeIntelVmd = false, DriverFolders = { empty } };

        var folders = await NewService().ResolveDriverFoldersAsync(options, NewTempDir());

        Assert.Empty(folders);
    }

    [Fact]
    public async Task ResolveDriverFolders_AcceptsFolderWithNestedInf()
    {
        var root = NewTempDir();
        var nested = Path.Combine(root, "VMD", "f6vmdflpy-x64");
        Directory.CreateDirectory(nested);
        File.WriteAllText(Path.Combine(nested, "iaStorVD.inf"), "; driver");
        var options = new DriverOptions { IncludeIntelVmd = false, DriverFolders = { root } };

        var folders = await NewService().ResolveDriverFoldersAsync(options, NewTempDir());

        Assert.Equal(new[] { root }, folders);
    }

    [Fact]
    public async Task ResolveDriverFolders_MissingFolder_IsSkippedNotFatal()
    {
        var options = new DriverOptions
        {
            IncludeIntelVmd = false,
            DriverFolders = { Path.Combine(Path.GetTempPath(), "ghost-drivers") },
        };

        var folders = await NewService().ResolveDriverFoldersAsync(options, NewTempDir());

        Assert.Empty(folders);
    }

    [Fact]
    public async Task ResolveDriverFolders_VmdDisabled_SkipsCatalog()
    {
        // Catalog has an entry with an unreachable URL; disabled VMD must never touch it.
        var json = """[{ "id": "x", "name": "x", "sourceUrl": "https://localhost:1/x.exe", "fileName": "x.exe", "sha256": "00" }]""";
        var svc = NewService(WriteTemp("driver-catalog.json", json));

        var folders = await svc.ResolveDriverFoldersAsync(
            new DriverOptions { IncludeIntelVmd = false }, NewTempDir());

        Assert.Empty(folders);
    }

    [Fact]
    public async Task ResolveDriverFolders_MalformedCatalog_WarnsAndContinues()
    {
        // Broken catalog JSON must never fail the build — user folders still resolve.
        var userDir = NewTempDir();
        File.WriteAllText(Path.Combine(userDir, "drv.inf"), ";");
        var svc = NewService(WriteTemp("driver-catalog.json", "{ not valid json ["));
        var options = new DriverOptions { IncludeIntelVmd = true, DriverFolders = { userDir } };

        var folders = await svc.ResolveDriverFoldersAsync(options, NewTempDir());

        Assert.Equal(new[] { userDir }, folders);
    }

    [Fact]
    public async Task AcquireAndExtract_RefusesUnpinnedPackage()
    {
        // The package is executed on the build machine — an empty SHA-256 pin must be rejected
        // before any download happens.
        var pkg = new DriverPackage
        {
            Id = "x", Name = "x", SourceUrl = "https://localhost:1/x.exe",
            FileName = "x.exe", Sha256 = "",
        };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => NewService().AcquireAndExtractAsync(pkg, NewTempDir()));
    }

    [Fact]
    public void ContainsInf_FalseForMissingOrEmpty_TrueForNested()
    {
        Assert.False(DriverInjectionService.ContainsInf(Path.Combine(Path.GetTempPath(), "ghost")));
        var dir = NewTempDir();
        Assert.False(DriverInjectionService.ContainsInf(dir));
        var sub = Path.Combine(dir, "a", "b");
        Directory.CreateDirectory(sub);
        File.WriteAllText(Path.Combine(sub, "drv.inf"), ";");
        Assert.True(DriverInjectionService.ContainsInf(dir));
    }

    private static DriverInjectionService NewService(string? catalogPath = null) =>
        new(new ProcessRunner(new NoOpLog()), new NoOpLog(), catalogPath);

    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "w11test", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string WriteTemp(string name, string content)
    {
        var path = Path.Combine(NewTempDir(), name);
        File.WriteAllText(path, content);
        return path;
    }
}
