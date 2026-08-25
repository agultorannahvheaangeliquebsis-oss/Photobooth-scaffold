namespace Photobooth.Core;

/// <summary>
/// Fake camera for development before EDSDK access is approved, and for unit
/// tests afterward (you don't want real hardware in a CI pipeline). Set
/// FailNextCapture to true to exercise the state machine's error path.
/// </summary>
public class MockCameraService : ICameraService
{
    private int _captureCount = 0;

    /// <summary>When true, the next CaptureAsync call throws instead of succeeding.
    /// Resets itself after firing once, so tests don't need manual cleanup.</summary>
    public bool FailNextCapture { get; set; } = false;

    public async Task<string> CaptureAsync(CancellationToken ct = default)
    {
        // Real cameras take a moment to focus and fire; simulate that so the
        // UI's Capturing state has something realistic to sit in.
        await Task.Delay(800, ct);

        if (FailNextCapture)
        {
            FailNextCapture = false;
            throw new InvalidOperationException("Mock camera error: simulated capture failure (e.g. camera busy).");
        }

        _captureCount++;
        string path = $"./captures/mock_{_captureCount:D4}.jpg";
        return path;
    }
}
