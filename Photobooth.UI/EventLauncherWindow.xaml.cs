using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Photobooth.Data;
using Photobooth.UI.ViewModels;

namespace Photobooth.UI;

/// <summary>
/// The app's front door -- "Your events": pick a saved event/location to
/// launch into KioskWindow, or create/duplicate/rename/delete one. Mirrors
/// dslrBooth's own event-list launcher (see the reference screenshots this
/// window was built against). "Event" here is exactly a <see cref="Photobooth.Data.LocationRecord"/>
/// row -- this codebase already modeled one booth deployment's full settings
/// (theme, capture mode, screens, sharing, etc.) as a Location, which is
/// dslrBooth's own "Event" concept in every way that matters, so this adds a
/// picker/CRUD UI over the existing table rather than a new one.
///
/// Replaces App.xaml.cs's old direct-to-KioskWindow startup: DB init and its
/// failure handling now happen here (on Loaded, before any event list can be
/// shown), and building the real KioskViewModel happens per-launch instead of
/// once at process start, since which event's services to build depends on
/// what the admin picks here.
/// </summary>
public partial class EventLauncherWindow : Window
{
    private readonly LocationRepository _locations = new();
    private List<LocationRecord> _allEvents = new();
    private int? _selectedLocationId;

    // Owns the camera bridge process across however many kiosk launches happen
    // from this window in one app run -- only the launch that actually started
    // it should kill it (see LaunchSelectedAsync), same ownership reasoning
    // App.xaml.cs's original KillCameraBridgeIfOwned had.
    private Process? _cameraBridgeProcess;

    // Guards kiosk.Closed's Show()/Activate() below against firing after this
    // window itself has already closed (e.g. the whole app shutting down
    // while a kiosk session is still open) -- confirmed via a crash log
    // (System.InvalidOperationException: "Cannot ... call Show ... after a
    // Window has closed", thrown from this exact handler) that this race is
    // real, not hypothetical, and previously took the whole process down
    // with it since nothing upstream catches it.
    private bool _closed;

    public EventLauncherWindow()
    {
        InitializeComponent();
        AppDomain.CurrentDomain.ProcessExit += KillCameraBridgeIfOwned;
        Closed += (_, _) => _closed = true;
        Loaded += async (_, _) => await LoadAsync();
    }

    private void KillCameraBridgeIfOwned(object? sender, EventArgs e)
    {
        if (_cameraBridgeProcess is { HasExited: false } process)
        {
            try { process.Kill(); } catch { /* already gone */ }
        }
    }

    private async Task LoadAsync()
    {
        try
        {
            await DatabaseInitializer.InitializeAsync();
        }
        catch (Exception ex)
        {
            Serilog.Log.Fatal(ex, "Database unavailable at startup");
            MessageBox.Show(
                $"Couldn't reach the booth database and can't start.\n\n{ex.Message}\n\n" +
                "Check that SQL Server LocalDB is installed and the MSSQLLocalDB instance is running, then restart the app.",
                "Focus & Snap -- startup failed", MessageBoxButton.OK, MessageBoxImage.Error);
            Environment.Exit(1);
            return;
        }

        await RefreshEventsAsync();
    }

    private async Task RefreshEventsAsync()
    {
        _allEvents = await _locations.GetAllAsync();
        RenderTiles();
    }

    // ============================================================ tiles ==

