namespace Photobooth.Core;

/// <summary>An admin-managed frame overlay a guest can pick during FramePicker. ImagePath
/// points to a transparent PNG composited over the photo -- see IFrameOverlayService.</summary>
public record FrameOption(int FrameId, string Name, string ImagePath);

/// <summary>
/// Abstracts reading the booth's currently-active frame overlays. Same
/// interface-plus-mock seam as IBoothSettingsProvider -- BoothStateMachine
/// reads this fresh at the start of every session (not cached), so a frame
/// an admin just added or retired takes effect for the very next guest.
/// </summary>
public interface IFrameLibraryService
{
    Task<IReadOnlyList<FrameOption>> GetActiveFramesAsync(CancellationToken ct = default);
}
