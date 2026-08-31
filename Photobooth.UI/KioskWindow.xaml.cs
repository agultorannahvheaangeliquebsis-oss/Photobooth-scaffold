using System.Windows;
using System.Windows.Input;
using Photobooth.Core;
using Photobooth.UI.ViewModels;

namespace Photobooth.UI;

/// <summary>
/// The dslrBooth-style guest-facing kiosk shell. Everything on screen is
/// driven by <see cref="KioskViewModel"/> through bindings -- this file
/// deliberately holds only what genuinely belongs to the window: choosing a
/// ViewModel, hiding the cursor, and disposing timers on close.
///
/// This is a second, self-contained shell alongside the existing MainWindow,
/// not a replacement for it: MainWindow still owns the interactive consent,
/// frame-picker, guestbook, feedback and survey screens that this five-screen
/// kiosk flow doesn't cover. To run this one instead, point App.xaml's
/// StartupUri at KioskWindow.xaml.
/// </summary>
public partial class KioskWindow : Window
{
    private readonly KioskViewModel _viewModel;

    /// <summary>Runs against Photobooth.Core's Mock* services -- no camera
    /// bridge, printer, LocalDB or Cloudinary key required. This is the
    /// constructor StartupUri uses, so the shell is runnable as-is.</summary>
    public KioskWindow() : this(KioskViewModel.CreateWithMockServices())
    {
    }

    /// <summary>Runs against whatever services are passed in. Compose real ones
    /// the way MainWindow's constructor does and hand the ViewModel here --
    /// see <see cref="KioskViewModel.CreateWithMockServices"/> for the two
    /// services that must stay mocked for this shell.</summary>
    public KioskWindow(KioskViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = _viewModel;
    }

    /// <summary>
    /// Hides the mouse pointer over the whole window. A touchscreen kiosk has no
    /// mouse, but Windows still parks an arrow wherever the pointer last was,
    /// and it shows up in every photo of the booth. Set here rather than in XAML
    /// so it stays with the other kiosk-hardware concerns.
    /// </summary>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        Cursor = Cursors.None;
    }

    /// <summary>
    /// Alt+F4 and the like are still available to staff -- this only stops the
    /// timers and releases the camera's live view mode so the app doesn't leave
    /// the bridge holding the sensor after the window is gone.
    /// </summary>
    protected override void OnClosed(EventArgs e)
    {
        _viewModel.Dispose();
        base.OnClosed(e);
    }
}
