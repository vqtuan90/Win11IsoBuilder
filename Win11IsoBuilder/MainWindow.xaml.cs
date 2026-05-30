using System.Windows;
using Win11IsoBuilder.ViewModels;

namespace Win11IsoBuilder;

/// <summary>Interaction logic for MainWindow.xaml — hosts the wizard shell view model.</summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
    }
}
