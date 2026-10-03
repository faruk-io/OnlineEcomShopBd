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
        var contains = TextSearch.Contains(q);
        var prefix = TextSearch.StartsWith(q);

        var products = await db.Products.AsNoTracking()
            .Where(p => p.IsActive && (EF.Functions.Like(p.Name, contains, TextSearch.Escape) ||
                                       EF.Functions.Like(p.Sku, contains, TextSearch.Escape) ||
                                       EF.Functions.Like(p.Brand.Name, contains, TextSearch.Escape)))
            .OrderByDescending(p => EF.Functions.Like(p.Name, prefix, TextSearch.Escape))
            .ThenByDescending(p => p.SoldCount)
            .ThenBy(p => p.Id)
            .Take(limit)
            .Select(p => new AutocompleteProductDto(
                p.Name, p.Slug, p.EffectivePrice,
                p.Images.OrderByDescending(i => i.IsPrimary).ThenBy(i => i.DisplayOrder).Select(i => i.Url).FirstOrDefault(),
                p.Category.Name))
            .ToListAsync(ct);

        var categories = await db.Categories.AsNoTracking()
            .Where(c => c.IsActive && EF.Functions.Like(c.Name, contains, TextSearch.Escape)).OrderBy(c => c.Name).Take(5)
            .Select(c => new CategoryRefDto(c.Id, c.Name, c.Slug)).ToListAsync(ct);

        var brands = await db.Brands.AsNoTracking()
            .Where(b => b.IsActive && EF.Functions.Like(b.Name, contains, TextSearch.Escape)).OrderBy(b => b.Name).Take(5)
            .Select(b => new BrandDto(b.Id, b.Name, b.Slug, b.LogoUrl)).ToListAsync(ct);

        return new AutocompleteResultDto(products, categories, brands);
    }
}
