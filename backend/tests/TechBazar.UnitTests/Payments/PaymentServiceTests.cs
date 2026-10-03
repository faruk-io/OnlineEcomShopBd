using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TechBazar.Application.Abstractions;
using TechBazar.Application.Common;
using TechBazar.Application.Orders;
using TechBazar.Application.Payments;
using TechBazar.Domain.Entities;
using TechBazar.Domain.Enums;
using TechBazar.Infrastructure.Persistence;
using TechBazar.UnitTests.Support;

namespace TechBazar.UnitTests.Payments;

/// <summary>Gateway callback handling against a real (SQLite) database: authenticity, amounts, ordering and idempotency.</summary>
public class PaymentServiceTests
{
    private static async Task<(Scenario s, PlaceOrderResult placed, string tran, decimal total)> OnlineOrder()
    {
        var s = await Scenario.CreateAsync();
        await s.AddToCart("Ryzen 5 5600 Processor", 2);
        var placed = await s.Place(PaymentMethod.Online);
        return (s, placed, $"{placed.Order.OrderNumber}-1", placed.Order.GrandTotal);
    }

    private static Dictionary<string, string> Paid(string tran, decimal amount, string sig = "ok", string currency = "BDT") => new()
    {
        ["tran_id"] = tran, ["status"] = "VALID", ["amount"] = amount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
        ["currency"] = currency, ["sig"] = sig, ["bank_tran_id"] = "BANK-1",
    };

    private static Task<CallbackResult> Callback(Scenario s, CallbackKind kind, Dictionary<string, string> p, string gateway = "sslcommerz") =>
        s.Get<IPaymentService>().HandleCallbackAsync(gateway, kind, p);

    // ------------------------------------------------------------------ starting a payment
    [Fact]
    public async Task PlacingAnOnlineOrder_StartsAGatewaySession_WithTheServerTotalAndCallbackUrls()
    {
        var (s, placed, tran, total) = await OnlineOrder();
        using var _ = s;

        Assert.Equal("https://sandbox.gateway.test/pay/" + tran, placed.Payment!.RedirectUrl);
        Assert.Null(placed.Payment.Error);
        var init = s.Db.Gateway.LastInit!;
        Assert.Equal(total, init.Amount);
        Assert.Equal(23800m + 70m, init.Amount);
        Assert.Equal("BDT", init.Currency);
        Assert.Equal(tran, init.TransactionId);
        Assert.Equal("https://api.test/api/v1/payments/sslcommerz/ipn", init.IpnUrl);
        Assert.Equal("https://api.test/api/v1/payments/sslcommerz/success", init.SuccessUrl);
        Assert.Equal("rahim@example.com", init.CustomerEmail);

        var order = await s.LoadOrder(placed.Order.OrderNumber);
        var p = Assert.Single(order.Payments);
        Assert.Equal((PaymentAttemptStatus.Pending, "sslcommerz", total), (p.Status, p.Gateway, p.Amount));
        Assert.Equal(PaymentStatus.Unpaid, order.PaymentStatus);
        Assert.Equal(OrderStatus.Pending, order.Status);   // not confirmed until the money is verified
    }

    [Fact]
    public async Task IfTheGatewayRefusesTheSession_TheOrderIsKept_TheAttemptFails_AndTheCustomerCanRetry()
    {
        using var s = await Scenario.CreateAsync();
        s.Db.Gateway.InitSucceeds = false;
        await s.AddToCart("Ryzen 5 5600 Processor");
        var placed = await s.Place(PaymentMethod.Online);

        Assert.Null(placed.Payment!.RedirectUrl);
        Assert.NotNull(placed.Payment.Error);
        Assert.Equal(PaymentStatus.Failed, placed.Order.PaymentStatus);
        Assert.Equal(PaymentAttemptStatus.Failed, placed.Order.Payments.Single().Status);
        Assert.True(placed.Order.CanPay);

        s.Db.Gateway.InitSucceeds = true;
        var retry = await s.Get<IOrderService>().RetryPaymentAsync(s.UserId, placed.Order.OrderNumber);
        Assert.EndsWith(placed.Order.OrderNumber + "-2", retry.RedirectUrl);
        var order = await s.LoadOrder(placed.Order.OrderNumber);
        Assert.Equal(2, order.Payments.Count);
        Assert.Equal(PaymentStatus.Unpaid, order.PaymentStatus);
    }

