namespace Photobooth.Core;

/// <summary>What the Payment screen shows the guest right after a payment attempt starts.
/// QrCodePng is null for a gateway with nothing to scan (e.g. a card reader) --
/// this is why the interface can't just be "generate a QR code" (see
/// MockCardReaderPaymentService).</summary>
public record PaymentPrompt(string Instructions, byte[]? QrCodePng);

/// <summary>Result of a completed (or failed) vendo-mode payment attempt.</summary>
public record PaymentResult(bool Success, string Method, string? TransactionRef);

/// <summary>
/// Abstracts collecting payment for a vendo-mode session, before printing.
/// Same interface-plus-mock seam as camera/printer/cloud upload/session
/// repository -- BoothStateMachine only ever talks to this interface. Two
/// mocks exist: MockQrPaymentService (GCash/Maya-style scan-to-pay) and
/// MockCardReaderPaymentService (tap/insert/swipe, no QR at all). A real
/// gateway now exists too -- see GatewayPaymentService, which talks to
/// PayMongo (the aggregator fronting GCash/Maya for this booth) and falls
/// back to MockQrPaymentService when SharingSettings.PaymentProvider isn't
/// "PayMongo" yet or no secret key is on file.
/// </summary>
public interface IPaymentService
{
    /// <summary>Starts a payment attempt and returns what to show the guest.
    /// Async (not just Task-wrapped for the mocks' benefit) because a real
    /// gateway genuinely needs a network round trip here -- creating a
    /// Payment Intent + Payment Method with PayMongo, for instance -- before
    /// there's anything to show the guest.</summary>
    Task<PaymentPrompt> InitiateAsync(decimal amount, string reference, CancellationToken ct = default);

    /// <summary>Waits for the payment started by InitiateAsync to be confirmed, declined, or time out. In production this polls the gateway (see GatewayPaymentService); the mocks just simulate guest confirmation time.</summary>
    Task<PaymentResult> WaitForConfirmationAsync(string reference, decimal amount, CancellationToken ct = default);

    /// <summary>
    /// Abandons the attempt for <paramref name="reference"/>: the guest walked
    /// away and BoothStateMachine has stopped awaiting the confirmation (see
    /// WithGuestIdleTimeoutAsync, which times out but can't itself cancel a
    /// call already in flight).
    ///
    /// A default no-op, because for most implementations there is genuinely
    /// nothing to release -- a polling gateway's loop is already bounded by its
    /// own MaxPollDuration, and the mocks resolve on a timer. It matters for
    /// ManualConfirmPaymentService, which parks a TaskCompletionSource nothing
    /// else will ever complete: without this the entry leaks for the life of
    /// the process and stays confirmable long after its session is over.
    /// Implementations must treat an unknown reference as a no-op.
    /// </summary>
    void CancelPending(string reference)
    {
    }
}
