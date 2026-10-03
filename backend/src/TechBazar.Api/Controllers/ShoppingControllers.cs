using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TechBazar.Application.Catalog.Dtos;
using TechBazar.Application.Shopping;

namespace TechBazar.Api.Controllers;

[Authorize]
public sealed class CartController(ICartService cart) : ApiControllerBase
{
    private Guid UserId => Guid.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);

    [HttpGet]
    public async Task<ActionResult<CartDto>> Get(CancellationToken ct) => Ok(await cart.GetAsync(UserId, ct));

    /// <summary>Set the quantity of a line (creates it if missing).</summary>
    [HttpPut("items/{productId:int}")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CartDto>> SetItem(int productId, SetCartItemRequest request, CancellationToken ct) =>
        Ok(await cart.SetItemAsync(UserId, productId, request.Quantity, ct));

    [HttpDelete("items/{productId:int}")]
    public async Task<ActionResult<CartDto>> RemoveItem(int productId, CancellationToken ct) =>
        Ok(await cart.RemoveItemAsync(UserId, productId, ct));

    [HttpDelete]
    public async Task<ActionResult<CartDto>> Clear(CancellationToken ct) => Ok(await cart.ClearAsync(UserId, ct));

    /// <summary>Re-prices a guest (localStorage) cart with current prices/stock. Anonymous, nothing is stored.</summary>
    [HttpPost("preview")]
    [AllowAnonymous]
    public async Task<ActionResult<CartDto>> Preview(CartMergeRequest request, CancellationToken ct) =>
        Ok(await cart.PreviewAsync(request.Items, ct));

    /// <summary>Merge a guest (localStorage) cart into the server cart after login.</summary>
    [HttpPost("merge")]
    public async Task<ActionResult<CartDto>> Merge(CartMergeRequest request, CancellationToken ct) =>
        Ok(await cart.MergeAsync(UserId, request.Items, ct));
}

[Authorize]
public sealed class WishlistController(IWishlistService wishlist) : ApiControllerBase
{
    private Guid UserId => Guid.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ProductListItemDto>>> Get(CancellationToken ct) => Ok(await wishlist.GetAsync(UserId, ct));

    [HttpPut("{productId:int}")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<ProductListItemDto>>> Add(int productId, CancellationToken ct) =>
        Ok(await wishlist.AddAsync(UserId, productId, ct));

    [HttpDelete("{productId:int}")]
    public async Task<ActionResult<IReadOnlyList<ProductListItemDto>>> Remove(int productId, CancellationToken ct) =>
        Ok(await wishlist.RemoveAsync(UserId, productId, ct));

    [HttpPost("merge")]
    public async Task<ActionResult<IReadOnlyList<ProductListItemDto>>> Merge(WishlistMergeRequest request, CancellationToken ct) =>
        Ok(await wishlist.MergeAsync(UserId, request.ProductIds, ct));
}
