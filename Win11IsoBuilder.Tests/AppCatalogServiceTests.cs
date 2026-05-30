using System.IO;
using Win11IsoBuilder.Services;

namespace Win11IsoBuilder.Tests;

public class AppCatalogServiceTests
{
    private readonly AppCatalogService _svc = new(new NoOpLog());

    [Fact]
    public void LoadCatalog_ReadsEntriesFromJson()
    {
        var json = """
            [
              { "id": "chrome", "name": "Chrome", "category": "Browser",
                "sourceUrl": "https://example/chrome.msi", "fileName": "chrome.msi", "silentArgs": "/qn" }
            ]
            """;
        var path = WriteTemp("catalog.json", json);
        var svc = new AppCatalogService(new NoOpLog(), path);

        var apps = svc.LoadCatalog();

        Assert.Single(apps);
        Assert.Equal("chrome", apps[0].Id);
        Assert.True(apps[0].IsMsi);
        Assert.True(apps[0].IsAcquirable); // has source URL
    }

    [Fact]
    public void LoadCatalog_MissingFile_ReturnsEmpty()
    {
        var svc = new AppCatalogService(new NoOpLog(), Path.Combine(Path.GetTempPath(), "nope-xyz.json"));
        Assert.Empty(svc.LoadCatalog());
    }

    [Fact]
    public void AddUserApp_AcceptsExeAndDefaultsSilentArgs()
    {
        var exe = WriteTemp("tool.exe", "stub");

        var entry = _svc.AddUserApp(exe);

        Assert.True(entry.IsUserAdded);
        Assert.True(entry.IsSelected);
        Assert.Equal("/S", entry.SilentArgs);
        Assert.Equal("tool.exe", entry.FileName);
    }

    [Fact]
    public void AddUserApp_RejectsUnsupportedExtension()
    {
        var txt = WriteTemp("readme.txt", "no");
        Assert.Throws<ArgumentException>(() => _svc.AddUserApp(txt));
    }

    [Fact]
    public void AddUserApp_MissingFile_Throws()
    {
        Assert.Throws<FileNotFoundException>(() =>
            _svc.AddUserApp(Path.Combine(Path.GetTempPath(), "ghost.msi")));
    }

    [Theory]
    [InlineData(".msi", "/qn /norestart")]
    [InlineData(".exe", "/S")]
    public void DefaultSilentArgs_ByExtension(string ext, string expected) =>
        Assert.Equal(expected, AppCatalogService.DefaultSilentArgs(ext));

    private static string WriteTemp(string name, string content)
    {
        var dir = Path.Combine(Path.GetTempPath(), "w11test", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, name);
        File.WriteAllText(path, content);
        return path;
    }
}
