using System.Windows;

namespace Photobooth.UI;

/// <summary>Opens the shared HSV color-picker window so the design/editor
/// pages' color swatches aren't limited to typing a hex code by hand.</summary>
internal static class ColorPickerHelper
{
    /// <summary>Shows the picker seeded with <paramref name="currentHex"/>,
    /// owned by <paramref name="owner"/>, and returns the chosen color as
    /// "#RRGGBB", or null if the user cancelled.</summary>
    public static string? PickColorHex(Window owner, string? currentHex) =>
        ColorPickerWindow.PickColor(owner, currentHex);
}
