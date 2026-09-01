using System.IO;
using System.Windows;
using System.Windows.Threading;
using Serilog;

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

        ConfigureLogging();

        // Without this, an unhandled exception on the UI thread (confirmed via
        // Windows Event Log: e.g. a stray Show() on an already-closed window
        // during kiosk teardown) takes the whole process down with no dialog --
        // it just vanishes mid-session, which reads as "stuck" rather than
        // "crashed" since there's no error to explain what happened. Logged
        // here (not just shown) so a field issue can be diagnosed from
        // %LocalAppData%\Photobooth\logs afterward, without needing to catch
        // it live or attach a debugger.
        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error(args.Exception, "Unhandled UI-thread exception");
            MessageBox.Show(
                $"Something went wrong and this action couldn't finish.\n\n{args.Exception.Message}",
                "Focus & Snap -- unexpected error", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        // Belt-and-suspenders for the two exception paths DispatcherUnhandledException
        // doesn't cover: a non-UI-thread exception (e.g. inside Task.Run) and a
        // faulted Task nobody awaited/observed. Neither can be recovered from
        // here -- by the time AppDomain.UnhandledException fires the process is
        // already going down -- but logging them means a crash still leaves a
        // trail instead of just vanishing from the guest's perspective.
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log.Fatal(args.ExceptionObject as Exception, "Unhandled non-UI-thread exception (terminating: {IsTerminating})", args.IsTerminating);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error(args.Exception, "Unobserved task exception");
            args.SetObserved();
        };

        var window = new EventLauncherWindow();
        MainWindow = window;
        window.Show();
    }

    /// <summary>Rolling daily log file under the current user's AppData, so an
    /// attendant/dev can pull yesterday's log off a booth machine after an
    /// incident without having attached a debugger or console at the time.
    /// 14-day retention keeps a season's worth of history without growing
    /// unbounded on a machine nobody manually cleans up.</summary>
    private static void ConfigureLogging()
    {
        string logDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Photobooth", "logs");

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .Enrich.FromLogContext()
            .WriteTo.File(
                Path.Combine(logDirectory, "photobooth-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                shared: true)
            .CreateLogger();

        Log.Information("Photobooth starting up");

        Current.Exit += (_, _) =>
        {
            Log.Information("Photobooth shutting down");
            Log.CloseAndFlush();
        };
    }
}
