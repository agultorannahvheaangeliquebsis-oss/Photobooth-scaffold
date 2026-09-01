using System.Windows;

namespace Photobooth.UI;

public partial class App : Application
{
    /// <summary>Replaces App.xaml's old StartupUri="MainWindow.xaml": shows
    /// EventLauncherWindow ("Your events") first, same as dslrBooth's own
    /// startup flow -- DB init, camera bridge, and real KioskViewModel
    /// construction now happen per-launch inside that window instead of once
    /// here, since which event's services to build depends on what the admin
    /// picks there (see EventLauncherWindow.LaunchSelectedAsync).</summary>
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var window = new EventLauncherWindow();
        MainWindow = window;
        window.Show();
    }
}
