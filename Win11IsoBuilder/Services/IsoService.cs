using System.IO;
using Win11IsoBuilder.Models;

namespace Win11IsoBuilder.Services;

/// <summary>
/// Owns the on-disk ISO image: extract a source ISO to an editable media tree,
/// validate it is a real Windows installer, and repack the tree into a bootable
/// (UEFI + legacy BIOS) ISO with oscdimg.
/// </summary>
public sealed class IsoService
{
    private static readonly TimeSpan MountTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan CopyTimeout = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan RepackTimeout = TimeSpan.FromMinutes(30);

    private readonly ProcessRunner _runner;
    private readonly ILogSink _log;

    public IsoService(ProcessRunner runner, ILogSink log)
    {
        _runner = runner;
        _log = log;
    }

    /// <summary>
    /// Mount the (read-only) ISO, robocopy every file to <paramref name="mediaDir"/>,
    /// then dismount. We copy rather than edit in place so the original ISO is untouched.
    /// </summary>
    public async Task ExtractIsoAsync(string isoPath, string mediaDir, CancellationToken ct = default)
    {
        if (!File.Exists(isoPath))
            throw new FileNotFoundException("Source ISO not found.", isoPath);

        Directory.CreateDirectory(mediaDir);
        string? driveLetter = null;
        try
        {
            driveLetter = await MountAsync(isoPath, ct).ConfigureAwait(false);
            _log.Info($"Mounted ISO at {driveLetter}: — copying to {mediaDir}");
            await RobocopyAsync($"{driveLetter}:\\", mediaDir, ct).ConfigureAwait(false);
        }
        finally
        {
            await DismountAsync(isoPath).ConfigureAwait(false);
        }
    }

    /// <summary>Locate sources\install.{wim|esd} on a drive (the mounted ISO or media tree).</summary>
    public static string? FindInstallImage(string rootDir)
    {
        var wim = Path.Combine(rootDir, "sources", "install.wim");
        if (File.Exists(wim)) return wim;
        var esd = Path.Combine(rootDir, "sources", "install.esd");
        return File.Exists(esd) ? esd : null;
    }

    /// <summary>A valid Windows ISO has sources\install.{wim|esd} and boot.wim.</summary>
    public bool ValidateMedia(string mediaDir, out string reason)
    {
        var sources = Path.Combine(mediaDir, "sources");
        var hasInstall = File.Exists(Path.Combine(sources, "install.wim"))
                         || File.Exists(Path.Combine(sources, "install.esd"));
        var hasBoot = File.Exists(Path.Combine(sources, "boot.wim"));

        reason = (hasInstall, hasBoot) switch
        {
            (false, _) => "Missing sources\\install.wim or install.esd.",
            (_, false) => "Missing sources\\boot.wim.",
            _ => string.Empty,
        };
        return string.IsNullOrEmpty(reason);
    }

    /// <summary>
    /// Repack <paramref name="mediaDir"/> into a dual-boot ISO. The -bootdata:2#... form
    /// embeds both the BIOS (etfsboot.com) and UEFI (efisys.bin) boot images.
    /// </summary>
    public async Task RepackIsoAsync(string mediaDir, string outIsoPath, string oscdimgPath,
        string volumeLabel = "WIN11", CancellationToken ct = default)
    {
        var biosBoot = Path.Combine(mediaDir, "boot", "etfsboot.com");
        var uefiBoot = Path.Combine(mediaDir, "efi", "microsoft", "boot", "efisys.bin");
        if (!File.Exists(biosBoot)) throw new FileNotFoundException("BIOS boot image missing.", biosBoot);
        if (!File.Exists(uefiBoot)) throw new FileNotFoundException("UEFI boot image missing.", uefiBoot);

        Directory.CreateDirectory(Path.GetDirectoryName(outIsoPath)!);

        var bootData = $"2#p0,e,b\"{biosBoot}\"#pEF,e,b\"{uefiBoot}\"";
        var args = $"-bootdata:{bootData} -u2 -udfver102 -m -o -l{volumeLabel} " +
                   $"\"{mediaDir}\" \"{outIsoPath}\"";

        _log.Info($"Repacking ISO → {outIsoPath}");
        var r = await _runner.RunAsync(oscdimgPath, args, timeout: RepackTimeout, cancellationToken: ct)
            .ConfigureAwait(false);
        if (!r.Success)
            throw new InvalidOperationException($"oscdimg failed (exit {r.ExitCode}). {r.StdErr.Trim()}");

        var sizeBytes = new FileInfo(outIsoPath).Length;
        _log.Info($"ISO created: {outIsoPath} ({sizeBytes / 1024 / 1024} MB)");
        if (sizeBytes > 4L * 1024 * 1024 * 1024)
            _log.Warn("ISO exceeds 4 GB — it will not fit a FAT32 UEFI USB. Use NTFS (Rufus) or split-WIM.");
    }

