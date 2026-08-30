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

    [Fact]
    public async Task UploadAsync_WhenFailNextUploadSet_ThrowsOnceThenResets()
    {
        var cloudUpload = new MockCloudUploadService { FailNextUpload = true };

        await Assert.ThrowsAsync<InvalidOperationException>(() => cloudUpload.UploadAsync("./captures/mock_0001.bmp"));

        Assert.False(cloudUpload.FailNextUpload);
        Uri url = await cloudUpload.UploadAsync("./captures/mock_0001.bmp");
        Assert.Contains("mock_0001.bmp", url.ToString());
    }
}

public class MockPhotoBrandingServiceTests
{
    [Fact]
    public async Task ApplyBrandingAsync_ReturnsANewPathAndLeavesOriginalUntouched()
    {
        var camera = new MockCameraService();
        string originalPath = await camera.CaptureAsync();
        var branding = new MockPhotoBrandingService();

        string brandedPath = await branding.ApplyBrandingAsync(originalPath);

        Assert.NotEqual(originalPath, brandedPath);
        Assert.Contains("_branded", brandedPath);
        Assert.True(File.Exists(brandedPath));
        Assert.True(File.Exists(originalPath));
    }
}

public class MockPhotoFilterServiceTests
{
    [Fact]
    public async Task ApplyGlamFilterAsync_ReturnsANewPathAndLeavesOriginalUntouched()
    {
        var camera = new MockCameraService();
        string originalPath = await camera.CaptureAsync();
        var filter = new MockPhotoFilterService();

        string filteredPath = await filter.ApplyGlamFilterAsync(originalPath);

        Assert.NotEqual(originalPath, filteredPath);
        Assert.Contains("_glam", filteredPath);
        Assert.True(File.Exists(filteredPath));
        Assert.True(File.Exists(originalPath));
    }
}

public class MockEmailDeliveryServiceTests
{
    [Fact]
    public async Task SendPhotoLinkAsync_RecordsTheEmailAndUrl()
    {
        var email = new MockEmailDeliveryService();
        var url = new Uri("https://res.cloudinary.com/example/photo.jpg");

        await email.SendPhotoLinkAsync("guest@example.com", url);

        var sent = Assert.Single(email.SentEmails);
        Assert.Equal("guest@example.com", sent.ToEmail);
        Assert.Equal(url, sent.PhotoUrl);
    }
}

public class MockFrameLibraryServiceTests
{
    [Fact]
    public async Task GetActiveFramesAsync_DefaultsToEmpty()
    {
        var library = new MockFrameLibraryService();

        var frames = await library.GetActiveFramesAsync();

        Assert.Empty(frames);
    }

    [Fact]
    public async Task GetActiveFramesAsync_ReturnsWhateverWasConfigured()
    {
        var library = new MockFrameLibraryService
        {
            Frames = new List<FrameOption> { new(1, "Gold Border", "./frames/gold.png") },
        };

        var frames = await library.GetActiveFramesAsync();

        var frame = Assert.Single(frames);
        Assert.Equal("Gold Border", frame.Name);
    }
}

public class MockFrameSelectionServiceTests
{
    [Fact]
    public async Task SelectFrameAsync_DefaultsToPickingTheFirstOption()
    {
        var selection = new MockFrameSelectionService();
        var options = new[] { new FrameOption(1, "Gold Border", "./frames/gold.png"), new FrameOption(2, "Confetti", "./frames/confetti.png") };

        FrameOption? chosen = await selection.SelectFrameAsync(options);

        Assert.Equal(1, chosen?.FrameId);
    }

    [Fact]
    public async Task SelectFrameAsync_WhenSkipNextSet_ReturnsNullOnceThenResets()
    {
        var selection = new MockFrameSelectionService { SkipNext = true };
        var options = new[] { new FrameOption(1, "Gold Border", "./frames/gold.png") };

        FrameOption? skipped = await selection.SelectFrameAsync(options);
        Assert.Null(skipped);
        Assert.False(selection.SkipNext);

        FrameOption? next = await selection.SelectFrameAsync(options);
        Assert.NotNull(next);
    }
}

public class MockFrameOverlayServiceTests
{
    [Fact]
    public async Task ApplyFrameAsync_ReturnsANewPathAndLeavesOriginalUntouched()
    {
        var camera = new MockCameraService();
        string originalPath = await camera.CaptureAsync();
        var overlay = new MockFrameOverlayService();

        string framedPath = await overlay.ApplyFrameAsync(originalPath, "./frames/gold.png");

        Assert.NotEqual(originalPath, framedPath);
        Assert.Contains("_framed", framedPath);
        Assert.True(File.Exists(framedPath));
        Assert.True(File.Exists(originalPath));
    }
}

public class UiFrameSelectionServiceTests
{
    [Fact]
    public async Task SelectFrameAsync_RaisesSelectionRequestedAndCompletesOnceSubmitSelectionIsCalled()
    {
        var selection = new UiFrameSelectionService();
        IReadOnlyList<FrameOption>? requestedOptions = null;
        selection.SelectionRequested += options => requestedOptions = options;
        var offered = new[] { new FrameOption(1, "Gold Border", "./frames/gold.png") };

        Task<FrameOption?> pending = selection.SelectFrameAsync(offered);

        // SelectFrameAsync shouldn't complete on its own -- it's waiting on a
        // guest tap, simulated here via SubmitSelection.
        Assert.False(pending.IsCompleted);
        Assert.Same(offered, requestedOptions);

        selection.SubmitSelection(offered[0]);

        FrameOption? result = await pending;
        Assert.Equal(offered[0], result);
    }

    [Fact]
    public async Task SelectFrameAsync_SubmitSelectionWithNull_MeansGuestSkippedTheFrame()
    {
        var selection = new UiFrameSelectionService();
        var offered = new[] { new FrameOption(1, "Gold Border", "./frames/gold.png") };

        Task<FrameOption?> pending = selection.SelectFrameAsync(offered);
        selection.SubmitSelection(null);

        Assert.Null(await pending);
    }
}

public class MockConsentServiceTests
{
    [Fact]
    public async Task CollectAsync_DefaultsToAcceptedWithEmailOptIn()
    {
        var consent = new MockConsentService();

        ConsentResult result = await consent.CollectAsync();

        Assert.True(result.DisclaimerAccepted);
        Assert.True(result.EmailOptIn);
        Assert.Equal("guest@example.com", result.Email);
    }

    [Fact]
    public async Task CollectAsync_WhenDeclineNextSet_ReportsDeclinedOnceThenResets()
    {
        var consent = new MockConsentService { DeclineNext = true };

        ConsentResult declined = await consent.CollectAsync();
        Assert.False(declined.DisclaimerAccepted);
        Assert.False(declined.EmailOptIn);
        Assert.Null(declined.Email);
        Assert.False(consent.DeclineNext);

        ConsentResult next = await consent.CollectAsync();
        Assert.True(next.DisclaimerAccepted);
    }

    [Fact]
    public async Task CollectAsync_WhenSimulateEmailOptInFalse_ReturnsNoEmail()
    {
        var consent = new MockConsentService { SimulateEmailOptIn = false };

        ConsentResult result = await consent.CollectAsync();

        Assert.True(result.DisclaimerAccepted);
        Assert.False(result.EmailOptIn);
        Assert.Null(result.Email);
    }
}
