using Microsoft.Data.SqlClient;
using Photobooth.Core;
using System.Linq;

namespace Photobooth.Data;

public record LocationRecord(
    int LocationId, string Name, string Type, string? Address, int CountdownSeconds, bool GlamFilterEnabled,
    PrintTemplate PrintTemplate, BoothTheme Theme, string AdminPin,
    CaptureSettings Capture, ScreenSettings Screen, EffectsSettings Effects, GreenScreenSettings GreenScreen,
    SurveySettings Survey, DisclaimerSettings Disclaimer, PrintOptions PrintOptions, SharingSettings Sharing,
    VirtualAttendantSettings VirtualAttendant, DateTime CreatedAt);

public class LocationRepository
{
    public async Task<int> InsertAsync(string name, string type, string? address, CancellationToken ct = default)
    {
        using var connection = await SqlConnectionFactory.OpenAsync(ct);
        using var command = new SqlCommand(
            "INSERT INTO Location (Name, Type, Address) OUTPUT INSERTED.LocationId VALUES (@Name, @Type, @Address);",
            connection);
        command.Parameters.AddWithValue("@Name", name);
        command.Parameters.AddWithValue("@Type", type);
        command.Parameters.AddWithValue("@Address", (object?)address ?? DBNull.Value);

        return (int)(await command.ExecuteScalarAsync(ct))!;
    }

    public async Task<List<LocationRecord>> GetAllAsync(CancellationToken ct = default)
    {
        using var connection = await SqlConnectionFactory.OpenAsync(ct);
        using var command = new SqlCommand(
            """
            SELECT LocationId, Name, Type, Address, CountdownSeconds, GlamFilterEnabled,
                   PrintLayout, PrintWidthInches, PrintHeightInches, PrintStripCopies,
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
                   CreatedAt
            FROM Location ORDER BY LocationId;
            """,
            connection);
        using var reader = await command.ExecuteReaderAsync(ct);

        var results = new List<LocationRecord>();
        while (await reader.ReadAsync(ct))
        {
            results.Add(new LocationRecord(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.GetInt32(4),
                reader.GetBoolean(5),
                new PrintTemplate(
                    reader.GetString(6),
                    (double)reader.GetDecimal(7),
                    (double)reader.GetDecimal(8),
                    reader.GetInt32(9)),
                new BoothTheme(
                    reader.GetString(10),
                    reader.GetString(11),
                    reader.GetString(12),
                    reader.IsDBNull(13) ? null : reader.GetString(13),
                    reader.GetString(14)),
                reader.GetString(15),
                new CaptureSettings(reader.GetString(16), reader.GetBoolean(17), reader.GetInt32(18), reader.GetInt32(19), reader.GetInt32(20)),
                new ScreenSettings(
                    reader.GetBoolean(21), reader.GetBoolean(22), reader.GetBoolean(23), reader.GetInt32(24),
                    reader.GetBoolean(25), reader.GetInt32(26), reader.IsDBNull(27) ? null : reader.GetString(27)),
                new EffectsSettings(
                    reader.GetBoolean(28), reader.GetString(29), reader.IsDBNull(30) ? null : reader.GetString(30),
                    reader.GetBoolean(31), reader.GetBoolean(32), reader.GetBoolean(34),
                    reader.IsDBNull(35) ? null : reader.GetString(35), reader.GetBoolean(36), reader.GetBoolean(37))
                {
                    EnabledFilterPresetIds = reader.GetString(33),
                },
                new GreenScreenSettings(reader.GetBoolean(38), reader.IsDBNull(39) ? null : reader.GetString(39)),
                new SurveySettings(reader.GetBoolean(40)),
                new DisclaimerSettings(reader.GetString(41), reader.GetString(42)),
                new PrintOptions(reader.GetBoolean(43), reader.GetBoolean(44), reader.GetInt32(45), reader.GetInt32(46), reader.GetString(47)),
                new SharingSettings(reader.GetBoolean(48), reader.GetBoolean(49), reader.GetBoolean(50)),
                new VirtualAttendantSettings(
                    reader.GetBoolean(51), reader.GetString(52), reader.GetBoolean(53), reader.GetBoolean(54),
                    reader.GetBoolean(55), reader.GetBoolean(56), reader.GetBoolean(57), reader.GetBoolean(58)),
                reader.GetDateTime(59)));
        }
        return results;
    }

