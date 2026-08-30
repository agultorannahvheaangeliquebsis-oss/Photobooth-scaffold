using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Versioning;

namespace Photobooth.Core;

/// <summary>
/// Real IVideoGuestbookService: drives ffmpeg as a child process to capture
/// the booth's webcam and microphone (DirectShow devices) to an mp4 file.
/// This is the project's first external-process dependency (not just
/// unplugged hardware behind an already-installed driver, like the D3500 or
/// the printer) -- ffmpeg itself has to be installed and reachable, either
/// on PATH or via PHOTOBOOTH_FFMPEG_PATH (same env-var-driven config
/// pattern as PHOTOBOOTH_PRINTER_NAME/CLOUDINARY_URL). Device names are
/// machine-specific (`ffmpeg -list_devices true -f dshow -i dummy` is how
/// an installer discovers them), so they're configured via
/// PHOTOBOOTH_WEBCAM_DEVICE_NAME/PHOTOBOOTH_MIC_DEVICE_NAME rather than
/// hardcoded. Not yet verified against real webcam/mic hardware -- no
/// interactive desktop or hardware available in this dev environment, same
/// category of gap the D3500 capture path started with.
/// </summary>
[SupportedOSPlatform("windows")]
public class FfmpegVideoGuestbookService : IVideoGuestbookService
{
    private const string FfmpegPathEnvVar = "PHOTOBOOTH_FFMPEG_PATH";
    private const string WebcamDeviceEnvVar = "PHOTOBOOTH_WEBCAM_DEVICE_NAME";
    private const string MicDeviceEnvVar = "PHOTOBOOTH_MIC_DEVICE_NAME";

    private readonly string _ffmpegPath;
    private readonly string? _webcamDeviceName;
    private readonly string? _micDeviceName;

    private Process? _process;
    private string? _currentPath;
    private Stopwatch? _stopwatch;

    public FfmpegVideoGuestbookService()
    {
        _ffmpegPath = Environment.GetEnvironmentVariable(FfmpegPathEnvVar) is { Length: > 0 } configuredPath
            ? configuredPath
            : "ffmpeg";
        _webcamDeviceName = Environment.GetEnvironmentVariable(WebcamDeviceEnvVar);
        _micDeviceName = Environment.GetEnvironmentVariable(MicDeviceEnvVar);
    }

    public Task StartRecordingAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (_process is not null)
        {
            throw new InvalidOperationException("A guestbook recording is already in progress.");
        }

        Directory.CreateDirectory("./guestbook");
        _currentPath = $"./guestbook/guestbook_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}.mp4";

        string videoInput = _webcamDeviceName ?? "default";
        string audioInput = _micDeviceName ?? "default";

        var startInfo = new ProcessStartInfo
        {
            FileName = _ffmpegPath,
            ArgumentList =
            {
                "-f", "dshow",
                "-i", $"video={videoInput}:audio={audioInput}",
                "-y", _currentPath,
            },
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        try
        {
            _process = Process.Start(startInfo);
        }
        catch (Win32Exception ex)
        {
            _process = null;
            _currentPath = null;
            throw new InvalidOperationException(
                $"Couldn't start ffmpeg -- is it installed and on PATH, or is {FfmpegPathEnvVar} set correctly? " +
                "See the README's guestbook setup section.", ex);
        }

        _stopwatch = Stopwatch.StartNew();
        return Task.CompletedTask;
    }

    public async Task<GuestbookRecording> StopRecordingAsync(CancellationToken ct = default)
    {
        if (_process is null || _currentPath is null || _stopwatch is null)
        {
            throw new InvalidOperationException("No guestbook recording is currently in progress.");
        }

        Process process = _process;
        string path = _currentPath;

        // ffmpeg's documented graceful-stop signal: writing "q" to stdin lets
        // it finalize the mp4's moov atom before exiting. Killing the
        // process outright risks a corrupt/unplayable file.
        try
        {
            await process.StandardInput.WriteAsync("q");
            await process.StandardInput.FlushAsync();
        }
        catch (Exception)
        {
            // Best-effort -- if stdin is already closed/broken, fall through
            // to the bounded wait below and let the timeout's Kill() clean up.
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // ffmpeg didn't exit gracefully in time -- force it rather than
            // leave a recording process running past the guestbook screen.
            if (!process.HasExited)
            {
                process.Kill();
            }
        }

        _stopwatch.Stop();
        TimeSpan duration = _stopwatch.Elapsed;

        process.Dispose();
        _process = null;
        _currentPath = null;
        _stopwatch = null;

        return new GuestbookRecording(path, duration);
    }
}
