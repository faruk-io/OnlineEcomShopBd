using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TechBazar.Application.Admin;
using TechBazar.Application.Common;
using TechBazar.Application.Orders;
using TechBazar.Application.Payments;
using TechBazar.Domain.Enums;
using TechBazar.UnitTests.Support;

namespace TechBazar.UnitTests.Orders;

public class OrderLifecycleTests
{
    private static async Task Advance(Scenario s, string number, params OrderStatus[] path)
    {
        var admin = s.Get<IAdminOrderService>();
        foreach (var status in path) await admin.UpdateStatusAsync(number, new UpdateOrderStatusRequest(status, null), Guid.NewGuid());
    }

    [Fact]
    public async Task CancellingBeforeShipment_PutsStockAndCouponBack_AndRecordsTheTimeline()
    {
        using var s = await Scenario.CreateAsync();
        var stock = s.Product("Ryzen 5 5600 Processor").StockQuantity;
        await s.AddToCart("Ryzen 5 5600 Processor", 3);
        var placed = await s.Place(coupon: "WELCOME10");
        s.Ctx.ChangeTracker.Clear();
        Assert.Equal(stock - 3, s.Product("Ryzen 5 5600 Processor").StockQuantity);
        Assert.Equal(1, (await s.Ctx.Coupons.SingleAsync(c => c.Code == "WELCOME10")).UsedCount);

        var cancelled = await s.Get<IOrderService>().CancelAsync(s.UserId, placed.Order.OrderNumber);

        Assert.Equal(OrderStatus.Cancelled, cancelled.Status);
        Assert.False(cancelled.CanCancel);
        Assert.Equal([OrderStatus.Pending, OrderStatus.Cancelled], cancelled.Timeline.Select(t => t.Status));
        Assert.Equal("Cancelled by customer", cancelled.History.Last().Note);
        s.Ctx.ChangeTracker.Clear();
        Assert.Equal(stock, s.Product("Ryzen 5 5600 Processor").StockQuantity);
        Assert.Equal(0, (await s.Ctx.Coupons.SingleAsync(c => c.Code == "WELCOME10")).UsedCount);
        Assert.Contains(s.Db.Emails.Sent, m => m.Subject.Contains("Cancelled"));
    }

    [Fact]
    public async Task CancellingARestockedOrderTwiceNeverRestocksTwice()
    {
        using var s = await Scenario.CreateAsync();
        var stock = s.Product("Ryzen 5 5600 Processor").StockQuantity;
        await s.AddToCart("Ryzen 5 5600 Processor", 2);
        var placed = await s.Place();
        var orders = s.Get<IOrderService>();
        await orders.CancelAsync(s.UserId, placed.Order.OrderNumber);
        await Assert.ThrowsAsync<ConflictException>(() => orders.CancelAsync(s.UserId, placed.Order.OrderNumber));
        s.Ctx.ChangeTracker.Clear();
        Assert.Equal(stock, s.Product("Ryzen 5 5600 Processor").StockQuantity);
    }

    [Fact]
    public async Task ASoldOutProductComesBackInStock_WhenTheLastOrderIsCancelled()
    {
        using var s = await Scenario.CreateAsync();
        s.Product("Ryzen 5 5600 Processor").StockQuantity = 1; await s.Ctx.SaveChangesAsync();
        await s.AddToCart("Ryzen 5 5600 Processor", 1);
        var placed = await s.Place();
        s.Ctx.ChangeTracker.Clear();
        Assert.Equal(StockStatus.OutOfStock, s.Product("Ryzen 5 5600 Processor").StockStatus);

        await s.Get<IOrderService>().CancelAsync(s.UserId, placed.Order.OrderNumber);
        s.Ctx.ChangeTracker.Clear();
        var p = s.Product("Ryzen 5 5600 Processor");
        Assert.Equal((1, StockStatus.InStock), (p.StockQuantity, p.StockStatus));
    }

