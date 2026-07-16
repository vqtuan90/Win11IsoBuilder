using System.Diagnostics;
using System.IO;

namespace Win11IsoBuilder.Services;

/// <summary>
/// Windows 11 24H2+ media boots the new "ConX" Setup (SetupPrep.exe). ConX applies the
/// windowsPE pass of autounattend.xml (disk wipe/ImageInstall work) but skips or mishandles
/// the specialize and oobeSystem passes — OOBE then asks region/keyboard/network and
/// SetupComplete.cmd never runs, so no offline app install. The community-verified fix
/// (identical to NTLite's "Legacy" boot option) is a winpeshl.ini inside boot.wim's Setup
/// image that launches the proven legacy setup.exe, which honors every unattend pass.
/// </summary>
public static class LegacySetupPatcher
{
    /// <summary>
    /// winpeshl.ini payload (CRLF mandatory for WinPE ini parsing). It must:
    /// (1) run wpeinit — a custom winpeshl.ini replaces the default shell so the stock
    ///     startnet.cmd (which is just "wpeinit") no longer runs; skipping it leaves the
    ///     WinPE bootstrap uninitialized;
    /// (2) launch the MEDIA-ROOT X:\setup.exe (the small "Windows Installer" launcher stub
    ///     that understands /legacy), NOT X:\sources\setup.exe (the classic setup engine).
    ///     /legacy makes the stub run the classic setup — which honors every unattend pass —
    ///     instead of ConX. Launching the engine directly bypasses the stub's bootstrap and
    ///     fails with "could not apply the Windows PE bootstrap setting".
    /// </summary>
    public const string WinpeshlIni =
        "[LaunchApps]\r\n" +
        "%SYSTEMDRIVE%\\Windows\\System32\\wpeinit.exe\r\n" +
        "%SYSTEMDRIVE%\\setup.exe, /legacy\r\n";

    /// <summary>Win11 24H2 (first ConX build). 25H2 = 26200.</summary>
    private const int FirstConXBuild = 26100;

    /// <summary>
    /// True when the extracted media ships the ConX Setup, detected via the build number
    /// of sources\setup.exe. Pre-24H2 media keeps the untouched legacy flow (the /legacy
    /// switch is never written there, so older setup.exe never sees an unknown argument).
    /// </summary>
    public static bool IsConXMedia(string sourcesDir)
    {
        var setupExe = Path.Combine(sourcesDir, "setup.exe");
        if (!File.Exists(setupExe)) return false;
        return IsConXBuild(FileVersionInfo.GetVersionInfo(setupExe).ProductBuildPart);
    }

    public static bool IsConXBuild(int build) => build >= FirstConXBuild;

    /// <summary>
    /// Write winpeshl.ini into a mounted boot.wim image if it is the Setup image
    /// (identified by \sources\setup.exe — the plain WinPE index has none).
    /// Returns true when the image was patched.
    /// </summary>
    public static bool TryPatchMountedImage(string mountDir)
    {
        if (!File.Exists(Path.Combine(mountDir, "sources", "setup.exe"))) return false;
        var system32 = Path.Combine(mountDir, "Windows", "System32");
        Directory.CreateDirectory(system32);
        File.WriteAllText(Path.Combine(system32, "winpeshl.ini"), WinpeshlIni);
        return true;
    }
}
