namespace Photobooth.Core;

/// <summary>
/// Fake live view for development before the camera bridge is wired up (or
/// when no camera is attached at all). Renders the same placeholder style as
/// MockCameraService, cycling backgrounds each call so the preview visibly
/// updates rather than looking frozen.
/// </summary>
public class MockLiveViewService : ILiveViewService
{
    private int _frameCount;

    public Task<byte[]?> GetFrameAsync(CancellationToken ct = default)
    {
        _frameCount++;
        return Task.FromResult<byte[]?>(PlaceholderImage.Render(_frameCount, DateTime.Now));
    }

    public Task StopAsync(CancellationToken ct = default) => Task.CompletedTask;
}