    private void RenderTiles()
    {
        string filter = SearchBox.Text.Trim();
        List<LocationRecord> visible = (filter.Length == 0
            ? _allEvents
            : _allEvents.Where(l => l.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList())
            .OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        TilesPanel.Children.Clear();
        foreach (LocationRecord location in visible)
        {
            TilesPanel.Children.Add(BuildTile(location));
        }

        EmptyStateText.Visibility = _allEvents.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        // The selection can go stale after a rename/delete/filter -- drop it
        // if it no longer points at a visible event rather than leaving the
        // preview panel/toolbar showing a now-wrong event.
        if (_selectedLocationId is int selectedId && visible.All(l => l.LocationId != selectedId))
        {
            SelectEvent(null);
        }
        else
        {
            UpdateTileHighlight();
        }
    }

    private Border BuildTile(LocationRecord location)
    {
        Brush accentBrush = HexToBrush(location.Theme.AccentColorHex);

        var swatch = new Border
        {
            Width = 152,
            Height = 96,
            CornerRadius = new CornerRadius(8),
            Background = accentBrush,
            Child = new TextBlock
            {
                Text = location.Name.Length > 0 ? location.Name[..1].ToUpperInvariant() : "?",
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 34,
                FontWeight = FontWeights.Bold,
                Foreground = (Brush)FindResource("OnAccentBrush"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };

        var nameText = new TextBlock
        {
            Text = location.Name,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("InkBrush"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(2, 8, 2, 0),
        };
        var captionText = new TextBlock
        {
            Text = $"{DescribeAge(location.CreatedAt)} • {(location.Type == "vendo" ? "Vendo" : "Event")}",
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 11,
            Foreground = (Brush)FindResource("MutedBrush"),
            Margin = new Thickness(2, 2, 2, 0),
        };

        var content = new StackPanel();
        content.Children.Add(swatch);
        content.Children.Add(nameText);
        content.Children.Add(captionText);

        var tile = new Border
        {
            Width = 168,
            Padding = new Thickness(8),
            Margin = new Thickness(0, 0, 14, 14),
            CornerRadius = new CornerRadius(10),
            Background = (Brush)FindResource("FieldBrush"),
            BorderBrush = (Brush)FindResource("LineBrush"),
            BorderThickness = new Thickness(2),
            Cursor = Cursors.Hand,
            Tag = location.LocationId,
            Child = content,
        };
        tile.MouseLeftButtonDown += (_, e) =>
        {
            SelectEvent(location.LocationId);
            if (e.ClickCount == 2)
            {
                _ = LaunchSelectedAsync();
            }
        };

        return tile;
    }

    private static string DescribeAge(DateTime createdAtUtc)
    {
        TimeSpan age = DateTime.UtcNow - createdAtUtc;
        if (age.TotalDays >= 30) return $"{(int)(age.TotalDays / 30)}mo ago";
        if (age.TotalDays >= 1) return $"{(int)age.TotalDays}d ago";
        if (age.TotalHours >= 1) return $"{(int)age.TotalHours}h ago";
        return "just now";
    }

    private static Brush HexToBrush(string hex)
    {
        try
        {
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        }
        catch (Exception)
        {
            return Brushes.Gray;
        }
    }

    private void UpdateTileHighlight()
    {
        foreach (Border tile in TilesPanel.Children.OfType<Border>())
        {
            bool isSelected = tile.Tag is int id && id == _selectedLocationId;
            tile.BorderBrush = isSelected ? (Brush)FindResource("AccentBrush") : (Brush)FindResource("LineBrush");
        }
    }

    // ============================================================ selection ==

    private void SelectEvent(int? locationId)
    {
        _selectedLocationId = locationId;
        UpdateTileHighlight();

        bool hasSelection = locationId is not null;
        LaunchEventButton.IsEnabled = hasSelection;
        DuplicateButton.IsEnabled = hasSelection;
        RenameButton.IsEnabled = hasSelection;
        DeleteButton.IsEnabled = hasSelection;

        LocationRecord? selected = _allEvents.FirstOrDefault(l => l.LocationId == locationId);
        if (selected is null)
        {
            PreviewSwatch.Background = (Brush)FindResource("LineBrush");
            PreviewInitialText.Text = string.Empty;
            PreviewNameText.Text = "Select an event";
            PreviewCaptionText.Text = string.Empty;
            PreviewActionsPanel.Visibility = Visibility.Collapsed;
            PreviewEmptyText.Visibility = Visibility.Visible;
            return;
        }

        PreviewSwatch.Background = HexToBrush(selected.Theme.AccentColorHex);
        PreviewInitialText.Text = selected.Name.Length > 0 ? selected.Name[..1].ToUpperInvariant() : "?";
        PreviewNameText.Text = selected.Name;
        PreviewCaptionText.Text = $"{(selected.Type == "vendo" ? "Vendo" : "Event")} • {DescribeAge(selected.CreatedAt)} • {selected.Capture.Mode}";
        PreviewActionsPanel.Visibility = Visibility.Visible;
        PreviewEmptyText.Visibility = Visibility.Collapsed;
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => RenderTiles();

    // ============================================================ toolbar ==

    private async void NewEventButton_Click(object sender, RoutedEventArgs e)
    {
        var prompt = new TextPromptWindow("Name this event", caption: "You can change this later.") { Owner = this };
        if (prompt.ShowDialog() != true)
        {
            return;
        }

        int newId = await _locations.InsertAsync(prompt.Value, "event", address: null);
        await RefreshEventsAsync();
        SelectEvent(newId);
    }

    private async void DuplicateButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedLocationId is not int sourceId)
        {
            return;
        }
        LocationRecord source = _allEvents.First(l => l.LocationId == sourceId);

        var prompt = new TextPromptWindow(
            "Name the duplicate", $"{source.Name} copy",
            "Copies this event's settings and branding. Frames, screen layouts, and survey questions start fresh.")
        { Owner = this };
        if (prompt.ShowDialog() != true)
        {
            return;
        }

        int newId = await _locations.DuplicateAsync(sourceId, prompt.Value);
        await RefreshEventsAsync();
        SelectEvent(newId);
    }

    private async void RenameButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedLocationId is not int locationId)
        {
            return;
        }
        LocationRecord source = _allEvents.First(l => l.LocationId == locationId);

        var prompt = new TextPromptWindow("Rename event", source.Name) { Owner = this };
        if (prompt.ShowDialog() != true)
        {
            return;
        }

        await _locations.RenameAsync(locationId, prompt.Value);
        await RefreshEventsAsync();
        SelectEvent(locationId);
    }

    private async void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedLocationId is not int locationId)
        {
            return;
        }
        LocationRecord source = _allEvents.First(l => l.LocationId == locationId);

