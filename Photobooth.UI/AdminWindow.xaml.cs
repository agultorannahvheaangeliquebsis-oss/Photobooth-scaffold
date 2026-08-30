using System.Windows;
using System.Windows.Controls;
using Photobooth.Core;
using Photobooth.Data;

namespace Photobooth.UI;

/// <summary>
/// Dashboard over AdminDashboardRepository (sessions today, revenue by
/// mode, low-inventory alerts -- read-only) plus an editable Settings
/// section (countdown duration, Glam Booth mode -- backed by the
/// Location row's own columns) and a Frame library section (add/retire the
/// overlays guests can pick during FramePicker). Reached from MainWindow
/// via F12 (see Window_KeyDown there), never from the guest-facing surface.
/// </summary>
public partial class AdminWindow : Window
{
    private readonly AdminDashboardRepository _repository = new();
    private readonly LocationRepository _locations = new();
    private readonly FrameRepository _frames = new();

    // "One booth machine has one location" -- same assumption
    // DatabaseInitializer's own seeding already makes -- so Settings and the
    // Frame library always target the first (only) Location row rather than
    // needing one passed in.
    private int _locationId;

    private string? _pendingFrameImagePath;

    public AdminWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) => await LoadAsync();
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e) => await LoadAsync();

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private async void SaveSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(CountdownSecondsBox.Text, out int countdownSeconds) || countdownSeconds <= 0)
        {
            SettingsStatusText.Text = "Countdown must be a whole number of seconds greater than 0.";
            SettingsStatusText.Foreground = System.Windows.Media.Brushes.Firebrick;
            return;
        }

        string layout = StripLayoutRadio.IsChecked == true ? "Strip" : "Single";
        if (!double.TryParse(PrintWidthBox.Text, out double widthInches)
            || !double.TryParse(PrintHeightBox.Text, out double heightInches)
            || !int.TryParse(StripCopiesBox.Text, out int stripCopies))
        {
            SettingsStatusText.Text = "Print width/height and strip copies must be numbers.";
            SettingsStatusText.Foreground = System.Windows.Media.Brushes.Firebrick;
            return;
        }

        var printTemplate = new PrintTemplate(layout, widthInches, heightInches, stripCopies);
        if (!printTemplate.IsValid)
        {
            SettingsStatusText.Text = "Print width/height must be greater than 0 and strip copies at least 1.";
            SettingsStatusText.Foreground = System.Windows.Media.Brushes.Firebrick;
            return;
        }

        SaveSettingsButton.IsEnabled = false;
        try
        {
            await _locations.UpdateSettingsAsync(_locationId, countdownSeconds, GlamFilterCheckBox.IsChecked == true, printTemplate);
            SettingsStatusText.Text = "Saved -- takes effect for the next guest session.";
            SettingsStatusText.Foreground = (System.Windows.Media.Brush)FindResource("MutedBrush");
        }
        catch (Exception ex)
        {
            SettingsStatusText.Text = $"Couldn't save: {ex.Message}";
            SettingsStatusText.Foreground = System.Windows.Media.Brushes.Firebrick;
        }
        finally
        {
            SaveSettingsButton.IsEnabled = true;
        }
    }

    private async Task LoadAsync()
    {
        RefreshButton.IsEnabled = false;
        try
        {
            int sessionsToday = await _repository.GetSessionsTodayCountAsync();
            SessionsTodayText.Text = sessionsToday.ToString();

            var revenue = await _repository.GetRevenueByModeAsync();
            RevenueList.ItemsSource = revenue.Select(r => $"{r.Mode}: {r.Revenue:C}").ToList();
            RevenueEmptyText.Visibility = revenue.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            FeedbackSummary feedbackSummary = await _repository.GetFeedbackSummaryAsync();
            FeedbackAverageText.Text = feedbackSummary.RatingCount == 0
                ? "No ratings yet."
                : $"{feedbackSummary.AverageRating:0.0} / 5 ({feedbackSummary.RatingCount} rating{(feedbackSummary.RatingCount == 1 ? "" : "s")})";

            var recentComments = await _repository.GetRecentCommentsAsync();
            FeedbackCommentsList.ItemsSource = recentComments
                .Select(c => $"“{c.Comment}” — session {c.SessionId}")
                .ToList();

            var alerts = await _repository.GetLowInventoryAlertsAsync();
            InventoryList.ItemsSource = alerts
                .Select(a => $"{a.Model} -- {a.ItemType}: {a.QuantityRemaining} remaining")
                .ToList();
            InventoryEmptyText.Visibility = alerts.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            var locations = await _locations.GetAllAsync();
            if (locations.Count > 0)
            {
                _locationId = locations[0].LocationId;
                CountdownSecondsBox.Text = locations[0].CountdownSeconds.ToString();
                GlamFilterCheckBox.IsChecked = locations[0].GlamFilterEnabled;

                PrintTemplate printTemplate = locations[0].PrintTemplate;
                SingleLayoutRadio.IsChecked = printTemplate.Layout != "Strip";
                StripLayoutRadio.IsChecked = printTemplate.Layout == "Strip";
                PrintWidthBox.Text = printTemplate.WidthInches.ToString();
                PrintHeightBox.Text = printTemplate.HeightInches.ToString();
                StripCopiesBox.Text = printTemplate.StripCopies.ToString();

                await LoadFramesAsync();
            }
        }
        catch (Exception ex)
        {
            SessionsTodayText.Text = $"Couldn't load: {ex.Message}";
        }
        finally
        {
            RefreshButton.IsEnabled = true;
        }
    }

    private async Task LoadFramesAsync()
    {
        var frames = await _frames.GetAllByLocationAsync(_locationId);
        FramesList.ItemsSource = frames;
        FramesEmptyText.Visibility = frames.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void BrowseFrameImageButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Image files (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg",
            Title = "Choose a frame overlay image",
        };
        if (dialog.ShowDialog() == true)
        {
            _pendingFrameImagePath = dialog.FileName;
            SelectedFrameImageText.Text = System.IO.Path.GetFileName(dialog.FileName);
        }
    }

    /// <summary>Copies the chosen image into a local Assets/Frames folder (same
    /// "own local copy, not a reference to wherever the admin picked it from"
    /// reasoning MockCameraService's captures/ folder already uses) and inserts
    /// the Frame row.</summary>
    private async void AddFrameButton_Click(object sender, RoutedEventArgs e)
    {
        string name = NewFrameNameBox.Text.Trim();
        if (name.Length == 0 || _pendingFrameImagePath is null)
        {
            FrameStatusText.Text = "Enter a name and choose an image first.";
            FrameStatusText.Foreground = System.Windows.Media.Brushes.Firebrick;
            return;
        }

        AddFrameButton.IsEnabled = false;
        try
        {
            string framesDirectory = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Frames");
            System.IO.Directory.CreateDirectory(framesDirectory);
            string storedFileName = $"{Guid.NewGuid():N}{System.IO.Path.GetExtension(_pendingFrameImagePath)}";
            string storedPath = System.IO.Path.Combine(framesDirectory, storedFileName);
            System.IO.File.Copy(_pendingFrameImagePath, storedPath, overwrite: true);

            var existing = await _frames.GetAllByLocationAsync(_locationId);
            await _frames.InsertAsync(_locationId, name, storedPath, sortOrder: existing.Count);

            NewFrameNameBox.Text = string.Empty;
            _pendingFrameImagePath = null;
            SelectedFrameImageText.Text = "No image selected.";
            FrameStatusText.Text = "Frame added -- available to the next guest.";
            FrameStatusText.Foreground = (System.Windows.Media.Brush)FindResource("MutedBrush");
            await LoadFramesAsync();
        }
        catch (Exception ex)
        {
            FrameStatusText.Text = $"Couldn't add frame: {ex.Message}";
            FrameStatusText.Foreground = System.Windows.Media.Brushes.Firebrick;
        }
        finally
        {
            AddFrameButton.IsEnabled = true;
        }
    }

    private async void FrameActiveCheckBox_Click(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { Tag: int frameId } checkBox)
        {
            await _frames.SetActiveAsync(frameId, checkBox.IsChecked == true);
            await LoadFramesAsync();
        }
    }

    private async void DeleteFrameButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: int frameId })
        {
            await _frames.DeleteAsync(frameId);
            await LoadFramesAsync();
        }
    }
}
