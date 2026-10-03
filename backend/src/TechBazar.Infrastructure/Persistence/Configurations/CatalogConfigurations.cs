using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TechBazar.Domain.Entities;

namespace TechBazar.Infrastructure.Persistence.Configurations;

internal static class IndexExtensions
{
    public const string NotDeleted = "[IsDeleted] = 0";

    /// <summary>Unique among non-deleted rows so a soft-deleted slug/SKU can be reused.</summary>
    public static void HasUniqueActiveIndex<T>(this EntityTypeBuilder<T> b, System.Linq.Expressions.Expression<Func<T, object?>> expr)
        where T : class => b.HasIndex(expr).IsUnique().HasFilter(NotDeleted);
}

public class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> b)
    {
        b.Property(x => x.Name).HasMaxLength(100).IsRequired();
        b.Property(x => x.Slug).HasMaxLength(120).IsRequired();
        b.Property(x => x.Description).HasMaxLength(1000);
        b.Property(x => x.ImageUrl).HasMaxLength(500);
        b.HasUniqueActiveIndex(x => x.Slug);
        b.HasIndex(x => x.ParentId);
        b.HasOne(x => x.Parent).WithMany(x => x.Children).HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class BrandConfiguration : IEntityTypeConfiguration<Brand>
{
    public void Configure(EntityTypeBuilder<Brand> b)
    {
        b.Property(x => x.Name).HasMaxLength(100).IsRequired();
        b.Property(x => x.Slug).HasMaxLength(120).IsRequired();
        b.Property(x => x.LogoUrl).HasMaxLength(500);
        b.Property(x => x.Description).HasMaxLength(1000);
        b.HasUniqueActiveIndex(x => x.Slug);
        b.HasUniqueActiveIndex(x => x.Name);
    }
}

public class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> b)
    {
        b.Property(x => x.Name).HasMaxLength(250).IsRequired();
        b.Property(x => x.Slug).HasMaxLength(300).IsRequired();
        b.Property(x => x.Sku).HasMaxLength(64).IsRequired();
        b.Property(x => x.ShortDescription).HasMaxLength(500);
        b.Property(x => x.Description).HasMaxLength(4000);
        b.Property(x => x.WarrantyDetails).HasMaxLength(500);
        b.Property(x => x.StockStatus).HasConversion<int>();
        b.Property(x => x.RatingAverage).HasPrecision(3, 2);
        b.Property(x => x.Version).IsConcurrencyToken();
        // Price, DiscountPrice, EffectivePrice -> decimal(18,2) via global convention.

        b.HasUniqueActiveIndex(x => x.Slug);
        b.HasUniqueActiveIndex(x => x.Sku);
        b.HasIndex(x => new { x.CategoryId, x.EffectivePrice });
        b.HasIndex(x => x.BrandId);
        // serves the in-stock filter (leading column) AND the low-stock range scans of the admin product list / dashboard
        b.HasIndex(x => new { x.StockStatus, x.StockQuantity });
        b.HasIndex(x => x.SoldCount);
        b.HasIndex(x => x.CreatedAt);

        b.ToTable(t =>
        {
            t.HasCheckConstraint("CK_Products_Price", "[Price] >= 0");
            t.HasCheckConstraint("CK_Products_DiscountPrice", "[DiscountPrice] IS NULL OR ([DiscountPrice] >= 0 AND [DiscountPrice] < [Price])");
        });

        b.HasOne(x => x.Category).WithMany(x => x.Products).HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Brand).WithMany(x => x.Products).HasForeignKey(x => x.BrandId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class ProductKeyFeatureConfiguration : IEntityTypeConfiguration<ProductKeyFeature>
{
    public void Configure(EntityTypeBuilder<ProductKeyFeature> b)
    {
        b.Property(x => x.Text).HasMaxLength(300).IsRequired();
        b.HasIndex(x => x.ProductId);
        b.HasOne(x => x.Product).WithMany(x => x.KeyFeatures).HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class ProductSpecificationConfiguration : IEntityTypeConfiguration<ProductSpecification>
{
    public void Configure(EntityTypeBuilder<ProductSpecification> b)
    {
        b.Property(x => x.Group).HasMaxLength(100).IsRequired();
        b.Property(x => x.Key).HasMaxLength(100).IsRequired();
        b.Property(x => x.Value).HasMaxLength(300).IsRequired();
        b.HasIndex(x => x.ProductId);
        // Serves spec filtering (Key = ? AND Value IN (...)) and facet counts.
        // Spec filters / facets look rows up by (Key, Value) and then need only ProductId: INCLUDE makes the index covering (no key lookups)
        b.HasIndex(x => new { x.Key, x.Value }).IncludeProperties(x => x.ProductId);
        b.HasOne(x => x.Product).WithMany(x => x.Specifications).HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class ProductImageConfiguration : IEntityTypeConfiguration<ProductImage>
{
    public void Configure(EntityTypeBuilder<ProductImage> b)
    {
        b.Property(x => x.Url).HasMaxLength(500).IsRequired();
        b.Property(x => x.AltText).HasMaxLength(250);
        b.HasIndex(x => x.ProductId);
        b.HasOne(x => x.Product).WithMany(x => x.Images).HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class ReviewConfiguration : IEntityTypeConfiguration<Review>
{
    public void Configure(EntityTypeBuilder<Review> b)
    {
        b.Property(x => x.ReviewerName).HasMaxLength(100).IsRequired();
        b.Property(x => x.Title).HasMaxLength(150);
        b.Property(x => x.Comment).HasMaxLength(2000);
        b.ToTable(t => t.HasCheckConstraint("CK_Reviews_Rating", "[Rating] BETWEEN 1 AND 5"));
        b.HasUniqueActiveIndex(x => new { x.ProductId, x.UserId });
        b.HasOne(x => x.Product).WithMany(x => x.Reviews).HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<Identity.ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}
