using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Win11IsoBuilder.Models;

namespace Win11IsoBuilder.ViewModels;

/// <summary>A single bloatware checkbox (curated Win11 provisioned-appx family).</summary>
public partial class BloatwareItem : ObservableObject
{
    [ObservableProperty] private bool _isSelected;
    public string DisplayName { get; init; } = string.Empty;
    /// <summary>PackageName prefix matched at build time against the live appx list.</summary>
    public string Pattern { get; init; } = string.Empty;
}

/// <summary>
/// Step 2: Windows tweaks. Text/toggle inputs bind directly to <c>Config.Windows</c>;
/// the bloatware checklist is mirrored into <c>Config.Windows.AppxToRemove</c> as patterns.
/// </summary>
public partial class WindowsCustomizeViewModel : WizardStepViewModel
{
    public WindowsCustomizeViewModel(BuildConfig config) : base(config, "2. Windows")
    {
        foreach (var (name, pattern) in DefaultBloatware)
        {
            var item = new BloatwareItem { DisplayName = name, Pattern = pattern };
            item.PropertyChanged += OnBloatwareToggled;
            Bloatware.Add(item);
        }
    }

    public ObservableCollection<BloatwareItem> Bloatware { get; } = new();
    public Array ComputerNameModes => Enum.GetValues(typeof(ComputerNameMode));

    public override bool CanProceed => !string.IsNullOrWhiteSpace(Config.Windows.LocalUsername);

    private void OnBloatwareToggled(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(BloatwareItem.IsSelected)) return;
        Config.Windows.AppxToRemove = Bloatware.Where(b => b.IsSelected).Select(b => b.Pattern).ToList();
    }

    // Common Win11 consumer bloatware. Pattern is the provisioned PackageName prefix.
    private static readonly (string Name, string Pattern)[] DefaultBloatware =
    {
        ("News", "Microsoft.BingNews"),
        ("Weather", "Microsoft.BingWeather"),
        ("Xbox Gaming App", "Microsoft.GamingApp"),
        ("Xbox Game Overlay", "Microsoft.XboxGamingOverlay"),
        ("Solitaire Collection", "Microsoft.MicrosoftSolitaireCollection"),
        ("Media Player (Zune Music)", "Microsoft.ZuneMusic"),
        ("Films & TV (Zune Video)", "Microsoft.ZuneVideo"),
        ("Feedback Hub", "Microsoft.WindowsFeedbackHub"),
        ("Get Help", "Microsoft.GetHelp"),
        ("Tips (Get Started)", "Microsoft.Getstarted"),
        ("To Do", "Microsoft.Todos"),
        ("Power Automate", "Microsoft.PowerAutomateDesktop"),
        ("Clipchamp", "Clipchamp.Clipchamp"),
        ("People", "Microsoft.People"),
        ("Mail & Calendar", "microsoft.windowscommunicationsapps"),
    };
}
