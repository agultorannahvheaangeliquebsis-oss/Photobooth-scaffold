using System.IO;
using System.Linq;
using IoPath = System.IO.Path;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Photobooth.Core;
using Photobooth.Data;

namespace Photobooth.UI;

/// <summary>
/// Drag-and-drop visual editor for a print template's logo/text overlays.
/// PreviewImage and ElementsCanvas occupy the exact same position/size, so a
/// drag delta measured on the canvas maps 1:1 onto the rendered preview
/// below it -- no separate scaling math needed to translate between them.
/// The preview itself is rendered by PrintCompositor, the same code
/// SpoolerPrinterService uses at print time, so what's shown here is
/// provably what actually prints, not a second renderer that merely looks
/// similar. Every drag/resize/property edit re-renders that preview so it
/// stays live. Not yet seen rendered or clicked through -- same
/// interactive-desktop gap every WPF screen in this project has; the
/// percent math and compositing underneath it are unit-tested separately
/// (PrintTemplateTests, PrintCompositorTests) since mouse-event wiring
/// itself isn't something a unit test can exercise.
/// </summary>
public partial class PrintTemplateEditorWindow : Window
{
    private const int PreviewWidthPx = 500;
    private const double HandleSize = 12;
    private const double MinElementSizePx = 20;

    private readonly int _locationId;
    private readonly PrintTemplate _initialTemplate;
    private readonly List<PrintTemplateElement> _elements;
    private readonly List<Border> _containers = new();
    private readonly List<Rectangle> _handles = new();
    private readonly string _samplePhotoPath;
    private readonly int _canvasWidth;
    private readonly int _canvasHeight;

    private int _selectedIndex = -1;
    private int _draggingIndex = -1;
    private bool _resizing;
    private Point _dragStartPoint;
    private double _dragStartLeft, _dragStartTop, _dragStartWidth, _dragStartHeight;
    private bool _suppressPropertyEvents;

    public PrintTemplateEditorWindow(PrintTemplate template, int locationId)
    {
        InitializeComponent();

        _locationId = locationId;
        _initialTemplate = template;
        _elements = template.Elements.ToList();
        _samplePhotoPath = FindOrCreateSamplePhoto();

        (_canvasWidth, _canvasHeight) = PrintCompositor.ComputePreviewDimensions(template, PreviewWidthPx);
        PreviewHost.Width = _canvasWidth;
        PreviewHost.Height = _canvasHeight;

        for (int i = 0; i < _elements.Count; i++)
        {
            AddVisualForElement(i);
        }

        RefreshPreview();
    }

    /// <summary>The most recently captured photo, if any -- falls back to a
    /// generated placeholder so the editor works even with an empty ./captures
    /// folder (e.g. this dev environment, with no camera hardware attached).</summary>
    private static string FindOrCreateSamplePhoto()
    {
        if (Directory.Exists("./captures"))
        {
            FileInfo? newest = new DirectoryInfo("./captures").GetFiles()
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .FirstOrDefault();
            if (newest is not null)
            {
                return newest.FullName;
            }
        }

        string placeholderPath = IoPath.Combine(IoPath.GetTempPath(), "photobooth_template_editor_placeholder.jpg");
        if (!File.Exists(placeholderPath))
        {
            using var bitmap = new System.Drawing.Bitmap(800, 1200);
            using (System.Drawing.Graphics graphics = System.Drawing.Graphics.FromImage(bitmap))
            {
                graphics.Clear(System.Drawing.Color.Gainsboro);
                using var font = new System.Drawing.Font("Segoe UI", 36);
                using var format = new System.Drawing.StringFormat
                {
                    Alignment = System.Drawing.StringAlignment.Center,
                    LineAlignment = System.Drawing.StringAlignment.Center,
                };
                graphics.DrawString("Sample Photo", font, System.Drawing.Brushes.DimGray, new System.Drawing.RectangleF(0, 0, 800, 1200), format);
            }
            bitmap.Save(placeholderPath, System.Drawing.Imaging.ImageFormat.Jpeg);
        }
        return placeholderPath;
    }

