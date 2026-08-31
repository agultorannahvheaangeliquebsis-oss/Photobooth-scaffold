namespace Photobooth.Core;

/// <summary>
/// Chroma-keys the green backdrop out of a captured photo and composites an
/// admin-configured replacement background in its place. Same
/// interface-plus-mock-plus-GDI+ seam as IPhotoFilterService/
/// IFrameOverlayService -- keeps BoothStateMachine, Photobooth.Tests, and
/// Photobooth.ConsoleDemo decoupled from System.Drawing.Common, which is
/// Windows-only.
/// </summary>
public interface IGreenScreenService
{
    /// <summary>Composites backgroundImagePath in place of photoPath's green backdrop and returns the path to the composited file (the original is left untouched).</summary>
    Task<string> ApplyGreenScreenAsync(string photoPath, string backgroundImagePath, CancellationToken ct = default);

    /// <summary>Same compositing as <see cref="ApplyGreenScreenAsync"/>, but for a
    /// single live-view frame already in memory (see ILiveViewService) rather than
    /// a file on disk -- used for the countdown screen's real-time preview, where
    /// a disk round trip per polled frame would fight the pipe fetch for the same
    /// budget. Returns the composited frame's own image bytes.</summary>
    Task<byte[]> ApplyToLiveFrameAsync(byte[] frameBytes, string backgroundImagePath, CancellationToken ct = default);
}
