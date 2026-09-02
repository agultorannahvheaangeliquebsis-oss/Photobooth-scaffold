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

        await printer.PrintAsync(new[] { "./captures/does-not-need-to-exist.bmp" }, PrintTemplate.Default);
    }

    [Fact]
    public async Task PrintAsync_RecordsEachTemplateItWasCalledWith()
    {
        var printer = new MockPrinterService();
        var stripTemplate = new PrintTemplate("Strip", WidthInches: 2, HeightInches: 6, StripCopies: 2);

        await printer.PrintAsync(new[] { "./captures/a.bmp" }, PrintTemplate.Default);
        await printer.PrintAsync(new[] { "./captures/b.bmp" }, stripTemplate);

        Assert.Equal(new[] { PrintTemplate.Default, stripTemplate }, printer.PrintedTemplates);
    }

    [Fact]
    public async Task PrintAsync_RecordsEveryImagePathItWasCalledWith()
    {
        var printer = new MockPrinterService();
        var poses = new[] { "./captures/a.bmp", "./captures/b.bmp", "./captures/c.bmp" };

        await printer.PrintAsync(poses, PrintTemplate.Default);

        Assert.Equal(poses, Assert.Single(printer.PrintedImagePaths));
    }
}

public class PrintTemplateTests
{
    [Theory]
    [InlineData("Single", 4, 6, 1, true)]
    [InlineData("Strip", 2, 6, 2, true)]
    [InlineData("Panorama", 4, 6, 1, false)] // unrecognized layout
    [InlineData("Single", 0, 6, 1, false)]   // width must be positive
    [InlineData("Single", 4, 0, 1, false)]   // height must be positive
    [InlineData("Strip", 2, 6, 0, false)]    // strip copies must be at least 1
    public void IsValid_ReflectsLayoutAndDimensionRules(string layout, double width, double height, int copies, bool expected)
    {
        var template = new PrintTemplate(layout, width, height, copies);

        Assert.Equal(expected, template.IsValid);
    }

    [Fact]
    public void ComputeCellBounds_SingleLayout_ReturnsOneRectangleMatchingPageBounds()
    {
        var template = PrintTemplate.Default;
        var pageBounds = new System.Drawing.Rectangle(10, 20, 400, 600);

        var cells = template.ComputeCellBounds(pageBounds);

        Assert.Equal(new[] { pageBounds }, cells);
    }

    [Fact]
    public void ComputeCellBounds_StripLayout_ReturnsEqualHeightCellsStackedTopToBottom()
    {
        var template = new PrintTemplate("Strip", WidthInches: 2, HeightInches: 6, StripCopies: 3);
        var pageBounds = new System.Drawing.Rectangle(0, 0, 200, 600);

        var cells = template.ComputeCellBounds(pageBounds);

        Assert.Equal(3, cells.Count);
        Assert.All(cells, cell => Assert.Equal(200, cell.Width));
        Assert.All(cells, cell => Assert.Equal(200, cell.Height));
        // Stacked top to bottom, covering the full page height with no gaps.
        Assert.Equal(0, cells[0].Top);
        Assert.Equal(200, cells[1].Top);
        Assert.Equal(400, cells[2].Top);
    }

    [Fact]
    public void Elements_DefaultsToEmpty()
    {
        Assert.Empty(PrintTemplate.Default.Elements);
    }

    [Fact]
    public void ComputeElementBounds_TranslatesPercentagesToPixelsWithinTheCell()
    {
        var template = PrintTemplate.Default;
        var cell = new System.Drawing.Rectangle(0, 0, 100, 200);
        var element = new PrintTemplateElement(PrintTemplateElementKind.Text, 0.1, 0.1, 0.5, 0.2, Text: "Hello");

        System.Drawing.Rectangle bounds = template.ComputeElementBounds(cell, element);

        Assert.Equal(new System.Drawing.Rectangle(10, 20, 50, 40), bounds);
    }

