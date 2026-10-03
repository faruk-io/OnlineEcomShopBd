using Microsoft.EntityFrameworkCore;
using TechBazar.Application.Abstractions;
using TechBazar.Application.Catalog.Dtos;
using TechBazar.Application.Common;

namespace TechBazar.Application.Catalog;

public sealed class CategoryService(IApplicationDbContext db, ICategoryHierarchy hierarchy) : ICategoryService
{
    public async Task<IReadOnlyList<CategoryTreeNodeDto>> GetTreeAsync(CancellationToken ct = default)
    {
        var cats = await db.Categories.AsNoTracking().Where(c => c.IsActive)
            .OrderBy(c => c.DisplayOrder).ThenBy(c => c.Name)
            .Select(c => new { c.Id, c.ParentId, c.Name, c.Slug, c.ImageUrl }).ToListAsync(ct);

        var directCounts = await db.Products.AsNoTracking().Where(p => p.IsActive)
            .GroupBy(p => p.CategoryId).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);

        var nodes = cats.ToDictionary(c => c.Id, c => new CategoryTreeNodeDto
        {
            Id = c.Id, Name = c.Name, Slug = c.Slug, ImageUrl = c.ImageUrl,
            ProductCount = directCounts.GetValueOrDefault(c.Id),
        });

        var roots = new List<CategoryTreeNodeDto>();
        foreach (var c in cats)
        {
            if (c.ParentId is { } pid && nodes.TryGetValue(pid, out var parent)) parent.Children.Add(nodes[c.Id]);
            else roots.Add(nodes[c.Id]);
        }

        static int Rollup(CategoryTreeNodeDto n)
        {
            foreach (var child in n.Children) n.ProductCount += Rollup(child);
            return n.ProductCount;
        }
        foreach (var r in roots) Rollup(r);
        return roots;
    }

    public async Task<CategoryDetailDto> GetBySlugAsync(string slug, CancellationToken ct = default)
    {
        var c = await db.Categories.AsNoTracking()
            .Where(x => x.IsActive && x.Slug == slug)
            .Select(x => new { x.Id, x.Name, x.Slug, x.Description, x.ImageUrl })
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException($"Category '{slug}' was not found.");

        var children = await db.Categories.AsNoTracking()
            .Where(x => x.IsActive && x.ParentId == c.Id)
            .OrderBy(x => x.DisplayOrder).ThenBy(x => x.Name)
            .Select(x => new CategoryRefDto(x.Id, x.Name, x.Slug)).ToListAsync(ct);

        return new CategoryDetailDto(c.Id, c.Name, c.Slug, c.Description, c.ImageUrl, await hierarchy.GetPathAsync(c.Id, ct), children);
    }
}
