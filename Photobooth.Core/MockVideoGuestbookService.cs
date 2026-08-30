using System.Diagnostics;

namespace Photobooth.Core;

/// <summary>
/// Fake video guestbook capture for development and tests -- writes a small
/// placeholder file rather than actually driving a webcam/mic, so
/// Photobooth.Tests and Photobooth.ConsoleDemo don't need ffmpeg or real
/// hardware just to exercise this seam.
/// </summary>
public class MockVideoGuestbookService : IVideoGuestbookService
{
    private readonly string _instanceId = Guid.NewGuid().ToString("N")[..8];
    private int _recordingCount;
    private Stopwatch? _stopwatch;
    private string? _currentPath;

    /// <summary>When true, the next StartRecordingAsync call throws instead of succeeding. Resets itself after firing once.</summary>
    public bool FailNextStart { get; set; }

    /// <summary>When true, the next StopRecordingAsync call throws instead of succeeding. Resets itself after firing once.</summary>
    public bool FailNextStop { get; set; }

    public List<string> RecordedFiles { get; } = new();

    public async Task StartRecordingAsync(CancellationToken ct = default)
    {
        await Task.Delay(20, ct);

        if (FailNextStart)
        {
            FailNextStart = false;
            throw new InvalidOperationException("Mock video guestbook error: simulated start failure (e.g. webcam/mic busy).");
        }

        _recordingCount++;
        _currentPath = $"./guestbook/mock_{_recordingCount:D4}_{_instanceId}.mp4";
        Directory.CreateDirectory(Path.GetDirectoryName(_currentPath)!);
        File.WriteAllBytes(_currentPath, new byte[] { 0 });
        _stopwatch = Stopwatch.StartNew();
    }

    public async Task<GuestbookRecording> StopRecordingAsync(CancellationToken ct = default)
    {
        await Task.Delay(20, ct);

        if (FailNextStop)
        {
            FailNextStop = false;
            throw new InvalidOperationException("Mock video guestbook error: simulated stop failure.");
        }

        if (_stopwatch is null || _currentPath is null)
        {
            throw new InvalidOperationException("No guestbook recording is currently in progress.");
        }

        _stopwatch.Stop();
        var recording = new GuestbookRecording(_currentPath, _stopwatch.Elapsed);
        RecordedFiles.Add(_currentPath);
        _stopwatch = null;
        _currentPath = null;
        return recording;
    }
}
