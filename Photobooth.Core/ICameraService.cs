namespace Photobooth.Core;

/// <summary>
/// Abstracts the camera. The state machine only knows about this interface,
/// never about EDSDK or the mock directly. That's what lets us build and test
/// the entire app before Canon's Developer Program approval comes through --
/// swap MockCameraService for a real EdsdkCameraService later and nothing
/// else in the app has to change.
/// </summary>
public interface ICameraService
{
    /// <summary>Triggers a capture and returns the local file path of the saved image.</summary>
    Task<string> CaptureAsync(CancellationToken ct = default);
}
