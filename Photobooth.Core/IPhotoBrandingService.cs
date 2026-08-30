namespace Photobooth.Core;

/// <summary>
/// Stamps a branded caption onto a captured photo before it's printed or
/// uploaded -- every commercial booth (LumaBooth, dslrBooth) brands photos
/// this way. Same interface-plus-mock seam as camera/printer -- keeps
/// BoothStateMachine, Photobooth.Tests, and Photobooth.ConsoleDemo
/// decoupled from System.Drawing.Common, which is Windows-only.
/// </summary>
public interface IPhotoBrandingService
{
    /// <summary>Composites branding onto the photo and returns the path to the branded file (the original is left untouched).</summary>
    Task<string> ApplyBrandingAsync(string photoPath, CancellationToken ct = default);
}
