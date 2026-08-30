using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.Versioning;

namespace Photobooth.Core;

/// <summary>
/// Real IPhotoBrandingService: composites a caption bar (studio name +
/// date) onto the bottom of the photo via GDI+. Windows-only, same
/// rationale as SpoolerPrinterService -- the whole solution already only
/// runs on the Windows booth machine.
/// </summary>
[SupportedOSPlatform("windows")]
public class GdiPhotoBrandingService : IPhotoBrandingService
{
    public Task<string> ApplyBrandingAsync(string photoPath, string studioName, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        // GDI+ compositing is synchronous CPU work; run it off the calling
        // thread same as SpoolerPrinterService does for PrintDocument.Print().
        return Task.Run(() =>
        {
            using Bitmap original = GdiImageHelpers.LoadIndependentCopy(photoPath);
            int bannerHeight = Math.Max(28, original.Height / 10);

            using var branded = new Bitmap(original.Width, original.Height + bannerHeight);
            using (Graphics g = Graphics.FromImage(branded))
            {
                g.DrawImage(original, 0, 0, original.Width, original.Height);
                g.FillRectangle(Brushes.Black, 0, original.Height, original.Width, bannerHeight);

                using var font = new Font("Segoe UI", bannerHeight * 0.4f, FontStyle.Bold);
                string caption = $"{studioName}  |  {DateTime.Now:MMM d, yyyy}";
                SizeF textSize = g.MeasureString(caption, font);
                float textX = (original.Width - textSize.Width) / 2f;
                float textY = original.Height + ((bannerHeight - textSize.Height) / 2f);
                g.DrawString(caption, font, Brushes.White, textX, textY);
            }

            string outputPath = GdiImageHelpers.DerivedJpegPath(photoPath, "_branded");
            branded.Save(outputPath, ImageFormat.Jpeg);
            return outputPath;
        }, ct);
    }
}
