using System.Drawing;
using System.Drawing.Printing;
using System.Runtime.Versioning;

namespace Photobooth.Core;

/// <summary>
/// Real IPrinterService, sent through the Windows print spooler via
/// System.Drawing.Printing.PrintDocument -- no vendor SDK needed here,
/// unlike the camera, since any driver-backed printer (DNP, Selphy,
/// Epson) shows up as a normal Windows printer once its driver is
/// installed. Reads the target printer name from PHOTOBOOTH_PRINTER_NAME
/// (same environment-variable pattern as CLOUDINARY_URL and
/// PHOTOBOOTH_DB_CONNECTION), falling back to the Windows default printer
/// if unset.
/// </summary>
[SupportedOSPlatform("windows")]
public class SpoolerPrinterService : IPrinterService
{
    private const string EnvVarName = "PHOTOBOOTH_PRINTER_NAME";

    private readonly string? _printerName;

    public SpoolerPrinterService()
    {
        _printerName = Environment.GetEnvironmentVariable(EnvVarName);
    }

    public Task PrintAsync(string imagePath, PrintTemplate template, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        // PrintDocument.Print() blocks until the job is handed off to the
        // spooler (not until the physical page comes out), same "printed"
        // meaning PrintAsync already had with MockPrinterService.
        return Task.Run(() =>
        {
            using var image = Image.FromFile(imagePath);
            using var document = new PrintDocument();

            if (!string.IsNullOrWhiteSpace(_printerName))
            {
                document.PrinterSettings.PrinterName = _printerName;
            }
            if (!document.PrinterSettings.IsValid)
            {
                throw new InvalidOperationException(
                    $"Printer '{document.PrinterSettings.PrinterName}' is not installed or not available. " +
                    $"Set {EnvVarName} to an installed printer's exact name, or install/attach the booth printer.");
            }

            // PaperSize.Width/Height are in hundredths of an inch, per the
            // PrintDocument API -- so a 2x6 strip template becomes a 200x600
            // custom paper size, same units as System.Windows.Forms uses.
            document.DefaultPageSettings.PaperSize = new PaperSize(
                "PhotoboothTemplate", (int)(template.WidthInches * 100), (int)(template.HeightInches * 100));

            document.PrintPage += (_, e) => Draw(image, e, template);
            document.Print();
        }, ct);
    }

    /// <summary>Draws the photo into each cell PrintTemplate.ComputeCellBounds hands back
    /// -- one full-page cell for "Single", one per strip copy for "Strip" -- scaled to
    /// fit and centered within its own cell.</summary>
    private static void Draw(Image image, PrintPageEventArgs e, PrintTemplate template)
    {
        foreach (Rectangle cell in template.ComputeCellBounds(e.MarginBounds))
        {
            DrawScaledToFit(image, cell, e.Graphics!);
        }
    }

    private static void DrawScaledToFit(Image image, Rectangle bounds, Graphics graphics)
    {
        double scale = Math.Min((double)bounds.Width / image.Width, (double)bounds.Height / image.Height);
        int width = (int)(image.Width * scale);
        int height = (int)(image.Height * scale);
        int x = bounds.Left + (bounds.Width - width) / 2;
        int y = bounds.Top + (bounds.Height - height) / 2;
        graphics.DrawImage(image, x, y, width, height);
    }
}
