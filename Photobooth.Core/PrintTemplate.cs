using System.Drawing;

namespace Photobooth.Core;

/// <summary>
/// Admin-editable print layout: paper size, and whether the photo is printed
/// once ("Single", e.g. a 4x6) or repeated down a narrow strip ("Strip", e.g.
/// a 2x6 photo-booth strip). Read fresh every session as part of
/// BoothSettings, same as CountdownSeconds/GlamFilterEnabled -- an admin's
/// save takes effect for the very next guest, not the app's next restart.
/// See AdminWindow's Settings section.
/// </summary>
public record PrintTemplate(string Layout, double WidthInches, double HeightInches, int StripCopies)
{
    public static readonly PrintTemplate Default = new("Single", WidthInches: 4, HeightInches: 6, StripCopies: 1);

    public bool IsValid =>
        (Layout == "Single" || Layout == "Strip")
        && WidthInches > 0 && HeightInches > 0
        && StripCopies >= 1;

    /// <summary>
    /// Splits the printable page area into the rectangle(s) the photo should
    /// be drawn into -- one full-bounds rectangle for "Single", StripCopies
    /// equal-height rectangles stacked top to bottom for "Strip". Pure
    /// geometry (System.Drawing.Rectangle isn't part of the Windows-only
    /// GDI+ surface, unlike Image/Graphics), so this is unit-testable
    /// without a real printer -- SpoolerPrinterService is what actually
    /// draws into these rectangles.
    /// </summary>
    public IReadOnlyList<Rectangle> ComputeCellBounds(Rectangle pageBounds)
    {
        if (Layout != "Strip")
        {
            return new[] { pageBounds };
        }

        int cellHeight = pageBounds.Height / StripCopies;
        var cells = new Rectangle[StripCopies];
        for (int i = 0; i < StripCopies; i++)
        {
            cells[i] = new Rectangle(pageBounds.Left, pageBounds.Top + i * cellHeight, pageBounds.Width, cellHeight);
        }
        return cells;
    }
}
