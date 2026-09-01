using System.IO;
using System.Linq;
using IoPath = System.IO.Path;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Photobooth.Core;
using Photobooth.Data;

namespace Photobooth.UI;

/// <summary>
/// Drag-and-drop visual editor for the Welcome/Capture/Sharing guest-facing
/// screens' text/image/shape overlays -- the Visual Screen Editor from
/// BUILD_PLAN.md's Phase 6. Reuses PrintTemplateEditorWindow's percent-of-canvas
/// drag/resize math (ElementsCanvas maps 1:1 to XPercent/YPercent/WidthPercent/
/// HeightPercent), but with one shared ElementsCanvas whose contents swap
/// per-tab rather than three separate canvases -- factoring the canvas/list/
/// property-panel state into a reusable per-tab structure keeps this
/// maintainable without either tripling ~700 lines of drag/resize code three
/// times over, or inventing a bigger abstraction layer than three tabs over one
/// element model actually needs. Unlike the print editor, there's no
/// PrintCompositor-rendered preview underneath: ElementsCanvas itself is the
/// live view, since these are placed WPF elements, not composited onto a
/// captured photo. Not yet seen rendered or clicked through -- same
/// interactive-desktop gap every WPF screen in this project has.
/// </summary>
public partial class ScreenTemplateEditorWindow : Window
{
    private const int CanvasWidth = 640;
    private const int CanvasHeight = 400;
    private const double HandleSize = 12;
    private const double MinElementSizePx = 20;

    private readonly int _locationId;

    /// <summary>Working copy of the screen-chrome toggles edited by the
    /// SETTINGS tab (see ScreenSettingsCheckBox_Click/LiveViewRotationCombo_
    /// SelectionChanged) -- booth-wide, not per-tab, same as the underlying
    /// Location columns (see LocationRepository.UpdateScreenSettingsAsync).</summary>
    private ScreenSettings _screenSettings;
    private bool _suppressScreenSettingsEvents;

    /// <summary>Working element lists, one per screen -- populated from the
    /// existing rows at load, mutated in place as the admin edits, and flattened
    /// back into one list on Save.</summary>
    private readonly Dictionary<ScreenTemplateScreen, List<ScreenTemplateElement>> _elementsByScreen = new()
    {
        [ScreenTemplateScreen.Welcome] = new(),
        [ScreenTemplateScreen.Capture] = new(),
        [ScreenTemplateScreen.Sharing] = new(),
    };

    private ScreenTemplateScreen _activeScreen = ScreenTemplateScreen.Welcome;
    private List<ScreenTemplateElement> _elements => _elementsByScreen[_activeScreen];
    private readonly List<Border> _containers = new();
    private readonly List<Rectangle> _handles = new();

    private int _selectedIndex = -1;
    private int _draggingIndex = -1;
    private bool _resizing;
    private Point _dragStartPoint;
    private double _dragStartLeft, _dragStartTop, _dragStartWidth, _dragStartHeight;
    private bool _suppressPropertyEvents;
    private bool _suppressLayerListEvents;

    public ScreenTemplateEditorWindow(IReadOnlyList<ScreenTemplateElement> existingElements, int locationId, ScreenSettings screenSettings)
    {
        InitializeComponent();

        _locationId = locationId;
        _screenSettings = screenSettings;
        foreach (ScreenTemplateElement element in existingElements)
        {
            _elementsByScreen[element.Screen].Add(element);
        }

        ElementsCanvas.Width = CanvasWidth;
        ElementsCanvas.Height = CanvasHeight;

        LoadActiveScreen();
        LoadScreenSettingsControls();
    }

    private void LoadScreenSettingsControls()
    {
        _suppressScreenSettingsEvents = true;
        try
        {
            BoothIconsEnabledCheckBox.IsChecked = _screenSettings.BoothIconsEnabled;
            ShowLiveViewCheckBox.IsChecked = _screenSettings.ShowLiveView;
            MirrorLiveViewCheckBox.IsChecked = _screenSettings.MirrorLiveView;
            foreach (ComboBoxItem item in LiveViewRotationCombo.Items)
            {
                if (item.Tag is string tag && int.TryParse(tag, out int degrees) && degrees == _screenSettings.LiveViewRotation)
                {
                    LiveViewRotationCombo.SelectedItem = item;
                    break;
                }
            }
        }
        finally
        {
            _suppressScreenSettingsEvents = false;
        }
    }

