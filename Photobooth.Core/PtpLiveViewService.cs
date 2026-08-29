using System.IO.Pipes;
using System.Text;

namespace Photobooth.Core;

/// <summary>
/// Real live view implementation: a thin client for the LIVEVIEW /
/// LIVEVIEW_STOP commands exposed by Photobooth.CameraBridge.Host over the
/// same named pipe PtpCameraService uses. Frame bytes travel base64-encoded
/// on a single line -- simpler than mixing binary reads with the
/// StreamReader used for every other command, at the cost of ~33% overhead,
/// which is fine for a low-fps preview.
/// </summary>
public class PtpLiveViewService : ILiveViewService
{
    private const string PipeName = "PhotoboothCameraBridge";
    private readonly TimeSpan _connectTimeout;

    public PtpLiveViewService(TimeSpan? connectTimeout = null)
    {
        _connectTimeout = connectTimeout ?? TimeSpan.FromSeconds(3);
    }

    public async Task<byte[]?> GetFrameAsync(CancellationToken ct = default)
    {
        string? response = await SendCommandAsync("LIVEVIEW", ct);
        if (response is null || !response.StartsWith("OK ", StringComparison.Ordinal))
        {
            return null;
        }

        return Convert.FromBase64String(response.Substring(3));
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        await SendCommandAsync("LIVEVIEW_STOP", ct);
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
