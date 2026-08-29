namespace Photobooth.Core;

/// <summary>
/// Abstracts a live camera preview feed, shown behind the Countdown screen so
/// the guest can see themselves before the shot fires. Separate from
/// ICameraService -- triggering a still capture and streaming a preview are
/// different operations against the bridge/hardware, and a caller may want
/// one without the other.
/// </summary>
public interface ILiveViewService
{
    /// <summary>Returns the latest preview frame's image bytes (any format a
    /// standard image decoder understands), or null if no frame is available
    /// right now (e.g. camera not connected, still warming up, live view not
    /// supported by this device). Callers should treat a null frame as "keep
    /// showing the last one" rather than an error.</summary>
    Task<byte[]?> GetFrameAsync(CancellationToken ct = default);

    /// <summary>Releases the camera's live view mode once the preview is no
    /// longer needed (e.g. countdown ended, about to trigger a still capture).</summary>
    Task StopAsync(CancellationToken ct = default);
}
