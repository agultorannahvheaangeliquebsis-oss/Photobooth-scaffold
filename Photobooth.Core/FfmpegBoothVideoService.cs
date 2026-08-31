using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Versioning;

namespace Photobooth.Core;

/// <summary>
/// Real IBoothVideoService: drives ffmpeg as a child process to capture the
/// booth's webcam and microphone (DirectShow devices) to an mp4 file, same
/// mechanism and same PHOTOBOOTH_FFMPEG_PATH/PHOTOBOOTH_WEBCAM_DEVICE_NAME/
/// PHOTOBOOTH_MIC_DEVICE_NAME env vars as FfmpegVideoGuestbookService (it's
/// the same physical webcam/mic either way, so the device configuration is
/// shared rather than duplicated under a second env var name). Not yet
/// verified against real webcam/mic hardware -- same gap
/// FfmpegVideoGuestbookService already has.
/// </summary>
[SupportedOSPlatform("windows")]
public class FfmpegBoothVideoService : IBoothVideoService
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

    public FfmpegBoothVideoService()
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
            throw new InvalidOperationException("A booth video recording is already in progress.");
        }

        Directory.CreateDirectory("./videos");
        _currentPath = $"./videos/video_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}.mp4";

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

    public async Task<BoothVideoRecording> StopRecordingAsync(CancellationToken ct = default)
    {
        if (_process is null || _currentPath is null || _stopwatch is null)
        {
            throw new InvalidOperationException("No booth video recording is currently in progress.");
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
            // leave a recording process running past Video-mode capture.
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

        return new BoothVideoRecording(path, duration);
    }
}
