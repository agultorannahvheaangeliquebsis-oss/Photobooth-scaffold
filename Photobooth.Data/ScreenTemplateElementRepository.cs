using Microsoft.Data.SqlClient;
using Photobooth.Core;

namespace Photobooth.Data;

/// <summary>Admin-facing CRUD over the ScreenTemplateElement table -- same
/// plain-repository shape as PrintTemplateElementRepository (no interface/mock,
/// since only ScreenTemplateEditorWindow and MainWindow's overlay rendering ever
/// talk to this directly, not BoothStateMachine).</summary>
public class ScreenTemplateElementRepository
{
    /// <summary>Every element for this location, across all three screens --
    /// MainWindow reads this once and groups by Screen locally, same shape
    /// SqlBoothSettingsProvider already reads PrintTemplateElement in one call.</summary>
    public async Task<List<ScreenTemplateElement>> GetAllByLocationAsync(int locationId, CancellationToken ct = default)
    {
        using var connection = await SqlConnectionFactory.OpenAsync(ct);
        using var command = new SqlCommand(
            """
            SELECT Screen, Kind, XPercent, YPercent, WidthPercent, HeightPercent, Text, ImagePath,
                   FontFamily, FontSizePercent, Bold, ColorHex
            FROM ScreenTemplateElement
            WHERE LocationId = @LocationId
            ORDER BY Screen, SortOrder, ElementId;
            """,
            connection);
        command.Parameters.AddWithValue("@LocationId", locationId);

        using var reader = await command.ExecuteReaderAsync(ct);
        var results = new List<ScreenTemplateElement>();
        while (await reader.ReadAsync(ct))
        {
            results.Add(new ScreenTemplateElement(
                Enum.Parse<ScreenTemplateScreen>(reader.GetString(0)),
                Enum.Parse<ScreenTemplateElementKind>(reader.GetString(1)),
                (double)reader.GetDecimal(2),
                (double)reader.GetDecimal(3),
                (double)reader.GetDecimal(4),
                (double)reader.GetDecimal(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.IsDBNull(8) ? "Segoe UI" : reader.GetString(8),
                reader.IsDBNull(9) ? 0.05 : (double)reader.GetDecimal(9),
                !reader.IsDBNull(10) && reader.GetBoolean(10),
                reader.IsDBNull(11) ? "#202124" : reader.GetString(11)));
        }
        return results;
    }

    /// <summary>Replaces every element across all three screens for this location in
    /// one delete-then-reinsert -- ScreenTemplateEditorWindow always saves its whole
    /// working set (all three tabs) at once, same "no per-row update/delete tracking
    /// needed" reasoning PrintTemplateElementRepository.ReplaceAllAsync already uses.</summary>
    public async Task ReplaceAllAsync(int locationId, IReadOnlyList<ScreenTemplateElement> elements, CancellationToken ct = default)
    {
        using var connection = await SqlConnectionFactory.OpenAsync(ct);
        using var transaction = connection.BeginTransaction();

        using (var deleteCommand = new SqlCommand("DELETE FROM ScreenTemplateElement WHERE LocationId = @LocationId;", connection, transaction))
        {
            deleteCommand.Parameters.AddWithValue("@LocationId", locationId);
            await deleteCommand.ExecuteNonQueryAsync(ct);
        }

        var sortOrderByScreen = new Dictionary<ScreenTemplateScreen, int>();
        foreach (ScreenTemplateElement element in elements)
        {
            int sortOrder = sortOrderByScreen.TryGetValue(element.Screen, out int n) ? n : 0;
            sortOrderByScreen[element.Screen] = sortOrder + 1;

            using var insertCommand = new SqlCommand(
                """
                INSERT INTO ScreenTemplateElement
                    (LocationId, Screen, Kind, XPercent, YPercent, WidthPercent, HeightPercent, Text, ImagePath, FontFamily, FontSizePercent, Bold, ColorHex, SortOrder)
                VALUES
                    (@LocationId, @Screen, @Kind, @XPercent, @YPercent, @WidthPercent, @HeightPercent, @Text, @ImagePath, @FontFamily, @FontSizePercent, @Bold, @ColorHex, @SortOrder);
                """,
                connection, transaction);
            insertCommand.Parameters.AddWithValue("@LocationId", locationId);
            insertCommand.Parameters.AddWithValue("@Screen", element.Screen.ToString());
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
            insertCommand.Parameters.AddWithValue("@SortOrder", sortOrder);
            await insertCommand.ExecuteNonQueryAsync(ct);
        }

        await transaction.CommitAsync(ct);
    }
}