    [Fact]
    public async Task OnlinePaymentIsRefusedWhenTheGatewayIsNotConfigured()
    {
        using var s = await Scenario.CreateAsync();
        s.Db.Gateway.IsConfigured = false;
        await s.AddToCart("Ryzen 5 5600 Processor");
        var ex = await Assert.ThrowsAsync<ConflictException>(() => s.Place(PaymentMethod.Online));
        Assert.Contains("cash on delivery", ex.Message);
    }

    // ------------------------------------------------------------------ the happy path
    [Fact]
    public async Task AVerifiedPaidCallback_MarksPaid_ConfirmsTheOrder_AndNotifiesTheCustomerOnce()
    {
        var (s, placed, tran, total) = await OnlineOrder();
        using var _ = s;
        s.Db.Emails.Sent.ToList(); // placement email already sent

        var r = await Callback(s, CallbackKind.Success, Paid(tran, total));
        Assert.True(r.Handled);
        Assert.False(r.AlreadyProcessed);
        Assert.Equal(PaymentAttemptStatus.Paid, r.Status);
        Assert.Equal(placed.Order.OrderNumber, r.OrderNumber);

        var order = await s.LoadOrder(placed.Order.OrderNumber);
        Assert.Equal(PaymentStatus.Paid, order.PaymentStatus);
        Assert.Equal(OrderStatus.Confirmed, order.Status);
        var pay = order.Payments.Single();
        Assert.Equal(PaymentAttemptStatus.Paid, pay.Status);
        Assert.NotNull(pay.PaidAt);
        Assert.Equal("BANK-1", pay.GatewayReference);
        Assert.Contains(order.History, h => h.Status == OrderStatus.Confirmed && h.Note == "Payment received");
        Assert.Equal(1, s.Db.Emails.Sent.Count(m => m.Subject.StartsWith("Payment received")));
    }

    // ------------------------------------------------------------------ idempotency
    [Fact]
    public async Task TheSameCallbackDeliveredManyTimes_ChangesNothingAfterTheFirst()
    {
        var (s, placed, tran, total) = await OnlineOrder();
        using var _ = s;

        var first = await Callback(s, CallbackKind.Ipn, Paid(tran, total));
        var browser = await Callback(s, CallbackKind.Success, Paid(tran, total));
        var again = await Callback(s, CallbackKind.Ipn, Paid(tran, total));

        Assert.False(first.AlreadyProcessed);
        Assert.True(browser.AlreadyProcessed);
        Assert.True(again.AlreadyProcessed);
        Assert.All(new[] { first, browser, again }, r => Assert.True(r.Handled));

        var order = await s.LoadOrder(placed.Order.OrderNumber);
        Assert.Equal(1, order.History.Count(h => h.Status == OrderStatus.Confirmed));           // one timeline entry
        Assert.Equal(1, s.Db.Emails.Sent.Count(m => m.Subject.StartsWith("Payment received")));  // one email
        Assert.Equal(PaymentAttemptStatus.Paid, order.Payments.Single().Status);
    }

    [Fact]
    public async Task ARaceWhereAnotherCallbackWinsBetweenReadAndWrite_IsDetectedAndTreatedAsAlreadyProcessed()
    {
        var (s, placed, tran, total) = await OnlineOrder();
        using var _ = s;

        // Decorate fulfilment so that, right before THIS request saves, a parallel request (IPN) records the payment first.
        var racing = new RacingFulfilment(s.Get<IOrderFulfilment>(), async () =>
        {
            using var other = s.Db.CreateScope();
            var ctx = other.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var row = await ctx.Payments.SingleAsync(p => p.TransactionId == tran);
            row.Status = PaymentAttemptStatus.Paid; row.PaidAt = DateTime.UtcNow;
            await ctx.SaveChangesAsync();
        });
        var service = new PaymentService(s.Ctx, s.Get<IEnumerable<IPaymentGateway>>(), racing, s.Get<OrderReader>(), s.Get<TechBazar.Application.Email.IEmailSender>(),
            s.Get<IOptions<PaymentOptions>>(), NullLogger<PaymentService>.Instance);

        var result = await service.HandleCallbackAsync("sslcommerz", CallbackKind.Success, Paid(tran, total));

        Assert.True(racing.Raced);
        Assert.True(result.Handled);
        Assert.True(result.AlreadyProcessed);                       // the loser backs off instead of throwing or double-applying
        s.Ctx.ChangeTracker.Clear();
        Assert.Equal(1, await s.Ctx.Payments.CountAsync(p => p.TransactionId == tran && p.Status == PaymentAttemptStatus.Paid));
    }

