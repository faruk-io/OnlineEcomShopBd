using FluentValidation;
using TechBazar.Application.Auth;
using TechBazar.Application.Common;
using TechBazar.Domain.Enums;

namespace TechBazar.Application.Orders;

public static class Divisions
{
    public static readonly string[] All = ["Barishal", "Chattogram", "Dhaka", "Khulna", "Mymensingh", "Rajshahi", "Rangpur", "Sylhet"];
}

// ---------------------------------------------------------------- addresses
public sealed record AddressDto(int Id, string Label, string FullName, string Phone, string Division, string District, string? Upazila, string AddressLine, string? PostalCode, bool IsDefault);
public sealed record SaveAddressRequest(string Label, string FullName, string Phone, string Division, string District, string? Upazila, string AddressLine, string? PostalCode, bool IsDefault);

public sealed class SaveAddressRequestValidator : AbstractValidator<SaveAddressRequest>
{
    public SaveAddressRequestValidator()
    {
        RuleFor(x => x.Label).NotEmpty().MaximumLength(50);
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Phone).NotEmpty().Matches(BdPhoneRule.Pattern()).WithMessage("Phone must be a valid Bangladeshi mobile number, e.g. 01712345678.");
        RuleFor(x => x.Division).NotEmpty().Must(d => Divisions.All.Contains(d, StringComparer.OrdinalIgnoreCase)).WithMessage("Choose a valid division.");
        RuleFor(x => x.District).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Upazila).MaximumLength(50);
        RuleFor(x => x.AddressLine).NotEmpty().MaximumLength(300);
        RuleFor(x => x.PostalCode).MaximumLength(10).Matches(@"^\d{4}$").When(x => !string.IsNullOrWhiteSpace(x.PostalCode)).WithMessage("Postal code must be 4 digits.");
    }
}

public interface IAddressService
{
    Task<IReadOnlyList<AddressDto>> ListAsync(Guid userId, CancellationToken ct = default);
    Task<AddressDto> CreateAsync(Guid userId, SaveAddressRequest request, CancellationToken ct = default);
    Task<AddressDto> UpdateAsync(Guid userId, int id, SaveAddressRequest request, CancellationToken ct = default);
    Task DeleteAsync(Guid userId, int id, CancellationToken ct = default);
    Task<IReadOnlyList<AddressDto>> SetDefaultAsync(Guid userId, int id, CancellationToken ct = default);
}

// ---------------------------------------------------------------- checkout
public sealed record ShippingOptionDto(ShippingMethod Method, string Label, decimal Fee, string Description);
public sealed record PaymentOptionDto(PaymentMethod Method, string Label, string Description, bool Enabled);
public sealed record StoreDto(string Name, string Address);
public sealed record CheckoutOptionsDto(IReadOnlyList<ShippingOptionDto> Shipping, IReadOnlyList<PaymentOptionDto> Payment, StoreDto Store, IReadOnlyList<string> Divisions, bool RequireVerifiedEmail = false);

public sealed record CheckoutQuoteRequest(ShippingMethod ShippingMethod, int? AddressId, string? CouponCode);

public sealed record CheckoutLineDto(int ProductId, string Slug, string Name, string? ImageUrl, decimal UnitPrice, int Quantity, decimal LineTotal, StockStatus StockStatus, int? Available, string? Problem);

public sealed record CheckoutQuoteDto(
    IReadOnlyList<CheckoutLineDto> Lines,
    decimal Subtotal, decimal Discount, ShippingMethod ShippingMethod, decimal ShippingFee, decimal GrandTotal,
    string? CouponCode, bool CouponApplied, string? CouponMessage,
    bool CanPlaceOrder, IReadOnlyList<string> Problems);

public sealed record PlaceOrderRequest(
    ShippingMethod ShippingMethod, int? AddressId, string? ContactName, string? ContactPhone,
    PaymentMethod PaymentMethod, string? CouponCode, string? Note);

public sealed class CheckoutQuoteRequestValidator : AbstractValidator<CheckoutQuoteRequest>
{
    public CheckoutQuoteRequestValidator()
    {
        RuleFor(x => x.ShippingMethod).IsInEnum();
        RuleFor(x => x.CouponCode).MaximumLength(50);
    }
}

