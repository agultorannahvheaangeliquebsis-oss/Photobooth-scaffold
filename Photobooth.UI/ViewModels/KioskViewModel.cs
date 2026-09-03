using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Photobooth.Core;
using Photobooth.Data;
using Photobooth.UI.Services;
using Serilog;

namespace Photobooth.UI.ViewModels;

public sealed record FrameSelectionItem(FrameOption Option, Guid RequestToken);
public sealed record FilterSelectionItem(FilterOption Option, Guid RequestToken);
public sealed record TemplateSelectionItem(PrintTemplate Option, Guid RequestToken);

/// <summary>
/// Drives KioskWindow. Owns a <see cref="BoothStateMachine"/> and projects it
/// onto the five guest-facing screens (see <see cref="KioskScreen"/>), plus the
/// live view feed, the QR code, and the share/print actions.
///
/// Services arrive through the constructor -- nothing is constructed in here --
/// so the same ViewModel runs against real hardware at an event and against
/// Photobooth.Core's Mock* implementations on a dev machine with no camera,
/// no printer, no LocalDB and no Cloudinary key (see
/// <see cref="CreateWithMockServices"/>).
///
/// Threading: BoothStateMachine raises its events on whatever thread is running
/// the session, so every handler here marshals onto the dispatcher captured at
/// construction before touching a bindable property. Nothing else in this class
/// may be called off the UI thread.
/// </summary>
public class KioskViewModel : ObservableObject, IDisposable
{
    /// <summary>How long the white flash wash stays up after the shutter. Long
    /// enough to read as a flash, short enough that it never covers the next
    /// screen -- the capture itself takes far longer than this.</summary>
    private static readonly TimeSpan FlashDuration = TimeSpan.FromMilliseconds(420);

    /// <summary>Live view poll interval. ~7fps, matching MainWindow's existing
    /// timer: fast enough to feel live, slow enough that a pipe round trip per
    /// frame doesn't pile up (see <see cref="_liveViewFetchInProgress"/>).</summary>
    private static readonly TimeSpan LiveViewInterval = TimeSpan.FromMilliseconds(150);

    /// <summary>Must match BoothStateMachine's own TargetLoopDurationMs -- the review-screen
    /// GIF/Boomerang preview timer derives its interval from this and the decoded frame count,
    /// same target total playback length the composed file was actually encoded with.</summary>
    private const int TargetLoopDurationMs = 3000;

    private readonly BoothServices _services;
    private readonly BoothStateMachine _stateMachine;
    private readonly CaptureModeOverrideSettingsProvider _captureModeOverride;
    private readonly ILiveViewService _liveView;
    private readonly Dispatcher _dispatcher;
    private readonly SemaphoreSlim _settingsReloadGate = new(1, 1);

    // Concrete Ui*/Sql* instances for the four screens whose guest interaction
    // BoothServices' interfaces can't expose (SelectionRequested/SubmitSelection
    // and friends aren't part of IFrameSelectionService etc. -- see each
    // interface). Null in mock mode (CreateWithMockServices) and for whichever
    // of these screens hasn't been ported to real UI yet -- see
    // BoothCompositionRoot.BuildKioskViewModel for how the real composition
    // root passes these in one at a time as each screen lands. When null, the
    // corresponding BoothState still runs (against its Mock* service, which
    // resolves on its own without needing a tap -- see e.g.
    // MockFeedbackService), it just has no guest-facing controls to submit
    // through, same as before that screen existed here.
    private readonly UiFilterSelectionService? _filterSelection;
    private readonly UiFrameSelectionService? _frameSelection;
    private readonly UiTemplateSelectionService? _templateSelection;
    private readonly UiFeedbackService? _feedback;
    private readonly UiGuestbookPromptService? _guestbookPrompt;
    private readonly SqlSurveyService? _survey;

    // Phase-6 screen-template overlays: null _locationId (mock/designer mode)
    // skips the SQL read entirely, since ScreenTemplateElementRepository has
    // no Mock* counterpart and CreateWithMockServices promises no LocalDB
    // dependency -- see ReloadSettingsAsync.
    private readonly int? _locationId;
    private readonly ScreenTemplateElementRepository _screenElements = new();
    private readonly SharingLogRepository _sharingLog = new();
    private ILookup<ScreenTemplateScreen, ScreenTemplateElement> _screenElementsByScreen =
        Array.Empty<ScreenTemplateElement>().ToLookup(e => e.Screen);

    private RemoteControlServer? _remoteControl;

    private readonly DispatcherTimer _liveViewTimer;
    private readonly DispatcherTimer _flashTimer;
    private readonly DispatcherTimer _shareTimer;
    private readonly DispatcherTimer _gifPreviewTimer;
    private List<ImageSource>? _gifPreviewFrames;
    private int _gifPreviewFrameIndex;

    private BoothSettings _settings = new(CountdownSeconds: 3, GlamFilterEnabled: false, PrintTemplate: PrintTemplate.Default);
    private bool _liveViewFetchInProgress;
    private bool _sessionRunning;
    private bool _disposed;

    public KioskViewModel(
        BoothServices services,
        ILiveViewService liveView,
        string mode = "event",
        UiFrameSelectionService? frameSelection = null,
        UiFeedbackService? feedback = null,
        UiGuestbookPromptService? guestbookPrompt = null,
        SqlSurveyService? survey = null,
        int? locationId = null,
        UiFilterSelectionService? filterSelection = null,
        UiTemplateSelectionService? templateSelection = null)
    {
        // Application.Current.Dispatcher, not Dispatcher.CurrentDispatcher: this
        // constructor runs inside BoothCompositionRoot.BuildKioskViewModel, which
        // EventLauncherWindow.LaunchSelectedAsync deliberately calls via Task.Run
        // to keep DB init/camera-bridge startup off the UI thread -- so "current"
        // here is a threadpool thread, not the real UI thread.
        // Dispatcher.CurrentDispatcher would have silently created a brand-new
        // Dispatcher bound to that threadpool thread (nobody ever calls Run() on
        // it, since it just returns to the pool), and every OnUi() call from the
        // real UI thread afterward would then block forever in Dispatcher.Invoke
        // waiting on a message loop that never pumps -- confirmed via live repro
        // (traced exactly to this line: execution stops dead between
        // "BuildKioskViewModel returned" and "LaunchEventCommand executed", 0% CPU,
        // no exception, no timeout). Application.Current.Dispatcher is fixed to the
        // one real UI thread for the life of the app, regardless of which thread
        // constructs this ViewModel. Falls back to CurrentDispatcher only for a
        // bare unit-test context with no running Application at all.
        _dispatcher = System.Windows.Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        _liveView = liveView;
        _locationId = locationId;

        // Every settings read the state machine performs goes through the
        // override first, so a guest's mode tile reaches the session without a
        // settings write -- see CaptureModeOverrideSettingsProvider.
        _captureModeOverride = new CaptureModeOverrideSettingsProvider(services.Settings);
        _services = services with { Settings = _captureModeOverride };
        _stateMachine = new BoothStateMachine(_services, mode);
        BoothSettingsChanged.Changed += OnSettingsChanged;

        Admin = new KioskAdminViewModel(_services.Settings);

        StartSessionCommand = new RelayCommand(StartSession, () => CanStartSession);
        SelectModeCommand = new RelayCommand(SelectMode, _ => CurrentScreenState == KioskScreen.Idle);
        LaunchEventCommand = new RelayCommand(() => _stateMachine.LaunchEvent(), () => CurrentBoothState == BoothState.Setup);
        PrintCommand = new AsyncRelayCommand(PrintAsync, () => CanPrint);
        SendEmailCommand = new AsyncRelayCommand(SendEmailAsync, () => CanSendEmail);
        SendSmsCommand = new AsyncRelayCommand(SendSmsAsync, () => CanSendSms);
        DoneCommand = new RelayCommand(FinishSharing);
        OpenAdminCommand = new RelayCommand(Admin.Open);
        CancelSessionCommand = new RelayCommand(CancelSession);
        RetakeCommand = new RelayCommand(RetakeSession);
        ShareOnTwitterCommand = new RelayCommand(ShareOnTwitter);
        SelectFilterCommand = new RelayCommand(SelectFilter);
        SelectFrameCommand = new RelayCommand(SelectFrame);
        SelectTemplateCommand = new RelayCommand(SelectTemplate);
        RecordGuestbookMessageCommand = new RelayCommand(parameter =>
        {
            if (parameter is Guid requestToken)
            {
                _guestbookPrompt?.SubmitRecordDecision(true, requestToken);
            }
        });
        SkipGuestbookMessageCommand = new RelayCommand(parameter =>
        {
            if (parameter is Guid requestToken)
            {
                _guestbookPrompt?.SubmitRecordDecision(false, requestToken);
            }
        });
        StopGuestbookRecordingCommand = new RelayCommand(parameter =>
        {
            if (parameter is Guid requestToken)
            {
                _guestbookPrompt?.SubmitStop(requestToken);
            }
        });
        SelectFeedbackStarCommand = new RelayCommand(SelectFeedbackStar);
        SubmitFeedbackCommand = new RelayCommand(SubmitFeedback);
        SkipFeedbackCommand = new RelayCommand(parameter =>
        {
            if (parameter is Guid requestToken)
            {
                _feedback?.SubmitFeedback(new FeedbackResult(null, null), requestToken);
            }
        });
        SubmitSurveyCommand = new RelayCommand(SubmitSurvey);
        SkipSurveyCommand = new RelayCommand(() => _survey?.SubmitAnswers(Array.Empty<SurveyAnswer>()));

        PrintCommand.ExceptionHandler = ex => ShareStatus = $"Print failed: {ex.Message}";
        SendEmailCommand.ExceptionHandler = ex => ShareStatus = $"Email failed: {ex.Message}";
        SendSmsCommand.ExceptionHandler = ex => ShareStatus = $"SMS failed: {ex.Message}";

        // DispatcherPriority.Normal, _dispatcher explicitly -- not the parameterless
        // DispatcherTimer() constructor, which binds to Dispatcher.CurrentDispatcher
        // for whatever thread is *executing this constructor*. Same threadpool-thread
        // trap the _dispatcher assignment above already works around: BuildKioskViewModel
        // runs this constructor via Task.Run, so the parameterless ctor would create a
        // brand-new Dispatcher on a pooled thread nobody ever pumps -- every one of these
        // timers would silently never fire a single Tick (confirmed via a live repro: events
        // that cross threads through OnUi/_dispatcher.Invoke fired correctly, but the GIF
        // preview timer's own Tick handler never once ran across a multi-second window after
        // its Start(), even though decoding and Interval assignment both completed fine).
        _liveViewTimer = new DispatcherTimer(DispatcherPriority.Normal, _dispatcher) { Interval = LiveViewInterval };
        _liveViewTimer.Tick += async (_, _) => await PollLiveViewFrameAsync();

        _flashTimer = new DispatcherTimer(DispatcherPriority.Normal, _dispatcher) { Interval = FlashDuration };
        _flashTimer.Tick += (_, _) =>
        {
            _flashTimer.Stop();
            IsFlashing = false;
        };

        _shareTimer = new DispatcherTimer(DispatcherPriority.Normal, _dispatcher) { Interval = TimeSpan.FromSeconds(1) };
        _shareTimer.Tick += (_, _) => TickShareTimer();

        _gifPreviewTimer = new DispatcherTimer(DispatcherPriority.Normal, _dispatcher);
        _gifPreviewTimer.Tick += (_, _) => AdvanceGifPreviewFrame();

        _stateMachine.StateChanged += state => OnUi(() => ApplyState(state));
        _stateMachine.CountdownTick += value => OnUi(() => CountdownValue = value);
        _stateMachine.PoseChanged += (pose, total) => OnUi(() => OnPoseChanged(pose, total));
        _stateMachine.PosePhotoCaptured += (pose, total, path) => OnUi(() => OnPosePhotoCaptured(pose, path));
        _stateMachine.FrameCaptured += (frame, total, path) => OnUi(() => OnFrameCaptured(frame, total, path));
        _stateMachine.ErrorOccurred += message => OnUi(() =>
        {
            // Worth a permanent log line (not just the on-screen ErrorMessage): a booth
            // running unattended at an event has nobody to read the Error screen before it
            // times back out to Idle, so %LocalAppData%\Photobooth\logs is the only record
            // left afterward -- same reasoning as App.xaml.cs's DispatcherUnhandledException.
            Log.Error("Session error: {Message}", message);
            ErrorMessage = message;
            Admin.ErrorsThisRun++;
        });
        _stateMachine.PhotoUploaded += url => OnUi(() => ApplyUploadedPhoto(url));
        _stateMachine.AttendantCueChanged += clip => OnUi(() => AttendantCueRequested?.Invoke(clip));

        _filterSelection = filterSelection;
        if (_filterSelection is not null)
        {
            _filterSelection.SelectionRequestedWithToken += (options, token) => OnUi(() => ShowFilterOptions(options, token));
        }

        _frameSelection = frameSelection;
        if (_frameSelection is not null)
        {
            _frameSelection.SelectionRequestedWithToken += (options, token) => OnUi(() => ShowFrameOptions(options, token));
        }

        _templateSelection = templateSelection;
        if (_templateSelection is not null)
        {
            _templateSelection.SelectionRequestedWithToken += (options, token) => OnUi(() => ShowTemplateOptions(options, token));
        }

        _feedback = feedback;
        if (_feedback is not null)
        {
            _feedback.FeedbackRequestedWithToken += token => OnUi(() => ShowFeedbackPrompt(token));
        }

        _guestbookPrompt = guestbookPrompt;
        if (_guestbookPrompt is not null)
        {
            _guestbookPrompt.RecordDecisionRequestedWithToken += token => OnUi(() =>
            {
                GuestbookAskRequestToken = token;
                GuestbookSubScreen = GuestbookSubScreen.Ask;
            });
            _guestbookPrompt.StopRequestedWithToken += token => OnUi(() =>
            {
                GuestbookStopRequestToken = token;
                GuestbookSubScreen = GuestbookSubScreen.Recording;
            });
        }

        _survey = survey;
        if (_survey is not null)
        {
            _survey.AnswersRequested += questions => OnUi(() => ShowSurveyQuestions(questions));
        }

        ApplyState(_stateMachine.CurrentState);
        _ = ReloadSettingsAsync();

        // Flush anything left queued by a previous run (e.g. the venue's WiFi
        // was down at closing time) rather than waiting for the first guest of
        // the day to trigger the per-session retry. Same call MainWindow makes.
        _ = _stateMachine.RetryQueuedUploadsAsync();
    }

