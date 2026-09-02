namespace Photobooth.Core;

/// <summary>
/// Abstracts reading the booth's favorited print-layout templates -- same
/// interface-plus-mock seam as IFrameLibraryService, which this replaces as
/// the source of guest-facing "frame/layout" choices (see BUILD_PLAN's Print
/// Layout / Frame Selection rework). BoothStateMachine reads this fresh at the
/// start of every session (not cached), so a template an admin just favorited
/// or unfavorited takes effect for the very next guest.
/// </summary>
public interface IPrintTemplateLibraryService
{
    Task<IReadOnlyList<PrintTemplate>> GetFavoriteTemplatesAsync(CancellationToken ct = default);
}
