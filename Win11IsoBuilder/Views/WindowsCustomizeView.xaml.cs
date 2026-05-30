using System.Windows;
using System.Windows.Controls;
using Win11IsoBuilder.ViewModels;

namespace Win11IsoBuilder.Views;

public partial class WindowsCustomizeView : UserControl
{
    public WindowsCustomizeView() => InitializeComponent();

    // PasswordBox.Password is not bindable for security; push it into the config manually.
    private void OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is WindowsCustomizeViewModel vm && sender is PasswordBox box)
            vm.Config.Windows.LocalPassword = box.Password;
    }
}
