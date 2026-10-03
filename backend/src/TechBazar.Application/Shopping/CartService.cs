using Microsoft.EntityFrameworkCore;
using TechBazar.Application.Abstractions;
using TechBazar.Application.Common;
using TechBazar.Domain.Entities;
using TechBazar.Domain.Enums;

namespace TechBazar.Application.Shopping;

public sealed class CartService(IApplicationDbContext db) : ICartService
{
    public Task<CartDto> GetAsync(Guid userId, CancellationToken ct = default) => BuildAsync(userId, ct);

    public async Task<CartDto> SetItemAsync(Guid userId, int productId, int quantity, CancellationToken ct = default)
    {
        var product = await db.Products.AsNoTracking()
            .Where(p => p.Id == productId && p.IsActive)
            .Select(p => new { p.EffectivePrice, p.StockStatus, p.Name })
            .FirstOrDefaultAsync(ct) ?? throw new NotFoundException("Product not found.");

        if (product.StockStatus is not (StockStatus.InStock or StockStatus.PreOrder))
            throw new ConflictException($"'{product.Name}' is currently not available for purchase.");

        var cart = await GetOrCreateCartAsync(userId, ct);
        var line = cart.Items.FirstOrDefault(i => i.ProductId == productId);
        if (line is null)
        {
            if (cart.Items.Count >= CartLimits.MaxLines) throw new ConflictException("Your cart is full.");
            cart.Items.Add(new CartItem { ProductId = productId, Quantity = quantity, UnitPrice = product.EffectivePrice });
        }
        else
        {
            line.Quantity = quantity;
            line.UnitPrice = product.EffectivePrice;
        }
        await db.SaveChangesAsync(ct);
        return await BuildAsync(userId, ct);
    }

    public async Task<CartDto> RemoveItemAsync(Guid userId, int productId, CancellationToken ct = default)
    {
        var line = await db.CartItems.FirstOrDefaultAsync(i => i.Cart.UserId == userId && i.ProductId == productId, ct);
        if (line is not null)
        {
            db.CartItems.Remove(line);
            await db.SaveChangesAsync(ct);
        }
        return await BuildAsync(userId, ct);
    }

    public async Task<CartDto> ClearAsync(Guid userId, CancellationToken ct = default)
    {
        var lines = await db.CartItems.Where(i => i.Cart.UserId == userId).ToListAsync(ct);
        db.CartItems.RemoveRange(lines);
        await db.SaveChangesAsync(ct);
        return new CartDto([]);
    }

    public async Task<CartDto> MergeAsync(Guid userId, IReadOnlyList<CartMergeLine> lines, CancellationToken ct = default)
    {
        var wanted = lines.GroupBy(l => l.ProductId).ToDictionary(g => g.Key, g => g.Sum(l => l.Quantity));
        if (wanted.Count == 0) return await BuildAsync(userId, ct);

        var ids = wanted.Keys.ToList();
        var products = await db.Products.AsNoTracking()
            .Where(p => p.IsActive && ids.Contains(p.Id) && (p.StockStatus == StockStatus.InStock || p.StockStatus == StockStatus.PreOrder))
            .Select(p => new { p.Id, p.EffectivePrice })
            .ToDictionaryAsync(p => p.Id, p => p.EffectivePrice, ct);

        var cart = await GetOrCreateCartAsync(userId, ct);
        foreach (var (productId, qty) in wanted)
        {
            if (!products.TryGetValue(productId, out var price)) continue; // silently skip unavailable products
            var line = cart.Items.FirstOrDefault(i => i.ProductId == productId);
            if (line is null)
            {
                if (cart.Items.Count >= CartLimits.MaxLines) break;
                cart.Items.Add(new CartItem { ProductId = productId, Quantity = Math.Min(qty, CartLimits.MaxQuantityPerLine), UnitPrice = price });
            }
            else
            {
                line.Quantity = Math.Min(line.Quantity + qty, CartLimits.MaxQuantityPerLine);
                line.UnitPrice = price;
            }
        }
        await db.SaveChangesAsync(ct);
        return await BuildAsync(userId, ct);
    }

    public async Task<CartDto> PreviewAsync(IReadOnlyList<CartMergeLine> lines, CancellationToken ct = default)
    {
        var wanted = lines.GroupBy(l => l.ProductId).ToDictionary(g => g.Key, g => Math.Min(g.Sum(l => l.Quantity), CartLimits.MaxQuantityPerLine));
        var ids = wanted.Keys.ToList();
        var products = await db.Products.AsNoTracking()
            .Where(p => p.IsActive && ids.Contains(p.Id))
            .Select(p => new
            {
                p.Id, p.Slug, p.Name, p.Sku, p.Price, p.EffectivePrice, p.StockStatus,
                Image = p.Images.OrderByDescending(x => x.IsPrimary).ThenBy(x => x.DisplayOrder).Select(x => x.Url).FirstOrDefault(),
            })
            .ToListAsync(ct);

        // keep the caller's line order
        var items = ids.Select(id => products.FirstOrDefault(p => p.Id == id)).Where(p => p is not null)
            .Select(p => new CartItemDto(p!.Id, p.Slug, p.Name, p.Sku, p.Image, p.Price, p.EffectivePrice, wanted[p.Id], p.StockStatus))
            .ToList();
        return new CartDto(items);
    }

    private async Task<Cart> GetOrCreateCartAsync(Guid userId, CancellationToken ct)
    {
        var cart = await db.Carts.Include(c => c.Items).FirstOrDefaultAsync(c => c.UserId == userId, ct);
        if (cart is not null) return cart;

        cart = new Cart { UserId = userId };
        db.Carts.Add(cart);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // A parallel request created the cart first (unique index on UserId): use that one.
            db.Carts.Remove(cart);
            return await db.Carts.Include(c => c.Items).FirstAsync(c => c.UserId == userId, ct);
        }
        return cart;
    }

    private async Task<CartDto> BuildAsync(Guid userId, CancellationToken ct)
    {
        var items = await db.CartItems.AsNoTracking()
            .Where(i => i.Cart.UserId == userId)
            .OrderBy(i => i.CreatedAt).ThenBy(i => i.Id)
            .Select(i => new CartItemDto(
                i.ProductId, i.Product.Slug, i.Product.Name, i.Product.Sku,
                i.Product.Images.OrderByDescending(x => x.IsPrimary).ThenBy(x => x.DisplayOrder).Select(x => x.Url).FirstOrDefault(),
                i.Product.Price, i.Product.EffectivePrice, i.Quantity, i.Product.StockStatus))
            .ToListAsync(ct);
        return new CartDto(items);
    }
}
