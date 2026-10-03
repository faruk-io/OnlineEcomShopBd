using Microsoft.EntityFrameworkCore;
using TechBazar.Application.Abstractions;
using TechBazar.Application.Catalog;
using TechBazar.Application.Common;
using TechBazar.Domain.Common;
using TechBazar.Domain.Entities;

namespace TechBazar.Application.Admin;

public sealed class AdminProductService(IApplicationDbContext db) : IAdminProductService
{
    public async Task<PagedResult<AdminProductListItemDto>> ListAsync(string? search, int? categoryId, bool? lowStockOnly, int page, int pageSize, CancellationToken ct = default)
    {
        var q = db.Products.AsNoTracking().AsQueryable();
        if (categoryId is { } cid) q = q.Where(p => p.CategoryId == cid);
        if (lowStockOnly == true) q = q.Where(p => p.StockQuantity <= 5 && p.StockStatus == Domain.Enums.StockStatus.InStock);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = TextSearch.Contains(search.Trim());
            q = q.Where(p => EF.Functions.Like(p.Name, pattern, TextSearch.Escape) || EF.Functions.Like(p.Sku, pattern, TextSearch.Escape));
        }
        var total = await q.CountAsync(ct);
        var items = await q.OrderByDescending(p => p.UpdatedAt ?? p.CreatedAt).ThenByDescending(p => p.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(p => new AdminProductListItemDto(p.Id, p.Name, p.Slug, p.Sku, p.Brand.Name, p.Category.Name, p.Price, p.DiscountPrice, p.StockStatus, p.StockQuantity,
                p.IsActive, p.IsFeatured, p.Images.OrderByDescending(i => i.IsPrimary).ThenBy(i => i.DisplayOrder).Select(i => i.Url).FirstOrDefault(), p.UpdatedAt))
            .ToListAsync(ct);
        return new PagedResult<AdminProductListItemDto>(items, page, pageSize, total);
    }

    public async Task<AdminProductDetailDto> GetAsync(int id, CancellationToken ct = default)
    {
        var p = await db.Products.AsNoTracking().Include(x => x.KeyFeatures).Include(x => x.Specifications).Include(x => x.Images)
                    .FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Product not found.");
        return ToDetail(p);
    }

    public async Task<AdminProductDetailDto> CreateAsync(SaveProductRequest r, CancellationToken ct = default)
    {
        var slug = ResolveSlug(r);
        await EnsureUniqueAsync(slug, r.Sku.Trim(), null, ct);
        await EnsureRefsAsync(r, ct);
        var product = new Product();
        Apply(product, r, slug);
        db.Products.Add(product);
        await db.SaveChangesAsync(ct);
        return ToDetail(product);
    }

    public async Task<AdminProductDetailDto> UpdateAsync(int id, SaveProductRequest r, CancellationToken ct = default)
    {
        var product = await db.Products.Include(x => x.KeyFeatures).Include(x => x.Specifications).Include(x => x.Images)
                          .FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Product not found.");
        var slug = ResolveSlug(r);
        await EnsureUniqueAsync(slug, r.Sku.Trim(), id, ct);
        await EnsureRefsAsync(r, ct);

        // Replace-all semantics for the children: the editor always submits the complete list.
        db.ProductKeyFeatures.RemoveRange(product.KeyFeatures.ToList());
        db.ProductSpecifications.RemoveRange(product.Specifications.ToList());
        db.ProductImages.RemoveRange(product.Images.ToList());
        product.KeyFeatures.Clear(); product.Specifications.Clear(); product.Images.Clear();

        Apply(product, r, slug);
        await db.SaveChangesAsync(ct);
        return ToDetail(product);
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var product = await db.Products.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Product not found.");
        db.Products.Remove(product); // soft delete; order history keeps its snapshot
        await db.SaveChangesAsync(ct);
    }

