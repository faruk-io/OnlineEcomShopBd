using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TechBazar.Application.Common;
using TechBazar.Application.Orders;
using TechBazar.Domain.Enums;
using TechBazar.UnitTests.Support;

namespace TechBazar.UnitTests.Orders;

public class CheckoutServiceTests
{
    [Fact]
    public async Task PlacingACashOnDeliveryOrder_RecalculatesEverythingOnTheServer()
    {
        using var s = await Scenario.CreateAsync();
        await s.AddToCart("Ryzen 5 5600 Processor", 2);       // 11,900 sale price x 2
        await s.AddToCart("Vengeance LPX 16GB", 1);            // 5,200
        var result = await s.Place();

        var o = result.Order;
        Assert.Matches(@"^TB-\d{6}-[A-Z2-9]{5}$", o.OrderNumber);
        Assert.Equal(11900m * 2 + 5200m, o.Subtotal);
        Assert.Equal(70m, o.ShippingFee);
        Assert.Equal(0m, o.DiscountTotal);
        Assert.Equal(11900m * 2 + 5200m + 70m, o.GrandTotal);
        Assert.Equal(OrderStatus.Pending, o.Status);
        Assert.Equal(PaymentStatus.Unpaid, o.PaymentStatus);
        Assert.Equal(PaymentMethod.CashOnDelivery, o.PaymentMethod);
        Assert.Null(result.Payment);
        Assert.Equal(2, o.Items.Count);
        Assert.All(o.Items, i => Assert.Equal(i.UnitPrice * i.Quantity, i.LineTotal));
        Assert.Equal("Rahim Uddin", o.ShipTo.FullName);
        Assert.Equal("Order placed", o.History.Single().Note);
        Assert.Equal(OrderStatus.Pending, o.Timeline.Single(t => t.Current).Status);
    }

    [Fact]
    public async Task PlacingAnOrderTakesStock_CountsTheSale_EmptiesTheCart_AndEmailsTheCustomer()
    {
        using var s = await Scenario.CreateAsync();
        var before = s.Product("Ryzen 5 5600 Processor");
        var stock = before.StockQuantity; var sold = before.SoldCount;
        await s.AddToCart("Ryzen 5 5600 Processor", 3);
        var result = await s.Place();

        s.Ctx.ChangeTracker.Clear();
        var after = s.Product("Ryzen 5 5600 Processor");
        Assert.Equal(stock - 3, after.StockQuantity);
        Assert.Equal(sold + 3, after.SoldCount);
        Assert.Empty(await s.Ctx.CartItems.Where(i => i.Cart.UserId == s.UserId).ToListAsync());

        var mail = Assert.Single(s.Db.Emails.Sent);
        Assert.Equal("rahim@example.com", mail.To);
        Assert.Contains(result.Order.OrderNumber, mail.Subject);
        Assert.Contains("৳35,770", mail.TextBody);                       // 3 x 11,900 + 70, Bangladeshi grouping
        Assert.Contains($"/account/orders/{result.Order.OrderNumber}", mail.TextBody);
        Assert.Contains("Cash on delivery", mail.TextBody);
        Assert.Contains("House 1, Road 2", mail.TextBody);
        Assert.NotNull(mail.HtmlBody);
    }

    [Fact]
    public async Task AFailingEmailProviderNeverFailsTheOrder()
    {
        using var s = await Scenario.CreateAsync();
        s.Db.Emails.Throw = true;
        await s.AddToCart("Ryzen 5 5600 Processor");
        var result = await s.Place();
        Assert.Equal(OrderStatus.Pending, result.Order.Status);
        Assert.Equal(1, await s.Ctx.Orders.CountAsync());
    }

    [Theory]
    [InlineData(ShippingMethod.HomeDeliveryInsideDhaka, "Dhaka", 70)]
    [InlineData(ShippingMethod.HomeDeliveryOutsideDhaka, "Chattogram", 130)]
    [InlineData(ShippingMethod.HomeDeliveryInsideDhaka, "Sylhet", 130)]   // claims inside Dhaka, address says otherwise -> charged the outside rate
    [InlineData(ShippingMethod.HomeDeliveryOutsideDhaka, "Dhaka", 70)]
    public async Task ShippingIsChargedByTheAddressDistrict_NotByWhatTheClientClaims(ShippingMethod claimed, string district, int fee)
    {
        using var s = await Scenario.CreateAsync();
        await s.AddToCart("Ryzen 5 5600 Processor");
        var address = await s.AddAddress(district, district == "Dhaka" ? "Dhaka" : district);
        var result = await s.Place(shipping: claimed, addressId: address.Id);
        Assert.Equal(fee, result.Order.ShippingFee);
        Assert.Equal(district == "Dhaka" ? ShippingMethod.HomeDeliveryInsideDhaka : ShippingMethod.HomeDeliveryOutsideDhaka, result.Order.ShippingMethod);
    }

