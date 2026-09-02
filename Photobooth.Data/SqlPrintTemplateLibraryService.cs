using System.Linq;
using Photobooth.Core;

namespace Photobooth.Data;

/// <summary>
/// Real IPrintTemplateLibraryService: reads this location's favorited
/// PrintTemplate library rows (plus each one's own saved elements) fresh on
/// every call -- same reasoning as SqlFrameLibraryService, which this
/// replaces as the source of the guest-facing "frame/layout" picker. A
/// template an admin just favorited/unfavorited/edited takes effect for the
/// very next guest session.
/// </summary>
public class SqlPrintTemplateLibraryService : IPrintTemplateLibraryService
{
    private readonly int _locationId;
    private readonly PrintTemplateRepository _templates = new();
    private readonly PrintTemplateElementRepository _elements = new();

    public SqlPrintTemplateLibraryService(int locationId)
    {
        _locationId = locationId;
    }

    public async Task<IReadOnlyList<PrintTemplate>> GetFavoriteTemplatesAsync(CancellationToken ct = default)
    {
        List<PrintTemplateRecord> records = await _templates.GetAllByLocationAsync(_locationId, ct);
        var favorites = new List<PrintTemplate>();
        foreach (PrintTemplateRecord record in records.Where(r => r.IsFavorite))
        {
            List<PrintTemplateElement> elements = await _elements.GetAllByTemplateAsync(record.PrintTemplateId, ct);
            favorites.Add(new PrintTemplate(record.Layout, record.WidthInches, record.HeightInches, record.StripCopies)
            {
                Id = record.PrintTemplateId,
                Name = record.Name,
                IsFavorite = true,
                Elements = elements,
            });
        }
        return favorites;
    }
}
