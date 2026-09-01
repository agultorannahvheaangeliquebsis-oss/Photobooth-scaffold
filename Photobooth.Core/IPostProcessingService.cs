namespace Photobooth.Core;

/// <summary>
/// Runs an admin-configured external application against each captured photo
/// right after capture -- see dslrBooth's Post-Processing setting under
/// Effects &amp; Stickers ("After photo capture, call this application to
/// perform post-processing on each photo"). Fire-and-forget by design: the
/// guest session doesn't wait on or depend on the application's own exit,
/// same "best effort, never blocks the guest" reasoning ICloudUploadService's
/// background upload already established, since an arbitrary external app's
/// runtime/exit behavior isn't something a live session can safely gate on.
/// </summary>
public interface IPostProcessingService
{
    /// <summary>Launches <paramref name="applicationPath"/> with
    /// <paramref name="photoPath"/> as its one argument. Never throws --
    /// a bad path or missing application is a configuration problem, not a
    /// reason to fail the guest's session over a side-channel hook.</summary>
    void Run(string applicationPath, string photoPath);
}