    /// <summary>
    /// Wires the ViewModel to Photobooth.Core's Mock* services so KioskWindow
    /// runs on any machine with no camera bridge, no printer, no LocalDB and no
    /// CLOUDINARY_URL -- useful for UI work and for the XAML designer.
    ///
    /// For a real booth, use <see cref="BoothCompositionRoot.BuildKioskViewModel"/>
    /// instead, which builds a real <see cref="BoothServices"/> and passes the
    /// concrete <c>Ui*</c>/<c>Sql*</c> instances this ViewModel needs to drive
    /// the FramePicker/Guestbook/Feedback/Survey screens. This factory
    /// deliberately keeps every one of those mocked: the Mock* implementations
    /// all resolve on their own after a short simulated delay (see e.g.
    /// MockFeedbackService), which is what lets a dev machine or the XAML
    /// designer run a full session with no guest actually tapping anything.
    /// </summary>
    public static KioskViewModel CreateWithMockServices() => new(
        new BoothServices(
            Camera: new MockCameraService(),
            Printer: new MockPrinterService(),
            CloudUpload: new MockCloudUploadService(),
            Sessions: new MockSessionRepository(),
            Payment: new MockQrPaymentService(),
            UploadQueue: new MockPendingUploadQueue(),
            Consent: new MockConsentService(),
            Email: new MockEmailDeliveryService(),
            Branding: new MockPhotoBrandingService(),
            Filter: new MockPhotoFilterService(),
            Settings: new MockBoothSettingsProvider(),
            FrameLibrary: new MockFrameLibraryService(),
            FrameSelection: new MockFrameSelectionService(),
            FrameOverlay: new MockFrameOverlayService(),
            Feedback: new MockFeedbackService(),
            GuestbookPrompt: new MockGuestbookPromptService(),
            VideoGuestbook: new MockVideoGuestbookService(),
            GifComposer: new MockGifComposerService(),
            BoothVideo: new MockBoothVideoService(),
            AttendantCue: new MockVirtualAttendantService(),
            Survey: new MockSurveyService())
        {
            Sms = new MockSmsDeliveryService(),
            GreenScreen = new MockGreenScreenService(),
            PostProcessing = new MockPostProcessingService(),
            FilterPreset = new MockFilterPresetService(),
            FilterSelection = new MockFilterSelectionService(),
        },
        new MockLiveViewService());

    public KioskAdminViewModel Admin { get; }

    /// <summary>Raised after screen-template overlay elements (admin-authored
    /// text/image/rectangle overlays -- see <see cref="ScreenTemplateElementRepository"/>)
    /// are (re)loaded whenever settings change, and at the existing Idle cadence
    /// as the rest of <see cref="ReloadSettingsAsync"/>. KioskWindow's code-behind
    /// subscribes to repaint its overlay Canvases -- kept out of this ViewModel
    /// since overlay rendering is FrameworkElement-construction glue, same
    /// reasoning MainWindow kept RenderScreenOverlay in code-behind.</summary>
    public event Action? ScreenOverlaysChanged;

    /// <summary>Raised when BoothStateMachine reports a Virtual Attendant clip
    /// to play. KioskWindow's code-behind subscribes to drive its MediaElement
    /// -- kept out of this ViewModel for the same reason as
    /// <see cref="ScreenOverlaysChanged"/>: MediaElement.Play() is a
    /// control-specific call with no clean ViewModel abstraction, same as
    /// MainWindow's own PlayAttendantCue never tried to abstract it either.</summary>
    public event Action<AttendantClip>? AttendantCueRequested;

    /// <summary>The admin-placed overlay elements for one guest-facing screen,
    /// for KioskWindow's code-behind to render into that screen's Canvas.</summary>
    public IReadOnlyList<ScreenTemplateElement> GetOverlayElements(ScreenTemplateScreen screen) =>
        _screenElementsByScreen[screen].ToList();

    // ======================================================= screen state ==

    private KioskScreen _currentScreenState = KioskScreen.Idle;

    /// <summary>The screen KioskWindow's switcher Grid is showing. Bound through
    /// EnumToVisibilityConverter, one binding per screen.</summary>
    public KioskScreen CurrentScreenState
    {
        get => _currentScreenState;
        private set
        {
            if (SetProperty(ref _currentScreenState, value))
            {
                RaisePropertyChanged(nameof(CanStartSession));
                StartSessionCommand.RaiseCanExecuteChanged();
                SelectModeCommand.RaiseCanExecuteChanged();
            }
        }
    }

    private BoothState _currentBoothState = BoothState.Setup;