    /// <summary>Renames an event/location -- the identity shown on the event
    /// launcher's tiles (see EventLauncherWindow), distinct from
    /// Theme.EventName (the guest-facing brand name shown on the kiosk itself,
    /// edited via UpdateThemeAsync).</summary>
    public async Task RenameAsync(int locationId, string name, CancellationToken ct = default)
    {
        using var connection = await SqlConnectionFactory.OpenAsync(ct);
        using var command = new SqlCommand("UPDATE Location SET Name = @Name WHERE LocationId = @LocationId;", connection);
        command.Parameters.AddWithValue("@Name", name);
        command.Parameters.AddWithValue("@LocationId", locationId);
        await command.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Deletes an event/location outright. Throws the underlying
    /// SqlException unhandled if any Session/Booking/etc. row still references
    /// it (no ON DELETE CASCADE in schema.sql) -- callers should catch that and
    /// tell the admin to archive rather than delete an event with recorded
    /// activity, same as EventLauncherWindow's DeleteButton_Click does.</summary>
    public async Task DeleteAsync(int locationId, CancellationToken ct = default)
    {
        using var connection = await SqlConnectionFactory.OpenAsync(ct);
        using var command = new SqlCommand("DELETE FROM Location WHERE LocationId = @LocationId;", connection);
        command.Parameters.AddWithValue("@LocationId", locationId);
        await command.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Clones an existing event's settings (theme, capture, screen,
    /// effects, green screen, survey, disclaimer, print options, sharing,
    /// virtual attendant) into a brand-new Location row under a new name --
    /// same "New event" starting point dslrBooth's own Duplicate action gives.
    /// Deliberately scoped to the Location row's own settings columns only:
    /// child assets (frame library, screen/print template layouts, survey
    /// questions, attendant clips) are NOT copied, since that would mean
    /// duplicating files on disk as well as five more repositories' worth of
    /// rows -- out of scope for this pass. A duplicated event starts with its
    /// own settings/branding but an empty frame library and default layouts,
    /// same as a brand-new event would.</summary>
    public async Task<int> DuplicateAsync(int sourceLocationId, string newName, CancellationToken ct = default)
    {
        List<LocationRecord> all = await GetAllAsync(ct);
        LocationRecord source = all.First(l => l.LocationId == sourceLocationId);

        int newLocationId = await InsertAsync(newName, source.Type, source.Address, ct);
        await UpdateThemeAsync(newLocationId, source.Theme, ct);
        await UpdateSettingsAsync(newLocationId, source.CountdownSeconds, source.GlamFilterEnabled, source.PrintTemplate, source.AdminPin, ct);
        await UpdateDslrBoothParitySettingsAsync(
            newLocationId, source.Capture, source.Screen, source.Effects, source.GreenScreen,
            source.Survey, source.Disclaimer, source.PrintOptions, source.Sharing, ct);
        await UpdateVirtualAttendantSettingsAsync(newLocationId, source.VirtualAttendant, ct);
        return newLocationId;
    }

    /// <summary>Updates just the guest-facing screen chrome toggles (Booth
    /// Icons/live view show-mirror-rotate) -- kept separate from
    /// UpdateDslrBoothParitySettingsAsync so the Screen Editor's own Settings
    /// tab (see ScreenTemplateEditorWindow) can save these without needing to
    /// also load and round-trip Capture/Effects/GreenScreen/Survey/Disclaimer/
    /// PrintOptions/Sharing, same one-section-per-save-button reasoning
    /// UpdateThemeAsync already established.</summary>
    public async Task UpdateScreenSettingsAsync(int locationId, ScreenSettings screen, CancellationToken ct = default)
    {
        using var connection = await SqlConnectionFactory.OpenAsync(ct);
        using var command = new SqlCommand(
            """
            UPDATE Location SET
                BoothIconsEnabled = @BoothIconsEnabled, ShowLiveView = @ShowLiveView,
                MirrorLiveView = @MirrorLiveView, LiveViewRotation = @LiveViewRotation,
                EnableWebcams = @EnableWebcams, WebcamResolutionQuality = @WebcamResolutionQuality,
                AudioInputDeviceName = @AudioInputDeviceName
            WHERE LocationId = @LocationId;
            """,
            connection);
        command.Parameters.AddWithValue("@BoothIconsEnabled", screen.BoothIconsEnabled);
        command.Parameters.AddWithValue("@ShowLiveView", screen.ShowLiveView);
        command.Parameters.AddWithValue("@MirrorLiveView", screen.MirrorLiveView);
        command.Parameters.AddWithValue("@LiveViewRotation", screen.LiveViewRotation);
        command.Parameters.AddWithValue("@EnableWebcams", screen.EnableWebcams);
        command.Parameters.AddWithValue("@WebcamResolutionQuality", screen.WebcamResolutionQuality);
        command.Parameters.AddWithValue("@AudioInputDeviceName", (object?)screen.AudioInputDeviceName ?? DBNull.Value);
        command.Parameters.AddWithValue("@LocationId", locationId);
        await command.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Updates the admin-editable brand identity for a location -- colors,
    /// logo, and event name. Kept separate from UpdateSettingsAsync so saving a
    /// theme change doesn't force the countdown/print-template fields to also
    /// validate, and vice versa.</summary>
    public async Task UpdateThemeAsync(int locationId, BoothTheme theme, CancellationToken ct = default)
    {
        using var connection = await SqlConnectionFactory.OpenAsync(ct);
        using var command = new SqlCommand(
            """
            UPDATE Location SET AccentColorHex = @AccentColorHex, CanvasColorHex = @CanvasColorHex,
                                 InkColorHex = @InkColorHex, LogoImagePath = @LogoImagePath, EventName = @EventName
            WHERE LocationId = @LocationId;
            """,
            connection);
        command.Parameters.AddWithValue("@AccentColorHex", theme.AccentColorHex);
        command.Parameters.AddWithValue("@CanvasColorHex", theme.CanvasColorHex);
        command.Parameters.AddWithValue("@InkColorHex", theme.InkColorHex);
        command.Parameters.AddWithValue("@LogoImagePath", (object?)theme.LogoImagePath ?? DBNull.Value);
        command.Parameters.AddWithValue("@EventName", theme.EventName);
        command.Parameters.AddWithValue("@LocationId", locationId);
        await command.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Updates the admin-editable booth settings for a location -- countdown
    /// duration, whether Glam Booth mode is on, the print template, and the PIN that
    /// gates MainWindow's Setup/launch screen. Read fresh by SqlBoothSettingsProvider
    /// at the start of every session, so a change here takes effect for the very next
    /// guest without needing to restart the app.</summary>
    public async Task UpdateSettingsAsync(int locationId, int countdownSeconds, bool glamFilterEnabled, PrintTemplate printTemplate, string adminPin, CancellationToken ct = default)
    {
        using var connection = await SqlConnectionFactory.OpenAsync(ct);
        using var command = new SqlCommand(
            """
            UPDATE Location SET CountdownSeconds = @CountdownSeconds, GlamFilterEnabled = @GlamFilterEnabled,
                                 PrintLayout = @PrintLayout, PrintWidthInches = @PrintWidthInches,
                                 PrintHeightInches = @PrintHeightInches, PrintStripCopies = @PrintStripCopies,
                                 AdminPin = @AdminPin
            WHERE LocationId = @LocationId;
            """,
            connection);
        command.Parameters.AddWithValue("@CountdownSeconds", countdownSeconds);
        command.Parameters.AddWithValue("@GlamFilterEnabled", glamFilterEnabled);
        command.Parameters.AddWithValue("@PrintLayout", printTemplate.Layout);
        command.Parameters.AddWithValue("@PrintWidthInches", printTemplate.WidthInches);
        command.Parameters.AddWithValue("@PrintHeightInches", printTemplate.HeightInches);
        command.Parameters.AddWithValue("@PrintStripCopies", printTemplate.StripCopies);
        command.Parameters.AddWithValue("@AdminPin", adminPin);
        command.Parameters.AddWithValue("@LocationId", locationId);
        await command.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Updates the dslrBooth feature-parity settings added in Phase 1 --
    /// Capture, Screen, Effects, Green Screen, Survey, Disclaimer, Print Options,
    /// and Sharing. Kept separate from UpdateSettingsAsync so saving one of these
    /// sections doesn't force the countdown/print-template fields to also
    /// validate, same reasoning as UpdateThemeAsync above.</summary>
    public async Task UpdateDslrBoothParitySettingsAsync(
        int locationId,
        CaptureSettings capture,
        ScreenSettings screen,
        EffectsSettings effects,
        GreenScreenSettings greenScreen,
        SurveySettings survey,
        DisclaimerSettings disclaimer,
        PrintOptions printOptions,
        SharingSettings sharing,
        CancellationToken ct = default)
    {
        using var connection = await SqlConnectionFactory.OpenAsync(ct);
        using var command = new SqlCommand(
            """
            UPDATE Location SET
                CaptureMode = @CaptureMode, AlsoCreateGif = @AlsoCreateGif, GifFrameCount = @GifFrameCount,
                GifFrameDelayMs = @GifFrameDelayMs, VideoDurationSeconds = @VideoDurationSeconds,
                BoothIconsEnabled = @BoothIconsEnabled, ShowLiveView = @ShowLiveView,
                MirrorLiveView = @MirrorLiveView, LiveViewRotation = @LiveViewRotation,
                EnableWebcams = @EnableWebcams, WebcamResolutionQuality = @WebcamResolutionQuality,
                AudioInputDeviceName = @AudioInputDeviceName,
                BeautyFilterEnabled = @BeautyFilterEnabled, BeautyFilterAlsoDuringCountdown = @BeautyFilterAlsoDuringCountdown,
                FiltersMode = @FiltersMode, FiltersEnabled = @FiltersEnabled, EnabledFilterPresetIds = @EnabledFilterPresetIds,
                PostProcessingEnabled = @PostProcessingEnabled, PostProcessingApplicationPath = @PostProcessingApplicationPath,
                StickersEnabled = @StickersEnabled,
                WatermarkImagePath = @WatermarkImagePath, WatermarkEnabled = @WatermarkEnabled,
                GreenScreenEnabled = @GreenScreenEnabled, GreenScreenBackgroundPath = @GreenScreenBackgroundPath,
                SurveyEnabled = @SurveyEnabled,
                DisclaimerHeader = @DisclaimerHeader, DisclaimerText = @DisclaimerText,
                PrintAutomatically = @PrintAutomatically, ShowPrintButton = @ShowPrintButton,
                PrintLimitPerEvent = @PrintLimitPerEvent, PrintLimitPerSession = @PrintLimitPerSession, PrintSharpening = @PrintSharpening,
                EmailEnabled = @EmailEnabled, SmsEnabled = @SmsEnabled, QrEnabled = @QrEnabled
            WHERE LocationId = @LocationId;
            """,
            connection);
        command.Parameters.AddWithValue("@CaptureMode", capture.Mode);
        command.Parameters.AddWithValue("@AlsoCreateGif", capture.AlsoCreateGif);
        command.Parameters.AddWithValue("@GifFrameCount", capture.FrameCount);
        command.Parameters.AddWithValue("@GifFrameDelayMs", capture.FrameDelayMs);
        command.Parameters.AddWithValue("@VideoDurationSeconds", capture.VideoDurationSeconds);
        command.Parameters.AddWithValue("@BoothIconsEnabled", screen.BoothIconsEnabled);
        command.Parameters.AddWithValue("@ShowLiveView", screen.ShowLiveView);
        command.Parameters.AddWithValue("@MirrorLiveView", screen.MirrorLiveView);
        command.Parameters.AddWithValue("@LiveViewRotation", screen.LiveViewRotation);
        command.Parameters.AddWithValue("@EnableWebcams", screen.EnableWebcams);
        command.Parameters.AddWithValue("@WebcamResolutionQuality", screen.WebcamResolutionQuality);
        command.Parameters.AddWithValue("@AudioInputDeviceName", (object?)screen.AudioInputDeviceName ?? DBNull.Value);
        command.Parameters.AddWithValue("@BeautyFilterEnabled", effects.BeautyFilterEnabled);
        command.Parameters.AddWithValue("@BeautyFilterAlsoDuringCountdown", effects.BeautyFilterAlsoDuringCountdown);
        command.Parameters.AddWithValue("@FiltersMode", effects.FiltersMode);
        command.Parameters.AddWithValue("@FiltersEnabled", effects.FiltersEnabled);
        command.Parameters.AddWithValue("@EnabledFilterPresetIds", effects.EnabledFilterPresetIds);
        command.Parameters.AddWithValue("@PostProcessingEnabled", effects.PostProcessingEnabled);
        command.Parameters.AddWithValue("@PostProcessingApplicationPath", (object?)effects.PostProcessingApplicationPath ?? DBNull.Value);
        command.Parameters.AddWithValue("@StickersEnabled", effects.StickersEnabled);
        command.Parameters.AddWithValue("@WatermarkImagePath", (object?)effects.WatermarkImagePath ?? DBNull.Value);
        command.Parameters.AddWithValue("@WatermarkEnabled", effects.WatermarkEnabled);
        command.Parameters.AddWithValue("@GreenScreenEnabled", greenScreen.Enabled);
        command.Parameters.AddWithValue("@GreenScreenBackgroundPath", (object?)greenScreen.BackgroundImagePath ?? DBNull.Value);
        command.Parameters.AddWithValue("@SurveyEnabled", survey.Enabled);
        command.Parameters.AddWithValue("@DisclaimerHeader", disclaimer.Header);
        command.Parameters.AddWithValue("@DisclaimerText", disclaimer.Text);
        command.Parameters.AddWithValue("@PrintAutomatically", printOptions.PrintAutomatically);
        command.Parameters.AddWithValue("@ShowPrintButton", printOptions.ShowPrintButton);
        command.Parameters.AddWithValue("@PrintLimitPerEvent", printOptions.PrintLimitPerEvent);
        command.Parameters.AddWithValue("@PrintLimitPerSession", printOptions.PrintLimitPerSession);
        command.Parameters.AddWithValue("@PrintSharpening", printOptions.PrintSharpening);
        command.Parameters.AddWithValue("@EmailEnabled", sharing.EmailEnabled);
        command.Parameters.AddWithValue("@SmsEnabled", sharing.SmsEnabled);
        command.Parameters.AddWithValue("@QrEnabled", sharing.QrEnabled);
        command.Parameters.AddWithValue("@LocationId", locationId);
        await command.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Updates just which built-in filter presets the Filters grid
    /// offers (see FilterLibraryWindow) -- kept separate from
    /// UpdateDslrBoothParitySettingsAsync so that window's own Save button
    /// doesn't need to load and round-trip every other Effects &amp; Stickers
    /// field, same one-section-per-save-button reasoning UpdateScreenSettingsAsync
    /// already established.</summary>
    public async Task UpdateEnabledFilterPresetsAsync(int locationId, string enabledFilterPresetIds, CancellationToken ct = default)
    {
        using var connection = await SqlConnectionFactory.OpenAsync(ct);
        using var command = new SqlCommand(
            "UPDATE Location SET EnabledFilterPresetIds = @EnabledFilterPresetIds WHERE LocationId = @LocationId;",
            connection);
        command.Parameters.AddWithValue("@EnabledFilterPresetIds", enabledFilterPresetIds);
        command.Parameters.AddWithValue("@LocationId", locationId);
        await command.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Updates the Virtual Attendant on/off switch, style, and per-stage
    /// randomize flags (see AdminWindow's Virtual Attendant section). Kept separate
    /// from UpdateDslrBoothParitySettingsAsync so saving one doesn't force the
    /// other's fields to also validate, same reasoning UpdateThemeAsync already
    /// gives. Read fresh by SqlVirtualAttendantService on every GetCueAsync call,
    /// so a change here takes effect for the very next state transition, not just
    /// the next session.</summary>
    public async Task UpdateVirtualAttendantSettingsAsync(int locationId, VirtualAttendantSettings settings, CancellationToken ct = default)
    {
        using var connection = await SqlConnectionFactory.OpenAsync(ct);
        using var command = new SqlCommand(
            """
            UPDATE Location SET
                AttendantEnabled = @AttendantEnabled, AttendantStyle = @AttendantStyle,
                AttendantRandomizeConsent = @AttendantRandomizeConsent, AttendantRandomizeCountdown = @AttendantRandomizeCountdown,
                AttendantRandomizeCapturing = @AttendantRandomizeCapturing, AttendantRandomizeReviewing = @AttendantRandomizeReviewing,
                AttendantRandomizePrinting = @AttendantRandomizePrinting, AttendantRandomizeComplete = @AttendantRandomizeComplete
            WHERE LocationId = @LocationId;
            """,
            connection);
        command.Parameters.AddWithValue("@AttendantEnabled", settings.Enabled);
        command.Parameters.AddWithValue("@AttendantStyle", settings.Style);
        command.Parameters.AddWithValue("@AttendantRandomizeConsent", settings.RandomizeConsent);
        command.Parameters.AddWithValue("@AttendantRandomizeCountdown", settings.RandomizeCountdown);
        command.Parameters.AddWithValue("@AttendantRandomizeCapturing", settings.RandomizeCapturing);
        command.Parameters.AddWithValue("@AttendantRandomizeReviewing", settings.RandomizeReviewing);
        command.Parameters.AddWithValue("@AttendantRandomizePrinting", settings.RandomizePrinting);
        command.Parameters.AddWithValue("@AttendantRandomizeComplete", settings.RandomizeComplete);
        command.Parameters.AddWithValue("@LocationId", locationId);
        await command.ExecuteNonQueryAsync(ct);
    }
}
