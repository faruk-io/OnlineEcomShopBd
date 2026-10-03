using Microsoft.EntityFrameworkCore;
using TechBazar.Application.Abstractions;
using TechBazar.Application.Common;
using TechBazar.Domain.Common;
using TechBazar.Domain.Entities;

namespace TechBazar.Application.Admin;

public sealed class AdminCatalogService(IApplicationDbContext db) : IAdminCatalogService
{
    // ------------------------------------------------------------ categories
    public async Task<IReadOnlyList<AdminCategoryDto>> ListCategoriesAsync(CancellationToken ct = default)
    {
        var cats = await db.Categories.AsNoTracking().OrderBy(c => c.DisplayOrder).ThenBy(c => c.Name).ToListAsync(ct);
        var counts = await db.Products.AsNoTracking().GroupBy(p => p.CategoryId).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N, ct);
        var names = cats.ToDictionary(c => c.Id, c => c.Name);
        return cats.Select(c => ToDto(c, names.GetValueOrDefault(c.ParentId ?? 0), counts.GetValueOrDefault(c.Id), cats.Count(x => x.ParentId == c.Id))).ToList();
    }

    public async Task<AdminCategoryDto> CreateCategoryAsync(SaveCategoryRequest r, CancellationToken ct = default)
    {
        var slug = Slug(r.Slug, r.Name);
        if (await db.Categories.AnyAsync(c => c.Slug == slug, ct)) throw new ConflictException($"A category with the slug '{slug}' already exists.");
        if (r.ParentId is { } pid && !await db.Categories.AnyAsync(c => c.Id == pid, ct)) throw new NotFoundException("Parent category not found.");
        var c = new Category(); Apply(c, r, slug);
        db.Categories.Add(c);
        await db.SaveChangesAsync(ct);
        return (await ListCategoriesAsync(ct)).First(x => x.Id == c.Id);
    }

    public async Task<AdminCategoryDto> UpdateCategoryAsync(int id, SaveCategoryRequest r, CancellationToken ct = default)
    {
        var all = await db.Categories.ToListAsync(ct);
        var c = all.FirstOrDefault(x => x.Id == id) ?? throw new NotFoundException("Category not found.");
        var slug = Slug(r.Slug, r.Name);
        if (all.Any(x => x.Slug == slug && x.Id != id)) throw new ConflictException($"A category with the slug '{slug}' already exists.");
        if (r.ParentId is { } pid)
        {
            if (all.All(x => x.Id != pid)) throw new NotFoundException("Parent category not found.");
            // A category cannot sit under itself or one of its own descendants (that would orphan the whole branch).
            for (int? cur = pid; cur is not null; cur = all.FirstOrDefault(x => x.Id == cur)?.ParentId)
                if (cur == id) throw new ConflictException("A category cannot be moved under itself or one of its subcategories.");
        }
        Apply(c, r, slug);
        await db.SaveChangesAsync(ct);
        return (await ListCategoriesAsync(ct)).First(x => x.Id == id);
    }

    public async Task DeleteCategoryAsync(int id, CancellationToken ct = default)
    {
        var c = await db.Categories.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Category not found.");
        if (await db.Categories.AnyAsync(x => x.ParentId == id, ct)) throw new ConflictException("Delete or move the subcategories first.");
        if (await db.Products.AnyAsync(p => p.CategoryId == id, ct)) throw new ConflictException("This category still has products. Move or delete them first.");
        db.Categories.Remove(c);
        await db.SaveChangesAsync(ct);
    }

    // ------------------------------------------------------------ brands
    public async Task<IReadOnlyList<AdminBrandDto>> ListBrandsAsync(CancellationToken ct = default) =>
        await db.Brands.AsNoTracking().OrderBy(b => b.Name)
            .Select(b => new AdminBrandDto(b.Id, b.Name, b.Slug, b.Description, b.LogoUrl, b.IsActive, b.Products.Count))
            .ToListAsync(ct);

    public async Task<AdminBrandDto> CreateBrandAsync(SaveBrandRequest r, CancellationToken ct = default)
    {
        var slug = Slug(r.Slug, r.Name);
        if (await db.Brands.AnyAsync(b => b.Slug == slug || b.Name == r.Name.Trim(), ct)) throw new ConflictException("A brand with this name or slug already exists.");
        var b = new Brand(); Apply(b, r, slug);
        db.Brands.Add(b);
        await db.SaveChangesAsync(ct);
        return new AdminBrandDto(b.Id, b.Name, b.Slug, b.Description, b.LogoUrl, b.IsActive, 0);
    }

    public async Task<AdminBrandDto> UpdateBrandAsync(int id, SaveBrandRequest r, CancellationToken ct = default)
    {
        var b = await db.Brands.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Brand not found.");
        var slug = Slug(r.Slug, r.Name);
        if (await db.Brands.AnyAsync(x => x.Id != id && (x.Slug == slug || x.Name == r.Name.Trim()), ct)) throw new ConflictException("A brand with this name or slug already exists.");
        Apply(b, r, slug);
        await db.SaveChangesAsync(ct);
        return new AdminBrandDto(b.Id, b.Name, b.Slug, b.Description, b.LogoUrl, b.IsActive, await db.Products.CountAsync(p => p.BrandId == id, ct));
    }

    public async Task DeleteBrandAsync(int id, CancellationToken ct = default)
    {
        var b = await db.Brands.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Brand not found.");
        if (await db.Products.AnyAsync(p => p.BrandId == id, ct)) throw new ConflictException("This brand still has products. Reassign or delete them first.");
        db.Brands.Remove(b);
        await db.SaveChangesAsync(ct);
    }

    // ------------------------------------------------------------ helpers
    private static string Slug(string? given, string name)
    {
        var s = string.IsNullOrWhiteSpace(given) ? SlugHelper.Slugify(name) : given.Trim();
        return s.Length > 0 ? s : throw new FluentValidation.ValidationException([new FluentValidation.Results.ValidationFailure("slug", "Could not build a slug from the name.")]);
    }

    private static void Apply(Category c, SaveCategoryRequest r, string slug)
    {
        c.Name = r.Name.Trim(); c.Slug = slug; c.Description = Clean(r.Description); c.ImageUrl = Clean(r.ImageUrl);
        c.ParentId = r.ParentId; c.DisplayOrder = r.DisplayOrder; c.IsActive = r.IsActive;
    }

    private static void Apply(Brand b, SaveBrandRequest r, string slug)
    {
        b.Name = r.Name.Trim(); b.Slug = slug; b.Description = Clean(r.Description); b.LogoUrl = Clean(r.LogoUrl); b.IsActive = r.IsActive;
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static AdminCategoryDto ToDto(Category c, string? parentName, int products, int children) =>
        new(c.Id, c.Name, c.Slug, c.Description, c.ImageUrl, c.ParentId, parentName, c.DisplayOrder, c.IsActive, products, children);
}
