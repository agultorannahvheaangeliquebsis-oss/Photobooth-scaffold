using Microsoft.Data.SqlClient;

namespace Photobooth.Data;

public record BookingRecord(int BookingId, int LocationId, string ClientName, DateTime EventDate, string PackageType, decimal Price, string Status);

public class BookingRepository
{
    public async Task<int> InsertAsync(int locationId, string clientName, DateTime eventDate, string packageType, decimal price, CancellationToken ct = default)
    {
        using var connection = await SqlConnectionFactory.OpenAsync(ct);
        using var command = new SqlCommand(
            """
            INSERT INTO Booking (LocationId, ClientName, EventDate, PackageType, Price)
            OUTPUT INSERTED.BookingId
            VALUES (@LocationId, @ClientName, @EventDate, @PackageType, @Price);
            """,
            connection);
        command.Parameters.AddWithValue("@LocationId", locationId);
        command.Parameters.AddWithValue("@ClientName", clientName);
        command.Parameters.AddWithValue("@EventDate", eventDate.Date);
        command.Parameters.AddWithValue("@PackageType", packageType);
        command.Parameters.AddWithValue("@Price", price);

        return (int)(await command.ExecuteScalarAsync(ct))!;
    }

    public async Task<List<BookingRecord>> GetByLocationAsync(int locationId, CancellationToken ct = default)
    {
        using var connection = await SqlConnectionFactory.OpenAsync(ct);
        using var command = new SqlCommand(
            "SELECT BookingId, LocationId, ClientName, EventDate, PackageType, Price, Status FROM Booking WHERE LocationId = @LocationId ORDER BY EventDate;",
            connection);
        command.Parameters.AddWithValue("@LocationId", locationId);
        using var reader = await command.ExecuteReaderAsync(ct);

        var results = new List<BookingRecord>();
        while (await reader.ReadAsync(ct))
        {
            results.Add(new BookingRecord(
                reader.GetInt32(0),
                reader.GetInt32(1),
                reader.GetString(2),
                reader.GetDateTime(3),
                reader.GetString(4),
                reader.GetDecimal(5),
                reader.GetString(6)));
        }
        return results;
    }
}
