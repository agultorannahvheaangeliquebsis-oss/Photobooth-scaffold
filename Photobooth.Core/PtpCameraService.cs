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

    /// <summary>How many times a bridge-reported (not connect-timeout) capture
    /// failure gets retried before giving up, and how long to wait between
    /// attempts. Exists for a specific transient case confirmed on the webcam
    /// fallback used when no D3500 is attached: that device has no real live
    /// view API, so the bridge's LIVEVIEW handler fakes one by running a full
    /// CapturePhoto() per poll (see CameraBridge.Host's HandleLiveViewFrame).
    /// If a GIF/Boomerang/Video capture lands immediately after one of those
    /// polls, the webcam wrapper can throw "Could not capture photo from
    /// webcam" from having no recovery gap between shots -- a brief wait and
    /// retry clears it. A real D3500 reports HaveLiveView=true and never hits
    /// this fallback path, so this also just adds tolerance for an ordinary
    /// transient hiccup there.</summary>
    private const int MaxCaptureAttempts = 3;
    private static readonly TimeSpan CaptureRetryDelay = TimeSpan.FromMilliseconds(400);

    public async Task<string> CaptureAsync(CancellationToken ct = default)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                return await CaptureOnceAsync(ct);
            }
            // Only a bridge-reported failure is retried -- a connect timeout
            // (BridgeUnreachableException) means the bridge process itself isn't
            // there to retry against (crashed, never started, camera cable
            // unplugged), so retrying it would just silently repeat the same
            // ~3s connect timeout up to MaxCaptureAttempts times (formerly up to
            // ~9-10s total) before finally telling the guest/attendant anything
            // is wrong. Failing on the first attempt instead surfaces that
            // "is the bridge process running?" error immediately.
            catch (BridgeReportedCaptureErrorException) when (attempt < MaxCaptureAttempts)
            {
                await Task.Delay(CaptureRetryDelay, ct);
            }
        }
    }

    private async Task<string> CaptureOnceAsync(CancellationToken ct)
    {
        using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);

        try
        {
            await pipe.ConnectAsync((int)_connectTimeout.TotalMilliseconds, ct);
        }
        catch (TimeoutException)
        {
            throw new BridgeUnreachableException(
                "Could not connect to Photobooth.CameraBridge.Host -- is the bridge process running?");
        }

        var writer = new StreamWriter(pipe, Encoding.ASCII) { AutoFlush = true };
        var reader = new StreamReader(pipe, Encoding.ASCII);

        await writer.WriteLineAsync("CAPTURE");
        string? response = await reader.ReadLineAsync(ct);

        if (response is null)
        {
            throw new BridgeReportedCaptureErrorException("Bridge closed the pipe without responding to CAPTURE.");
        }

        if (response.StartsWith("OK ", StringComparison.Ordinal))
        {
            return response.Substring(3);
        }

        throw new BridgeReportedCaptureErrorException($"Camera bridge reported an error: {response}");
    }

    /// <summary>The bridge process couldn't be reached at all (connect timeout) --
    /// distinct from <see cref="BridgeReportedCaptureErrorException"/> purely so
    /// CaptureAsync's retry loop can tell them apart; still an
    /// InvalidOperationException to any external catch.</summary>
    private sealed class BridgeUnreachableException(string message) : InvalidOperationException(message);

    /// <summary>The bridge process was reached but a specific capture attempt
    /// failed (an ERR response, or the pipe closing mid-response) -- the class
    /// this retry loop exists for (see MaxCaptureAttempts's doc comment). Still
    /// an InvalidOperationException to any external catch.</summary>
    private sealed class BridgeReportedCaptureErrorException(string message) : InvalidOperationException(message);
}
