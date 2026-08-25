namespace Photobooth.Core;

/// <summary>
/// Drives a single guest session end to end. This is the piece your WPF UI
/// binds to: subscribe to StateChanged to swap screens, CountdownTick to
/// update the on-screen number, and ErrorOccurred to show a friendly message.
///
/// Camera and printer are injected as interfaces (not created here), so the
/// same state machine runs identically whether it's driving MockCameraService
/// during development or a real EDSDK-backed service at an actual event.
/// </summary>
public class BoothStateMachine
{
    private readonly ICameraService _camera;
    private readonly IPrinterService _printer;

    public BoothState CurrentState { get; private set; } = BoothState.Idle;
    public string? LastCapturedImagePath { get; private set; }

    public event Action<BoothState>? StateChanged;
    public event Action<int>? CountdownTick;
    public event Action<string>? ErrorOccurred;

    public BoothStateMachine(ICameraService camera, IPrinterService printer)
    {
        _camera = camera;
        _printer = printer;
    }

    private void SetState(BoothState state)
    {
        CurrentState = state;
        StateChanged?.Invoke(state);
    }

    /// <summary>
    /// Runs one full guest session: countdown, capture, review, print, then
    /// resets to Idle. Any failure at any step is caught, reported via
    /// ErrorOccurred, and the machine still returns to Idle -- a session
    /// should never leave the booth stuck on a dead screen.
    /// </summary>
    public async Task RunSessionAsync(CancellationToken ct = default)
    {
        try
        {
            SetState(BoothState.Countdown);
            for (int i = 3; i > 0; i--)
            {
                CountdownTick?.Invoke(i);
                await Task.Delay(1000, ct);
            }

            SetState(BoothState.Capturing);
            LastCapturedImagePath = await _camera.CaptureAsync(ct);

            SetState(BoothState.Reviewing);
            await Task.Delay(2000, ct); // guest sees the shot before it prints

            SetState(BoothState.Printing);
            await _printer.PrintAsync(LastCapturedImagePath, ct);

            SetState(BoothState.Complete);
            await Task.Delay(1500, ct); // "thank you" screen dwell time
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(ex.Message);
            SetState(BoothState.Error);
            await Task.Delay(3000, ct); // show the error briefly before resetting
        }
        finally
        {
            SetState(BoothState.Idle);
        }
    }
}
