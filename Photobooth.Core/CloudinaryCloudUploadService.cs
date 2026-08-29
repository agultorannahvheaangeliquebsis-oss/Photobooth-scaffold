using CloudinaryDotNet;
using CloudinaryDotNet.Actions;

namespace Photobooth.Core;

/// <summary>
/// Real upload backend, swapped in for <see cref="MockCloudUploadService"/> once
/// a Cloudinary account exists. Chosen over Firebase Storage because Firebase
/// now requires the paid Blaze plan just to provision a Storage bucket, while
/// Cloudinary's free tier (25GB storage/bandwidth per month) needs no card.
/// </summary>
public class CloudinaryCloudUploadService : ICloudUploadService
{
    private const string EnvVarName = "CLOUDINARY_URL";

    private readonly Cloudinary _cloudinary;

    /// <summary>
    /// Reads credentials from the CLOUDINARY_URL environment variable
    /// (cloudinary://&lt;api_key&gt;:&lt;api_secret&gt;@&lt;cloud_name&gt;, copied
    /// straight from the Cloudinary dashboard) -- same pattern as
    /// PHOTOBOOTH_DB_CONNECTION in SqlConnectionFactory, so credentials never
    /// need to be hardcoded or committed.
    /// </summary>
    public CloudinaryCloudUploadService()
    {
        string? url = Environment.GetEnvironmentVariable(EnvVarName);
        if (string.IsNullOrWhiteSpace(url))
        {
            throw new InvalidOperationException(
                $"{EnvVarName} is not set. Copy the API environment variable from " +
                "the Cloudinary dashboard (Settings > API Keys) and set it before " +
                "starting the booth.");
        }

        _cloudinary = new Cloudinary(url);
    }

    public async Task<Uri> UploadAsync(string localFilePath, CancellationToken ct = default)
    {
        var uploadParams = new ImageUploadParams
        {
            File = new FileDescription(localFilePath),
            Folder = "photobooth",
            UseFilename = true,
            UniqueFilename = true,
            Overwrite = false,
        };

        ImageUploadResult result = await _cloudinary.UploadAsync(uploadParams, ct);
        if (result.Error != null)
        {
            throw new InvalidOperationException($"Cloudinary upload failed: {result.Error.Message}");
        }

        return result.SecureUrl;
    }
}
