using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Photobooth.Core;
using Photobooth.Data;

namespace Photobooth.UI;

/// <summary>
/// The admin Filter library screen -- see dslrBooth's own Filters screen.
/// Opened from AdminWindow's Effects &amp; Stickers "Configure" button (Filters
/// card). Lets the admin toggle which PhotoFilterPreset values the guest-facing
/// FilterPicker screen offers; the actual color grading lives in
/// GdiFilterPresetService, this window only edits EffectsSettings.EnabledFilterPresetIds.
/// </summary>
public partial class FilterLibraryWindow : Window
{
    private readonly int _locationId;
    private readonly LocationRepository _locations = new();
    private readonly CustomFilterRepository _customFilters = new();
    private readonly HashSet<PhotoFilterPreset> _enabled = new();
    private List<CustomFilterRecord> _customFilterRecords = new();

    public FilterLibraryWindow(int locationId)
    {
        InitializeComponent();
        _locationId = locationId;
        Loaded += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        SaveButton.IsEnabled = false;
        StatusText.Text = "Loading...";
        try
        {
            List<LocationRecord> locations = await _locations.GetAllAsync();
            LocationRecord location = locations.First(l => l.LocationId == _locationId);

            _enabled.Clear();
            foreach (PhotoFilterPreset preset in PhotoFilterPresets.Parse(location.Effects.EnabledFilterPresetIds))
            {
                _enabled.Add(preset);
            }
            _customFilterRecords = await _customFilters.GetAllByLocationAsync(_locationId);

            await RenderTilesAsync();
            StatusText.Text = string.Empty;
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Couldn't load: {ex.Message}";
        }
        finally
        {
            SaveButton.IsEnabled = true;
        }
    }

    /// <summary>Ensures every preset/custom filter has a cached preview
    /// thumbnail (see EnsurePreviewImageAsync/EnsureCustomFilterPreviewImageAsync),
    /// then rebuilds the tile grid -- same "generate tiles from a list in
    /// code" pattern EventLauncherWindow.BuildTile/AdminWindow.BuildAttendantStageCard
    /// already established. Built-in tiles first, then admin-uploaded custom
    /// LUT tiles, then the dashed "+ Add Custom Filter" tile -- one grid, not
    /// two separate sections, so a guest's FilterPicker (which offers both
    /// kinds together too, see BoothStateMachine) matches what the admin sees
    /// here.</summary>
    private async Task RenderTilesAsync()
    {
        TilesPanel.Children.Clear();
        foreach (PhotoFilterPreset preset in PhotoFilterPresets.All)
        {
            string previewPath = await EnsurePreviewImageAsync(preset);
            TilesPanel.Children.Add(BuildTile(preset, previewPath));
        }
        foreach (CustomFilterRecord record in _customFilterRecords)
        {
            string previewPath = await EnsureCustomFilterPreviewImageAsync(record);
            TilesPanel.Children.Add(BuildCustomFilterTile(record, previewPath));
        }
        TilesPanel.Children.Add(BuildAddCustomFilterTile());
    }

    private Border BuildTile(PhotoFilterPreset preset, string previewImagePath)
    {
        var image = new Image
        {
            Width = 160,
            Height = 100,
            Stretch = Stretch.UniformToFill,
            Source = LoadImage(previewImagePath),
        };

        var nameText = new TextBlock
        {
            Text = PhotoFilterPresets.DisplayName(preset),
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("InkBrush"),
            Margin = new Thickness(0, 8, 0, 4),
        };

        var enabledCheckBox = new CheckBox
        {
            Content = "Enabled",
            IsChecked = _enabled.Contains(preset),
        };
        enabledCheckBox.Checked += (_, _) => _enabled.Add(preset);
        enabledCheckBox.Unchecked += (_, _) => _enabled.Remove(preset);

        var content = new StackPanel();
        content.Children.Add(image);
        content.Children.Add(nameText);
        content.Children.Add(enabledCheckBox);

        return new Border
        {
            Width = 184,
            Padding = new Thickness(8),
            Margin = new Thickness(0, 0, 14, 14),
            CornerRadius = new CornerRadius(10),
            Background = (Brush)FindResource("FieldBrush"),
            BorderBrush = (Brush)FindResource("LineBrush"),
            BorderThickness = new Thickness(1),
            Child = content,
        };
    }

