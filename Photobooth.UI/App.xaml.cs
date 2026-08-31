using System.Diagnostics;
using System.Windows;

namespace Photobooth.UI;

public partial class App : Application
{
    private Process? _cameraBridgeProcess;

    /// <summary>Replaces App.xaml's old StartupUri="MainWindow.xaml": building a
    /// real KioskViewModel needs BoothCompositionRoot.Build() to run first (DB
    /// init, camera bridge, real services) and its failure handling shown
    /// before any window exists, which a declarative StartupUri can't express
    /// -- this is where MainWindow's constructor used to do exactly that.</summary>
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // ProcessExit rather than a window's Closing: the catch blocks below
        // call Environment.Exit(1) directly on failure, which skips Closing
        // entirely -- same reasoning MainWindow's own comment gave for this.
        AppDomain.CurrentDomain.ProcessExit += KillCameraBridgeIfOwned;

        ViewModels.KioskViewModel viewModel;
        BoothCompositionRoot.RealBooth booth;
        try
        {
            (viewModel, booth) = BoothCompositionRoot.BuildKioskViewModel();
        }
        catch (BoothCompositionRoot.DatabaseUnavailableException ex)
        {
            MessageBox.Show(
                $"Couldn't reach the booth database and can't start.\n\n{ex.Message}\n\n" +
                "Check that SQL Server LocalDB is installed and the MSSQLLocalDB instance is running, then restart the app.",
                "Focus & Snap -- startup failed", MessageBoxButton.OK, MessageBoxImage.Error);
            Environment.Exit(1);
            return;
        }
        catch (Exception ex)
        {
            // Service constructors can throw synchronously (e.g.
            // CloudinaryCloudUploadService requires CLOUDINARY_URL to be
            // set) -- same reasoning as the DB catch above.
            MessageBox.Show(
                $"Couldn't start the booth services.\n\n{ex.Message}",
                "Focus & Snap -- startup failed", MessageBoxButton.OK, MessageBoxImage.Error);
            Environment.Exit(1);
            return;
        }

        _cameraBridgeProcess = booth.CameraBridgeProcess;

        var window = new KioskWindow(viewModel);
        MainWindow = window;
        window.Show();
    }

    // Only tear down the bridge process if this app instance is the one who
    // launched it -- if it was already running (started manually, or by a
    // prior instance of this app), leave it for whatever's still using it.
    // Same as MainWindow's former KillCameraBridgeIfOwned.
    private void KillCameraBridgeIfOwned(object? sender, EventArgs e)
    {
        if (_cameraBridgeProcess is { HasExited: false } process)
        {
            try { process.Kill(); } catch { /* already gone */ }
        }
    }
}
