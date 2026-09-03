namespace Photobooth.Core;

/// <summary>
/// Flips a captured photo horizontally so the saved file matches the mirrored
/// live preview the guest actually saw (see ScreenSettings.SaveMirroredPhotos
/// and BoothStateMachine's capture step). Same interface-plus-mock seam as
/// IPhotoFilterService, and for the same reason -- no hardware or network to
/// fake, just keeping System.Drawing.Common (Windows-only) out of
/// Photobooth.Tests/Photobooth.ConsoleDemo.
/// </summary>
public interface IPhotoMirrorService
{
    /// <summary>Flips the photo horizontally (left-right) and returns the path to the flipped file (the original is left untouched).</summary>
    Task<string> FlipHorizontallyAsync(string photoPath, CancellationToken ct = default);
}
