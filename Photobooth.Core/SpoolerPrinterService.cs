using System.Drawing;
using System.Drawing.Printing;
using System.Linq;
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

    public Task PrintAsync(IReadOnlyList<string> imagePaths, PrintTemplate template, PrintRenderContext? context = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        // PrintDocument.Print() blocks until the job is handed off to the
        // spooler (not until the physical page comes out), same "printed"
        // meaning PrintAsync already had with MockPrinterService.
        return Task.Run(() =>
        {
            var images = imagePaths.Select(path => (Image)Image.FromFile(path)).ToList();
            try
            {
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

                // Pre-flight the spooler's own status. Print() below only means
                // "handed to the spooler", so without this an out-of-paper or
                // offline printer swallowed every job silently while the booth
                // kept cheerfully telling guests their photo was on its way --
                // see PrinterStatus for why that's the classic unattended-booth
                // failure. Checked before spooling rather than after so the job
                // doesn't pile up in a queue that will need clearing by hand.
                if (PrinterStatus.DescribeProblem(document.PrinterSettings.PrinterName) is string problem)
                {
                    throw new PrinterUnavailableException(problem);
                }

                // PaperSize.Width/Height are in hundredths of an inch, per the
                // PrintDocument API -- so a 2x6 strip template becomes a 200x600
                // custom paper size, same units as System.Windows.Forms uses.
                document.DefaultPageSettings.PaperSize = new PaperSize(
                    "PhotoboothTemplate", (int)(template.WidthInches * 100), (int)(template.HeightInches * 100));

                document.PrintPage += (_, e) => Draw(images, e, template, context);
                document.Print();

                // And once more afterward: a printer can go offline or run out
                // between the check above and the job actually reaching it, and
                // that job is the one the guest is standing there waiting for.
                if (PrinterStatus.DescribeProblem(document.PrinterSettings.PrinterName) is string postSpoolProblem)
                {
                    throw new PrinterUnavailableException(postSpoolProblem);
                }
            }
            finally
            {
                foreach (Image image in images)
                {
                    image.Dispose();
                }
            }
        }, ct);
    }

    /// <summary>Draws the captured pose(s) (plus any admin-placed overlay elements) into
    /// the page -- one full-page cell repeating images[0] for a legacy "Single"/"Strip"
    /// template, or one photo per PhotoSlot for a true multi-pose template. Delegates to
    /// PrintCompositor, the same code PrintTemplateEditorWindow's live preview calls, so
    /// what the admin sees in the editor is provably what actually prints.</summary>
    private static void Draw(IReadOnlyList<Image> images, PrintPageEventArgs e, PrintTemplate template, PrintRenderContext? context) =>
        PrintCompositor.DrawTemplate(images, template, e.MarginBounds, e.Graphics!, context);
}
