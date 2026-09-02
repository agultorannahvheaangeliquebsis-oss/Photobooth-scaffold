using System.Diagnostics;
using System.Linq;
using Photobooth.Core;
using Photobooth.Data;
using Photobooth.UI.ViewModels;

namespace Photobooth.UI;

/// <summary>
/// Builds the real (non-mock) service graph a guest-facing window needs to
/// run an actual booth -- DB init, the camera bridge process, and every
/// <see cref="BoothServices"/> implementation. Extracted out of MainWindow's
/// constructor so KioskWindow's real-services path (and MainWindow, until
/// it's retired) can share one composition root instead of duplicating it.
/// Deliberately does not wire any events (StateChanged, SelectionRequested,
/// etc.) -- that stays the caller's job, same as KioskViewModel's own
/// constructor already keeps event wiring separate from object construction.
/// </summary>
public static class BoothCompositionRoot
{
    /// <summary>Thrown when <see cref="DatabaseInitializer.InitializeAsync"/>
    /// fails, so callers can show a DB-specific message distinct from a
    /// generic service-construction failure (e.g. a missing CLOUDINARY_URL).</summary>
    public sealed class DatabaseUnavailableException : Exception
    {
        public DatabaseUnavailableException(Exception inner)
            : base(inner.Message, inner)
        {
        }
    }

    /// <summary>Everything a real booth window needs. The four concrete
    /// <c>Ui*</c>/<c>Sql*</c> instances are surfaced alongside
    /// <see cref="Services"/> (which only holds them behind their
    /// interfaces) because callers need the concrete types to wire
    /// <c>SelectionRequested</c>/<c>FeedbackRequested</c>/etc. and to call
    /// their <c>Submit*</c> methods.</summary>
    public sealed record RealBooth(
        BoothServices Services,
        ILiveViewService LiveView,
        DatabaseInitializer.SeedIds SeedIds,
        Process? CameraBridgeProcess,
        UiFrameSelectionService FrameSelection,
        UiFeedbackService Feedback,
        UiGuestbookPromptService GuestbookPrompt,
        SqlSurveyService Survey,
        UiFilterSelectionService FilterSelection,
        UiTemplateSelectionService TemplateSelection);

    /// <summary>Blocking -- callers must invoke this off the UI thread's
    /// synchronous continuation via <c>Task.Run(() =&gt; Build())...GetAwaiter().GetResult()</c>
    /// style if called from a Dispatcher thread (same deadlock reasoning
    /// MainWindow's original constructor comment already gave: awaited
    /// continuations inside <see cref="DatabaseInitializer.InitializeAsync"/>
    /// try to resume on the calling thread, which is blocked waiting on the
    /// result). Throws <see cref="DatabaseUnavailableException"/> if DB
    /// init fails, or a plain <see cref="Exception"/> if a service
    /// constructor fails (e.g. missing CLOUDINARY_URL) -- callers own
    /// showing a message and exiting on either.</summary>
    /// <paramref name="locationId"/> picks which event/location this booth run
    /// is for -- see EventLauncherWindow, where an admin launches one of
    /// possibly several saved events. Null (the default) keeps the original
    /// "one booth machine has one Location" behavior: whichever Location
    /// DatabaseInitializer seeded/found first.
    public static RealBooth Build(int? locationId = null)
    {
        DatabaseInitializer.SeedIds seedIds;
        try
        {
            seedIds = Task.Run(() => DatabaseInitializer.InitializeAsync()).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            throw new DatabaseUnavailableException(ex);
        }

        var locationRepository = new LocationRepository();
        List<LocationRecord> locations = Task.Run(() => locationRepository.GetAllAsync()).GetAwaiter().GetResult();
        LocationRecord target = (locationId is int requestedId ? locations.FirstOrDefault(l => l.LocationId == requestedId) : null)
            ?? locations.First(l => l.LocationId == seedIds.LocationId);
        var printerRepository = new PrinterRepository();
        List<PrinterRecord> printers = Task.Run(() => printerRepository.GetByLocationAsync(target.LocationId)).GetAwaiter().GetResult();
        PrinterRecord targetPrinter = printers.First();
        seedIds = seedIds with
        {
            LocationId = target.LocationId,
            PrinterId = targetPrinter.PrinterId,
            LocationType = target.Type
        };

        // Real, not mocked -- a frame pick / star rating+comment / guestbook
        // ask-stop tap / survey answer is just button taps and text input,
        // no external hardware or gateway to integrate, unlike
        // Consent/Payment which stay mocked below.
        var frameSelection = new UiFrameSelectionService();
        var feedback = new UiFeedbackService();
        var guestbookPrompt = new UiGuestbookPromptService();
        var survey = new SqlSurveyService(seedIds.LocationId);
        var filterSelection = new UiFilterSelectionService();
        var templateSelection = new UiTemplateSelectionService();

        Process? cameraBridgeProcess = EnsureCameraBridgeRunning(target.Screen.EnableWebcams);

        var sessionRepository = new SqlSessionRepository(seedIds.LocationId, seedIds.PrinterId);
        // Shared by Settings below and the two real delivery services --
        // Email/Sms need it to read SharingSettings' SMTP/Twilio config
        // fresh on every send (see SmtpEmailDeliveryService/
        // TwilioSmsDeliveryService's own doc comments), not just once here.
        var settingsProvider = new SqlBoothSettingsProvider(seedIds.LocationId);
        var services = new BoothServices(
            Camera: new PtpCameraService(),
            Printer: new SpoolerPrinterService(),
            CloudUpload: new CloudinaryCloudUploadService(),
            Sessions: sessionRepository,
            Payment: new MockQrPaymentService(),
            UploadQueue: new FileSystemPendingUploadQueue(),
            Consent: new MockConsentService(),
            Email: new SmtpEmailDeliveryService(settingsProvider),
            Branding: new GdiPhotoBrandingService(),
            Filter: new GdiPhotoFilterService(),
            Settings: settingsProvider,
            FrameLibrary: new SqlFrameLibraryService(seedIds.LocationId),
            FrameSelection: frameSelection,
            FrameOverlay: new GdiFrameOverlayService(),
            Feedback: feedback,
            GuestbookPrompt: guestbookPrompt,
            VideoGuestbook: new FfmpegVideoGuestbookService(),
            GifComposer: new GdiGifComposerService(),
            BoothVideo: new FfmpegBoothVideoService(),
            AttendantCue: new SqlVirtualAttendantService(seedIds.LocationId),
            Survey: survey)
        {
            Sms = new TwilioSmsDeliveryService(settingsProvider),
            GreenScreen = new GdiGreenScreenService(),
            PostProcessing = new ProcessPostProcessingService(),
            FilterPreset = new GdiFilterPresetService(),
            FilterSelection = filterSelection,
            CustomFilterLibrary = new SqlCustomFilterLibraryService(seedIds.LocationId),
            CustomFilter = new GdiCubeLutFilterService(),
            TemplateLibrary = new SqlPrintTemplateLibraryService(seedIds.LocationId),
            TemplateSelection = templateSelection,
        };

        return new RealBooth(
            services,
            new PtpLiveViewService(),
            seedIds,
            cameraBridgeProcess,
            frameSelection,
            feedback,
            guestbookPrompt,
            survey,
            filterSelection,
            templateSelection);
    }

