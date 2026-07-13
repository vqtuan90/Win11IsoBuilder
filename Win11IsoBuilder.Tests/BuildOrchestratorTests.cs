using Win11IsoBuilder.Services;

namespace Win11IsoBuilder.Tests;

public class BuildOrchestratorTests
{
    // ---- ForceDeleteDirectory ------------------------------------------------

    [Fact]
    public void ForceDeleteDirectory_RemovesTreeWithReadOnlyFilesAndDirectories()
    {
        // Extracted ISO media carries FILE_ATTRIBUTE_READONLY on directories, not just files
        // (inherited from the read-only ISO mount). RemoveDirectory rejects a read-only directory
        // outright, so a leftover one from an interrupted build must not make cleanup fail forever.
        var root = Path.Combine(Path.GetTempPath(), "Win11IsoBuilderTests", Guid.NewGuid().ToString("N"));
        var subDir = Path.Combine(root, "sources", "boot", "en-us");
        Directory.CreateDirectory(subDir);
        var file = Path.Combine(subDir, "boot.sdi");
        File.WriteAllText(file, "stub");

        File.SetAttributes(file, File.GetAttributes(file) | FileAttributes.ReadOnly);
        File.SetAttributes(subDir, File.GetAttributes(subDir) | FileAttributes.ReadOnly);

        BuildOrchestrator.ForceDeleteDirectory(root);

        Assert.False(Directory.Exists(root));
    }
}
