using System.Reflection;
using DbUp;
using Serilog;

namespace Photobooth.Data;

/// <summary>
/// Gets a freshly-provisioned database from nothing to ready-to-run: creates
/// it if it doesn't exist, applies every not-yet-applied script under
/// Migrations/ (via DbUp -- see that folder's scripts for the schema
/// history), and seeds a Location, a Printer, and a couple Booking rows so
/// the FK chain Session/Print depend on isn't empty on first run. Idempotent
/// -- safe to call on every app startup.
/// </summary>
public static class DatabaseInitializer
{
    public record SeedIds(int LocationId, int PrinterId, string LocationType);

    public static async Task<SeedIds> InitializeAsync(CancellationToken ct = default)
    {
        string connectionString = SqlConnectionFactory.ConnectionString;

        EnsureDatabase.For.SqlDatabase(connectionString);
        ApplyMigrations(connectionString);

        return await EnsureSeedDataAsync(ct);
    }

    /// <summary>Synchronous -- DbUp's engine doesn't expose an async entry
    /// point, same as the ADO.NET commands this replaced were themselves
    /// synchronous over a blocking network call either way.</summary>
    private static void ApplyMigrations(string connectionString)
    {
        var upgrader =
            DeployChanges.To
                .SqlDatabase(connectionString)
                .WithScriptsEmbeddedInAssembly(Assembly.GetExecutingAssembly())
                .WithTransactionPerScript()
                .LogTo(new SerilogDbUpLogger())
                .Build();

        var result = upgrader.PerformUpgrade();
        if (!result.Successful)
        {
            Log.Error(result.Error, "Database migration failed on script {Script}", result.ErrorScript?.Name);
            throw new InvalidOperationException(
                $"Database migration failed on script '{result.ErrorScript?.Name}': {result.Error?.Message}", result.Error);
        }
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
