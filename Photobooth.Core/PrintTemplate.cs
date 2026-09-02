using System.Drawing;
using System.Linq;

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
    public static readonly PrintTemplate Default = new("Single", WidthInches: 4, HeightInches: 6, StripCopies: 1) { Name = "Default" };

    /// <summary>0 for the location's one "live" print setup (the Location row's own
    /// PrintLayout/PrintWidthInches/PrintHeightInches/PrintStripCopies columns plus
    /// whatever PrintTemplateElement rows have PrintTemplateId IS NULL -- what every
    /// pre-library PrintTemplate already was). A positive id identifies one saved row
    /// in the new PrintTemplate library table (see PrintTemplateRepository) --
    /// PrintTemplateEditorWindow's switcher loads a library entry's Layout/dimensions/
    /// Elements into its in-memory working copy, still with this same Id, so Save can
    /// tell "editing the live setup" (Id == 0) apart from "editing a saved template
    /// that hasn't been activated onto the live setup yet".</summary>
    public int Id { get; init; } = 0;

    /// <summary>Admin-chosen label shown in PrintTemplateEditorWindow's template
    /// switcher and "Edit templates" list. Only meaningful once Id > 0 -- the live
    /// setup (Id == 0) has no row of its own to name.</summary>
    public string Name { get; init; } = "Untitled";

    /// <summary>Whether this saved template appears in the guest-facing "Choose
    /// Template" picker (see ScreenSettings.ChooseTemplateEnabled) -- guests only
    /// ever pick from favorited templates, same as dslrBooth's own favorite-star
    /// convention. Only meaningful once Id > 0.</summary>
    public bool IsFavorite { get; init; } = false;

    /// <summary>Admin-placed logo/text overlays, drawn on top of the photo in every
    /// cell (see PrintCompositor.DrawTemplate). An init-only property outside the
    /// primary constructor, not a 5th positional parameter -- same reasoning
    /// BoothSettings.Theme uses -- so every existing `new PrintTemplate(...)` call
    /// site keeps compiling unchanged with an empty element list.</summary>
    public IReadOnlyList<PrintTemplateElement> Elements { get; init; } = Array.Empty<PrintTemplateElement>();

    /// <summary>How many distinct captured photos this template needs -- 1 for every
    /// template that existed before PhotoSlot elements (the single capture is repeated
    /// into every cell, see PrintCompositor's legacy cell mode), or one more than the
    /// highest PhotoIndex among this template's PhotoSlot elements otherwise. Read by
    /// BoothStateMachine before Capturing to decide how many pose/countdown cycles to
    /// run.</summary>
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

    /// <summary>Translates one element's cell-relative percentages into actual pixel
    /// bounds within a given cell -- pure geometry, same unit-testability as
    /// ComputeCellBounds, so the percent math can be verified without a real
    /// printer or GDI+.</summary>
    public Rectangle ComputeElementBounds(Rectangle cellBounds, PrintTemplateElement element) => new(
        cellBounds.Left + (int)(element.XPercent * cellBounds.Width),
        cellBounds.Top + (int)(element.YPercent * cellBounds.Height),
        (int)(element.WidthPercent * cellBounds.Width),
        (int)(element.HeightPercent * cellBounds.Height));
}
