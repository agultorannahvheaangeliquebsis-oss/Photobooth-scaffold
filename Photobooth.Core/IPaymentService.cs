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
}