public sealed class PlaceOrderRequestValidator : AbstractValidator<PlaceOrderRequest>
{
    public PlaceOrderRequestValidator()
    {
        RuleFor(x => x.ShippingMethod).IsInEnum();
        RuleFor(x => x.PaymentMethod).Must(m => m is PaymentMethod.CashOnDelivery or PaymentMethod.Online).WithMessage("Choose cash on delivery or online payment.");
        RuleFor(x => x.CouponCode).MaximumLength(50);
        RuleFor(x => x.Note).MaximumLength(500);
        RuleFor(x => x.AddressId).NotNull().When(x => x.ShippingMethod != ShippingMethod.StorePickup).WithMessage("Choose a delivery address.");
        RuleFor(x => x.ContactName).NotEmpty().MaximumLength(100).When(x => x.ShippingMethod == ShippingMethod.StorePickup).WithMessage("Enter the name of the person collecting the order.");
        RuleFor(x => x.ContactPhone).NotEmpty().Matches(BdPhoneRule.Pattern()).When(x => x.ShippingMethod == ShippingMethod.StorePickup).WithMessage("Enter a valid Bangladeshi mobile number.");
    }
}

// ---------------------------------------------------------------- orders
public sealed record OrderLineDto(int ProductId, string? Slug, string Name, string Sku, string? ImageUrl, decimal UnitPrice, int Quantity, decimal LineTotal);
public sealed record ShipAddressDto(string FullName, string Phone, string Division, string District, string? Upazila, string AddressLine, string? PostalCode);
public sealed record PaymentAttemptDto(string Gateway, PaymentMethod Method, PaymentAttemptStatus Status, decimal Amount, DateTime CreatedAt, DateTime? PaidAt, string? FailureReason);
public sealed record StatusEntryDto(OrderStatus Status, string? Note, DateTime At);
public sealed record TimelineStepDto(OrderStatus Status, string Label, DateTime? ReachedAt, bool Done, bool Current);

public sealed record OrderSummaryDto(
    string OrderNumber, DateTime CreatedAt, OrderStatus Status, PaymentStatus PaymentStatus, PaymentMethod PaymentMethod,
    ShippingMethod ShippingMethod, decimal GrandTotal, int ItemCount, string? FirstItemName, string? FirstItemImage);

public sealed record OrderDetailDto(
    string OrderNumber, DateTime CreatedAt, OrderStatus Status, PaymentStatus PaymentStatus, PaymentMethod PaymentMethod,
    ShippingMethod ShippingMethod, decimal Subtotal, decimal DiscountTotal, decimal ShippingFee, decimal GrandTotal,
    string? CouponCode, string? Note, string ContactEmail, ShipAddressDto ShipTo, StoreDto? Pickup,
    IReadOnlyList<OrderLineDto> Items, IReadOnlyList<StatusEntryDto> History, IReadOnlyList<TimelineStepDto> Timeline,
    IReadOnlyList<PaymentAttemptDto> Payments, bool CanCancel, bool CanPay);

public sealed record PaymentRedirectDto(string? RedirectUrl, string? Error);
public sealed record PlaceOrderResult(OrderDetailDto Order, PaymentRedirectDto? Payment);

public interface ICheckoutService
{
    CheckoutOptionsDto Options();
    Task<CheckoutQuoteDto> QuoteAsync(Guid userId, CheckoutQuoteRequest request, CancellationToken ct = default);
    Task<PlaceOrderResult> PlaceAsync(Guid userId, string email, PlaceOrderRequest request, CancellationToken ct = default);
}

public interface IOrderService
{
    Task<PagedResult<OrderSummaryDto>> ListAsync(Guid userId, int page, int pageSize, CancellationToken ct = default);
    Task<OrderDetailDto> GetAsync(Guid userId, string orderNumber, CancellationToken ct = default);
    Task<OrderDetailDto> CancelAsync(Guid userId, string orderNumber, CancellationToken ct = default);
    /// <summary>Starts a new online payment attempt for an unpaid order.</summary>
    Task<PaymentRedirectDto> RetryPaymentAsync(Guid userId, string orderNumber, CancellationToken ct = default);
}
