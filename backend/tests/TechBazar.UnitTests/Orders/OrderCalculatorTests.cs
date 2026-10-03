using TechBazar.Application.Common;
using TechBazar.Application.Orders;
using TechBazar.Domain.Enums;

namespace TechBazar.UnitTests.Orders;

public class OrderCalculatorTests
{
    private static readonly DateTime Now = new(2026, 10, 3, 12, 0, 0, DateTimeKind.Utc);

    private static CouponTerms Coupon(DiscountType type = DiscountType.Percentage, decimal value = 10, decimal? min = null, decimal? max = null,
        int? limit = null, int used = 0, DateTime? starts = null, DateTime? expires = null, bool active = true, string code = "WELCOME10") =>
        new(code, type, value, min, max, limit, used, starts, expires, active);

    private static PricedLine[] Cart(params (decimal price, int qty)[] lines) =>
        lines.Select((l, i) => new PricedLine(i + 1, l.price, l.qty)).ToArray();

    [Fact]
    public void Subtotal_IsSumOfRoundedLineTotals()
    {
        var lines = Cart((11900, 2), (13800, 1), (0.333m, 3));
        Assert.Equal(23800m + 13800m + 1.00m, OrderCalculator.Subtotal(lines)); // 0.333 x 3 = 0.999 -> 1.00
    }

    [Fact]
    public void NoCoupon_GrandTotalIsSubtotalPlusShipping()
    {
        var t = OrderCalculator.Calculate(Cart((1000, 2)), 70m, null, null, Now);
        Assert.Equal(2000m, t.Subtotal);
        Assert.Equal(0m, t.Discount);
        Assert.Equal(70m, t.ShippingFee);
        Assert.Equal(2070m, t.GrandTotal);
        Assert.Null(t.Coupon);
    }

    [Fact]
    public void PercentageCoupon_AppliesToSubtotalOnly_NotShipping()
    {
        var t = OrderCalculator.Calculate(Cart((10000, 1)), 130m, Coupon(value: 10), "welcome10", Now);
        Assert.True(t.Coupon!.Applied);
        Assert.Equal(1000m, t.Discount);
        Assert.Equal(10000m - 1000m + 130m, t.GrandTotal);
    }

    [Fact]
    public void PercentageCoupon_IsCappedByMaxDiscount()
    {
        var t = OrderCalculator.Calculate(Cart((50000, 1)), 0, Coupon(value: 10, max: 1000), "WELCOME10", Now);
        Assert.Equal(1000m, t.Discount);
        Assert.Equal(49000m, t.GrandTotal);
    }

    [Fact]
    public void FixedCoupon_NeverExceedsTheSubtotal()
    {
        var t = OrderCalculator.Calculate(Cart((300, 1)), 70m, Coupon(DiscountType.FixedAmount, 500), "WELCOME10", Now);
        Assert.Equal(300m, t.Discount);       // cannot go negative
        Assert.Equal(70m, t.GrandTotal);      // customer still pays shipping
    }

    [Fact]
    public void FixedCoupon_IgnoresTheMaxDiscountCapField()
    {
        var t = OrderCalculator.Calculate(Cart((20000, 1)), 0, Coupon(DiscountType.FixedAmount, 500, max: 100), "WELCOME10", Now);
        Assert.Equal(500m, t.Discount);
    }

    [Theory]
    [InlineData(33.335, 10, 3.33)]   // 3.3335 -> 3.33
    [InlineData(33.35, 10, 3.34)]    // 3.335 -> half away from zero -> 3.34
    [InlineData(999.99, 15, 150.00)] // 149.9985 -> 150.00
    public void Discount_IsRoundedHalfAwayFromZeroToTwoDecimals(double price, double percent, double expected)
    {
        var t = OrderCalculator.Calculate(Cart(((decimal)price, 1)), 0, Coupon(value: (decimal)percent), "WELCOME10", Now);
        Assert.Equal((decimal)expected, t.Discount);
    }

