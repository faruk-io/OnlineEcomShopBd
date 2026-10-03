using TechBazar.Application.Payments;
using TechBazar.Domain.Enums;

namespace TechBazar.Infrastructure.Payments;

/// <summary>No money moves online: the payment attempt stays Pending and is settled when the order is delivered / collected.</summary>
public sealed class CashOnDeliveryGateway : IPaymentGateway
{
    public string Name => PaymentService.CodGateway;
    public bool IsConfigured => true;
    public bool Supports(PaymentMethod method) => method == PaymentMethod.CashOnDelivery;

    public Task<PaymentInitResult> InitiateAsync(PaymentInitRequest request, CancellationToken ct = default) =>
        Task.FromResult(new PaymentInitResult(true, null, null));

    public Task<PaymentVerification> VerifyCallbackAsync(CallbackKind kind, IReadOnlyDictionary<string, string> payload, CancellationToken ct = default) =>
        Task.FromResult(new PaymentVerification(false, PaymentOutcome.Failed, null, null, null, null, "Cash on delivery has no gateway callbacks."));
}
