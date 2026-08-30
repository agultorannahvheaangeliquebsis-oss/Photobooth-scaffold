namespace Photobooth.Core;

/// <summary>
/// Composites the guest's chosen frame overlay onto their photo. Same
/// interface-plus-mock seam as IPhotoBrandingService/IPhotoFilterService --
/// keeps BoothStateMachine, Photobooth.Tests, and Photobooth.ConsoleDemo
/// decoupled from System.Drawing.Common, which is Windows-only.
/// </summary>
public interface IFrameOverlayService
{
    /// <summary>Composites frameImagePath (a transparent-PNG overlay) onto photoPath and returns the path to the framed file (the original is left untouched).</summary>
    Task<string> ApplyFrameAsync(string photoPath, string frameImagePath, CancellationToken ct = default);
}