    private void RefreshPreview()
    {
        try
        {
            PrintTemplate workingTemplate = _initialTemplate with { Elements = _elements };
            using System.Drawing.Bitmap rendered = PrintCompositor.RenderPreview(_samplePhotoPath, workingTemplate, PreviewWidthPx);
            PreviewImage.Source = ToBitmapSource(rendered);
        }
        catch (Exception ex)
        {
            EditorStatusText.Text = $"Couldn't render preview: {ex.Message}";
            EditorStatusText.Foreground = Brushes.Firebrick;
        }
    }

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    private static BitmapSource ToBitmapSource(System.Drawing.Bitmap bitmap)
    {
        IntPtr hBitmap = bitmap.GetHbitmap();
        try
        {
            BitmapSource source = Imaging.CreateBitmapSourceFromHBitmap(
                hBitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        finally
        {
            DeleteObject(hBitmap);
        }
    }

    private void AddVisualForElement(int index)
    {
        PrintTemplateElement element = _elements[index];

        FrameworkElement content = element.Kind == PrintTemplateElementKind.Text
            ? new TextBlock
            {
                Text = element.Text,
                FontWeight = element.Bold ? FontWeights.Bold : FontWeights.Normal,
                Foreground = HexToBrush(element.ColorHex),
                TextAlignment = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
            }
            : new Image
            {
                Source = element.ImagePath is string path && File.Exists(path)
                    ? new BitmapImage(new Uri(IoPath.GetFullPath(path)))
                    : null,
                Stretch = Stretch.Uniform,
            };

        var container = new Border
        {
            BorderBrush = Brushes.Gray,
            BorderThickness = new Thickness(1),
            Background = Brushes.Transparent, // hit-test visible, same reasoning MainWindow's surface background needs one
            Child = content,
        };
        container.MouseLeftButtonDown += ElementContainer_MouseLeftButtonDown;
        container.MouseMove += ElementContainer_MouseMove;
        container.MouseLeftButtonUp += ElementContainer_MouseLeftButtonUp;

        var handle = new Rectangle
        {
            Width = HandleSize,
            Height = HandleSize,
            Fill = Brushes.SteelBlue,
            Cursor = Cursors.SizeNWSE,
        };
        handle.MouseLeftButtonDown += Handle_MouseLeftButtonDown;
        handle.MouseMove += Handle_MouseMove;
        handle.MouseLeftButtonUp += Handle_MouseLeftButtonUp;

        _containers.Insert(index, container);
        _handles.Insert(index, handle);
        ElementsCanvas.Children.Add(container);
        ElementsCanvas.Children.Add(handle);

        PositionVisual(index);
    }

    private void PositionVisual(int index)
    {
        PrintTemplateElement element = _elements[index];
        Border container = _containers[index];
        Rectangle handle = _handles[index];

        double left = element.XPercent * _canvasWidth;
        double top = element.YPercent * _canvasHeight;
        double width = element.WidthPercent * _canvasWidth;
        double height = element.HeightPercent * _canvasHeight;

        Canvas.SetLeft(container, left);
        Canvas.SetTop(container, top);
        container.Width = width;
        container.Height = height;

        Canvas.SetLeft(handle, left + width - HandleSize / 2);
        Canvas.SetTop(handle, top + height - HandleSize / 2);
    }

    private void ElementContainer_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border container)
        {
            return;
        }

        int index = _containers.IndexOf(container);
        if (index < 0)
        {
            return;
        }

        _draggingIndex = index;
        _dragStartPoint = e.GetPosition(ElementsCanvas);
        _dragStartLeft = Canvas.GetLeft(container);
        _dragStartTop = Canvas.GetTop(container);
        container.CaptureMouse();
        e.Handled = true;
    }

    private void ElementContainer_MouseMove(object sender, MouseEventArgs e)
    {
        if (_draggingIndex < 0 || sender is not Border container || !container.IsMouseCaptured)
        {
            return;
        }

        Point current = e.GetPosition(ElementsCanvas);
        double deltaX = current.X - _dragStartPoint.X;
        double deltaY = current.Y - _dragStartPoint.Y;

        double newLeft = Math.Clamp(_dragStartLeft + deltaX, 0, Math.Max(0, _canvasWidth - container.Width));
        double newTop = Math.Clamp(_dragStartTop + deltaY, 0, Math.Max(0, _canvasHeight - container.Height));

        Canvas.SetLeft(container, newLeft);
        Canvas.SetTop(container, newTop);

        Rectangle handle = _handles[_draggingIndex];
        Canvas.SetLeft(handle, newLeft + container.Width - HandleSize / 2);
        Canvas.SetTop(handle, newTop + container.Height - HandleSize / 2);
    }