    // ---- internals ----------------------------------------------------------

    /// <summary>Mount a read-only ISO and return its drive letter (no trailing colon).</summary>
    public async Task<string> MountAsync(string isoPath, CancellationToken ct)
    {
        var ps = EscapePsLiteral(isoPath);
        // Mount, then poll for the assigned drive letter — it is not always ready immediately
        // (slow storage / AV scanning the just-mounted volume). Print the letter on its own line.
        var script =
            $"Mount-DiskImage -ImagePath '{ps}' | Out-Null; " +
            "for ($i=0; $i -lt 20; $i++) { " +
            $"  $v = (Get-DiskImage -ImagePath '{ps}' | Get-Volume).DriveLetter; " +
            "  if ($v) { $v; break }; Start-Sleep -Milliseconds 250 }";
        var r = await _runner.RunAsync("powershell.exe",
            $"-NoProfile -NonInteractive -Command \"{script}\"",
            timeout: MountTimeout, cancellationToken: ct).ConfigureAwait(false);

        var letter = r.StdOut.Replace("\r", "").Split('\n')
            .Select(l => l.Trim())
            .LastOrDefault(l => l.Length == 1 && char.IsLetter(l[0]));

        if (string.IsNullOrEmpty(letter))
            throw new InvalidOperationException($"Could not determine ISO drive letter. {r.StdErr.Trim()}");
        return letter;
    }

    public async Task DismountAsync(string isoPath)
    {
        var script = $"Dismount-DiskImage -ImagePath '{EscapePsLiteral(isoPath)}'";
        var r = await _runner.RunAsync("powershell.exe",
            $"-NoProfile -NonInteractive -Command \"{script}\"",
            timeout: MountTimeout, cancellationToken: CancellationToken.None).ConfigureAwait(false);
        if (!r.Success) _log.Warn($"Dismount-DiskImage returned {r.ExitCode} for {isoPath}.");
    }

    /// <summary>Escape a path for a PowerShell single-quoted literal (double any quote).</summary>
    private static string EscapePsLiteral(string value) => value.Replace("'", "''");

    private async Task RobocopyAsync(string source, string dest, CancellationToken ct)
    {
        // /E recurse incl. empty. /A-:R strips the read-only attribute the files inherit from the
        // read-only ISO mount — DISM cannot mount install.wim for modify if it stays read-only (0xc1510111).
        var args = $"\"{source.TrimEnd('\\')}\" \"{dest}\" /E /A-:R /R:1 /W:1 /NFL /NDL /NJH /NJS /NP";
        var r = await _runner.RunAsync("robocopy.exe", args, timeout: CopyTimeout, cancellationToken: ct)
            .ConfigureAwait(false);
        // robocopy success is exit code < 8 (8+ means at least one file failed).
        if (r.ExitCode >= 8)
            throw new InvalidOperationException($"robocopy failed (exit {r.ExitCode}). {r.StdErr.Trim()}");
    }
}