    private static ImageSource? LoadImage(string path)
    {
        try
        {
            var bitmap = new System.Windows.Media.Imaging.BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception)
        {
            // Missing/unreadable preview -- tile just shows no image rather
            // than crashing the whole grid, same "convert to null" reasoning
            // PathToImageSourceConverter already uses for FrameOption previews.
            return null;
        }
    }

    /// <summary>Returns the cached preview thumbnail path for a preset,
    /// generating (and caching to disk) it first if this is the first time
    /// it's been needed -- see FilterPreviewSampleImage/GdiFilterPresetService.
    /// A one-time cost per preset per machine, not regenerated on every open
    /// of this window.</summary>
    private static async Task<string> EnsurePreviewImageAsync(PhotoFilterPreset preset)
    {
        string previewsDirectory = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "FilterPreviews");
        System.IO.Directory.CreateDirectory(previewsDirectory);
        string cachedPath = System.IO.Path.Combine(previewsDirectory, $"{preset}.jpg");
        if (System.IO.File.Exists(cachedPath))
        {
            return cachedPath;
        }

        string sampleImagePath = FilterPreviewSampleImage.EnsurePath();
        string generatedPath = await new GdiFilterPresetService().ApplyPresetAsync(sampleImagePath, preset);

        // Original is a no-op that returns the sample path unchanged (see
        // IFilterPresetService's doc) -- copy rather than move so the shared
        // sample image file itself is left in place for the next preset.
        if (generatedPath == sampleImagePath)
        {
            System.IO.File.Copy(generatedPath, cachedPath, overwrite: true);
        }
        else
        {
            System.IO.File.Move(generatedPath, cachedPath, overwrite: true);
        }
        return cachedPath;
    }

    /// <summary>Returns the cached preview thumbnail path for a custom filter,
    /// generating (and caching to disk) it first if this is the first time
    /// it's been needed -- same one-time-cost-per-machine reasoning as
    /// EnsurePreviewImageAsync, keyed by CustomFilterId rather than the enum
    /// name. RegeneratePreviewsButton_Click's directory wipe clears these
    /// too, since they live in the same FilterPreviews folder.</summary>
    private static async Task<string> EnsureCustomFilterPreviewImageAsync(CustomFilterRecord record)
    {
        string previewsDirectory = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "FilterPreviews");
        System.IO.Directory.CreateDirectory(previewsDirectory);
        string cachedPath = System.IO.Path.Combine(previewsDirectory, $"custom_{record.CustomFilterId}.jpg");
        if (System.IO.File.Exists(cachedPath))
        {
            return cachedPath;
        }

        string sampleImagePath = FilterPreviewSampleImage.EnsurePath();
        string generatedPath = await new GdiCubeLutFilterService().ApplyCustomFilterAsync(sampleImagePath, record.CubeFilePath);
        System.IO.File.Move(generatedPath, cachedPath, overwrite: true);
        return cachedPath;
    }

    /// <summary>Same tile shape as BuildTile, plus a teal "CUSTOM" badge over
    /// the thumbnail and a remove "x" button -- the two things that
    /// distinguish an admin-uploaded LUT from a built-in preset (which can be
    /// disabled but never removed).</summary>
    private Border BuildCustomFilterTile(CustomFilterRecord record, string previewImagePath)
    {
        var image = new Image
        {
            Width = 160,
            Height = 100,
            Stretch = Stretch.UniformToFill,
            Source = LoadImage(previewImagePath),
        };

        var badge = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x2E, 0x4F, 0xBF, 0xAE)),
            BorderBrush = (Brush)FindResource("AccentBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(999),
            Padding = new Thickness(7, 2, 7, 2),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(6),
            Child = new TextBlock
            {
                Text = "CUSTOM",
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 9,
                FontWeight = FontWeights.Bold,
                Foreground = (Brush)FindResource("AccentBrush"),
            },
        };

        var removeButton = new Button
        {
            Content = "×",
            Width = 20,
            Height = 20,
            Padding = new Thickness(0),
            FontSize = 12,
            Background = new SolidColorBrush(Color.FromArgb(0x8C, 0x0B, 0x0C, 0x0D)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(6),
            Tag = record.CustomFilterId,
            ToolTip = "Remove this custom filter",
        };
        removeButton.Click += DeleteCustomFilterButton_Click;

        var thumbnailArea = new Grid { Width = 160, Height = 100 };
        thumbnailArea.Children.Add(image);
        thumbnailArea.Children.Add(badge);
        thumbnailArea.Children.Add(removeButton);

        var nameText = new TextBlock
        {
            Text = record.Name,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("InkBrush"),
            Margin = new Thickness(0, 8, 0, 4),
        };

        var enabledCheckBox = new CheckBox
        {
            Content = "Enabled",
            IsChecked = record.IsActive,
            Tag = record.CustomFilterId,
        };
        enabledCheckBox.Click += CustomFilterEnabledCheckBox_Click;

        var content = new StackPanel();
        content.Children.Add(thumbnailArea);
        content.Children.Add(nameText);
        content.Children.Add(enabledCheckBox);

        return new Border
        {
            Width = 184,
            Padding = new Thickness(8),
            Margin = new Thickness(0, 0, 14, 14),
            CornerRadius = new CornerRadius(10),
            Background = (Brush)FindResource("FieldBrush"),
            BorderBrush = (Brush)FindResource("LineBrush"),
            BorderThickness = new Thickness(1),
            Child = content,
        };
    }

    /// <summary>The dashed-border tile at the end of the grid -- a plain
    /// Border can't draw a dashed edge, so this is a Grid with a Rectangle
    /// (StrokeDashArray) behind the "+ Add Custom Filter" content instead.</summary>
    private Grid BuildAddCustomFilterTile()
    {
        var dashedOutline = new System.Windows.Shapes.Rectangle
        {
            RadiusX = 10,
            RadiusY = 10,
            Stroke = new SolidColorBrush(Color.FromArgb(0x80, 0x4F, 0xBF, 0xAE)),
            StrokeThickness = 2,
            StrokeDashArray = new DoubleCollection { 4, 3 },
            Fill = Brushes.Transparent,
        };

        var plusIcon = new TextBlock
        {
            Text = "+",
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 22,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)FindResource("AccentBrush"),
            HorizontalAlignment = HorizontalAlignment.Center,
        };

        var label = new TextBlock
        {
            Text = "Add Custom Filter",
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("AccentBrush"),
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 8, 0, 0),
        };

        var content = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        content.Children.Add(plusIcon);
        content.Children.Add(label);

        var tile = new Grid
        {
            Width = 184,
            Height = 150,
            Margin = new Thickness(0, 0, 14, 14),
            Cursor = System.Windows.Input.Cursors.Hand,
        };
        tile.Children.Add(dashedOutline);
        tile.Children.Add(content);
        tile.MouseLeftButtonUp += async (_, _) => await OpenAddCustomFilterDialogAsync();
        return tile;
    }

    private async Task OpenAddCustomFilterDialogAsync()
    {
        var dialog = new AddCustomFilterWindow(_locationId) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            _customFilterRecords = await _customFilters.GetAllByLocationAsync(_locationId);
            await RenderTilesAsync();
        }
    }

    private async void CustomFilterEnabledCheckBox_Click(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { Tag: int customFilterId } checkBox)
        {
            await _customFilters.SetActiveAsync(customFilterId, checkBox.IsChecked == true);
            _customFilterRecords = await _customFilters.GetAllByLocationAsync(_locationId);
            await RenderTilesAsync();
        }
    }

    private async void DeleteCustomFilterButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: int customFilterId })
        {
            await _customFilters.DeleteAsync(customFilterId);
            _customFilterRecords = await _customFilters.GetAllByLocationAsync(_locationId);
            await RenderTilesAsync();
        }
    }

    private async void RegeneratePreviewsButton_Click(object sender, RoutedEventArgs e)
    {
        RegeneratePreviewsButton.IsEnabled = false;
        StatusText.Text = "Regenerating previews...";
        try
        {
            string previewsDirectory = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "FilterPreviews");
            if (System.IO.Directory.Exists(previewsDirectory))
            {
                System.IO.Directory.Delete(previewsDirectory, recursive: true);
            }
            await RenderTilesAsync();
            StatusText.Text = "Previews regenerated.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Couldn't regenerate previews: {ex.Message}";
        }
        finally
        {
            RegeneratePreviewsButton.IsEnabled = true;
        }
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        SaveButton.IsEnabled = false;
        try
        {
            string enabledFilterPresetIds = string.Join(',', PhotoFilterPresets.All.Where(_enabled.Contains));
            await _locations.UpdateEnabledFilterPresetsAsync(_locationId, enabledFilterPresetIds);
            StatusText.Text = "Saved -- takes effect for the next guest session.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Couldn't save: {ex.Message}";
        }
        finally
        {
            SaveButton.IsEnabled = true;
        }
    }
}