    [Fact]
    public void ComputeElementBounds_OffsetCell_AddsTheCellsOwnOffset()
    {
        var template = PrintTemplate.Default;
        var cell = new System.Drawing.Rectangle(50, 100, 200, 400);
        var element = new PrintTemplateElement(PrintTemplateElementKind.Logo, 0.25, 0.5, 0.5, 0.25, ImagePath: "./logo.png");

        System.Drawing.Rectangle bounds = template.ComputeElementBounds(cell, element);

        Assert.Equal(new System.Drawing.Rectangle(50 + 50, 100 + 200, 100, 100), bounds);
    }

    [Fact]
    public void RequiredPhotoCount_NoPhotoSlotElements_ReturnsOne()
    {
        var template = PrintTemplate.Default with
        {
            Elements = new[] { new PrintTemplateElement(PrintTemplateElementKind.Text, 0, 0, 0.5, 0.1, Text: "Hi") },
        };

        Assert.Equal(1, template.RequiredPhotoCount);
    }

    [Fact]
    public void RequiredPhotoCount_PhotoSlotElements_ReturnsHighestIndexPlusOne()
    {
        var template = PrintTemplate.Default with
        {
            Elements = new[]
            {
                new PrintTemplateElement(PrintTemplateElementKind.PhotoSlot, 0, 0, 0.5, 0.5, PhotoIndex: 0),
                new PrintTemplateElement(PrintTemplateElementKind.PhotoSlot, 0.5, 0, 0.5, 0.5, PhotoIndex: 1),
                new PrintTemplateElement(PrintTemplateElementKind.PhotoSlot, 0, 0.5, 0.5, 0.5, PhotoIndex: 2),
                new PrintTemplateElement(PrintTemplateElementKind.PhotoSlot, 0.5, 0.5, 0.5, 0.5, PhotoIndex: 3),
            },
        };

        Assert.Equal(4, template.RequiredPhotoCount);
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

        string brandedPath = await branding.ApplyBrandingAsync(originalPath, "Focus & Snap");

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

public class MockSmsDeliveryServiceTests
{
    [Fact]
    public async Task SendPhotoLinkAsync_RecordsThePhoneAndUrl()
    {
        var sms = new MockSmsDeliveryService();
        var url = new Uri("https://res.cloudinary.com/example/photo.jpg");

        await sms.SendPhotoLinkAsync("+15551234567", url);

        var sent = Assert.Single(sms.SentMessages);
        Assert.Equal("+15551234567", sent.ToPhone);
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

public class MockGreenScreenServiceTests
{
    [Fact]
    public async Task ApplyGreenScreenAsync_ReturnsANewPathAndLeavesOriginalUntouched()
    {
        var camera = new MockCameraService();
        string originalPath = await camera.CaptureAsync();
        var greenScreen = new MockGreenScreenService();

        string compositedPath = await greenScreen.ApplyGreenScreenAsync(originalPath, "./backgrounds/beach.jpg");

        Assert.NotEqual(originalPath, compositedPath);
        Assert.Contains("_greenscreen", compositedPath);
        Assert.True(File.Exists(compositedPath));
        Assert.True(File.Exists(originalPath));
    }

    [Fact]
    public async Task ApplyToLiveFrameAsync_ReturnsTheFrameBytesUnchanged()
    {
        var greenScreen = new MockGreenScreenService();
        byte[] frameBytes = { 1, 2, 3, 4 };

        byte[] result = await greenScreen.ApplyToLiveFrameAsync(frameBytes, "./backgrounds/beach.jpg");

        Assert.Same(frameBytes, result);
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

    [Fact]
    public async Task SelectFrameAsync_RejectsStaleSubmissionAfterReplacement()
    {
        var selection = new UiFrameSelectionService();
        var offered = new[] { new FrameOption(1, "Gold Border", "./frames/gold.png") };

        Task<FrameOption?> first = selection.SelectFrameAsync(offered);
        Guid firstToken = selection.CurrentRequestToken!.Value;
        Task<FrameOption?> second = selection.SelectFrameAsync(offered);
        Guid secondToken = selection.CurrentRequestToken!.Value;

        selection.SubmitSelection(offered[0], firstToken);
        Assert.False(second.IsCompleted);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);

        selection.SubmitSelection(offered[0], secondToken);
        Assert.Same(offered[0], await second);
    }

    [Fact]
    public async Task SelectFrameAsync_CancelPendingCancelsOutstandingRequest()
    {
        var selection = new UiFrameSelectionService();
        Task<FrameOption?> pending = selection.SelectFrameAsync(
            new[] { new FrameOption(1, "Gold Border", "./frames/gold.png") });

        selection.CancelPending();

        Assert.Null(selection.CurrentRequestToken);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
    }
}

public class MockFeedbackServiceTests
{
    [Fact]
    public async Task CollectAsync_DefaultsToAFiveStarRatingWithNoComment()
    {
        var feedback = new MockFeedbackService();

        FeedbackResult result = await feedback.CollectAsync();

        Assert.Equal(5, result.Rating);
        Assert.Null(result.Comment);
        Assert.False(result.IsEmpty);
    }

    [Fact]
    public async Task CollectAsync_WhenSkipNextSet_ReturnsEmptyResultOnceThenResets()
    {
        var feedback = new MockFeedbackService { SkipNext = true };

        FeedbackResult skipped = await feedback.CollectAsync();
        Assert.True(skipped.IsEmpty);
        Assert.False(feedback.SkipNext);

        FeedbackResult next = await feedback.CollectAsync();
        Assert.False(next.IsEmpty);
    }
}

public class UiFeedbackServiceTests
{
    [Fact]
    public async Task CollectAsync_RaisesFeedbackRequestedAndCompletesOnceSubmitFeedbackIsCalled()
    {
        var feedback = new UiFeedbackService();
        bool requested = false;
        feedback.FeedbackRequested += () => requested = true;

        Task<FeedbackResult> pending = feedback.CollectAsync();

        // CollectAsync shouldn't complete on its own -- it's waiting on a
        // guest tap, simulated here via SubmitFeedback.
        Assert.False(pending.IsCompleted);
        Assert.True(requested);

        feedback.SubmitFeedback(new FeedbackResult(4, "Great booth!"));

        FeedbackResult result = await pending;
        Assert.Equal(4, result.Rating);
        Assert.Equal("Great booth!", result.Comment);
    }

    [Fact]
    public async Task CollectAsync_SubmitFeedbackWithEmptyResult_MeansGuestSkipped()
    {
        var feedback = new UiFeedbackService();

        Task<FeedbackResult> pending = feedback.CollectAsync();
        feedback.SubmitFeedback(new FeedbackResult(null, null));

        FeedbackResult result = await pending;
        Assert.True(result.IsEmpty);
    }

    [Fact]
    public async Task CollectAsync_RejectsStaleSubmissionAfterReplacement()
    {
        var feedback = new UiFeedbackService();
        Task<FeedbackResult> first = feedback.CollectAsync();
        Guid firstToken = feedback.CurrentRequestToken!.Value;
        Task<FeedbackResult> second = feedback.CollectAsync();
        Guid secondToken = feedback.CurrentRequestToken!.Value;

        feedback.SubmitFeedback(new FeedbackResult(1, null), firstToken);
        Assert.False(second.IsCompleted);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);

        feedback.SubmitFeedback(new FeedbackResult(5, null), secondToken);
        Assert.Equal(5, (await second).Rating);
    }

    [Fact]
    public async Task CollectAsync_CancelPendingCancelsOutstandingRequest()
    {
        var feedback = new UiFeedbackService();
        Task<FeedbackResult> pending = feedback.CollectAsync();

        feedback.CancelPending();

        Assert.Null(feedback.CurrentRequestToken);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
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
