namespace Photobooth.Core;

/// <summary>
/// Abstracts the printer. Real implementation sends to the Windows print
/// spooler once a DNP/Selphy/Epson driver is installed -- no vendor SDK
/// needed there, unlike the camera.
/// </summary>
public interface IPrinterService
{
    /// <param name="imagePaths">One captured pose per PrintTemplate.RequiredPhotoCount slot,
    /// in PhotoIndex order -- a single-element list for every template that predates
    /// PhotoSlot elements (the common case), matching PrintCompositor's legacy cell mode.</param>
    Task PrintAsync(IReadOnlyList<string> imagePaths, PrintTemplate template, PrintRenderContext? context = null, CancellationToken ct = default);
}