    private void ScreenSettingsCheckBox_Click(object sender, RoutedEventArgs e)
    {
        if (_suppressScreenSettingsEvents)
        {
            return;
        }

        _screenSettings = _screenSettings with
        {
            BoothIconsEnabled = BoothIconsEnabledCheckBox.IsChecked == true,
            ShowLiveView = ShowLiveViewCheckBox.IsChecked == true,
            MirrorLiveView = MirrorLiveViewCheckBox.IsChecked == true,
        };
    }

    private void LiveViewRotationCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressScreenSettingsEvents)
        {
            return;
        }

        if (LiveViewRotationCombo.SelectedItem is ComboBoxItem { Tag: string tag } && int.TryParse(tag, out int degrees))
        {
            _screenSettings = _screenSettings with { LiveViewRotation = degrees };
        }
    }

    private void ScreenTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ScreenTabControl.SelectedItem is not TabItem { Tag: string tag } || !Enum.TryParse(tag, out ScreenTemplateScreen screen))
        {
            return;
        }

        _activeScreen = screen;
        LoadActiveScreen();
    }

    /// <summary>Rebuilds ElementsCanvas's visuals for whichever screen is now
    /// active -- same "clear and re-add" approach MoveSelectedLayerTo already
    /// uses in PrintTemplateEditorWindow for a re-order, just for a full tab
    /// switch instead.</summary>
    private void LoadActiveScreen()
    {
        ElementsCanvas.Children.Clear();
        _containers.Clear();
        _handles.Clear();
        _selectedIndex = -1;

        for (int i = 0; i < _elements.Count; i++)
        {
            AddVisualForElement(i);
        }

        RefreshLayerList();
        SelectElement(-1);
    }

    private void AddVisualForElement(int index)
    {
        ScreenTemplateElement element = _elements[index];

        FrameworkElement content = element.Kind switch
        {
            ScreenTemplateElementKind.Text => new TextBlock
            {
                Text = element.Text,
                FontWeight = element.Bold ? FontWeights.Bold : FontWeights.Normal,
                Foreground = HexToBrush(element.ColorHex),
                TextAlignment = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
            },
            ScreenTemplateElementKind.Image => new Image
            {
                Source = element.ImagePath is string path && File.Exists(path)
                    ? new BitmapImage(new Uri(IoPath.GetFullPath(path)))
                    : null,
                Stretch = Stretch.Uniform,
            },
            _ => new System.Windows.Shapes.Rectangle { Fill = HexToBrush(element.ColorHex) },
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
        ScreenTemplateElement element = _elements[index];
        Border container = _containers[index];
        Rectangle handle = _handles[index];

        double left = element.XPercent * CanvasWidth;
        double top = element.YPercent * CanvasHeight;
        double width = element.WidthPercent * CanvasWidth;
        double height = element.HeightPercent * CanvasHeight;

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

        double newLeft = Math.Clamp(_dragStartLeft + deltaX, 0, Math.Max(0, CanvasWidth - container.Width));
        double newTop = Math.Clamp(_dragStartTop + deltaY, 0, Math.Max(0, CanvasHeight - container.Height));

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
            XPercent = left / CanvasWidth,
            YPercent = top / CanvasHeight,
        };

        SelectElement(index);
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
        double newWidth = Math.Clamp(_dragStartWidth + deltaX, MinElementSizePx, Math.Max(MinElementSizePx, CanvasWidth - left));
        double newHeight = Math.Clamp(_dragStartHeight + deltaY, MinElementSizePx, Math.Max(MinElementSizePx, CanvasHeight - top));

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
            WidthPercent = container.Width / CanvasWidth,
            HeightPercent = container.Height / CanvasHeight,
        };

        SelectElement(index);
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

        _suppressLayerListEvents = true;
        try
        {
            LayerListBox.SelectedIndex = index;
        }
        finally
        {
            _suppressLayerListEvents = false;
        }

        if (index < 0)
        {
            TextPropertiesPanel.Visibility = Visibility.Collapsed;
            ImagePropertiesPanel.Visibility = Visibility.Collapsed;
            ShapePropertiesPanel.Visibility = Visibility.Collapsed;
            return;
        }

        ScreenTemplateElement element = _elements[index];
        _suppressPropertyEvents = true;
        try
        {
            TextPropertiesPanel.Visibility = element.Kind == ScreenTemplateElementKind.Text ? Visibility.Visible : Visibility.Collapsed;
            ImagePropertiesPanel.Visibility = element.Kind == ScreenTemplateElementKind.Image ? Visibility.Visible : Visibility.Collapsed;
            ShapePropertiesPanel.Visibility = element.Kind == ScreenTemplateElementKind.Shape ? Visibility.Visible : Visibility.Collapsed;

            if (element.Kind == ScreenTemplateElementKind.Text)
            {
                ElementTextBox.Text = element.Text ?? string.Empty;
                FontSizeSlider.Value = element.FontSizePercent;
                BoldCheckBox.IsChecked = element.Bold;
                ElementColorBox.Text = element.ColorHex;
            }
            else if (element.Kind == ScreenTemplateElementKind.Shape)
            {
                ShapeColorBox.Text = element.ColorHex;
            }
        }
        finally
        {
            _suppressPropertyEvents = false;
        }
    }

    private void RefreshVisualContent(int index)
    {
        ScreenTemplateElement element = _elements[index];
        if (_containers[index].Child is TextBlock textBlock)
        {
            textBlock.Text = element.Text;
            textBlock.FontWeight = element.Bold ? FontWeights.Bold : FontWeights.Normal;
            textBlock.Foreground = HexToBrush(element.ColorHex);
        }
        else if (_containers[index].Child is System.Windows.Shapes.Rectangle rectangle)
        {
            rectangle.Fill = HexToBrush(element.ColorHex);
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
    }

    private void FontSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppressPropertyEvents || _selectedIndex < 0)
        {
            return;
        }

        _elements[_selectedIndex] = _elements[_selectedIndex] with { FontSizePercent = FontSizeSlider.Value };
    }

    private void BoldCheckBox_Click(object sender, RoutedEventArgs e)
    {
        if (_suppressPropertyEvents || _selectedIndex < 0)
        {
            return;
        }

        _elements[_selectedIndex] = _elements[_selectedIndex] with { Bold = BoldCheckBox.IsChecked == true };
        RefreshVisualContent(_selectedIndex);
    }

    private void ElementColorBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressPropertyEvents || _selectedIndex < 0)
        {
            return;
        }

        string hex = _elements[_selectedIndex].Kind == ScreenTemplateElementKind.Shape ? ShapeColorBox.Text : ElementColorBox.Text;
        _elements[_selectedIndex] = _elements[_selectedIndex] with { ColorHex = hex };
        RefreshVisualContent(_selectedIndex);
    }

    private void AddTextButton_Click(object sender, RoutedEventArgs e)
    {
        var element = new ScreenTemplateElement(
            _activeScreen, ScreenTemplateElementKind.Text,
            XPercent: 0.1, YPercent: 0.4, WidthPercent: 0.8, HeightPercent: 0.2,
            Text: "Your text here");
        _elements.Add(element);
        AddVisualForElement(_elements.Count - 1);
        RefreshLayerList();
        SelectElement(_elements.Count - 1);
    }

    private void AddImageButton_Click(object sender, RoutedEventArgs e)
    {
        string? storedPath = PickAndStoreImage();
        if (storedPath is null)
        {
            return;
        }

        var element = new ScreenTemplateElement(
            _activeScreen, ScreenTemplateElementKind.Image,
            XPercent: 0.35, YPercent: 0.05, WidthPercent: 0.3, HeightPercent: 0.2,
            ImagePath: storedPath);
        _elements.Add(element);
        AddVisualForElement(_elements.Count - 1);
        RefreshLayerList();
        SelectElement(_elements.Count - 1);
    }

    private void AddShapeButton_Click(object sender, RoutedEventArgs e)
    {
        var element = new ScreenTemplateElement(
            _activeScreen, ScreenTemplateElementKind.Shape,
            XPercent: 0.05, YPercent: 0.05, WidthPercent: 0.2, HeightPercent: 0.1,
            ColorHex: "#365C58");
        _elements.Add(element);
        AddVisualForElement(_elements.Count - 1);
        RefreshLayerList();
        SelectElement(_elements.Count - 1);
    }

    private void ChangeImageButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedIndex < 0)
        {
            return;
        }

        string? storedPath = PickAndStoreImage();
        if (storedPath is null)
        {
            return;
        }

        _elements[_selectedIndex] = _elements[_selectedIndex] with { ImagePath = storedPath };
        if (_containers[_selectedIndex].Child is Image image)
        {
            image.Source = new BitmapImage(new Uri(IoPath.GetFullPath(storedPath)));
        }
    }

    /// <summary>Copies the chosen image into a local Assets/ScreenElements folder,
    /// same "own local copy, not a reference to wherever the admin picked it from"
    /// pattern PrintTemplateEditorWindow.PickAndStoreLogoImage already established.</summary>
    private static string? PickAndStoreImage()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Image files (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg",
            Title = "Choose an image",
        };
        if (dialog.ShowDialog() != true)
        {
            return null;
        }

        string directory = IoPath.Combine(AppContext.BaseDirectory, "Assets", "ScreenElements");
        Directory.CreateDirectory(directory);
        string storedFileName = $"{Guid.NewGuid():N}{IoPath.GetExtension(dialog.FileName)}";
        string storedPath = IoPath.Combine(directory, storedFileName);
        File.Copy(dialog.FileName, storedPath, overwrite: true);
        return storedPath;
    }

    /// <summary>Aligns the selected element against the canvas bounds --
    /// left/right/top/bottom snap the corresponding edge to 0 or the
    /// canvas's own width/height, CenterHorizontal/CenterVertical center it
    /// within the canvas. Only ever touches XPercent/YPercent (size is
    /// untouched), same as a drag-move does.</summary>
    private void AlignButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedIndex < 0 || sender is not Button { Tag: string alignment })
        {
            return;
        }

        Border container = _containers[_selectedIndex];
        double left = Canvas.GetLeft(container);
        double top = Canvas.GetTop(container);

        switch (alignment)
        {
            case "Left": left = 0; break;
            case "Right": left = CanvasWidth - container.Width; break;
            case "CenterHorizontal": left = (CanvasWidth - container.Width) / 2; break;
            case "Top": top = 0; break;
            case "Bottom": top = CanvasHeight - container.Height; break;
            case "CenterVertical": top = (CanvasHeight - container.Height) / 2; break;
        }

        Canvas.SetLeft(container, left);
        Canvas.SetTop(container, top);
        Rectangle handle = _handles[_selectedIndex];
        Canvas.SetLeft(handle, left + container.Width - HandleSize / 2);
        Canvas.SetTop(handle, top + container.Height - HandleSize / 2);

        _elements[_selectedIndex] = _elements[_selectedIndex] with
        {
            XPercent = left / CanvasWidth,
            YPercent = top / CanvasHeight,
        };
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

        RefreshLayerList();
        SelectElement(-1);
    }

    private void RefreshLayerList()
    {
        _suppressLayerListEvents = true;
        try
        {
            LayerListBox.Items.Clear();
            foreach (ScreenTemplateElement element in _elements)
            {
                LayerListBox.Items.Add(element.Kind switch
                {
                    ScreenTemplateElementKind.Text => $"Text: {element.Text}",
                    ScreenTemplateElementKind.Image => "Image",
                    _ => "Shape",
                });
            }
        }
        finally
        {
            _suppressLayerListEvents = false;
        }
    }

    private void LayerListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressLayerListEvents)
        {
            return;
        }

        SelectElement(LayerListBox.SelectedIndex);
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        List<ScreenTemplateElement> all = _elementsByScreen.Values.SelectMany(list => list).ToList();
        if (all.Any(element => !element.IsValid))
        {
            EditorStatusText.Text = "Every element needs valid bounds and either text or an image.";
            EditorStatusText.Foreground = Brushes.Firebrick;
            return;
        }

        SaveButton.IsEnabled = false;
        try
        {
            await new ScreenTemplateElementRepository().ReplaceAllAsync(_locationId, all);
            await new LocationRepository().UpdateScreenSettingsAsync(_locationId, _screenSettings);
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
