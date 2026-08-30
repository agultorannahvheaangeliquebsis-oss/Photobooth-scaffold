using Microsoft.Data.SqlClient;

namespace Photobooth.Data;

public class InventoryLogRepository
{
    public async Task InsertAsync(int printerId, string itemType, int quantityRemaining, CancellationToken ct = default)
    {
        using var connection = await SqlConnectionFactory.OpenAsync(ct);
        using var command = new SqlCommand(
            "INSERT INTO InventoryLog (PrinterId, ItemType, QuantityRemaining) VALUES (@PrinterId, @ItemType, @QuantityRemaining);",
            connection);
        command.Parameters.AddWithValue("@PrinterId", printerId);
        command.Parameters.AddWithValue("@ItemType", itemType);
        command.Parameters.AddWithValue("@QuantityRemaining", quantityRemaining);
        await command.ExecuteNonQueryAsync(ct);
    }
}
