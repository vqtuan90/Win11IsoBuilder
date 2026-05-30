using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using Win11IsoBuilder.Models;
using Win11IsoBuilder.Services;

namespace Win11IsoBuilder.ViewModels;

/// <summary>Step 1: pick the source ISO and choose a Windows edition (peeked from the ISO).</summary>
public partial class SelectIsoViewModel : WizardStepViewModel
{
    private readonly LogService _log;
    private readonly ToolPaths _tools;
    private readonly IsoService _iso;
    private readonly WimService? _wim;

    [ObservableProperty] private string? _isoPath;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusMessage = "Select a Windows 11 ISO to begin.";
    [ObservableProperty] private WindowsEdition? _selectedEdition;

    public ObservableCollection<WindowsEdition> Editions { get; } = new();

    public SelectIsoViewModel(BuildConfig config, LogService log, ToolPaths tools)
        : base(config, "1. Select ISO")
    {
        _log = log;
        _tools = tools;
        var runner = new ProcessRunner(log);
        _iso = new IsoService(runner, log);
        _wim = tools.HasDism ? new WimService(runner, log, tools.DismPath!) : null;
    }

    public override bool CanProceed => !IsBusy && !string.IsNullOrEmpty(IsoPath) && SelectedEdition is not null;

    partial void OnSelectedEditionChanged(WindowsEdition? value)
    {
        Config.SelectedEdition = value;
        RaiseCanProceedChanged();
    }

    [RelayCommand]
    private async Task BrowseAsync()
    {
        var dlg = new OpenFileDialog { Filter = "Windows ISO (*.iso)|*.iso", Title = "Select Windows 11 ISO" };
        if (dlg.ShowDialog() != true) return;

        IsoPath = dlg.FileName;
        Config.SourceIsoPath = dlg.FileName;
        await LoadEditionsAsync();
    }

    private async Task LoadEditionsAsync()
    {
        if (_wim is null)
        {
            StatusMessage = "DISM not available — cannot read editions.";
            return;
        }

        IsBusy = true;
        RaiseCanProceedChanged();
        Editions.Clear();
        string? letter = null;
        try
        {
            StatusMessage = "Reading editions from ISO...";
            letter = await _iso.MountAsync(IsoPath!, CancellationToken.None);
            var image = IsoService.FindInstallImage($"{letter}:\\");
            if (image is null) { StatusMessage = "No install.wim/esd found — not a Windows ISO."; return; }

            foreach (var e in await _wim.GetImageInfoAsync(image))
                Editions.Add(e);
            SelectedEdition = Editions.FirstOrDefault();
            StatusMessage = $"Found {Editions.Count} edition(s).";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to read ISO: {ex.Message}";
            _log.Error(StatusMessage);
        }
        finally
        {
            if (letter is not null) await _iso.DismountAsync(IsoPath!);
            IsBusy = false;
            RaiseCanProceedChanged();
        }
    }
}
