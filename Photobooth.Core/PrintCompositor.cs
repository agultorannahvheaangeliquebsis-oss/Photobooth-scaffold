using System.Drawing;
using System.Runtime.Versioning;

namespace Photobooth.Core;

/// <summary>
/// Draws a photo (scaled to fit) plus any admin-placed logo/text elements
/// into a print template's cells. The single source of truth both
/// SpoolerPrinterService (the real print) and PrintTemplateEditorWindow's
/// live preview draw from -- factored out specifically so the editor's
/// preview is provably WYSIWYG rather than a second renderer that happens
/// to agree with the real one today and drift from it tomorrow.
/// </summary>
[SupportedOSPlatform("windows")]
public static class PrintCompositor
{
    /// <summary>Draws the photo scaled-to-fit into each of template.ComputeCellBounds's
    /// cells, then each of template.Elements on top of each cell.</summary>
    public static void DrawTemplate(Image photo, PrintTemplate template, Rectangle pageBounds, Graphics graphics)
    {
        foreach (Rectangle cell in template.ComputeCellBounds(pageBounds))
        {
            DrawScaledToFit(photo, cell, graphics);
            foreach (PrintTemplateElement element in template.Elements)
            {
                DrawElement(element, template.ComputeElementBounds(cell, element), graphics);
            }
        }
    }

    /// <summary>Renders template (with photoPath as the base photo) into a new Bitmap
    /// scaled so its longer side is previewWidthPx -- what
    /// PrintTemplateEditorWindow shows as its live preview.</summary>
    public static Bitmap RenderPreview(string photoPath, PrintTemplate template, int previewWidthPx)
    {
        using Image photo = Image.FromFile(photoPath);

        (int width, int height) = ComputePreviewDimensions(template, previewWidthPx);

        var preview = new Bitmap(width, height);
        using (Graphics graphics = Graphics.FromImage(preview))
        {
            graphics.Clear(Color.White);
            DrawTemplate(photo, template, new Rectangle(0, 0, width, height), graphics);
        }
        return preview;
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

    private static void DrawElement(PrintTemplateElement element, Rectangle bounds, Graphics graphics)
    {
        if (element.Kind == PrintTemplateElementKind.Logo)
        {
            if (element.ImagePath is not null && File.Exists(element.ImagePath))
            {
                using Image logo = Image.FromFile(element.ImagePath);
                DrawScaledToFit(logo, bounds, graphics);
            }
            return;
        }

        if (string.IsNullOrEmpty(element.Text))
        {
            return;
        }

        float fontSize = (float)(element.FontSizePercent * bounds.Height * 4); // percent is of cell height; *4 keeps typical values in a legible point-size range
        FontStyle style = element.Bold ? FontStyle.Bold : FontStyle.Regular;
        using var font = new Font(element.FontFamily, Math.Max(fontSize, 6f), style);
        using var brush = new SolidBrush(ColorTranslator.FromHtml(element.ColorHex));
        using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };

        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
        graphics.DrawString(element.Text, font, brush, bounds, format);
    }
}