    [Fact]
    public void MinimumOrderAmount_IsCheckedAgainstTheSubtotalBeforeDiscount()
    {
        var c = Coupon(DiscountType.FixedAmount, 500, min: 10000);
        Assert.False(OrderCalculator.Calculate(Cart((9999, 1)), 0, c, "WELCOME10", Now).Coupon!.Applied);
        var ok = OrderCalculator.Calculate(Cart((10000, 1)), 0, c, "WELCOME10", Now);
        Assert.True(ok.Coupon!.Applied);
        Assert.Equal(9500m, ok.GrandTotal);
    }

    [Theory]
    [MemberData(nameof(Rejections))]
    public void InvalidCoupons_AreRejectedWithAReason_AndGiveNoDiscount(CouponTerms? coupon, string expectedMessage)
    {
        var t = OrderCalculator.Calculate(Cart((20000, 1)), 70m, coupon, "WELCOME10", Now);
        Assert.False(t.Coupon!.Applied);
        Assert.Contains(expectedMessage, t.Coupon.Error);
        Assert.Equal(0m, t.Discount);
        Assert.Equal(20070m, t.GrandTotal);
    }

    public static IEnumerable<object?[]> Rejections()
    {
        yield return [null, "not valid"];
        yield return [Coupon(active: false), "no longer active"];
        yield return [Coupon(starts: Now.AddDays(1)), "not valid yet"];
        yield return [Coupon(expires: Now.AddSeconds(-1)), "expired"];
        yield return [Coupon(limit: 5, used: 5), "usage limit"];
    }

    [Fact]
    public void Coupon_UsageLimit_AllowsTheLastUse()
    {
        Assert.True(OrderCalculator.ApplyCoupon(Coupon(limit: 5, used: 4), "WELCOME10", 1000, Now).Applied);
    }

    [Fact]
    public void Coupon_ValidityWindow_IsInclusiveAtTheEdges()
    {
        Assert.True(OrderCalculator.ApplyCoupon(Coupon(starts: Now, expires: Now), "WELCOME10", 1000, Now).Applied);
    }

    [Fact]
    public void BlankCouponCode_MeansNoCoupon()
    {
        var t = OrderCalculator.Calculate(Cart((1000, 1)), 0, Coupon(), "   ", Now);
        Assert.Null(t.Coupon);
        Assert.Equal(1000m, t.GrandTotal);
    }

    [Fact]
    public void EmptyCart_HasNoDiscountEvenWithACoupon()
    {
        var t = OrderCalculator.Calculate([], 70m, Coupon(DiscountType.FixedAmount, 500), "WELCOME10", Now);
        Assert.Equal(0m, t.Subtotal);
        Assert.Equal(0m, t.Discount);
    }

    [Fact]
    public void NegativeShipping_IsClampedToZero()
    {
        Assert.Equal(1000m, OrderCalculator.Calculate(Cart((1000, 1)), -50m, null, null, Now).GrandTotal);
    }

    [Fact]
    public void GrandTotal_Identity_HoldsForManyCombinations()
    {
        foreach (var qty in new[] { 1, 3, 10 })
            foreach (var price in new[] { 99.99m, 1250m, 78500m })
                foreach (var fee in new[] { 0m, 70m, 130m })
                    foreach (var c in new[] { null, Coupon(value: 7), Coupon(DiscountType.FixedAmount, 750), Coupon(value: 25, max: 300) })
                    {
                        var t = OrderCalculator.Calculate(Cart((price, qty)), fee, c, c?.Code, Now);
                        Assert.Equal(OrderCalculator.Round(t.Subtotal - t.Discount + t.ShippingFee), t.GrandTotal);
                        Assert.InRange(t.Discount, 0m, t.Subtotal);
                        Assert.True(t.GrandTotal >= t.ShippingFee);
                    }
    }

    [Theory]
    [InlineData(125000, "৳1,25,000")]
    [InlineData(999, "৳999")]
    [InlineData(1250.5, "৳1,250.50")]
    [InlineData(0, "৳0")]
    [InlineData(1234567, "৳12,34,567")]
    public void Money_FormatsBdtWithIndianGrouping(double amount, string expected) => Assert.Equal(expected, Money.Bdt((decimal)amount));
}
