using System.Text.RegularExpressions;

namespace Photobooth.Core;

/// <summary>
/// Admin-editable brand identity: accent/canvas/ink colors, an optional
/// logo image, and the event/studio name shown in place of the hardcoded
/// "Focus &amp; Snap" text. Read fresh every session as part of BoothSettings,
/// same as CountdownSeconds/PrintTemplate -- an admin's save takes effect at
/// the next Idle screen, not the app's next restart. See AdminWindow's Theme
/// section.
/// </summary>
public record BoothTheme(string AccentColorHex, string CanvasColorHex, string InkColorHex, string? LogoImagePath, string EventName)
{
    public static readonly BoothTheme Default = new("#365C58", "#F4F3F0", "#202124", null, "Focus & Snap");

    public bool IsValid =>
        IsValidHex(AccentColorHex) && IsValidHex(CanvasColorHex) && IsValidHex(InkColorHex)
        && !string.IsNullOrWhiteSpace(EventName);

    private static bool IsValidHex(string s) =>
        !string.IsNullOrWhiteSpace(s) && Regex.IsMatch(s, "^#[0-9A-Fa-f]{6}$");
}
