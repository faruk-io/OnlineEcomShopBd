using TechBazar.Domain.Common;
using TechBazar.Domain.Enums;

namespace TechBazar.Domain.Entities;

public class Cart : BaseEntity
{
    public Guid UserId { get; set; }
    public ICollection<CartItem> Items { get; set; } = new List<CartItem>();
}

public class CartItem : BaseEntity
{
    public int CartId { get; set; }
    public Cart Cart { get; set; } = default!;
    public int ProductId { get; set; }
    public Product Product { get; set; } = default!;
    public int Quantity { get; set; }
    /// <summary>Price snapshot at the time the item was added.</summary>
    public decimal UnitPrice { get; set; }
}

public class Wishlist : BaseEntity
{
    public Guid UserId { get; set; }
    public ICollection<WishlistItem> Items { get; set; } = new List<WishlistItem>();
}

public class WishlistItem : BaseEntity
{
    public int WishlistId { get; set; }
    public Wishlist Wishlist { get; set; } = default!;
    public int ProductId { get; set; }
    public Product Product { get; set; } = default!;
}

public class Address : BaseEntity
{
    public Guid UserId { get; set; }
    public string Label { get; set; } = "Home";
    public string FullName { get; set; } = default!;
    public string Phone { get; set; } = default!;
    public string Division { get; set; } = default!;
    public string District { get; set; } = default!;
    public string? Upazila { get; set; }
    public string AddressLine { get; set; } = default!;
    public string? PostalCode { get; set; }
    public bool IsDefault { get; set; }
}

public class Coupon : BaseEntity, IVersioned
{
    public int Version { get; set; }
    public string Code { get; set; } = default!;
    public string? Description { get; set; }
    public DiscountType DiscountType { get; set; }
    public decimal Value { get; set; }
    public decimal? MinOrderAmount { get; set; }
    public decimal? MaxDiscountAmount { get; set; }
    public int? UsageLimit { get; set; }
    public int UsedCount { get; set; }
    public DateTime? StartsAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public bool IsActive { get; set; } = true;
}

public class Order : BaseEntity
{
    public string OrderNumber { get; set; } = default!;
    public Guid UserId { get; set; }
    public OrderStatus Status { get; set; } = OrderStatus.Pending;
    public ShippingMethod ShippingMethod { get; set; }
    public string ContactEmail { get; set; } = default!;
    public PaymentMethod PaymentMethod { get; set; }
    public PaymentStatus PaymentStatus { get; set; } = PaymentStatus.Unpaid;

    public decimal Subtotal { get; set; }
    public decimal DiscountTotal { get; set; }
    public decimal ShippingFee { get; set; }
    public decimal GrandTotal { get; set; }
    public string? CouponCode { get; set; }
    public string? Note { get; set; }

    // Shipping address snapshot (addresses can change/be deleted after the order is placed).
    public string ShipFullName { get; set; } = default!;
    public string ShipPhone { get; set; } = default!;
    public string ShipDivision { get; set; } = default!;
    public string ShipDistrict { get; set; } = default!;
    public string? ShipUpazila { get; set; }
    public string ShipAddressLine { get; set; } = default!;
    public string? ShipPostalCode { get; set; }

    public ICollection<OrderItem> Items { get; set; } = new List<OrderItem>();
    public ICollection<OrderStatusHistory> History { get; set; } = new List<OrderStatusHistory>();
    public ICollection<Payment> Payments { get; set; } = new List<Payment>();
}

public class OrderItem : BaseEntity
{
    public int OrderId { get; set; }
    public Order Order { get; set; } = default!;
    public int ProductId { get; set; }
    public Product Product { get; set; } = default!;
    public string ProductName { get; set; } = default!;
    public string Sku { get; set; } = default!;
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
    public decimal LineTotal { get; set; }
    /// <summary>True when placing the order took units from stock (pre-orders do not), so cancelling knows whether to put them back.</summary>
    public bool StockDecremented { get; set; }
}

/// <summary>One row per status change; drives the tracking timeline.</summary>
public class OrderStatusHistory : BaseEntity
{
    public int OrderId { get; set; }
    public Order Order { get; set; } = default!;
    public OrderStatus Status { get; set; }
    public string? Note { get; set; }
    /// <summary>Who changed it (null = system / customer action recorded by the system).</summary>
    public Guid? ChangedByUserId { get; set; }
}

/// <summary>A payment attempt for an order. <see cref="TransactionId"/> is OUR unique reference sent to the gateway.</summary>
public class Payment : BaseEntity, IVersioned
{
    public int Version { get; set; }
    public int OrderId { get; set; }
    public Order Order { get; set; } = default!;
    public string Gateway { get; set; } = default!;
    public PaymentMethod Method { get; set; }
    /// <summary>Amount in BDT calculated by the server when the attempt was created (the only amount ever accepted).</summary>
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "BDT";
    public PaymentAttemptStatus Status { get; set; } = PaymentAttemptStatus.Pending;
    public string TransactionId { get; set; } = default!;
    /// <summary>Gateway-side identifiers (val_id / bank_tran_id), set when the payment is validated.</summary>
    public string? GatewayReference { get; set; }
    public string? FailureReason { get; set; }
    public DateTime? PaidAt { get; set; }
}

/// <summary>A saved / shareable PC Builder configuration.</summary>
public class PcBuild : BaseEntity
{
    public string ShareCode { get; set; } = default!;
    public string? Name { get; set; }
    public Guid? UserId { get; set; }
    public ICollection<PcBuildItem> Items { get; set; } = new List<PcBuildItem>();
}

public class PcBuildItem : BaseEntity
{
    public int PcBuildId { get; set; }
    public PcBuild PcBuild { get; set; } = default!;
    public BuildSlot Slot { get; set; }
    public int ProductId { get; set; }
    public Product Product { get; set; } = default!;
    public int Quantity { get; set; } = 1;
}
