using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TechBazar.Api.Extensions;
using TechBazar.Application.Admin;
using TechBazar.Application.Common;
using TechBazar.Application.Storage;
using TechBazar.Domain.Enums;

namespace TechBazar.Api.Controllers;

/// <summary>Everything under /api/v1/admin requires the Admin role (customers get 403, anonymous 401).</summary>
[ApiController]
[Authorize(Policy = Policies.AdminOnly)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
public abstract class AdminControllerBase : ControllerBase;

[Route("api/v1/admin/dashboard")]
public sealed class AdminDashboardController(IDashboardService dashboard) : AdminControllerBase
{
    [HttpGet]
    public async Task<ActionResult<DashboardDto>> Get([FromQuery] int days = 30, [FromQuery] int lowStock = 5, CancellationToken ct = default) =>
        Ok(await dashboard.GetAsync(days, Math.Clamp(lowStock, 0, 1000), ct));
}

[Route("api/v1/admin/products")]
public sealed class AdminProductsController(IAdminProductService products, CatalogCache cache) : AdminControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<AdminProductListItemDto>>> List([FromQuery] string? search, [FromQuery] int? categoryId, [FromQuery] bool? lowStock,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        Ok(await products.ListAsync(search, categoryId, lowStock, Math.Max(page, 1), Math.Clamp(pageSize, 1, 100), ct));

    [HttpGet("{id:int}")]
    public async Task<ActionResult<AdminProductDetailDto>> Get(int id, CancellationToken ct) => Ok(await products.GetAsync(id, ct));

    [HttpPost]
    [ProducesResponseType(typeof(AdminProductDetailDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(SaveProductRequest request, CancellationToken ct)
    {
        var created = await products.CreateAsync(request, ct);
        await cache.InvalidateAsync(ct);
        return Created($"/api/v1/admin/products/{created.Id}", created);
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<AdminProductDetailDto>> Update(int id, SaveProductRequest request, CancellationToken ct)
    {
        var updated = await products.UpdateAsync(id, request, ct);
        await cache.InvalidateAsync(ct);
        return Ok(updated);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await products.DeleteAsync(id, ct);
        await cache.InvalidateAsync(ct);
        return NoContent();
    }
}

[Route("api/v1/admin/uploads")]
public sealed class AdminUploadsController(IFileStorage storage) : AdminControllerBase
{
    public sealed record UploadResult(string Url);

    /// <summary>Product image upload (PNG, JPEG, GIF, WebP up to 5 MB). The type is detected from the file content, not its name.</summary>
    [HttpPost("images")]
    [RequestSizeLimit(ImageSniffer.MaxBytes + 64 * 1024)]
    [ProducesResponseType(typeof(UploadResult), StatusCodes.Status201Created)]
    public async Task<IActionResult> UploadImage(IFormFile file, CancellationToken ct)
    {
        if (file.Length is 0 or > ImageSniffer.MaxBytes)
            return Problem(statusCode: 400, title: "Invalid image.", detail: $"Images must be between 1 byte and {ImageSniffer.MaxBytes / 1024 / 1024} MB.");

        await using var stream = file.OpenReadStream();
        var header = new byte[12];
        var read = await stream.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, ct);
        var ext = ImageSniffer.DetectExtension(header.AsSpan(0, read));
        if (ext is null) return Problem(statusCode: 400, title: "Unsupported image.", detail: "Upload a PNG, JPEG, GIF or WebP image.");

        stream.Position = 0;
        var url = await storage.SaveImageAsync(stream, ext, ct);
        return Created(url, new UploadResult(url));
    }
}

[Route("api/v1/admin/categories")]
public sealed class AdminCategoriesController(IAdminCatalogService catalog, CatalogCache cache) : AdminControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AdminCategoryDto>>> List(CancellationToken ct) => Ok(await catalog.ListCategoriesAsync(ct));

    [HttpPost]
    public async Task<IActionResult> Create(SaveCategoryRequest request, CancellationToken ct)
    {
        var created = await catalog.CreateCategoryAsync(request, ct);
        await cache.InvalidateAsync(ct);
        return Created($"/api/v1/admin/categories/{created.Id}", created);
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<AdminCategoryDto>> Update(int id, SaveCategoryRequest request, CancellationToken ct)
    {
        var updated = await catalog.UpdateCategoryAsync(id, request, ct);
        await cache.InvalidateAsync(ct);
        return Ok(updated);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await catalog.DeleteCategoryAsync(id, ct);
        await cache.InvalidateAsync(ct);
        return NoContent();
    }
}

[Route("api/v1/admin/brands")]
public sealed class AdminBrandsController(IAdminCatalogService catalog, CatalogCache cache) : AdminControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AdminBrandDto>>> List(CancellationToken ct) => Ok(await catalog.ListBrandsAsync(ct));

    [HttpPost]
    public async Task<IActionResult> Create(SaveBrandRequest request, CancellationToken ct)
    {
        var created = await catalog.CreateBrandAsync(request, ct);
        await cache.InvalidateAsync(ct);
        return Created($"/api/v1/admin/brands/{created.Id}", created);
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<AdminBrandDto>> Update(int id, SaveBrandRequest request, CancellationToken ct)
    {
        var updated = await catalog.UpdateBrandAsync(id, request, ct);
        await cache.InvalidateAsync(ct);
        return Ok(updated);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await catalog.DeleteBrandAsync(id, ct);
        await cache.InvalidateAsync(ct);
        return NoContent();
    }
}

[Route("api/v1/admin/coupons")]
public sealed class AdminCouponsController(IAdminCouponService coupons) : AdminControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AdminCouponDto>>> List(CancellationToken ct) => Ok(await coupons.ListAsync(ct));

    [HttpPost]
    public async Task<IActionResult> Create(SaveCouponRequest request, CancellationToken ct)
    {
        var created = await coupons.CreateAsync(request, ct);
        return Created($"/api/v1/admin/coupons/{created.Id}", created);
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<AdminCouponDto>> Update(int id, SaveCouponRequest request, CancellationToken ct) => Ok(await coupons.UpdateAsync(id, request, ct));

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await coupons.DeleteAsync(id, ct);
        return NoContent();
    }
}

[Route("api/v1/admin/orders")]
public sealed class AdminOrdersController(IAdminOrderService orders, CatalogCache cache) : AdminControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<AdminOrderListItemDto>>> List([FromQuery] OrderStatus? status, [FromQuery] string? search,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        Ok(await orders.ListAsync(status, search, Math.Max(page, 1), Math.Clamp(pageSize, 1, 100), ct));

    [HttpGet("{orderNumber}")]
    public async Task<ActionResult<AdminOrderDetailDto>> Get(string orderNumber, CancellationToken ct) => Ok(await orders.GetAsync(orderNumber, ct));

    /// <summary>Moves the order along its lifecycle (illegal transitions return 409). Cancelling / returning restocks the items.</summary>
    [HttpPut("{orderNumber}/status")]
    public async Task<ActionResult<AdminOrderDetailDto>> UpdateStatus(string orderNumber, UpdateOrderStatusRequest request, CancellationToken ct)
    {
        var detail = await orders.UpdateStatusAsync(orderNumber, request, User.UserId(), ct);
        await cache.InvalidateAsync(ct);
        return Ok(detail);
    }

    [HttpPost("{orderNumber}/mark-paid")]
    public async Task<ActionResult<AdminOrderDetailDto>> MarkPaid(string orderNumber, CancellationToken ct) =>
        Ok(await orders.MarkPaidAsync(orderNumber, User.UserId(), ct));
}
