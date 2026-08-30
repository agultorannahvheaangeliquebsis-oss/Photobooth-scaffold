using System.IO.Pipes;
using System.Text;

namespace Photobooth.Core;

/// <summary>
/// Real camera implementation: a thin client for the named-pipe protocol
/// exposed by Photobooth.CameraBridge.Host (see that project, and README's
/// "Camera: Nikon D3500" section, for why the D3500 integration lives in a
/// separate net48/x86 process instead of here). Swaps in for
/// MockCameraService at the composition root once the bridge host is
/// running and a D3500 is attached.
/// </summary>
public class PtpCameraService : ICameraService
{
    public const string PipeName = "PhotoboothCameraBridge";
    private readonly TimeSpan _connectTimeout;

    public PtpCameraService(TimeSpan? connectTimeout = null)
    {
        _connectTimeout = connectTimeout ?? TimeSpan.FromSeconds(3);
    }

    /// <summary>Quick, synchronous check for whether Photobooth.CameraBridge.Host
    /// is already listening, so a caller can decide whether it still needs to be
    /// launched instead of connecting twice.</summary>
    public static bool IsBridgeHostRunning(int timeoutMs = 200)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut);
            pipe.Connect(timeoutMs);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    public async Task<string> CaptureAsync(CancellationToken ct = default)
    {
        using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);

        try
        {
            await pipe.ConnectAsync((int)_connectTimeout.TotalMilliseconds, ct);
        }
        catch (TimeoutException)
        {
            throw new InvalidOperationException(
                "Could not connect to Photobooth.CameraBridge.Host -- is the bridge process running?");
        }

        var writer = new StreamWriter(pipe, Encoding.ASCII) { AutoFlush = true };
        var reader = new StreamReader(pipe, Encoding.ASCII);

        await writer.WriteLineAsync("CAPTURE");
        string? response = await reader.ReadLineAsync(ct);

        if (response is null)
        {
            throw new InvalidOperationException("Bridge closed the pipe without responding to CAPTURE.");
        }

        if (response.StartsWith("OK ", StringComparison.Ordinal))
        {
            return response.Substring(3);
        }

        throw new InvalidOperationException($"Camera bridge reported an error: {response}");
    }
}
