namespace Photobooth.Core;

public enum PrintTemplateElementKind { Logo, Text, Image, Shape, QrCode, SessionData, PhotoSlot }

/// <summary>
/// One admin-placed overlay stamped into a print template. When the
/// template has no PhotoSlot elements, every element is stamped into every
/// print cell PrintTemplate.ComputeCellBounds returns -- one Strip copy
/// gets the same element list as a Single layout's one cell, not a separate
/// list per copy. When PhotoSlot elements are present, PrintCompositor
/// switches to "slot mode": every element (including each PhotoSlot) is
/// positioned once against the whole page instead of being repeated per
/// cell -- see PrintCompositor.DrawTemplate. Position/size are always
/// stored as fractions (0-1) of the bounds they're drawn against, not
/// absolute pixels/inches, specifically so the same element list scales
/// correctly however WidthInches/HeightInches later change -- see
/// PrintTemplate.ComputeElementBounds.
///
/// Kind-specific data reuses existing properties rather than growing a new
/// column per kind: Image behaves exactly like Logo (ImagePath); SessionData
/// stores its field key ("EventName"/"Date"/"Time") in Text, the same way
/// Logo already reuses ImagePath instead of Text. Shape uses the new
/// ShapeType property ("Rectangle"/"Ellipse") plus the existing ColorHex as
/// its fill color. PhotoSlot uses the new PhotoIndex property (0-based,
/// which captured pose to draw here) and needs neither Text nor ImagePath.
/// QrCode needs nothing beyond its bounds -- it always encodes whatever
/// PrintRenderContext.PhotoUrl is live at print time.
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
    string ColorHex = "#202124",
    string? ShapeType = null,
    int? PhotoIndex = null)
{
    public bool IsValid =>
        XPercent is >= 0 and <= 1 && YPercent is >= 0 and <= 1
        && WidthPercent is > 0 and <= 1 && HeightPercent is > 0 and <= 1
        && Kind switch
        {
            PrintTemplateElementKind.Text => !string.IsNullOrWhiteSpace(Text),
            PrintTemplateElementKind.Logo or PrintTemplateElementKind.Image => !string.IsNullOrWhiteSpace(ImagePath),
            PrintTemplateElementKind.Shape => !string.IsNullOrWhiteSpace(ShapeType),
            PrintTemplateElementKind.SessionData => !string.IsNullOrWhiteSpace(Text),
            PrintTemplateElementKind.PhotoSlot => PhotoIndex is >= 0,
            PrintTemplateElementKind.QrCode => true,
            _ => false,
        };
}
