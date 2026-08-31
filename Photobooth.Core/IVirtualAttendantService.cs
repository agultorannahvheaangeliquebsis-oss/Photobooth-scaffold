namespace Photobooth.Core;

/// <summary>One admin-uploaded audio/video cue played alongside whatever screen is
/// already showing for a given stage -- see IVirtualAttendantService, MainWindow's
/// AttendantCueChanged handler.</summary>
public record AttendantClip(string FilePath, BoothState Stage);

/// <summary>
/// Abstracts picking (or randomizing) a Virtual Attendant clip for a given
/// stage of a guest session. Same interface-plus-mock seam as
/// IFrameLibraryService -- BoothStateMachine calls this once per SetState,
/// best-effort (a missing/misconfigured clip should never disrupt a guest
/// session), and never introduces a new BoothState of its own.
/// </summary>
public interface IVirtualAttendantService
{
    /// <summary>Returns the clip to play for this stage, or null if the Virtual
    /// Attendant is disabled, or no clips are configured for this stage --
    /// same "empty pool = feature invisible" reasoning as Frame/FramePicker.</summary>
    Task<AttendantClip?> GetCueAsync(BoothState state, CancellationToken ct = default);
}
