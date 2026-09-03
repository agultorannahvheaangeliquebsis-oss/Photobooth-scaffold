using Microsoft.Data.SqlClient;
using Photobooth.Core;

namespace Photobooth.Data;

/// <summary>
/// Real IBoothSettingsProvider: reads CountdownSeconds/GlamFilterEnabled/the
/// print template straight from the Location row on every call (no caching)
/// -- this is what makes an admin's settings change take effect for the very
/// next guest session instead of needing an app restart.
/// </summary>
public class SqlBoothSettingsProvider : IBoothSettingsProvider
{
    private readonly int _locationId;

    public SqlBoothSettingsProvider(int locationId)
    {
        _locationId = locationId;
    }

    public async Task<BoothSettings> GetSettingsAsync(CancellationToken ct = default)
    {
        using var connection = await SqlConnectionFactory.OpenAsync(ct);
        using var command = new SqlCommand(
            """
            SELECT CountdownSeconds, GlamFilterEnabled, PrintLayout, PrintWidthInches, PrintHeightInches, PrintStripCopies,
                   AccentColorHex, CanvasColorHex, InkColorHex, LogoImagePath, EventName, AdminPin,
                   CaptureMode, AlsoCreateGif, GifFrameCount, GifFrameDelayMs, VideoDurationSeconds,
                   BoothIconsEnabled, ShowLiveView, MirrorLiveView, LiveViewRotation,
                   EnableWebcams, WebcamResolutionQuality, AudioInputDeviceName,
                   BeautyFilterEnabled, FiltersMode, WatermarkImagePath,
                   BeautyFilterAlsoDuringCountdown, FiltersEnabled, EnabledFilterPresetIds,
                   PostProcessingEnabled, PostProcessingApplicationPath,
                   StickersEnabled, WatermarkEnabled,
                   GreenScreenEnabled, GreenScreenBackgroundPath,
                   SurveyEnabled,
                   DisclaimerHeader, DisclaimerText,
                   PrintAutomatically, ShowPrintButton, PrintLimitPerEvent, PrintLimitPerSession, PrintSharpening,
                   EmailEnabled, SmsEnabled, QrEnabled,
                   AttendantEnabled, AttendantStyle, AttendantRandomizeConsent, AttendantRandomizeCountdown,
                   AttendantRandomizeCapturing, AttendantRandomizeReviewing, AttendantRandomizePrinting, AttendantRandomizeComplete,
                   PoseStripPosition,
                   EmailFromAddress, EmailSubject, EmailSmtpHost, EmailSmtpPort, EmailSmtpUsername, EmailUseSsl,
                   EmailSmtpPasswordProtected, TwilioAccountSid, TwilioFromNumber, TwilioAuthTokenProtected,
                   IsLocked, RemoteControlEnabled,
                   SlideshowEnabled, SlideshowIntervalSeconds, SlideshowTransition, SlideshowShowLogoOverlay, SlideshowShowQrOverlay,
                   BoothIconLabelsEnabled, WelcomeShowLiveView, LiveTemplatePreview, StretchLiveView,
                   BrowseButtonEnabled, ChooseTemplateEnabled, StartScreenVideoPath, UnlockButtonOpacityPercent,
                   SessionTriggerTouchScreen, SessionTriggerF13, SessionTriggerKeys, GuestQrCodeEnabled,
                   CropLiveView, AutoTriggerCamera, FlashScreenWhite, ShowCancelButton,
                   CountdownColorHex, PhotoThumbnailsEnabled, SayCheeseImagePath,
                   SkipSharingScreen, ShowDoneButton, SharingIconsLocation, SharingTextLabelsEnabled,
                   FinalScreenTimeoutSeconds, ShowOriginalPhotos, ShowRetakeButton,
                   TwitterEnabled, PrintEnabled,
                   WelcomeBackgroundColorHex, WelcomeBackgroundImagePath,
                   CaptureBackgroundColorHex, CaptureBackgroundImagePath,
                   SharingBackgroundColorHex, SharingBackgroundImagePath,
                   WelcomePhotoIconEnabled, WelcomeGifIconEnabled, WelcomeBoomerangIconEnabled, WelcomeVideoIconEnabled,
                   WelcomeIconsPositionXPercent, WelcomeIconsPositionYPercent, WelcomeIconsLayout, WelcomeIconsAlignment,
                   CaptureCancelButtonPositionXPercent, CaptureCancelButtonPositionYPercent,
                   SharingIconsGroupEnabled, SharingIconsPositionXPercent, SharingIconsPositionYPercent,
                   SharingIconsLayout, SharingIconsAlignment,
                   PoseStripBackgroundOpacityPercent, PoseStripActiveBorderColorHex, PoseStripShowPlaceholderNumbers,
                   PaymentTiming, CameraDeviceName, SaveMirroredPhotos
            FROM Location WHERE LocationId = @LocationId;
            """,
            connection);
        command.Parameters.AddWithValue("@LocationId", _locationId);

        int countdownSeconds;
        bool glamFilterEnabled;
        string adminPin;
        var printTemplate = default(PrintTemplate)!;
        var theme = default(BoothTheme)!;
        var capture = default(CaptureSettings)!;
        var screen = default(ScreenSettings)!;
        var effects = default(EffectsSettings)!;
        var greenScreen = default(GreenScreenSettings)!;
        var survey = default(SurveySettings)!;
        var disclaimer = default(DisclaimerSettings)!;
        var printOptions = default(PrintOptions)!;
        var sharing = default(SharingSettings)!;
        var virtualAttendant = default(VirtualAttendantSettings)!;
        bool isLocked;
        bool remoteControlEnabled;
        var slideshow = default(SlideshowSettings)!;
        using (var reader = await command.ExecuteReaderAsync(ct))
        {
            if (!await reader.ReadAsync(ct))
            {
                throw new InvalidOperationException($"Location {_locationId} not found -- can't read booth settings.");
            }

            countdownSeconds = reader.GetInt32(0);
            glamFilterEnabled = reader.GetBoolean(1);
            printTemplate = new PrintTemplate(
                reader.GetString(2),
                (double)reader.GetDecimal(3),
                (double)reader.GetDecimal(4),
                reader.GetInt32(5));
            theme = new BoothTheme(
                reader.GetString(6),
                reader.GetString(7),
                reader.GetString(8),
                reader.IsDBNull(9) ? null : reader.GetString(9),
                reader.GetString(10));
            adminPin = reader.GetString(11);
            capture = new CaptureSettings(reader.GetString(12), reader.GetBoolean(13), reader.GetInt32(14), reader.GetInt32(15), reader.GetInt32(16));
            screen = new ScreenSettings(
                reader.GetBoolean(17), reader.GetBoolean(18), reader.GetBoolean(19), reader.GetInt32(20),
                reader.GetBoolean(21), reader.GetInt32(22), reader.IsDBNull(23) ? null : reader.GetString(23),
                reader.GetString(55))
            {
                BoothIconLabelsEnabled = reader.GetBoolean(73),
                WelcomeShowLiveView = reader.GetBoolean(74),
                LiveTemplatePreview = reader.GetBoolean(75),
                StretchLiveView = reader.GetString(76),
                BrowseButtonEnabled = reader.GetBoolean(77),
                ChooseTemplateEnabled = reader.GetBoolean(78),
                StartScreenVideoPath = reader.IsDBNull(79) ? null : reader.GetString(79),
                UnlockButtonOpacityPercent = reader.GetInt32(80),
                SessionTriggerTouchScreen = reader.GetBoolean(81),
                SessionTriggerF13 = reader.GetBoolean(82),
                SessionTriggerKeys = reader.GetBoolean(83),
                GuestQrCodeEnabled = reader.GetBoolean(84),
                CropLiveView = reader.GetBoolean(85),
                AutoTriggerCamera = reader.GetBoolean(86),
                FlashScreenWhite = reader.GetBoolean(87),
                ShowCancelButton = reader.GetBoolean(88),
                CountdownColorHex = reader.GetString(89),
                PhotoThumbnailsEnabled = reader.GetBoolean(90),
                SayCheeseImagePath = reader.IsDBNull(91) ? null : reader.GetString(91),
                SkipSharingScreen = reader.GetBoolean(92),
                ShowDoneButton = reader.GetBoolean(93),
                SharingIconsLocation = reader.GetString(94),
                SharingTextLabelsEnabled = reader.GetBoolean(95),
                FinalScreenTimeoutSeconds = reader.GetInt32(96),
                ShowOriginalPhotos = reader.GetBoolean(97),
                ShowRetakeButton = reader.GetBoolean(98),
                WelcomeBackgroundColorHex = reader.GetString(101),
                WelcomeBackgroundImagePath = reader.IsDBNull(102) ? null : reader.GetString(102),
                CaptureBackgroundColorHex = reader.GetString(103),
                CaptureBackgroundImagePath = reader.IsDBNull(104) ? null : reader.GetString(104),
                SharingBackgroundColorHex = reader.GetString(105),
                SharingBackgroundImagePath = reader.IsDBNull(106) ? null : reader.GetString(106),
                WelcomePhotoIconEnabled = reader.GetBoolean(107),
                WelcomeGifIconEnabled = reader.GetBoolean(108),
                WelcomeBoomerangIconEnabled = reader.GetBoolean(109),
                WelcomeVideoIconEnabled = reader.GetBoolean(110),
                WelcomeIconsPositionXPercent = reader.GetDouble(111),
                WelcomeIconsPositionYPercent = reader.GetDouble(112),
                WelcomeIconsLayout = reader.GetString(113),
                WelcomeIconsAlignment = reader.GetString(114),
                CaptureCancelButtonPositionXPercent = reader.GetDouble(115),
                CaptureCancelButtonPositionYPercent = reader.GetDouble(116),
                SharingIconsGroupEnabled = reader.GetBoolean(117),
                SharingIconsPositionXPercent = reader.GetDouble(118),
                SharingIconsPositionYPercent = reader.GetDouble(119),
                SharingIconsLayout = reader.GetString(120),
                SharingIconsAlignment = reader.GetString(121),
                PoseStripBackgroundOpacityPercent = reader.GetInt32(122),
                PoseStripActiveBorderColorHex = reader.GetString(123),
                PoseStripShowPlaceholderNumbers = reader.GetBoolean(124),
                CameraDeviceName = reader.IsDBNull(126) ? null : reader.GetString(126),
                SaveMirroredPhotos = reader.GetBoolean(127),
            };
            effects = new EffectsSettings(
                reader.GetBoolean(24), reader.GetString(25), reader.IsDBNull(26) ? null : reader.GetString(26),
                reader.GetBoolean(27), reader.GetBoolean(28), reader.GetBoolean(30),
                reader.IsDBNull(31) ? null : reader.GetString(31), reader.GetBoolean(32), reader.GetBoolean(33))
            {
                EnabledFilterPresetIds = reader.GetString(29),
            };
            greenScreen = new GreenScreenSettings(reader.GetBoolean(34), reader.IsDBNull(35) ? null : reader.GetString(35));
            survey = new SurveySettings(reader.GetBoolean(36));
            disclaimer = new DisclaimerSettings(reader.GetString(37), reader.GetString(38));
            printOptions = new PrintOptions(reader.GetBoolean(39), reader.GetBoolean(40), reader.GetInt32(41), reader.GetInt32(42), reader.GetString(43));
            sharing = new SharingSettings(reader.GetBoolean(44), reader.GetBoolean(45), reader.GetBoolean(46))
            {
                EmailFromAddress = reader.GetString(56),
                EmailSubject = reader.GetString(57),
                EmailSmtpHost = reader.GetString(58),
                EmailSmtpPort = reader.GetInt32(59),
                EmailSmtpUsername = reader.GetString(60),
                EmailUseSsl = reader.GetBoolean(61),
                EmailSmtpPasswordProtected = reader.GetString(62),
                TwilioAccountSid = reader.GetString(63),
                TwilioFromNumber = reader.GetString(64),
                TwilioAuthTokenProtected = reader.GetString(65),
                TwitterEnabled = reader.GetBoolean(99),
                PrintEnabled = reader.GetBoolean(100),
                PaymentTiming = reader.GetString(125),
            };
            virtualAttendant = new VirtualAttendantSettings(
                reader.GetBoolean(47), reader.GetString(48), reader.GetBoolean(49), reader.GetBoolean(50),
                reader.GetBoolean(51), reader.GetBoolean(52), reader.GetBoolean(53), reader.GetBoolean(54));
            isLocked = reader.GetBoolean(66);
            remoteControlEnabled = reader.GetBoolean(67);
            slideshow = new SlideshowSettings(
                reader.GetBoolean(68), reader.GetInt32(69), reader.GetString(70), reader.GetBoolean(71), reader.GetBoolean(72));
        }

        // A separate connection (not this method's `connection` above), so
        // this can only run once the reader above has finished either way --
        // fetched after, not interleaved with, the Location row read.
        printTemplate = printTemplate with
        {
            Elements = await new PrintTemplateElementRepository().GetAllByLocationAsync(_locationId, ct),
        };

        return new BoothSettings(countdownSeconds, glamFilterEnabled, printTemplate, adminPin)
        {
            Theme = theme,
            Capture = capture,
            Screen = screen,
            Effects = effects,
            GreenScreen = greenScreen,
            Survey = survey,
            Disclaimer = disclaimer,
            PrintOptions = printOptions,
            Sharing = sharing,
            VirtualAttendant = virtualAttendant,
            IsLocked = isLocked,
            RemoteControlEnabled = remoteControlEnabled,
            Slideshow = slideshow,
        };
    }
}
