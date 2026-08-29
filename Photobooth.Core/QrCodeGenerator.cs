using QRCoder;

namespace Photobooth.Core;

/// <summary>
/// Turns the cloud download URL into a scannable QR code. Pure local
/// generation -- no network call, no cloud dependency -- so this half of
/// the delivery feature works regardless of which upload backend is wired
/// up behind ICloudUploadService.
/// </summary>
public static class QrCodeGenerator
{
    /// <summary>Renders a QR code encoding the given text as PNG bytes, ready to load into a WPF Image.</summary>
    public static byte[] GeneratePng(string content, int pixelsPerModule = 10)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(content, QRCodeGenerator.ECCLevel.Q);
        using var pngCode = new PngByteQRCode(data);
        return pngCode.GetGraphic(pixelsPerModule, drawQuietZones: true);
    }
}
