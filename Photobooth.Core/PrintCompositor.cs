using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.Versioning;

namespace Photobooth.Core;

/// <summary>
/// Draws the captured photo(s) plus any admin-placed overlay elements into
/// a print template's page. The single source of truth both
/// SpoolerPrinterService (the real print) and PrintTemplateEditorWindow's
/// live preview draw from -- factored out specifically so the editor's
/// preview is provably WYSIWYG rather than a second renderer that happens
/// to agree with the real one today and drift from it tomorrow.
/// </summary>
[SupportedOSPlatform("windows")]
public static class PrintCompositor
{
    /// <summary>
    /// Two rendering modes, chosen by whether template.Elements contains any
    /// PhotoSlot element:
    ///  - Slot mode (true multi-pose templates): every element, including
    ///    each PhotoSlot, is positioned once against the whole page --
    ///    photos[element.PhotoIndex] is drawn for a PhotoSlot, everything
    ///    else draws through DrawElement. No cell repetition.
    ///  - Legacy cell mode (every template that predates PhotoSlot):
    ///    unchanged behavior -- photos[0] is repeated into every
    ///    template.ComputeCellBounds cell, with the rest of Elements drawn
    ///    on top of each cell.
    /// </summary>
    public static void DrawTemplate(IReadOnlyList<Image> photos, PrintTemplate template, Rectangle pageBounds, Graphics graphics, PrintRenderContext? context = null)
    {
        bool slotMode = template.Elements.Any(e => e.Kind == PrintTemplateElementKind.PhotoSlot);
        if (slotMode)
        {
            foreach (PrintTemplateElement element in template.Elements)
            {
                Rectangle bounds = template.ComputeElementBounds(pageBounds, element);
                if (element.Kind == PrintTemplateElementKind.PhotoSlot)
                {
                    int index = Math.Clamp(element.PhotoIndex ?? 0, 0, photos.Count - 1);
                    DrawScaledToFit(photos[index], bounds, graphics);
                }
                else
                {
                    DrawElement(element, bounds, graphics, context);
                }
            }
            return;
        }

        foreach (Rectangle cell in template.ComputeCellBounds(pageBounds))
        {
            DrawScaledToFit(photos[0], cell, graphics);
            foreach (PrintTemplateElement element in template.Elements)
            {
                DrawElement(element, template.ComputeElementBounds(cell, element), graphics, context);
            }
        }
    }

    /// <summary>Renders template (with photoPaths as the captured pose(s), in PhotoIndex
    /// order) into a new Bitmap scaled so its longer side is previewWidthPx -- what
    /// PrintTemplateEditorWindow shows as its live preview.</summary>
    public static Bitmap RenderPreview(IReadOnlyList<string> photoPaths, PrintTemplate template, int previewWidthPx, PrintRenderContext? context = null)
    {
        var photos = photoPaths.Select(path => (Image)Image.FromFile(path)).ToList();
        try
        {
            (int width, int height) = ComputePreviewDimensions(template, previewWidthPx);

            var preview = new Bitmap(width, height);
            using (Graphics graphics = Graphics.FromImage(preview))
            {
                graphics.Clear(Color.White);
                DrawTemplate(photos, template, new Rectangle(0, 0, width, height), graphics, context);
            }
            return preview;
        }
        finally
        {
            foreach (Image photo in photos)
            {
                photo.Dispose();
            }
        }
    }

    /// <summary>Pixel dimensions for a preview of this template, scaled so its longer
    /// side is previewWidthPx and the aspect ratio matches WidthInches:HeightInches --
    /// shared by RenderPreview and PrintTemplateEditorWindow so the editor's canvas is
    /// sized identically to what RenderPreview actually renders.</summary>
    public static (int Width, int Height) ComputePreviewDimensions(PrintTemplate template, int previewWidthPx)
    {
        double aspect = template.WidthInches / template.HeightInches; // width:height

        // The longer side becomes previewWidthPx -- a portrait 4x6 (aspect < 1)
        // is taller than it is wide, so height is the longer side.
        return aspect <= 1
            ? ((int)(previewWidthPx * aspect), previewWidthPx)
            : (previewWidthPx, (int)(previewWidthPx / aspect));
    }

