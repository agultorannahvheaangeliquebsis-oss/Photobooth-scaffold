namespace Photobooth.Core;

/// <summary>
/// What PrintCompositor needs to render a QrCode/SessionData element,
/// beyond the photo(s) and the template itself. Everything is optional --
/// a null PhotoUrl means a QrCode element draws nothing (same
/// "feature invisible until configured" reasoning every other
/// not-set-up-yet gap in this codebase already uses) rather than throwing.
/// </summary>
public record PrintRenderContext(Uri? PhotoUrl = null, string? EventName = null, DateTime? PrintedAt = null);
