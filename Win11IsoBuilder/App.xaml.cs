using System.Windows;
using Win11IsoBuilder.Services;

namespace Win11IsoBuilder;

/// <summary>
/// App entry point. With no arguments it shows the wizard; with <c>--build ...</c> it runs
/// the pipeline headlessly (for automation / smoke tests) and exits with 0 on success.
/// </summary>
public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (e.Args.Length > 0 && e.Args[0].Equals("--build", StringComparison.OrdinalIgnoreCase))
        {
            var exitCode = await HeadlessBuildRunner.RunAsync(e.Args);
            Shutdown(exitCode);
            return;
        }

        new MainWindow().Show();
    }
}
