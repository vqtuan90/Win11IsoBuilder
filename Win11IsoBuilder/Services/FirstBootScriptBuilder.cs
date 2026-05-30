using System.IO;
using System.Text;
using Win11IsoBuilder.Models;

namespace Win11IsoBuilder.Services;

/// <summary>
/// Emits the offline first-boot scripts: SetupComplete.cmd (SYSTEM context, before first
/// logon) plus set-computername.ps1. SetupComplete sets the machine name and runs every
/// staged installer from the payload drive. One installer failing never stops the rest.
/// </summary>
public sealed class FirstBootScriptBuilder
{
    private readonly ILogSink? _log;
    private readonly string _assetsDir;

    public FirstBootScriptBuilder(ILogSink? log = null, string? assetsDir = null)
    {
        _log = log;
        _assetsDir = assetsDir ?? Path.Combine(AppContext.BaseDirectory, "Assets");
    }

    /// <summary>
    /// Write SetupComplete.cmd + set-computername.ps1 into the $OEM$ tree and drop the
    /// payload marker file. Setup copies $OEM$\$$ → %WINDIR% automatically.
    /// </summary>
    public void Write(BuildConfig config, bool officeEnabled, IReadOnlyList<AppInstallCommand> apps)
    {
        var scriptsDir = Path.Combine(config.MediaDir, "sources", "$OEM$", "$$", "Setup", "Scripts");
        Directory.CreateDirectory(scriptsDir);
        Directory.CreateDirectory(config.PayloadDir);

        var cmd = BuildSetupCompleteCmd(
            ReadTemplate("setup-complete.template.cmd"), officeEnabled, apps);
        File.WriteAllText(Path.Combine(scriptsDir, "SetupComplete.cmd"), cmd, new UTF8Encoding(false));

        var ps1 = BuildComputerNameScript(
            ReadTemplate("set-computername.template.ps1"), config.Windows);
        File.WriteAllText(Path.Combine(scriptsDir, "set-computername.ps1"), ps1, new UTF8Encoding(false));

        // Marker the first-boot script greps for to find the (variable) payload drive letter.
        File.WriteAllText(Path.Combine(config.PayloadDir, "manifest.txt"),
            $"Win11IsoBuilder payload {DateTime.Now:s}");

        _log?.Info($"Wrote first-boot scripts → {scriptsDir}");
    }

    /// <summary>Fill the cmd template's Office/Apps blocks. Public for unit testing.</summary>
    public string BuildSetupCompleteCmd(string template, bool officeEnabled,
        IReadOnlyList<AppInstallCommand> apps) =>
        template
            .Replace("{{OFFICE_BLOCK}}", officeEnabled ? BuildOfficeBlock() : "  rem Office not selected")
            .Replace("{{APPS_BLOCK}}", BuildAppsBlock(apps));

    /// <summary>Inject mode + sanitized fixed name into the ps1 template. Public for testing.</summary>
    public string BuildComputerNameScript(string template, WinCustomizationOptions o)
    {
        var fixedName = TrySanitizeNetbiosName(o.FixedComputerName, out var safe) ? safe : string.Empty;
        return template
            .Replace("{{MODE}}", o.ComputerNameMode.ToString())
            .Replace("{{FIXEDNAME}}", fixedName);
    }

    private static string BuildOfficeBlock()
    {
        var sb = new StringBuilder();
        sb.AppendLine("  echo [%DATE% %TIME%] Installing Microsoft 365 Apps... >> \"%LOG%\"");
        sb.AppendLine("  pushd \"%PD%\\Office\"");
        sb.AppendLine("  start /wait \"\" \"%PD%\\Office\\setup.exe\" /configure \"%PD%\\Office\\configuration.xml\" >> \"%LOG%\" 2>&1");
        sb.AppendLine("  echo [%DATE% %TIME%] Office finished (exit !ERRORLEVEL!) >> \"%LOG%\"");
        sb.Append("  popd");
        return sb.ToString();
    }

    /// <summary>One start/wait line per app; msi goes through msiexec, exe runs directly.</summary>
    public string BuildAppsBlock(IReadOnlyList<AppInstallCommand> apps)
    {
        if (apps.Count == 0) return "  rem No apps selected";
        var sb = new StringBuilder();
        foreach (var app in apps)
        {
            var target = $"%PD%\\{app.RelativePath.Replace('/', '\\')}";
            var line = app.IsMsi
                ? $"start /wait \"\" msiexec /i \"{target}\" {app.Arguments}".TrimEnd()
                : $"start /wait \"\" \"{target}\" {app.Arguments}".TrimEnd();

            sb.AppendLine($"  echo [%DATE% %TIME%] Installing {app.Id}... >> \"%LOG%\"");
            sb.AppendLine($"  {line} >> \"%LOG%\" 2>&1");
            sb.AppendLine($"  echo [%DATE% %TIME%] {app.Id} finished (exit !ERRORLEVEL!) >> \"%LOG%\"");
        }
        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// NetBIOS-safe name rules (mirrors set-computername.ps1): strip chars outside
    /// [A-Za-z0-9-], cap at 15, reject blank/OEM-placeholder values. Returns false when
    /// the caller must fall back to a generated WIN-xxxxxx name.
    /// </summary>
    public static bool TrySanitizeNetbiosName(string? raw, out string name)
    {
        name = string.Empty;
        if (string.IsNullOrWhiteSpace(raw)) return false;

        var placeholders = new[]
        {
            "To be filled by O.E.M.", "Default string", "System Serial Number",
            "None", "0", "Not Specified",
        };
        if (placeholders.Contains(raw.Trim(), StringComparer.OrdinalIgnoreCase)) return false;

        var clean = new string(raw.Where(c => char.IsLetterOrDigit(c) || c == '-').ToArray());
        if (clean.Length > 15) clean = clean[..15];
        if (string.IsNullOrWhiteSpace(clean)) return false;

        name = clean;
        return true;
    }

    private string ReadTemplate(string fileName)
    {
        var path = Path.Combine(_assetsDir, fileName);
        return File.ReadAllText(path);
    }
}
