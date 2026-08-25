namespace Photobooth.Core;

/// <summary>
/// Abstracts the printer. Real implementation sends to the Windows print
/// spooler once a DNP/Selphy/Epson driver is installed -- no vendor SDK
/// needed there, unlike the camera.
/// </summary>
public interface IPrinterService
{
    Task PrintAsync(string imagePath, CancellationToken ct = default);
}