    [Fact]
    public async Task StorePickup_IsFree_UsesTheContactDetails_AndShowsThePickupLocation()
    {
        using var s = await Scenario.CreateAsync();
        await s.AddToCart("Ryzen 5 5600 Processor");
        var result = await s.Place(shipping: ShippingMethod.StorePickup, contactName: "Karim Hossain", contactPhone: "01812345678");
        var o = result.Order;
        Assert.Equal(0m, o.ShippingFee);
        Assert.Equal("Karim Hossain", o.ShipTo.FullName);
        Assert.NotNull(o.Pickup);
        Assert.Contains("Store pickup", o.ShipTo.AddressLine);
        Assert.Equal(OrderStatus.ReadyForPickup, OrderStateMachine.Timeline(o.ShippingMethod)[3]);
        Assert.Contains("Pick up from", s.Db.Emails.Sent.Single().TextBody);
    }

    [Fact]
    public async Task ACouponReducesTheTotal_IsCountedAsUsed_AndShippingIsNeverDiscounted()
    {
        using var s = await Scenario.CreateAsync();
        await s.AddToCart("Ryzen 7 7800X3D", 1);    // pre-order, 58,000
        await s.AddToCart("Core i5-12400F", 1);     // 13,800
        var result = await s.Place(coupon: "welcome10");     // 10% of 71,800 = 7,180 -> capped at 1,000

        Assert.Equal("WELCOME10", result.Order.CouponCode);
        Assert.Equal(1000m, result.Order.DiscountTotal);
        Assert.Equal(58000m + 13800m - 1000m + 70m, result.Order.GrandTotal);
        s.Ctx.ChangeTracker.Clear();
        Assert.Equal(1, (await s.Ctx.Coupons.SingleAsync(c => c.Code == "WELCOME10")).UsedCount);
    }

    [Fact]
    public async Task AnUnknownCouponRejectsTheWholeOrder_AndLeavesStockCartAndCouponUntouched()
    {
        using var s = await Scenario.CreateAsync();
        await s.AddToCart("Ryzen 5 5600 Processor", 2);
        var stock = s.Product("Ryzen 5 5600 Processor").StockQuantity;

        var ex = await Assert.ThrowsAsync<ConflictException>(() => s.Place(coupon: "NOPE"));
        Assert.Contains("not valid", ex.Message);

        s.Ctx.ChangeTracker.Clear();
        Assert.Equal(stock, s.Product("Ryzen 5 5600 Processor").StockQuantity);
        Assert.Equal(0, await s.Ctx.Orders.CountAsync());
        Assert.Equal(2, (await s.Ctx.CartItems.SingleAsync(i => i.Cart.UserId == s.UserId)).Quantity);
    }

    [Fact]
    public async Task CouponBelowItsMinimum_IsRejected_WithoutSideEffects()
    {
        using var s = await Scenario.CreateAsync();
        await s.AddToCart("Kingston FURY Beast 8GB", 1);   // 2,600 < 10,000 minimum of EID500
        var stock = s.Product("Kingston FURY Beast 8GB").StockQuantity;
        var ex = await Assert.ThrowsAsync<ConflictException>(() => s.Place(coupon: "EID500"));
        Assert.Contains("Spend at least", ex.Message);

        s.Ctx.ChangeTracker.Clear();
        Assert.Equal(stock, s.Product("Kingston FURY Beast 8GB").StockQuantity);
        Assert.Equal(0, await s.Ctx.Orders.CountAsync());
        Assert.Single(await s.Ctx.CartItems.Where(i => i.Cart.UserId == s.UserId).ToListAsync());
        Assert.Equal(0, (await s.Ctx.Coupons.SingleAsync(c => c.Code == "EID500")).UsedCount);
    }

    [Fact]
    public async Task TheCouponUsageLimitIsEnforced_OnTheLastRedemption()
    {
        using var s = await Scenario.CreateAsync();
        var coupon = await s.Ctx.Coupons.SingleAsync(c => c.Code == "EID500");
        coupon.UsageLimit = 1;
        await s.Ctx.SaveChangesAsync();

        await s.AddToCart("Ryzen 5 5600 Processor", 1);
        await s.Place(coupon: "EID500");
        await s.AddToCart("Ryzen 5 5600 Processor", 1);
        var ex = await Assert.ThrowsAsync<ConflictException>(() => s.Place(coupon: "EID500"));
        Assert.Contains("usage limit", ex.Message);
    }

