using TechBazar.Application.Orders;
using TechBazar.Domain.Enums;

namespace TechBazar.Application.Payments;

public enum CallbackKind { Success = 1, Fail = 2, Cancel = 3, Ipn = 4 }

/// <summary>Everything a gateway needs to start a payment. Amount is the SERVER-calculated order total.</summary>
public sealed record PaymentInitRequest(
    string TransactionId, decimal Amount, string Currency, string OrderNumber,
    string CustomerName, string CustomerEmail, string CustomerPhone, string AddressLine, string City, string ProductSummary,
    string SuccessUrl, string FailUrl, string CancelUrl, string IpnUrl);

public sealed record PaymentInitResult(bool Success, string? RedirectUrl, string? Error);

public enum PaymentOutcome { Paid = 1, Failed = 2, Cancelled = 3 }

/// <summary>
/// Result of authenticating a gateway callback. <see cref="IsAuthentic"/> is false when the signature or the
/// gateway's own validation API rejects the message: such callbacks never change any state.
/// </summary>
public sealed record PaymentVerification(
    bool IsAuthentic, PaymentOutcome Outcome, string? TransactionId, decimal? Amount, string? Currency, string? GatewayReference, string? Error);

public interface IPaymentGateway
{
    /// <summary>Stable key used in URLs and stored on <c>Payment.Gateway</c> ("cod", "sslcommerz").</summary>
    string Name { get; }
    /// <summary>False when credentials are missing: the gateway is then hidden from checkout.</summary>
    bool IsConfigured { get; }
    bool Supports(PaymentMethod method);
    Task<PaymentInitResult> InitiateAsync(PaymentInitRequest request, CancellationToken ct = default);
    Task<PaymentVerification> VerifyCallbackAsync(CallbackKind kind, IReadOnlyDictionary<string, string> payload, CancellationToken ct = default);
}

public sealed class PaymentOptions
{
    public const string SectionName = "Payments";
    /// <summary>Publicly reachable API origin used for gateway callbacks (use a tunnel such as ngrok in local development).</summary>
    public string PublicApiBaseUrl { get; set; } = "http://localhost:5080";
    /// <summary>Storefront origin the browser is redirected back to after paying.</summary>
    public string StorefrontBaseUrl { get; set; } = "http://localhost:4200";
}

public sealed record CallbackResult(bool Handled, bool AlreadyProcessed, string? OrderNumber, PaymentAttemptStatus? Status, string Message);

public interface IPaymentService
{
    bool OnlinePaymentsEnabled { get; }
    /// <summary>Adds a Pending payment attempt to the (tracked) order. Nothing is saved; the caller saves with the order.</summary>
    Domain.Entities.Payment CreateAttempt(Domain.Entities.Order order, PaymentMethod method);
    /// <summary>Calls the gateway for a persisted online attempt. On failure the attempt is marked Failed (saved).</summary>
    Task<PaymentRedirectDto> StartOnlinePaymentAsync(string orderNumber, string transactionId, CancellationToken ct = default);
    Task<CallbackResult> HandleCallbackAsync(string gatewayName, CallbackKind kind, IReadOnlyDictionary<string, string> payload, CancellationToken ct = default);
}
