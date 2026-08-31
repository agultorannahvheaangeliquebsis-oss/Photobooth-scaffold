namespace Photobooth.Core;

/// <summary>The file a Video-mode capture was saved to, and how long it ran.</summary>
public record BoothVideoRecording(string FilePath, TimeSpan Duration);

/// <summary>
/// Abstracts recording a guest's Video-mode capture (see BUILD_PLAN.md's
/// "dslrBooth feature-parity plan", Phase 2). Deliberately independent of
/// ICameraService/the CameraBridge pipe protocol, same reasoning
/// IVideoGuestbookService already established: that protocol tethers the
/// Nikon D3500 (a photo/PTP-only device with no video/audio path), so this
/// drives an ordinary webcam+microphone instead. Distinct interface from
/// IVideoGuestbookService even though the shape is identical -- they're
/// different guest-facing moments (Video mode is the main capture the guest
/// gets printed/shared, the guestbook is an optional post-session message)
/// that happen to want the same Start/Stop capability.
/// </summary>
public interface IBoothVideoService
{
    /// <summary>Starts recording to a new local file and returns once capture has actually begun.</summary>
    Task StartRecordingAsync(CancellationToken ct = default);

    /// <summary>Stops the in-progress recording and returns the saved file's path and actual duration. Throws if nothing is currently recording.</summary>
    Task<BoothVideoRecording> StopRecordingAsync(CancellationToken ct = default);
}
