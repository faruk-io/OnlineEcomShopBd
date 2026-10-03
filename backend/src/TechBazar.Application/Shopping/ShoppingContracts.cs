using FluentValidation;
using TechBazar.Application.Catalog.Dtos;
using TechBazar.Domain.Enums;

namespace TechBazar.Application.Shopping;

public static class CartLimits
{
    public const int MaxQuantityPerLine = 10;
    public const int MaxLines = 50;
}

public sealed record CartItemDto(
    int ProductId, string Slug, string Name, string Sku, string? ImageUrl,
    decimal ListPrice, decimal UnitPrice, int Quantity, StockStatus StockStatus)
{
    public decimal LineTotal => UnitPrice * Quantity;
    public bool Purchasable => StockStatus is StockStatus.InStock or StockStatus.PreOrder;
}

public sealed record CartDto(IReadOnlyList<CartItemDto> Items)
{
    public int ItemCount => Items.Sum(i => i.Quantity);
    public decimal Subtotal => Items.Sum(i => i.LineTotal);
    public decimal Savings => Items.Sum(i => (i.ListPrice - i.UnitPrice) * i.Quantity);
}

public sealed record SetCartItemRequest(int Quantity);
public sealed record CartMergeLine(int ProductId, int Quantity);
public sealed record CartMergeRequest(IReadOnlyList<CartMergeLine> Items);
public sealed record WishlistMergeRequest(IReadOnlyList<int> ProductIds);

public interface ICartService
{
    Task<CartDto> GetAsync(Guid userId, CancellationToken ct = default);
    /// <summary>Sets the quantity of a line (adds it if missing). Rejects unknown or unavailable products.</summary>
    Task<CartDto> SetItemAsync(Guid userId, int productId, int quantity, CancellationToken ct = default);
    Task<CartDto> RemoveItemAsync(Guid userId, int productId, CancellationToken ct = default);
    Task<CartDto> ClearAsync(Guid userId, CancellationToken ct = default);
    /// <summary>Adds a guest cart to the server cart (quantities add up, capped; unavailable products are skipped).</summary>
    Task<CartDto> MergeAsync(Guid userId, IReadOnlyList<CartMergeLine> lines, CancellationToken ct = default);
}

public interface IWishlistService
{
    Task<IReadOnlyList<ProductListItemDto>> GetAsync(Guid userId, CancellationToken ct = default);
    Task<IReadOnlyList<ProductListItemDto>> AddAsync(Guid userId, int productId, CancellationToken ct = default);
    Task<IReadOnlyList<ProductListItemDto>> RemoveAsync(Guid userId, int productId, CancellationToken ct = default);
    Task<IReadOnlyList<ProductListItemDto>> MergeAsync(Guid userId, IReadOnlyList<int> productIds, CancellationToken ct = default);
}

public sealed class SetCartItemRequestValidator : AbstractValidator<SetCartItemRequest>
{
    public SetCartItemRequestValidator() =>
        RuleFor(x => x.Quantity).InclusiveBetween(1, CartLimits.MaxQuantityPerLine);
}

public sealed class CartMergeRequestValidator : AbstractValidator<CartMergeRequest>
{
    public CartMergeRequestValidator()
    {
        RuleFor(x => x.Items).NotNull().Must(i => i.Count <= CartLimits.MaxLines).WithMessage($"At most {CartLimits.MaxLines} lines.");
        RuleForEach(x => x.Items).ChildRules(l =>
        {
            l.RuleFor(x => x.ProductId).GreaterThan(0);
            l.RuleFor(x => x.Quantity).InclusiveBetween(1, CartLimits.MaxQuantityPerLine);
        });
    }
}

public sealed class WishlistMergeRequestValidator : AbstractValidator<WishlistMergeRequest>
{
    public WishlistMergeRequestValidator() =>
        RuleFor(x => x.ProductIds).NotNull().Must(i => i.Count <= 200).WithMessage("At most 200 products.");
}
