using Photobooth.Core;

namespace Photobooth.Tests;

public class MockCameraServiceTests
{
    [Fact]
    public async Task CaptureAsync_ReturnsPathToARealBmpFile()
    {
        var camera = new MockCameraService();

        string path = await camera.CaptureAsync();

        Assert.True(File.Exists(path));
        byte[] header = new byte[2];
        using (var stream = File.OpenRead(path))
        {
            _ = stream.Read(header, 0, header.Length);
        }
        // BMP files start with the 'B' 'M' magic bytes.
        Assert.Equal((byte)'B', header[0]);
        Assert.Equal((byte)'M', header[1]);
    }

    [Fact]
    public async Task CaptureAsync_IncrementsFrameNumberAcrossCalls()
    {
        var camera = new MockCameraService();

        string first = await camera.CaptureAsync();
        string second = await camera.CaptureAsync();

        Assert.NotEqual(first, second);
        Assert.Contains("mock_0001", first);
        Assert.Contains("mock_0002", second);
    }

    [Fact]
    public async Task CaptureAsync_WhenFailNextCaptureSet_ThrowsOnceThenResets()
    {
        var camera = new MockCameraService { FailNextCapture = true };

        await Assert.ThrowsAsync<InvalidOperationException>(() => camera.CaptureAsync());

        Assert.False(camera.FailNextCapture);
        string path = await camera.CaptureAsync();
        Assert.True(File.Exists(path));
    }
}

public class MockPrinterServiceTests
{
    [Fact]
    public async Task PrintAsync_CompletesWithoutThrowing()
    {
        var printer = new MockPrinterService();

        await printer.PrintAsync("./captures/does-not-need-to-exist.bmp");
    }
}

public class MockCloudUploadServiceTests
{
    [Fact]
    public async Task UploadAsync_ReturnsUrlContainingTheFileName()
    {
        var cloudUpload = new MockCloudUploadService();

        Uri url = await cloudUpload.UploadAsync("./captures/mock_0001.bmp");

        Assert.Contains("mock_0001.bmp", url.ToString());
    }
}
