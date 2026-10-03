using Microsoft.EntityFrameworkCore;
using TechBazar.Application.Abstractions;
using TechBazar.Application.Catalog.Dtos;

namespace TechBazar.Application.Catalog;

public sealed class BrandService(IApplicationDbContext db) : IBrandService
{
    public async Task<IReadOnlyList<BrandListItemDto>> GetAllAsync(CancellationToken ct = default) =>
        await db.Brands.AsNoTracking().Where(b => b.IsActive)
            .OrderBy(b => b.Name)
            .Select(b => new BrandListItemDto(b.Id, b.Name, b.Slug, b.LogoUrl, b.Products.Count(p => p.IsActive)))
            .ToListAsync(ct);
}

public sealed class SearchService(IApplicationDbContext db) : ISearchService
{
    public async Task<AutocompleteResultDto> AutocompleteAsync(string query, int limit, CancellationToken ct = default)
    {
        var q = query.Trim();

        var products = await db.Products.AsNoTracking()
            .Where(p => p.IsActive && (p.Name.Contains(q) || p.Sku.Contains(q) || p.Brand.Name.Contains(q)))
            .OrderByDescending(p => p.Name.StartsWith(q))
            .ThenByDescending(p => p.SoldCount)
            .ThenBy(p => p.Id)
            .Take(limit)
            .Select(p => new AutocompleteProductDto(
                p.Name, p.Slug, p.EffectivePrice,
                p.Images.OrderByDescending(i => i.IsPrimary).ThenBy(i => i.DisplayOrder).Select(i => i.Url).FirstOrDefault(),
                p.Category.Name))
            .ToListAsync(ct);

        var categories = await db.Categories.AsNoTracking()
            .Where(c => c.IsActive && c.Name.Contains(q)).OrderBy(c => c.Name).Take(5)
            .Select(c => new CategoryRefDto(c.Id, c.Name, c.Slug)).ToListAsync(ct);

        var brands = await db.Brands.AsNoTracking()
            .Where(b => b.IsActive && b.Name.Contains(q)).OrderBy(b => b.Name).Take(5)
            .Select(b => new BrandDto(b.Id, b.Name, b.Slug, b.LogoUrl)).ToListAsync(ct);

        return new AutocompleteResultDto(products, categories, brands);
    }
}