    /// <summary>Builds a real <see cref="KioskViewModel"/> for a real booth --
    /// the production counterpart to <see cref="KioskViewModel.CreateWithMockServices"/>.
    /// Passes every concrete <c>Ui*</c>/<c>Sql*</c> instance from <see cref="RealBooth"/>
    /// through to the ViewModel, since KioskWindow now has a screen for each of
    /// FramePicker/Guestbook/Feedback/Survey. Returns the <see cref="RealBooth"/>
    /// too, so the caller can register camera-bridge process cleanup.</summary>
    public static (KioskViewModel ViewModel, RealBooth Booth) BuildKioskViewModel(int? locationId = null)
    {
        RealBooth booth = Build(locationId);
        var viewModel = new KioskViewModel(
            booth.Services,
            booth.LiveView,
            booth.SeedIds.LocationType,
            booth.FrameSelection,
            booth.Feedback,
            booth.GuestbookPrompt,
            booth.Survey,
            booth.SeedIds.LocationId,
            booth.FilterSelection,
            booth.TemplateSelection);
        return (viewModel, booth);
    }

    /// <summary>Launches Photobooth.CameraBridge.Host (the out-of-process pipe
    /// server that drives the camera -- see PtpCameraService and the README's
    /// "Camera: Nikon D3500" section) if it isn't already listening, so an
    /// attendant/dev doesn't have to start it by hand before opening this app.
    /// The bridge auto-detects whatever camera the device actually has (a
    /// tethered D3500 if one's attached, otherwise a laptop webcam) -- set
    /// PHOTOBOOTH_REQUIRE_DSLR=1 on real booth hardware to disable the webcam
    /// fallback instead (see Program.cs in that project for why that matters).
    /// Returns the started process (null if already running, not found, or
    /// failed to start) -- the caller owns killing it on exit if non-null,
    /// since only the process that launched it should tear it down.</summary>
    /// <param name="enableWebcams">The launched event's own
    /// ScreenSettings.EnableWebcams (AdminWindow's Camera Settings section) --
    /// false has the same effect as PHOTOBOOTH_REQUIRE_DSLR, just per-event
    /// instead of machine-wide. Only takes effect while starting a fresh bridge
    /// process: if one is already running (e.g. a previous event launch in this
    /// same app session left it up), this setting change won't retroactively
    /// restart it with new arguments -- same "next fresh launch" caveat every
    /// other admin-editable setting in this codebase avoids by being read fresh
    /// per session, which isn't possible here since the bridge is an
    /// already-running external process, not something re-read per call.</param>
    private static Process? EnsureCameraBridgeRunning(bool enableWebcams)
    {
        if (PtpCameraService.IsBridgeHostRunning())
        {
            return null;
        }

        string? exePath = ResolveCameraBridgeHostPath();
        if (exePath is null)
        {
            // Not found -- PtpCameraService will surface a clear "is the bridge
            // process running?" error on first capture instead of failing here.
            Serilog.Log.Warning("Camera bridge host executable not found; capture will fail until PHOTOBOOTH_CAMERA_BRIDGE_EXE is set or it's placed at the expected build output path");
            return null;
        }

        var startInfo = new ProcessStartInfo(exePath)
        {
            WorkingDirectory = System.IO.Path.GetDirectoryName(exePath)!,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        bool requireDslr = !enableWebcams || Environment.GetEnvironmentVariable("PHOTOBOOTH_REQUIRE_DSLR") is "1" or "true";
        if (requireDslr)
        {
            startInfo.ArgumentList.Add("--require-dslr");
        }

        Process? process;
        try
        {
            process = Process.Start(startInfo);
        }
        catch (Exception ex)
        {
            // Swallow -- same reasoning as the exePath-not-found case above.
            Serilog.Log.Warning(ex, "Failed to start camera bridge host at {ExePath}", exePath);
            return null;
        }

        // The bridge doesn't start listening on its pipe until after it
        // finishes scanning for a camera (DSLR pass, then a webcam fallback
        // pass -- see Program.cs), which can take a few seconds. Without this
        // wait, the window shows and is tappable before that finishes, so an
        // attendant/guest who taps Start immediately hits "is the bridge
        // process running?" even though the bridge comes up moments later --
        // confirmed by reproducing that exact race against a real run. Block
        // here, before the window is shown, same reasoning as the
        // DatabaseInitializer wait above -- an extra few seconds once at
        // startup beats a confusing false error on the guest's first tap.
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        while (!PtpCameraService.IsBridgeHostRunning() && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(250);
        }

        return process;
    }

    /// <summary>Where this event's captured photos/GIFs/videos actually live
    /// on disk (see AdminWindow's Event folder/Export Event/Slideshow
    /// sections) -- the camera bridge host writes into a "captures" folder
    /// relative to *its own* working directory (see Program.cs's HandleCapture,
    /// `Directory.CreateDirectory("captures")`), which is a different process
    /// with a different working directory than Photobooth.UI's own (see
    /// EnsureCameraBridgeRunning's WorkingDirectory), so "AppContext.BaseDirectory
    /// of this process" would be the wrong folder for a real capture. Reuses
    /// the exact same resolution ResolveCameraBridgeHostPath already uses to
    /// find that process's own directory. Falls back to a "captures" folder
    /// relative to this process's own current directory when the bridge
    /// host's location can't be resolved -- the same relative convention
    /// MockCameraService itself uses when running in-process against mocks
    /// (no separate bridge process to have its own working directory at all).</summary>
    public static string ResolveCapturesDirectory()
    {
        string? bridgeExePath = ResolveCameraBridgeHostPath();
        string? bridgeDirectory = bridgeExePath is not null ? System.IO.Path.GetDirectoryName(bridgeExePath) : null;
        string baseDirectory = bridgeDirectory ?? Environment.CurrentDirectory;
        return System.IO.Path.GetFullPath(System.IO.Path.Combine(baseDirectory, "captures"));
    }

    private static string? ResolveCameraBridgeHostPath()
    {
        string? overridePath = Environment.GetEnvironmentVariable("PHOTOBOOTH_CAMERA_BRIDGE_EXE");
        if (overridePath is { Length: > 0 } && System.IO.File.Exists(overridePath))
        {
            return overridePath;
        }

        // Dev layout: walk up from this app's own build output to the solution
        // root, then into the bridge host project's build output for the same
        // configuration. Deployed installs should set
        // PHOTOBOOTH_CAMERA_BRIDGE_EXE instead of relying on this.
        var dir = new System.IO.DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
        while (dir is not null && !System.IO.File.Exists(System.IO.Path.Combine(dir.FullName, "Photobooth.sln")))
        {
            dir = dir.Parent;
        }
        if (dir is null)
        {
            return null;
        }

#if DEBUG
        const string configuration = "Debug";
#else
        const string configuration = "Release";
#endif
        var candidate = System.IO.Path.Combine(
            dir.FullName, "Photobooth.CameraBridge.Host", "bin", configuration, "net48", "Photobooth.CameraBridge.Host.exe");
        return System.IO.File.Exists(candidate) ? candidate : null;
    }
}