    [Fact]
    public async Task TheCustomerCannotCancelOnceTheOrderIsBeingPrepared_OrCancelSomeoneElsesOrder()
    {
        using var s = await Scenario.CreateAsync();
        var (other, _) = await s.NewUserAsync("karim@example.com");
        await s.AddToCart("Ryzen 5 5600 Processor");
        var placed = await s.Place();
        await Advance(s, placed.Order.OrderNumber, OrderStatus.Confirmed, OrderStatus.Processing);

        var ex = await Assert.ThrowsAsync<ConflictException>(() => s.Get<IOrderService>().CancelAsync(s.UserId, placed.Order.OrderNumber));
        Assert.Contains("contact support", ex.Message);
        await Assert.ThrowsAsync<NotFoundException>(() => s.Get<IOrderService>().CancelAsync(other, placed.Order.OrderNumber));
        await Assert.ThrowsAsync<NotFoundException>(() => s.Get<IOrderService>().GetAsync(other, placed.Order.OrderNumber));
    }

    [Fact]
    public async Task CashOnDelivery_IsSettledWhenTheOrderIsDelivered_WithAFullTimeline()
    {
        using var s = await Scenario.CreateAsync();
        await s.AddToCart("Ryzen 5 5600 Processor");
        var placed = await s.Place();
        await Advance(s, placed.Order.OrderNumber, OrderStatus.Confirmed, OrderStatus.Processing, OrderStatus.Shipped);
        Assert.Equal(PaymentStatus.Unpaid, (await s.LoadOrder(placed.Order.OrderNumber)).PaymentStatus);   // cash not collected yet

        await Advance(s, placed.Order.OrderNumber, OrderStatus.Delivered);
        var detail = await s.Get<IOrderService>().GetAsync(s.UserId, placed.Order.OrderNumber);

        Assert.Equal(PaymentStatus.Paid, detail.PaymentStatus);
        Assert.Equal(PaymentAttemptStatus.Paid, detail.Payments.Single().Status);
        Assert.NotNull(detail.Payments.Single().PaidAt);
        Assert.All(detail.Timeline, t => Assert.True(t.Done));
        Assert.Equal(OrderStatus.Delivered, detail.Timeline.Single(t => t.Current).Status);
        Assert.All(detail.Timeline, t => Assert.NotNull(t.ReachedAt));
    }

    [Fact]
    public async Task IllegalTransitionsAreRejected_AndLeaveTheOrderUnchanged()
    {
        using var s = await Scenario.CreateAsync();
        await s.AddToCart("Ryzen 5 5600 Processor");
        var placed = await s.Place();
        var admin = s.Get<IAdminOrderService>();

        foreach (var illegal in new[] { OrderStatus.Shipped, OrderStatus.Delivered, OrderStatus.Returned, OrderStatus.ReadyForPickup, OrderStatus.Pending })
            await Assert.ThrowsAsync<ConflictException>(() => admin.UpdateStatusAsync(placed.Order.OrderNumber, new UpdateOrderStatusRequest(illegal, null), Guid.NewGuid()));
        Assert.Equal(OrderStatus.Pending, (await s.LoadOrder(placed.Order.OrderNumber)).Status);
        Assert.Single((await s.LoadOrder(placed.Order.OrderNumber)).History);
    }

    [Fact]
    public async Task PickupOrdersFollowTheirOwnFlow()
    {
        using var s = await Scenario.CreateAsync();
        await s.AddToCart("Ryzen 5 5600 Processor");
        var placed = await s.Place(shipping: ShippingMethod.StorePickup, contactName: "Karim", contactPhone: "01812345678");
        await Advance(s, placed.Order.OrderNumber, OrderStatus.Confirmed, OrderStatus.Processing, OrderStatus.ReadyForPickup);
        await Assert.ThrowsAsync<ConflictException>(() => Advance(s, placed.Order.OrderNumber, OrderStatus.Shipped));
        await Advance(s, placed.Order.OrderNumber, OrderStatus.Delivered);
        var detail = await s.Get<IOrderService>().GetAsync(s.UserId, placed.Order.OrderNumber);
        Assert.Equal("Picked up", detail.Timeline.Last().Label);
        Assert.Equal(PaymentStatus.Paid, detail.PaymentStatus);
    }

