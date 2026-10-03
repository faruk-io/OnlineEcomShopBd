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

public class Coupon : BaseEntity
{
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
}
