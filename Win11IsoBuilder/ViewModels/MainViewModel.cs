using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Win11IsoBuilder.Models;
using Win11IsoBuilder.Services;

namespace Win11IsoBuilder.ViewModels;

/// <summary>
/// Wizard shell: owns the single <see cref="BuildConfig"/>, detects tools once at startup,
/// builds the five step VMs, and drives Back/Next navigation gated by each step's CanProceed.
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly BuildConfig _config = new();

    [ObservableProperty] private WizardStepViewModel _currentStep = null!;
    [ObservableProperty] private int _stepIndex;

    public ObservableCollection<WizardStepViewModel> Steps { get; } = new();
    public LogService Log { get; } = new();

    public MainViewModel()
    {
        var tools = new ToolDetectionService(Log).Detect();

        Steps.Add(new SelectIsoViewModel(_config, Log, tools));
        Steps.Add(new WindowsCustomizeViewModel(_config));
        Steps.Add(new OfficeViewModel(_config));
        Steps.Add(new AppsViewModel(_config, Log));
        Steps.Add(new ReviewBuildViewModel(_config, Log, tools));

        GoToStep(0);
    }

    public bool IsFirstStep => StepIndex == 0;
    public bool IsLastStep => StepIndex == Steps.Count - 1;

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private void Next()
    {
        if (!IsLastStep) GoToStep(StepIndex + 1);
    }

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void Back()
    {
        if (!IsFirstStep) GoToStep(StepIndex - 1);
    }

    private bool CanGoNext() => !IsLastStep && CurrentStep.CanProceed;
    private bool CanGoBack() => !IsFirstStep;

    private void GoToStep(int index)
    {
        if (CurrentStep is not null) CurrentStep.PropertyChanged -= OnStepPropertyChanged;
        StepIndex = index;
        CurrentStep = Steps[index];
        CurrentStep.PropertyChanged += OnStepPropertyChanged;
        CurrentStep.OnEntered();
        RefreshNav();
    }

    private void OnStepPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WizardStepViewModel.CanProceed)) RefreshNav();
    }

    private void RefreshNav()
    {
        OnPropertyChanged(nameof(IsFirstStep));
        OnPropertyChanged(nameof(IsLastStep));
        NextCommand.NotifyCanExecuteChanged();
        BackCommand.NotifyCanExecuteChanged();
    }
}
