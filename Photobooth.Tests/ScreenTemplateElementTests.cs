using Photobooth.Core;

namespace Photobooth.Tests;

// Same shape as PrintTemplateElementTests -- ScreenTemplateElement is the
// Visual Screen Editor's (Phase 6) equivalent record, with Shape added as a
// third Kind that needs neither Text nor ImagePath to be valid.
public class ScreenTemplateElementTests
{
    [Fact]
    public void IsValid_TextElementWithText_ReturnsTrue()
    {
        var element = new ScreenTemplateElement(ScreenTemplateScreen.Welcome, ScreenTemplateElementKind.Text, 0, 0, 0.5, 0.1, Text: "Hello");
        Assert.True(element.IsValid);
    }

    [Fact]
    public void IsValid_TextElementWithNoText_ReturnsFalse()
    {
        var element = new ScreenTemplateElement(ScreenTemplateScreen.Welcome, ScreenTemplateElementKind.Text, 0, 0, 0.5, 0.1, Text: null);
        Assert.False(element.IsValid);
    }

    [Fact]
    public void IsValid_ImageElementWithImagePath_ReturnsTrue()
    {
        var element = new ScreenTemplateElement(ScreenTemplateScreen.Capture, ScreenTemplateElementKind.Image, 0, 0, 0.5, 0.1, ImagePath: "./logo.png");
        Assert.True(element.IsValid);
    }

    [Fact]
    public void IsValid_ImageElementWithNoImagePath_ReturnsFalse()
    {
        var element = new ScreenTemplateElement(ScreenTemplateScreen.Capture, ScreenTemplateElementKind.Image, 0, 0, 0.5, 0.1, ImagePath: null);
        Assert.False(element.IsValid);
    }

    [Fact]
    public void IsValid_ShapeElementWithNeitherTextNorImagePath_ReturnsTrue()
    {
        // Shape only needs valid bounds + ColorHex (both always present, ColorHex has
        // a default) -- unlike Text/Image, it has nothing else to require.
        var element = new ScreenTemplateElement(ScreenTemplateScreen.Sharing, ScreenTemplateElementKind.Shape, 0, 0, 0.5, 0.1);
        Assert.True(element.IsValid);
    }

    [Theory]
    [InlineData(-0.1, 0, 0.5, 0.5)]
    [InlineData(0, -0.1, 0.5, 0.5)]
    [InlineData(1.1, 0, 0.5, 0.5)]
    [InlineData(0, 0, 0, 0.5)]
    [InlineData(0, 0, 0.5, 0)]
    [InlineData(0, 0, 1.1, 0.5)]
    public void IsValid_OutOfRangeBounds_ReturnsFalse(double x, double y, double width, double height)
    {
        var element = new ScreenTemplateElement(ScreenTemplateScreen.Welcome, ScreenTemplateElementKind.Text, x, y, width, height, Text: "Hello");
        Assert.False(element.IsValid);
    }
}
