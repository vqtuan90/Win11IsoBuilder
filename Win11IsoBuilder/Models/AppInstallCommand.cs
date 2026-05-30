namespace Win11IsoBuilder.Models;

/// <summary>
/// A single offline installer to run at first boot. Produced by AppCatalogService
/// staging; consumed by FirstBootScriptBuilder to emit a SetupComplete.cmd line.
/// </summary>
/// <param name="Id">App id (used for the payload sub-folder).</param>
/// <param name="RelativePath">Installer path relative to the Payload root, e.g. Apps\chrome\chrome.msi.</param>
/// <param name="Arguments">Silent-install arguments (for exe) or msiexec args (for msi).</param>
/// <param name="IsMsi">True → invoke via msiexec; false → run the exe directly.</param>
public sealed record AppInstallCommand(string Id, string RelativePath, string Arguments, bool IsMsi);
