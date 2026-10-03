using TechBazar.Application.Email;
using TechBazar.Application.Payments;
using TechBazar.Domain.Enums;

namespace TechBazar.IntegrationTests.Support;

public sealed class CapturingEmailSender : IEmailSender
{
    private readonly List<EmailMessage> _sent = [];
    public IReadOnlyList<EmailMessage> Sent { get { lock (_sent) return [.. _sent]; } }
    public Task SendAsync(EmailMessage message, CancellationToken ct = default) { lock (_sent) _sent.Add(message); return Task.CompletedTask; }
}

/// <summary>Replaces the SSLCommerz gateway in API tests: authenticity is signalled by sig=ok (the real one is covered by its own tests).</summary>
public sealed class FakeOnlineGateway : IPaymentGateway
{
    public string Name => "sslcommerz";
    public bool IsConfigured => true;
    public bool Supports(PaymentMethod method) => method == PaymentMethod.Online;

    public Task<PaymentInitResult> InitiateAsync(PaymentInitRequest r, CancellationToken ct = default) =>
        Task.FromResult(new PaymentInitResult(true, "https://sandbox.gateway.test/pay/" + r.TransactionId, null));

    public Task<PaymentVerification> VerifyCallbackAsync(CallbackKind kind, IReadOnlyDictionary<string, string> p, CancellationToken ct = default)
    {
        var tran = p.GetValueOrDefault("tran_id");
        if (string.IsNullOrEmpty(tran)) return Task.FromResult(new PaymentVerification(false, PaymentOutcome.Failed, null, null, null, null, "no tran_id"));
        var status = p.GetValueOrDefault("status") ?? "";
        if (kind == CallbackKind.Cancel) return Task.FromResult(new PaymentVerification(true, PaymentOutcome.Cancelled, tran, null, null, null, "cancelled"));
        if (kind == CallbackKind.Fail || status != "VALID") return Task.FromResult(new PaymentVerification(true, PaymentOutcome.Failed, tran, null, null, null, status));
        if (p.GetValueOrDefault("sig") != "ok") return Task.FromResult(new PaymentVerification(false, PaymentOutcome.Failed, tran, null, null, null, "bad signature"));
        return Task.FromResult(new PaymentVerification(true, PaymentOutcome.Paid, tran,
            decimal.Parse(p.GetValueOrDefault("amount") ?? "0", System.Globalization.CultureInfo.InvariantCulture), p.GetValueOrDefault("currency", "BDT"), "BANK-IT", null));
    }
}
