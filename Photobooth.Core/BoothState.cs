namespace Photobooth.Core;

/// <summary>
/// The states a single guest session moves through. The idle screen loops
/// back here after every session, whether it finished normally or errored out.
/// </summary>
public enum BoothState
{
    Idle,
    Countdown,
    Capturing,
    Reviewing,
    Printing,
    Complete,
    Error
}
