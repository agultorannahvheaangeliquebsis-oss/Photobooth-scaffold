using System.Windows;
using Photobooth.UI.Converters;
using Photobooth.UI.ViewModels;

namespace Photobooth.UI.Tests;

public class EnumToVisibilityConverterTests
{
    private readonly EnumToVisibilityConverter _converter = new();

    [Theory]
    [InlineData("Idle", "Idle")]
    [InlineData("idle", "Idle")] // case-insensitive
    [InlineData("Countdown,Capture,Processing,Review", "Capture")] // comma-separated set
    public void Convert_MatchingState_ReturnsVisible(string parameter, string value)
    {
        object result = _converter.Convert(Enum.Parse<KioskScreen>(value), typeof(Visibility), parameter, null!);
        Assert.Equal(Visibility.Visible, result);
    }

    [Fact]
    public void Convert_NonMatchingState_ReturnsFallback()
    {
        object result = _converter.Convert(KioskScreen.Idle, typeof(Visibility), "Countdown", null!);
        Assert.Equal(Visibility.Collapsed, result);
    }

    [Fact]
    public void Convert_NullValue_ReturnsFallback()
    {
        object result = _converter.Convert(null, typeof(Visibility), "Idle", null!);
        Assert.Equal(Visibility.Collapsed, result);
    }

    [Fact]
    public void Convert_CustomFallback_IsHonored()
    {
        var converter = new EnumToVisibilityConverter { FallbackVisibility = Visibility.Hidden };
        object result = converter.Convert(KioskScreen.Idle, typeof(Visibility), "Countdown", null!);
        Assert.Equal(Visibility.Hidden, result);
    }
}

public class EnumToTagConverterTests
{
    private readonly EnumToTagConverter _converter = new();

    [Fact]
    public void Convert_MatchingValue_ReturnsSelected()
    {
        Assert.Equal("selected", _converter.Convert("Photo", typeof(string), "Photo", null!));
    }

    [Fact]
    public void Convert_NonMatchingValue_ReturnsUnselected()
    {
        Assert.Equal("unselected", _converter.Convert("Photo", typeof(string), "GIF", null!));
    }
}

public class BoolToVisibilityConverterTests
{
    [Theory]
    [InlineData(true, false, Visibility.Visible)]
    [InlineData(false, false, Visibility.Collapsed)]
    [InlineData(true, true, Visibility.Collapsed)]
    [InlineData(false, true, Visibility.Visible)]
    public void Convert_ReturnsExpectedVisibility(bool input, bool invert, Visibility expected)
    {
        var converter = new BoolToVisibilityConverter { Invert = invert };
        Assert.Equal(expected, converter.Convert(input, typeof(Visibility), null, null!));
    }
}

public class NullToVisibilityConverterTests
{
    private readonly NullToVisibilityConverter _converter = new();

    [Fact]
    public void Convert_NonNullObject_ReturnsVisible()
    {
        Assert.Equal(Visibility.Visible, _converter.Convert(new object(), typeof(Visibility), null, null!));
    }

    [Fact]
    public void Convert_Null_ReturnsCollapsed()
    {
        Assert.Equal(Visibility.Collapsed, _converter.Convert(null, typeof(Visibility), null, null!));
    }

    [Fact]
    public void Convert_BlankString_ReturnsCollapsed()
    {
        Assert.Equal(Visibility.Collapsed, _converter.Convert("   ", typeof(Visibility), null, null!));
    }

    [Fact]
    public void Convert_NonBlankString_ReturnsVisible()
    {
        Assert.Equal(Visibility.Visible, _converter.Convert("hello", typeof(Visibility), null, null!));
    }

    [Fact]
    public void Convert_Inverted_FlipsResult()
    {
        var converter = new NullToVisibilityConverter { Invert = true };
        Assert.Equal(Visibility.Collapsed, converter.Convert("hello", typeof(Visibility), null, null!));
        Assert.Equal(Visibility.Visible, converter.Convert(null, typeof(Visibility), null, null!));
    }
}

public class RatingToStarConverterTests
{
    private readonly RatingToStarConverter _converter = new();

    [Theory]
    [InlineData(0, "1", "☆")]
    [InlineData(3, "3", "★")]
    [InlineData(3, "4", "☆")]
    [InlineData(5, "5", "★")]
    public void Convert_ReturnsFilledOrOutlineStar(int rating, string rank, string expected)
    {
        Assert.Equal(expected, _converter.Convert(rating, typeof(string), rank, null!));
    }
}

public class PathToImageSourceConverterTests
{
    private readonly PathToImageSourceConverter _converter = new();

    [Fact]
    public void Convert_MissingFile_ReturnsNull()
    {
        Assert.Null(_converter.Convert(@"C:\definitely\not\a\real\path.png", typeof(object), null, null!));
    }

    [Fact]
    public void Convert_NullValue_ReturnsNull()
    {
        Assert.Null(_converter.Convert(null, typeof(object), null, null!));
    }

    [Fact]
    public void Convert_BlankPath_ReturnsNull()
    {
        Assert.Null(_converter.Convert("   ", typeof(object), null, null!));
    }
}
