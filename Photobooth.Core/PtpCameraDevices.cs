namespace Photobooth.Core;

/// <summary>
/// Thin client for the camera bridge's LIST_CAMERAS / SELECT_CAMERA commands
/// (see Photobooth.CameraBridge.Host's Program.cs) -- lets AdminWindow's
/// Camera Settings screen show which cameras the bridge currently sees and
/// let an admin pick one, over the same named pipe PtpCameraService/
/// PtpLiveViewService already talk to. Kept separate from those two: this is
/// an admin-only device-listing/selection concern with no role in
/// BoothStateMachine's own capture/live-view path.
///
/// Shares <see cref="CameraBridgeClient"/>'s gate with capture and live view,
/// so an admin opening the Camera Settings picker can no longer land a
/// LIST_CAMERAS on the pipe in the middle of a guest's capture -- see that
/// class.
/// </summary>
public class PtpCameraDevices
{
    private readonly CameraBridgeClient _bridge;

    public PtpCameraDevices(TimeSpan? connectTimeout = null)
    {
        _bridge = new CameraBridgeClient(connectTimeout);
    }

    /// <summary>Every camera the bridge currently sees, by display name (see
    /// Program.cs's HandleListCameras for when it does/doesn't widen the scan
    /// to include webcams). Empty if the bridge isn't reachable or reports no
    /// cameras -- callers should treat both the same as "nothing to pick from
    /// yet" rather than an error.</summary>
    public async Task<List<string>> ListAsync(CancellationToken ct = default)
    {
        BridgeResponse response = await _bridge.SendAsync("LIST_CAMERAS", ct);
        if (!response.IsOk)
        {
            return new List<string>();
        }

        string payload = response.Payload;
        return payload.Length == 0
            ? new List<string>()
            : payload.Split('|').ToList();
    }

    /// <summary>Asks the bridge to make <paramref name="deviceName"/> (an exact
    /// name from <see cref="ListAsync"/>) the active camera for capture/live
    /// view. Returns false if the bridge is unreachable or no longer sees a
    /// device by that name (e.g. it was unplugged since the last list).</summary>
    public async Task<bool> SelectAsync(string deviceName, CancellationToken ct = default)
    {
        BridgeResponse response = await _bridge.SendAsync($"SELECT_CAMERA {deviceName}", ct);
        return response.IsOk;
    }

}
