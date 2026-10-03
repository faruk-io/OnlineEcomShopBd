using Microsoft.EntityFrameworkCore;
using TechBazar.Domain.Entities;

namespace TechBazar.Application.Abstractions;

/// <summary>Persistence abstraction used by Application services (implemented by Infrastructure's DbContext).</summary>
public interface IApplicationDbContext
{
    DbSet<Category> Categories { get; }
    DbSet<Brand> Brands { get; }
    DbSet<Product> Products { get; }
    DbSet<ProductKeyFeature> ProductKeyFeatures { get; }
    DbSet<ProductSpecification> ProductSpecifications { get; }
    DbSet<ProductImage> ProductImages { get; }
    DbSet<Review> Reviews { get; }
    DbSet<Cart> Carts { get; }
    DbSet<CartItem> CartItems { get; }
    DbSet<Wishlist> Wishlists { get; }
    DbSet<WishlistItem> WishlistItems { get; }
    DbSet<Address> Addresses { get; }
    DbSet<Coupon> Coupons { get; }
    DbSet<Order> Orders { get; }
    DbSet<OrderItem> OrderItems { get; }
    DbSet<OrderStatusHistory> OrderStatusHistories { get; }
    DbSet<Payment> Payments { get; }
    DbSet<PcBuild> PcBuilds { get; }
    DbSet<PcBuildItem> PcBuildItems { get; }

    Microsoft.EntityFrameworkCore.ChangeTracking.ChangeTracker ChangeTracker { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
