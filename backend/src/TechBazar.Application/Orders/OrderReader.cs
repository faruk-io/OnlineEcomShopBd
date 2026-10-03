using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TechBazar.Application.Abstractions;
using TechBazar.Application.Common;
using TechBazar.Domain.Entities;
using TechBazar.Domain.Enums;

namespace TechBazar.Application.Orders;

/// <summary>Builds order DTOs (shared by customers, checkout and the admin panel).</summary>
public sealed class OrderReader(IApplicationDbContext db, IOptions<ShippingOptions> shipping)
{
    public async Task<OrderDetailDto> GetDetailAsync(Expression<Func<Order, bool>> filter, CancellationToken ct = default)
    {
        var order = await db.Orders.AsNoTracking()
            .Include(o => o.Items).Include(o => o.History).Include(o => o.Payments)
            .FirstOrDefaultAsync(filter, ct) ?? throw new NotFoundException("Order not found.");

        var productIds = order.Items.Select(i => i.ProductId).ToList();
        var products = await db.Products.AsNoTracking().Where(p => productIds.Contains(p.Id)).Select(p => new { p.Id, p.Slug }).ToDictionaryAsync(p => p.Id, p => p.Slug, ct);
        var images = await db.ProductImages.AsNoTracking().Where(i => productIds.Contains(i.ProductId) && i.IsPrimary)
            .Select(i => new { i.ProductId, i.Url }).ToListAsync(ct);
        var imageByProduct = images.GroupBy(i => i.ProductId).ToDictionary(g => g.Key, g => g.First().Url);

        var history = order.History.OrderBy(h => h.CreatedAt).ThenBy(h => h.Id).Select(h => new StatusEntryDto(h.Status, h.Note, h.CreatedAt)).ToList();
        var pickup = ShippingRules.IsPickup(order.ShippingMethod);
        var canPay = order.Status == OrderStatus.Pending && order.PaymentMethod == PaymentMethod.Online && order.PaymentStatus != PaymentStatus.Paid;

        return new OrderDetailDto(
            order.OrderNumber, order.CreatedAt, order.Status, order.PaymentStatus, order.PaymentMethod, order.ShippingMethod,
            order.Subtotal, order.DiscountTotal, order.ShippingFee, order.GrandTotal, order.CouponCode, order.Note, order.ContactEmail,
            new ShipAddressDto(order.ShipFullName, order.ShipPhone, order.ShipDivision, order.ShipDistrict, order.ShipUpazila, order.ShipAddressLine, order.ShipPostalCode),
            pickup ? new StoreDto(shipping.Value.StoreName, shipping.Value.StoreAddress) : null,
            order.Items.OrderBy(i => i.Id).Select(i => new OrderLineDto(i.ProductId, products.GetValueOrDefault(i.ProductId), i.ProductName, i.Sku,
                imageByProduct.GetValueOrDefault(i.ProductId), i.UnitPrice, i.Quantity, i.LineTotal)).ToList(),
            history,
            BuildTimeline(order.Status, order.ShippingMethod, history),
            order.Payments.OrderBy(p => p.Id).Select(p => new PaymentAttemptDto(p.Gateway, p.Method, p.Status, p.Amount, p.CreatedAt, p.PaidAt, p.FailureReason)).ToList(),
            OrderStateMachine.CustomerCanCancel(order.Status), canPay);
    }

    public static IReadOnlyList<TimelineStepDto> BuildTimeline(OrderStatus current, ShippingMethod method, IReadOnlyList<StatusEntryDto> history)
    {
        DateTime? ReachedAt(OrderStatus s) => history.Where(h => h.Status == s).Select(h => (DateTime?)h.At).FirstOrDefault();
        var steps = OrderStateMachine.Timeline(method);
        var pickup = ShippingRules.IsPickup(method);

        if (current is OrderStatus.Cancelled or OrderStatus.Returned)
        {
            // Show only what actually happened, then the terminal state.
            var reached = steps.Where(s => ReachedAt(s) is not null).Select(s => new TimelineStepDto(s, Label(s, pickup), ReachedAt(s), true, false)).ToList();
            reached.Add(new TimelineStepDto(current, Label(current, pickup), ReachedAt(current), true, true));
            return reached;
        }

        var idx = steps.ToList().IndexOf(current);
        return steps.Select((s, i) => new TimelineStepDto(s, Label(s, pickup), ReachedAt(s), i <= idx, i == idx)).ToList();
    }

    public static string Label(OrderStatus s, bool pickup) => s switch
    {
        OrderStatus.Pending => "Order placed",
        OrderStatus.Confirmed => "Confirmed",
        OrderStatus.Processing => "Preparing your order",
        OrderStatus.Shipped => "Out for delivery",
        OrderStatus.ReadyForPickup => "Ready for pickup",
        OrderStatus.Delivered => pickup ? "Picked up" : "Delivered",
        OrderStatus.Cancelled => "Cancelled",
        OrderStatus.Returned => "Returned",
        _ => s.ToString(),
    };
}