    /// <summary>The underlying pipeline state, for the admin overlay's status
    /// line. Guests never see this -- they see <see cref="CurrentScreenState"/>.</summary>
    public BoothState CurrentBoothState
    {
        get => _currentBoothState;
        private set
        {
            if (SetProperty(ref _currentBoothState, value))
            {
                RaisePropertyChanged(nameof(IsBoothLocked));
                RaisePropertyChanged(nameof(IsIdleBlocked));
                LaunchEventCommand.RaiseCanExecuteChanged();

                // CanStartSession keys off THIS property, not CurrentScreenState:
                // Setup and Idle both map to the Idle screen, so launching the
                // event doesn't change CurrentScreenState and re-querying the
                // command from there alone would leave the touch-to-start target
                // permanently disabled. (Confirmed by running the kiosk: after
                // Launch Event, tapping the idle screen did nothing.)
                RaisePropertyChanged(nameof(CanStartSession));
                StartSessionCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>True until an admin has launched the event. The idle screen shows
    /// a locked prompt instead of "Touch Screen to Begin" -- a guest must not be
    /// able to start a session before the booth has been set up (see
    /// BoothState.Setup).</summary>
    public bool IsBoothLocked => CurrentBoothState == BoothState.Setup;

    private bool _isAdminLocked;

    /// <summary>Show Lock Screen (see AdminWindow's Show Lock Screen section
    /// and BoothSettings.IsLocked). Distinct from <see cref="IsBoothLocked"/>
    /// (pre-launch, BoothState.Setup) -- this blocks a *new* session on an
    /// already-launched, already-running event. Set two ways: the DB value,
    /// re-read at every return to Idle same as every other admin setting
    /// (see ReloadSettingsAsync); and immediately, live, by AdminWindow's own
    /// Lock Now/Unlock buttons via <see cref="KioskAdminViewModel.OnLockChanged"/>
    /// when reached from a running kiosk session -- waiting for the next Idle
    /// re-read would leave the booth briefly unlocked to whoever is standing
    /// at it right now.</summary>
    public bool IsAdminLocked
    {
        get => _isAdminLocked;
        set
        {
            if (SetProperty(ref _isAdminLocked, value))
            {
                RaisePropertyChanged(nameof(CanStartSession));
                RaisePropertyChanged(nameof(IsIdleBlocked));
                StartSessionCommand.RaiseCanExecuteChanged();
            }
        }
    }

    private bool _isTouchStartEnabled = true;

    /// <summary>ScreenSettings.SessionTriggerTouchScreen -- when off, the
    /// Idle screen's full-bleed tap target no longer starts a session; F13/
    /// keyboard triggers (see KioskWindow.xaml.cs's PreviewKeyDown handler)
    /// still can if their own toggles are on.</summary>
    public bool IsTouchStartEnabled
    {
        get => _isTouchStartEnabled;
        private set => SetProperty(ref _isTouchStartEnabled, value);
    }

    private double _unlockButtonOpacity = 0.1;

    /// <summary>ScreenSettings.UnlockButtonOpacityPercent / 100 -- how visible
    /// the hidden admin-unlock tap target (top-right corner) is.</summary>
    public double UnlockButtonOpacity
    {
        get => _unlockButtonOpacity;
        private set => SetProperty(ref _unlockButtonOpacity, value);
    }

    /// <summary>Either lock reason -- what the Idle screen's touch-to-start
    /// prompt and mode picker actually bind their Visibility to, since a
    /// single Bool-to-Visibility converter can't express an OR of two
    /// properties on its own.</summary>
    public bool IsIdleBlocked => IsBoothLocked || IsAdminLocked;

    public bool CanStartSession => CurrentBoothState == BoothState.Idle && !_sessionRunning && !IsAdminLocked;

    // ============================================================ branding ==

    private string _eventName = BoothTheme.Default.EventName;
    public string EventName
    {
        get => _eventName;
        private set => SetProperty(ref _eventName, value);
    }

    private ImageSource? _brandLogo;
    public ImageSource? BrandLogo
    {
        get => _brandLogo;
        private set => SetProperty(ref _brandLogo, value);
    }

    // =========================================================== countdown ==

    private int _countdownValue = 3;

    /// <summary>The big centred digit. KioskWindow binds this with
    /// NotifyOnTargetUpdated so each tick re-runs the scale-up storyboard.</summary>
    public int CountdownValue
    {
        get => _countdownValue;
        private set => SetProperty(ref _countdownValue, value);
    }

    private string _poseProgressText = string.Empty;

    /// <summary>"Pose 2 of 4" -- only meaningful for a true multi-pose template
    /// (PrintTemplate.RequiredPhotoCount > 1). Bound alongside CountdownValue/
    /// LiveViewStream on the Countdown/Capturing screens, visibility gated by
    /// ShowPoseProgress so a template with a single photo slot (every template
    /// before this feature) shows nothing extra.</summary>
    public string PoseProgressText
    {
        get => _poseProgressText;
        private set => SetProperty(ref _poseProgressText, value);
    }

    private bool _showPoseProgress;
    public bool ShowPoseProgress
    {
        get => _showPoseProgress;
        private set => SetProperty(ref _showPoseProgress, value);
    }

    private ImageSource? _liveViewStream;

    /// <summary>Latest live view frame, shown full-bleed behind the countdown.
    /// Null whenever the feed isn't running, in which case the countdown screen
    /// falls back to the flat canvas colour.</summary>
    public ImageSource? LiveViewStream
    {
        get => _liveViewStream;
        private set => SetProperty(ref _liveViewStream, value);
    }

    private Transform _liveViewTransform = Transform.Identity;

    /// <summary>Mirror/rotation from ScreenSettings, applied to the preview only
    /// -- the saved capture is untouched, same as MainWindow's behaviour.</summary>
    public Transform LiveViewTransform
    {
        get => _liveViewTransform;
        private set => SetProperty(ref _liveViewTransform, value);
    }

    private Stretch _liveViewStretch = Stretch.UniformToFill;

    /// <summary>ScreenSettings.CropLiveView: on (the historical default) fills
    /// the kiosk edge-to-edge, cropping whatever doesn't fit the aspect ratio,
    /// same as MainWindow's own live view; off shows the full uncropped frame
    /// letterboxed instead, for a guest who wants to see their whole body/the
    /// whole scene rather than a tight crop.</summary>
    public Stretch LiveViewStretch
    {
        get => _liveViewStretch;
        private set => SetProperty(ref _liveViewStretch, value);
    }

    private Brush _countdownColorBrush = Brushes.White;

    /// <summary>ScreenSettings.CountdownColorHex, converted once per settings
    /// reload rather than in a value converter -- same "compute it where the
    /// setting is read, bind the result directly" pattern LiveViewTransform
    /// above already uses for Mirror/Rotation.</summary>
    public Brush CountdownColorBrush
    {
        get => _countdownColorBrush;
        private set => SetProperty(ref _countdownColorBrush, value);
    }

    private Brush _welcomeBackgroundBrush = HexToBrush("#17181A");

    /// <summary>ScreenSettings.WelcomeBackgroundColorHex -- IdleScreen's own
    /// Background, replacing the fixed KioskCanvasBrush it inherited from the
    /// root Grid before this setting existed.</summary>
    public Brush WelcomeBackgroundBrush
    {
        get => _welcomeBackgroundBrush;
        private set => SetProperty(ref _welcomeBackgroundBrush, value);
    }

    private ImageSource? _welcomeBackgroundImage;

    /// <summary>ScreenSettings.WelcomeBackgroundImagePath -- an optional
    /// full-bleed image layered over WelcomeBackgroundBrush on IdleScreen.
    /// Null means no image (the color alone shows).</summary>
    public ImageSource? WelcomeBackgroundImage
    {
        get => _welcomeBackgroundImage;
        private set => SetProperty(ref _welcomeBackgroundImage, value);
    }

    private Uri? _welcomeBackgroundVideo;

    /// <summary>ScreenSettings.StartScreenVideoPath -- an optional full-bleed,
    /// muted, looping video layered over WelcomeBackgroundImage/Brush on
    /// IdleScreen (KioskWindow drives the actual looping via its
    /// WelcomeBackgroundMediaElement's MediaEnded handler, since MediaElement
    /// has no built-in repeat). Null means no video.</summary>
    public Uri? WelcomeBackgroundVideo
    {
        get => _welcomeBackgroundVideo;
        private set => SetProperty(ref _welcomeBackgroundVideo, value);
    }

    private Brush _captureBackgroundBrush = HexToBrush("#17181A");

    /// <summary>ScreenSettings.CaptureBackgroundColorHex -- CountdownScreen
    /// and CaptureScreen's shared Background (both map to the Screen
    /// Editor's single "Capture" tab).</summary>
    public Brush CaptureBackgroundBrush
    {
        get => _captureBackgroundBrush;
        private set => SetProperty(ref _captureBackgroundBrush, value);
    }

    private ImageSource? _captureBackgroundImage;

    /// <summary>ScreenSettings.CaptureBackgroundImagePath. Only actually
    /// visible when the live camera feed doesn't already cover it (i.e.
    /// ShowLiveView off), since the feed is drawn on top of it.</summary>
    public ImageSource? CaptureBackgroundImage
    {
        get => _captureBackgroundImage;
        private set => SetProperty(ref _captureBackgroundImage, value);
    }

    private Brush _sharingBackgroundBrush = HexToBrush("#17181A");

    /// <summary>ScreenSettings.SharingBackgroundColorHex -- ReviewScreen's
    /// own Background.</summary>
    public Brush SharingBackgroundBrush
    {
        get => _sharingBackgroundBrush;
        private set => SetProperty(ref _sharingBackgroundBrush, value);
    }

    private ImageSource? _sharingBackgroundImage;

    /// <summary>ScreenSettings.SharingBackgroundImagePath.</summary>
    public ImageSource? SharingBackgroundImage
    {
        get => _sharingBackgroundImage;
        private set => SetProperty(ref _sharingBackgroundImage, value);
    }

    private bool _isCancelButtonVisible;

    /// <summary>ScreenSettings.ShowCancelButton, shown only while a session is
    /// actually cancellable (Countdown/Capture) -- see CancelSessionCommand.</summary>
    public bool IsCancelButtonVisible
    {
        get => _isCancelButtonVisible;
        private set => SetProperty(ref _isCancelButtonVisible, value);
    }

    private bool _isDoneButtonVisible = true;

    /// <summary>ScreenSettings.ShowDoneButton.</summary>
    public bool IsDoneButtonVisible
    {
        get => _isDoneButtonVisible;
        private set => SetProperty(ref _isDoneButtonVisible, value);
    }

    private bool _isRetakeVisible;

    /// <summary>ScreenSettings.ShowRetakeButton, shown alongside Done on the
    /// Review screen -- see RetakeCommand.</summary>
    public bool IsRetakeVisible
    {
        get => _isRetakeVisible;
        private set => SetProperty(ref _isRetakeVisible, value);
    }

    // ===== Booth Icons group (Welcome), Sharing icon row, Capture Cancel
    // button -- free-positioned/toggleable/row-or-column groups edited via
    // ScreenTemplateEditorWindow's DESIGN tab (see IconGroupLayout). Percent
    // properties are top-left-of-group anchors, same 0-1-of-screen convention
    // GetOverlayElements' ScreenTemplateElement.XPercent/YPercent already use;
    // KioskWindow converts them to pixels against WelcomeIconsPositionCanvas/
    // SharingIconsPositionCanvas/CaptureCancelPositionCanvas's own
    // ActualWidth/ActualHeight, the same SizeChanged-driven pattern
    // RenderScreenOverlay already uses for ScreenTemplateElement rows.

    private bool _isWelcomeIconsGroupVisible = true;

    /// <summary>ScreenSettings.BoothIconsEnabled.</summary>
    public bool IsWelcomeIconsGroupVisible
    {
        get => _isWelcomeIconsGroupVisible;
        private set => SetProperty(ref _isWelcomeIconsGroupVisible, value);
    }

    private bool _isWelcomeIconLabelsVisible = true;

    /// <summary>ScreenSettings.BoothIconLabelsEnabled.</summary>
    public bool IsWelcomeIconLabelsVisible
    {
        get => _isWelcomeIconLabelsVisible;
        private set => SetProperty(ref _isWelcomeIconLabelsVisible, value);
    }

    private bool _isWelcomePhotoIconVisible = true;
    public bool IsWelcomePhotoIconVisible { get => _isWelcomePhotoIconVisible; private set => SetProperty(ref _isWelcomePhotoIconVisible, value); }

    private bool _isWelcomeGifIconVisible = true;
    public bool IsWelcomeGifIconVisible { get => _isWelcomeGifIconVisible; private set => SetProperty(ref _isWelcomeGifIconVisible, value); }

    private bool _isWelcomeBoomerangIconVisible = true;
    public bool IsWelcomeBoomerangIconVisible { get => _isWelcomeBoomerangIconVisible; private set => SetProperty(ref _isWelcomeBoomerangIconVisible, value); }

    private bool _isWelcomeVideoIconVisible = true;
    public bool IsWelcomeVideoIconVisible { get => _isWelcomeVideoIconVisible; private set => SetProperty(ref _isWelcomeVideoIconVisible, value); }

    private Orientation _welcomeIconsOrientation = Orientation.Horizontal;

    /// <summary>ScreenSettings.WelcomeIconsLayout ("Row"/"Column"), converted
    /// once per settings reload for a direct StackPanel.Orientation binding.</summary>
    public Orientation WelcomeIconsOrientation
    {
        get => _welcomeIconsOrientation;
        private set => SetProperty(ref _welcomeIconsOrientation, value);
    }

    private double _welcomeIconsPositionXPercent = 0.5;
    public double WelcomeIconsPositionXPercent { get => _welcomeIconsPositionXPercent; private set => SetProperty(ref _welcomeIconsPositionXPercent, value); }

    private double _welcomeIconsPositionYPercent = 0.72;
    public double WelcomeIconsPositionYPercent { get => _welcomeIconsPositionYPercent; private set => SetProperty(ref _welcomeIconsPositionYPercent, value); }

    private double _captureCancelButtonPositionXPercent = 0.5;

    /// <summary>ScreenSettings.CaptureCancelButtonPositionXPercent -- shared
    /// by both the Countdown and Capture screens' own Cancel button (both map
    /// to the Screen Editor's single "Capture" tab, same as
    /// CaptureBackgroundBrush above).</summary>
    public double CaptureCancelButtonPositionXPercent { get => _captureCancelButtonPositionXPercent; private set => SetProperty(ref _captureCancelButtonPositionXPercent, value); }

    private double _captureCancelButtonPositionYPercent = 0.93;
    public double CaptureCancelButtonPositionYPercent { get => _captureCancelButtonPositionYPercent; private set => SetProperty(ref _captureCancelButtonPositionYPercent, value); }

    // ===== Pose thumbnail strip -- the Capture screen's filmstrip of
    // already-taken shots, docked to whichever edge ScreenSettings.
    // PoseStripPosition names (see ScreenTemplateEditorWindow's "Thumbnail
    // Strip Settings" section). Alignment/Orientation are computed once per
    // settings reload (ReloadSettingsCoreAsync), same "compute where the
    // setting is read" pattern CountdownColorBrush/LiveViewTransform above
    // already use; PoseThumbnails itself is (re)built per multi-pose session
    // by the PoseChanged/PosePhotoCaptured handlers below, not here.

    private bool _isPoseThumbnailStripVisible;

    /// <summary>ScreenSettings.PhotoThumbnailsEnabled, gated by the same
    /// "true multi-pose template" condition ShowPoseProgress already uses --
    /// set alongside ShowPoseProgress in the PoseChanged handler below.</summary>
    public bool IsPoseThumbnailStripVisible
    {
        get => _isPoseThumbnailStripVisible;
        private set => SetProperty(ref _isPoseThumbnailStripVisible, value);
    }

    private HorizontalAlignment _poseStripHorizontalAlignment = HorizontalAlignment.Stretch;
    public HorizontalAlignment PoseStripHorizontalAlignment { get => _poseStripHorizontalAlignment; private set => SetProperty(ref _poseStripHorizontalAlignment, value); }

    private VerticalAlignment _poseStripVerticalAlignment = VerticalAlignment.Bottom;
    public VerticalAlignment PoseStripVerticalAlignment { get => _poseStripVerticalAlignment; private set => SetProperty(ref _poseStripVerticalAlignment, value); }

    private Orientation _poseStripOrientation = Orientation.Horizontal;

    /// <summary>Row for Top/Bottom, column for Left/Right -- follows
    /// PoseStripPosition automatically, no separate admin control.</summary>
    public Orientation PoseStripOrientation { get => _poseStripOrientation; private set => SetProperty(ref _poseStripOrientation, value); }

    private Brush _poseStripBackgroundBrush = CreateFrozenPoseStripDefaultBrush();

    /// <summary>ScreenSettings.PoseStripBackgroundOpacityPercent, converted to
    /// a black brush with that alpha -- the strip's own backing panel.</summary>
    public Brush PoseStripBackgroundBrush { get => _poseStripBackgroundBrush; private set => SetProperty(ref _poseStripBackgroundBrush, value); }

    private Brush _poseStripActiveBorderBrush = HexToBrush("#2ED9A0");

    /// <summary>ScreenSettings.PoseStripActiveBorderColorHex -- the border
    /// drawn around whichever slot is the pose currently being captured.</summary>
    public Brush PoseStripActiveBorderBrush { get => _poseStripActiveBorderBrush; private set => SetProperty(ref _poseStripActiveBorderBrush, value); }

    /// <summary>One slot per PrintTemplate.RequiredPhotoCount, rebuilt at the
    /// start of each multi-pose session (see the PoseChanged handler in the
    /// constructor) and filled in as PosePhotoCaptured lands each shot. Empty
    /// for a single-photo template/session, same as PoseThumbnailStripVisible
    /// staying false in that case.</summary>
    public ObservableCollection<PoseThumbnailSlot> PoseThumbnails { get; } = new();

    private bool _isSharingIconsGroupVisible = true;

    /// <summary>ScreenSettings.SharingIconsGroupEnabled.</summary>
    public bool IsSharingIconsGroupVisible
    {
        get => _isSharingIconsGroupVisible;
        private set => SetProperty(ref _isSharingIconsGroupVisible, value);
    }

    private Orientation _sharingIconsOrientation = Orientation.Vertical;

    /// <summary>ScreenSettings.SharingIconsLayout ("Row"/"Column").</summary>
    public Orientation SharingIconsOrientation
    {
        get => _sharingIconsOrientation;
        private set => SetProperty(ref _sharingIconsOrientation, value);
    }

    private double _sharingIconsPositionXPercent = 0.56;
    public double SharingIconsPositionXPercent { get => _sharingIconsPositionXPercent; private set => SetProperty(ref _sharingIconsPositionXPercent, value); }

    private double _sharingIconsPositionYPercent = 0.32;
    public double SharingIconsPositionYPercent { get => _sharingIconsPositionYPercent; private set => SetProperty(ref _sharingIconsPositionYPercent, value); }

    private bool _isFlashing;

    /// <summary>Drives the white flash wash on the capture screen.</summary>
    public bool IsFlashing
    {
        get => _isFlashing;
        private set => SetProperty(ref _isFlashing, value);
    }

    // ========================================================== processing ==

    private string _processingStatus = "Processing your photo strip...";
    public string ProcessingStatus
    {
        get => _processingStatus;
        private set => SetProperty(ref _processingStatus, value);
    }

    private string? _errorMessage;
    public string? ErrorMessage
    {
        get => _errorMessage;
        private set => SetProperty(ref _errorMessage, value);
    }

    // ======================================================== filter picker ==

    /// <summary>Populated when <see cref="_filterSelection"/> raises SelectionRequested
    /// (i.e. right as BoothStateMachine enters FilterPicker). Empty in mock mode or
    /// before that screen is reached -- MockFilterSelectionService resolves on its
    /// own without ever raising the event, same reasoning FrameOptions/
    /// MockFrameSelectionService already give.</summary>
    public ObservableCollection<FilterSelectionItem> FilterOptions { get; } = new();

    public RelayCommand SelectFilterCommand { get; }

    /// <summary>Bound to each filter preview's CommandParameter.</summary>
    private void SelectFilter(object? parameter)
    {
        if (parameter is FilterSelectionItem item)
        {
            _filterSelection?.SubmitSelection(item.Option, item.RequestToken);
        }
    }

    private void ShowFilterOptions(IReadOnlyList<FilterOption> options, Guid requestToken)
    {
        FilterRequestToken = requestToken;
        FilterOptions.Clear();
        foreach (FilterOption option in options)
        {
            FilterOptions.Add(new FilterSelectionItem(option, requestToken));
        }
    }

    // ========================================================= frame picker ==

    /// <summary>Populated when <see cref="_frameSelection"/> raises SelectionRequested
    /// (i.e. right as BoothStateMachine enters FramePicker). Empty in mock mode or
    /// before that screen is reached -- MockFrameSelectionService resolves on its
    /// own without ever raising the event, matching MainWindow's ShowFrameOptions.</summary>
    public ObservableCollection<FrameSelectionItem> FrameOptions { get; } = new();

    public RelayCommand SelectFrameCommand { get; }

    /// <summary>Bound to each frame thumbnail's CommandParameter, and to the
    /// "No frame" button with a null parameter.</summary>
    private void SelectFrame(object? parameter)
    {
        if (parameter is FrameSelectionItem item)
        {
            _frameSelection?.SubmitSelection(item.Option, item.RequestToken);
        }
        else if (parameter is Guid requestToken)
        {
            _frameSelection?.SubmitSelection(null, requestToken);
        }
    }

    private void ShowFrameOptions(IReadOnlyList<FrameOption> options, Guid requestToken)
    {
        FrameRequestToken = requestToken;
        FrameOptions.Clear();
        foreach (FrameOption option in options)
        {
            FrameOptions.Add(new FrameSelectionItem(option, requestToken));
        }
    }

    private Guid? _frameRequestToken;
    public Guid? FrameRequestToken
    {
        get => _frameRequestToken;
        private set => SetProperty(ref _frameRequestToken, value);
    }

    // ====================================================== template picker ==

    /// <summary>Populated when <see cref="_templateSelection"/> raises
    /// SelectionRequested (i.e. right as BoothStateMachine enters its now-first
    /// FramePicker state). Empty in mock mode, before that screen is reached, or
    /// whenever the admin has no favorited saved templates -- same reasoning
    /// FrameOptions/MockTemplateSelectionService already give. This is the
    /// guest's "which layout am I getting" choice -- replaces the old sticker-
    /// overlay frame picker as the only source of "frame" choices.</summary>
    public ObservableCollection<TemplateSelectionItem> TemplateOptions { get; } = new();

    public RelayCommand SelectTemplateCommand { get; }

    /// <summary>Bound to each layout tile's CommandParameter, and to the
    /// "Use default layout" button with a null parameter.</summary>
    private void SelectTemplate(object? parameter)
    {
        if (parameter is TemplateSelectionItem item)
        {
            _templateSelection?.SubmitSelection(item.Option, item.RequestToken);
        }
        else if (parameter is Guid requestToken)
        {
            _templateSelection?.SubmitSelection(null, requestToken);
        }
    }

    private void ShowTemplateOptions(IReadOnlyList<PrintTemplate> options, Guid requestToken)
    {
        TemplateRequestToken = requestToken;
        TemplateOptions.Clear();
        foreach (PrintTemplate option in options)
        {
            TemplateOptions.Add(new TemplateSelectionItem(option, requestToken));
        }
    }

    private Guid? _templateRequestToken;
    public Guid? TemplateRequestToken
    {
        get => _templateRequestToken;
        private set => SetProperty(ref _templateRequestToken, value);
    }

    private Guid? _filterRequestToken;
    public Guid? FilterRequestToken
    {
        get => _filterRequestToken;
        private set => SetProperty(ref _filterRequestToken, value);
    }

    // ============================================================ payment ==

    private string? _paymentInstructions;

    /// <summary>Gateway-specific guest-facing text (e.g. "Scan to pay with GCash"),
    /// mirrors MainWindow's PaymentInstructionsText -- see BoothStateMachine.PaymentInstructions.</summary>
    public string? PaymentInstructions
    {
        get => _paymentInstructions;
        private set => SetProperty(ref _paymentInstructions, value);
    }

    private ImageSource? _paymentQrCode;

    /// <summary>Null for a gateway with no QR (e.g. a card reader) -- the Payment
    /// screen hides the QR card entirely in that case, same as MainWindow's
    /// PaymentQrBorder visibility.</summary>
    public ImageSource? PaymentQrCode
    {
        get => _paymentQrCode;
        private set => SetProperty(ref _paymentQrCode, value);
    }

    // =========================================================== guestbook ==

    private GuestbookSubScreen _guestbookSubScreen = GuestbookSubScreen.Ask;

    /// <summary>Which sub-panel the Guestbook screen shows -- set by
    /// <see cref="_guestbookPrompt"/>'s RecordDecisionRequested/StopRequested events,
    /// mirroring MainWindow's ShowGuestbookAskPrompt/ShowGuestbookRecordingPrompt.</summary>
    public GuestbookSubScreen GuestbookSubScreen
    {
        get => _guestbookSubScreen;
        private set => SetProperty(ref _guestbookSubScreen, value);
    }

    public RelayCommand RecordGuestbookMessageCommand { get; }
    public RelayCommand SkipGuestbookMessageCommand { get; }
    public RelayCommand StopGuestbookRecordingCommand { get; }

    private Guid? _guestbookAskRequestToken;
    public Guid? GuestbookAskRequestToken
    {
        get => _guestbookAskRequestToken;
        private set => SetProperty(ref _guestbookAskRequestToken, value);
    }

    private Guid? _guestbookStopRequestToken;
    public Guid? GuestbookStopRequestToken
    {
        get => _guestbookStopRequestToken;
        private set => SetProperty(ref _guestbookStopRequestToken, value);
    }

    // ============================================================ feedback ==

    private int _selectedFeedbackRating;

    /// <summary>The guest's tapped star (1-5), or 0 if none tapped yet. Reset
    /// whenever <see cref="_feedback"/> raises FeedbackRequested.</summary>
    public int SelectedFeedbackRating
    {
        get => _selectedFeedbackRating;
        private set => SetProperty(ref _selectedFeedbackRating, value);
    }

    private string _feedbackComment = string.Empty;
    public string FeedbackComment
    {
        get => _feedbackComment;
        set => SetProperty(ref _feedbackComment, value);
    }

    public RelayCommand SelectFeedbackStarCommand { get; }
    public RelayCommand SubmitFeedbackCommand { get; }
    public RelayCommand SkipFeedbackCommand { get; }

    /// <summary>Bound to each star button's CommandParameter ("1".."5").</summary>
    private void SelectFeedbackStar(object? parameter)
    {
        if (parameter is int rating)
        {
            SelectedFeedbackRating = rating;
        }
        else if (parameter is string text && int.TryParse(text, out int parsed))
        {
            SelectedFeedbackRating = parsed;
        }
    }

    private void SubmitFeedback(object? parameter)
    {
        if (parameter is not Guid requestToken)
        {
            return;
        }

        int? rating = SelectedFeedbackRating > 0 ? SelectedFeedbackRating : null;
        string? comment = string.IsNullOrWhiteSpace(FeedbackComment) ? null : FeedbackComment.Trim();
        _feedback?.SubmitFeedback(new FeedbackResult(rating, comment), requestToken);
    }

    private void ShowFeedbackPrompt(Guid requestToken)
    {
        FeedbackRequestToken = requestToken;
        SelectedFeedbackRating = 0;
        FeedbackComment = string.Empty;
    }

    private Guid? _feedbackRequestToken;
    public Guid? FeedbackRequestToken
    {
        get => _feedbackRequestToken;
        private set => SetProperty(ref _feedbackRequestToken, value);
    }

    // ============================================================== survey ==

    /// <summary>One row per active question, populated when <see cref="_survey"/>
    /// raises AnswersRequested -- mirrors MainWindow's dynamic TextBox-per-question,
    /// just as bindable rows instead of code-behind-constructed controls.</summary>
    public ObservableCollection<SurveyQuestionAnswer> SurveyAnswers { get; } = new();

    public RelayCommand SubmitSurveyCommand { get; }
    public RelayCommand SkipSurveyCommand { get; }

    private void SubmitSurvey()
    {
        var answers = SurveyAnswers
            .Where(row => !string.IsNullOrWhiteSpace(row.Answer))
            .Select(row => new SurveyAnswer(row.SurveyQuestionId, row.Answer.Trim()))
            .ToList();
        _survey?.SubmitAnswers(answers);
    }

    private void ShowSurveyQuestions(IReadOnlyList<SurveyQuestion> questions)
    {
        SurveyAnswers.Clear();
        foreach (SurveyQuestion question in questions)
        {
            SurveyAnswers.Add(new SurveyQuestionAnswer(question.SurveyQuestionId, question.Text));
        }
    }

    // ========================================================= review/share ==

    private ImageSource? _templatePreview;

    /// <summary>The rendered print template (4x6 single, or a strip), composed by
    /// PrintCompositor -- the same code path the printer draws through, so what
    /// the guest reviews is what comes out of the printer.</summary>
    public ImageSource? TemplatePreview
    {
        get => _templatePreview;
        private set => SetProperty(ref _templatePreview, value);
    }

    private string? _previewUnavailableReason;

    /// <summary>Set instead of <see cref="TemplatePreview"/> for a capture with no
    /// still to composite (Video mode), so the review screen explains itself
    /// rather than showing an empty frame.</summary>
    public string? PreviewUnavailableReason
    {
        get => _previewUnavailableReason;
        private set => SetProperty(ref _previewUnavailableReason, value);
    }

    private ImageSource? _qrCodeImage;

    /// <summary>QR for the uploaded photo's download URL. Null until the
    /// background upload finishes (or forever, if it fails) -- the sharing panel
    /// shows a "still uploading" line in its place rather than a dead frame.</summary>
    public ImageSource? QrCodeImage
    {
        get => _qrCodeImage;
        private set => SetProperty(ref _qrCodeImage, value);
    }

    private bool _isPrinting;

    /// <summary>True while the spooler job is in flight, whether it was started
    /// automatically by the state machine or by the guest's Print button.</summary>
    public bool IsPrinting
    {
        get => _isPrinting;
        private set
        {
            if (SetProperty(ref _isPrinting, value))
            {
                PrintCommand.RaiseCanExecuteChanged();
            }
        }
    }

    private int _printsRemaining;

    /// <summary>Badge on the Print button: PrintOptions.PrintLimitPerSession minus
    /// what this session has already spooled.</summary>
    public int PrintsRemaining
    {
        get => _printsRemaining;
        private set
        {
            if (SetProperty(ref _printsRemaining, value))
            {
                RaisePropertyChanged(nameof(CanPrint));
                PrintCommand.RaiseCanExecuteChanged();
            }
        }
    }

    private string _shareEmail = string.Empty;
    public string ShareEmail
    {
        get => _shareEmail;
        set
        {
            if (SetProperty(ref _shareEmail, value))
            {
                RaisePropertyChanged(nameof(CanSendEmail));
                SendEmailCommand.RaiseCanExecuteChanged();
            }
        }
    }

    private string _sharePhone = string.Empty;
    public string SharePhone
    {
        get => _sharePhone;
        set
        {
            if (SetProperty(ref _sharePhone, value))
            {
                RaisePropertyChanged(nameof(CanSendSms));
                SendSmsCommand.RaiseCanExecuteChanged();
            }
        }
    }

    private string? _shareStatus;

    /// <summary>One-line confirmation/failure under the share controls ("Sent to
    /// ...", "Print failed: ..."). Cleared at the start of every session.</summary>
    public string? ShareStatus
    {
        get => _shareStatus;
        private set => SetProperty(ref _shareStatus, value);
    }

    private bool _emailEnabled = true;
    public bool IsEmailEnabled
    {
        get => _emailEnabled;
        private set
        {
            if (SetProperty(ref _emailEnabled, value))
            {
                RaisePropertyChanged(nameof(CanSendEmail));
                SendEmailCommand.RaiseCanExecuteChanged();
            }
        }
    }

    private bool _smsEnabled;
    public bool IsSmsEnabled
    {
        get => _smsEnabled;
        private set
        {
            if (SetProperty(ref _smsEnabled, value))
            {
                RaisePropertyChanged(nameof(CanSendSms));
                SendSmsCommand.RaiseCanExecuteChanged();
            }
        }
    }

    private bool _qrEnabled = true;
    public bool IsQrEnabled
    {
        get => _qrEnabled;
        private set => SetProperty(ref _qrEnabled, value);
    }

    private bool _isTwitterEnabled;

    /// <summary>SharingSettings.TwitterEnabled -- see ShareOnTwitterCommand.</summary>
    public bool IsTwitterEnabled
    {
        get => _isTwitterEnabled;
        private set => SetProperty(ref _isTwitterEnabled, value);
    }

    private bool _isPrintButtonVisible;

    /// <summary>PrintOptions.ShowPrintButton -- the automatic print
    /// (PrintOptions.PrintAutomatically) already runs before this screen is
    /// reachable; this is the guest's own manual reprint button, opt-in
    /// rather than always shown, matching the setting's own default of false.</summary>
    public bool IsPrintButtonVisible
    {
        get => _isPrintButtonVisible;
        private set => SetProperty(ref _isPrintButtonVisible, value);
    }

    private bool _isSharingTextLabelsEnabled = true;

    /// <summary>ScreenSettings.SharingTextLabelsEnabled -- the captions next
    /// to each sharing option ("EMAIL IT TO ME", "Scan to download", etc.).</summary>
    public bool IsSharingTextLabelsEnabled
    {
        get => _isSharingTextLabelsEnabled;
        private set => SetProperty(ref _isSharingTextLabelsEnabled, value);
    }

    public bool CanPrint => PrintsRemaining > 0 && !IsPrinting && _stateMachine.LastCapturedImagePaths.Count > 0;

    public bool CanSendEmail => IsEmailEnabled && LooksLikeEmail(ShareEmail) && _stateMachine.LastPhotoUrl is not null;

    public bool CanSendSms => IsSmsEnabled && SharePhone.Trim().Length >= 7;

    // ---- Done timer bar -------------------------------------------------
    // A visible "this screen is about to reset" affordance. It does NOT drive
    // the reset: BoothStateMachine owns the actual return to Idle (its Complete
    // dwell, then guestbook/feedback/survey, then the finally that sets Idle).
    // The bar is clamped at zero rather than forcing a transition, so the two
    // can never fight over who ends the session.

    private double _shareSecondsTotal = 30;
    public double ShareSecondsTotal
    {
        get => _shareSecondsTotal;
        private set => SetProperty(ref _shareSecondsTotal, value);
    }

    private double _shareSecondsRemaining;
    public double ShareSecondsRemaining
    {
        get => _shareSecondsRemaining;
        private set => SetProperty(ref _shareSecondsRemaining, value);
    }

    /// <summary>Which event/location this kiosk is running -- null in mock/
    /// designer mode. Lets KioskWindow open AdminWindow scoped to the same
    /// event it's actually running, rather than AdminWindow guessing at
    /// "the first Location row" once more than one event can exist (see
    /// EventLauncherWindow).</summary>
    public int? LocationId => _locationId;

    // ============================================================ commands ==

    public RelayCommand StartSessionCommand { get; }
    public RelayCommand SelectModeCommand { get; }
    public RelayCommand LaunchEventCommand { get; }
    public AsyncRelayCommand PrintCommand { get; }
    public AsyncRelayCommand SendEmailCommand { get; }
    public AsyncRelayCommand SendSmsCommand { get; }
    public RelayCommand DoneCommand { get; }
    public RelayCommand CancelSessionCommand { get; }
    public RelayCommand RetakeCommand { get; }
    public RelayCommand ShareOnTwitterCommand { get; }
    public RelayCommand OpenAdminCommand { get; }

    private CaptureMode _selectedCaptureMode = CaptureMode.Photo;

    /// <summary>The tile the guest has highlighted on the idle screen. Pushed into
    /// the settings override so the very next session captures in that mode.</summary>
    public CaptureMode SelectedCaptureMode
    {
        get => _selectedCaptureMode;
        private set
        {
            if (SetProperty(ref _selectedCaptureMode, value))
            {
                _captureModeOverride.Mode = value.ToString();
            }
        }
    }

    private void SelectMode(object? parameter)
    {
        if (parameter is CaptureMode mode)
        {
            SelectedCaptureMode = mode;
        }
        else if (parameter is string name && Enum.TryParse(name, ignoreCase: true, out CaptureMode parsed))
        {
            SelectedCaptureMode = parsed;
        }
    }

    private CancellationTokenSource? _sessionCts;
    private bool _retakeRequested;

    private void StartSession()
    {
        if (!CanStartSession)
        {
            return;
        }

        _sessionCts = new CancellationTokenSource();
        _sessionRunning = true;
        RaisePropertyChanged(nameof(CanStartSession));
        StartSessionCommand.RaiseCanExecuteChanged();
        Admin.SessionsThisRun++;

        _ = RunSessionAsync();
    }

    /// <summary>ScreenSettings.SessionTriggerF13/SessionTriggerKeys -- called
    /// from KioskWindow.xaml.cs's PreviewKeyDown for F13, Space, S, PageUp and
    /// PageDown. Each key trigger is gated by its own admin toggle, unlike
    /// StartSessionCommand's touch-target Visibility (IsTouchStartEnabled)
    /// which the Idle screen's tap target binds directly.</summary>
    public void TryStartSessionFromKey(bool isF13)
    {
        bool allowed = isF13 ? _settings.Screen.SessionTriggerF13 : _settings.Screen.SessionTriggerKeys;
        if (allowed)
        {
            StartSession();
        }
    }

    /// <summary>Cancels the in-progress session (Countdown/Capture's Cancel
    /// button, gated by ScreenSettings.ShowCancelButton) -- BoothStateMachine
    /// treats the resulting OperationCanceledException as a deliberate stop,
    /// not an Error, and its own finally already returns to Idle.</summary>
    private void CancelSession() => _sessionCts?.Cancel();

    /// <summary>Retake (Review's Retake button, gated by
    /// ScreenSettings.ShowRetakeButton): cancels the current session same as
    /// CancelSession, but RunSessionAsync's own finally below notices
    /// _retakeRequested once the cancelled session has actually unwound and
    /// immediately starts a fresh one, rather than leaving the guest back at
    /// the Idle screen -- a "let's do this again" gesture, not a "stop".</summary>
    private void RetakeSession()
    {
        _retakeRequested = true;
        _sessionCts?.Cancel();
    }

    /// <summary>Twitter/X's web share intent needs no app registration or
    /// OAuth (see SharingSettings.TwitterEnabled's own doc comment) -- opens
    /// the guest's/attendant's default browser to a pre-filled compose window
    /// pointed at the uploaded photo's public URL.</summary>
    private void ShareOnTwitter()
    {
        if (_stateMachine.LastPhotoUrl is not Uri url)
        {
            return;
        }

        string tweetText = Uri.EscapeDataString($"Check out my photo from {EventName}!");
        string tweetUrl = Uri.EscapeDataString(url.ToString());
        try
        {
            Process.Start(new ProcessStartInfo($"https://twitter.com/intent/tweet?text={tweetText}&url={tweetUrl}") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ShareStatus = $"Couldn't open Twitter: {ex.Message}";
        }
    }

    private async Task RunSessionAsync()
    {
        try
        {
            await _stateMachine.RunSessionAsync(_sessionCts?.Token ?? default);
        }
        finally
        {
            OnUi(() =>
            {
                _sessionCts?.Dispose();
                _sessionCts = null;
                _sessionRunning = false;
                RaisePropertyChanged(nameof(CanStartSession));
                StartSessionCommand.RaiseCanExecuteChanged();

                if (_retakeRequested)
                {
                    _retakeRequested = false;
                    StartSession();
                }
            });
        }
    }

    /// <summary>Guest-initiated reprint from the sharing screen. Separate from the
    /// state machine's automatic print (PrintOptions.PrintAutomatically), which
    /// has already run by the time this screen is reachable -- hence the
    /// per-session limit being decremented in both places.</summary>
    private async Task PrintAsync()
    {
        if (_stateMachine.LastCapturedImagePaths.Count == 0)
        {
            return;
        }

        IsPrinting = true;
        ShareStatus = "Sending to printer...";
        try
        {
            var context = new PrintRenderContext(_stateMachine.LastPhotoUrl, _settings.Theme.EventName, DateTime.Now);
            // A guest reprint uses whatever template this session actually printed
            // with (see BoothStateMachine's now-first FramePicker step), not always
            // the location's default -- otherwise a guest who picked a different
            // saved layout would get a reprint that doesn't match their first print.
            PrintTemplate reprintTemplate = _stateMachine.LastSelectedTemplate ?? _settings.PrintTemplate;
            await _services.Printer.PrintAsync(_stateMachine.LastCapturedImagePaths, reprintTemplate, context);
            PrintsRemaining = Math.Max(0, PrintsRemaining - 1);
            Admin.PrintsThisSession++;
            Admin.PrintsThisEvent++;
            ShareStatus = "Printing -- your photo is on its way.";
        }
        finally
        {
            IsPrinting = false;
        }
    }

    private async Task SendEmailAsync()
    {
        if (_stateMachine.LastPhotoUrl is not Uri url)
        {
            ShareStatus = "Your photo is still uploading -- try again in a moment.";
            return;
        }

        string address = ShareEmail.Trim();
        try
        {
            await _services.Email.SendPhotoLinkAsync(address, url);
            await LogShareAttemptAsync("Email", address, url, status: "Sent", errorMessage: null);
        }
        catch (Exception ex)
        {
            await LogShareAttemptAsync("Email", address, url, status: "Failed", errorMessage: ex.Message);
            throw;
        }
        ShareStatus = $"Sent to {address}.";
        ShareEmail = string.Empty;
    }

    private async Task SendSmsAsync()
    {
        if (_stateMachine.LastPhotoUrl is not Uri url)
        {
            ShareStatus = "Your photo is still uploading -- try again in a moment.";
            return;
        }

        string phone = SharePhone.Trim();
        try
        {
            await _services.Sms.SendPhotoLinkAsync(phone, url);
            await LogShareAttemptAsync("SMS", phone, url, status: "Sent", errorMessage: null);
        }
        catch (Exception ex)
        {
            await LogShareAttemptAsync("SMS", phone, url, status: "Failed", errorMessage: ex.Message);
            throw;
        }
        ShareStatus = $"Sent to {phone}.";
        SharePhone = string.Empty;
    }

    /// <summary>Records one Sharing Status row (see AdminWindow's Sharing
    /// Status section) -- skipped entirely in mock/designer mode, same
    /// `_locationId is int` guard the Phase-6 screen-overlay reads already
    /// use, since there's no LocalDB to write to there. A logging failure
    /// (e.g. a dropped DB connection) is swallowed rather than surfaced --
    /// it must never turn a real, successful send into a "Send failed"
    /// message the guest sees, or vice versa mask a real send failure behind
    /// a logging exception.</summary>
    private async Task LogShareAttemptAsync(string method, string destination, Uri photoUrl, string status, string? errorMessage)
    {
        if (_locationId is null || _stateMachine.LastSessionId is not int sessionId)
        {
            return;
        }

        try
        {
            await _sharingLog.InsertAsync(sessionId, method, destination, photoUrl.ToString(), status, errorMessage);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Couldn't record SharingLog row for session {SessionId}", sessionId);
        }
    }

    private void FinishSharing()
    {
        ShareSecondsRemaining = 0;
        _shareTimer.Stop();
        ShareStatus = null;
    }

    private void TickShareTimer()
    {
        ShareSecondsRemaining = Math.Max(0, ShareSecondsRemaining - 1);
        if (ShareSecondsRemaining <= 0)
        {
            _shareTimer.Stop();
        }
    }

    // =============================================== state machine plumbing ==

    private void ApplyState(BoothState state)
    {
        CurrentBoothState = state;
        CurrentScreenState = MapScreen(state);
        ProcessingStatus = DescribeProcessing(state);

        UpdateLiveView(state);
        UpdateFlash(state);
        IsCancelButtonVisible = _settings.Screen.ShowCancelButton
            && (CurrentScreenState == KioskScreen.Countdown || CurrentScreenState == KioskScreen.Capture);
        IsRetakeVisible = _settings.Screen.ShowRetakeButton && CurrentScreenState == KioskScreen.Review;

        if (state == BoothState.Idle)
        {
            ResetForNextGuest();
            // Re-read settings/theme at Idle as a fallback for changes made by
            // another process or while the kiosk was not yet subscribed.
            _ = ReloadSettingsAsync();
        }

        if (state == BoothState.Printing)
        {
            // The automatic print the state machine performs still costs the
            // guest one of their per-session prints.
            IsPrinting = true;
            PrintsRemaining = Math.Max(0, PrintsRemaining - 1);
            Admin.PrintsThisSession++;
            Admin.PrintsThisEvent++;
        }
        else
        {
            IsPrinting = false;
        }

        if (state == BoothState.Reviewing)
        {
            _ = BuildTemplatePreviewAsync(_stateMachine.LastCapturedImagePaths);
        }

        if (state == BoothState.Payment)
        {
            PaymentInstructions = _stateMachine.PaymentInstructions;
            PaymentQrCode = _stateMachine.PaymentQrPng is byte[] qrPng ? LoadImageFromBytes(qrPng) : null;
        }

        if (CurrentScreenState == KioskScreen.Review)
        {
            if (!_shareTimer.IsEnabled)
            {
                ShareSecondsRemaining = ShareSecondsTotal;
                _shareTimer.Start();
            }
        }
        else
        {
            _shareTimer.Stop();
        }

        RaisePropertyChanged(nameof(CanPrint));
        RaisePropertyChanged(nameof(CanSendEmail));
        PrintCommand.RaiseCanExecuteChanged();
        SendEmailCommand.RaiseCanExecuteChanged();
    }

    /// <summary>
    /// BoothState (fourteen pipeline steps) -> KioskScreen (the guest-facing
    /// screen vocabulary).
    ///
    /// Setup shares the Idle screen because both are "no session running"; the
    /// screen itself swaps its prompt on <see cref="IsBoothLocked"/>.
    ///
    /// Consent/Reviewing/Printing collapse to Processing: from the guest's side
    /// each is a wait with a spinner and a status line, and Consent is
    /// mock-only/auto-accepting (see BUILD_PLAN.md), so there's nothing for a
    /// guest to actually do there yet. Reviewing lands here rather than on the
    /// Review screen because at that point the template hasn't been composited
    /// or printed yet; it is the "stitching your photo strip" beat, not the
    /// sharing beat.
    ///
    /// FilterPicker/FramePicker/Payment/Guestbook/Feedback/Survey each get their
    /// own screen: unlike Consent/Reviewing/Printing, a guest genuinely acts on
    /// these (pick a filter, pick a frame, scan a QR to pay, decide whether to
    /// record a message, rate the experience, answer a question) -- see
    /// FilterOptions/FrameOptions/PaymentInstructions/GuestbookSubScreen/
    /// SelectedFeedbackRating/SurveyAnswers.
    ///
    /// Complete is the sharing beat: the photo exists, the print (if any) has
    /// spooled, and the upload has usually landed, so the QR, email and reprint
    /// controls all have something to act on.
    /// </summary>
    /// <summary>Internal rather than private so Photobooth.UI.Tests can assert
    /// the full mapping table directly (see InternalsVisibleTo below) without
    /// standing up a whole BoothStateMachine just to exercise a pure switch.</summary>
    internal static KioskScreen MapScreen(BoothState state) => state switch
    {
        BoothState.Setup or BoothState.Idle => KioskScreen.Idle,
        BoothState.Countdown => KioskScreen.Countdown,
        BoothState.Capturing => KioskScreen.Capture,
        BoothState.Consent or BoothState.Reviewing or BoothState.Printing => KioskScreen.Processing,
        BoothState.FilterPicker => KioskScreen.FilterPicker,
        BoothState.FramePicker => KioskScreen.FramePicker,
        BoothState.PrePayment or BoothState.Payment => KioskScreen.Payment,
        BoothState.Guestbook => KioskScreen.Guestbook,
        BoothState.Feedback => KioskScreen.Feedback,
        BoothState.Survey => KioskScreen.Survey,
        BoothState.Complete => KioskScreen.Review,
        BoothState.Error => KioskScreen.Error,
        _ => KioskScreen.Idle,
    };

    private static string DescribeProcessing(BoothState state) => state switch
    {
        BoothState.Consent => "Just a moment...",
        BoothState.Reviewing => "Processing your photo strip...",
        BoothState.Printing => "Printing your photo strip...",
        _ => "Processing your photo strip...",
    };

    private void ResetForNextGuest()
    {
        _filterSelection?.CancelPending();
        _frameSelection?.CancelPending();
        _templateSelection?.CancelPending();
        _feedback?.CancelPending();
        _guestbookPrompt?.CancelPending();
        FilterRequestToken = null;
        FrameRequestToken = null;
        TemplateRequestToken = null;
        FeedbackRequestToken = null;
        GuestbookAskRequestToken = null;
        GuestbookStopRequestToken = null;
        _captureModeOverride.Mode = null;
        _selectedCaptureMode = CaptureMode.Photo;
        RaisePropertyChanged(nameof(SelectedCaptureMode));
        StopGifPreviewAnimation();
        TemplatePreview = null;
        PreviewUnavailableReason = null;
        QrCodeImage = null;
        ShareStatus = null;
        ShareEmail = string.Empty;
        SharePhone = string.Empty;
        ErrorMessage = null;
        LiveViewStream = null;
        FilterOptions.Clear();
        FrameOptions.Clear();
        TemplateOptions.Clear();
        PaymentInstructions = null;
        PaymentQrCode = null;
        GuestbookSubScreen = GuestbookSubScreen.Ask;
        SelectedFeedbackRating = 0;
        FeedbackComment = string.Empty;
        SurveyAnswers.Clear();
        PoseThumbnails.Clear();
        IsPoseThumbnailStripVisible = false;
        Admin.PrintsThisSession = 0;
        Admin.LastUploadUrl = null;
        PrintsRemaining = _settings.PrintOptions.PrintLimitPerSession;
    }

    private async Task ReloadSettingsAsync()
    {
        await _settingsReloadGate.WaitAsync();
        try
        {
            await ReloadSettingsCoreAsync();
        }
        finally
        {
            _settingsReloadGate.Release();
        }
    }

    private async Task ReloadSettingsCoreAsync()
    {
        BoothSettings settings;
        try
        {
            settings = await _services.Settings.GetSettingsAsync();
        }
        catch (Exception)
        {
            // Best-effort, same reasoning as MainWindow.ApplyThemeAsync: a failed
            // settings read leaves the last-applied theme in place rather than
            // taking the idle screen down.
            return;
        }

        // Skipped in mock/designer mode (see _locationId) -- ScreenTemplateElementRepository
        // has no Mock* counterpart, and CreateWithMockServices promises no
        // LocalDB dependency.
        List<ScreenTemplateElement>? overlayElements = null;
        if (_locationId is int locationId)
        {
            try
            {
                overlayElements = await _screenElements.GetAllByLocationAsync(locationId);
            }
            catch (Exception)
            {
                // Best-effort, same reasoning as the settings-read catch above --
                // a failed overlay-elements read just means the guest screens
                // keep whatever overlay was last rendered (or none).
            }
        }

        OnUi(() =>
        {
            _settings = settings;
            EventName = settings.Theme.EventName;
            BrandLogo = LoadImageFromPath(settings.Theme.LogoImagePath);
            CountdownValue = settings.CountdownSeconds;
            IsEmailEnabled = settings.Sharing.EmailEnabled;
            IsSmsEnabled = settings.Sharing.SmsEnabled;
            IsQrEnabled = settings.Sharing.QrEnabled;
            IsTwitterEnabled = settings.Sharing.TwitterEnabled;
            IsPrintButtonVisible = settings.PrintOptions.ShowPrintButton;
            IsSharingTextLabelsEnabled = settings.Screen.SharingTextLabelsEnabled;
            IsDoneButtonVisible = settings.Screen.ShowDoneButton;
            PrintsRemaining = settings.PrintOptions.PrintLimitPerSession;
            LiveViewTransform = BuildLiveViewTransform(settings.Screen);
            LiveViewStretch = settings.Screen.CropLiveView ? Stretch.UniformToFill : Stretch.Uniform;
            CountdownColorBrush = HexToBrush(settings.Screen.CountdownColorHex);
            WelcomeBackgroundBrush = HexToBrush(settings.Screen.WelcomeBackgroundColorHex);
            WelcomeBackgroundImage = LoadImageFromPath(settings.Screen.WelcomeBackgroundImagePath);
            WelcomeBackgroundVideo = LoadVideoUriFromPath(settings.Screen.StartScreenVideoPath);
            CaptureBackgroundBrush = HexToBrush(settings.Screen.CaptureBackgroundColorHex);
            CaptureBackgroundImage = LoadImageFromPath(settings.Screen.CaptureBackgroundImagePath);
            SharingBackgroundBrush = HexToBrush(settings.Screen.SharingBackgroundColorHex);
            SharingBackgroundImage = LoadImageFromPath(settings.Screen.SharingBackgroundImagePath);
            IsTouchStartEnabled = settings.Screen.SessionTriggerTouchScreen;
            UnlockButtonOpacity = Math.Clamp(settings.Screen.UnlockButtonOpacityPercent, 0, 100) / 100.0;
            ShareSecondsTotal = settings.Screen.FinalScreenTimeoutSeconds;
            IsAdminLocked = settings.IsLocked;
            IsCancelButtonVisible = settings.Screen.ShowCancelButton
                && (CurrentScreenState == KioskScreen.Countdown || CurrentScreenState == KioskScreen.Capture);
            IsRetakeVisible = settings.Screen.ShowRetakeButton && CurrentScreenState == KioskScreen.Review;

            IsWelcomeIconsGroupVisible = settings.Screen.BoothIconsEnabled;
            IsWelcomeIconLabelsVisible = settings.Screen.BoothIconLabelsEnabled;
            IsWelcomePhotoIconVisible = settings.Screen.WelcomePhotoIconEnabled;
            IsWelcomeGifIconVisible = settings.Screen.WelcomeGifIconEnabled;
            IsWelcomeBoomerangIconVisible = settings.Screen.WelcomeBoomerangIconEnabled;
            IsWelcomeVideoIconVisible = settings.Screen.WelcomeVideoIconEnabled;
            WelcomeIconsOrientation = settings.Screen.WelcomeIconsLayout == IconGroupLayout.ColumnLayout
                ? Orientation.Vertical : Orientation.Horizontal;
            WelcomeIconsPositionXPercent = settings.Screen.WelcomeIconsPositionXPercent;
            WelcomeIconsPositionYPercent = settings.Screen.WelcomeIconsPositionYPercent;

            CaptureCancelButtonPositionXPercent = settings.Screen.CaptureCancelButtonPositionXPercent;
            CaptureCancelButtonPositionYPercent = settings.Screen.CaptureCancelButtonPositionYPercent;

            (PoseStripHorizontalAlignment, PoseStripVerticalAlignment, PoseStripOrientation) = settings.Screen.PoseStripPosition switch
            {
                "Top" => (HorizontalAlignment.Stretch, VerticalAlignment.Top, Orientation.Horizontal),
                "Left" => (HorizontalAlignment.Left, VerticalAlignment.Stretch, Orientation.Vertical),
                "Right" => (HorizontalAlignment.Right, VerticalAlignment.Stretch, Orientation.Vertical),
                _ => (HorizontalAlignment.Stretch, VerticalAlignment.Bottom, Orientation.Horizontal),
            };
            byte poseStripAlpha = (byte)(Math.Clamp(settings.Screen.PoseStripBackgroundOpacityPercent, 0, 100) * 255 / 100);
            var poseStripBackgroundBrush = new SolidColorBrush(Color.FromArgb(poseStripAlpha, 0, 0, 0));
            poseStripBackgroundBrush.Freeze();
            PoseStripBackgroundBrush = poseStripBackgroundBrush;
            PoseStripActiveBorderBrush = HexToBrush(settings.Screen.PoseStripActiveBorderColorHex);

            IsSharingIconsGroupVisible = settings.Screen.SharingIconsGroupEnabled;
            SharingIconsOrientation = settings.Screen.SharingIconsLayout == IconGroupLayout.ColumnLayout
                ? Orientation.Vertical : Orientation.Horizontal;
            SharingIconsPositionXPercent = settings.Screen.SharingIconsPositionXPercent;
            SharingIconsPositionYPercent = settings.Screen.SharingIconsPositionYPercent;

            if (overlayElements is not null)
            {
                _screenElementsByScreen = overlayElements.ToLookup(e => e.Screen);
            }
            ScreenOverlaysChanged?.Invoke();
            ApplyRemoteControlEnabled(settings.RemoteControlEnabled);
        });
    }

    private void OnSettingsChanged(object? sender, BoothSettingsChangedEventArgs args)
    {
        if (_locationId == args.LocationId && !_disposed)
        {
            _ = ReloadSettingsAsync();
        }
    }

    /// <summary>Starts or stops the loopback Remote Control HTTP listener to
    /// match the admin's Enable toggle (see AdminWindow's Remote Control
    /// section, RemoteControlServer). Idempotent -- safe to call on every
    /// ReloadSettingsAsync (every return to Idle) even when the toggle
    /// hasn't changed since the last call.</summary>
    private void ApplyRemoteControlEnabled(bool enabled)
    {
        if (enabled)
        {
            if (_remoteControl is null)
            {
                _remoteControl = new RemoteControlServer(
                    // Both callbacks fire on the listener's background thread
                    // (see RemoteControlServer's own doc comment) -- Dispatcher.Invoke,
                    // not OnUi, since the HTTP response needs a real result back
                    // from the UI thread, not a fire-and-forget dispatch.
                    getStatus: () => _dispatcher.Invoke(() => CurrentBoothState.ToString()),
                    tryStartNextGuest: () => _dispatcher.Invoke(() =>
                    {
                        if (!CanStartSession)
                        {
                            return false;
                        }
                        StartSession();
                        return true;
                    }));
                try
                {
                    _remoteControl.Start();
                }
                catch (Exception ex)
                {
                    // Best-effort, same reasoning as every other settings-driven
                    // side effect in this method -- a booth that can't bind the
                    // loopback port (e.g. another instance already running)
                    // still runs a normal guest session; it just isn't
                    // remotely controllable this run.
                    Log.Warning(ex, "Couldn't start the Remote Control listener");
                    _remoteControl = null;
                }
            }
        }
        else
        {
            _remoteControl?.Dispose();
            _remoteControl = null;
        }
    }

    /// <summary>Mirror then rotate, matching MainWindow.ApplyLiveViewTransform.
    /// Only 0/90/180/270 are meaningful; the schema has no CHECK constraint, so
    /// anything else falls back to unrotated rather than skewing the preview.</summary>
    private static Transform BuildLiveViewTransform(ScreenSettings screen)
    {
        double rotation = screen.LiveViewRotation is 90 or 180 or 270 ? screen.LiveViewRotation : 0;
        var group = new TransformGroup();
        group.Children.Add(new ScaleTransform(screen.MirrorLiveView ? -1 : 1, 1));
        group.Children.Add(new RotateTransform(rotation));
        group.Freeze(); // shared with the render thread via binding
        return group;
    }

    private static Brush HexToBrush(string hex)
    {
        try
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            return brush;
        }
        catch (Exception)
        {
            return Brushes.White;
        }
    }

    /// <summary>Field-initializer default for PoseStripBackgroundBrush, run
    /// during construction -- which BuildKioskViewModel does via Task.Run off
    /// the UI thread (see the _dispatcher comment in the constructor). Must be
    /// frozen like every other default Brush/ImageSource in this file (see
    /// HexToBrush/DecodeFrozen), or KioskWindow's binding to it throws "Must
    /// create DependencySource on same Thread as the DependencyObject" the
    /// moment the window shows, before ReloadSettingsAsync replaces it.</summary>
    private static Brush CreateFrozenPoseStripDefaultBrush()
    {
        var brush = new SolidColorBrush(Color.FromArgb(115, 0, 0, 0));
        brush.Freeze();
        return brush;
    }

    private void UpdateLiveView(BoothState state)
    {
        bool wantFeed = state == BoothState.Countdown && _settings.Screen.ShowLiveView;

        if (wantFeed)
        {
            if (!_liveViewTimer.IsEnabled)
            {
                _liveViewTimer.Start();
            }
            return;
        }

        if (_liveViewTimer.IsEnabled)
        {
            _liveViewTimer.Stop();
            // Release the camera's live view mode before the still capture
            // fires -- on a tethered body the two can't both own the sensor.
            _ = _liveView.StopAsync();
        }
    }

    /// <summary>A single shutter-click flash for Photo mode's one still. Skipped for
    /// GIF/Boomerang/Video: those go through OnFrameCaptured instead, which shows the guest
    /// each shot as it lands rather than flashing the screen white repeatedly -- a burst
    /// capture should read as continuous motion (as close to a real boomerang/GIF booth's
    /// "recording" feel as a tethered body's PTP-only capture loop can get), not a series of
    /// camera-flash clicks.</summary>
    private void UpdateFlash(BoothState state)
    {
        if (state != BoothState.Capturing || SelectedCaptureMode != CaptureMode.Photo || !_settings.Screen.FlashScreenWhite)
        {
            return;
        }

        IsFlashing = true;
        _flashTimer.Stop();
        _flashTimer.Start();
    }

    /// <summary>Fires right before each pose's own Countdown starts (see
    /// BoothStateMachine.PoseChanged). Rebuilds PoseThumbnails to `total` empty
    /// slots the first time this fires in a session (pose == 1) -- there's no
    /// separate "session started" hook, and this is the earliest point the
    /// real pose count for the guest's chosen template is known -- then marks
    /// the slot for the pose about to be shot as the active one.</summary>
    private void OnPoseChanged(int pose, int total)
    {
        ShowPoseProgress = total > 1;
        PoseProgressText = $"Pose {pose} of {total}";
        IsPoseThumbnailStripVisible = _settings.Screen.PhotoThumbnailsEnabled && total > 1;

        if (pose == 1)
        {
            PoseThumbnails.Clear();
            for (int number = 1; number <= total; number++)
            {
                PoseThumbnails.Add(new PoseThumbnailSlot(number, _settings.Screen.PoseStripShowPlaceholderNumbers));
            }
        }

        foreach (PoseThumbnailSlot slot in PoseThumbnails)
        {
            slot.IsActive = slot.Number == pose;
        }
    }

    /// <summary>Fills that pose's own thumbnail slot the instant its shot is
    /// ready (see BoothStateMachine.PosePhotoCaptured) -- the slot stops
    /// reading as "active" once its own image lands.</summary>
    private void OnPosePhotoCaptured(int pose, string path)
    {
        PoseThumbnailSlot? slot = PoseThumbnails.FirstOrDefault(s => s.Number == pose);
        if (slot is not null)
        {
            slot.ImageSource = LoadImageFromPath(path);
            slot.IsActive = false;
        }
    }

    /// <summary>GIF/Boomerang only: shows each shot on the Capture screen (which already
    /// renders LiveViewStream, per KioskWindow.xaml) the instant it lands, instead of a flash
    /// -- live view itself can't run mid-loop on a tethered body (see UpdateLiveView), so
    /// this is the closest available substitute for "the guest sees themselves moving" during
    /// the ~FrameCount * FrameDelayMs capture window.</summary>
    private void OnFrameCaptured(int frame, int total, string path)
    {
        ShowPoseProgress = total > 1;
        PoseProgressText = $"{frame} of {total}";
        LiveViewStream = LoadImageFromPath(path) ?? LiveViewStream;
    }

    private async Task PollLiveViewFrameAsync()
    {
        // A pipe round trip can outlast the timer interval; skip this tick
        // rather than letting requests queue behind each other.
        if (_liveViewFetchInProgress)
        {
            return;
        }

        _liveViewFetchInProgress = true;
        try
        {
            byte[]? frame = await _liveView.GetFrameAsync();
            if (frame is not null)
            {
                // Same threshold this booth's capture step will apply to the
                // still, run live so the preview reads as "green screen" while
                // the guest is still posing, not just on the reviewed photo
                // afterward. Guarded by _liveViewFetchInProgress like the pipe
                // fetch above -- a slow composite skips a tick rather than
                // piling frames up behind each other.
                if (_settings.GreenScreen is { Enabled: true, BackgroundImagePath: not null } greenScreen)
                {
                    frame = await _services.GreenScreen.ApplyToLiveFrameAsync(frame, greenScreen.BackgroundImagePath);
                }
                LiveViewStream = LoadImageFromBytes(frame);
            }
            // null frame: keep the last one on screen. A dropped frame is normal
            // (camera warming up, bridge busy) and shouldn't blank the preview.
        }
        catch (Exception)
        {
            // Best-effort preview, same as MainWindow's poller.
        }
        finally
        {
            _liveViewFetchInProgress = false;
        }
    }

    private void ApplyUploadedPhoto(Uri url)
    {
        Admin.LastUploadUrl = url.ToString();
        if (IsQrEnabled)
        {
            QrCodeImage = LoadImageFromBytes(QrCodeGenerator.GeneratePng(url.ToString()));
        }
        RaisePropertyChanged(nameof(CanSendEmail));
        SendEmailCommand.RaiseCanExecuteChanged();
    }

    /// <summary>
    /// Renders the print template the guest is about to receive, through
    /// PrintCompositor -- the same geometry SpoolerPrinterService draws with, so
    /// the preview can't drift from the print. Composition is GDI+ work on a
    /// multi-megapixel capture, so it runs off the UI thread and the resulting
    /// bitmap is frozen before it crosses back.
    /// </summary>
    private async Task BuildTemplatePreviewAsync(IReadOnlyList<string> capturePaths)
    {
        StopGifPreviewAnimation();
        TemplatePreview = null;
        PreviewUnavailableReason = null;

        if (capturePaths.Count == 0 || !File.Exists(capturePaths[0]))
        {
            PreviewUnavailableReason = "Your photo is on its way.";
            return;
        }

        string extension = Path.GetExtension(capturePaths[0]).ToLowerInvariant();
        if (extension is not (".jpg" or ".jpeg" or ".png" or ".bmp" or ".gif"))
        {
            // Video mode: nothing to composite onto paper (which is also why
            // BoothStateMachine skips Printing for it entirely).
            PreviewUnavailableReason = "Your video is ready to share.";
            return;
        }

        if (extension == ".gif")
        {
            // GIF/Boomerang only, neither of which is printable (isNonPrintableCapture in
            // BoothStateMachine), so there's no template to composite onto here anyway --
            // and PrintCompositor.RenderPreview couldn't show the animation even if there
            // were: it draws through GDI+'s Graphics.DrawImage, which only ever paints a
            // multi-frame GIF's *current* (first) frame, silently discarding every other
            // frame -- including the whole reversed half of a Boomerang. Play the real file
            // back frame by frame instead.
            StartGifPreviewAnimation(capturePaths[0]);
            return;
        }

        // Same "whatever this session actually used" reasoning as PrintAsync's
        // reprintTemplate above -- a guest who picked a different saved layout
        // during FramePicker should see that layout previewed here, not the
        // location's default.
        PrintTemplate template = _stateMachine.LastSelectedTemplate ?? _settings.PrintTemplate;
        try
        {
            ImageSource preview = await Task.Run(() =>
            {
                using System.Drawing.Bitmap composed = PrintCompositor.RenderPreview(capturePaths, template, previewWidthPx: 720);
                using var buffer = new MemoryStream();
                composed.Save(buffer, System.Drawing.Imaging.ImageFormat.Png);
                buffer.Position = 0;
                return DecodeFrozen(buffer);
            });

            OnUi(() => TemplatePreview = preview);
        }
        catch (Exception)
        {
            // A preview is a nicety; if compositing fails the guest still gets
            // their print and their QR code, so fall back to the plain capture
            // rather than surfacing an error over a successful session.
            OnUi(() =>
            {
                TemplatePreview = LoadImageFromPath(capturePaths[^1]);
                if (TemplatePreview is null)
                {
                    PreviewUnavailableReason = "Your photo is ready to share.";
                }
            });
        }
    }

    /// <summary>Decodes every frame of the composed GIF (via WPF's built-in GifBitmapDecoder --
    /// no new dependency, same "no third-party imaging library" preference GdiGifComposerService
    /// already established) and cycles TemplatePreview through them on a timer. Runs the decode
    /// off the UI thread since it reads the whole file; GdiGifComposerService always encodes
    /// every frame with the same delay, so a single timer interval is accurate rather than
    /// needing to read each frame's own Graphic Control Extension.</summary>
    private void StartGifPreviewAnimation(string path)
    {
        _ = Task.Run(() =>
        {
            List<ImageSource>? frames;
            try
            {
                using var stream = File.OpenRead(Path.GetFullPath(path));
                var decoder = new GifBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                frames = decoder.Frames.Select(frame => (ImageSource)frame).ToList();
                foreach (ImageSource frame in frames)
                {
                    frame.Freeze();
                }
            }
            catch (Exception)
            {
                frames = null;
            }

            OnUi(() =>
            {
                if (frames is not { Count: > 0 })
                {
                    TemplatePreview = LoadImageFromPath(path);
                    if (TemplatePreview is null)
                    {
                        PreviewUnavailableReason = "Your photo is ready to share.";
                    }
                    return;
                }

                _gifPreviewFrames = frames;
                _gifPreviewFrameIndex = 0;
                TemplatePreview = frames[0];

                if (frames.Count > 1)
                {
                    // Matches BoothStateMachine's own playbackFrameDelayMs computation
                    // (TargetLoopDurationMs / sequence length) via the actual decoded frame
                    // count, rather than re-deriving sequence length from FrameCount/mode here.
                    _gifPreviewTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(TargetLoopDurationMs / frames.Count, 50));
                    _gifPreviewTimer.Start();
                }
            });
        });
    }

    private void AdvanceGifPreviewFrame()
    {
        if (_gifPreviewFrames is not { Count: > 0 } frames)
        {
            _gifPreviewTimer.Stop();
            return;
        }

        _gifPreviewFrameIndex = (_gifPreviewFrameIndex + 1) % frames.Count;
        TemplatePreview = frames[_gifPreviewFrameIndex];
    }

    private void StopGifPreviewAnimation()
    {
        _gifPreviewTimer.Stop();
        _gifPreviewFrames = null;
    }

    // ============================================================== helpers ==

    private static Uri? LoadVideoUriFromPath(string? path) =>
        string.IsNullOrWhiteSpace(path) || !File.Exists(path) ? null : new Uri(Path.GetFullPath(path));

    private static ImageSource? LoadImageFromPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(Path.GetFullPath(path));
            return DecodeFrozen(stream);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static ImageSource? LoadImageFromBytes(byte[] bytes)
    {
        try
        {
            using var stream = new MemoryStream(bytes);
            return DecodeFrozen(stream);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Decodes fully on load and freezes, so the bitmap holds no handle
    /// on the source stream (the capture file gets overwritten by the next
    /// session) and can be handed across threads.</summary>
    private static ImageSource DecodeFrozen(Stream stream)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }

    /// <summary>Deliberately loose: a kiosk keyboard entry only needs to be
    /// plausible before it reaches IEmailDeliveryService, and a strict pattern
    /// rejecting a valid address in front of a queue of guests is the worse
    /// failure.</summary>
    private static bool LooksLikeEmail(string value)
    {
        string trimmed = value.Trim();
        int at = trimmed.IndexOf('@');
        return at > 0 && trimmed.IndexOf('.', at) > at + 1 && !trimmed.EndsWith('.');
    }

    private void OnUi(Action action)
    {
        if (_dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            _dispatcher.Invoke(action);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        BoothSettingsChanged.Changed -= OnSettingsChanged;

        _liveViewTimer.Stop();
        _flashTimer.Stop();
        _shareTimer.Stop();
        _gifPreviewTimer.Stop();
        _ = _liveView.StopAsync();
        _remoteControl?.Dispose();
        GC.SuppressFinalize(this);
    }
}
