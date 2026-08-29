namespace Photobooth.Core;

/// <summary>
/// Drives a single guest session end to end. This is the piece your WPF UI
/// binds to: subscribe to StateChanged to swap screens, CountdownTick to
/// update the on-screen number, and ErrorOccurred to show a friendly message.
///
/// Camera and printer are injected as interfaces (not created here), so the
/// same state machine runs identically whether it's driving MockCameraService
/// during development or a real PTP-backed service driving the Nikon D3500
/// at an actual event.
/// </summary>
public class BoothStateMachine
{
    private readonly ICameraService _camera;
    private readonly IPrinterService _printer;
    private readonly ICloudUploadService _cloudUpload;
    private readonly ISessionRepository _sessions;

    public BoothState CurrentState { get; private set; } = BoothState.Idle;
    public string? LastCapturedImagePath { get; private set; }
    public Uri? LastPhotoUrl { get; private set; }

    public event Action<BoothState>? StateChanged;
    public event Action<int>? CountdownTick;
    public event Action<string>? ErrorOccurred;

    /// <summary>Fires when the background upload for the current session's photo finishes -- may land during Reviewing, Printing, or Complete, whichever is showing when the network call happens to finish.</summary>
    public event Action<Uri>? PhotoUploaded;

    public BoothStateMachine(ICameraService camera, IPrinterService printer, ICloudUploadService cloudUpload, ISessionRepository sessions)
    {
        _camera = camera;
        _printer = printer;
        _cloudUpload = cloudUpload;
        _sessions = sessions;
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
        // No vendo payment flow yet (that's Day 6), so every session today
        // is event mode; recorded as a zero-amount 'free_event' Payment
        // rather than skipping the Payment row, so the revenue-by-mode query
        // the admin dashboard will eventually run doesn't have to special-case
        // sessions with no Payment at all.
        const string mode = "event";
        int? sessionId = null;

        try
        {
            sessionId = await _sessions.CreateAsync(mode, ct);

            SetState(BoothState.Countdown);
            for (int i = 3; i > 0; i--)
            {
                CountdownTick?.Invoke(i);
                await Task.Delay(1000, ct);
            }

            SetState(BoothState.Capturing);
            LastPhotoUrl = null;
            LastCapturedImagePath = await _camera.CaptureAsync(ct);

            // Fire-and-forget: upload runs alongside Reviewing/Printing rather
            // than blocking the guest flow on network latency. A failed or
            // slow upload just means no QR code shows this session -- it
            // never holds up the print, which is the part that actually
            // matters to the guest standing at the booth.
            _ = UploadInBackgroundAsync(LastCapturedImagePath, ct);

            SetState(BoothState.Reviewing);
            await Task.Delay(2000, ct); // guest sees the shot before it prints

            SetState(BoothState.Printing);
            await _printer.PrintAsync(LastCapturedImagePath, ct);
            await _sessions.RecordPrintAsync(sessionId.Value, LastCapturedImagePath, ct);
            await _sessions.RecordPaymentAsync(sessionId.Value, 0m, "free_event", ct);

            SetState(BoothState.Complete);
            await _sessions.CompleteAsync(sessionId.Value, ct);
            await Task.Delay(1500, ct); // "thank you" screen dwell time
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(ex.Message);
            SetState(BoothState.Error);
            if (sessionId.HasValue)
            {
                await _sessions.FailAsync(sessionId.Value, ct);
            }
            await Task.Delay(3000, ct); // show the error briefly before resetting
        }
        finally
        {
            SetState(BoothState.Idle);
        }
    }

    private async Task UploadInBackgroundAsync(string imagePath, CancellationToken ct)
    {
        try
        {
            LastPhotoUrl = await _cloudUpload.UploadAsync(imagePath, ct);
            PhotoUploaded?.Invoke(LastPhotoUrl);
        }
        catch (Exception)
        {
            // Best-effort: swallow so an upload failure can't surface as an
            // unobserved task exception -- it's not on the guest-facing
            // error path, just a missing QR code for this session.
        }
    }
}
