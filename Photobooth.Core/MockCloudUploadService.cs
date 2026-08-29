namespace Photobooth.Core;

/// <summary>
/// Fake upload for development before a Firebase project exists, and for
/// unit tests afterward. Returns a fabricated URL rather than actually
/// hosting the file anywhere -- the QR code it produces won't resolve to a
/// real photo, but it exercises the full upload -> URL -> QR pipeline.
/// </summary>
public class MockCloudUploadService : ICloudUploadService
{
    public async Task<Uri> UploadAsync(string localFilePath, CancellationToken ct = default)
    {
        // Real upload has network latency; simulate it so the UI's
        // "preparing your download link" window has something realistic to
        // sit in instead of the QR appearing suspiciously instantly.
        await Task.Delay(1500, ct);

        string fileName = Path.GetFileName(localFilePath);
        return new Uri($"https://storage.example.invalid/mock-booth/{fileName}");
    }
}
