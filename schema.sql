-- Photobooth business management system
-- Schema covers both event-mode (attended, flat fee) and vendo-mode
-- (unattended, pay-per-print) sessions on the same underlying tables.

CREATE TABLE Location (
    LocationId      INT IDENTITY(1,1) PRIMARY KEY,
    Name            NVARCHAR(100)   NOT NULL,
    Type            NVARCHAR(20)    NOT NULL CHECK (Type IN ('event', 'vendo')),
    Address         NVARCHAR(255)   NULL,
    CountdownSeconds   INT          NOT NULL DEFAULT 3,   -- admin-editable, see AdminWindow's Settings section
    GlamFilterEnabled  BIT          NOT NULL DEFAULT 0,   -- admin-editable, see AdminWindow's Settings section
    AdminPin           NVARCHAR(20) NOT NULL DEFAULT '1234',  -- gates MainWindow's Setup/launch screen, admin-editable, see AdminWindow's Settings section
    PrintLayout        NVARCHAR(20) NOT NULL DEFAULT 'Single' CHECK (PrintLayout IN ('Single', 'Strip')),
    PrintWidthInches   DECIMAL(5,2) NOT NULL DEFAULT 4,    -- e.g. 4 for a 4x6, 2 for a 2x6 strip
    PrintHeightInches  DECIMAL(5,2) NOT NULL DEFAULT 6,
    PrintStripCopies   INT          NOT NULL DEFAULT 1,    -- how many times the photo repeats down a Strip layout
    AccentColorHex     NVARCHAR(9)  NOT NULL DEFAULT '#365C58',  -- admin-editable, see AdminWindow's Theme section
    CanvasColorHex     NVARCHAR(9)  NOT NULL DEFAULT '#F4F3F0',
    InkColorHex        NVARCHAR(9)  NOT NULL DEFAULT '#202124',
    LogoImagePath      NVARCHAR(500) NULL,
    EventName          NVARCHAR(100) NOT NULL DEFAULT 'Focus & Snap',

    -- dslrBooth feature-parity settings (see BUILD_PLAN.md's "dslrBooth
    -- feature-parity plan" section, Phase 1). Booth-wide, admin-editable,
    -- read fresh every session same as CountdownSeconds/GlamFilterEnabled
    -- above -- one booth machine has one Location, so these live here
    -- rather than a new settings table.
    CaptureMode         NVARCHAR(20) NOT NULL DEFAULT 'Photo' CHECK (CaptureMode IN ('Photo', 'GIF', 'Boomerang', 'Video')),
    AlsoCreateGif       BIT          NOT NULL DEFAULT 0,   -- PhotoCaptureSettings.AlsoCreateGif
    GifFrameCount       INT          NOT NULL DEFAULT 4,   -- GifCaptureSettings.FrameCount ("Photos to capture"), see BoothStateMachine's GIF branch
    GifFrameDelayMs     INT          NOT NULL DEFAULT 500, -- GifCaptureSettings.FrameDelayMs (composed GIF's own playback speed)
    VideoDurationSeconds INT         NOT NULL DEFAULT 10,  -- VideoCaptureSettings.ClipDurationSeconds, see IBoothVideoService

    -- Capture Settings split into four independently-configurable panels
    -- (see dslrBooth's own Capture Settings screen and
    -- Photobooth.Core/IBoothSettingsProvider.cs's PhotoCaptureSettings/
    -- GifCaptureSettings/BoomerangCaptureSettings/VideoCaptureSettings) --
    -- each with its own Enabled flag, gating that mode's Welcome-screen tile
    -- alongside ScreenSettings.WelcomeXIconEnabled (see
    -- KioskViewModel.ApplySettings). The columns above (CaptureMode/
    -- AlsoCreateGif/GifFrameCount/GifFrameDelayMs/VideoDurationSeconds)
    -- predate this split and are reused as-is rather than duplicated.
    PhotoEnabled                    BIT          NOT NULL DEFAULT 1,
    PhotoBeforePhoto1Seconds        INT          NOT NULL DEFAULT 10,
    PhotoBeforeOtherPhotosSeconds   INT          NOT NULL DEFAULT 10,
    PhotoReviewSeconds              INT          NOT NULL DEFAULT 3,

    GifEnabled                      BIT          NOT NULL DEFAULT 1,
    GifSize                         NVARCHAR(40) NOT NULL DEFAULT 'Regular (720x480)',
    GifBeforePhoto1Seconds          INT          NOT NULL DEFAULT 5,
    GifBeforeOtherPhotosSeconds     INT          NOT NULL DEFAULT 5,
    GifPhotoReviewSeconds           INT          NOT NULL DEFAULT 3,
    GifReverseGif                   BIT          NOT NULL DEFAULT 0,
    GifImageOverlayPath             NVARCHAR(500) NULL,

    BoomerangEnabled                 BIT          NOT NULL DEFAULT 1,
    BoomerangSize                    NVARCHAR(40) NOT NULL DEFAULT 'Regular (720x480)',
    BoomerangCountdownSeconds        INT          NOT NULL DEFAULT 5,
    BoomerangFrameDelayMs            INT          NOT NULL DEFAULT 50,
    BoomerangRecordingDurationSeconds INT         NOT NULL DEFAULT 1,
    BoomerangImageOverlayPath        NVARCHAR(500) NULL,

    VideoEnabled                          BIT          NOT NULL DEFAULT 1,
    VideoOrientationDegrees               INT          NOT NULL DEFAULT 0,
    VideoSize                             NVARCHAR(40) NOT NULL DEFAULT '1280x720',
    VideoOutputQualityPercent             INT          NOT NULL DEFAULT 50,
    VideoType                             NVARCHAR(20) NOT NULL DEFAULT 'Video' CHECK (VideoType IN ('Video', '360SlowMotion')),
    VideoNumberOfClips                    INT          NOT NULL DEFAULT 1,
    VideoCountdownBeforeClip1Seconds      INT          NOT NULL DEFAULT 5,
    VideoCountdownBeforeOtherClipsSeconds INT          NOT NULL DEFAULT 5,
    VideoRecordOnMotionEnabled            BIT          NOT NULL DEFAULT 0,
    VideoSoundtrackMp3Path                NVARCHAR(500) NULL,
    VideoImageOverlayPath                 NVARCHAR(500) NULL,
    VideoBeforeRecordingClipPath          NVARCHAR(500) NULL,
    VideoAfterRecordingClipPath           NVARCHAR(500) NULL,

    BoothIconsEnabled   BIT          NOT NULL DEFAULT 0,
    ShowLiveView        BIT          NOT NULL DEFAULT 1,
    MirrorLiveView      BIT          NOT NULL DEFAULT 1,
    SaveMirroredPhotos  BIT          NOT NULL DEFAULT 1,   -- when MirrorLiveView is on, also flip the SAVED photo to match -- see GdiPhotoMirrorService
    LiveViewRotation    INT          NOT NULL DEFAULT 0,
    EnableWebcams       BIT          NOT NULL DEFAULT 1,   -- if 0, only Canon/Nikon are used -- see AdminWindow's Camera Settings section
    WebcamResolutionQuality INT      NOT NULL DEFAULT 70,  -- 0 (fastest framerate) - 100 (highest quality)
    AudioInputDeviceName NVARCHAR(200) NULL,                -- NULL = system default device
    CameraDeviceName    NVARCHAR(200) NULL,                -- NULL = auto-detect (DSLR, then webcam fallback) -- see AdminWindow's Camera Settings device picker
    BeautyFilterEnabled BIT          NOT NULL DEFAULT 0,
    BeautyFilterAlsoDuringCountdown BIT NOT NULL DEFAULT 0,
    FiltersMode         NVARCHAR(20) NOT NULL DEFAULT 'Ask' CHECK (FiltersMode IN ('Ask', 'Auto')),
    FiltersEnabled      BIT          NOT NULL DEFAULT 0,
    -- Comma-separated PhotoFilterPreset names -- must match PhotoFilterPresets.All (Photobooth.Core).
    EnabledFilterPresetIds NVARCHAR(300) NOT NULL DEFAULT 'Original,BlackAndWhiteGlam,BlackAndWhite,Filter1977,Brannan,Gotham,Hefe,LordKelvin,Nashville',
    PostProcessingEnabled BIT        NOT NULL DEFAULT 0,
    PostProcessingApplicationPath NVARCHAR(500) NULL,
    StickersEnabled     BIT          NOT NULL DEFAULT 1,
    WatermarkImagePath  NVARCHAR(500) NULL,
    WatermarkEnabled    BIT          NOT NULL DEFAULT 0,
    GreenScreenEnabled  BIT          NOT NULL DEFAULT 0,
    GreenScreenBackgroundPath NVARCHAR(500) NULL,
    SurveyEnabled       BIT          NOT NULL DEFAULT 0,
    DisclaimerHeader    NVARCHAR(200) NOT NULL DEFAULT 'Do you agree with the terms?',
    DisclaimerText      NVARCHAR(MAX) NOT NULL DEFAULT '',
    PrintAutomatically  BIT          NOT NULL DEFAULT 1,
    ShowPrintButton     BIT          NOT NULL DEFAULT 0,
    PrintLimitPerEvent  INT          NOT NULL DEFAULT 5000,
    PrintLimitPerSession INT         NOT NULL DEFAULT 3,
    PrintSharpening     NVARCHAR(10) NOT NULL DEFAULT 'Medium' CHECK (PrintSharpening IN ('Low', 'Medium', 'High')),
    EmailEnabled        BIT          NOT NULL DEFAULT 1,
    SmsEnabled          BIT          NOT NULL DEFAULT 0,
    QrEnabled           BIT          NOT NULL DEFAULT 1,
    PaymentTiming       NVARCHAR(20) NOT NULL DEFAULT 'SharingScreen' CHECK (PaymentTiming IN ('SharingScreen', 'StartScreen')),

    -- Real SMTP/Twilio delivery config (see Photobooth.Core's
    -- SmtpEmailDeliveryService/TwilioSmsDeliveryService). The two
    -- *Protected columns hold DPAPI ciphertext (SecretProtector), never a
    -- plaintext password/token.
    EmailFromAddress            NVARCHAR(320) NOT NULL DEFAULT '',
    EmailSubject                NVARCHAR(200) NOT NULL DEFAULT 'Here is your photo',
    EmailSmtpHost               NVARCHAR(200) NOT NULL DEFAULT '',
    EmailSmtpPort               INT           NOT NULL DEFAULT 587,
    EmailSmtpUsername           NVARCHAR(200) NOT NULL DEFAULT '',
    EmailUseSsl                 BIT           NOT NULL DEFAULT 1,
    EmailSmtpPasswordProtected  NVARCHAR(MAX) NOT NULL DEFAULT '',
    TwilioAccountSid            NVARCHAR(100) NOT NULL DEFAULT '',
    TwilioFromNumber            NVARCHAR(20)  NOT NULL DEFAULT '',
    TwilioAuthTokenProtected    NVARCHAR(MAX) NOT NULL DEFAULT '',

    -- Virtual Attendant (see BUILD_PLAN.md's Phase 6 scope text,
    -- IVirtualAttendantService). Randomize is one column per cue-worthy
    -- stage, not a generic key-value table -- BoothState's cue-worthy
    -- stages are a small, fixed set.
    AttendantEnabled            BIT NOT NULL DEFAULT 0,
    AttendantStyle               NVARCHAR(20) NOT NULL DEFAULT 'Friendly',
    AttendantRandomizeConsent    BIT NOT NULL DEFAULT 0,
    AttendantRandomizeCountdown  BIT NOT NULL DEFAULT 0,
    AttendantRandomizeCapturing  BIT NOT NULL DEFAULT 0,
    AttendantRandomizeReviewing  BIT NOT NULL DEFAULT 0,
    AttendantRandomizePrinting   BIT NOT NULL DEFAULT 0,
    AttendantRandomizeComplete   BIT NOT NULL DEFAULT 0,

    -- Admin Dashboard sections added after the dslrBooth-parity pass (see
    -- BUILD_PLAN.md's "Admin Dashboard stub sections" writeup) -- Show Lock
    -- Screen, Remote Control, Slideshow. Read fresh into BoothSettings same
    -- as every other admin-editable column above.
    IsLocked                     BIT NOT NULL DEFAULT 0,
    RemoteControlEnabled         BIT NOT NULL DEFAULT 0,
    SlideshowEnabled             BIT NOT NULL DEFAULT 1,
    SlideshowIntervalSeconds     INT NOT NULL DEFAULT 4,
    SlideshowTransition          NVARCHAR(20) NOT NULL DEFAULT 'Fade',
    SlideshowShowLogoOverlay     BIT NOT NULL DEFAULT 1,
    SlideshowShowQrOverlay       BIT NOT NULL DEFAULT 1,

    CreatedAt       DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME()
);

CREATE TABLE Printer (
    PrinterId       INT IDENTITY(1,1) PRIMARY KEY,
    LocationId      INT             NOT NULL REFERENCES Location(LocationId),
    Model           NVARCHAR(100)   NOT NULL,     -- e.g. 'Canon Selphy CP1500', 'DNP DS620A'
    SerialNumber    NVARCHAR(100)   NULL,
    Status          NVARCHAR(20)    NOT NULL DEFAULT 'active'
                        CHECK (Status IN ('active', 'offline', 'maintenance')),
    CreatedAt       DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME()
);

CREATE TABLE Booking (
    BookingId       INT IDENTITY(1,1) PRIMARY KEY,
    LocationId      INT             NOT NULL REFERENCES Location(LocationId),
    ClientName      NVARCHAR(150)   NOT NULL,
    EventDate       DATE            NOT NULL,
    PackageType     NVARCHAR(50)    NOT NULL,     -- 'Starter', 'Standard', 'Premium'
    Price           DECIMAL(10,2)   NOT NULL,
    Status          NVARCHAR(20)    NOT NULL DEFAULT 'confirmed'
                        CHECK (Status IN ('inquiry', 'confirmed', 'completed', 'cancelled')),
    CreatedAt       DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME()
);

CREATE TABLE Session (
    SessionId       INT IDENTITY(1,1) PRIMARY KEY,
    LocationId      INT             NOT NULL REFERENCES Location(LocationId),
    BookingId       INT             NULL REFERENCES Booking(BookingId),  -- NULL for vendo sessions
    Mode            NVARCHAR(20)    NOT NULL CHECK (Mode IN ('event', 'vendo')),
    StartedAt       DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME(),
    EndedAt         DATETIME2       NULL,
    Status          NVARCHAR(20)    NOT NULL DEFAULT 'in_progress'
                        CHECK (Status IN ('in_progress', 'completed', 'abandoned', 'error'))
);

-- Bracketed: PRINT is a reserved T-SQL keyword, and "ON Print(SessionId)"
-- below parses as the PRINT statement rather than a table reference
-- without the brackets.
CREATE TABLE [Print] (
    PrintId         INT IDENTITY(1,1) PRIMARY KEY,
    SessionId       INT             NOT NULL REFERENCES Session(SessionId),
    PrinterId       INT             NOT NULL REFERENCES Printer(PrinterId),
    PrintedAt       DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME(),
    TemplateUsed    NVARCHAR(100)   NULL,
    FilePath        NVARCHAR(500)   NULL          -- local path to the composited image
);

CREATE TABLE Payment (
    PaymentId       INT IDENTITY(1,1) PRIMARY KEY,
    SessionId       INT             NOT NULL REFERENCES Session(SessionId),
    Amount          DECIMAL(10,2)   NOT NULL,
    Method          NVARCHAR(30)    NOT NULL,     -- 'qr_gcash', 'qr_maya', 'card', 'free_event'
    Status          NVARCHAR(20)    NOT NULL DEFAULT 'pending'
                        CHECK (Status IN ('pending', 'paid', 'refunded', 'failed')),
    TransactionRef  NVARCHAR(100)   NULL,
    PaidAt          DATETIME2       NULL
);

CREATE TABLE Consent (
    ConsentId           INT IDENTITY(1,1) PRIMARY KEY,
    SessionId           INT             NOT NULL REFERENCES Session(SessionId),
    DisclaimerAccepted  BIT             NOT NULL,
    EmailOptIn          BIT             NOT NULL DEFAULT 0,
    Email               NVARCHAR(255)   NULL,
    RecordedAt          DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME()
);

CREATE TABLE InventoryLog (
    InventoryId     INT IDENTITY(1,1) PRIMARY KEY,
    PrinterId       INT             NOT NULL REFERENCES Printer(PrinterId),
    ItemType        NVARCHAR(20)    NOT NULL CHECK (ItemType IN ('paper', 'ink', 'ribbon')),
    QuantityRemaining INT           NOT NULL,
    LoggedAt        DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME()
);

-- Admin-managed frame overlays a guest can pick during a session (see
-- AdminWindow's Frame library section, IFrameLibraryService). IsActive lets
-- an admin retire a frame without losing its history; SortOrder controls
-- the order guests see them in the picker.
CREATE TABLE Frame (
    FrameId         INT IDENTITY(1,1) PRIMARY KEY,
    LocationId      INT             NOT NULL REFERENCES Location(LocationId),
    Name            NVARCHAR(100)   NOT NULL,
    ImagePath       NVARCHAR(500)   NOT NULL,
    SortOrder       INT             NOT NULL DEFAULT 0,
    IsActive        BIT             NOT NULL DEFAULT 1,
    CreatedAt       DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME()
);

-- Admin-uploaded custom .CUBE 3D LUT filters offered alongside the built-in
-- PhotoFilterPreset tiles (see FilterLibraryWindow's "Add Custom Filter"
-- tile, ICustomFilterLibraryService, GdiCubeLutFilterService). Same
-- IsActive/SortOrder shape as Frame -- CubeFilePath points at the .cube file
-- copied into Assets/CustomFilters, not the admin's original file location.
CREATE TABLE CustomFilter (
    CustomFilterId  INT IDENTITY(1,1) PRIMARY KEY,
    LocationId      INT             NOT NULL REFERENCES Location(LocationId),
    Name            NVARCHAR(100)   NOT NULL,
    CubeFilePath    NVARCHAR(500)   NOT NULL,
    SortOrder       INT             NOT NULL DEFAULT 0,
    IsActive        BIT             NOT NULL DEFAULT 1,
    CreatedAt       DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME()
);

-- Admin-uploaded digital props (hats, glasses, mustaches, etc.) a guest can
-- add to their own photo -- see dslrBooth's own Stickers screen,
-- AdminWindow's Stickers card, StickerLibraryWindow. Same IsActive/SortOrder
-- shape as Frame/CustomFilter -- ImagePath points at the transparent PNG
-- copied into Assets/Stickers, not the admin's original file location. Only
-- the admin-side library (add/remove, Effects & Stickers on/off toggle) is
-- wired up so far; nothing at guest-session time reads this table yet.
CREATE TABLE Sticker (
    StickerId       INT IDENTITY(1,1) PRIMARY KEY,
    LocationId      INT             NOT NULL REFERENCES Location(LocationId),
    Name            NVARCHAR(100)   NOT NULL,
    ImagePath       NVARCHAR(500)   NOT NULL,
    SortOrder       INT             NOT NULL DEFAULT 0,
    IsActive        BIT             NOT NULL DEFAULT 1,
    CreatedAt       DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME()
);

-- General guest feedback (rating/comment), shown right after Complete (see
-- BoothStateMachine's Feedback state, IFeedbackService). Both columns are
-- nullable and a row is only ever inserted when at least one is non-null --
-- a guest who skips entirely leaves no row, same "nothing worth recording"
-- reasoning a declined Consent doesn't stop its own row (Consent always
-- needs the DisclaimerAccepted outcome either way; Feedback has nothing
-- mandatory to record at all).
CREATE TABLE Feedback (
    FeedbackId      INT IDENTITY(1,1) PRIMARY KEY,
    SessionId       INT             NOT NULL REFERENCES Session(SessionId),
    Rating          INT             NULL CHECK (Rating BETWEEN 1 AND 5),
    Comment         NVARCHAR(1000)  NULL,
    RecordedAt      DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME()
);

-- Guest video messages recorded during the Guestbook state (see
-- BoothStateMachine, IVideoGuestbookService). Purely admin-reviewed --
-- unlike the photo, a guestbook message isn't uploaded, QR'd, or printed,
-- so there's no LastPhotoUrl-style column here, just where the file lives.
CREATE TABLE GuestbookVideo (
    GuestbookVideoId INT IDENTITY(1,1) PRIMARY KEY,
    SessionId        INT             NOT NULL REFERENCES Session(SessionId),
    FilePath         NVARCHAR(500)   NOT NULL,
    DurationSeconds  INT             NOT NULL,
    RecordedAt       DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME()
);

-- Admin-placed logo/text overlays drawn on top of the photo at print time
-- (see PrintTemplate.Elements, PrintCompositor, PrintTemplateEditorWindow).
-- Booth-wide and admin-managed like Frame, not guest-facing per-session
-- choices like FramePicker's frame art. Percent columns are cell-relative
-- fractions (0-1), not absolute pixels/inches, so the same rows still make
-- sense if the paper size later changes.
CREATE TABLE PrintTemplateElement (
    ElementId       INT IDENTITY(1,1) PRIMARY KEY,
    LocationId      INT             NOT NULL REFERENCES Location(LocationId),
    Kind            NVARCHAR(20)    NOT NULL CHECK (Kind IN ('Logo', 'Text', 'Image', 'Shape', 'QrCode', 'SessionData', 'PhotoSlot')),
    XPercent        DECIMAL(6,4)    NOT NULL,
    YPercent        DECIMAL(6,4)    NOT NULL,
    WidthPercent    DECIMAL(6,4)    NOT NULL,
    HeightPercent   DECIMAL(6,4)    NOT NULL,
    Text            NVARCHAR(200)   NULL,
    ImagePath       NVARCHAR(500)   NULL,
    FontFamily      NVARCHAR(100)   NULL,
    FontSizePercent DECIMAL(6,4)    NULL,
    Bold            BIT             NOT NULL DEFAULT 0,
    ColorHex        NVARCHAR(9)     NULL,
    ShapeType       NVARCHAR(20)    NULL,         -- 'Rectangle' or 'Ellipse', Shape only
    PhotoIndex      INT             NULL,         -- 0-based captured pose, PhotoSlot only
    SortOrder       INT             NOT NULL DEFAULT 0,
    CreatedAt       DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME()
);

-- Per-stage Virtual Attendant audio/video cue pool (see
-- IVirtualAttendantService, SqlVirtualAttendantService). A pool per stage,
-- not a single row, since AttendantRandomize* above needs multiple clips to
-- pick from.
CREATE TABLE VirtualAttendantClip (
    ClipId          INT IDENTITY(1,1) PRIMARY KEY,
    LocationId      INT             NOT NULL REFERENCES Location(LocationId),
    Stage           NVARCHAR(20)    NOT NULL CONSTRAINT CK_VirtualAttendantClip_Stage CHECK (Stage IN ('Setup', 'Idle', 'Consent', 'Countdown', 'Capturing', 'FilterPicker', 'Reviewing', 'FramePicker', 'Payment', 'Printing', 'Complete', 'Guestbook', 'Feedback', 'Survey', 'Error')),
    FilePath        NVARCHAR(500)   NOT NULL,
    SortOrder       INT             NOT NULL DEFAULT 0,
    CreatedAt       DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME()
);

-- Admin-authored survey question-builder (see ISurveyService, AdminWindow's
-- Survey section, BoothState.Survey). A pool of questions, guest answers
-- recorded per-question -- same "empty table = feature invisible" reasoning
-- Frame/FramePicker already established for SurveyQuestion being empty.
CREATE TABLE SurveyQuestion (
    SurveyQuestionId INT IDENTITY(1,1) PRIMARY KEY,
    LocationId       INT             NOT NULL REFERENCES Location(LocationId),
    Text             NVARCHAR(300)   NOT NULL,
    SortOrder        INT             NOT NULL DEFAULT 0,
    IsActive         BIT             NOT NULL DEFAULT 1,
    CreatedAt        DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME()
);

CREATE TABLE SurveyResponse (
    SurveyResponseId INT IDENTITY(1,1) PRIMARY KEY,
    SessionId        INT             NOT NULL REFERENCES Session(SessionId),
    SurveyQuestionId INT             NOT NULL REFERENCES SurveyQuestion(SurveyQuestionId),
    Answer           NVARCHAR(1000)  NOT NULL,
    RecordedAt       DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME()
);

-- One row per guest email/SMS share attempt (see AdminWindow's Sharing
-- Status section, KioskViewModel.SendEmailAsync/SendSmsAsync). PhotoUrl is
-- stored (not just looked up via SessionId) so a Failed row's Retry action
-- can re-send without needing the original guest session's in-memory state,
-- which no longer exists by the time an admin looks at this screen.
CREATE TABLE SharingLog (
    SharingLogId    INT IDENTITY(1,1) PRIMARY KEY,
    SessionId       INT             NOT NULL REFERENCES Session(SessionId),
    Method          NVARCHAR(10)    NOT NULL CHECK (Method IN ('Email', 'SMS')),
    Destination     NVARCHAR(320)   NOT NULL,
    PhotoUrl        NVARCHAR(500)   NOT NULL,
    Status          NVARCHAR(10)    NOT NULL CHECK (Status IN ('Sent', 'Failed')),
    ErrorMessage    NVARCHAR(500)   NULL,
    SentAt          DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME()
);

-- Admin-placed elements for the Welcome/Capture/Sharing guest-facing screens
-- (see ScreenTemplateEditorWindow, MainWindow's live overlay rendering).
-- Same percent-of-canvas model as PrintTemplateElement, one Screen value per
-- tab in the editor.
CREATE TABLE ScreenTemplateElement (
    ElementId       INT IDENTITY(1,1) PRIMARY KEY,
    LocationId      INT             NOT NULL REFERENCES Location(LocationId),
    Screen          NVARCHAR(20)    NOT NULL CHECK (Screen IN ('Welcome', 'Capture', 'Sharing')),
    Kind            NVARCHAR(20)    NOT NULL CHECK (Kind IN ('Text', 'Image', 'Shape')),
    XPercent        DECIMAL(6,4)    NOT NULL,
    YPercent        DECIMAL(6,4)    NOT NULL,
    WidthPercent    DECIMAL(6,4)    NOT NULL,
    HeightPercent   DECIMAL(6,4)    NOT NULL,
    Text            NVARCHAR(200)   NULL,
    ImagePath       NVARCHAR(500)   NULL,
    FontFamily      NVARCHAR(100)   NULL,
    FontSizePercent DECIMAL(6,4)    NULL,
    Bold            BIT             NOT NULL DEFAULT 0,
    ColorHex        NVARCHAR(9)     NULL,
    SortOrder       INT             NOT NULL DEFAULT 0,
    CreatedAt       DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME()
);

-- Helpful indexes for the dashboard queries you'll write later
CREATE INDEX IX_Session_Location_Mode ON Session(LocationId, Mode);
CREATE INDEX IX_Print_Session ON [Print](SessionId);
CREATE INDEX IX_Payment_Session ON Payment(SessionId);
CREATE INDEX IX_Consent_Session ON Consent(SessionId);
CREATE INDEX IX_InventoryLog_Printer_LoggedAt ON InventoryLog(PrinterId, LoggedAt DESC);
CREATE INDEX IX_Frame_Location_Active ON Frame(LocationId, IsActive, SortOrder);
CREATE INDEX IX_CustomFilter_Location_Active ON CustomFilter(LocationId, IsActive, SortOrder);
CREATE INDEX IX_Sticker_Location_Active ON Sticker(LocationId, IsActive, SortOrder);
CREATE INDEX IX_Feedback_Session ON Feedback(SessionId);
CREATE INDEX IX_GuestbookVideo_Session ON GuestbookVideo(SessionId);
CREATE INDEX IX_PrintTemplateElement_Location ON PrintTemplateElement(LocationId, SortOrder);
CREATE INDEX IX_VirtualAttendantClip_Location_Stage ON VirtualAttendantClip(LocationId, Stage, SortOrder);
CREATE INDEX IX_SurveyQuestion_Location_Active ON SurveyQuestion(LocationId, IsActive, SortOrder);
CREATE INDEX IX_SurveyResponse_Session ON SurveyResponse(SessionId);
CREATE INDEX IX_ScreenTemplateElement_Location_Screen ON ScreenTemplateElement(LocationId, Screen, SortOrder);
CREATE INDEX IX_SharingLog_Session ON SharingLog(SessionId);
CREATE INDEX IX_SharingLog_SentAt ON SharingLog(SentAt DESC);