        MessageBoxResult confirm = MessageBox.Show(
            $"Delete \"{source.Name}\"? This can't be undone.",
            "Focus & Snap", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            await _locations.DeleteAsync(locationId);
        }
        catch (Exception)
        {
            // No ON DELETE CASCADE in schema.sql -- a Session/Booking/etc. row
            // still pointing at this Location throws a FK-conflict SqlException.
            MessageBox.Show(
                $"Can't delete \"{source.Name}\" -- it still has recorded sessions or other activity.",
                "Focus & Snap", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        await RefreshEventsAsync();
    }

    // ============================================================ launch ==

    private async void LaunchEventButton_Click(object sender, RoutedEventArgs e) => await LaunchSelectedAsync();

    private async Task LaunchSelectedAsync()
    {
        if (_selectedLocationId is not int locationId)
        {
            return;
        }

        LaunchEventButton.IsEnabled = false;
        Cursor = Cursors.Wait;
        try
        {
            KioskViewModel viewModel;
            BoothCompositionRoot.RealBooth booth;
            try
            {
                // Off the UI thread: DB init plus the camera bridge's own
                // up-to-15s startup wait (see EnsureCameraBridgeRunning) would
                // otherwise freeze this window for that whole time.
                (viewModel, booth) = await Task.Run(() => BoothCompositionRoot.BuildKioskViewModel(locationId));
            }
            catch (BoothCompositionRoot.DatabaseUnavailableException ex)
            {
                MessageBox.Show(
                    $"Couldn't reach the booth database and can't start.\n\n{ex.Message}",
                    "Focus & Snap -- launch failed", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Couldn't start the booth services.\n\n{ex.Message}",
                    "Focus & Snap -- launch failed", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (booth.CameraBridgeProcess is not null)
            {
                _cameraBridgeProcess = booth.CameraBridgeProcess;
            }

            // Picking an event here and clicking Launch *is* the admin
            // action that used to require walking up to the kiosk, tapping
            // the hidden corner button, and entering the PIN a second time
            // (BoothState.Setup's "this booth isn't open yet" screen) --
            // that gate doesn't need to fire again on top of it. Land
            // straight on the guest-facing Welcome/idle screen instead of
            // Setup, same transition LaunchEventCommand already drives.
            viewModel.LaunchEventCommand.Execute(null);

            var kiosk = new KioskWindow(viewModel);
            kiosk.Closed += (_, _) =>
            {
                if (_closed)
                {
                    return;
                }
                Show();
                Activate();
                _ = RefreshEventsAsync();
            };
            Hide();
            kiosk.Show();
        }
        finally
        {
            LaunchEventButton.IsEnabled = true;
            Cursor = Cursors.Arrow;
        }
    }

    // ============================================================ settings ==

    private void OpenSettingsButton_Click(object sender, RoutedEventArgs e) => OpenAdminWindow("General");

    private void QuickLink_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string section })
        {
            OpenAdminWindow(section);
        }
    }

    private void OpenAdminWindow(string section)
    {
        if (_selectedLocationId is not int locationId)
        {
            return;
        }

        new AdminWindow(locationId, section) { Owner = this }.ShowDialog();
        _ = RefreshEventsAsync();
    }
}
