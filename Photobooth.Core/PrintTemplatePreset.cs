namespace Photobooth.Core;

/// <summary>One "Apply a Preset Layout" option in PrintTemplateEditorWindow's New
/// Template dialog: a starting Layout/dimensions/Elements list an admin can pick
/// instead of always starting from a blank canvas. Every preset below is expressed
/// in PrintTemplateElement's real geometry so what the dialog shows a thumbnail of is
/// exactly what PrintCompositor will render -- no separate preview-only description.</summary>
public record PrintTemplatePreset(
    string Id, string Name, string Description, string Layout, double WidthInches, double HeightInches, int StripCopies,
    IReadOnlyList<PrintTemplateElement> Elements)
{
    /// <summary>How many PhotoSlot elements this preset needs -- shown in the dialog
    /// next to each option, same number PrintTemplate.RequiredPhotoCount would report
    /// once the preset is applied.</summary>
    public int RequiredPhotoCount
    {
        get
        {
            List<int> photoIndexes = Elements
                .Where(e => e.Kind == PrintTemplateElementKind.PhotoSlot)
                .Select(e => e.PhotoIndex ?? 0)
                .ToList();
            return photoIndexes.Count == 0 ? 1 : photoIndexes.Max() + 1;
        }
    }
}

public static class PrintTemplatePresets
{
    public static readonly PrintTemplatePreset Blank = new(
        "Blank", "Blank", "Start from an empty canvas", "Single", WidthInches: 4, HeightInches: 6, StripCopies: 1,
        Elements: Array.Empty<PrintTemplateElement>());

    public static readonly PrintTemplatePreset FourPosesGrid = new(
        "FourPosesGrid", "Four Poses – 2×2 Grid", "4 photo slots · 6 × 4 landscape",
        "Single", WidthInches: 6, HeightInches: 4, StripCopies: 1,
        Elements:
        [
            new(PrintTemplateElementKind.PhotoSlot, 0.03, 0.04, 0.46, 0.46, PhotoIndex: 0),
            new(PrintTemplateElementKind.PhotoSlot, 0.51, 0.04, 0.46, 0.46, PhotoIndex: 1),
            new(PrintTemplateElementKind.PhotoSlot, 0.03, 0.52, 0.46, 0.46, PhotoIndex: 2),
            new(PrintTemplateElementKind.PhotoSlot, 0.51, 0.52, 0.46, 0.46, PhotoIndex: 3),
            new(PrintTemplateElementKind.QrCode, 0.84, 0.85, 0.13, 0.13),
        ]);

    public static readonly PrintTemplatePreset FourPosesSingleStrip = new(
        "FourPosesSingleStrip", "Four Poses – Single Strip", "4 photo slots · 2 × 6 strip",
        "Single", WidthInches: 2, HeightInches: 6, StripCopies: 1,
        Elements:
        [
            new(PrintTemplateElementKind.PhotoSlot, 0.05, 0.02, 0.90, 0.22, PhotoIndex: 0),
            new(PrintTemplateElementKind.PhotoSlot, 0.05, 0.26, 0.90, 0.22, PhotoIndex: 1),
            new(PrintTemplateElementKind.PhotoSlot, 0.05, 0.50, 0.90, 0.22, PhotoIndex: 2),
            new(PrintTemplateElementKind.PhotoSlot, 0.05, 0.74, 0.90, 0.22, PhotoIndex: 3),
        ]);

    /// <summary>Two copies of the same 4 slots side by side on one 4x6 sheet -- the
    /// same 4 poses (PhotoIndex 0-3, so RequiredPhotoCount is still 4, not 8) drawn
    /// twice, once per column, so a guest can cut the sheet into two matching strips.
    /// Deliberately "Single" layout with 8 PhotoSlot elements rather than "Strip" with
    /// StripCopies: 2 -- once a template has any PhotoSlot element, PrintCompositor
    /// switches to slot mode and positions every element once against the whole page,
    /// ignoring StripCopies/ComputeCellBounds entirely (see PrintTemplateElement's own
    /// doc comment), so StripCopies can't be what produces the second copy here.</summary>
    public static readonly PrintTemplatePreset FourPosesDoubleStrip = new(
        "FourPosesDoubleStrip", "Four Poses – Double Strip", "4 photo slots, printed twice · 4 × 6 portrait",
        "Single", WidthInches: 4, HeightInches: 6, StripCopies: 1,
        Elements:
        [
            new(PrintTemplateElementKind.PhotoSlot, 0.03, 0.02, 0.44, 0.22, PhotoIndex: 0),
            new(PrintTemplateElementKind.PhotoSlot, 0.03, 0.26, 0.44, 0.22, PhotoIndex: 1),
            new(PrintTemplateElementKind.PhotoSlot, 0.03, 0.50, 0.44, 0.22, PhotoIndex: 2),
            new(PrintTemplateElementKind.PhotoSlot, 0.03, 0.74, 0.44, 0.22, PhotoIndex: 3),
            new(PrintTemplateElementKind.PhotoSlot, 0.53, 0.02, 0.44, 0.22, PhotoIndex: 0),
            new(PrintTemplateElementKind.PhotoSlot, 0.53, 0.26, 0.44, 0.22, PhotoIndex: 1),
            new(PrintTemplateElementKind.PhotoSlot, 0.53, 0.50, 0.44, 0.22, PhotoIndex: 2),
            new(PrintTemplateElementKind.PhotoSlot, 0.53, 0.74, 0.44, 0.22, PhotoIndex: 3),
        ]);

    public static readonly PrintTemplatePreset OneLargeThreeSmall = new(
        "OneLargeThreeSmall", "One Large, Three Small", "4 photo slots · 6 × 4 landscape",
        "Single", WidthInches: 6, HeightInches: 4, StripCopies: 1,
        Elements:
        [
            new(PrintTemplateElementKind.PhotoSlot, 0.03, 0.04, 0.60, 0.92, PhotoIndex: 0),
            new(PrintTemplateElementKind.PhotoSlot, 0.66, 0.04, 0.31, 0.29, PhotoIndex: 1),
            new(PrintTemplateElementKind.PhotoSlot, 0.66, 0.355, 0.31, 0.29, PhotoIndex: 2),
            new(PrintTemplateElementKind.PhotoSlot, 0.66, 0.67, 0.31, 0.29, PhotoIndex: 3),
        ]);

    /// <summary>No PhotoSlot elements at all -- stays in PrintCompositor's legacy
    /// per-cell mode, where the one captured photo is repeated into every one of
    /// StripCopies cells (see PrintTemplate.ComputeCellBounds), rather than slot mode.</summary>
    public static readonly PrintTemplatePreset SinglePoseRepeatedStrip = new(
        "SinglePoseRepeatedStrip", "Single Pose – Repeated Strip", "1 photo slot, printed twice · 2 × 6 strip",
        "Strip", WidthInches: 2, HeightInches: 6, StripCopies: 2,
        Elements: Array.Empty<PrintTemplateElement>());

    public static readonly IReadOnlyList<PrintTemplatePreset> All =
    [
        Blank, FourPosesGrid, FourPosesSingleStrip, FourPosesDoubleStrip, OneLargeThreeSmall, SinglePoseRepeatedStrip,
    ];
}
