using TechBazar.Domain.Enums;

namespace TechBazar.Application.Orders;

/// <summary>Legal order status transitions. Shared by customers (cancel), admins (fulfilment) and payment callbacks.</summary>
public static class OrderStateMachine
{
    public static IReadOnlyList<OrderStatus> Next(OrderStatus from, ShippingMethod method)
    {
        var pickup = ShippingRules.IsPickup(method);
        return from switch
        {
            OrderStatus.Pending => [OrderStatus.Confirmed, OrderStatus.Cancelled],
            OrderStatus.Confirmed => [OrderStatus.Processing, OrderStatus.Cancelled],
            OrderStatus.Processing => pickup ? [OrderStatus.ReadyForPickup, OrderStatus.Cancelled] : [OrderStatus.Shipped, OrderStatus.Cancelled],
            OrderStatus.Shipped => [OrderStatus.Delivered, OrderStatus.Returned],
            OrderStatus.ReadyForPickup => [OrderStatus.Delivered, OrderStatus.Cancelled],
            OrderStatus.Delivered => [OrderStatus.Returned],
            _ => [], // Cancelled and Returned are terminal
        };
    }

    public static bool CanTransition(OrderStatus from, OrderStatus to, ShippingMethod method) => Next(from, method).Contains(to);

    /// <summary>The customer may cancel only before the order leaves the warehouse.</summary>
    public static bool CustomerCanCancel(OrderStatus status) => status is OrderStatus.Pending or OrderStatus.Confirmed;

    /// <summary>Statuses after which stock goes back on the shelf.</summary>
    public static bool ReleasesStock(OrderStatus to) => to is OrderStatus.Cancelled or OrderStatus.Returned;

    /// <summary>The ordered steps shown on the tracking timeline for a given delivery type.</summary>
    public static IReadOnlyList<OrderStatus> Timeline(ShippingMethod method) => ShippingRules.IsPickup(method)
        ? [OrderStatus.Pending, OrderStatus.Confirmed, OrderStatus.Processing, OrderStatus.ReadyForPickup, OrderStatus.Delivered]
        : [OrderStatus.Pending, OrderStatus.Confirmed, OrderStatus.Processing, OrderStatus.Shipped, OrderStatus.Delivered];
}
