using Photobooth.Core;

namespace Photobooth.Tests;

public class BoothThemeTests
{
    [Fact]
    public void IsValid_Default_ReturnsTrue()
    {
        Assert.True(BoothTheme.Default.IsValid);
    }

    [Theory]
    [InlineData("365C58")] // missing '#'
    [InlineData("#365C5")] // too short
    [InlineData("#GGGGGG")] // not hex
    [InlineData("")]
    public void IsValid_BadAccentHex_ReturnsFalse(string badHex)
    {
        var theme = BoothTheme.Default with { AccentColorHex = badHex };
        Assert.False(theme.IsValid);
    }

    [Theory]
    [InlineData("365C58")]
    [InlineData("#12345")]
    public void IsValid_BadCanvasOrInkHex_ReturnsFalse(string badHex)
    {
        Assert.False((BoothTheme.Default with { CanvasColorHex = badHex }).IsValid);
        Assert.False((BoothTheme.Default with { InkColorHex = badHex }).IsValid);
    }

    [Fact]
    public void IsValid_EmptyEventName_ReturnsFalse()
    {
        var theme = BoothTheme.Default with { EventName = "   " };
        Assert.False(theme.IsValid);
    }

    [Fact]
    public void IsValid_NullLogoImagePath_StillValid()
    {
        // Logo is optional -- a theme with just colors + a name is valid.
        Assert.Null(BoothTheme.Default.LogoImagePath);
        Assert.True(BoothTheme.Default.IsValid);
    }
}
