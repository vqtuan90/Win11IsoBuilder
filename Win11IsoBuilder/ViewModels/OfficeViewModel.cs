using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Win11IsoBuilder.Models;

namespace Win11IsoBuilder.ViewModels;

/// <summary>An Office app the user can exclude from the install.</summary>
public partial class ExcludableApp : ObservableObject
{
    [ObservableProperty] private bool _isExcluded;
    public string Id { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
}

/// <summary>
/// Step 3: Microsoft 365 Apps. Product/lang/bitness are fixed per the locked decision
/// (M365 Business, en-us, x64). The user toggles inclusion and which apps to exclude.
/// </summary>
public partial class OfficeViewModel : WizardStepViewModel
{
    public OfficeViewModel(BuildConfig config) : base(config, "3. Office")
    {
        foreach (var (id, name) in Excludable)
        {
            var item = new ExcludableApp { Id = id, DisplayName = name };
            item.PropertyChanged += OnExclusionToggled;
            ExcludableApps.Add(item);
        }
    }

    public ObservableCollection<ExcludableApp> ExcludableApps { get; } = new();

    public bool OfficeEnabled
    {
        get => Config.Office.Enabled;
        set { Config.Office.Enabled = value; OnPropertyChanged(); }
    }

    private void OnExclusionToggled(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ExcludableApp.IsExcluded)) return;
        Config.Office.ExcludedApps = ExcludableApps.Where(a => a.IsExcluded).Select(a => a.Id).ToList();
    }

    private static readonly (string Id, string Name)[] Excludable =
    {
        ("Access", "Access"),
        ("Publisher", "Publisher"),
        ("Lync", "Skype for Business"),
        ("Groove", "OneDrive (Groove)"),
        ("OneDrive", "OneDrive Desktop"),
        ("Teams", "Teams"),
        ("Outlook", "Outlook"),
        ("OneNote", "OneNote"),
        ("Bing", "Bing Search add-in"),
    };
}
