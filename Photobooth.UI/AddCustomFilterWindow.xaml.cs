using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Photobooth.Core;
using Photobooth.Data;

namespace Photobooth.UI;

/// <summary>
/// Modal "Add Custom Filter" dialog, opened from FilterLibraryWindow's
/// dashed "+ Add Custom Filter" tile. Validates the picked .cube file by
/// actually parsing it (CubeLut.Parse) and renders a real before/after
/// preview against the same sample image FilterLibraryWindow's own tiles
/// use (FilterPreviewSampleImage), via the real GdiCubeLutFilterService --
/// not a placeholder graphic -- so what the admin sees here is exactly what
/// guests will see. DialogResult=true tells FilterLibraryWindow to reload
/// its tile grid; false/unset (Cancel or the window X) leaves it untouched.
/// </summary>
public partial class AddCustomFilterWindow : Window
{
    private readonly int _locationId;
    private readonly CustomFilterRepository _customFilters = new();
    private string? _pendingCubeFilePath;

    public AddCustomFilterWindow(int locationId)
    {
        InitializeComponent();
        _locationId = locationId;
        NameBox.TextChanged += (_, _) => UpdateAddButtonEnabled();
    }

    private async void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Cube LUT files (*.cube)|*.cube|All files (*.*)|*.*",
            Title = "Choose a .CUBE LUT file",
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            CubeLut lut = CubeLut.Parse(dialog.FileName);
            _pendingCubeFilePath = dialog.FileName;

            FileCheckIcon.Visibility = Visibility.Visible;
            SelectedFileText.Text = System.IO.Path.GetFileName(dialog.FileName);
            SelectedFileText.Foreground = (System.Windows.Media.Brush)FindResource("InkBrush");
            FileDetailText.Text = $"{lut.Size}x{lut.Size}x{lut.Size} · 3D LUT · valid";
            FileDetailText.Visibility = Visibility.Visible;
            ErrorBanner.Visibility = Visibility.Collapsed;

            BrowseButton.IsEnabled = false;
            StatusText.Text = "Rendering preview...";
            try
            {
                await RenderPreviewAsync(dialog.FileName);
                StatusText.Text = string.Empty;
            }
            finally
            {
                BrowseButton.IsEnabled = true;
            }
        }
        catch (Exception ex)
        {
            _pendingCubeFilePath = null;

            FileCheckIcon.Visibility = Visibility.Collapsed;
            SelectedFileText.Text = System.IO.Path.GetFileName(dialog.FileName);
            SelectedFileText.Foreground = System.Windows.Media.Brushes.Firebrick;
            FileDetailText.Visibility = Visibility.Collapsed;

            ErrorText.Text = ex.Message;
            ErrorBanner.Visibility = Visibility.Visible;
            PreviewLabel.Visibility = Visibility.Collapsed;
            PreviewGrid.Visibility = Visibility.Collapsed;
        }

        UpdateAddButtonEnabled();
    }

    private async Task RenderPreviewAsync(string cubeFilePath)
    {
        string sampleImagePath = FilterPreviewSampleImage.EnsurePath();
        BeforeImage.Source = LoadImage(sampleImagePath);

        string previewPath = await new GdiCubeLutFilterService().ApplyCustomFilterAsync(sampleImagePath, cubeFilePath);
        AfterImage.Source = LoadImage(previewPath);

        PreviewLabel.Visibility = Visibility.Visible;
        PreviewGrid.Visibility = Visibility.Visible;
    }

    private static ImageSource? LoadImage(string path)
    {
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private void UpdateAddButtonEnabled()
    {
        AddButton.IsEnabled = NameBox.Text.Trim().Length > 0 && _pendingCubeFilePath is not null;
    }

    private async void AddButton_Click(object sender, RoutedEventArgs e)
    {
        string name = NameBox.Text.Trim();
        if (name.Length == 0 || _pendingCubeFilePath is null)
        {
            return;
        }

        AddButton.IsEnabled = false;
        try
        {
            string customFiltersDirectory = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "CustomFilters");
            System.IO.Directory.CreateDirectory(customFiltersDirectory);
            string storedFileName = $"{Guid.NewGuid():N}{System.IO.Path.GetExtension(_pendingCubeFilePath)}";
            string storedPath = System.IO.Path.Combine(customFiltersDirectory, storedFileName);
            System.IO.File.Copy(_pendingCubeFilePath, storedPath, overwrite: true);

            List<CustomFilterRecord> existing = await _customFilters.GetAllByLocationAsync(_locationId);
            await _customFilters.InsertAsync(_locationId, name, storedPath, sortOrder: existing.Count);

            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Couldn't add filter: {ex.Message}";
            StatusText.Foreground = System.Windows.Media.Brushes.Firebrick;
            AddButton.IsEnabled = true;
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
