namespace TechBazar.Application.Catalog;

public enum ProductSort { Popularity, Newest, PriceAsc, PriceDesc }

/// <summary>
/// Query-string contract of <c>GET /api/v1/products</c>.
/// Example: <c>?category=processor&amp;brand=intel&amp;brand=amd&amp;minPrice=10000&amp;inStock=true&amp;spec=Socket:AM5&amp;sort=price_asc&amp;page=1&amp;pageSize=20</c>.
/// Repeated <c>brand</c> values are OR-ed; repeated <c>spec</c> values for the same key are OR-ed, different keys are AND-ed.
/// </summary>
public class ProductListQuery
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 60;
    public static readonly string[] AllowedSorts = ["popularity", "newest", "price_asc", "price_desc"];

    /// <summary>Category slug; products of all descendant categories are included.</summary>
    public string? Category { get; set; }
    public List<string>? Brand { get; set; }
    public decimal? MinPrice { get; set; }
    public decimal? MaxPrice { get; set; }
    public bool? InStock { get; set; }
    /// <summary>Only products currently discounted (<c>DiscountPrice &lt; Price</c>).</summary>
    public bool? OnSale { get; set; }
    /// <summary>Spec filters in <c>Key:Value</c> form (e.g. <c>Socket:AM5</c>, <c>RAM Type:DDR5</c>).</summary>
    public List<string>? Spec { get; set; }
    /// <summary>Free-text search (name, SKU, brand, category).</summary>
    public string? Q { get; set; }
    public string? Sort { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = DefaultPageSize;

    public ProductSort ParsedSort => ParseSort(Sort);

    public static ProductSort ParseSort(string? sort) => sort?.Trim().ToLowerInvariant() switch
    {
        "newest" => ProductSort.Newest,
        "price_asc" => ProductSort.PriceAsc,
        "price_desc" => ProductSort.PriceDesc,
        _ => ProductSort.Popularity,
    };
}
