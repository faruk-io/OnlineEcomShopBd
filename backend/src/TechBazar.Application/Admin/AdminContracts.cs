using FluentValidation;
using TechBazar.Application.Common;
using TechBazar.Application.Orders;
using TechBazar.Domain.Enums;

namespace TechBazar.Application.Admin;

// ---------------------------------------------------------------- products
public sealed record AdminProductListItemDto(
    int Id, string Name, string Slug, string Sku, string BrandName, string CategoryName, decimal Price, decimal? DiscountPrice,
    StockStatus StockStatus, int StockQuantity, bool IsActive, bool IsFeatured, string? ImageUrl, DateTime? UpdatedAt);

public sealed record SpecInput(string Group, string Key, string Value, bool? IsFilterable);
public sealed record ImageInput(string Url, string? AltText, bool IsPrimary);

public sealed record SaveProductRequest(
    string Name, string? Slug, string Sku, int CategoryId, int BrandId, decimal Price, decimal? DiscountPrice,
    StockStatus StockStatus, int StockQuantity, int WarrantyMonths, string? WarrantyDetails,
    string? ShortDescription, string? Description, bool IsFeatured, bool IsActive,
    IReadOnlyList<string> KeyFeatures, IReadOnlyList<SpecInput> Specifications, IReadOnlyList<ImageInput> Images);

public sealed record AdminProductDetailDto(
    int Id, string Name, string Slug, string Sku, int CategoryId, int BrandId, decimal Price, decimal? DiscountPrice,
    StockStatus StockStatus, int StockQuantity, int WarrantyMonths, string? WarrantyDetails, string? ShortDescription, string? Description,
    bool IsFeatured, bool IsActive, IReadOnlyList<string> KeyFeatures, IReadOnlyList<SpecInput> Specifications, IReadOnlyList<ImageInput> Images);

public sealed class SaveProductRequestValidator : AbstractValidator<SaveProductRequest>
{
    public SaveProductRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(250);
        RuleFor(x => x.Slug).MaximumLength(300).Matches("^[a-z0-9]+(?:-[a-z0-9]+)*$").When(x => !string.IsNullOrWhiteSpace(x.Slug))
            .WithMessage("Slug may contain lower-case letters, numbers and single hyphens.");
        RuleFor(x => x.Sku).NotEmpty().MaximumLength(64);
        RuleFor(x => x.CategoryId).GreaterThan(0);
        RuleFor(x => x.BrandId).GreaterThan(0);
        RuleFor(x => x.Price).GreaterThan(0).LessThan(100_000_000m).PrecisionScale(18, 2, false);
        RuleFor(x => x.DiscountPrice).GreaterThan(0).PrecisionScale(18, 2, false).When(x => x.DiscountPrice.HasValue);
        RuleFor(x => x).Must(x => x.DiscountPrice is null || x.DiscountPrice < x.Price).WithName("DiscountPrice").WithMessage("Sale price must be lower than the regular price.");
        RuleFor(x => x.StockStatus).IsInEnum();
        RuleFor(x => x.StockQuantity).InclusiveBetween(0, 1_000_000);
        RuleFor(x => x.WarrantyMonths).InclusiveBetween(0, 600);
        RuleFor(x => x.WarrantyDetails).MaximumLength(500);
        RuleFor(x => x.ShortDescription).MaximumLength(500);
        RuleFor(x => x.Description).MaximumLength(4000);
        RuleFor(x => x.KeyFeatures).NotNull().Must(f => f.Count <= 12).WithMessage("At most 12 key features.");
        RuleForEach(x => x.KeyFeatures).NotEmpty().MaximumLength(300);
        RuleFor(x => x.Specifications).NotNull().Must(s => s.Count <= 120).WithMessage("At most 120 specifications.");
        RuleForEach(x => x.Specifications).ChildRules(s =>
        {
            s.RuleFor(i => i.Group).NotEmpty().MaximumLength(100);
            s.RuleFor(i => i.Key).NotEmpty().MaximumLength(100);
            s.RuleFor(i => i.Value).NotEmpty().MaximumLength(300);
        });
        RuleFor(x => x.Images).NotNull().Must(i => i.Count <= 10).WithMessage("At most 10 images.");
        RuleForEach(x => x.Images).ChildRules(i =>
        {
            i.RuleFor(img => img.Url).NotEmpty().MaximumLength(500).Must(SafeImageUrl).WithMessage("Image URL must be a site path (/uploads/…) or an https:// URL.");
            i.RuleFor(img => img.AltText).MaximumLength(250);
        });
    }

    internal static bool SafeImageUrl(string? url) =>
        !string.IsNullOrWhiteSpace(url) && !url.Contains('\\') &&
        ((url.StartsWith('/') && !url.StartsWith("//")) || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase));
}

