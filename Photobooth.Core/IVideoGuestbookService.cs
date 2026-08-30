namespace Photobooth.Core;

/// <summary>The file a guestbook recording was saved to, and how long it ran.</summary>
public record GuestbookRecording(string FilePath, TimeSpan Duration);

/// <summary>
/// Abstracts recording a guest's spoken video message. Deliberately
/// independent of ICameraService/the CameraBridge pipe protocol: that
/// protocol tethers the Nikon D3500 (a photo/PTP-only device with no audio
/// path), and a guestbook message needs the guest's actual voice, so this
/// drives an ordinary webcam+microphone through a separate capture pipeline
/// instead.
/// </summary>
public interface IVideoGuestbookService
{
    /// <summary>Starts recording to a new local file and returns once capture has actually begun.</summary>
    Task StartRecordingAsync(CancellationToken ct = default);

    /// <summary>Stops the in-progress recording and returns the saved file's path and actual duration. Throws if nothing is currently recording.</summary>
    Task<GuestbookRecording> StopRecordingAsync(CancellationToken ct = default);
}
