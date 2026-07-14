using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using Win11IsoBuilder.Models;
using Win11IsoBuilder.Services;

namespace Win11IsoBuilder.ViewModels;

/// <summary>Step 5: name the output, run the build, watch progress + live log.</summary>
public partial class ReviewBuildViewModel : WizardStepViewModel
{
    private readonly LogService _log;
    private readonly ToolPaths _tools;
    private readonly BuildOrchestrator _orchestrator;
    private readonly StringBuilder _logBuffer = new();
    private CancellationTokenSource? _cts;

    [ObservableProperty] private int _progressPercent;
    [ObservableProperty] private string _statusMessage = "Ready to build.";
    [ObservableProperty] private bool _isBuilding;
    [ObservableProperty] private string _logText = string.Empty;
    [ObservableProperty] private bool _succeeded;

    public ReviewBuildViewModel(BuildConfig config, LogService log, ToolPaths tools)
        : base(config, "5. Review & Build")
    {
        _log = log;
        _tools = tools;
        _orchestrator = new BuildOrchestrator(log);
        _log.EntryLogged += (_, entry) => AppendLog(entry.ToString());
    }

    public bool AdkMissing => _tools.IsAdkMissing;
    public string AdkWarning => AdkMissing
        ? "oscdimg not found. Bundle it under tools\\oscdimg or install the Windows ADK before building."
        : string.Empty;

    // Evaluated each time the Review step's view is (re)created, so they reflect the
    // latest choices made in step 2 (the config POCOs raise no change notifications).
    public bool ZeroTouchEnabled => Config.Windows.AutoPartition;
    public string ZeroTouchWarning =>
        "Fully automated install is ON — Disk 0 of the target machine will be ERASED without any prompt. " +
        "Go back to step 2 to switch to the interactive drive picker.";
    public string DriversSummary =>
        $"Storage drivers: Intel VMD {(Config.Drivers.IncludeIntelVmd ? "ON" : "off")}" +
        $" | custom folders: {Config.Drivers.DriverFolders.Count}";

    public string OutputName
    {
        get => Config.OutputIsoName;
        set { Config.OutputIsoName = value; OnPropertyChanged(); BuildCommand.NotifyCanExecuteChanged(); }
    }

    public string OutputDirectory
    {
        get => Config.OutputDirectory;
        set { Config.OutputDirectory = value; OnPropertyChanged(); }
    }

    private bool CanBuild =>
        !IsBuilding && !AdkMissing && !string.IsNullOrWhiteSpace(Config.SourceIsoPath)
        && !string.IsNullOrWhiteSpace(OutputName);

    [RelayCommand]
    private void BrowseOutput()
    {
        var dlg = new OpenFolderDialog { Title = "Select output folder" };
        if (dlg.ShowDialog() == true) OutputDirectory = dlg.FolderName;
    }

    [RelayCommand(CanExecute = nameof(CanBuild))]
    private async Task BuildAsync()
    {
        IsBuilding = true;
        Succeeded = false;
        BuildCommand.NotifyCanExecuteChanged();
        _cts = new CancellationTokenSource();
        var progress = new Progress<BuildProgress>(OnProgress);

        try
        {
            await _orchestrator.RunAsync(Config, progress, _cts.Token);
            Succeeded = true;
            StatusMessage = $"Done: {Config.OutputIsoPath}";
            OpenOutputFolder();
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Build cancelled.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Build failed: {ex.Message}";
        }
        finally
        {
            IsBuilding = false;
            _cts?.Dispose();
            _cts = null;
            BuildCommand.NotifyCanExecuteChanged();
        }
    }

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    private void OnProgress(BuildProgress p)
    {
        ProgressPercent = p.Percent;
        StatusMessage = $"{p.Stage}: {p.Message}";
    }

    private void OpenOutputFolder()
    {
        if (File.Exists(Config.OutputIsoPath))
            Process.Start("explorer.exe", $"/select,\"{Config.OutputIsoPath}\"");
    }

    private void AppendLog(string line)
    {
        // LogService raises on background threads — marshal to the UI thread.
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(() => AppendLog(line));
            return;
        }
        _logBuffer.AppendLine(line);
        LogText = _logBuffer.ToString();
    }
}
