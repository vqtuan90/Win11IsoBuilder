using System.IO;
using System.Runtime.InteropServices;
using Win11IsoBuilder.Models;

namespace Win11IsoBuilder.Services;

/// <summary>
/// Runs a build from the command line (no GUI) for automation / smoke tests:
///   --build --iso "x.iso" --out "C:\out" [--name file.iso] [--edition N]
///           [--debloat "Microsoft.BingNews,Microsoft.BingWeather"] [--office]
/// Office and apps are OFF unless requested, keeping a smoke test fast.
/// </summary>
public static class HeadlessBuildRunner
{
    [DllImport("kernel32.dll")] private static extern bool AllocConsole();

    public static async Task<int> RunAsync(string[] args)
    {
        AllocConsole();
        var opts = Parse(args);
        var log = new LogService();
        log.EntryLogged += (_, e) =>
        {
            if (e.Level is LogLevel.Warn or LogLevel.Error) Console.WriteLine(e.ToString());
        };

        try
        {
            var cfg = BuildConfigFrom(opts);
            SelectApps(cfg, opts, log);
            Console.WriteLine($"Source ISO : {cfg.SourceIsoPath}");
            Console.WriteLine($"Output     : {cfg.OutputIsoPath}");

            await SelectEditionAsync(cfg, log, opts).ConfigureAwait(false);
            Console.WriteLine($"Edition    : {cfg.SelectedEdition}");
            Console.WriteLine($"Office     : {cfg.Office.Enabled} | Debloat: {cfg.Windows.AppxToRemove.Count} | Apps: {cfg.SelectedApps.Count}");
            Console.WriteLine(new string('-', 60));

            var progress = new Progress<BuildProgress>(p =>
                Console.WriteLine($"[{p.Percent,3}%] {p.Stage}: {p.Message}"));

            var orchestrator = new BuildOrchestrator(log);
            await orchestrator.RunAsync(cfg, progress, CancellationToken.None).ConfigureAwait(false);

            Console.WriteLine(new string('-', 60));
            Console.WriteLine($"SUCCESS: {cfg.OutputIsoPath}");
            Console.WriteLine($"Log: {log.LogFilePath}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine(new string('-', 60));
            Console.WriteLine($"FAILED: {ex.Message}");
            Console.WriteLine($"Log: {log.LogFilePath}");
            return 1;
        }
    }

    private static BuildConfig BuildConfigFrom(Dictionary<string, string> o)
    {
        var iso = Require(o, "iso");
        if (!File.Exists(iso)) throw new FileNotFoundException("Source ISO not found.", iso);

        var cfg = new BuildConfig
        {
            SourceIsoPath = iso,
            OutputDirectory = o.GetValueOrDefault("out",
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)),
            OutputIsoName = o.GetValueOrDefault("name", "Win11-Custom-Test.iso"),
        };
        cfg.Office.Enabled = o.ContainsKey("office");
        cfg.Windows.ComputerNameMode = ComputerNameMode.Serial;

        if (o.TryGetValue("debloat", out var d) && !string.IsNullOrWhiteSpace(d))
            cfg.Windows.AppxToRemove = d.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

        return cfg;
    }

    /// <summary>Load the bundled catalog and mark the apps named in --apps "id1,id2" as selected.</summary>
    private static void SelectApps(BuildConfig cfg, Dictionary<string, string> o, LogService log)
    {
        if (!o.TryGetValue("apps", out var csv) || string.IsNullOrWhiteSpace(csv)) return;
        var want = csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var catalog = new AppCatalogService(log).LoadCatalog();
        foreach (var a in catalog) a.IsSelected = want.Contains(a.Id);
        cfg.SelectedApps = catalog;
        Console.WriteLine($"Apps selected: {string.Join(", ", catalog.Where(a => a.IsSelected).Select(a => a.Id))}");
    }

    /// <summary>Peek editions from the ISO and select --edition N, else a Pro SKU, else index 1.</summary>
    private static async Task SelectEditionAsync(BuildConfig cfg, LogService log, Dictionary<string, string> o)
    {
        var tools = new ToolDetectionService(log).Detect();
        if (tools.IsAdkMissing) throw new InvalidOperationException("oscdimg not found (bundle it under tools\\oscdimg).");
        var runner = new ProcessRunner(log);
        var iso = new IsoService(runner, log);
        var wim = new WimService(runner, log, tools.DismPath!);

        string? letter = null;
        try
        {
            letter = await iso.MountAsync(cfg.SourceIsoPath!, CancellationToken.None).ConfigureAwait(false);
            var image = IsoService.FindInstallImage($"{letter}:\\")
                        ?? throw new InvalidOperationException("No install.wim/esd on the ISO.");
            var editions = await wim.GetImageInfoAsync(image).ConfigureAwait(false);
            Console.WriteLine($"Editions found: {string.Join(" | ", editions.Select(e => e.ToString()))}");

            cfg.SelectedEdition = o.TryGetValue("edition", out var n) && int.TryParse(n, out var idx)
                ? editions.FirstOrDefault(e => e.Index == idx) ?? editions.First()
                : editions.FirstOrDefault(e => e.Name.Contains("Pro", StringComparison.OrdinalIgnoreCase))
                  ?? editions.First();
        }
        finally
        {
            if (letter is not null) await iso.DismountAsync(cfg.SourceIsoPath!).ConfigureAwait(false);
        }
    }

    private static Dictionary<string, string> Parse(string[] args)
    {
        var o = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--")) continue;
            var key = args[i][2..];
            if (key.Equals("build", StringComparison.OrdinalIgnoreCase)) continue;
            var hasVal = i + 1 < args.Length && !args[i + 1].StartsWith("--");
            o[key] = hasVal ? args[++i] : "true";
        }
        return o;
    }

    private static string Require(Dictionary<string, string> o, string key) =>
        o.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v)
            ? v : throw new ArgumentException($"Missing required --{key}");
}
