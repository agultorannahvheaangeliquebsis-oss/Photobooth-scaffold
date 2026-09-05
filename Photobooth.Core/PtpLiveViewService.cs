namespace Photobooth.Core;

/// <summary>
/// Real live view implementation: a thin client for the LIVEVIEW /
/// LIVEVIEW_STOP commands exposed by Photobooth.CameraBridge.Host over the
/// same named pipe PtpCameraService uses. Frame bytes travel base64-encoded
/// on a single line -- simpler than mixing binary reads with the
/// StreamReader used for every other command, at the cost of ~33% overhead,
/// which is fine for a low-fps preview.
///
/// Shares <see cref="CameraBridgeClient"/>'s gate with capture and device
/// listing, so a poll that's still in flight when the countdown ends can no
/// longer make the guest's capture time out on the pipe -- see that class.
/// </summary>
public class PtpLiveViewService : ILiveViewService
{
    private readonly CameraBridgeClient _bridge;

    public PtpLiveViewService(TimeSpan? connectTimeout = null)
    {
        _bridge = new CameraBridgeClient(connectTimeout);
    }

    public async Task<byte[]?> GetFrameAsync(CancellationToken ct = default)
    {
        BridgeResponse response = await _bridge.SendAsync("LIVEVIEW", ct);
        // An empty payload is treated as "no frame right now", same as an ERR
        // or an unreachable bridge -- ILiveViewService's contract says a null
        // frame means "keep showing the last one", which is the right thing to
        // do for a bare "OK" too rather than handing the UI a zero-byte image.
        if (!response.IsOk || response.Payload.Length == 0)
        {
            return null;
        }

        return Convert.FromBase64String(response.Payload);
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        await _bridge.SendAsync("LIVEVIEW_STOP", ct);
    }
}
