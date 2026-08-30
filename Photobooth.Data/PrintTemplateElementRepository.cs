using Microsoft.Data.SqlClient;
using Photobooth.Core;

namespace Photobooth.Data;

/// <summary>Admin-facing CRUD over the PrintTemplateElement table -- same plain-repository
/// shape as FrameRepository (no interface/mock, since only SqlBoothSettingsProvider and
/// PrintTemplateEditorWindow ever talk to this directly, not BoothStateMachine).</summary>
public class PrintTemplateElementRepository
{
    public async Task<List<PrintTemplateElement>> GetAllByLocationAsync(int locationId, CancellationToken ct = default)
    {
        using var connection = await SqlConnectionFactory.OpenAsync(ct);
        using var command = new SqlCommand(
            """
            SELECT Kind, XPercent, YPercent, WidthPercent, HeightPercent, Text, ImagePath,
                   FontFamily, FontSizePercent, Bold, ColorHex
            FROM PrintTemplateElement
            WHERE LocationId = @LocationId
            ORDER BY SortOrder, ElementId;
            """,
            connection);
        command.Parameters.AddWithValue("@LocationId", locationId);

        using var reader = await command.ExecuteReaderAsync(ct);
        var results = new List<PrintTemplateElement>();
        while (await reader.ReadAsync(ct))
        {
            results.Add(new PrintTemplateElement(
                Enum.Parse<PrintTemplateElementKind>(reader.GetString(0)),
                (double)reader.GetDecimal(1),
                (double)reader.GetDecimal(2),
                (double)reader.GetDecimal(3),
                (double)reader.GetDecimal(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? "Segoe UI" : reader.GetString(7),
                reader.IsDBNull(8) ? 0.05 : (double)reader.GetDecimal(8),
                !reader.IsDBNull(9) && reader.GetBoolean(9),
                reader.IsDBNull(10) ? "#202124" : reader.GetString(10)));
        }
        return results;
    }

    /// <summary>Replaces every element for this location in one delete-then-reinsert --
    /// PrintTemplateEditorWindow always saves its whole working list at once, so there's
    /// no need for per-row update/delete tracking.</summary>
    public async Task ReplaceAllAsync(int locationId, IReadOnlyList<PrintTemplateElement> elements, CancellationToken ct = default)
    {
        using var connection = await SqlConnectionFactory.OpenAsync(ct);
        using var transaction = connection.BeginTransaction();

        using (var deleteCommand = new SqlCommand("DELETE FROM PrintTemplateElement WHERE LocationId = @LocationId;", connection, transaction))
        {
            deleteCommand.Parameters.AddWithValue("@LocationId", locationId);
            await deleteCommand.ExecuteNonQueryAsync(ct);
        }

        for (int i = 0; i < elements.Count; i++)
        {
            PrintTemplateElement element = elements[i];
            using var insertCommand = new SqlCommand(
                """
                INSERT INTO PrintTemplateElement
                    (LocationId, Kind, XPercent, YPercent, WidthPercent, HeightPercent, Text, ImagePath, FontFamily, FontSizePercent, Bold, ColorHex, SortOrder)
                VALUES
                    (@LocationId, @Kind, @XPercent, @YPercent, @WidthPercent, @HeightPercent, @Text, @ImagePath, @FontFamily, @FontSizePercent, @Bold, @ColorHex, @SortOrder);
                """,
                connection, transaction);
            insertCommand.Parameters.AddWithValue("@LocationId", locationId);
            insertCommand.Parameters.AddWithValue("@Kind", element.Kind.ToString());
            insertCommand.Parameters.AddWithValue("@XPercent", element.XPercent);
            insertCommand.Parameters.AddWithValue("@YPercent", element.YPercent);
            insertCommand.Parameters.AddWithValue("@WidthPercent", element.WidthPercent);
            insertCommand.Parameters.AddWithValue("@HeightPercent", element.HeightPercent);
            insertCommand.Parameters.AddWithValue("@Text", (object?)element.Text ?? DBNull.Value);
            insertCommand.Parameters.AddWithValue("@ImagePath", (object?)element.ImagePath ?? DBNull.Value);
            insertCommand.Parameters.AddWithValue("@FontFamily", element.FontFamily);
            insertCommand.Parameters.AddWithValue("@FontSizePercent", element.FontSizePercent);
            insertCommand.Parameters.AddWithValue("@Bold", element.Bold);
            insertCommand.Parameters.AddWithValue("@ColorHex", element.ColorHex);
            insertCommand.Parameters.AddWithValue("@SortOrder", i);
            await insertCommand.ExecuteNonQueryAsync(ct);
        }

        await transaction.CommitAsync(ct);
    }
}
