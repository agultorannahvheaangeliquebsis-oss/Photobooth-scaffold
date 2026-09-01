-- Baseline schema for a genuinely fresh database. Guarded so this script
-- stays a no-op against a database that already has Location (e.g. one
-- provisioned before this DbUp-based migration runner existed, via the old
-- ad-hoc DatabaseInitializer.EnsureSchemaAsync patches now split across
-- Script0002 and up) -- DbUp records every script as run regardless of
-- whether its body did anything, so this guard is what keeps re-running the
-- full CREATE TABLE set from failing against an already-provisioned DB.
--
-- Content mirrors schema.sql (the repo's own single source of truth for the
-- schema shape) as of the point this migration runner was introduced.
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'Location')
BEGIN

CREATE TABLE Location (
    LocationId      INT IDENTITY(1,1) PRIMARY KEY,
    Name            NVARCHAR(100)   NOT NULL,
    Type            NVARCHAR(20)    NOT NULL CHECK (Type IN ('event', 'vendo')),
    Address         NVARCHAR(255)   NULL,
    CountdownSeconds   INT          NOT NULL DEFAULT 3,
    GlamFilterEnabled  BIT          NOT NULL DEFAULT 0,
    AdminPin           NVARCHAR(20) NOT NULL DEFAULT '1234',
    PrintLayout        NVARCHAR(20) NOT NULL DEFAULT 'Single' CHECK (PrintLayout IN ('Single', 'Strip')),
    PrintWidthInches   DECIMAL(5,2) NOT NULL DEFAULT 4,
    PrintHeightInches  DECIMAL(5,2) NOT NULL DEFAULT 6,
    PrintStripCopies   INT          NOT NULL DEFAULT 1,
    AccentColorHex     NVARCHAR(9)  NOT NULL DEFAULT '#365C58',
    CanvasColorHex     NVARCHAR(9)  NOT NULL DEFAULT '#F4F3F0',
    InkColorHex        NVARCHAR(9)  NOT NULL DEFAULT '#202124',
    LogoImagePath      NVARCHAR(500) NULL,
    EventName          NVARCHAR(100) NOT NULL DEFAULT 'Focus & Snap',
    CaptureMode         NVARCHAR(20) NOT NULL DEFAULT 'Photo' CHECK (CaptureMode IN ('Photo', 'GIF', 'Boomerang', 'Video')),
    AlsoCreateGif       BIT          NOT NULL DEFAULT 0,
    GifFrameCount       INT          NOT NULL DEFAULT 4,
    GifFrameDelayMs     INT          NOT NULL DEFAULT 500,
    VideoDurationSeconds INT         NOT NULL DEFAULT 10,
    BoothIconsEnabled   BIT          NOT NULL DEFAULT 0,
    ShowLiveView        BIT          NOT NULL DEFAULT 1,
    MirrorLiveView      BIT          NOT NULL DEFAULT 1,
    LiveViewRotation    INT          NOT NULL DEFAULT 0,
    EnableWebcams       BIT          NOT NULL DEFAULT 1,
    WebcamResolutionQuality INT      NOT NULL DEFAULT 70,
    AudioInputDeviceName NVARCHAR(200) NULL,
    BeautyFilterEnabled BIT          NOT NULL DEFAULT 0,
    BeautyFilterAlsoDuringCountdown BIT NOT NULL DEFAULT 0,
    FiltersMode         NVARCHAR(20) NOT NULL DEFAULT 'Ask' CHECK (FiltersMode IN ('Ask', 'Auto')),
    FiltersEnabled      BIT          NOT NULL DEFAULT 0,
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
    AttendantEnabled            BIT NOT NULL DEFAULT 0,
    AttendantStyle               NVARCHAR(20) NOT NULL DEFAULT 'Friendly',
    AttendantRandomizeConsent    BIT NOT NULL DEFAULT 0,
    AttendantRandomizeCountdown  BIT NOT NULL DEFAULT 0,
    AttendantRandomizeCapturing  BIT NOT NULL DEFAULT 0,
    AttendantRandomizeReviewing  BIT NOT NULL DEFAULT 0,
    AttendantRandomizePrinting   BIT NOT NULL DEFAULT 0,
    AttendantRandomizeComplete   BIT NOT NULL DEFAULT 0,
    CreatedAt       DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME()
);

CREATE TABLE Printer (
    PrinterId       INT IDENTITY(1,1) PRIMARY KEY,
    LocationId      INT             NOT NULL REFERENCES Location(LocationId),
    Model           NVARCHAR(100)   NOT NULL,
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
    PackageType     NVARCHAR(50)    NOT NULL,
    Price           DECIMAL(10,2)   NOT NULL,
    Status          NVARCHAR(20)    NOT NULL DEFAULT 'confirmed'
                        CHECK (Status IN ('inquiry', 'confirmed', 'completed', 'cancelled')),
    CreatedAt       DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME()
);

