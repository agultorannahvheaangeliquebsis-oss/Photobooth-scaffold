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

    public Task PrintAsync(string imagePath, CancellationToken ct = default)
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

            document.PrintPage += (_, e) => DrawScaledToMargins(image, e);
            document.Print();
        }, ct);
    }

    /// <summary>Scales the captured photo to fit within the page margins, preserving aspect ratio and centering it -- the booth print layout (strip vs. 4x6, borders, branding) is future work, this just proves the spooler round trip.</summary>
    private static void DrawScaledToMargins(Image image, PrintPageEventArgs e)
    {
        Rectangle bounds = e.MarginBounds;
        double scale = Math.Min((double)bounds.Width / image.Width, (double)bounds.Height / image.Height);
        int width = (int)(image.Width * scale);
        int height = (int)(image.Height * scale);
        int x = bounds.Left + (bounds.Width - width) / 2;
        int y = bounds.Top + (bounds.Height - height) / 2;
        e.Graphics!.DrawImage(image, x, y, width, height);
    }
}
