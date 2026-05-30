using CommunityToolkit.Mvvm.ComponentModel;
using Win11IsoBuilder.Models;

namespace Win11IsoBuilder.ViewModels;

/// <summary>
/// Base for each wizard page. Holds the shared <see cref="BuildConfig"/> and exposes a
/// title plus a <see cref="CanProceed"/> gate the shell uses to enable/disable Next.
/// </summary>
public abstract partial class WizardStepViewModel : ObservableObject
{
    protected WizardStepViewModel(BuildConfig config, string title)
    {
        Config = config;
        Title = title;
    }

    public BuildConfig Config { get; }
    public string Title { get; }

    /// <summary>Whether the step's required input is satisfied (gates the Next button).</summary>
    public virtual bool CanProceed => true;

    /// <summary>Called when the step becomes active (e.g. to lazy-load data).</summary>
    public virtual void OnEntered() { }

    /// <summary>Re-evaluate <see cref="CanProceed"/> from derived VMs after input changes.</summary>
    protected void RaiseCanProceedChanged() => OnPropertyChanged(nameof(CanProceed));
}
