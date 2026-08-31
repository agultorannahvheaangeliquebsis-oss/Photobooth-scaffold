namespace Photobooth.Core;

/// <summary>
/// Composites a burst of still captures into one animated file -- backs
/// dslrBooth-parity GIF and Boomerang capture modes (see
/// BUILD_PLAN.md's "dslrBooth feature-parity plan", Phase 2). Same
/// interface-plus-mock seam as camera/printer/branding -- keeps
/// BoothStateMachine, Photobooth.Tests, and Photobooth.ConsoleDemo
/// decoupled from whatever GDI+/imaging work the real implementation needs.
/// </summary>
public interface IGifComposerService
{
    /// <summary>Composites the given frames (in capture order) into one animated file
    /// and returns its path. When <paramref name="reversed"/> is true (Boomerang mode),
    /// the frames play forward then backward in a loop rather than just forward
    /// (GIF mode).</summary>
    Task<string> ComposeAsync(IReadOnlyList<string> framePaths, bool reversed, int frameDelayMs, CancellationToken ct = default);
}