    public static void DrawScaledToFit(Image image, Rectangle bounds, Graphics graphics)
    {
        double scale = Math.Min((double)bounds.Width / image.Width, (double)bounds.Height / image.Height);
        int width = (int)(image.Width * scale);
        int height = (int)(image.Height * scale);
        int x = bounds.Left + (bounds.Width - width) / 2;
        int y = bounds.Top + (bounds.Height - height) / 2;
        graphics.DrawImage(image, x, y, width, height);
    }

    private static void DrawElement(PrintTemplateElement element, Rectangle bounds, Graphics graphics, PrintRenderContext? context)
    {
        switch (element.Kind)
        {
            case PrintTemplateElementKind.Logo:
            case PrintTemplateElementKind.Image:
                if (element.ImagePath is not null && File.Exists(element.ImagePath))
                {
                    using Image image = Image.FromFile(element.ImagePath);
                    DrawScaledToFit(image, bounds, graphics);
                }
                return;

            case PrintTemplateElementKind.Shape:
                DrawShape(element, bounds, graphics);
                return;

            case PrintTemplateElementKind.QrCode:
                DrawQrCode(context, bounds, graphics);
                return;

            case PrintTemplateElementKind.SessionData:
                DrawText(ResolveSessionDataText(element.Text, context), element, bounds, graphics);
                return;

            case PrintTemplateElementKind.Text:
                DrawText(element.Text, element, bounds, graphics);
                return;

            // PhotoSlot never reaches here -- DrawTemplate's slot-mode branch
            // handles it directly, and legacy cell mode never contains one
            // (slot mode is entered whenever any PhotoSlot element exists).
        }
    }

    private static void DrawShape(PrintTemplateElement element, Rectangle bounds, Graphics graphics)
    {
        using var brush = new SolidBrush(ColorTranslator.FromHtml(element.ColorHex));
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        if (element.ShapeType == "Ellipse")
        {
            graphics.FillEllipse(brush, bounds);
        }
        else
        {
            graphics.FillRectangle(brush, bounds);
        }
    }

    /// <summary>Skips drawing entirely when there's no photo URL yet (upload still in
    /// flight, or failed) -- same "feature invisible until configured/ready" reasoning
    /// every other not-yet-available gap in this codebase already follows, rather than
    /// drawing a broken placeholder.</summary>
    private static void DrawQrCode(PrintRenderContext? context, Rectangle bounds, Graphics graphics)
    {
        if (context?.PhotoUrl is null)
        {
            return;
        }

        byte[] png = QrCodeGenerator.GeneratePng(context.PhotoUrl.ToString());
        using var stream = new MemoryStream(png);
        using Image qr = Image.FromStream(stream);
        DrawScaledToFit(qr, bounds, graphics);
    }

    /// <summary>Resolves a SessionData element's field key (stored in its Text property,
    /// same reuse pattern Logo already applies to ImagePath) against the live print
    /// context. An unresolved key (context missing, or a key this booth doesn't
    /// recognize) draws as empty rather than showing the raw key name to a guest.</summary>
    private static string ResolveSessionDataText(string? fieldKey, PrintRenderContext? context) => fieldKey switch
    {
        "EventName" => context?.EventName ?? string.Empty,
        "Date" => context?.PrintedAt?.ToString("MMMM d, yyyy") ?? string.Empty,
        "Time" => context?.PrintedAt?.ToString("h:mm tt") ?? string.Empty,
        _ => string.Empty,
    };

    private static void DrawText(string? text, PrintTemplateElement element, Rectangle bounds, Graphics graphics)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        float fontSize = (float)(element.FontSizePercent * bounds.Height * 4); // percent is of cell height; *4 keeps typical values in a legible point-size range
        FontStyle style = element.Bold ? FontStyle.Bold : FontStyle.Regular;
        using var font = new Font(element.FontFamily, Math.Max(fontSize, 6f), style);
        using var brush = new SolidBrush(ColorTranslator.FromHtml(element.ColorHex));
        using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };

        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
        graphics.DrawString(text, font, brush, bounds, format);
    }
}
