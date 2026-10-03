using Microsoft.EntityFrameworkCore;
using TechBazar.Application.Abstractions;
using TechBazar.Application.Common;
using TechBazar.Domain.Entities;
using TechBazar.Domain.Enums;

namespace TechBazar.Application.Orders;

public interface IOrderFulfilment
{
    /// <summary>
    /// Moves a TRACKED order to a new status: validates the transition, writes the timeline entry, puts stock and coupon
    /// usage back on cancellation/return, and settles cash-on-delivery payments on delivery. Saves.
    /// </summary>
    Task TransitionAsync(Order order, OrderStatus to, string? note, Guid? changedBy, CancellationToken ct = default);
}

public sealed class OrderFulfilment(IApplicationDbContext db) : IOrderFulfilment
{
    public async Task TransitionAsync(Order order, OrderStatus to, string? note, Guid? changedBy, CancellationToken ct = default)
    {
        if (!OrderStateMachine.CanTransition(order.Status, to, order.ShippingMethod))
            throw new ConflictException($"An order that is {order.Status} cannot be moved to {to}.");

        order.Status = to;
        db.OrderStatusHistories.Add(new OrderStatusHistory { OrderId = order.Id, Order = order, Status = to, Note = note, ChangedByUserId = changedBy });

        if (OrderStateMachine.ReleasesStock(to)) await ReleaseAsync(order, ct);

        if (to == OrderStatus.Delivered)
        {
            // Cash on delivery (or pay-at-store) is collected at hand-over.
            var cod = order.Payments.FirstOrDefault(p => p.Method == PaymentMethod.CashOnDelivery && p.Status == PaymentAttemptStatus.Pending);
            if (cod is not null)
            {
                cod.Status = PaymentAttemptStatus.Paid;
                cod.PaidAt = DateTime.UtcNow;
                order.PaymentStatus = PaymentStatus.Paid;
            }
        }

        if (to is OrderStatus.Cancelled or OrderStatus.Returned && order.PaymentStatus == PaymentStatus.Paid)
        {
            order.PaymentStatus = PaymentStatus.Refunded;
            foreach (var p in order.Payments.Where(p => p.Status == PaymentAttemptStatus.Paid)) p.Status = PaymentAttemptStatus.Refunded;
            db.OrderStatusHistories.Add(new OrderStatusHistory { OrderId = order.Id, Order = order, Status = to, Note = "Payment was received: a refund must be issued to the customer.", ChangedByUserId = changedBy });
        }
        else if (to == OrderStatus.Cancelled)
        {
            foreach (var p in order.Payments.Where(p => p.Status == PaymentAttemptStatus.Pending)) p.Status = PaymentAttemptStatus.Cancelled;
        }

        await db.SaveChangesAsync(ct);
    }

    private async Task ReleaseAsync(Order order, CancellationToken ct)
    {
        var restock = order.Items.Where(i => i.StockDecremented).ToList();
        if (restock.Count > 0)
        {
            var ids = restock.Select(i => i.ProductId).ToList();
            var products = await db.Products.Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
            foreach (var item in restock)
            {
                if (!products.TryGetValue(item.ProductId, out var p)) continue;
                p.StockQuantity += item.Quantity;
                if (p.StockStatus == StockStatus.OutOfStock && p.StockQuantity > 0) p.StockStatus = StockStatus.InStock;
                item.StockDecremented = false; // never restock the same line twice
            }
        }

        if (!string.IsNullOrWhiteSpace(order.CouponCode))
        {
            var coupon = await db.Coupons.FirstOrDefaultAsync(c => c.Code == order.CouponCode, ct);
            if (coupon is { UsedCount: > 0 }) coupon.UsedCount--;
        }
    }
}
