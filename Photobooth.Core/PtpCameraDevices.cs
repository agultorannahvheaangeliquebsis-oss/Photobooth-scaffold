using System.IO.Pipes;
using System.Text;

namespace Photobooth.Core;

/// <summary>
/// Thin client for the camera bridge's LIST_CAMERAS / SELECT_CAMERA commands
/// (see Photobooth.CameraBridge.Host's Program.cs) -- lets AdminWindow's
/// Camera Settings screen show which cameras the bridge currently sees and
/// let an admin pick one, over the same named pipe PtpCameraService/
/// PtpLiveViewService already talk to. Kept separate from those two: this is
/// an admin-only device-listing/selection concern with no role in
/// BoothStateMachine's own capture/live-view path.
/// </summary>
public class PtpCameraDevices
{
    private const string PipeName = "PhotoboothCameraBridge";
    private readonly TimeSpan _connectTimeout;

    public PtpCameraDevices(TimeSpan? connectTimeout = null)
    {
        _connectTimeout = connectTimeout ?? TimeSpan.FromSeconds(3);
    }

    /// <summary>Every camera the bridge currently sees, by display name (see
    /// Program.cs's HandleListCameras for when it does/doesn't widen the scan
    /// to include webcams). Empty if the bridge isn't reachable or reports no
    /// cameras -- callers should treat both the same as "nothing to pick from
    /// yet" rather than an error.</summary>
    public async Task<List<string>> ListAsync(CancellationToken ct = default)
    {
        string? response = await SendCommandAsync("LIST_CAMERAS", ct);
        if (response is null || !response.StartsWith("OK", StringComparison.Ordinal))
        {
            return new List<string>();
        }

        string payload = response.Length > 3 ? response.Substring(3) : string.Empty;
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
        string? response = await SendCommandAsync($"SELECT_CAMERA {deviceName}", ct);
        return response is not null && response.StartsWith("OK", StringComparison.Ordinal);
    }

    private async Task<string?> SendCommandAsync(string command, CancellationToken ct)
    {
        using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);

        try
        {
            await pipe.ConnectAsync((int)_connectTimeout.TotalMilliseconds, ct);
        }
        catch (TimeoutException)
        {
            return null;
        }

        var writer = new StreamWriter(pipe, Encoding.ASCII) { AutoFlush = true };
        var reader = new StreamReader(pipe, Encoding.ASCII);

        await writer.WriteLineAsync(command);
        return await reader.ReadLineAsync(ct);
    }
}
