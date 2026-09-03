using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Photobooth.Data;

namespace Photobooth.UI;

/// <summary>
/// The admin Stickers library screen -- see dslrBooth's own "Your digital
/// props" screen. Opened from AdminWindow's Effects &amp; Stickers Stickers
/// card. Lets the admin add/remove the transparent-PNG props a guest would
/// pick from; unlike FilterLibraryWindow's built-in-plus-custom tile grid,
/// every prop here is admin-uploaded, so there's no separate "enabled"
/// checkbox or Save button -- adding/deleting a tile writes to the Sticker
/// table immediately (same as FilterLibraryWindow's custom-filter "x"
/// button). Only manages the library itself; the guest-facing screen where
/// a prop actually gets placed onto a photo is a separate, not-yet-built
/// piece -- see StickerRepository's own doc comment.
/// </summary>
public partial class StickerLibraryWindow : Window
{
    private readonly int _locationId;
    private readonly StickerRepository _stickers = new();
    private List<StickerRecord> _stickerRecords = new();

    public StickerLibraryWindow(int locationId)
    {
        InitializeComponent();
        _locationId = locationId;
        Loaded += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        StatusText.Text = "Loading...";
        try
        {
            _stickerRecords = await _stickers.GetAllByLocationAsync(_locationId);
            RenderTiles();
            StatusText.Text = string.Empty;
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Couldn't load: {ex.Message}";
        }
    }

    private void RenderTiles()
    {
        TilesPanel.Children.Clear();
        foreach (StickerRecord record in _stickerRecords)
        {
            TilesPanel.Children.Add(BuildStickerTile(record));
        }
        TilesPanel.Children.Add(BuildAddMediaTile());
    }

    private Border BuildStickerTile(StickerRecord record)
    {
        var image = new Image
        {
            Width = 160,
            Height = 100,
            Stretch = Stretch.Uniform,
            Source = LoadImage(record.ImagePath),
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
            Tag = record.StickerId,
            ToolTip = "Remove this prop",
        };
        removeButton.Click += DeleteStickerButton_Click;

        var thumbnailArea = new Grid { Width = 160, Height = 100 };
        thumbnailArea.Children.Add(image);
        thumbnailArea.Children.Add(removeButton);

        var nameText = new TextBlock
        {
            Text = record.Name,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("InkBrush"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 8, 0, 0),
        };

        var content = new StackPanel();
        content.Children.Add(thumbnailArea);
        content.Children.Add(nameText);

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
    /// (StrokeDashArray) behind the "+ Add media" content, same trick
    /// FilterLibraryWindow.BuildAddCustomFilterTile uses.</summary>
    private Grid BuildAddMediaTile()
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
            Text = "Add media",
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("AccentBrush"),
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 8, 0, 0),
        };

        var subLabel = new TextBlock
        {
            Text = "PNG",
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 11,
            Foreground = (Brush)FindResource("MutedBrush"),
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 2, 0, 0),
        };

        var content = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        content.Children.Add(plusIcon);
        content.Children.Add(label);
        content.Children.Add(subLabel);

        var tile = new Grid
        {
            Width = 184,
            Height = 150,
            Margin = new Thickness(0, 0, 14, 14),
            Cursor = System.Windows.Input.Cursors.Hand,
        };
        tile.Children.Add(dashedOutline);
        tile.Children.Add(content);
        tile.MouseLeftButtonUp += async (_, _) => await AddMediaAsync();
        return tile;
    }

    /// <summary>Lets the admin pick several PNGs at once -- dslrBooth's own
    /// picker supports multi-select too, and there's nothing per-file to
    /// configure (unlike AddCustomFilterWindow's name+preview dialog), so a
    /// single file dialog is all this needs rather than a modal per prop.</summary>
    private async Task AddMediaAsync()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "PNG files (*.png)|*.png",
            Title = "Add digital props",
            Multiselect = true,
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        StatusText.Text = "Adding...";
        try
        {
            string stickersDirectory = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Stickers");
            System.IO.Directory.CreateDirectory(stickersDirectory);

            int sortOrder = _stickerRecords.Count;
            foreach (string sourcePath in dialog.FileNames)
            {
                string storedFileName = $"{Guid.NewGuid():N}.png";
                string storedPath = System.IO.Path.Combine(stickersDirectory, storedFileName);
                System.IO.File.Copy(sourcePath, storedPath, overwrite: true);

                string name = System.IO.Path.GetFileNameWithoutExtension(sourcePath);
                await _stickers.InsertAsync(_locationId, name, storedPath, sortOrder++);
            }

            _stickerRecords = await _stickers.GetAllByLocationAsync(_locationId);
            RenderTiles();
            StatusText.Text = string.Empty;
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Couldn't add: {ex.Message}";
        }
    }

    private async void DeleteStickerButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: int stickerId })
        {
            await _stickers.DeleteAsync(stickerId);
            _stickerRecords = await _stickers.GetAllByLocationAsync(_locationId);
            RenderTiles();
        }
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
            // Missing/unreadable file -- tile just shows no image rather
            // than crashing the whole grid, same reasoning FilterLibraryWindow's
            // own LoadImage already uses.
            return null;
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