    private sealed class RacingFulfilment(IOrderFulfilment inner, Func<Task> parallelWrite) : IOrderFulfilment
    {
        public bool Raced { get; private set; }
        public async Task TransitionAsync(Order order, OrderStatus to, string? note, Guid? changedBy, CancellationToken ct = default)
        {
            if (!Raced) { Raced = true; await parallelWrite(); }
            await inner.TransitionAsync(order, to, note, changedBy, ct);
        }
    }

    // ------------------------------------------------------------------ never trust the callback
    [Theory]
    [InlineData("1.00")]       // paid far less
    [InlineData("23869.99")]   // off by one paisa
    [InlineData("99999.00")]   // paid more
    public async Task APaidCallbackForTheWrongAmount_IsNotAccepted(string amount)
    {
        var (s, placed, tran, total) = await OnlineOrder();
        using var _ = s;
        var r = await Callback(s, CallbackKind.Ipn, Paid(tran, decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture)));

        Assert.False(r.Handled);
        var order = await s.LoadOrder(placed.Order.OrderNumber);
        Assert.Equal(PaymentStatus.Unpaid, order.PaymentStatus);
        Assert.Equal(OrderStatus.Pending, order.Status);
        Assert.Equal(PaymentAttemptStatus.Pending, order.Payments.Single().Status);
        Assert.Equal("Amount or currency mismatch", order.Payments.Single().FailureReason);
        Assert.DoesNotContain(s.Db.Emails.Sent, m => m.Subject.StartsWith("Payment received"));
        Assert.Equal(23870m, total);
    }

    [Fact]
    public async Task ACurrencyOtherThanBdt_IsNotAccepted()
    {
        var (s, placed, tran, total) = await OnlineOrder();
        using var _ = s;
        Assert.False((await Callback(s, CallbackKind.Ipn, Paid(tran, total, currency: "USD"))).Handled);
        Assert.Equal(PaymentStatus.Unpaid, (await s.LoadOrder(placed.Order.OrderNumber)).PaymentStatus);
    }

    [Fact]
    public async Task AnUnauthenticatedCallback_ChangesNothing()
    {
        var (s, placed, tran, total) = await OnlineOrder();
        using var _ = s;
        var r = await Callback(s, CallbackKind.Ipn, Paid(tran, total, sig: "forged"));
        Assert.False(r.Handled);
        var order = await s.LoadOrder(placed.Order.OrderNumber);
        Assert.Equal(PaymentStatus.Unpaid, order.PaymentStatus);
        Assert.Equal(PaymentAttemptStatus.Pending, order.Payments.Single().Status);
    }

    [Fact]
    public async Task UnknownTransactions_UnknownGateways_AndMissingIds_AreIgnored()
    {
        var (s, _, _, total) = await OnlineOrder();
        using var _ = s;
        Assert.False((await Callback(s, CallbackKind.Ipn, Paid("TB-000000-NOPE-1", total))).Handled);
        Assert.False((await Callback(s, CallbackKind.Ipn, Paid("whatever", total), gateway: "paypal")).Handled);
        Assert.False((await Callback(s, CallbackKind.Ipn, new Dictionary<string, string> { ["status"] = "VALID" })).Handled);
    }

    [Fact]
    public async Task ACallbackCannotSettleAnotherOrdersPayment_ByUsingTheOtherTransactionId()
    {
        var (s, a, tranA, totalA) = await OnlineOrder();
        using var _ = s;
        await s.AddToCart("Core i5-12400F", 1);
        var b = await s.Place(PaymentMethod.Online);                       // a second, cheaper order
        var tranB = $"{b.Order.OrderNumber}-1";

        // Paying order B's (smaller) amount against order A's transaction id must fail the amount check.
        Assert.False((await Callback(s, CallbackKind.Ipn, Paid(tranA, b.Order.GrandTotal))).Handled);
        Assert.Equal(PaymentStatus.Unpaid, (await s.LoadOrder(a.Order.OrderNumber)).PaymentStatus);
        // and the right amount settles only its own order
        Assert.True((await Callback(s, CallbackKind.Ipn, Paid(tranB, b.Order.GrandTotal))).Handled);
        Assert.Equal(PaymentStatus.Unpaid, (await s.LoadOrder(a.Order.OrderNumber)).PaymentStatus);
        Assert.Equal(PaymentStatus.Paid, (await s.LoadOrder(b.Order.OrderNumber)).PaymentStatus);
        Assert.NotEqual(totalA, b.Order.GrandTotal);
    }

    // ------------------------------------------------------------------ failure paths & ordering
    [Fact]
    public async Task FailedAndCancelledCallbacks_CloseThePendingAttempt_ButKeepTheOrderOpenForRetry()
    {
        var (s, placed, tran, _) = await OnlineOrder();
        using var _ = s;
        var f = await Callback(s, CallbackKind.Fail, new Dictionary<string, string> { ["tran_id"] = tran, ["status"] = "FAILED" });
        Assert.True(f.Handled);
        var order = await s.LoadOrder(placed.Order.OrderNumber);
        Assert.Equal(PaymentAttemptStatus.Failed, order.Payments.Single().Status);
        Assert.Equal(PaymentStatus.Failed, order.PaymentStatus);
        Assert.Equal(OrderStatus.Pending, order.Status);

        // a new attempt that the customer abandons
        var retry = await s.Get<IOrderService>().RetryPaymentAsync(s.UserId, placed.Order.OrderNumber);
        Assert.NotNull(retry.RedirectUrl);
        var c = await Callback(s, CallbackKind.Cancel, new Dictionary<string, string> { ["tran_id"] = $"{placed.Order.OrderNumber}-2", ["status"] = "CANCELLED" });
        Assert.Equal(PaymentAttemptStatus.Cancelled, c.Status);
    }

    [Fact]
    public async Task APaymentThatIsAlreadyPaid_CanNeverBeDowngradedByALaterFailureMessage()
    {
        var (s, placed, tran, total) = await OnlineOrder();
        using var _ = s;
        await Callback(s, CallbackKind.Ipn, Paid(tran, total));
        var late = await Callback(s, CallbackKind.Fail, new Dictionary<string, string> { ["tran_id"] = tran, ["status"] = "FAILED" });

        Assert.True(late.AlreadyProcessed);
        var order = await s.LoadOrder(placed.Order.OrderNumber);
        Assert.Equal(PaymentAttemptStatus.Paid, order.Payments.Single().Status);
        Assert.Equal(PaymentStatus.Paid, order.PaymentStatus);
    }

    [Fact]
    public async Task VerifiedMoneyWins_APaidMessageAfterAnEarlierFailureStillSettlesTheOrder()
    {
        var (s, placed, tran, total) = await OnlineOrder();
        using var _ = s;
        await Callback(s, CallbackKind.Fail, new Dictionary<string, string> { ["tran_id"] = tran, ["status"] = "FAILED" }); // e.g. a spoofed/early fail
        var paid = await Callback(s, CallbackKind.Ipn, Paid(tran, total));

        Assert.True(paid.Handled);
        var order = await s.LoadOrder(placed.Order.OrderNumber);
        Assert.Equal(PaymentStatus.Paid, order.PaymentStatus);
        Assert.Equal(OrderStatus.Confirmed, order.Status);
    }

    [Fact]
    public async Task APaymentArrivingForAnAbandonedEarlierAttempt_StillCountsAfterARetry()
    {
        var (s, placed, tran1, total) = await OnlineOrder();
        using var _ = s;
        await s.Get<IOrderService>().RetryPaymentAsync(s.UserId, placed.Order.OrderNumber);   // attempt 2 supersedes attempt 1
        var r = await Callback(s, CallbackKind.Ipn, Paid(tran1, total));                       // but the customer actually paid attempt 1
        Assert.True(r.Handled);
        Assert.Equal(PaymentStatus.Paid, (await s.LoadOrder(placed.Order.OrderNumber)).PaymentStatus);
    }

    [Fact]
    public async Task APaidMessageForACancelledOrder_RecordsThePaymentWithoutResurrectingTheOrder()
    {
        var (s, placed, tran, total) = await OnlineOrder();
        using var _ = s;
        await s.Get<IOrderService>().CancelAsync(s.UserId, placed.Order.OrderNumber);
        var r = await Callback(s, CallbackKind.Ipn, Paid(tran, total));
        var order = await s.LoadOrder(placed.Order.OrderNumber);

        Assert.True(r.Handled);
        Assert.Equal(OrderStatus.Cancelled, order.Status);         // the state machine refuses to revive it
        Assert.Equal(PaymentStatus.Paid, order.PaymentStatus);      // but we know the money arrived -> an admin must refund it
    }

    [Fact]
    public async Task TheCodGatewayHasNoCallbacks()
    {
        using var s = await Scenario.CreateAsync();
        Assert.False((await Callback(s, CallbackKind.Ipn, new Dictionary<string, string> { ["tran_id"] = "COD-1", ["status"] = "VALID" }, "cod")).Handled);
    }
}
