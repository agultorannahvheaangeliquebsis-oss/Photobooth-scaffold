namespace Photobooth.Core;

public enum PrintTemplateElementKind { Logo, Text }

/// <summary>
/// One admin-placed overlay (a logo image or a text caption) stamped into
/// every print cell PrintTemplate.ComputeCellBounds returns -- one Strip
/// copy gets the same element list as a Single layout's one cell, not a
/// separate list per copy. Position/size are stored as fractions (0-1) of
/// that cell's bounds, not absolute pixels/inches, specifically so the same
/// element list scales correctly however WidthInches/HeightInches later
/// change -- see PrintTemplate.ComputeElementBounds.
/// </summary>
public record PrintTemplateElement(
    PrintTemplateElementKind Kind,
    double XPercent,
    double YPercent,
    double WidthPercent,
    double HeightPercent,
    string? Text = null,
    string? ImagePath = null,
    string FontFamily = "Segoe UI",
    double FontSizePercent = 0.05,
    bool Bold = false,
    string ColorHex = "#202124")
{
    public bool IsValid =>
        XPercent is >= 0 and <= 1 && YPercent is >= 0 and <= 1
        && WidthPercent is > 0 and <= 1 && HeightPercent is > 0 and <= 1
        && (Kind == PrintTemplateElementKind.Text ? !string.IsNullOrWhiteSpace(Text) : !string.IsNullOrWhiteSpace(ImagePath));
}