    [Fact]
    public async Task ReturningADeliveredOrder_RestocksIt_AndFlagsARefundForPaidOrders()
    {
        using var s = await Scenario.CreateAsync();
        var stock = s.Product("Ryzen 5 5600 Processor").StockQuantity;
        await s.AddToCart("Ryzen 5 5600 Processor", 2);
        var placed = await s.Place();
        await Advance(s, placed.Order.OrderNumber, OrderStatus.Confirmed, OrderStatus.Processing, OrderStatus.Shipped, OrderStatus.Delivered, OrderStatus.Returned);

        var order = await s.LoadOrder(placed.Order.OrderNumber);
        Assert.Equal(PaymentStatus.Refunded, order.PaymentStatus);
        Assert.Contains(order.History, h => h.Note != null && h.Note.Contains("refund must be issued"));
        s.Ctx.ChangeTracker.Clear();
        Assert.Equal(stock, s.Product("Ryzen 5 5600 Processor").StockQuantity);
    }

    [Fact]
    public async Task CancellingAnOrderThatWasAlreadyPaidOnline_FlagsTheRefund()
    {
        using var s = await Scenario.CreateAsync();
        await s.AddToCart("Ryzen 5 5600 Processor");
        var placed = await s.Place(PaymentMethod.Online);
        await s.Get<IPaymentService>().HandleCallbackAsync("sslcommerz", CallbackKind.Ipn, new Dictionary<string, string>
            { ["tran_id"] = $"{placed.Order.OrderNumber}-1", ["status"] = "VALID", ["amount"] = placed.Order.GrandTotal.ToString("0.00"), ["currency"] = "BDT", ["sig"] = "ok" });

        var cancelled = await s.Get<IOrderService>().CancelAsync(s.UserId, placed.Order.OrderNumber);
        Assert.Equal(PaymentStatus.Refunded, cancelled.PaymentStatus);
        Assert.Equal(PaymentAttemptStatus.Refunded, cancelled.Payments.Single().Status);
        Assert.Contains(cancelled.History, h => h.Note != null && h.Note.Contains("refund"));
    }

    [Fact]
    public async Task MarkPaid_RecordsManualPayments_ButNotTwice_AndNotForCancelledOrders()
    {
        using var s = await Scenario.CreateAsync();
        await s.AddToCart("Ryzen 5 5600 Processor");
        var placed = await s.Place();
        var admin = s.Get<IAdminOrderService>();

        var paid = await admin.MarkPaidAsync(placed.Order.OrderNumber, Guid.NewGuid());
        Assert.Equal(PaymentStatus.Paid, paid.Order.PaymentStatus);
        Assert.False(paid.CanMarkPaid);
        await Assert.ThrowsAsync<ConflictException>(() => admin.MarkPaidAsync(placed.Order.OrderNumber, Guid.NewGuid()));

        await s.AddToCart("Core i5-12400F");
        var second = await s.Place();
        await s.Get<IOrderService>().CancelAsync(s.UserId, second.Order.OrderNumber);
        await Assert.ThrowsAsync<ConflictException>(() => admin.MarkPaidAsync(second.Order.OrderNumber, Guid.NewGuid()));
    }

    [Fact]
    public async Task OrderHistoryList_IsPagedNewestFirst_AndPrivate()
    {
        using var s = await Scenario.CreateAsync();
        var (other, otherEmail) = await s.NewUserAsync("karim@example.com");
        for (var i = 0; i < 3; i++) { await s.AddToCart("Core i5-12400F"); await s.Place(); }
        await s.AddToCart("Core i5-12400F", 1, other);
        await s.Place(user: other, email: otherEmail);

        var orders = s.Get<IOrderService>();
        var page1 = await orders.ListAsync(s.UserId, 1, 2);
        Assert.Equal(3, page1.TotalCount);
        Assert.Equal(2, page1.Items.Count);
        Assert.Equal(2, page1.TotalPages);
        Assert.True(page1.Items[0].CreatedAt >= page1.Items[1].CreatedAt);
        Assert.Equal("Intel Core i5-12400F 12th Gen Processor", page1.Items[0].FirstItemName);
        Assert.Single((await orders.ListAsync(other, 1, 10)).Items);
    }
}