    private void ElementContainer_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_draggingIndex < 0 || sender is not Border container)
        {
            return;
        }

        container.ReleaseMouseCapture();
        int index = _draggingIndex;
        _draggingIndex = -1;

        double left = Canvas.GetLeft(container);
        double top = Canvas.GetTop(container);
        _elements[index] = _elements[index] with
        {
            XPercent = left / _canvasWidth,
            YPercent = top / _canvasHeight,
        };

        SelectElement(index);
        RefreshPreview();
        e.Handled = true;
    }

    private void Handle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Rectangle handle)
        {
            return;
        }

        int index = _handles.IndexOf(handle);
        if (index < 0)
        {
            return;
        }

        _draggingIndex = index;
        _resizing = true;
        _dragStartPoint = e.GetPosition(ElementsCanvas);
        _dragStartWidth = _containers[index].Width;
        _dragStartHeight = _containers[index].Height;
        handle.CaptureMouse();
        e.Handled = true;
    }

    private void Handle_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_resizing || _draggingIndex < 0 || sender is not Rectangle handle || !handle.IsMouseCaptured)
        {
            return;
        }

        Border container = _containers[_draggingIndex];
        Point current = e.GetPosition(ElementsCanvas);
        double deltaX = current.X - _dragStartPoint.X;
        double deltaY = current.Y - _dragStartPoint.Y;

        double left = Canvas.GetLeft(container);
        double top = Canvas.GetTop(container);
        double newWidth = Math.Clamp(_dragStartWidth + deltaX, MinElementSizePx, Math.Max(MinElementSizePx, _canvasWidth - left));
        double newHeight = Math.Clamp(_dragStartHeight + deltaY, MinElementSizePx, Math.Max(MinElementSizePx, _canvasHeight - top));

        container.Width = newWidth;
        container.Height = newHeight;
        Canvas.SetLeft(handle, left + newWidth - HandleSize / 2);
        Canvas.SetTop(handle, top + newHeight - HandleSize / 2);
    }

    private void Handle_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_resizing || _draggingIndex < 0 || sender is not Rectangle handle)
        {
            return;
        }

        handle.ReleaseMouseCapture();
        int index = _draggingIndex;
        _draggingIndex = -1;
        _resizing = false;

        Border container = _containers[index];
        _elements[index] = _elements[index] with
        {
            WidthPercent = container.Width / _canvasWidth,
            HeightPercent = container.Height / _canvasHeight,
        };

        SelectElement(index);
        RefreshPreview();
        e.Handled = true;
    }

    private void SelectElement(int index)
    {
        for (int i = 0; i < _containers.Count; i++)
        {
            _containers[i].BorderBrush = i == index ? Brushes.SteelBlue : Brushes.Gray;
            _containers[i].BorderThickness = new Thickness(i == index ? 2 : 1);
        }

        _selectedIndex = index;
        DeleteSelectedButton.IsEnabled = index >= 0;
        NoSelectionText.Visibility = index >= 0 ? Visibility.Collapsed : Visibility.Visible;

        if (index < 0)
        {
            TextPropertiesPanel.Visibility = Visibility.Collapsed;
            LogoPropertiesPanel.Visibility = Visibility.Collapsed;
            return;
        }

        PrintTemplateElement element = _elements[index];
        _suppressPropertyEvents = true;
        try
        {
            if (element.Kind == PrintTemplateElementKind.Text)
            {
                TextPropertiesPanel.Visibility = Visibility.Visible;
                LogoPropertiesPanel.Visibility = Visibility.Collapsed;
                ElementTextBox.Text = element.Text ?? string.Empty;
                FontSizeSlider.Value = element.FontSizePercent;
                BoldCheckBox.IsChecked = element.Bold;
                ElementColorBox.Text = element.ColorHex;
            }
            else
            {
                TextPropertiesPanel.Visibility = Visibility.Collapsed;
                LogoPropertiesPanel.Visibility = Visibility.Visible;
            }
        }
        finally
        {
            _suppressPropertyEvents = false;
        }
    }

    private void RefreshVisualContent(int index)
    {
        PrintTemplateElement element = _elements[index];
        if (_containers[index].Child is TextBlock textBlock)
        {
            textBlock.Text = element.Text;
            textBlock.FontWeight = element.Bold ? FontWeights.Bold : FontWeights.Normal;
            textBlock.Foreground = HexToBrush(element.ColorHex);
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

    private void ElementTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressPropertyEvents || _selectedIndex < 0)
        {
            return;
        }

        _elements[_selectedIndex] = _elements[_selectedIndex] with { Text = ElementTextBox.Text };
        RefreshVisualContent(_selectedIndex);
        RefreshPreview();
    }

    private void FontSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppressPropertyEvents || _selectedIndex < 0)
        {
            return;
        }

        _elements[_selectedIndex] = _elements[_selectedIndex] with { FontSizePercent = FontSizeSlider.Value };
        RefreshPreview();
    }

    private void BoldCheckBox_Click(object sender, RoutedEventArgs e)
    {
        if (_suppressPropertyEvents || _selectedIndex < 0)
        {
            return;
        }

        _elements[_selectedIndex] = _elements[_selectedIndex] with { Bold = BoldCheckBox.IsChecked == true };
        RefreshVisualContent(_selectedIndex);
        RefreshPreview();
    }

    private void ElementColorBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressPropertyEvents || _selectedIndex < 0)
        {
            return;
        }

        _elements[_selectedIndex] = _elements[_selectedIndex] with { ColorHex = ElementColorBox.Text };
        RefreshVisualContent(_selectedIndex);
        RefreshPreview();
    }

    private void AddTextButton_Click(object sender, RoutedEventArgs e)
    {
        var element = new PrintTemplateElement(
            PrintTemplateElementKind.Text,
            XPercent: 0.1, YPercent: 0.85, WidthPercent: 0.8, HeightPercent: 0.1,
            Text: "Your text here");
        _elements.Add(element);
        AddVisualForElement(_elements.Count - 1);
        SelectElement(_elements.Count - 1);
        RefreshPreview();
    }

    private void AddLogoButton_Click(object sender, RoutedEventArgs e)
    {
        string? storedPath = PickAndStoreLogoImage();
        if (storedPath is null)
        {
            return;
        }

        var element = new PrintTemplateElement(
            PrintTemplateElementKind.Logo,
            XPercent: 0.7, YPercent: 0.05, WidthPercent: 0.25, HeightPercent: 0.1,
            ImagePath: storedPath);
        _elements.Add(element);
        AddVisualForElement(_elements.Count - 1);
        SelectElement(_elements.Count - 1);
        RefreshPreview();
    }

    private void ChangeLogoImageButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedIndex < 0)
        {
            return;
        }

        string? storedPath = PickAndStoreLogoImage();
        if (storedPath is null)
        {
            return;
        }

        _elements[_selectedIndex] = _elements[_selectedIndex] with { ImagePath = storedPath };
        if (_containers[_selectedIndex].Child is Image image)
        {
            image.Source = new BitmapImage(new Uri(IoPath.GetFullPath(storedPath)));
        }
        RefreshPreview();
    }

    /// <summary>Copies the chosen image into a local Assets/PrintElements folder,
    /// same "own local copy, not a reference to wherever the admin picked it from"
    /// pattern AddFrameButton_Click/SaveThemeButton_Click already established.</summary>
    private static string? PickAndStoreLogoImage()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Image files (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg",
            Title = "Choose a logo image",
        };
        if (dialog.ShowDialog() != true)
        {
            return null;
        }

        string directory = IoPath.Combine(AppContext.BaseDirectory, "Assets", "PrintElements");
        Directory.CreateDirectory(directory);
        string storedFileName = $"{Guid.NewGuid():N}{IoPath.GetExtension(dialog.FileName)}";
        string storedPath = IoPath.Combine(directory, storedFileName);
        File.Copy(dialog.FileName, storedPath, overwrite: true);
        return storedPath;
    }

    private void DeleteSelectedButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedIndex < 0)
        {
            return;
        }

        ElementsCanvas.Children.Remove(_containers[_selectedIndex]);
        ElementsCanvas.Children.Remove(_handles[_selectedIndex]);
        _containers.RemoveAt(_selectedIndex);
        _handles.RemoveAt(_selectedIndex);
        _elements.RemoveAt(_selectedIndex);

        SelectElement(-1);
        RefreshPreview();
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (_elements.Any(element => !element.IsValid))
        {
            EditorStatusText.Text = "Every element needs valid bounds and either text or an image.";
            EditorStatusText.Foreground = Brushes.Firebrick;
            return;
        }

        SaveButton.IsEnabled = false;
        try
        {
            await new PrintTemplateElementRepository().ReplaceAllAsync(_locationId, _elements);
            DialogResult = true;
        }
        catch (Exception ex)
        {
            EditorStatusText.Text = $"Couldn't save: {ex.Message}";
            EditorStatusText.Foreground = Brushes.Firebrick;
            SaveButton.IsEnabled = true;
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
