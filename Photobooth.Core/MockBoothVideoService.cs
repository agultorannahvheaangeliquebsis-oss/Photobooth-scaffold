using System.Diagnostics;

namespace Photobooth.Core;

/// <summary>
/// Fake Video-mode capture for development and tests -- writes a small
/// placeholder file rather than actually driving a webcam/mic, same
/// reasoning MockVideoGuestbookService already established.
/// </summary>
public class MockBoothVideoService : IBoothVideoService
{
    private readonly string _instanceId = Guid.NewGuid().ToString("N")[..8];
    private int _recordingCount;
    private Stopwatch? _stopwatch;
    private string? _currentPath;

    /// <summary>When true, the next StartRecordingAsync call throws instead of succeeding. Resets itself after firing once.</summary>
    public bool FailNextStart { get; set; }

    public List<string> RecordedFiles { get; } = new();

    public async Task StartRecordingAsync(CancellationToken ct = default)
    {
        await Task.Delay(20, ct);

        if (FailNextStart)
        {
            FailNextStart = false;
            throw new InvalidOperationException("Mock booth video error: simulated start failure (e.g. webcam/mic busy).");
        }

        _recordingCount++;
        _currentPath = $"./videos/mock_{_recordingCount:D4}_{_instanceId}.mp4";
        Directory.CreateDirectory(Path.GetDirectoryName(_currentPath)!);
        File.WriteAllBytes(_currentPath, new byte[] { 0 });
        _stopwatch = Stopwatch.StartNew();
    }

    public async Task<BoothVideoRecording> StopRecordingAsync(CancellationToken ct = default)
    {
        await Task.Delay(20, ct);

        if (_stopwatch is null || _currentPath is null)
        {
            throw new InvalidOperationException("No booth video recording is currently in progress.");
        }

        _stopwatch.Stop();
        var recording = new BoothVideoRecording(_currentPath, _stopwatch.Elapsed);
        RecordedFiles.Add(_currentPath);
        _stopwatch = null;
        _currentPath = null;
        return recording;
    }
}
