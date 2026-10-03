using TechBazar.Application.Email;
using TechBazar.Application.Payments;
using TechBazar.Domain.Enums;

namespace TechBazar.UnitTests.Support;

public sealed class CapturingEmailSender : IEmailSender
{
    private readonly List<EmailMessage> _sent = [];
    public IReadOnlyList<EmailMessage> Sent { get { lock (_sent) return [.. _sent]; } }
    public bool Throw { get; set; }

    public Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        if (Throw) throw new InvalidOperationException("SMTP down");
        lock (_sent) _sent.Add(message);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Stand-in for the online gateway at the PaymentService boundary. Authenticity is signalled by the "sig" field ("ok" = authentic),
/// mirroring what the real gateway proves with its signature + validation API (which have their own tests).
/// </summary>
public sealed class FakeOnlineGateway : IPaymentGateway
{
    public string Name => "sslcommerz";
    public bool IsConfigured { get; set; } = true;
    public bool InitSucceeds { get; set; } = true;
    public int InitCalls { get; private set; }
    public PaymentInitRequest? LastInit { get; private set; }
    public bool Supports(PaymentMethod method) => method == PaymentMethod.Online;

    public Task<PaymentInitResult> InitiateAsync(PaymentInitRequest request, CancellationToken ct = default)
    {
        InitCalls++;
        LastInit = request;
        return Task.FromResult(InitSucceeds ? new PaymentInitResult(true, "https://sandbox.gateway.test/pay/" + request.TransactionId, null) : new PaymentInitResult(false, null, "declined by test"));
    }

    public Task<PaymentVerification> VerifyCallbackAsync(CallbackKind kind, IReadOnlyDictionary<string, string> p, CancellationToken ct = default)
    {
        var tran = p.GetValueOrDefault("tran_id");
        if (string.IsNullOrEmpty(tran)) return Task.FromResult(new PaymentVerification(false, PaymentOutcome.Failed, null, null, null, null, "no tran_id"));
        var status = p.GetValueOrDefault("status") ?? "";
        if (kind == CallbackKind.Cancel || status == "CANCELLED") return Task.FromResult(new PaymentVerification(true, PaymentOutcome.Cancelled, tran, null, null, null, "cancelled"));
        if (kind == CallbackKind.Fail || status != "VALID") return Task.FromResult(new PaymentVerification(true, PaymentOutcome.Failed, tran, null, null, null, status));
        if (p.GetValueOrDefault("sig") != "ok") return Task.FromResult(new PaymentVerification(false, PaymentOutcome.Failed, tran, null, null, null, "bad signature"));
        return Task.FromResult(new PaymentVerification(true, PaymentOutcome.Paid, tran,
            decimal.Parse(p.GetValueOrDefault("amount") ?? "0", System.Globalization.CultureInfo.InvariantCulture), p.GetValueOrDefault("currency", "BDT"), p.GetValueOrDefault("bank_tran_id", "BANK123"), null));
    }
}
