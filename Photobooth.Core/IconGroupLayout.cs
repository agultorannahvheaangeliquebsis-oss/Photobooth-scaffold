namespace Photobooth.Core;

/// <summary>
/// Free-position layout for a draggable icon/button group on a guest-facing
/// screen -- one shared shape for ScreenSettings' WelcomeIcons* (the Photo/
/// GIF/Boomerang/Video mode tiles), SharingIcons* (QR/Email/SMS/Print), and
/// CaptureCancelButton* (just the Cancel button) properties below, so
/// ScreenTemplateEditorWindow's drag/layout/align code (see IconGroup_MouseMove
/// et al.) and KioskWindow's rendering both work the same way for all three
/// groups instead of three near-duplicate implementations.
///
/// PositionXPercent/PositionYPercent anchor the group's top-left corner as a
/// percent of the screen, same 0-1-of-canvas convention
/// ScreenTemplateElement.XPercent/YPercent already use, so it scales the
/// same way across window sizes/resolutions. Layout/Alignment only matter for
/// a multi-item group (Welcome/Sharing); a single-item group (Capture's
/// Cancel button) leaves them at the default and never shows the controls
/// that would edit them.
///
/// Not itself persisted as a single column -- each screen's ScreenSettings
/// properties (WelcomeIconsPositionXPercent, etc.) are the real storage;
/// this is the in-memory shape the editor and KioskWindow both build from /
/// decompose into those flat properties, matching the "flat columns, not a
/// nested blob" convention every other ScreenSettings field already follows.
/// </summary>
public record IconGroupLayout(
    double PositionXPercent,
    double PositionYPercent,
    string Layout = IconGroupLayout.RowLayout,
    string Alignment = IconGroupLayout.CenterAlignment)
{
    public const string RowLayout = "Row";
    public const string ColumnLayout = "Column";

    public const string StartAlignment = "Start";
    public const string CenterAlignment = "Center";
    public const string EndAlignment = "End";
}
