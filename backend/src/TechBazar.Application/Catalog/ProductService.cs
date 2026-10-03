using Microsoft.EntityFrameworkCore;
using TechBazar.Application.Abstractions;
using TechBazar.Application.Catalog.Dtos;
using TechBazar.Application.Common;
using TechBazar.Domain.Entities;
using TechBazar.Domain.Enums;

namespace TechBazar.Application.Catalog;

public sealed class ProductService(IApplicationDbContext db, ICategoryHierarchy hierarchy) : IProductService
{
    public async Task<PagedResult<ProductListItemDto>> GetProductsAsync(ProductListQuery query, CancellationToken ct = default)
    {
        var filtered = await ApplyFiltersAsync(db.Products.AsNoTracking().Where(p => p.IsActive), query, ct);
        if (filtered is null) return new PagedResult<ProductListItemDto>([], query.Page, query.PageSize, 0);

        var total = await filtered.CountAsync(ct);
        var items = await BuildPageQuery(filtered, query).ToListAsync(ct);

        return new PagedResult<ProductListItemDto>(items, query.Page, query.PageSize, total);
    }

    public async Task<ProductDetailDto> GetBySlugAsync(string slug, CancellationToken ct = default)
    {
        // Scalar row first, then one small query per child collection (avoids a cartesian-product join).
        var p = await db.Products.AsNoTracking()
            .Where(x => x.IsActive && x.Slug == slug)
            .Select(x => new
            {
                x.Id, x.Name, x.Slug, x.Sku, x.ShortDescription, x.Description, x.Price, x.DiscountPrice, x.EffectivePrice,
                x.StockStatus, x.WarrantyMonths, x.WarrantyDetails, x.RatingAverage, x.ReviewCount, x.CategoryId,
                Brand = new BrandDto(x.Brand.Id, x.Brand.Name, x.Brand.Slug, x.Brand.LogoUrl),
            })
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException($"Product '{slug}' was not found.");

        var images = await db.ProductImages.AsNoTracking().Where(i => i.ProductId == p.Id)
            .OrderByDescending(i => i.IsPrimary).ThenBy(i => i.DisplayOrder)
            .Select(i => new ProductImageDto(i.Url, i.AltText, i.IsPrimary)).ToListAsync(ct);

        var features = await db.ProductKeyFeatures.AsNoTracking().Where(k => k.ProductId == p.Id)
            .OrderBy(k => k.DisplayOrder).Select(k => k.Text).ToListAsync(ct);

        var specs = await db.ProductSpecifications.AsNoTracking().Where(s => s.ProductId == p.Id)
            .OrderBy(s => s.DisplayOrder).Select(s => new { s.Group, s.Key, s.Value, s.DisplayOrder }).ToListAsync(ct);

        var reviews = await db.Reviews.AsNoTracking().Where(r => r.ProductId == p.Id && r.IsApproved)
            .OrderByDescending(r => r.CreatedAt).Take(10)
            .Select(r => new ReviewDto(r.ReviewerName, r.Rating, r.Title, r.Comment, r.CreatedAt)).ToListAsync(ct);

        var path = await hierarchy.GetPathAsync(p.CategoryId, ct);
        var groups = specs
            .GroupBy(s => s.Group)
            .OrderBy(g => g.Min(s => s.DisplayOrder))
            .Select(g => new SpecificationGroupDto(g.Key, g.OrderBy(s => s.DisplayOrder).Select(s => new SpecificationItemDto(s.Key, s.Value)).ToList()))
            .ToList();

        return new ProductDetailDto
        {
            Id = p.Id, Name = p.Name, Slug = p.Slug, Sku = p.Sku, ShortDescription = p.ShortDescription, Description = p.Description,
            Price = p.Price, DiscountPrice = p.DiscountPrice, EffectivePrice = p.EffectivePrice, StockStatus = p.StockStatus,
            WarrantyMonths = p.WarrantyMonths, WarrantyDetails = p.WarrantyDetails, RatingAverage = p.RatingAverage, ReviewCount = p.ReviewCount,
            Brand = p.Brand, Category = path[^1], Breadcrumbs = path, Images = images, KeyFeatures = features,
            Specifications = groups, Reviews = reviews,
        };
    }

    public async Task<IReadOnlyList<ProductListItemDto>> GetRelatedAsync(string slug, int count, CancellationToken ct = default)
    {
        var source = await db.Products.AsNoTracking()
            .Where(p => p.IsActive && p.Slug == slug)
            .Select(p => new { p.Id, p.CategoryId, p.BrandId, p.EffectivePrice })
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException($"Product '{slug}' was not found.");

        var lo = source.EffectivePrice * 0.5m;
        var hi = source.EffectivePrice * 2m;

        var close = await db.Products.AsNoTracking()
            .Where(p => p.IsActive && p.Id != source.Id && p.CategoryId == source.CategoryId && p.EffectivePrice >= lo && p.EffectivePrice <= hi)
            .OrderBy(p => p.BrandId == source.BrandId ? 0 : 1)
            .ThenByDescending(p => p.SoldCount)
            .ThenBy(p => p.Id)
            .Take(count)
            .Select(ProductProjections.ListItem)
            .ToListAsync(ct);

        if (close.Count >= count) return close;

        var taken = close.Select(c => c.Id).Append(source.Id).ToList();
        var fill = await db.Products.AsNoTracking()
            .Where(p => p.IsActive && p.CategoryId == source.CategoryId && !taken.Contains(p.Id))
            .OrderByDescending(p => p.SoldCount).ThenBy(p => p.Id)
            .Take(count - close.Count)
            .Select(ProductProjections.ListItem)
            .ToListAsync(ct);

        return [.. close, .. fill];
    }

