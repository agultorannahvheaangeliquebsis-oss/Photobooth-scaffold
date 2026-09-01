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
    private readonly HashSet<PhotoFilterPreset> _enabled = new();

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

    /// <summary>Ensures every preset has a cached preview thumbnail (see
    /// EnsurePreviewImageAsync), then rebuilds the tile grid -- same "generate
    /// tiles from a list in code" pattern EventLauncherWindow.BuildTile/
    /// AdminWindow.BuildAttendantStageCard already established.</summary>
    private async Task RenderTilesAsync()
    {
        TilesPanel.Children.Clear();
        foreach (PhotoFilterPreset preset in PhotoFilterPresets.All)
        {
            string previewPath = await EnsurePreviewImageAsync(preset);
            TilesPanel.Children.Add(BuildTile(preset, previewPath));
        }
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