    private static string ResolveSlug(SaveProductRequest r)
    {
        var slug = string.IsNullOrWhiteSpace(r.Slug) ? SlugHelper.Slugify(r.Name) : r.Slug.Trim();
        return slug.Length > 0 ? slug : throw new FluentValidation.ValidationException([new FluentValidation.Results.ValidationFailure("slug", "Could not build a slug from the name.")]);
    }

    private async Task EnsureUniqueAsync(string slug, string sku, int? selfId, CancellationToken ct)
    {
        if (await db.Products.AnyAsync(p => p.Slug == slug && p.Id != selfId, ct)) throw new ConflictException($"Another product already uses the slug '{slug}'.");
        if (await db.Products.AnyAsync(p => p.Sku == sku && p.Id != selfId, ct)) throw new ConflictException($"Another product already uses the SKU '{sku}'.");
    }

    private async Task EnsureRefsAsync(SaveProductRequest r, CancellationToken ct)
    {
        if (!await db.Categories.AnyAsync(c => c.Id == r.CategoryId, ct)) throw new NotFoundException("Category not found.");
        if (!await db.Brands.AnyAsync(b => b.Id == r.BrandId, ct)) throw new NotFoundException("Brand not found.");
    }

    private static void Apply(Product p, SaveProductRequest r, string slug)
    {
        p.Name = r.Name.Trim(); p.Slug = slug; p.Sku = r.Sku.Trim();
        p.CategoryId = r.CategoryId; p.BrandId = r.BrandId;
        p.Price = r.Price; p.DiscountPrice = r.DiscountPrice;
        p.StockStatus = r.StockStatus; p.StockQuantity = r.StockQuantity;
        p.WarrantyMonths = r.WarrantyMonths; p.WarrantyDetails = Clean(r.WarrantyDetails);
        p.ShortDescription = Clean(r.ShortDescription); p.Description = Clean(r.Description);
        p.IsFeatured = r.IsFeatured; p.IsActive = r.IsActive;
        p.RecalculateEffectivePrice();

        var order = 0;
        foreach (var f in r.KeyFeatures.Where(f => !string.IsNullOrWhiteSpace(f))) p.KeyFeatures.Add(new ProductKeyFeature { Text = f.Trim(), DisplayOrder = order++ });
        order = 0;
        foreach (var s in r.Specifications)
        {
            var key = s.Key.Trim(); var value = s.Value.Trim();
            p.Specifications.Add(new ProductSpecification
            {
                Group = s.Group.Trim(), Key = key, Value = value, DisplayOrder = order++,
                IsFilterable = s.IsFilterable ?? SpecRules.IsFilterable(key),
                NumericValue = SpecRules.NumericFor(key, value),
            });
        }
        order = 0;
        var primarySeen = false;
        foreach (var i in r.Images)
        {
            var primary = i.IsPrimary && !primarySeen; primarySeen |= primary;
            p.Images.Add(new ProductImage { Url = i.Url.Trim(), AltText = Clean(i.AltText) ?? p.Name, IsPrimary = primary, DisplayOrder = order++ });
        }
        if (p.Images.Count > 0 && !primarySeen) p.Images.First().IsPrimary = true;
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static AdminProductDetailDto ToDetail(Product p) => new(
        p.Id, p.Name, p.Slug, p.Sku, p.CategoryId, p.BrandId, p.Price, p.DiscountPrice, p.StockStatus, p.StockQuantity, p.WarrantyMonths, p.WarrantyDetails,
        p.ShortDescription, p.Description, p.IsFeatured, p.IsActive,
        p.KeyFeatures.OrderBy(k => k.DisplayOrder).Select(k => k.Text).ToList(),
        p.Specifications.OrderBy(s => s.DisplayOrder).Select(s => new SpecInput(s.Group, s.Key, s.Value, s.IsFilterable)).ToList(),
        p.Images.OrderBy(i => i.DisplayOrder).Select(i => new ImageInput(i.Url, i.AltText, i.IsPrimary)).ToList());
}
