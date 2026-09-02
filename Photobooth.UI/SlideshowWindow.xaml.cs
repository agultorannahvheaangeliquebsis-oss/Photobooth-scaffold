using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Photobooth.Core;

namespace Photobooth.UI;

/// <summary>
/// Real Slideshow implementation (see AdminWindow's Slideshow section) --
/// cycles this event's captured photos full-screen with a real crossfade,
/// launched as its own top-level window so an admin can drag it onto a
/// second monitor and keep working in AdminWindow/KioskWindow. Reads the
/// image list once at open (not live-watched) -- a slideshow already
/// re-launches per event, and watching the captures folder for new files
/// mid-slideshow is more than this pass attempts (see Transition/
/// ShowQrOverlay's own doc comments on SlideshowSettings for the same
/// "real but scoped down" status this window's own Fade-only transition has).
/// </summary>
public partial class SlideshowWindow : Window
{
    private static readonly string[] ImageExtensions = { ".jpg", ".jpeg", ".png", ".bmp", ".gif" };

    private readonly List<string> _imagePaths;
    private readonly DispatcherTimer _advanceTimer;
    private int _currentIndex = -1;
    private bool _primaryIsFront = true;

    public SlideshowWindow(string capturesDirectory, string eventName, string? logoImagePath, SlideshowSettings settings)
    {
        InitializeComponent();

        EventNameText.Text = eventName;
        if (settings.ShowLogoOverlay && logoImagePath is not null && File.Exists(logoImagePath))
        {
            LogoOverlayImage.Source = new BitmapImage(new Uri(logoImagePath));
            LogoOverlayImage.Visibility = Visibility.Visible;
        }

        _imagePaths = Directory.Exists(capturesDirectory)
            ? Directory.EnumerateFiles(capturesDirectory)
                .Where(path => ImageExtensions.Contains(Path.GetExtension(path).ToLowerInvariant()))
                .OrderBy(path => path)
                .ToList()
            : new List<string>();

        if (_imagePaths.Count == 0)
        {
            EmptyStateText.Visibility = Visibility.Visible;
        }
        else
        {
            ShowNextImage();
        }

        int intervalSeconds = settings.IntervalSeconds > 0 ? settings.IntervalSeconds : SlideshowSettings.Default.IntervalSeconds;
        _advanceTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(intervalSeconds) };
        _advanceTimer.Tick += (_, _) => ShowNextImage();
        if (_imagePaths.Count > 1)
        {
            _advanceTimer.Start();
        }
    }

    /// <summary>Crossfades from whichever Image is currently on top to the
    /// other one, loaded with the next photo -- a 600ms fade, same
    /// reasoning KioskWindow's own flash-wash duration gives for picking a
    /// duration that reads clearly without dragging on.</summary>
    private void ShowNextImage()
    {
        if (_imagePaths.Count == 0)
        {
            return;
        }

        _currentIndex = (_currentIndex + 1) % _imagePaths.Count;
        System.Windows.Controls.Image incoming = _primaryIsFront ? SecondaryImage : PrimaryImage;
        System.Windows.Controls.Image outgoing = _primaryIsFront ? PrimaryImage : SecondaryImage;

        try
        {
            incoming.Source = new BitmapImage(new Uri(_imagePaths[_currentIndex]));
        }
        catch (Exception)
        {
            // A file that failed to decode (still being written, corrupt,
            // etc.) just gets skipped on the next tick rather than crashing
            // the slideshow.
            return;
        }

        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(600));
        var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(600));
        incoming.BeginAnimation(OpacityProperty, fadeIn);
        outgoing.BeginAnimation(OpacityProperty, fadeOut);

        _primaryIsFront = !_primaryIsFront;
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _advanceTimer.Stop();
        base.OnClosed(e);
    }
}
