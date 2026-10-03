using TechBazar.Domain.Common;
using TechBazar.Domain.Enums;

namespace TechBazar.Domain.Entities;

public class Category : BaseEntity
{
    public string Name { get; set; } = default!;
    public string Slug { get; set; } = default!;
    public string? Description { get; set; }
    public string? ImageUrl { get; set; }
    public int? ParentId { get; set; }
    public Category? Parent { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public ICollection<Category> Children { get; set; } = new List<Category>();
    public ICollection<Product> Products { get; set; } = new List<Product>();
}

public class Brand : BaseEntity
{
    public string Name { get; set; } = default!;
    public string Slug { get; set; } = default!;
    public string? LogoUrl { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public ICollection<Product> Products { get; set; } = new List<Product>();
}

public class Product : BaseEntity
{
    public string Name { get; set; } = default!;
    public string Slug { get; set; } = default!;
    public string Sku { get; set; } = default!;
    public string? ShortDescription { get; set; }
    public string? Description { get; set; }

    /// <summary>Regular price in BDT.</summary>
    public decimal Price { get; set; }
    /// <summary>Optional sale price in BDT; must be lower than <see cref="Price"/>.</summary>
    public decimal? DiscountPrice { get; set; }

    public StockStatus StockStatus { get; set; } = StockStatus.InStock;
    public int StockQuantity { get; set; }
    public int WarrantyMonths { get; set; }
    public string? WarrantyDetails { get; set; }

    public int ViewCount { get; set; }
    public int SoldCount { get; set; }
    public decimal RatingAverage { get; set; }
    public int ReviewCount { get; set; }
    public bool IsFeatured { get; set; }
    public bool IsActive { get; set; } = true;

    public int CategoryId { get; set; }
    public Category Category { get; set; } = default!;
    public int BrandId { get; set; }
    public Brand Brand { get; set; } = default!;

    public ICollection<ProductKeyFeature> KeyFeatures { get; set; } = new List<ProductKeyFeature>();
    public ICollection<ProductSpecification> Specifications { get; set; } = new List<ProductSpecification>();
    public ICollection<ProductImage> Images { get; set; } = new List<ProductImage>();
    public ICollection<Review> Reviews { get; set; } = new List<Review>();

    /// <summary>
    /// Persisted price actually charged (<see cref="DiscountPrice"/> when valid, else <see cref="Price"/>).
    /// Kept in sync by <see cref="RecalculateEffectivePrice"/> (called from DbContext.SaveChanges) so it can be indexed,
    /// filtered and sorted in SQL without repeating the COALESCE logic everywhere.
    /// </summary>
    public decimal EffectivePrice { get; private set; }

    public void RecalculateEffectivePrice() =>
        EffectivePrice = DiscountPrice is > 0 && DiscountPrice < Price ? DiscountPrice.Value : Price;
}

public class ProductKeyFeature : BaseEntity
{
    public int ProductId { get; set; }
    public Product Product { get; set; } = default!;
    public string Text { get; set; } = default!;
    public int DisplayOrder { get; set; }
}

/// <summary>Grouped key/value spec, e.g. Group "Processor", Key "Base Clock", Value "3.5 GHz".</summary>
public class ProductSpecification : BaseEntity
{
    public int ProductId { get; set; }
    public Product Product { get; set; } = default!;
    public string Group { get; set; } = default!;
    public string Key { get; set; } = default!;
    public string Value { get; set; } = default!;
    /// <summary>Parsed leading number of <see cref="Value"/> (e.g. "65 W" -> 65) for numeric filtering / PC Builder maths.</summary>
    public decimal? NumericValue { get; set; }
    /// <summary>Exposed as a filter facet in the catalog.</summary>
    public bool IsFilterable { get; set; }
    public int DisplayOrder { get; set; }
}

public class ProductImage : BaseEntity
{
    public int ProductId { get; set; }
    public Product Product { get; set; } = default!;
    public string Url { get; set; } = default!;
    public string? AltText { get; set; }
    public bool IsPrimary { get; set; }
    public int DisplayOrder { get; set; }
}

public class Review : BaseEntity
{
    public int ProductId { get; set; }
    public Product Product { get; set; } = default!;
    public Guid UserId { get; set; }
    public string ReviewerName { get; set; } = default!;
    public int Rating { get; set; }
    public string? Title { get; set; }
    public string? Comment { get; set; }
    public bool IsApproved { get; set; }
}
