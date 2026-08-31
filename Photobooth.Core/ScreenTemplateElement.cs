namespace Photobooth.Core;

public enum ScreenTemplateElementKind { Text, Image, Shape }

/// <summary>Which guest-facing screen an element belongs to -- one tab in
/// ScreenTemplateEditorWindow per value, see BUILD_PLAN.md's Phase 6 scope text.
/// "Capture" covers Countdown (the only screen with a live camera feed to
/// overlay onto, same mapping Phase 5 established for ScreenSettings) and
/// "Sharing" covers the post-Complete QR/upload step, not a literal
/// BoothState.Sharing (there isn't one).</summary>
public enum ScreenTemplateScreen { Welcome, Capture, Sharing }

/// <summary>
/// One admin-placed overlay (text, image, or a plain color shape) drawn live
/// on top of a guest-facing screen -- the Visual Screen Editor's equivalent
/// of PrintTemplateElement, which does the same job for the print
/// composite. Percent-of-canvas position/size, same reasoning
/// PrintTemplateElement uses: the same element list scales correctly
/// however the window is sized. ColorHex doubles as the fill color for a
/// Shape element (no separate ShapeColorHex -- Kind alone tells a renderer
/// which meaning applies, same "one Kind, differently-used fields"
/// shape PrintTemplateElement already established for Text vs Logo).
/// </summary>
public record ScreenTemplateElement(
    ScreenTemplateScreen Screen,
    ScreenTemplateElementKind Kind,
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
        && (Kind == ScreenTemplateElementKind.Text ? !string.IsNullOrWhiteSpace(Text)
            : Kind == ScreenTemplateElementKind.Image ? !string.IsNullOrWhiteSpace(ImagePath)
            : true); // Shape only needs bounds + ColorHex, both already required above
}
