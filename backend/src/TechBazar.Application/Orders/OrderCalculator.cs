using TechBazar.Domain.Enums;

namespace TechBazar.Application.Orders;

public sealed record PricedLine(int ProductId, decimal UnitPrice, int Quantity)
{
    public decimal LineTotal => OrderCalculator.Round(UnitPrice * Quantity);
}

/// <summary>The coupon fields that matter for pricing (decoupled from the EF entity so the maths is trivially testable).</summary>
public sealed record CouponTerms(
    string Code, DiscountType Type, decimal Value, decimal? MinOrderAmount, decimal? MaxDiscountAmount,
    int? UsageLimit, int UsedCount, DateTime? StartsAt, DateTime? ExpiresAt, bool IsActive);

public sealed record CouponResult(bool Applied, string Code, decimal Discount, string? Error);

public sealed record OrderTotals(decimal Subtotal, decimal Discount, decimal ShippingFee, decimal GrandTotal, CouponResult? Coupon);

/// <summary>
/// All order money maths lives here and ONLY here. The server always recomputes totals from database prices; amounts
/// sent by a browser are never used. Rules: coupon discounts never touch shipping, never exceed the subtotal, and every
/// figure is rounded to 2 decimals half-away-from-zero (matching decimal(18,2) storage).
/// </summary>
public static class OrderCalculator
{
    public static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    public static decimal Subtotal(IEnumerable<PricedLine> lines) => lines.Sum(l => l.LineTotal);

    public static CouponResult ApplyCoupon(CouponTerms? coupon, string? requestedCode, decimal subtotal, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(requestedCode)) return new CouponResult(false, "", 0, null);
        var code = requestedCode.Trim().ToUpperInvariant();
        if (coupon is null) return Reject(code, "This coupon code is not valid.");
        if (!coupon.IsActive) return Reject(code, "This coupon is no longer active.");
        if (coupon.StartsAt is { } start && nowUtc < start) return Reject(code, "This coupon is not valid yet.");
        if (coupon.ExpiresAt is { } end && nowUtc > end) return Reject(code, "This coupon has expired.");
        if (coupon.UsageLimit is { } limit && coupon.UsedCount >= limit) return Reject(code, "This coupon has reached its usage limit.");
        if (coupon.MinOrderAmount is { } min && subtotal < min) return Reject(code, $"Spend at least ৳{min:0.##} to use this coupon.");

        var raw = coupon.Type == DiscountType.Percentage ? subtotal * coupon.Value / 100m : coupon.Value;
        if (coupon.Type == DiscountType.Percentage && coupon.MaxDiscountAmount is { } cap) raw = Math.Min(raw, cap);
        var discount = Round(Math.Min(Math.Max(raw, 0), subtotal));
        return new CouponResult(true, coupon.Code, discount, null);

        static CouponResult Reject(string code, string error) => new(false, code, 0, error);
    }

    public static OrderTotals Calculate(IReadOnlyCollection<PricedLine> lines, decimal shippingFee, CouponTerms? coupon, string? couponCode, DateTime nowUtc)
    {
        var subtotal = Subtotal(lines);
        var result = ApplyCoupon(coupon, couponCode, subtotal, nowUtc);
        var discount = result.Applied ? result.Discount : 0m;
        var shipping = Round(Math.Max(shippingFee, 0));
        var grand = Round(subtotal - discount + shipping);
        return new OrderTotals(subtotal, discount, shipping, grand, string.IsNullOrWhiteSpace(couponCode) ? null : result);
    }
}
