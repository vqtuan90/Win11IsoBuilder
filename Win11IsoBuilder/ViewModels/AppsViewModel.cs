using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using Win11IsoBuilder.Models;
using Win11IsoBuilder.Services;

namespace Win11IsoBuilder.ViewModels;

/// <summary>
/// Step 4: choose apps from the built-in catalog and add your own installers. The same
/// AppEntry instances are shared with <c>Config.SelectedApps</c>; the build reads IsSelected.
/// </summary>
public partial class AppsViewModel : WizardStepViewModel
{
    private readonly AppCatalogService _catalog;
    private readonly LogService _log;

    public ObservableCollection<AppEntry> Apps { get; } = new();

    public AppsViewModel(BuildConfig config, LogService log) : base(config, "4. Apps")
    {
        _log = log;
        _catalog = new AppCatalogService(log);
        foreach (var app in _catalog.LoadCatalog())
            Apps.Add(app);

        // Share the same instances with the build config so IsSelected flows through.
        Config.SelectedApps = Apps;
    }

    [RelayCommand]
    private void AddUserApp()
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Installers (*.exe;*.msi)|*.exe;*.msi",
            Title = "Add an installer",
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            var entry = _catalog.AddUserApp(dlg.FileName);
            Apps.Add(entry);
        }
        catch (Exception ex)
        {
            _log.Error($"Could not add app: {ex.Message}");
        }
    }
}
