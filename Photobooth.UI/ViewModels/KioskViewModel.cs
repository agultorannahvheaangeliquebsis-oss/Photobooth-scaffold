using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Photobooth.Core;
using Photobooth.Data;
using Photobooth.UI.Services;

namespace Photobooth.UI.ViewModels;

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

    private readonly BoothServices _services;
    private readonly BoothStateMachine _stateMachine;
    private readonly CaptureModeOverrideSettingsProvider _captureModeOverride;
    private readonly ILiveViewService _liveView;
    private readonly Dispatcher _dispatcher;

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
    private readonly UiFrameSelectionService? _frameSelection;
    private readonly UiFeedbackService? _feedback;
    private readonly UiGuestbookPromptService? _guestbookPrompt;
    private readonly SqlSurveyService? _survey;

    // Phase-6 screen-template overlays: null _locationId (mock/designer mode)
    // skips the SQL read entirely, since ScreenTemplateElementRepository has
    // no Mock* counterpart and CreateWithMockServices promises no LocalDB
    // dependency -- see ReloadSettingsAsync.
    private readonly int? _locationId;
    private readonly ScreenTemplateElementRepository _screenElements = new();
    private ILookup<ScreenTemplateScreen, ScreenTemplateElement> _screenElementsByScreen =
        Array.Empty<ScreenTemplateElement>().ToLookup(e => e.Screen);

    private readonly DispatcherTimer _liveViewTimer;
    private readonly DispatcherTimer _flashTimer;
    private readonly DispatcherTimer _shareTimer;

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
        int? locationId = null)
    {
        _dispatcher = Dispatcher.CurrentDispatcher;
        _liveView = liveView;
        _locationId = locationId;

        // Every settings read the state machine performs goes through the
        // override first, so a guest's mode tile reaches the session without a
        // settings write -- see CaptureModeOverrideSettingsProvider.
        _captureModeOverride = new CaptureModeOverrideSettingsProvider(services.Settings);
        _services = services with { Settings = _captureModeOverride };
        _stateMachine = new BoothStateMachine(_services, mode);

        Admin = new KioskAdminViewModel(_services.Settings, _services.UploadQueue, () => _stateMachine.RetryQueuedUploadsAsync());

        StartSessionCommand = new RelayCommand(StartSession, () => CanStartSession);
        SelectModeCommand = new RelayCommand(SelectMode, _ => CurrentScreenState == KioskScreen.Idle);
        LaunchEventCommand = new RelayCommand(() => _stateMachine.LaunchEvent(), () => CurrentBoothState == BoothState.Setup);
        PrintCommand = new AsyncRelayCommand(PrintAsync, () => CanPrint);
        SendEmailCommand = new AsyncRelayCommand(SendEmailAsync, () => CanSendEmail);
        SendSmsCommand = new AsyncRelayCommand(SendSmsAsync, () => CanSendSms);
        DoneCommand = new RelayCommand(FinishSharing);
        OpenAdminCommand = new RelayCommand(Admin.Open);
        SelectFrameCommand = new RelayCommand(SelectFrame);
        RecordGuestbookMessageCommand = new RelayCommand(() => _guestbookPrompt?.SubmitRecordDecision(true));
        SkipGuestbookMessageCommand = new RelayCommand(() => _guestbookPrompt?.SubmitRecordDecision(false));
        StopGuestbookRecordingCommand = new RelayCommand(() => _guestbookPrompt?.SubmitStop());
        SelectFeedbackStarCommand = new RelayCommand(SelectFeedbackStar);
        SubmitFeedbackCommand = new RelayCommand(SubmitFeedback);
        SkipFeedbackCommand = new RelayCommand(() => _feedback?.SubmitFeedback(new FeedbackResult(null, null)));
        SubmitSurveyCommand = new RelayCommand(SubmitSurvey);
        SkipSurveyCommand = new RelayCommand(() => _survey?.SubmitAnswers(Array.Empty<SurveyAnswer>()));

        PrintCommand.ExceptionHandler = ex => ShareStatus = $"Print failed: {ex.Message}";
        SendEmailCommand.ExceptionHandler = ex => ShareStatus = $"Email failed: {ex.Message}";
        SendSmsCommand.ExceptionHandler = ex => ShareStatus = $"SMS failed: {ex.Message}";

        _liveViewTimer = new DispatcherTimer { Interval = LiveViewInterval };
        _liveViewTimer.Tick += async (_, _) => await PollLiveViewFrameAsync();

        _flashTimer = new DispatcherTimer { Interval = FlashDuration };
        _flashTimer.Tick += (_, _) =>
        {
            _flashTimer.Stop();
            IsFlashing = false;
        };

        _shareTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _shareTimer.Tick += (_, _) => TickShareTimer();

        _stateMachine.StateChanged += state => OnUi(() => ApplyState(state));
        _stateMachine.CountdownTick += value => OnUi(() => CountdownValue = value);
        _stateMachine.ErrorOccurred += message => OnUi(() =>
        {
            ErrorMessage = message;
            Admin.ErrorsThisRun++;
        });
        _stateMachine.PhotoUploaded += url => OnUi(() => ApplyUploadedPhoto(url));
        _stateMachine.AttendantCueChanged += clip => OnUi(() => AttendantCueRequested?.Invoke(clip));

        _frameSelection = frameSelection;
        if (_frameSelection is not null)
        {
            _frameSelection.SelectionRequested += options => OnUi(() => ShowFrameOptions(options));
        }

        _feedback = feedback;
        if (_feedback is not null)
        {
            _feedback.FeedbackRequested += () => OnUi(ShowFeedbackPrompt);
        }

        _guestbookPrompt = guestbookPrompt;
        if (_guestbookPrompt is not null)
        {
            _guestbookPrompt.RecordDecisionRequested += () => OnUi(() => GuestbookSubScreen = GuestbookSubScreen.Ask);
            _guestbookPrompt.StopRequested += () => OnUi(() => GuestbookSubScreen = GuestbookSubScreen.Recording);
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
        },
        new MockLiveViewService());

    public KioskAdminViewModel Admin { get; }

    /// <summary>Raised after screen-template overlay elements (admin-authored
    /// text/image/rectangle overlays -- see <see cref="ScreenTemplateElementRepository"/>)
    /// are (re)loaded, at the same Idle-only "next guest, no restart" cadence
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

    public bool CanStartSession => CurrentBoothState == BoothState.Idle && !_sessionRunning;

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

    // ========================================================= frame picker ==

    /// <summary>Populated when <see cref="_frameSelection"/> raises SelectionRequested
    /// (i.e. right as BoothStateMachine enters FramePicker). Empty in mock mode or
    /// before that screen is reached -- MockFrameSelectionService resolves on its
    /// own without ever raising the event, matching MainWindow's ShowFrameOptions.</summary>
    public ObservableCollection<FrameOption> FrameOptions { get; } = new();

    public RelayCommand SelectFrameCommand { get; }

    /// <summary>Bound to each frame thumbnail's CommandParameter, and to the
    /// "No frame" button with a null parameter.</summary>
    private void SelectFrame(object? parameter) => _frameSelection?.SubmitSelection(parameter as FrameOption);

    private void ShowFrameOptions(IReadOnlyList<FrameOption> options)
    {
        FrameOptions.Clear();
        foreach (FrameOption option in options)
        {
            FrameOptions.Add(option);
        }
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

    private void SubmitFeedback()
    {
        int? rating = SelectedFeedbackRating > 0 ? SelectedFeedbackRating : null;
        string? comment = string.IsNullOrWhiteSpace(FeedbackComment) ? null : FeedbackComment.Trim();
        _feedback?.SubmitFeedback(new FeedbackResult(rating, comment));
    }

    private void ShowFeedbackPrompt()
    {
        SelectedFeedbackRating = 0;
        FeedbackComment = string.Empty;
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

    public bool CanPrint => PrintsRemaining > 0 && !IsPrinting && _stateMachine.LastCapturedImagePath is not null;

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

    // ============================================================ commands ==

    public RelayCommand StartSessionCommand { get; }
    public RelayCommand SelectModeCommand { get; }
    public RelayCommand LaunchEventCommand { get; }
    public AsyncRelayCommand PrintCommand { get; }
    public AsyncRelayCommand SendEmailCommand { get; }
    public AsyncRelayCommand SendSmsCommand { get; }
    public RelayCommand DoneCommand { get; }
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

    private void StartSession()
    {
        if (!CanStartSession)
        {
            return;
        }

        _sessionRunning = true;
        RaisePropertyChanged(nameof(CanStartSession));
        StartSessionCommand.RaiseCanExecuteChanged();
        Admin.SessionsThisRun++;

        _ = RunSessionAsync();
    }

    private async Task RunSessionAsync()
    {
        try
        {
            await _stateMachine.RunSessionAsync();
        }
        finally
        {
            OnUi(() =>
            {
                _sessionRunning = false;
                RaisePropertyChanged(nameof(CanStartSession));
                StartSessionCommand.RaiseCanExecuteChanged();
            });
        }
    }

    /// <summary>Guest-initiated reprint from the sharing screen. Separate from the
    /// state machine's automatic print (PrintOptions.PrintAutomatically), which
    /// has already run by the time this screen is reachable -- hence the
    /// per-session limit being decremented in both places.</summary>
    private async Task PrintAsync()
    {
        if (_stateMachine.LastCapturedImagePath is not string path)
        {
            return;
        }

        IsPrinting = true;
        ShareStatus = "Sending to printer...";
        try
        {
            await _services.Printer.PrintAsync(path, _settings.PrintTemplate);
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
        await _services.Email.SendPhotoLinkAsync(address, url);
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
        await _services.Sms.SendPhotoLinkAsync(phone, url);
        ShareStatus = $"Sent to {phone}.";
        SharePhone = string.Empty;
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

        if (state == BoothState.Idle)
        {
            ResetForNextGuest();
            // Re-read settings/theme at Idle only, so an admin's save reaches
            // the next guest without an app restart and without repainting
            // mid-session. Same cadence MainWindow.ApplyThemeAsync uses.
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
            _ = BuildTemplatePreviewAsync(_stateMachine.LastCapturedImagePath);
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
    /// FramePicker/Payment/Guestbook/Feedback/Survey each get their own screen:
    /// unlike Consent/Reviewing/Printing, a guest genuinely acts on these (pick
    /// a frame, scan a QR to pay, decide whether to record a message, rate the
    /// experience, answer a question) -- see FrameOptions/PaymentInstructions/
    /// GuestbookSubScreen/SelectedFeedbackRating/SurveyAnswers.
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
        BoothState.FramePicker => KioskScreen.FramePicker,
        BoothState.Payment => KioskScreen.Payment,
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
        TemplatePreview = null;
        PreviewUnavailableReason = null;
        QrCodeImage = null;
        ShareStatus = null;
        ShareEmail = string.Empty;
        SharePhone = string.Empty;
        ErrorMessage = null;
        LiveViewStream = null;
        FrameOptions.Clear();
        PaymentInstructions = null;
        PaymentQrCode = null;
        GuestbookSubScreen = GuestbookSubScreen.Ask;
        SelectedFeedbackRating = 0;
        FeedbackComment = string.Empty;
        SurveyAnswers.Clear();
        Admin.PrintsThisSession = 0;
        Admin.LastUploadUrl = null;
        PrintsRemaining = _settings.PrintOptions.PrintLimitPerSession;
    }

    private async Task ReloadSettingsAsync()
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
            PrintsRemaining = settings.PrintOptions.PrintLimitPerSession;
            LiveViewTransform = BuildLiveViewTransform(settings.Screen);

            if (overlayElements is not null)
            {
                _screenElementsByScreen = overlayElements.ToLookup(e => e.Screen);
            }
            ScreenOverlaysChanged?.Invoke();
        });
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

    private void UpdateFlash(BoothState state)
    {
        if (state != BoothState.Capturing)
        {
            return;
        }

        IsFlashing = true;
        _flashTimer.Stop();
        _flashTimer.Start();
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
    private async Task BuildTemplatePreviewAsync(string? capturePath)
    {
        TemplatePreview = null;
        PreviewUnavailableReason = null;

        if (capturePath is null || !File.Exists(capturePath))
        {
            PreviewUnavailableReason = "Your photo is on its way.";
            return;
        }

        string extension = Path.GetExtension(capturePath).ToLowerInvariant();
        if (extension is not (".jpg" or ".jpeg" or ".png" or ".bmp" or ".gif"))
        {
            // Video mode: nothing to composite onto paper (which is also why
            // BoothStateMachine skips Printing for it entirely).
            PreviewUnavailableReason = "Your video is ready to share.";
            return;
        }

        PrintTemplate template = _settings.PrintTemplate;
        try
        {
            ImageSource preview = await Task.Run(() =>
            {
                using System.Drawing.Bitmap composed = PrintCompositor.RenderPreview(capturePath, template, previewWidthPx: 720);
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
                TemplatePreview = LoadImageFromPath(capturePath);
                if (TemplatePreview is null)
                {
                    PreviewUnavailableReason = "Your photo is ready to share.";
                }
            });
        }
    }

    // ============================================================== helpers ==

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

        _liveViewTimer.Stop();
        _flashTimer.Stop();
        _shareTimer.Stop();
        _ = _liveView.StopAsync();
        GC.SuppressFinalize(this);
    }
}
