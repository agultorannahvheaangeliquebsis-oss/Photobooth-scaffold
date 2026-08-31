using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace Photobooth.UI.Converters;

/// <summary>
/// Maps the ViewModel's current screen state onto one screen's Visibility.
/// The ConverterParameter names the state (or a comma-separated set of
/// states) that should make this screen visible -- a set, because a couple of
/// KioskWindow's chrome elements (the brand bar, the QR panel) belong to more
/// than one state and would otherwise need a bool per state on the ViewModel.
///
/// Compared by name rather than by enum value so the parameter stays readable
/// in XAML ("Idle", "Countdown") instead of an int nobody can review.
/// </summary>
public class EnumToVisibilityConverter : IValueConverter
{
    /// <summary>What a non-matching state collapses to. Collapsed by default
    /// (a hidden screen shouldn't reserve layout space); Hidden is available
    /// for the rare element whose slot has to stay reserved.</summary>
    public Visibility FallbackVisibility { get; set; } = Visibility.Collapsed;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null || parameter is not string expected)
        {
            return FallbackVisibility;
        }

        string actual = value.ToString()!;
        foreach (string candidate in expected.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (string.Equals(candidate, actual, StringComparison.OrdinalIgnoreCase))
            {
                return Visibility.Visible;
            }
        }
        return FallbackVisibility;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("One-way only -- the state is owned by the ViewModel.");
}

/// <summary>
/// Same idea as <see cref="EnumToVisibilityConverter"/> but producing a bool,
/// for the capture-mode tiles' selected styling (the KioskModeTile template
/// keys off Tag) -- one enum property on the ViewModel drives all four tiles
/// instead of four separate IsSelected bools.
/// </summary>
public class EnumToTagConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not null && parameter is string expected
        && string.Equals(value.ToString(), expected, StringComparison.OrdinalIgnoreCase)
            ? "selected"
            : "unselected";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("One-way only -- selection is owned by the ViewModel.");
}

/// <summary>
/// Bool to Visibility, with an Invert flag. WPF ships a BooleanToVisibility
/// converter but it can't invert, which this UI needs in several places
/// (e.g. "show the Print button only while NOT printing").
/// </summary>
public class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool flag = value is bool b && b;
        return flag != Invert ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Visibility v && (v == Visibility.Visible) != Invert;
}

/// <summary>
/// Renders one star of the Feedback screen's 1-5 rating row: filled (&#9733;) if
/// the bound rating is at least this star's rank (given via ConverterParameter,
/// "1".."5"), outline (&#9734;) otherwise -- lets five plain buttons share one
/// SelectedFeedbackRating int instead of five separate bools.
/// </summary>
public class RatingToStarConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        int rating = value is int i ? i : 0;
        int rank = parameter is string s && int.TryParse(s, out int r) ? r : 0;
        return rating >= rank ? "★" : "☆";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("One-way only -- the rating is owned by the ViewModel.");
}

/// <summary>
/// Visible when the bound value is non-null (and, for strings, non-blank).
/// Backs the QR panel, the composite preview, and the share/error status
/// lines -- all of which are simply absent until the thing they show exists.
/// </summary>
public class NullToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool present = value is not null && (value is not string s || !string.IsNullOrWhiteSpace(s));
        return present != Invert ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("One-way only.");
}