    [Fact]
    public async Task ThePriceIsReadFromTheDatabaseAtOrderTime_NotFromTheCartSnapshot()
    {
        using var s = await Scenario.CreateAsync();
        await s.AddToCart("Core i5-12400F", 1);   // snapshot taken at 13,800
        var p = s.Product("Core i5-12400F");
        p.DiscountPrice = null; p.Price = 15000m;      // price changes after the item was added
        await s.Ctx.SaveChangesAsync();

        var result = await s.Place();
        Assert.Equal(15000m, result.Order.Items.Single().UnitPrice);
        Assert.Equal(15000m + 70m, result.Order.GrandTotal);
    }

    [Fact]
    public async Task AskingForMoreThanIsInStock_IsRefusedWithoutPartialEffects()
    {
        using var s = await Scenario.CreateAsync();
        await s.AddToCart("Ryzen 5 5600 Processor", 5);
        await s.AddToCart("Vengeance LPX 16GB", 1);
        var cpu = s.Product("Ryzen 5 5600 Processor");
        cpu.StockQuantity = 3; await s.Ctx.SaveChangesAsync();
        var ramStock = s.Product("Vengeance LPX 16GB").StockQuantity;

        var ex = await Assert.ThrowsAsync<ConflictException>(() => s.Place());
        Assert.Contains("Only 3", ex.Message);
        s.Ctx.ChangeTracker.Clear();
        Assert.Equal(0, await s.Ctx.Orders.CountAsync());
        Assert.Equal(ramStock, s.Product("Vengeance LPX 16GB").StockQuantity);   // the other line was not touched either
        Assert.Equal(3, s.Product("Ryzen 5 5600 Processor").StockQuantity);
    }

    [Fact]
    public async Task AProductThatSoldOutWhileInTheCart_BlocksCheckout()
    {
        using var s = await Scenario.CreateAsync();
        await s.AddToCart("Ryzen 5 5600 Processor");
        var p = s.Product("Ryzen 5 5600 Processor");
        p.StockStatus = StockStatus.OutOfStock; await s.Ctx.SaveChangesAsync();
        var ex = await Assert.ThrowsAsync<ConflictException>(() => s.Place());
        Assert.Contains("no longer available", ex.Message);
    }

    [Fact]
    public async Task TheLastUnitGoesToTheFirstBuyer_TheSecondIsToldItSoldOut()
    {
        using var s = await Scenario.CreateAsync();
        var (other, otherEmail) = await s.NewUserAsync("karim@example.com");
        var p = s.Product("Ryzen 5 5600 Processor");
        p.StockQuantity = 1; await s.Ctx.SaveChangesAsync();
        await s.AddToCart("Ryzen 5 5600 Processor", 1);
        await s.AddToCart("Ryzen 5 5600 Processor", 1, other);

        await s.Place();
        s.Ctx.ChangeTracker.Clear();
        var after = s.Product("Ryzen 5 5600 Processor");
        Assert.Equal(0, after.StockQuantity);
        Assert.Equal(StockStatus.OutOfStock, after.StockStatus);   // the shelf label flips automatically

        var ex = await Assert.ThrowsAsync<ConflictException>(() => s.Place(user: other, email: otherEmail));
        Assert.Contains("no longer available", ex.Message);
        Assert.Equal(1, await s.Ctx.Orders.CountAsync());
    }

    [Fact]
    public async Task PreOrderItemsAreSoldWithoutTouchingStock_AndCancellingDoesNotInventStock()
    {
        using var s = await Scenario.CreateAsync();
        var p = s.Product("Ryzen 7 7800X3D");
        Assert.Equal(StockStatus.PreOrder, p.StockStatus);
        await s.AddToCart("Ryzen 7 7800X3D", 2);
        var result = await s.Place();

        s.Ctx.ChangeTracker.Clear();
        Assert.Equal(0, s.Product("Ryzen 7 7800X3D").StockQuantity);
        Assert.False((await s.LoadOrder(result.Order.OrderNumber)).Items.Single().StockDecremented);

        await s.Get<IOrderService>().CancelAsync(s.UserId, result.Order.OrderNumber);
        s.Ctx.ChangeTracker.Clear();
        Assert.Equal(0, s.Product("Ryzen 7 7800X3D").StockQuantity);
    }

    [Fact]
    public async Task EmptyCart_HomeDeliveryWithoutAddress_AndAnotherUsersAddress_AreRejected()
    {
        using var s = await Scenario.CreateAsync();
        await Assert.ThrowsAsync<ConflictException>(() => s.Place(shipping: ShippingMethod.StorePickup, contactName: "A", contactPhone: "01712345678"));

        await s.AddToCart("Ryzen 5 5600 Processor");
        var (other, _) = await s.NewUserAsync("karim@example.com");
        var foreign = await s.AddAddress(user: other);
        await Assert.ThrowsAsync<NotFoundException>(() => s.Place(addressId: foreign.Id));
        await Assert.ThrowsAsync<ValidationException>(async () => await s.Get<ICheckoutService>().PlaceAsync(s.UserId, s.Email,
            new PlaceOrderRequest(ShippingMethod.HomeDeliveryInsideDhaka, null, null, null, PaymentMethod.CashOnDelivery, null, null)));
    }

