using TechBazar.Domain.Enums;

namespace TechBazar.Application.Catalog.Dtos;

public sealed record ProductListItemDto
{
    public int Id { get; init; }
    public string Name { get; init; } = default!;
    public string Slug { get; init; } = default!;
    public string Sku { get; init; } = default!;
    public decimal Price { get; init; }
    public decimal? DiscountPrice { get; init; }
    public decimal EffectivePrice { get; init; }
    public int DiscountPercent => PriceMath.DiscountPercent(Price, EffectivePrice);
    public StockStatus StockStatus { get; init; }
    public string? ImageUrl { get; init; }
    public string BrandName { get; init; } = default!;
    public string BrandSlug { get; init; } = default!;
    public string CategoryName { get; init; } = default!;
    public string CategorySlug { get; init; } = default!;
    public decimal RatingAverage { get; init; }
    public int ReviewCount { get; init; }
    public int WarrantyMonths { get; init; }
    public IReadOnlyList<string> KeyFeatures { get; init; } = [];
}

public static class PriceMath
{
    public static int DiscountPercent(decimal price, decimal effectivePrice) =>
        price > 0 && effectivePrice < price ? (int)Math.Round((price - effectivePrice) / price * 100m, MidpointRounding.AwayFromZero) : 0;
}

public sealed record ProductDetailDto
{
    public int Id { get; init; }
    public string Name { get; init; } = default!;
    public string Slug { get; init; } = default!;
    public string Sku { get; init; } = default!;
    public string? ShortDescription { get; init; }
    public string? Description { get; init; }
    public decimal Price { get; init; }
    public decimal? DiscountPrice { get; init; }
    public decimal EffectivePrice { get; init; }
    public int DiscountPercent => PriceMath.DiscountPercent(Price, EffectivePrice);
    public decimal SavingsAmount => Price - EffectivePrice;
    public StockStatus StockStatus { get; init; }
    public int WarrantyMonths { get; init; }
    public string? WarrantyDetails { get; init; }
    public decimal RatingAverage { get; init; }
    public int ReviewCount { get; init; }
    public BrandDto Brand { get; init; } = default!;
    public CategoryRefDto Category { get; init; } = default!;
    public IReadOnlyList<CategoryRefDto> Breadcrumbs { get; init; } = [];
    public IReadOnlyList<ProductImageDto> Images { get; init; } = [];
    public IReadOnlyList<string> KeyFeatures { get; init; } = [];
    public IReadOnlyList<SpecificationGroupDto> Specifications { get; init; } = [];
    public IReadOnlyList<ReviewDto> Reviews { get; init; } = [];
}

public sealed record ProductImageDto(string Url, string? AltText, bool IsPrimary);
public sealed record SpecificationItemDto(string Key, string Value);
public sealed record SpecificationGroupDto(string Group, IReadOnlyList<SpecificationItemDto> Items);
public sealed record ReviewDto(string ReviewerName, int Rating, string? Title, string? Comment, DateTime CreatedAt);

public sealed record BrandDto(int Id, string Name, string Slug, string? LogoUrl);
public sealed record BrandListItemDto(int Id, string Name, string Slug, string? LogoUrl, int ProductCount);
public sealed record CategoryRefDto(int Id, string Name, string Slug);

public sealed record CategoryTreeNodeDto
{
    public int Id { get; init; }
    public string Name { get; init; } = default!;
    public string Slug { get; init; } = default!;
    public string? ImageUrl { get; init; }
    /// <summary>Products in this category including all descendants.</summary>
    public int ProductCount { get; set; }
    public List<CategoryTreeNodeDto> Children { get; init; } = [];
}

public sealed record CategoryDetailDto(
    int Id, string Name, string Slug, string? Description, string? ImageUrl,
    IReadOnlyList<CategoryRefDto> Breadcrumbs, IReadOnlyList<CategoryRefDto> Children);

public sealed record FacetValueDto(string Value, int Count);
public sealed record SpecFacetDto(string Key, IReadOnlyList<FacetValueDto> Values);
public sealed record BrandFacetDto(string Name, string Slug, int Count);

public sealed record ProductFacetsDto(
    IReadOnlyList<BrandFacetDto> Brands,
    decimal? MinPrice,
    decimal? MaxPrice,
    int InStockCount,
    int TotalCount,
    IReadOnlyList<SpecFacetDto> Specifications);

public sealed record AutocompleteProductDto(string Name, string Slug, decimal Price, string? ImageUrl, string CategoryName);
public sealed record AutocompleteResultDto(
    IReadOnlyList<AutocompleteProductDto> Products,
    IReadOnlyList<CategoryRefDto> Categories,
    IReadOnlyList<BrandDto> Brands);
