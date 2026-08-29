namespace Photobooth.Core;

/// <summary>
/// Abstracts the camera. The state machine only knows about this interface,
/// never about the Nikon D3500 or the mock directly. The D3500 is a
/// consumer body with no official Nikon SDK support, so the real
/// implementation will tether over PTP (e.g. via digiCamControl's
/// CameraControl library or gPhoto2) rather than a vendor SDK -- swap
/// MockCameraService for a real PtpCameraService later and nothing else
/// in the app has to change.
/// </summary>
public interface ICameraService
{
    /// <summary>Triggers a capture and returns the local file path of the saved image.</summary>
    Task<string> CaptureAsync(CancellationToken ct = default);
}