    // ------------------------------------------------------------------ quote
    [Fact]
    public async Task Quote_ReturnsServerComputedTotals_CouponFeedback_AndBlockingProblems()
    {
        using var s = await Scenario.CreateAsync();
        await s.AddToCart("Ryzen 5 5600 Processor", 2);
        var address = await s.AddAddress("Chattogram", "Chattogram");
        var checkout = s.Get<ICheckoutService>();

        var q = await checkout.QuoteAsync(s.UserId, new CheckoutQuoteRequest(ShippingMethod.HomeDeliveryInsideDhaka, address.Id, "welcome10"));
        Assert.Equal(ShippingMethod.HomeDeliveryOutsideDhaka, q.ShippingMethod);
        Assert.Equal(130m, q.ShippingFee);
        Assert.Equal(23800m, q.Subtotal);
        Assert.Equal(1000m, q.Discount);
        Assert.Equal(23800m - 1000m + 130m, q.GrandTotal);
        Assert.True(q.CouponApplied);
        Assert.True(q.CanPlaceOrder);

        var bad = await checkout.QuoteAsync(s.UserId, new CheckoutQuoteRequest(ShippingMethod.HomeDeliveryInsideDhaka, address.Id, "NOPE"));
        Assert.False(bad.CouponApplied);
        Assert.NotNull(bad.CouponMessage);
        Assert.Equal(23800m + 130m, bad.GrandTotal);

        var noAddress = await checkout.QuoteAsync(s.UserId, new CheckoutQuoteRequest(ShippingMethod.HomeDeliveryInsideDhaka, null, null));
        Assert.False(noAddress.CanPlaceOrder);
        Assert.Contains("Choose a delivery address.", noAddress.Problems);

        s.Product("Ryzen 5 5600 Processor").StockQuantity = 1; await s.Ctx.SaveChangesAsync();
        var low = await checkout.QuoteAsync(s.UserId, new CheckoutQuoteRequest(ShippingMethod.StorePickup, null, null));
        Assert.False(low.CanPlaceOrder);
        Assert.Contains("Only 1", low.Lines.Single().Problem);
    }

    [Fact]
    public void Options_ListShippingFees_AndOnlyEnabledPaymentMethods()
    {
        using var db = new TestDatabase();
        using var scope = db.CreateScope();
        var checkout = scope.ServiceProvider.GetRequiredService<ICheckoutService>();
        var o = checkout.Options();
        Assert.Equal([70m, 130m, 0m], o.Shipping.Select(x => x.Fee));
        Assert.Equal(8, o.Divisions.Count);
        Assert.True(o.Payment.Single(p => p.Method == PaymentMethod.Online).Enabled);

        db.Gateway.IsConfigured = false;
        Assert.False(checkout.Options().Payment.Single(p => p.Method == PaymentMethod.Online).Enabled);
        Assert.True(checkout.Options().Payment.Single(p => p.Method == PaymentMethod.CashOnDelivery).Enabled);
    }

    // ------------------------------------------------------------------ request validation
    [Fact]
    public void PlaceOrderRequestValidation_MatchesTheDeliveryType()
    {
        var v = new PlaceOrderRequestValidator();
        Assert.True(v.Validate(new PlaceOrderRequest(ShippingMethod.HomeDeliveryInsideDhaka, 5, null, null, PaymentMethod.CashOnDelivery, null, null)).IsValid);
        Assert.False(v.Validate(new PlaceOrderRequest(ShippingMethod.HomeDeliveryInsideDhaka, null, null, null, PaymentMethod.CashOnDelivery, null, null)).IsValid);
        Assert.False(v.Validate(new PlaceOrderRequest(ShippingMethod.StorePickup, null, null, null, PaymentMethod.CashOnDelivery, null, null)).IsValid);
        Assert.False(v.Validate(new PlaceOrderRequest(ShippingMethod.StorePickup, null, "Karim", "123", PaymentMethod.CashOnDelivery, null, null)).IsValid);
        Assert.True(v.Validate(new PlaceOrderRequest(ShippingMethod.StorePickup, null, "Karim", "01812345678", PaymentMethod.Online, null, null)).IsValid);
        Assert.False(v.Validate(new PlaceOrderRequest(ShippingMethod.StorePickup, null, "Karim", "01812345678", PaymentMethod.BKash, null, null)).IsValid);
        Assert.False(v.Validate(new PlaceOrderRequest((ShippingMethod)9, 1, null, null, PaymentMethod.CashOnDelivery, null, null)).IsValid);
    }
}
