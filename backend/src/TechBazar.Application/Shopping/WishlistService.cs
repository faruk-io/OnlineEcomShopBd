using Microsoft.EntityFrameworkCore;
using TechBazar.Application.Abstractions;
using TechBazar.Application.Catalog;
using TechBazar.Application.Catalog.Dtos;
using TechBazar.Application.Common;
using TechBazar.Domain.Entities;

namespace TechBazar.Application.Shopping;

public sealed class WishlistService(IApplicationDbContext db) : IWishlistService
{
    private const int MaxItems = 200;

    public Task<IReadOnlyList<ProductListItemDto>> GetAsync(Guid userId, CancellationToken ct = default) => BuildAsync(userId, ct);

    public async Task<IReadOnlyList<ProductListItemDto>> AddAsync(Guid userId, int productId, CancellationToken ct = default)
    {
        if (!await db.Products.AnyAsync(p => p.Id == productId && p.IsActive, ct)) throw new NotFoundException("Product not found.");
        var wishlist = await GetOrCreateAsync(userId, ct);
        if (wishlist.Items.All(i => i.IsDeleted || i.ProductId != productId))
        {
            if (wishlist.Items.Count(i => !i.IsDeleted) >= MaxItems) throw new ConflictException("Your wishlist is full.");
            wishlist.Items.Add(new WishlistItem { ProductId = productId });
            await db.SaveChangesAsync(ct);
        }
        return await BuildAsync(userId, ct);
    }

    public async Task<IReadOnlyList<ProductListItemDto>> RemoveAsync(Guid userId, int productId, CancellationToken ct = default)
    {
        var item = await db.WishlistItems.FirstOrDefaultAsync(i => i.Wishlist.UserId == userId && i.ProductId == productId, ct);
        if (item is not null)
        {
            db.WishlistItems.Remove(item);
            await db.SaveChangesAsync(ct);
        }
        return await BuildAsync(userId, ct);
    }

    public async Task<IReadOnlyList<ProductListItemDto>> MergeAsync(Guid userId, IReadOnlyList<int> productIds, CancellationToken ct = default)
    {
        var ids = productIds.Distinct().ToList();
        if (ids.Count == 0) return await BuildAsync(userId, ct);

        var valid = await db.Products.AsNoTracking().Where(p => p.IsActive && ids.Contains(p.Id)).Select(p => p.Id).ToListAsync(ct);
        var wishlist = await GetOrCreateAsync(userId, ct);
        var existing = wishlist.Items.Where(i => !i.IsDeleted).Select(i => i.ProductId).ToHashSet();
        foreach (var id in valid.Where(id => !existing.Contains(id)).Take(MaxItems - existing.Count))
            wishlist.Items.Add(new WishlistItem { ProductId = id });
        await db.SaveChangesAsync(ct);
        return await BuildAsync(userId, ct);
    }

    private async Task<Wishlist> GetOrCreateAsync(Guid userId, CancellationToken ct)
    {
        var wishlist = await db.Wishlists.Include(w => w.Items).FirstOrDefaultAsync(w => w.UserId == userId, ct);
        if (wishlist is not null) return wishlist;

        wishlist = new Wishlist { UserId = userId };
        db.Wishlists.Add(wishlist);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            db.Wishlists.Remove(wishlist);
            return await db.Wishlists.Include(w => w.Items).FirstAsync(w => w.UserId == userId, ct);
        }
        return wishlist;
    }

    private async Task<IReadOnlyList<ProductListItemDto>> BuildAsync(Guid userId, CancellationToken ct) =>
        await db.WishlistItems.AsNoTracking()
            .Where(i => i.Wishlist.UserId == userId && i.Product.IsActive)
            .OrderByDescending(i => i.CreatedAt).ThenByDescending(i => i.Id)
            .Select(i => i.Product)
            .Select(ProductProjections.ListItem)
            .ToListAsync(ct);
}
