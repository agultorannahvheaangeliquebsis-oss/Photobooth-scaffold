using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Photobooth.UI;

/// <summary>Small self-contained HSV color picker (saturation/value square +
/// hue bar + hex field) used by every design page's color swatches, so
/// choosing a color doesn't require typing a hex code by hand.</summary>
public partial class ColorPickerWindow : Window
{
    private const double SvSize = 200;
    private const double HueBarWidth = 200;

    private double _hue;
    private double _saturation;
    private double _value;
    private bool _isDraggingSv;
    private bool _isDraggingHue;
    private bool _suppressHexUpdate;

    private string? ResultHex { get; set; }

    public ColorPickerWindow(string? currentHex)
    {
        InitializeComponent();

        Color initial = TryParseHex(currentHex, out Color parsed) ? parsed : Colors.White;
        (_hue, _saturation, _value) = RgbToHsv(initial);
        UpdateHueLayer();
        UpdateSvCursor();
        UpdateHueCursor();
        UpdatePreviewAndHex();
    }

    /// <summary>Shows the picker seeded with <paramref name="currentHex"/>,
    /// owned by <paramref name="owner"/>, and returns the chosen color as
    /// "#RRGGBB", or null if the user cancelled.</summary>
    public static string? PickColor(Window owner, string? currentHex)
    {
        var window = new ColorPickerWindow(currentHex) { Owner = owner };
        return window.ShowDialog() == true ? window.ResultHex : null;
    }

    private void SvArea_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _isDraggingSv = true;
        SvArea.CaptureMouse();
        UpdateSvFromPoint(e.GetPosition(SvArea));
    }

    private void SvArea_MouseMove(object sender, MouseEventArgs e)
    {
        if (_isDraggingSv)
        {
            UpdateSvFromPoint(e.GetPosition(SvArea));
        }
    }

    private void SvArea_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _isDraggingSv = false;
        SvArea.ReleaseMouseCapture();
    }

    private void UpdateSvFromPoint(Point p)
    {
        _saturation = Math.Clamp(p.X, 0, SvSize) / SvSize;
        _value = 1 - (Math.Clamp(p.Y, 0, SvSize) / SvSize);
        UpdateSvCursor();
        UpdatePreviewAndHex();
    }

    private void HueBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _isDraggingHue = true;
        HueBar.CaptureMouse();
        UpdateHueFromPoint(e.GetPosition(HueBar));
    }

    private void HueBar_MouseMove(object sender, MouseEventArgs e)
    {
        if (_isDraggingHue)
        {
            UpdateHueFromPoint(e.GetPosition(HueBar));
        }
    }

    private void HueBar_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _isDraggingHue = false;
        HueBar.ReleaseMouseCapture();
    }

    private void UpdateHueFromPoint(Point p)
    {
        double x = Math.Clamp(p.X, 0, HueBarWidth);
        _hue = Math.Min(x / HueBarWidth * 360, 359.999);
        UpdateHueLayer();
        UpdateHueCursor();
        UpdatePreviewAndHex();
    }

    private void UpdateSvCursor()
    {
        Canvas.SetLeft(SvCursor, (_saturation * SvSize) - (SvCursor.Width / 2));
        Canvas.SetTop(SvCursor, ((1 - _value) * SvSize) - (SvCursor.Height / 2));
    }

    private void UpdateHueCursor()
    {
        Canvas.SetLeft(HueCursor, (_hue / 360 * HueBarWidth) - (HueCursor.Width / 2));
    }

    private void UpdateHueLayer()
    {
        SvHueLayer.Fill = new SolidColorBrush(HsvToRgb(_hue, 1, 1));
    }

    private void UpdatePreviewAndHex()
    {
        Color color = HsvToRgb(_hue, _saturation, _value);
        PreviewSwatch.Background = new SolidColorBrush(color);

        _suppressHexUpdate = true;
        HexBox.Text = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        HexBox.CaretIndex = HexBox.Text.Length;
        _suppressHexUpdate = false;

        ResultHex = HexBox.Text;
    }

    private void HexBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressHexUpdate || !TryParseHex(HexBox.Text, out Color color))
        {
            return;
        }

        (_hue, _saturation, _value) = RgbToHsv(color);
        UpdateHueLayer();
        UpdateSvCursor();
        UpdateHueCursor();
        PreviewSwatch.Background = new SolidColorBrush(color);
        ResultHex = HexBox.Text;
    }

    private void OkButton_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private static bool TryParseHex(string? hex, out Color color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(hex))
        {
            return false;
        }

        try
        {
            color = (Color)ColorConverter.ConvertFromString(hex);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static (double H, double S, double V) RgbToHsv(Color c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double delta = max - min;

        double h;
        if (delta < 0.00001)
        {
            h = 0;
        }
        else if (max == r)
        {
            h = 60 * (((g - b) / delta) % 6);
        }
        else if (max == g)
        {
            h = 60 * (((b - r) / delta) + 2);
        }
        else
        {
            h = 60 * (((r - g) / delta) + 4);
        }
        if (h < 0)
        {
            h += 360;
        }

        double s = max <= 0 ? 0 : delta / max;
        return (h, s, max);
    }

    private static Color HsvToRgb(double h, double s, double v)
    {
        double c = v * s;
        double x = c * (1 - Math.Abs((h / 60 % 2) - 1));
        double m = v - c;

        (double r, double g, double b) = h switch
        {
            < 60 => (c, x, 0.0),
            < 120 => (x, c, 0.0),
            < 180 => (0.0, c, x),
            < 240 => (0.0, x, c),
            < 300 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };

        return Color.FromRgb(
            (byte)Math.Round((r + m) * 255),
            (byte)Math.Round((g + m) * 255),
            (byte)Math.Round((b + m) * 255));
    }
}
