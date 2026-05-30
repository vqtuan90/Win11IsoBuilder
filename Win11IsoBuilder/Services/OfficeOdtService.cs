using System.IO;
using System.Xml.Linq;
using Win11IsoBuilder.Models;

namespace Win11IsoBuilder.Services;

/// <summary>
/// Office Deployment Tool integration: generate configuration.xml, download the Microsoft
/// 365 source offline (cached), and stage setup.exe + source into the ISO payload. Only the
/// legal ODT flow — never any activation bypass. Activation is the user's responsibility.
/// </summary>
public sealed class OfficeOdtService
{
    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromHours(2);
    private static readonly TimeSpan CopyTimeout = TimeSpan.FromHours(1);

    private readonly ProcessRunner _runner;
    private readonly ILogSink _log;
    private readonly string _baseDir;

    public OfficeOdtService(ProcessRunner runner, ILogSink log, string? baseDir = null)
    {
        _runner = runner;
        _log = log;
        _baseDir = baseDir ?? AppContext.BaseDirectory;
    }

    /// <summary>
    /// Build an ODT configuration.xml. <paramref name="sourcePath"/> is set for the /download
    /// pass; pass null for the install copy so setup.exe uses its working directory.
    /// </summary>
    public XDocument BuildConfigurationXml(OfficeOptions o, string? sourcePath)
    {
        var product = new XElement("Product", new XAttribute("ID", o.ProductId));
        foreach (var lang in o.Languages)
            product.Add(new XElement("Language", new XAttribute("ID", lang)));
        foreach (var app in o.ExcludedApps)
            product.Add(new XElement("ExcludeApp", new XAttribute("ID", app)));

        var add = new XElement("Add",
            new XAttribute("OfficeClientEdition", o.Bitness),
            new XAttribute("Channel", o.Channel),
            product);
        if (!string.IsNullOrEmpty(sourcePath))
            add.SetAttributeValue("SourcePath", sourcePath);

        return new XDocument(
            new XElement("Configuration",
                add,
                new XElement("Display",
                    new XAttribute("Level", "None"), new XAttribute("AcceptEULA", "TRUE")),
                new XElement("Property",
                    new XAttribute("Name", "FORCEAPPSHUTDOWN"), new XAttribute("Value", "TRUE")),
                new XElement("RemoveMSI")));
    }

    /// <summary>Bundled ODT setup.exe (tools\odt\setup.exe).</summary>
    public string EnsureOdt()
    {
        var odt = Path.Combine(_baseDir, "tools", "odt", "setup.exe");
        if (!File.Exists(odt))
            throw new FileNotFoundException(
                "ODT setup.exe not bundled. Place it at tools\\odt\\setup.exe.", odt);
        return odt;
    }

    /// <summary>Download the Office source offline into the cache (idempotent / reusable).</summary>
    public async Task DownloadOfflineAsync(OfficeOptions o, string cacheDir, CancellationToken ct = default)
    {
        Directory.CreateDirectory(cacheDir);
        var odt = EnsureOdt();
        var configPath = Path.Combine(cacheDir, "download-config.xml");
        BuildConfigurationXml(o, cacheDir).Save(configPath);

        _log.Info("Downloading Microsoft 365 source (this can take a while, ~3.5 GB)...");
        var r = await _runner.RunAsync(odt, $"/download \"{configPath}\"",
            workingDirectory: cacheDir, timeout: DownloadTimeout, cancellationToken: ct)
            .ConfigureAwait(false);
        if (!r.Success)
            throw new InvalidOperationException($"ODT /download failed (exit {r.ExitCode}). {r.StdErr.Trim()}");
    }

    /// <summary>
    /// Stage setup.exe + an install configuration.xml (no SourcePath) + the downloaded
    /// "Office" source tree into <c>payload\Office</c>. First boot does <c>setup.exe /configure</c>
    /// from that folder so the offline source is found via the working directory.
    /// </summary>
    public async Task StageToPayloadAsync(OfficeOptions o, string cacheDir, string payloadDir,
        CancellationToken ct = default)
    {
        var odt = EnsureOdt();
        var officeOut = Path.Combine(payloadDir, "Office");
        Directory.CreateDirectory(officeOut);

        File.Copy(odt, Path.Combine(officeOut, "setup.exe"), overwrite: true);
        BuildConfigurationXml(o, null).Save(Path.Combine(officeOut, "configuration.xml"));

        var sourceTree = Path.Combine(cacheDir, "Office");
        if (!Directory.Exists(sourceTree))
            throw new DirectoryNotFoundException(
                $"Office source not downloaded yet ({sourceTree}). Run DownloadOffline first.");

        await CopyDirAsync(sourceTree, Path.Combine(officeOut, "Office"), ct).ConfigureAwait(false);
        _log.Info($"Staged Office payload → {officeOut}");
    }

    private async Task CopyDirAsync(string source, string dest, CancellationToken ct)
    {
        Directory.CreateDirectory(dest);
        var args = $"\"{source}\" \"{dest}\" /E /R:1 /W:1 /NFL /NDL /NJH /NJS /NP";
        var r = await _runner.RunAsync("robocopy.exe", args, timeout: CopyTimeout, cancellationToken: ct)
            .ConfigureAwait(false);
        if (r.ExitCode >= 8)
            throw new InvalidOperationException($"robocopy Office failed (exit {r.ExitCode}).");
    }
}
