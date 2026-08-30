using System.Windows;
using Photobooth.Data;

namespace Photobooth.UI;

/// <summary>
/// Dashboard over AdminDashboardRepository (sessions today, revenue by
/// mode, low-inventory alerts -- read-only) plus an editable Settings
/// section (countdown duration, Glam Booth mode -- backed by the
/// Location row's own columns). Reached from MainWindow via F12 (see
/// Window_KeyDown there), never from the guest-facing surface.
/// </summary>
public partial class AdminWindow : Window
{
    private readonly AdminDashboardRepository _repository = new();
    private readonly LocationRepository _locations = new();

    // "One booth machine has one location" -- same assumption
    // DatabaseInitializer's own seeding already makes -- so Settings always
    // edits the first (only) Location row rather than needing one passed in.
    private int _locationId;

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

        SaveSettingsButton.IsEnabled = false;
        try
        {
            await _locations.UpdateSettingsAsync(_locationId, countdownSeconds, GlamFilterCheckBox.IsChecked == true);
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
}
