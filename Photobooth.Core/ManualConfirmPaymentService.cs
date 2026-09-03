using System.Collections.Concurrent;

namespace Photobooth.Core;

/// <summary>
/// Real IPaymentService for a booth with no registered business yet (no TIN,
/// so no PayMongo -- or any other aggregator's -- live keys, since BSP AML
/// rules require KYB-verified business identity before any of them will
/// settle real money to an account). Guests pay the attendant directly (cash,
/// or a scan of the attendant's own *personal* GCash/Maya QR -- not this
/// booth's), and the attendant taps ConfirmPayment/DeclinePayment on the
/// booth itself once they've actually received it. Requires a staffed
/// session -- there's no one to tap Confirm on a truly unattended vendo
/// placement, same limitation a real card reader with no attendant would
/// have.
///
/// KioskViewModel holds the same instance BoothCompositionRoot constructed
/// (passed through RealBooth, same pattern as UiFrameSelectionService/
/// UiFeedbackService/etc.) so it can call ConfirmPayment/DeclinePayment
/// directly from the two buttons the Payment screen shows whenever
/// SharingSettings.PaymentProvider is "Manual".
/// </summary>
public class ManualConfirmPaymentService : IPaymentService
{
    private readonly ConcurrentDictionary<string, TaskCompletionSource<bool>> _pendingByReference = new();

    public Task<PaymentPrompt> InitiateAsync(decimal amount, string reference, CancellationToken ct = default)
    {
        _pendingByReference[reference] = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        return Task.FromResult(new PaymentPrompt(
            $"Collect ₱{amount:0.00} in cash, or have the guest scan your GCash/Maya QR, then tap Payment Received below.",
            QrCodePng: null));
    }

    /// <summary>Whether InitiateAsync started a manual attempt for this
    /// reference that's still waiting on the attendant -- lets
    /// GatewayPaymentService route WaitForConfirmationAsync to this instance
    /// instead of re-reading (and matching against) SharingSettings.PaymentProvider
    /// a second time, which could theoretically have changed between the two
    /// calls.</summary>
    public bool HasPending(string reference) => _pendingByReference.ContainsKey(reference);

    public async Task<PaymentResult> WaitForConfirmationAsync(string reference, decimal amount, CancellationToken ct = default)
    {
        if (!_pendingByReference.TryGetValue(reference, out TaskCompletionSource<bool>? tcs))
        {
            // InitiateAsync was never called for this reference (shouldn't
            // happen through BoothStateMachine's own call pattern, but fail
            // closed rather than hang forever).
            return new PaymentResult(false, "manual", null);
        }

        try
        {
            using CancellationTokenRegistration registration = ct.Register(() => tcs.TrySetCanceled(ct));
            bool approved = await tcs.Task;
            return new PaymentResult(approved, "manual", approved ? reference : null);
        }
        finally
        {
            _pendingByReference.TryRemove(reference, out _);
        }
    }

    /// <summary>Attendant tapped "Payment Received" -- unblocks
    /// WaitForConfirmationAsync for this reference with success. A no-op if
    /// this reference already resolved or was never initiated (e.g. a stray
    /// tap after the guest's session moved on).</summary>
    public void ConfirmPayment(string? reference)
    {
        if (reference is not null && _pendingByReference.TryGetValue(reference, out TaskCompletionSource<bool>? tcs))
        {
            tcs.TrySetResult(true);
        }
    }

    /// <summary>Attendant tapped "Declined/Cancel" (guest changed their mind,
    /// walked away, or paid the wrong amount) -- unblocks
    /// WaitForConfirmationAsync for this reference with failure, same
    /// guest-facing outcome as a card decline or a guest-idle timeout.</summary>
    public void DeclinePayment(string? reference)
    {
        if (reference is not null && _pendingByReference.TryGetValue(reference, out TaskCompletionSource<bool>? tcs))
        {
            tcs.TrySetResult(false);
        }
    }
}