CREATE TABLE Session (
    SessionId       INT IDENTITY(1,1) PRIMARY KEY,
    LocationId      INT             NOT NULL REFERENCES Location(LocationId),
    BookingId       INT             NULL REFERENCES Booking(BookingId),
    Mode            NVARCHAR(20)    NOT NULL CHECK (Mode IN ('event', 'vendo')),
    StartedAt       DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME(),
    EndedAt         DATETIME2       NULL,
    Status          NVARCHAR(20)    NOT NULL DEFAULT 'in_progress'
                        CHECK (Status IN ('in_progress', 'completed', 'abandoned', 'error'))
);

CREATE TABLE [Print] (
    PrintId         INT IDENTITY(1,1) PRIMARY KEY,
    SessionId       INT             NOT NULL REFERENCES Session(SessionId),
    PrinterId       INT             NOT NULL REFERENCES Printer(PrinterId),
    PrintedAt       DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME(),
    TemplateUsed    NVARCHAR(100)   NULL,
    FilePath        NVARCHAR(500)   NULL
);

CREATE TABLE Payment (
    PaymentId       INT IDENTITY(1,1) PRIMARY KEY,
    SessionId       INT             NOT NULL REFERENCES Session(SessionId),
    Amount          DECIMAL(10,2)   NOT NULL,
    Method          NVARCHAR(30)    NOT NULL,
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

CREATE TABLE Frame (
    FrameId         INT IDENTITY(1,1) PRIMARY KEY,
    LocationId      INT             NOT NULL REFERENCES Location(LocationId),
    Name            NVARCHAR(100)   NOT NULL,
    ImagePath       NVARCHAR(500)   NOT NULL,
    SortOrder       INT             NOT NULL DEFAULT 0,
    IsActive        BIT             NOT NULL DEFAULT 1,
    CreatedAt       DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME()
);

CREATE TABLE Feedback (
    FeedbackId      INT IDENTITY(1,1) PRIMARY KEY,
    SessionId       INT             NOT NULL REFERENCES Session(SessionId),
    Rating          INT             NULL CHECK (Rating BETWEEN 1 AND 5),
    Comment         NVARCHAR(1000)  NULL,
    RecordedAt      DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME()
);

CREATE TABLE GuestbookVideo (
    GuestbookVideoId INT IDENTITY(1,1) PRIMARY KEY,
    SessionId        INT             NOT NULL REFERENCES Session(SessionId),
    FilePath         NVARCHAR(500)   NOT NULL,
    DurationSeconds  INT             NOT NULL,
    RecordedAt       DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME()
);

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
    ShapeType       NVARCHAR(20)    NULL,
    PhotoIndex      INT             NULL,
    SortOrder       INT             NOT NULL DEFAULT 0,
    CreatedAt       DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME()
);

CREATE TABLE VirtualAttendantClip (
    ClipId          INT IDENTITY(1,1) PRIMARY KEY,
    LocationId      INT             NOT NULL REFERENCES Location(LocationId),
    Stage           NVARCHAR(20)    NOT NULL CONSTRAINT CK_VirtualAttendantClip_Stage CHECK (Stage IN ('Setup', 'Idle', 'Consent', 'Countdown', 'Capturing', 'FilterPicker', 'Reviewing', 'FramePicker', 'Payment', 'Printing', 'Complete', 'Guestbook', 'Feedback', 'Survey', 'Error')),
    FilePath        NVARCHAR(500)   NOT NULL,
    SortOrder       INT             NOT NULL DEFAULT 0,
    CreatedAt       DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME()
);

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

CREATE INDEX IX_Session_Location_Mode ON Session(LocationId, Mode);
CREATE INDEX IX_Print_Session ON [Print](SessionId);
CREATE INDEX IX_Payment_Session ON Payment(SessionId);
CREATE INDEX IX_Consent_Session ON Consent(SessionId);
CREATE INDEX IX_InventoryLog_Printer_LoggedAt ON InventoryLog(PrinterId, LoggedAt DESC);
CREATE INDEX IX_Frame_Location_Active ON Frame(LocationId, IsActive, SortOrder);
CREATE INDEX IX_Feedback_Session ON Feedback(SessionId);
CREATE INDEX IX_GuestbookVideo_Session ON GuestbookVideo(SessionId);
CREATE INDEX IX_PrintTemplateElement_Location ON PrintTemplateElement(LocationId, SortOrder);
CREATE INDEX IX_VirtualAttendantClip_Location_Stage ON VirtualAttendantClip(LocationId, Stage, SortOrder);
CREATE INDEX IX_SurveyQuestion_Location_Active ON SurveyQuestion(LocationId, IsActive, SortOrder);
CREATE INDEX IX_SurveyResponse_Session ON SurveyResponse(SessionId);
CREATE INDEX IX_ScreenTemplateElement_Location_Screen ON ScreenTemplateElement(LocationId, Screen, SortOrder);

END
