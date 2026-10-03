using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TechBazar.Application.Abstractions;
using TechBazar.Application.Common;
using TechBazar.Application.Email;
using TechBazar.Application.Orders;
using TechBazar.Domain.Entities;
using TechBazar.Domain.Enums;

namespace TechBazar.Application.Payments;

public sealed class PaymentService(
    IApplicationDbContext db,
    IEnumerable<IPaymentGateway> gateways,
    IOrderFulfilment fulfilment,
    OrderReader reader,
    IEmailSender email,
    IOptions<PaymentOptions> options,
    ILogger<PaymentService> logger) : IPaymentService
{
    public const string CodGateway = "cod";
    public const string OnlineGateway = "sslcommerz";
    private readonly PaymentOptions _opt = options.Value;

    public bool OnlinePaymentsEnabled => gateways.Any(g => g.Name == OnlineGateway && g.IsConfigured);

    public Payment CreateAttempt(Order order, PaymentMethod method)
    {
        var attempt = order.Payments.Count + 1;
        var payment = new Payment
        {
            Order = order,
            Gateway = method == PaymentMethod.Online ? OnlineGateway : CodGateway,
            Method = method,
            Amount = order.GrandTotal,
            Currency = "BDT",
            Status = PaymentAttemptStatus.Pending,
            // OUR reference: unique per attempt, short enough for gateway limits (30 chars).
            TransactionId = method == PaymentMethod.Online ? $"{order.OrderNumber}-{attempt}" : $"COD-{order.OrderNumber}",
        };
        order.Payments.Add(payment);
        return payment;
    }

    public async Task<PaymentRedirectDto> StartOnlinePaymentAsync(string orderNumber, string transactionId, CancellationToken ct = default)
    {
        var payment = await db.Payments.Include(p => p.Order).FirstOrDefaultAsync(p => p.TransactionId == transactionId && p.Order.OrderNumber == orderNumber, ct)
                      ?? throw new NotFoundException("Payment not found.");
        var gateway = gateways.FirstOrDefault(g => g.Name == payment.Gateway && g.IsConfigured);
        if (gateway is null)
        {
            await FailAsync(payment, "The payment gateway is not configured.", ct);
            return new PaymentRedirectDto(null, "Online payment is not available right now. Please choose cash on delivery.");
        }

        var o = payment.Order;
        var baseUrl = _opt.PublicApiBaseUrl.TrimEnd('/');
        var request = new PaymentInitRequest(
            payment.TransactionId, payment.Amount, payment.Currency, o.OrderNumber,
            o.ShipFullName, o.ContactEmail, o.ShipPhone, o.ShipAddressLine, o.ShipDistrict, $"TechBazar BD order {o.OrderNumber}",
            $"{baseUrl}/api/v1/payments/{gateway.Name}/success", $"{baseUrl}/api/v1/payments/{gateway.Name}/fail",
            $"{baseUrl}/api/v1/payments/{gateway.Name}/cancel", $"{baseUrl}/api/v1/payments/{gateway.Name}/ipn");

        PaymentInitResult result;
        try { result = await gateway.InitiateAsync(request, ct); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogError(ex, "Gateway {Gateway} unreachable for order {Order}", gateway.Name, o.OrderNumber);
            result = new PaymentInitResult(false, null, "The payment gateway could not be reached.");
        }

        if (!result.Success || string.IsNullOrEmpty(result.RedirectUrl))
        {
            await FailAsync(payment, result.Error ?? "The gateway rejected the payment request.", ct);
            return new PaymentRedirectDto(null, "We couldn’t start the online payment. You can retry from your order page or choose cash on delivery.");
        }
        return new PaymentRedirectDto(result.RedirectUrl, null);
    }

    public async Task<CallbackResult> HandleCallbackAsync(string gatewayName, CallbackKind kind, IReadOnlyDictionary<string, string> payload, CancellationToken ct = default)
    {
        var gateway = gateways.FirstOrDefault(g => g.Name.Equals(gatewayName, StringComparison.OrdinalIgnoreCase) && g.IsConfigured);
        if (gateway is null) return new CallbackResult(false, false, null, null, "Unknown or unconfigured gateway.");

        var v = await gateway.VerifyCallbackAsync(kind, payload, ct);
        if (!v.IsAuthentic || string.IsNullOrEmpty(v.TransactionId))
        {
            logger.LogWarning("Rejected {Gateway} {Kind} callback: {Error}", gateway.Name, kind, v.Error);
            return new CallbackResult(false, false, null, null, "Callback could not be authenticated.");
        }

        for (var attempt = 0; ; attempt++)
        {
            var payment = await db.Payments.Include(p => p.Order).ThenInclude(o => o.Payments)
                .Include(p => p.Order).ThenInclude(o => o.Items)
                .FirstOrDefaultAsync(p => p.TransactionId == v.TransactionId && p.Gateway == gateway.Name, ct);
            if (payment is null) return new CallbackResult(false, false, null, null, "Unknown transaction.");

            try
            {
                var result = await ApplyAsync(payment, v, ct);
                if (result.Status == PaymentAttemptStatus.Paid && !result.AlreadyProcessed) await NotifyPaidAsync(payment.Order.OrderNumber, ct);
                return result;
            }
            catch (DbUpdateConcurrencyException) when (attempt < 3)
            {
                // A parallel callback (browser redirect + IPN arrive together) won the race: reload and re-evaluate.
                db.ChangeTracker.Clear();
            }
        }
    }

    private async Task<CallbackResult> ApplyAsync(Payment payment, PaymentVerification v, CancellationToken ct)
    {
        var order = payment.Order;
        CallbackResult Result(bool handled, bool already, string msg) => new(handled, already, order.OrderNumber, payment.Status, msg);

        if (v.Outcome == PaymentOutcome.Paid)
        {
            if (payment.Status == PaymentAttemptStatus.Paid) return Result(true, true, "Already processed."); // idempotent replay
            if (payment.Status == PaymentAttemptStatus.Refunded) return Result(false, true, "Payment was refunded.");

            // Never trust the callback's numbers: the amount must equal what the SERVER asked the customer to pay.
            if (v.Amount is null || OrderCalculator.Round(v.Amount.Value) != OrderCalculator.Round(payment.Amount)
                || !string.Equals(v.Currency ?? "BDT", payment.Currency, StringComparison.OrdinalIgnoreCase))
            {
                logger.LogError("Amount/currency mismatch for {Tran}: gateway {GwAmount} {GwCur}, expected {Amount} {Cur}", payment.TransactionId, v.Amount, v.Currency, payment.Amount, payment.Currency);
                payment.FailureReason = "Amount or currency mismatch";
                await db.SaveChangesAsync(ct);
                return Result(false, false, "Amount mismatch.");
            }

            payment.Status = PaymentAttemptStatus.Paid;
            payment.PaidAt = DateTime.UtcNow;
            payment.GatewayReference = v.GatewayReference;
            payment.FailureReason = null;
            order.PaymentStatus = PaymentStatus.Paid;
            if (order.Status == OrderStatus.Pending) await fulfilment.TransitionAsync(order, OrderStatus.Confirmed, "Payment received", null, ct);
            else await db.SaveChangesAsync(ct);
            return Result(true, false, "Payment recorded.");
        }

        // Failed / cancelled: only an unpaid attempt can be downgraded, a Paid one is never undone by a later message.
        if (payment.Status != PaymentAttemptStatus.Pending) return Result(true, true, "Already processed.");
        payment.Status = v.Outcome == PaymentOutcome.Cancelled ? PaymentAttemptStatus.Cancelled : PaymentAttemptStatus.Failed;
        payment.FailureReason = v.Error ?? (v.Outcome == PaymentOutcome.Cancelled ? "Cancelled by customer" : "Payment failed");
        if (order.PaymentStatus != PaymentStatus.Paid) order.PaymentStatus = PaymentStatus.Failed;
        await db.SaveChangesAsync(ct);
        return Result(true, false, "Payment not completed.");
    }

    private async Task FailAsync(Payment payment, string reason, CancellationToken ct)
    {
        payment.Status = PaymentAttemptStatus.Failed;
        payment.FailureReason = reason;
        if (payment.Order.PaymentStatus != PaymentStatus.Paid) payment.Order.PaymentStatus = PaymentStatus.Failed;
        await db.SaveChangesAsync(ct);
    }

    private async Task NotifyPaidAsync(string orderNumber, CancellationToken ct)
    {
        try
        {
            var detail = await reader.GetDetailAsync(o => o.OrderNumber == orderNumber, ct);
            await email.SendAsync(OrderEmails.PaymentReceived(detail, _opt.StorefrontBaseUrl), ct);
        }
        catch (Exception ex) { logger.LogWarning(ex, "Could not send payment email for {Order}", orderNumber); }
    }

    /// <summary>8 chars from an unambiguous alphabet (no 0/O/1/I) for order numbers and share codes.</summary>
    public static string RandomCode(int length)
    {
        const string alphabet = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ";
        return string.Create(length, alphabet, (span, a) => { for (var i = 0; i < span.Length; i++) span[i] = a[RandomNumberGenerator.GetInt32(a.Length)]; });
    }
}
