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
    public record SeedIds(int LocationId, int PrinterId);

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
            if (await checkCommand.ExecuteScalarAsync(ct) is not null)
            {
                return; // schema already applied
            }
        }

        string schemaPath = Path.Combine(AppContext.BaseDirectory, "schema.sql");
        string schema = await File.ReadAllTextAsync(schemaPath, ct);
        using var createCommand = new SqlCommand(schema, connection);
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
            int existingLocationId = existing[0].LocationId;
            var existingPrinters = await printers.GetByLocationAsync(existingLocationId, ct);
            return new SeedIds(existingLocationId, existingPrinters[0].PrinterId);
        }

        int locationId = await locations.InsertAsync("Focus & Snap Studio", "event", null, ct);
        int printerId = await printers.InsertAsync(locationId, "Canon Selphy CP1500", null, ct);

        await bookings.InsertAsync(locationId, "Sample Client A", DateTime.UtcNow.Date.AddDays(7), "Standard", 8000m, ct);
        await bookings.InsertAsync(locationId, "Sample Client B", DateTime.UtcNow.Date.AddDays(14), "Premium", 12000m, ct);

        return new SeedIds(locationId, printerId);
    }
}
