namespace Photobooth.Core;

/// <summary>
/// The built-in filters guests can choose from during FilterPicker, see
/// dslrBooth's own Filters screen. A fixed enum, not an admin-editable table
/// like Frame -- these are baked-in GDI+ color-grading recipes (see
/// GdiFilterPresetService), not files an admin uploads, so there's nothing
/// per-preset to store beyond "is it enabled" (EffectsSettings.EnabledFilterPresetIds).
/// Custom .CUBE LUT uploads (dslrBooth's "+ Add filter") aren't built --
/// separate, much larger feature (real LUT parsing/interpolation).
/// </summary>
public enum PhotoFilterPreset
{
    Original,
    BlackAndWhiteGlam,
    BlackAndWhite,
    Filter1977,
    Brannan,
    Gotham,
    Hefe,
    LordKelvin,
    Nashville,
}

/// <summary>Display names and the full/default preset list, shared by the admin
/// Filter library screen and the guest-facing FilterPicker screen so both read
/// off one source of truth.</summary>
public static class PhotoFilterPresets
{
    /// <summary>Every built-in preset, in the order shown on both the admin
    /// library grid and the guest FilterPicker screen -- Original first, same
    /// "no filter is always the first, obvious option" convention dslrBooth's
    /// own Filters screen uses.</summary>
    public static readonly IReadOnlyList<PhotoFilterPreset> All =
    [
        PhotoFilterPreset.Original,
        PhotoFilterPreset.BlackAndWhiteGlam,
        PhotoFilterPreset.BlackAndWhite,
        PhotoFilterPreset.Filter1977,
        PhotoFilterPreset.Brannan,
        PhotoFilterPreset.Gotham,
        PhotoFilterPreset.Hefe,
        PhotoFilterPreset.LordKelvin,
        PhotoFilterPreset.Nashville,
    ];

    /// <summary>Every preset enabled -- EffectsSettings.EnabledFilterPresetIds'
    /// default, so a fresh booth shows the full grid rather than none.</summary>
    public static readonly string DefaultEnabledIds = string.Join(',', All);

    public static string DisplayName(PhotoFilterPreset preset) => preset switch
    {
        PhotoFilterPreset.Original => "Original",
        PhotoFilterPreset.BlackAndWhiteGlam => "Black & White Glam",
        PhotoFilterPreset.BlackAndWhite => "Black And White",
        PhotoFilterPreset.Filter1977 => "1977",
        PhotoFilterPreset.Brannan => "Brannan",
        PhotoFilterPreset.Gotham => "Gotham",
        PhotoFilterPreset.Hefe => "Hefe",
        PhotoFilterPreset.LordKelvin => "Lord Kelvin",
        PhotoFilterPreset.Nashville => "Nashville",
        _ => preset.ToString(),
    };

    /// <summary>Parses a comma-separated EffectsSettings.EnabledFilterPresetIds
    /// string back into preset values, in All's canonical order -- ignores any
    /// name it doesn't recognize rather than throwing (forward-compatible with
    /// a stored value from a future build that added a preset this one doesn't
    /// know about yet).</summary>
    public static List<PhotoFilterPreset> Parse(string enabledFilterPresetIds)
    {
        var enabled = new HashSet<string>(
            enabledFilterPresetIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return All.Where(p => enabled.Contains(p.ToString())).ToList();
    }
}
