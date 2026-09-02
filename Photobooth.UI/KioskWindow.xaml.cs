using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Photobooth.Core;
using Photobooth.UI.ViewModels;

namespace Photobooth.UI;

/// <summary>
/// The dslrBooth-style guest-facing kiosk shell, and (as of the "converge
/// into KioskWindow" decision) the only guest-facing window this app has --
/// MainWindow was retired once every screen it had was ported here. Almost
/// everything on screen is driven by <see cref="KioskViewModel"/> through
/// bindings; this file holds what genuinely belongs to the window itself:
/// choosing a ViewModel, hiding the cursor, disposing timers on close, and
/// the two pieces of chrome with no clean ViewModel abstraction --
/// Visual Screen Editor overlay rendering (raw FrameworkElement construction)
/// and Virtual Attendant clip playback (a MediaElement-specific call) -- same
/// reasoning MainWindow kept those in code-behind too.
/// </summary>
public partial class KioskWindow : Window
{
    private readonly KioskViewModel _viewModel;

    /// <summary>Runs against Photobooth.Core's Mock* services -- no camera
    /// bridge, printer, LocalDB or Cloudinary key required. This is the
    /// constructor a bare `new KioskWindow()` uses (e.g. the XAML designer);
    /// App.xaml.cs's real startup path always passes a real ViewModel via
    /// the other constructor instead -- see BoothCompositionRoot.BuildKioskViewModel.</summary>
    public KioskWindow() : this(KioskViewModel.CreateWithMockServices())
    {
    }

    /// <summary>Runs against whatever services are passed in. Compose real ones
    /// via <see cref="BoothCompositionRoot.BuildKioskViewModel"/> and hand the
    /// ViewModel here.</summary>
    public KioskWindow(KioskViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = _viewModel;

        // Set here rather than passed into KioskViewModel's constructor: the
        // ViewModel is built before this Window exists, so "this" can't be
        // captured any earlier. Same PIN trust level MainWindow's F12/Setup
        // "Open Settings" button used -- no second PIN check, since reaching
        // the admin overlay already required one.
        _viewModel.Admin.OpenFullSettings = () =>
            new AdminWindow(_viewModel.LocationId, onLockChanged: locked => _viewModel.IsAdminLocked = locked) { Owner = this }.ShowDialog();

        _viewModel.ScreenOverlaysChanged += () => Dispatcher.Invoke(RenderAllScreenOverlays);
        _viewModel.AttendantCueRequested += clip => Dispatcher.Invoke(() => PlayAttendantCue(clip));

        // Canvas.ActualWidth/Height are 0 until the first layout pass -- these
        // re-render once real dimensions are known (and on any later resize),
        // same reasoning MainWindow's identical wiring gave.
        WelcomeOverlayCanvas.SizeChanged += (_, _) => RenderScreenOverlay(WelcomeOverlayCanvas, ScreenTemplateScreen.Welcome);
        CaptureOverlayCanvas.SizeChanged += (_, _) => RenderScreenOverlay(CaptureOverlayCanvas, ScreenTemplateScreen.Capture);
        SharingOverlayCanvas.SizeChanged += (_, _) => RenderScreenOverlay(SharingOverlayCanvas, ScreenTemplateScreen.Sharing);

        PreviewKeyDown += KioskWindow_PreviewKeyDown;
    }

    /// <summary>ScreenSettings.SessionTriggerF13/SessionTriggerKeys -- a
    /// touch-only booth's Idle screen tap target (IsTouchStartEnabled) is one
    /// session trigger; this is the other, for a booth with an attendant
    /// keyboard/foot-pedal-as-F13 nearby. Each key still funnels through
    /// KioskViewModel.TryStartSessionFromKey, which gates by its own toggle
    /// and by CanStartSession (via StartSession), so this handler doesn't
    /// need to duplicate either check.</summary>
    private void KioskWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.F13:
                _viewModel.TryStartSessionFromKey(isF13: true);
                e.Handled = true;
                break;
            case Key.Space or Key.S or Key.PageUp or Key.PageDown:
                _viewModel.TryStartSessionFromKey(isF13: false);
                e.Handled = true;
                break;
        }
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

    private void RenderAllScreenOverlays()
    {
        RenderScreenOverlay(WelcomeOverlayCanvas, ScreenTemplateScreen.Welcome);
        RenderScreenOverlay(CaptureOverlayCanvas, ScreenTemplateScreen.Capture);
        RenderScreenOverlay(SharingOverlayCanvas, ScreenTemplateScreen.Sharing);
    }

    /// <summary>Redraws one screen's ScreenTemplateElement rows as live WPF
    /// elements positioned by percent of the canvas's own ActualWidth/
    /// ActualHeight -- ported verbatim from MainWindow's RenderScreenOverlay,
    /// the guest-facing equivalent of PrintCompositor's percent-of-cell
    /// overlay math, just rendered directly as TextBlock/Image/Rectangle
    /// instead of composited onto a bitmap, since this is a live interactive
    /// screen, not a print.</summary>
    private void RenderScreenOverlay(Canvas canvas, ScreenTemplateScreen screen)
    {
        canvas.Children.Clear();
        double width = canvas.ActualWidth;
        double height = canvas.ActualHeight;
        if (width <= 0 || height <= 0)
        {
            return; // not yet laid out -- SizeChanged (see constructor) re-renders once it is
        }

        foreach (ScreenTemplateElement element in _viewModel.GetOverlayElements(screen))
        {
            FrameworkElement content = element.Kind switch
            {
                ScreenTemplateElementKind.Text => new TextBlock
                {
                    Text = element.Text,
                    FontFamily = new FontFamily(element.FontFamily),
                    FontSize = Math.Max(1, element.FontSizePercent * height),
                    FontWeight = element.Bold ? FontWeights.Bold : FontWeights.Normal,
                    Foreground = HexToBrush(element.ColorHex),
                    TextAlignment = TextAlignment.Center,
                    TextWrapping = TextWrapping.Wrap,
                },
                ScreenTemplateElementKind.Image => new Image
                {
                    Source = element.ImagePath is string path && System.IO.File.Exists(path)
                        ? new BitmapImage(new Uri(System.IO.Path.GetFullPath(path)))
                        : null,
                    Stretch = Stretch.Uniform,
                },
                _ => (FrameworkElement)new System.Windows.Shapes.Rectangle { Fill = HexToBrush(element.ColorHex) },
            };

            content.Width = element.WidthPercent * width;
            content.Height = element.HeightPercent * height;
            Canvas.SetLeft(content, element.XPercent * width);
            Canvas.SetTop(content, element.YPercent * height);
            canvas.Children.Add(content);
        }
    }

    private static SolidColorBrush HexToBrush(string hex)
    {
        try
        {
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        }
        catch (Exception)
        {
            return Brushes.Black;
        }
    }

    /// <summary>Plays a Virtual Attendant clip alongside whatever screen is
    /// already showing -- best-effort, same reasoning BoothStateMachine's
    /// FireAttendantCueAsync already swallows lookup failures for: a
    /// missing/bad file here shouldn't crash or interrupt the guest session
    /// either. Ported verbatim from MainWindow's PlayAttendantCue.</summary>
    private void PlayAttendantCue(AttendantClip clip)
    {
        try
        {
            if (!System.IO.File.Exists(clip.FilePath))
            {
                return;
            }

            AttendantMediaElement.Source = new Uri(System.IO.Path.GetFullPath(clip.FilePath));
            AttendantMediaElement.Play();
        }
        catch (Exception)
        {
            // Best-effort: a bad clip path/format shouldn't disrupt the session.
        }
    }
}