public interface IAdminProductService
{
    Task<PagedResult<AdminProductListItemDto>> ListAsync(string? search, int? categoryId, bool? lowStockOnly, int page, int pageSize, CancellationToken ct = default);
    Task<AdminProductDetailDto> GetAsync(int id, CancellationToken ct = default);
    Task<AdminProductDetailDto> CreateAsync(SaveProductRequest request, CancellationToken ct = default);
    Task<AdminProductDetailDto> UpdateAsync(int id, SaveProductRequest request, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);
}

// ---------------------------------------------------------------- categories & brands
public sealed record AdminCategoryDto(int Id, string Name, string Slug, string? Description, string? ImageUrl, int? ParentId, string? ParentName, int DisplayOrder, bool IsActive, int ProductCount, int ChildCount);
public sealed record SaveCategoryRequest(string Name, string? Slug, string? Description, string? ImageUrl, int? ParentId, int DisplayOrder, bool IsActive);
public sealed record AdminBrandDto(int Id, string Name, string Slug, string? Description, string? LogoUrl, bool IsActive, int ProductCount);
public sealed record SaveBrandRequest(string Name, string? Slug, string? Description, string? LogoUrl, bool IsActive);

public sealed class SaveCategoryRequestValidator : AbstractValidator<SaveCategoryRequest>
{
    public SaveCategoryRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Slug).MaximumLength(120).Matches("^[a-z0-9]+(?:-[a-z0-9]+)*$").When(x => !string.IsNullOrWhiteSpace(x.Slug));
        RuleFor(x => x.Description).MaximumLength(1000);
        RuleFor(x => x.ImageUrl).MaximumLength(500).Must(SaveProductRequestValidator.SafeImageUrl).When(x => !string.IsNullOrWhiteSpace(x.ImageUrl));
        RuleFor(x => x.DisplayOrder).InclusiveBetween(0, 10_000);
    }
}

public sealed class SaveBrandRequestValidator : AbstractValidator<SaveBrandRequest>
{
    public SaveBrandRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Slug).MaximumLength(120).Matches("^[a-z0-9]+(?:-[a-z0-9]+)*$").When(x => !string.IsNullOrWhiteSpace(x.Slug));
        RuleFor(x => x.Description).MaximumLength(1000);
        RuleFor(x => x.LogoUrl).MaximumLength(500).Must(SaveProductRequestValidator.SafeImageUrl).When(x => !string.IsNullOrWhiteSpace(x.LogoUrl));
    }
}

public interface IAdminCatalogService
{
    Task<IReadOnlyList<AdminCategoryDto>> ListCategoriesAsync(CancellationToken ct = default);
    Task<AdminCategoryDto> CreateCategoryAsync(SaveCategoryRequest request, CancellationToken ct = default);
    Task<AdminCategoryDto> UpdateCategoryAsync(int id, SaveCategoryRequest request, CancellationToken ct = default);
    Task DeleteCategoryAsync(int id, CancellationToken ct = default);
    Task<IReadOnlyList<AdminBrandDto>> ListBrandsAsync(CancellationToken ct = default);
    Task<AdminBrandDto> CreateBrandAsync(SaveBrandRequest request, CancellationToken ct = default);
    Task<AdminBrandDto> UpdateBrandAsync(int id, SaveBrandRequest request, CancellationToken ct = default);
    Task DeleteBrandAsync(int id, CancellationToken ct = default);
}

// ---------------------------------------------------------------- coupons
public sealed record AdminCouponDto(int Id, string Code, string? Description, DiscountType DiscountType, decimal Value, decimal? MinOrderAmount, decimal? MaxDiscountAmount, int? UsageLimit, int UsedCount, DateTime? StartsAt, DateTime? ExpiresAt, bool IsActive);
public sealed record SaveCouponRequest(string Code, string? Description, DiscountType DiscountType, decimal Value, decimal? MinOrderAmount, decimal? MaxDiscountAmount, int? UsageLimit, DateTime? StartsAt, DateTime? ExpiresAt, bool IsActive);

