namespace Photobooth.Core;

/// <summary>
/// Abstracts uploading a captured photo somewhere a guest's phone can reach
/// it. The state machine only knows about this interface, never about
/// Firebase Storage or the mock directly -- same seam as ICameraService and
/// IPrinterService, so the QR-download flow can be built and demoed before
/// a Firebase project exists.
/// </summary>
public interface ICloudUploadService
{
    /// <summary>Uploads the file at the given local path and returns a URL a guest's phone can download it from.</summary>
    Task<Uri> UploadAsync(string localFilePath, CancellationToken ct = default);
}
