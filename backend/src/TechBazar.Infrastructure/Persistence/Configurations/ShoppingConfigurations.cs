using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TechBazar.Domain.Entities;
using TechBazar.Infrastructure.Identity;

namespace TechBazar.Infrastructure.Persistence.Configurations;

public class CartConfiguration : IEntityTypeConfiguration<Cart>
{
    public void Configure(EntityTypeBuilder<Cart> b)
    {
        b.HasUniqueActiveIndex(x => x.UserId);
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class CartItemConfiguration : IEntityTypeConfiguration<CartItem>
{
    public void Configure(EntityTypeBuilder<CartItem> b)
    {
        b.ToTable(t => t.HasCheckConstraint("CK_CartItems_Quantity", "[Quantity] > 0"));
        b.HasUniqueActiveIndex(x => new { x.CartId, x.ProductId });
        b.HasOne(x => x.Cart).WithMany(x => x.Items).HasForeignKey(x => x.CartId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class WishlistConfiguration : IEntityTypeConfiguration<Wishlist>
{
    public void Configure(EntityTypeBuilder<Wishlist> b)
    {
        b.HasUniqueActiveIndex(x => x.UserId);
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class WishlistItemConfiguration : IEntityTypeConfiguration<WishlistItem>
{
    public void Configure(EntityTypeBuilder<WishlistItem> b)
    {
        b.HasUniqueActiveIndex(x => new { x.WishlistId, x.ProductId });
        b.HasOne(x => x.Wishlist).WithMany(x => x.Items).HasForeignKey(x => x.WishlistId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class AddressConfiguration : IEntityTypeConfiguration<Address>
{
    public void Configure(EntityTypeBuilder<Address> b)
    {
        b.Property(x => x.Label).HasMaxLength(50).IsRequired();
        b.Property(x => x.FullName).HasMaxLength(100).IsRequired();
        b.Property(x => x.Phone).HasMaxLength(20).IsRequired();
        b.Property(x => x.Division).HasMaxLength(50).IsRequired();
        b.Property(x => x.District).HasMaxLength(50).IsRequired();
        b.Property(x => x.Upazila).HasMaxLength(50);
        b.Property(x => x.AddressLine).HasMaxLength(300).IsRequired();
        b.Property(x => x.PostalCode).HasMaxLength(10);
        b.HasIndex(x => x.UserId);
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class CouponConfiguration : IEntityTypeConfiguration<Coupon>
{
    public void Configure(EntityTypeBuilder<Coupon> b)
    {
        b.Property(x => x.Code).HasMaxLength(50).IsRequired();
        b.Property(x => x.Description).HasMaxLength(300);
        b.Property(x => x.DiscountType).HasConversion<int>();
        b.Property(x => x.Version).IsConcurrencyToken();
        b.HasUniqueActiveIndex(x => x.Code);
        b.ToTable(t => t.HasCheckConstraint("CK_Coupons_Value", "[Value] > 0"));
    }
}

public class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> b)
    {
        b.Property(x => x.OrderNumber).HasMaxLength(30).IsRequired();
        b.Property(x => x.Status).HasConversion<int>();
        b.Property(x => x.PaymentMethod).HasConversion<int>();
        b.Property(x => x.PaymentStatus).HasConversion<int>();
        b.Property(x => x.ShippingMethod).HasConversion<int>();
        b.Property(x => x.ContactEmail).HasMaxLength(256).IsRequired();
        b.Property(x => x.CouponCode).HasMaxLength(50);
        b.Property(x => x.Note).HasMaxLength(500);
        b.Property(x => x.ShipFullName).HasMaxLength(100).IsRequired();
        b.Property(x => x.ShipPhone).HasMaxLength(20).IsRequired();
        b.Property(x => x.ShipDivision).HasMaxLength(50).IsRequired();
        b.Property(x => x.ShipDistrict).HasMaxLength(50).IsRequired();
        b.Property(x => x.ShipUpazila).HasMaxLength(50);
        b.Property(x => x.ShipAddressLine).HasMaxLength(300).IsRequired();
        b.Property(x => x.ShipPostalCode).HasMaxLength(10);
        b.HasUniqueActiveIndex(x => x.OrderNumber);
        b.HasIndex(x => new { x.UserId, x.CreatedAt });
        // admin lists: filter by status, newest first; status counts
        b.HasIndex(x => new { x.Status, x.CreatedAt });
        // dashboard sales window: range on CreatedAt, aggregating Status + GrandTotal straight from the index
        b.HasIndex(x => x.CreatedAt).IncludeProperties(x => new { x.Status, x.GrandTotal });
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class OrderItemConfiguration : IEntityTypeConfiguration<OrderItem>
{
    public void Configure(EntityTypeBuilder<OrderItem> b)
    {
        b.Property(x => x.ProductName).HasMaxLength(250).IsRequired();
        b.Property(x => x.Sku).HasMaxLength(64).IsRequired();
        b.ToTable(t => t.HasCheckConstraint("CK_OrderItems_Quantity", "[Quantity] > 0"));
        // order detail + dashboard top-products join: by OrderId, reading ProductId/Quantity/LineTotal without touching the table rows
        b.HasIndex(x => x.OrderId).IncludeProperties(x => new { x.ProductId, x.Quantity, x.LineTotal });
        b.HasOne(x => x.Order).WithMany(x => x.Items).HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> b)
    {
        b.Property(x => x.TokenHash).HasMaxLength(64).IsRequired();
        b.Property(x => x.ReplacedByTokenHash).HasMaxLength(64);
        b.Property(x => x.CreatedByIp).HasMaxLength(64);
        b.Property(x => x.RevokedReason).HasMaxLength(100);
        b.Ignore(x => x.IsRevoked);
        b.HasIndex(x => x.TokenHash).IsUnique();
        b.HasIndex(x => x.UserId);
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class OrderStatusHistoryConfiguration : IEntityTypeConfiguration<OrderStatusHistory>
{
    public void Configure(EntityTypeBuilder<OrderStatusHistory> b)
    {
        b.Property(x => x.Status).HasConversion<int>();
        b.Property(x => x.Note).HasMaxLength(500);
        b.HasIndex(x => x.OrderId);
        b.HasOne(x => x.Order).WithMany(x => x.History).HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> b)
    {
        b.Property(x => x.Gateway).HasMaxLength(30).IsRequired();
        b.Property(x => x.Method).HasConversion<int>();
        b.Property(x => x.Status).HasConversion<int>();
        b.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        b.Property(x => x.TransactionId).HasMaxLength(40).IsRequired();
        b.Property(x => x.GatewayReference).HasMaxLength(100);
        b.Property(x => x.FailureReason).HasMaxLength(300);
        b.Property(x => x.Version).IsConcurrencyToken();
        b.HasUniqueActiveIndex(x => x.TransactionId);
        b.HasIndex(x => x.OrderId);
        b.HasOne(x => x.Order).WithMany(x => x.Payments).HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class PcBuildConfiguration : IEntityTypeConfiguration<PcBuild>
{
    public void Configure(EntityTypeBuilder<PcBuild> b)
    {
        b.Property(x => x.ShareCode).HasMaxLength(12).IsRequired();
        b.Property(x => x.Name).HasMaxLength(100);
        b.HasUniqueActiveIndex(x => x.ShareCode);
        b.HasIndex(x => x.UserId);
    }
}

public class PcBuildItemConfiguration : IEntityTypeConfiguration<PcBuildItem>
{
    public void Configure(EntityTypeBuilder<PcBuildItem> b)
    {
        b.Property(x => x.Slot).HasConversion<int>();
        b.HasIndex(x => x.PcBuildId);
        b.HasOne(x => x.PcBuild).WithMany(x => x.Items).HasForeignKey(x => x.PcBuildId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
    }
}