    public async Task<ProductFacetsDto> GetFacetsAsync(string? categorySlug, CancellationToken ct = default)
    {
        var scope = await ApplyFiltersAsync(
            db.Products.AsNoTracking().Where(p => p.IsActive),
            new ProductListQuery { Category = categorySlug }, ct);

        if (scope is null) return new ProductFacetsDto([], null, null, 0, 0, []);

        var brands = await scope.GroupBy(p => new { p.Brand.Name, p.Brand.Slug })
            .Select(g => new BrandFacetDto(g.Key.Name, g.Key.Slug, g.Count()))
            .ToListAsync(ct);

        var total = await scope.CountAsync(ct);
        var inStock = await scope.CountAsync(p => p.StockStatus == StockStatus.InStock, ct);
        var min = total == 0 ? (decimal?)null : await scope.MinAsync(p => p.EffectivePrice, ct);
        var max = total == 0 ? (decimal?)null : await scope.MaxAsync(p => p.EffectivePrice, ct);

        var scopedIds = scope.Select(p => p.Id);
        var specRows = await db.ProductSpecifications.AsNoTracking()
            .Where(s => s.IsFilterable && scopedIds.Contains(s.ProductId))
            .GroupBy(s => new { s.Key, s.Value })
            .Select(g => new { g.Key.Key, g.Key.Value, Count = g.Select(x => x.ProductId).Distinct().Count() })
            .ToListAsync(ct);

        var specs = specRows
            .GroupBy(r => r.Key)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => new SpecFacetDto(g.Key, g.OrderByDescending(v => v.Count).ThenBy(v => v.Value, StringComparer.OrdinalIgnoreCase)
                .Select(v => new FacetValueDto(v.Value, v.Count)).ToList()))
            .ToList();

        return new ProductFacetsDto(
            brands.OrderByDescending(b => b.Count).ThenBy(b => b.Name).ToList(), min, max, inStock, total, specs);
    }

    /// <summary>Applies all filters of <paramref name="query"/>. Returns null when the requested category does not exist.</summary>
    internal async Task<IQueryable<Product>?> ApplyFiltersAsync(IQueryable<Product> products, ProductListQuery query, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(query.Category))
        {
            var ids = await hierarchy.GetSelfAndDescendantIdsAsync(query.Category.Trim(), ct);
            if (ids.Count == 0) return null;
            products = products.Where(p => ids.Contains(p.CategoryId));
        }

        var brands = (query.Brand ?? [])
            .SelectMany(b => b.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Select(b => b.ToLowerInvariant()).Distinct().ToList();
        if (brands.Count > 0) products = products.Where(p => brands.Contains(p.Brand.Slug));

        if (query.MinPrice is { } min) products = products.Where(p => p.EffectivePrice >= min);
        if (query.MaxPrice is { } max) products = products.Where(p => p.EffectivePrice <= max);
        if (query.OnSale == true) products = products.Where(p => p.EffectivePrice < p.Price);
        if (query.InStock == true) products = products.Where(p => p.StockStatus == StockStatus.InStock);

        foreach (var (key, values) in SpecFilterParser.Parse(query.Spec))
        {
            var k = key;
            var v = values.ToList();
            products = products.Where(p => p.Specifications.Any(s => s.Key == k && v.Contains(s.Value)));
        }

        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            foreach (var token in query.Q.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(5))
            {
                var pattern = TextSearch.Contains(token);
                products = products.Where(p =>
                    EF.Functions.Like(p.Name, pattern, TextSearch.Escape) || EF.Functions.Like(p.Sku, pattern, TextSearch.Escape) ||
                    EF.Functions.Like(p.Brand.Name, pattern, TextSearch.Escape) || EF.Functions.Like(p.Category.Name, pattern, TextSearch.Escape));
            }
        }

        return products;
    }

    /// <summary>Sort + page + projection (internal so tests can compile it against the SQL Server provider).</summary>
    internal static IQueryable<ProductListItemDto> BuildPageQuery(IQueryable<Product> filtered, ProductListQuery query) =>
        ApplySort(filtered, query.ParsedSort)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(ProductProjections.ListItem);

    private static IQueryable<Product> ApplySort(IQueryable<Product> q, ProductSort sort) => sort switch
    {
        ProductSort.PriceAsc => q.OrderBy(p => p.EffectivePrice).ThenBy(p => p.Id),
        ProductSort.PriceDesc => q.OrderByDescending(p => p.EffectivePrice).ThenBy(p => p.Id),
        ProductSort.Newest => q.OrderByDescending(p => p.CreatedAt).ThenByDescending(p => p.Id),
        _ => q.OrderByDescending(p => p.SoldCount).ThenByDescending(p => p.ViewCount).ThenBy(p => p.Id),
    };
}
