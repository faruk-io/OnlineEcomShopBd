using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TechBazar.Application.Common;
using TechBazar.Application.Orders;
using TechBazar.Application.Payments;

namespace TechBazar.Api.Controllers;

[Authorize]
public sealed class AddressesController(IAddressService addresses) : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AddressDto>>> List(CancellationToken ct) => Ok(await addresses.ListAsync(User.UserId(), ct));

    [HttpPost]
    [ProducesResponseType(typeof(AddressDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(SaveAddressRequest request, CancellationToken ct)
    {
        var created = await addresses.CreateAsync(User.UserId(), request, ct);
        return Created($"/api/v1/addresses/{created.Id}", created);
    }

    [HttpPut("{id:int}")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AddressDto>> Update(int id, SaveAddressRequest request, CancellationToken ct) =>
        Ok(await addresses.UpdateAsync(User.UserId(), id, request, ct));

    [HttpPut("{id:int}/default")]
    public async Task<ActionResult<IReadOnlyList<AddressDto>>> SetDefault(int id, CancellationToken ct) =>
        Ok(await addresses.SetDefaultAsync(User.UserId(), id, ct));

    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await addresses.DeleteAsync(User.UserId(), id, ct);
        return NoContent();
    }
}

public sealed class CheckoutController(ICheckoutService checkout) : ApiControllerBase
{
    /// <summary>Shipping methods and fees, enabled payment methods, store pickup location, divisions.</summary>
    [HttpGet("options")]
    [AllowAnonymous]
    public ActionResult<CheckoutOptionsDto> Options() => Ok(checkout.Options());

    /// <summary>Server-side price quote for the signed-in user's cart: lines, coupon, shipping and grand total.</summary>
    [HttpPost("quote")]
    [Authorize]
    public async Task<ActionResult<CheckoutQuoteDto>> Quote(CheckoutQuoteRequest request, CancellationToken ct) =>
        Ok(await checkout.QuoteAsync(User.UserId(), request, ct));
}

[Authorize]
public sealed class OrdersController(ICheckoutService checkout, IOrderService orders, CatalogCache cache) : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<OrderSummaryDto>>> List([FromQuery] int page = 1, [FromQuery] int pageSize = 10, CancellationToken ct = default) =>
        Ok(await orders.ListAsync(User.UserId(), Math.Max(page, 1), Math.Clamp(pageSize, 1, 50), ct));

    [HttpGet("{orderNumber}")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OrderDetailDto>> Get(string orderNumber, CancellationToken ct) =>
        Ok(await orders.GetAsync(User.UserId(), orderNumber, ct));

    /// <summary>Places the order from the server-side cart. Totals are recalculated here; nothing price-related is read from the request.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(PlaceOrderResult), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Place(PlaceOrderRequest request, CancellationToken ct)
    {
        var result = await checkout.PlaceAsync(User.UserId(), User.Email(), request, ct);
        await cache.InvalidateAsync(ct);
        return Created($"/api/v1/orders/{result.Order.OrderNumber}", result);
    }

    [HttpPost("{orderNumber}/cancel")]
    public async Task<ActionResult<OrderDetailDto>> Cancel(string orderNumber, CancellationToken ct)
    {
        var detail = await orders.CancelAsync(User.UserId(), orderNumber, ct);
        await cache.InvalidateAsync(ct);
        return Ok(detail);
    }

    /// <summary>Starts a fresh online payment attempt (e.g. after a failed or abandoned one).</summary>
    [HttpPost("{orderNumber}/pay")]
    public async Task<ActionResult<PaymentRedirectDto>> Pay(string orderNumber, CancellationToken ct) =>
        Ok(await orders.RetryPaymentAsync(User.UserId(), orderNumber, ct));
}
