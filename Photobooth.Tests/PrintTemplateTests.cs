using Photobooth.Core;

namespace Photobooth.Tests;

// PrintTemplate's own IsValid/ComputeCellBounds coverage lives in
// MockServicesTests.cs's PrintTemplateTests class (predates this file);
// this one covers just the new PrintTemplateElement piece.
public class PrintTemplateElementTests
{
    [Fact]
    public void IsValid_TextElementWithText_ReturnsTrue()
    {
        var element = new PrintTemplateElement(PrintTemplateElementKind.Text, 0, 0, 0.5, 0.1, Text: "Hello");
        Assert.True(element.IsValid);
    }

    [Fact]
    public void IsValid_TextElementWithNoText_ReturnsFalse()
    {
        var element = new PrintTemplateElement(PrintTemplateElementKind.Text, 0, 0, 0.5, 0.1, Text: null);
        Assert.False(element.IsValid);
    }

    [Fact]
    public void IsValid_LogoElementWithImagePath_ReturnsTrue()
    {
        var element = new PrintTemplateElement(PrintTemplateElementKind.Logo, 0, 0, 0.5, 0.1, ImagePath: "./logo.png");
        Assert.True(element.IsValid);
    }

    [Fact]
    public void IsValid_LogoElementWithNoImagePath_ReturnsFalse()
    {
        var element = new PrintTemplateElement(PrintTemplateElementKind.Logo, 0, 0, 0.5, 0.1, ImagePath: null);
        Assert.False(element.IsValid);
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
        var element = new PrintTemplateElement(PrintTemplateElementKind.Text, x, y, width, height, Text: "Hello");
        Assert.False(element.IsValid);
    }
}
