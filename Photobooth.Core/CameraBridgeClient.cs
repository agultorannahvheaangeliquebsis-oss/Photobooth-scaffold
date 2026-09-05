using System.IO.Pipes;
using System.Text;

namespace Photobooth.Core;

/// <summary>
/// One round trip to Photobooth.CameraBridge.Host's named pipe: whether the
/// connection was made at all, and (if so) the single response line the
/// bridge wrote back.
/// </summary>
/// <param name="Connected">False means the bridge couldn't be reached within
/// the connect timeout -- the process isn't running, crashed, or is wedged.
/// Distinct from a connected-but-failed command, which callers have to tell
/// apart: see PtpCameraService's retry loop, which retries the latter and
/// deliberately doesn't retry the former.</param>
/// <param name="Line">The response line, or null if the bridge accepted the
/// connection and then closed it without answering. Only meaningful when
/// <paramref name="Connected"/> is true.</param>
public readonly record struct BridgeResponse(bool Connected, string? Line)
{
    /// <summary>True for an "OK"/"OK &lt;payload&gt;" response -- the shape every
    /// command in the bridge protocol uses for success.</summary>
    public bool IsOk => Line is not null && Line.StartsWith("OK", StringComparison.Ordinal);

    /// <summary>Everything after "OK " for a successful response, or an empty
    /// string for a bare "OK" (LIVEVIEW_STOP/SELECT_CAMERA) or any failure.</summary>
    public string Payload => IsOk && Line!.Length > 3 ? Line.Substring(3) : string.Empty;
}

/// <summary>
/// The single place anything in this app talks to Photobooth.CameraBridge.Host.
/// Four callers used to open their own <see cref="NamedPipeClientStream"/> with
/// four copies of the same connect/write/read code: PtpCameraService (CAPTURE),
/// PtpLiveViewService (LIVEVIEW/LIVEVIEW_STOP), PtpCameraDevices
/// (LIST_CAMERAS/SELECT_CAMERA), and AdminWindow's camera preview through the
/// second of those.
///
/// The reason they now share one class is <see cref="Gate"/>, not the
/// deduplication. The bridge listens on a pipe created with
/// maxNumberOfServerInstances: 1 and serves it synchronously on its main
/// thread, so exactly one command can be in flight machine-wide. Before this,
/// leaving Countdown fired LIVEVIEW_STOP without awaiting it while a LIVEVIEW
/// poll could still be in flight, and the state machine immediately issued
/// CAPTURE -- three clients racing for one pipe instance. The loser hit the
/// 3-second connect timeout, which PtpCameraService deliberately does NOT
/// retry (it means "the bridge isn't there"), so the guest's session failed
/// with "is the bridge process running?" against a bridge that was running
/// perfectly well. Worst on the webcam fallback path, where the bridge answers
/// a single LIVEVIEW poll by running a whole CapturePhoto() cycle with its own
/// five-second internal wait.
///
/// Waiting on the gate is deliberately unbounded: queueing behind an in-flight
/// command is exactly the outcome we want, and every bridge-side handler is
/// already bounded by its own timeout (10s for CAPTURE, 5s for the live view
/// fallback), so the wait can't outlast those. A caller that shouldn't block
/// (the live view poller) already skips its own tick when the previous one
/// hasn't finished.
/// </summary>
public sealed class CameraBridgeClient
{
    public const string PipeName = "PhotoboothCameraBridge";

    /// <summary>Static, because the resource it guards is static: one machine
    /// has one bridge process listening on one single-instance pipe. Making
    /// this an instance field would mean each of the four callers -- which are
    /// constructed independently, in different places, some of them lazily
    /// (AdminWindow builds its own PtpLiveViewService when the camera preview
    /// opens) -- guarded a different lock and none of them guarded the pipe.</summary>
    private static readonly SemaphoreSlim Gate = new(1, 1);

    private readonly TimeSpan _connectTimeout;

    public CameraBridgeClient(TimeSpan? connectTimeout = null)
    {
        _connectTimeout = connectTimeout ?? TimeSpan.FromSeconds(3);
    }

    /// <summary>Quick, synchronous check for whether the bridge host is already
    /// listening, so a caller can decide whether it still needs to be launched
    /// instead of connecting twice. Deliberately outside <see cref="Gate"/>:
    /// it's called from startup paths (BoothCompositionRoot's launch-and-wait
    /// loop) before any command traffic exists, and blocking those behind an
    /// in-flight guest capture would be backwards.</summary>
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

    /// <summary>Sends one newline-terminated command and reads the single
    /// response line, holding <see cref="Gate"/> for the whole round trip so no
    /// other caller can be mid-command on the pipe at the same time.</summary>
    public async Task<BridgeResponse> SendAsync(string command, CancellationToken ct = default)
    {
        await Gate.WaitAsync(ct);
        try
        {
            using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);

            try
            {
                await pipe.ConnectAsync((int)_connectTimeout.TotalMilliseconds, ct);
            }
            catch (TimeoutException)
            {
                return new BridgeResponse(Connected: false, Line: null);
            }

            var writer = new StreamWriter(pipe, Encoding.ASCII) { AutoFlush = true };
            var reader = new StreamReader(pipe, Encoding.ASCII);

            await writer.WriteLineAsync(command);
            return new BridgeResponse(Connected: true, Line: await reader.ReadLineAsync(ct));
        }
        finally
        {
            Gate.Release();
        }
    }
}
