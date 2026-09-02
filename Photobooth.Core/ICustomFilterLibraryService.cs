namespace Photobooth.Core;

/// <summary>An admin-uploaded custom .CUBE LUT filter a guest can pick during
/// FilterPicker, alongside the built-in PhotoFilterPreset tiles -- see
/// FilterOption.CustomFilterId. CubeFilePath points at the .cube file copied
/// into Assets/CustomFilters by the admin "Add Custom Filter" dialog.</summary>
public record CustomFilterOption(int CustomFilterId, string Name, string CubeFilePath);

/// <summary>
/// Abstracts reading the booth's currently-active custom LUT filters. Same
/// interface-plus-mock seam as IFrameLibraryService -- BoothStateMachine reads
/// this fresh at the start of every session, so a filter an admin just added
/// or retired takes effect for the very next guest.
/// </summary>
public interface ICustomFilterLibraryService
{
    Task<IReadOnlyList<CustomFilterOption>> GetActiveCustomFiltersAsync(CancellationToken ct = default);
}