public sealed class SaveCouponRequestValidator : AbstractValidator<SaveCouponRequest>
{
    public SaveCouponRequestValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(50).Matches("^[A-Za-z0-9_-]+$").WithMessage("Code may contain letters, numbers, - and _.");
        RuleFor(x => x.Description).MaximumLength(300);
        RuleFor(x => x.DiscountType).IsInEnum();
        RuleFor(x => x.Value).GreaterThan(0).PrecisionScale(18, 2, false);
        RuleFor(x => x.Value).LessThanOrEqualTo(100).When(x => x.DiscountType == DiscountType.Percentage).WithMessage("A percentage discount cannot exceed 100.");
        RuleFor(x => x.MinOrderAmount).GreaterThanOrEqualTo(0).When(x => x.MinOrderAmount.HasValue);
        RuleFor(x => x.MaxDiscountAmount).GreaterThan(0).When(x => x.MaxDiscountAmount.HasValue);
        RuleFor(x => x.UsageLimit).GreaterThan(0).When(x => x.UsageLimit.HasValue);
        RuleFor(x => x).Must(x => x.StartsAt is null || x.ExpiresAt is null || x.StartsAt < x.ExpiresAt).WithName("ExpiresAt").WithMessage("Expiry must be after the start date.");
    }
}

public interface IAdminCouponService
{
    Task<IReadOnlyList<AdminCouponDto>> ListAsync(CancellationToken ct = default);
    Task<AdminCouponDto> CreateAsync(SaveCouponRequest request, CancellationToken ct = default);
    Task<AdminCouponDto> UpdateAsync(int id, SaveCouponRequest request, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);
}

// ---------------------------------------------------------------- orders
public sealed record AdminOrderListItemDto(
    string OrderNumber, DateTime CreatedAt, string CustomerName, string Phone, string Email, OrderStatus Status, PaymentStatus PaymentStatus,
    PaymentMethod PaymentMethod, ShippingMethod ShippingMethod, decimal GrandTotal, int ItemCount);

public sealed record AdminOrderDetailDto(OrderDetailDto Order, IReadOnlyList<OrderStatus> AllowedNext, bool CanMarkPaid);
public sealed record UpdateOrderStatusRequest(OrderStatus Status, string? Note);

public sealed class UpdateOrderStatusRequestValidator : AbstractValidator<UpdateOrderStatusRequest>
{
    public UpdateOrderStatusRequestValidator()
    {
        RuleFor(x => x.Status).IsInEnum();
        RuleFor(x => x.Note).MaximumLength(300);
    }
}

public interface IAdminOrderService
{
    Task<PagedResult<AdminOrderListItemDto>> ListAsync(OrderStatus? status, string? search, int page, int pageSize, CancellationToken ct = default);
    Task<AdminOrderDetailDto> GetAsync(string orderNumber, CancellationToken ct = default);
    Task<AdminOrderDetailDto> UpdateStatusAsync(string orderNumber, UpdateOrderStatusRequest request, Guid adminId, CancellationToken ct = default);
    /// <summary>Records a manual payment (e.g. bank transfer or card at the store) for an unpaid order.</summary>
    Task<AdminOrderDetailDto> MarkPaidAsync(string orderNumber, Guid adminId, CancellationToken ct = default);
}

// ---------------------------------------------------------------- dashboard
public sealed record DailySalesDto(DateOnly Date, decimal Revenue, int Orders);
public sealed record StatusCountDto(OrderStatus Status, int Count);
public sealed record TopProductDto(int ProductId, string Name, int Quantity, decimal Revenue);
public sealed record LowStockDto(int Id, string Name, string Sku, string Slug, int StockQuantity);

public sealed record DashboardDto(
    int Days, decimal Revenue, int Orders, decimal AverageOrderValue, int PendingOrders, int OrdersToday, decimal RevenueToday,
    IReadOnlyList<DailySalesDto> SalesByDay, IReadOnlyList<StatusCountDto> OrdersByStatus,
    IReadOnlyList<TopProductDto> TopProducts, IReadOnlyList<LowStockDto> LowStock, int LowStockThreshold,
    IReadOnlyList<AdminOrderListItemDto> RecentOrders);

public interface IDashboardService
{
    Task<DashboardDto> GetAsync(int days, int lowStockThreshold, CancellationToken ct = default);
}
