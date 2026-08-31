using Microsoft.Data.SqlClient;

namespace Photobooth.Data;

/// <summary>
/// Gets a freshly-provisioned LocalDB instance from nothing to
/// ready-to-run: creates the database if it doesn't exist, applies
/// schema.sql if the tables aren't there yet, and seeds a Location,
/// a Printer, and a couple Booking rows so the FK chain Session/Print
/// depend on isn't empty on first run. Idempotent -- safe to call on
/// every app startup.
/// </summary>
public static class DatabaseInitializer
{
    public record SeedIds(int LocationId, int PrinterId, string LocationType);

    public static async Task<SeedIds> InitializeAsync(CancellationToken ct = default)
    {
        await EnsureDatabaseExistsAsync(ct);
        await EnsureSchemaAsync(ct);
        return await EnsureSeedDataAsync(ct);
    }

    private static async Task EnsureDatabaseExistsAsync(CancellationToken ct)
    {
        var builder = new SqlConnectionStringBuilder(SqlConnectionFactory.ConnectionString);
        string databaseName = builder.InitialCatalog;
        builder.InitialCatalog = "master";

        using var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync(ct);
        using var command = new SqlCommand(
            $"IF NOT EXISTS (SELECT 1 FROM sys.databases WHERE name = @Name) EXEC('CREATE DATABASE [{databaseName}]');",
            connection);
        command.Parameters.AddWithValue("@Name", databaseName);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task EnsureSchemaAsync(CancellationToken ct)
    {
        using var connection = await SqlConnectionFactory.OpenAsync(ct);
        using (var checkCommand = new SqlCommand(
            "SELECT 1 FROM sys.tables WHERE name = 'Location';", connection))
        {
            if (await checkCommand.ExecuteScalarAsync(ct) is null)
            {
                string schemaPath = Path.Combine(AppContext.BaseDirectory, "schema.sql");
                string schema = await File.ReadAllTextAsync(schemaPath, ct);
                using var createCommand = new SqlCommand(schema, connection);
                await createCommand.ExecuteNonQueryAsync(ct);
                return; // schema.sql above already includes Consent for a fresh database
            }
        }

        // Location already existed, so schema.sql didn't run above -- that
        // check predates the Consent table, so a database seeded before this
        // feature needs it added on its own. Not a real migration system,
        // just enough to keep an already-seeded LocalDB working without a
        // manual DROP DATABASE.
        await EnsureConsentTableAsync(connection, ct);
        await EnsureBoothSettingsColumnsAsync(connection, ct);
        await EnsureAdminPinColumnAsync(connection, ct);
        await EnsureFrameTableAsync(connection, ct);
        await EnsurePrintTemplateColumnsAsync(connection, ct);
        await EnsureFeedbackTableAsync(connection, ct);
        await EnsureBoothThemeColumnsAsync(connection, ct);
        await EnsureGuestbookVideoTableAsync(connection, ct);
        await EnsurePrintTemplateElementTableAsync(connection, ct);
        await EnsureDslrBoothParitySettingsColumnsAsync(connection, ct);
    }

    /// <summary>Same reasoning as EnsureBoothSettingsColumnsAsync, for the dslrBooth
    /// feature-parity settings columns added to Location after this check was
    /// originally written (see BUILD_PLAN.md's "dslrBooth feature-parity plan"
    /// section, Phase 1).</summary>
    private static async Task EnsureDslrBoothParitySettingsColumnsAsync(SqlConnection connection, CancellationToken ct)
    {
        using var command = new SqlCommand(
            """
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'CaptureMode')
                ALTER TABLE Location ADD CaptureMode NVARCHAR(20) NOT NULL DEFAULT 'Photo' CHECK (CaptureMode IN ('Photo', 'GIF', 'Boomerang', 'Video'));
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'AlsoCreateGif')
                ALTER TABLE Location ADD AlsoCreateGif BIT NOT NULL DEFAULT 0;
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'GifFrameCount')
                ALTER TABLE Location ADD GifFrameCount INT NOT NULL DEFAULT 4;
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'GifFrameDelayMs')
                ALTER TABLE Location ADD GifFrameDelayMs INT NOT NULL DEFAULT 500;
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'BoothIconsEnabled')
                ALTER TABLE Location ADD BoothIconsEnabled BIT NOT NULL DEFAULT 0;
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'ShowLiveView')
                ALTER TABLE Location ADD ShowLiveView BIT NOT NULL DEFAULT 1;
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'MirrorLiveView')
                ALTER TABLE Location ADD MirrorLiveView BIT NOT NULL DEFAULT 1;
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'LiveViewRotation')
                ALTER TABLE Location ADD LiveViewRotation INT NOT NULL DEFAULT 0;
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'BeautyFilterEnabled')
                ALTER TABLE Location ADD BeautyFilterEnabled BIT NOT NULL DEFAULT 0;
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'FiltersMode')
                ALTER TABLE Location ADD FiltersMode NVARCHAR(20) NOT NULL DEFAULT 'Ask' CHECK (FiltersMode IN ('Ask', 'Auto'));
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'WatermarkImagePath')
                ALTER TABLE Location ADD WatermarkImagePath NVARCHAR(500) NULL;
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'GreenScreenEnabled')
                ALTER TABLE Location ADD GreenScreenEnabled BIT NOT NULL DEFAULT 0;
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'GreenScreenBackgroundPath')
                ALTER TABLE Location ADD GreenScreenBackgroundPath NVARCHAR(500) NULL;
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'SurveyEnabled')
                ALTER TABLE Location ADD SurveyEnabled BIT NOT NULL DEFAULT 0;
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'DisclaimerHeader')
                ALTER TABLE Location ADD DisclaimerHeader NVARCHAR(200) NOT NULL DEFAULT 'Do you agree with the terms?';
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'DisclaimerText')
                ALTER TABLE Location ADD DisclaimerText NVARCHAR(MAX) NOT NULL DEFAULT '';
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'PrintAutomatically')
                ALTER TABLE Location ADD PrintAutomatically BIT NOT NULL DEFAULT 1;
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'ShowPrintButton')
                ALTER TABLE Location ADD ShowPrintButton BIT NOT NULL DEFAULT 0;
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'PrintLimitPerEvent')
                ALTER TABLE Location ADD PrintLimitPerEvent INT NOT NULL DEFAULT 5000;
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'PrintLimitPerSession')
                ALTER TABLE Location ADD PrintLimitPerSession INT NOT NULL DEFAULT 3;
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'PrintSharpening')
                ALTER TABLE Location ADD PrintSharpening NVARCHAR(10) NOT NULL DEFAULT 'Medium' CHECK (PrintSharpening IN ('Low', 'Medium', 'High'));
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'EmailEnabled')
                ALTER TABLE Location ADD EmailEnabled BIT NOT NULL DEFAULT 1;
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'SmsEnabled')
                ALTER TABLE Location ADD SmsEnabled BIT NOT NULL DEFAULT 0;
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'QrEnabled')
                ALTER TABLE Location ADD QrEnabled BIT NOT NULL DEFAULT 1;
            """,
            connection);
        await command.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Same reasoning as EnsureConsentTableAsync, for the PrintTemplateElement
    /// table added after this check was originally written.</summary>
    private static async Task EnsurePrintTemplateElementTableAsync(SqlConnection connection, CancellationToken ct)
    {
        using (var checkCommand = new SqlCommand("SELECT 1 FROM sys.tables WHERE name = 'PrintTemplateElement';", connection))
        {
            if (await checkCommand.ExecuteScalarAsync(ct) is not null)
            {
                return;
            }
        }

        using var createCommand = new SqlCommand(
            """
            CREATE TABLE PrintTemplateElement (
                ElementId       INT IDENTITY(1,1) PRIMARY KEY,
                LocationId      INT             NOT NULL REFERENCES Location(LocationId),
                Kind            NVARCHAR(20)    NOT NULL CHECK (Kind IN ('Logo', 'Text')),
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
            CREATE INDEX IX_PrintTemplateElement_Location ON PrintTemplateElement(LocationId, SortOrder);
            """,
            connection);
        await createCommand.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Same reasoning as EnsureConsentTableAsync, for the GuestbookVideo table added
    /// after this check was originally written.</summary>
    private static async Task EnsureGuestbookVideoTableAsync(SqlConnection connection, CancellationToken ct)
    {
        using (var checkCommand = new SqlCommand("SELECT 1 FROM sys.tables WHERE name = 'GuestbookVideo';", connection))
        {
            if (await checkCommand.ExecuteScalarAsync(ct) is not null)
            {
                return;
            }
        }

        using var createCommand = new SqlCommand(
            """
            CREATE TABLE GuestbookVideo (
                GuestbookVideoId INT IDENTITY(1,1) PRIMARY KEY,
                SessionId        INT             NOT NULL REFERENCES Session(SessionId),
                FilePath         NVARCHAR(500)   NOT NULL,
                DurationSeconds  INT             NOT NULL,
                RecordedAt       DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME()
            );
            CREATE INDEX IX_GuestbookVideo_Session ON GuestbookVideo(SessionId);
            """,
            connection);
        await createCommand.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Same reasoning as EnsureBoothSettingsColumnsAsync, for the five
    /// brand-identity columns added to Location after this check was originally
    /// written.</summary>
    private static async Task EnsureBoothThemeColumnsAsync(SqlConnection connection, CancellationToken ct)
    {
        using var command = new SqlCommand(
            """
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'AccentColorHex')
                ALTER TABLE Location ADD AccentColorHex NVARCHAR(9) NOT NULL DEFAULT '#365C58';
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'CanvasColorHex')
                ALTER TABLE Location ADD CanvasColorHex NVARCHAR(9) NOT NULL DEFAULT '#F4F3F0';
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'InkColorHex')
                ALTER TABLE Location ADD InkColorHex NVARCHAR(9) NOT NULL DEFAULT '#202124';
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'LogoImagePath')
                ALTER TABLE Location ADD LogoImagePath NVARCHAR(500) NULL;
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'EventName')
                ALTER TABLE Location ADD EventName NVARCHAR(100) NOT NULL DEFAULT 'Focus & Snap';
            """,
            connection);
        await command.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Same reasoning as EnsureConsentTableAsync, for the Feedback table added
    /// after this check was originally written.</summary>
    private static async Task EnsureFeedbackTableAsync(SqlConnection connection, CancellationToken ct)
    {
        using (var checkCommand = new SqlCommand("SELECT 1 FROM sys.tables WHERE name = 'Feedback';", connection))
        {
            if (await checkCommand.ExecuteScalarAsync(ct) is not null)
            {
                return;
            }
        }

        using var createCommand = new SqlCommand(
            """
            CREATE TABLE Feedback (
                FeedbackId      INT IDENTITY(1,1) PRIMARY KEY,
                SessionId       INT             NOT NULL REFERENCES Session(SessionId),
                Rating          INT             NULL CHECK (Rating BETWEEN 1 AND 5),
                Comment         NVARCHAR(1000)  NULL,
                RecordedAt      DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME()
            );
            CREATE INDEX IX_Feedback_Session ON Feedback(SessionId);
            """,
            connection);
        await createCommand.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Same reasoning as EnsureBoothSettingsColumnsAsync, for the four
    /// print-template columns added to Location after this check was originally
    /// written.</summary>
    private static async Task EnsurePrintTemplateColumnsAsync(SqlConnection connection, CancellationToken ct)
    {
        using var command = new SqlCommand(
            """
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'PrintLayout')
                ALTER TABLE Location ADD PrintLayout NVARCHAR(20) NOT NULL DEFAULT 'Single' CHECK (PrintLayout IN ('Single', 'Strip'));
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'PrintWidthInches')
                ALTER TABLE Location ADD PrintWidthInches DECIMAL(5,2) NOT NULL DEFAULT 4;
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'PrintHeightInches')
                ALTER TABLE Location ADD PrintHeightInches DECIMAL(5,2) NOT NULL DEFAULT 6;
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'PrintStripCopies')
                ALTER TABLE Location ADD PrintStripCopies INT NOT NULL DEFAULT 1;
            """,
            connection);
        await command.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Same reasoning as EnsureConsentTableAsync, for the Frame table added
    /// after this check was originally written.</summary>
    private static async Task EnsureFrameTableAsync(SqlConnection connection, CancellationToken ct)
    {
        using (var checkCommand = new SqlCommand("SELECT 1 FROM sys.tables WHERE name = 'Frame';", connection))
        {
            if (await checkCommand.ExecuteScalarAsync(ct) is not null)
            {
                return;
            }
        }

        using var createCommand = new SqlCommand(
            """
            CREATE TABLE Frame (
                FrameId         INT IDENTITY(1,1) PRIMARY KEY,
                LocationId      INT             NOT NULL REFERENCES Location(LocationId),
                Name            NVARCHAR(100)   NOT NULL,
                ImagePath       NVARCHAR(500)   NOT NULL,
                SortOrder       INT             NOT NULL DEFAULT 0,
                IsActive        BIT             NOT NULL DEFAULT 1,
                CreatedAt       DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME()
            );
            CREATE INDEX IX_Frame_Location_Active ON Frame(LocationId, IsActive, SortOrder);
            """,
            connection);
        await createCommand.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Same reasoning as EnsureConsentTableAsync, for the two admin-settings
    /// columns added to Location after this check was originally written.</summary>
    private static async Task EnsureBoothSettingsColumnsAsync(SqlConnection connection, CancellationToken ct)
    {
        using var command = new SqlCommand(
            """
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'CountdownSeconds')
                ALTER TABLE Location ADD CountdownSeconds INT NOT NULL DEFAULT 3;
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'GlamFilterEnabled')
                ALTER TABLE Location ADD GlamFilterEnabled BIT NOT NULL DEFAULT 0;
            """,
            connection);
        await command.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Same reasoning as EnsureConsentTableAsync, for the AdminPin column
    /// added to Location after this check was originally written -- gates
    /// MainWindow's Setup/launch screen (see BoothState.Setup).</summary>
    private static async Task EnsureAdminPinColumnAsync(SqlConnection connection, CancellationToken ct)
    {
        using var command = new SqlCommand(
            """
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'AdminPin')
                ALTER TABLE Location ADD AdminPin NVARCHAR(20) NOT NULL DEFAULT '1234';
            """,
            connection);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task EnsureConsentTableAsync(SqlConnection connection, CancellationToken ct)
    {
        using (var checkCommand = new SqlCommand("SELECT 1 FROM sys.tables WHERE name = 'Consent';", connection))
        {
            if (await checkCommand.ExecuteScalarAsync(ct) is not null)
            {
                return;
            }
        }

        using var createCommand = new SqlCommand(
            """
            CREATE TABLE Consent (
                ConsentId           INT IDENTITY(1,1) PRIMARY KEY,
                SessionId           INT             NOT NULL REFERENCES Session(SessionId),
                DisclaimerAccepted  BIT             NOT NULL,
                EmailOptIn          BIT             NOT NULL DEFAULT 0,
                Email               NVARCHAR(255)   NULL,
                RecordedAt          DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME()
            );
            CREATE INDEX IX_Consent_Session ON Consent(SessionId);
            """,
            connection);
        await createCommand.ExecuteNonQueryAsync(ct);
    }

    private static async Task<SeedIds> EnsureSeedDataAsync(CancellationToken ct)
    {
        var locations = new LocationRepository();
        var printers = new PrinterRepository();
        var bookings = new BookingRepository();

        var existing = await locations.GetAllAsync(ct);
        if (existing.Count > 0)
        {
            var existingLocation = existing[0];
            var existingPrinters = await printers.GetByLocationAsync(existingLocation.LocationId, ct);
            return new SeedIds(existingLocation.LocationId, existingPrinters[0].PrinterId, existingLocation.Type);
        }

        const string locationType = "event";
        int locationId = await locations.InsertAsync("Focus & Snap Studio", locationType, null, ct);
        int printerId = await printers.InsertAsync(locationId, "Canon Selphy CP1500", null, ct);

        await bookings.InsertAsync(locationId, "Sample Client A", DateTime.UtcNow.Date.AddDays(7), "Standard", 8000m, ct);
        await bookings.InsertAsync(locationId, "Sample Client B", DateTime.UtcNow.Date.AddDays(14), "Premium", 12000m, ct);

        // Seeds one reading so the admin dashboard's low-inventory alert has
        // something to evaluate on first run, same reasoning as the Location/
        // Printer/Booking seeds above -- real usage will log its own rows
        // over time once something writes to InventoryLog on print.
        await new InventoryLogRepository().InsertAsync(printerId, "paper", 100, ct);

        return new SeedIds(locationId, printerId, locationType);
    }
}
