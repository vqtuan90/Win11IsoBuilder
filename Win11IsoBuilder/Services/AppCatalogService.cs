using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using Win11IsoBuilder.Models;

namespace Win11IsoBuilder.Services;

/// <summary>
/// Manages the app catalog: loads the built-in JSON list, lets the user add their own
/// installers, acquires installers into a reusable cache (download or copy, optional
/// SHA-256 verify), and stages selected apps into the offline payload — returning the
/// install commands for the first-boot script.
/// </summary>
public sealed class AppCatalogService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(30) };
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private readonly ILogSink _log;
    private readonly string _catalogPath;

    public AppCatalogService(ILogSink log, string? catalogPath = null)
    {
        _log = log;
        _catalogPath = catalogPath ?? Path.Combine(AppContext.BaseDirectory, "Assets", "app-catalog.json");
    }

    public List<AppEntry> LoadCatalog()
    {
        if (!File.Exists(_catalogPath))
        {
            _log.Warn($"App catalog not found at {_catalogPath}.");
            return new List<AppEntry>();
        }
        var json = File.ReadAllText(_catalogPath);
        return JsonSerializer.Deserialize<List<AppEntry>>(json, JsonOpts) ?? new List<AppEntry>();
    }

    /// <summary>Validate and wrap a user-supplied installer (.exe/.msi).</summary>
    public AppEntry AddUserApp(string installerPath, string? silentArgs = null)
    {
        if (!File.Exists(installerPath))
            throw new FileNotFoundException("Installer not found.", installerPath);
        var ext = Path.GetExtension(installerPath).ToLowerInvariant();
        if (ext is not (".exe" or ".msi"))
            throw new ArgumentException("Only .exe or .msi installers are supported.", nameof(installerPath));

        var name = Path.GetFileNameWithoutExtension(installerPath);
        return new AppEntry
        {
            Id = Sanitize(name),
            Name = name,
            Category = "User",
            LocalPath = installerPath,
            FileName = Path.GetFileName(installerPath),
            SilentArgs = silentArgs ?? DefaultSilentArgs(ext),
            IsUserAdded = true,
            IsSelected = true,
        };
    }

    /// <summary>Ensure the installer is in the cache; returns the cached file path.</summary>
    public async Task<string> AcquireInstallerAsync(AppEntry app, string cacheDir, CancellationToken ct = default)
    {
        var appsCache = Path.Combine(cacheDir, "apps", app.Id);
        Directory.CreateDirectory(appsCache);
        var dest = Path.Combine(appsCache, app.FileName);

        if (File.Exists(dest) && await IsHashOkAsync(dest, app.Sha256, ct).ConfigureAwait(false))
        {
            _log.Info($"Using cached installer: {app.Id}");
            return dest;
        }

        if (!string.IsNullOrWhiteSpace(app.LocalPath) && File.Exists(app.LocalPath))
            File.Copy(app.LocalPath, dest, overwrite: true);
        else if (!string.IsNullOrWhiteSpace(app.SourceUrl))
            await DownloadAsync(app.SourceUrl!, dest, ct).ConfigureAwait(false);
        else
            throw new InvalidOperationException($"App '{app.Id}' has no source URL or local file.");

        if (!await IsHashOkAsync(dest, app.Sha256, ct).ConfigureAwait(false))
            throw new InvalidOperationException($"SHA-256 mismatch for {app.Id}.");
        return dest;
    }

    /// <summary>
    /// Stage every acquirable selected app into payload\Apps\&lt;id&gt; and return the first-boot
    /// install commands. Non-acquirable selections are skipped with a warning (build continues).
    /// </summary>
    public async Task<List<AppInstallCommand>> StageToPayloadAsync(
        IEnumerable<AppEntry> selected, string payloadDir, string cacheDir, CancellationToken ct = default)
    {
        var commands = new List<AppInstallCommand>();
        foreach (var app in selected)
        {
            ct.ThrowIfCancellationRequested();
            if (!app.IsAcquirable)
            {
                _log.Warn($"Skipping '{app.Id}': no installer available. {app.Note}");
                continue;
            }

            var cached = await AcquireInstallerAsync(app, cacheDir, ct).ConfigureAwait(false);
            var appDir = Path.Combine(payloadDir, "Apps", app.Id);
            Directory.CreateDirectory(appDir);
            var staged = Path.Combine(appDir, app.FileName);
            File.Copy(cached, staged, overwrite: true);

            var relative = Path.Combine("Apps", app.Id, app.FileName);
            var args = app.IsMsi && string.IsNullOrWhiteSpace(app.SilentArgs)
                ? "/qn /norestart"
                : app.SilentArgs;
            commands.Add(new AppInstallCommand(app.Id, relative, args, app.IsMsi));
            _log.Info($"Staged app: {app.Id}");
        }
        return commands;
    }

    public static string DefaultSilentArgs(string extension) =>
        extension.ToLowerInvariant() == ".msi" ? "/qn /norestart" : "/S";

    private async Task DownloadAsync(string url, string dest, CancellationToken ct)
    {
        _log.Info($"Downloading {url}");
        using var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        await using var fs = File.Create(dest);
        await resp.Content.CopyToAsync(fs, ct).ConfigureAwait(false);
    }

    private static async Task<bool> IsHashOkAsync(string path, string? expected, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(expected)) return true;
        await using var fs = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(fs, ct).ConfigureAwait(false);
        return Convert.ToHexString(hash).Equals(expected.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static string Sanitize(string s) =>
        new(s.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_').ToArray());
}
