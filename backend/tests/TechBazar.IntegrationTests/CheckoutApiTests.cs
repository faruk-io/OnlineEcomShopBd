using System.Net;
using System.Net.Http.Json;
using TechBazar.Application.Auth;
using TechBazar.Application.Catalog.Dtos;
using TechBazar.Application.Common;
using TechBazar.Application.Orders;
using TechBazar.Application.Shopping;
using TechBazar.Domain.Enums;
using TechBazar.IntegrationTests.Support;

namespace TechBazar.IntegrationTests;

public class CheckoutApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient(new() { AllowAutoRedirect = false });

    private async Task<string> NewUserAsync()
    {
        var r = await _client.PostJsonAsync("/api/v1/auth/register", new { fullName = "Rahim Uddin", email = $"u{Guid.NewGuid():N}@example.com", password = "Passw0rdX" });
        return (await r.ReadAsync<AuthResponse>()).AccessToken;
    }

    private static HttpRequestMessage Req(HttpMethod m, string url, string token, object? body = null)
    {
        var msg = new HttpRequestMessage(m, url).WithBearer(token);
        if (body is not null) msg.Content = JsonContent.Create(body, options: Http.Json);
        return msg;
    }

    private async Task<ProductListItemDto> ProductAsync(string query) =>
        (await (await _client.GetAsync($"/api/v1/products?{query}")).ReadAsync<PagedResult<ProductListItemDto>>()).Items.First();

    private async Task<int> AddressAsync(string token, string division = "Dhaka", string district = "Dhaka")
    {
        var r = await _client.SendAsync(Req(HttpMethod.Post, "/api/v1/addresses", token, new
        { label = "Home", fullName = "Rahim Uddin", phone = "01712345678", division, district, addressLine = "House 1, Road 2", postalCode = "1216", isDefault = true }));
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        return (await r.ReadAsync<AddressDto>()).Id;
    }

    private async Task CartAsync(string token, ProductListItemDto p, int qty = 1) =>
        Assert.True((await _client.SendAsync(Req(HttpMethod.Put, $"/api/v1/cart/items/{p.Id}", token, new { quantity = qty }))).IsSuccessStatusCode);

    [Fact]
    public async Task CheckoutEndpoints_RequireAuthentication_ExceptTheOptions()
    {
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/api/v1/checkout/options")).StatusCode);
        foreach (var url in new[] { "/api/v1/addresses", "/api/v1/orders", "/api/v1/orders/TB-1" })
            Assert.Equal(HttpStatusCode.Unauthorized, (await _client.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.PostJsonAsync("/api/v1/orders", new { })).StatusCode);
    }

    [Fact]
    public async Task Options_ListFeesPaymentMethodsAndPickupStore()
    {
        var o = await (await _client.GetAsync("/api/v1/checkout/options")).ReadAsync<CheckoutOptionsDto>();
        Assert.Equal([70m, 130m, 0m], o.Shipping.Select(s => s.Fee));
        Assert.Contains(o.Payment, p => p.Method == PaymentMethod.CashOnDelivery && p.Enabled);
        Assert.Contains(o.Payment, p => p.Method == PaymentMethod.Online && p.Enabled);
        Assert.Contains("Dhaka", o.Divisions);
        Assert.False(string.IsNullOrWhiteSpace(o.Store.Address));
    }

    [Fact]
    public async Task FullFlow_Address_Quote_Place_Track_Cancel()
    {
        var token = await NewUserAsync();
        var product = await ProductAsync("inStock=true&category=ssd&maxPrice=7000");
        await CartAsync(token, product, 2);
        var dhaka = await AddressAsync(token);
        var outside = await AddressAsync(token, "Chattogram", "Chattogram");

        var q1 = await (await _client.SendAsync(Req(HttpMethod.Post, "/api/v1/checkout/quote", token, new { shippingMethod = "HomeDeliveryInsideDhaka", addressId = dhaka }))).ReadAsync<CheckoutQuoteDto>();
        Assert.Equal(70m, q1.ShippingFee);
        Assert.Equal(q1.Subtotal + 70m, q1.GrandTotal);
        // a wrong zone is corrected by the server rather than trusted
        var q2 = await (await _client.SendAsync(Req(HttpMethod.Post, "/api/v1/checkout/quote", token, new { shippingMethod = "HomeDeliveryInsideDhaka", addressId = outside }))).ReadAsync<CheckoutQuoteDto>();
        Assert.Equal(130m, q2.ShippingFee);

        var place = await _client.SendAsync(Req(HttpMethod.Post, "/api/v1/orders", token,
            new { shippingMethod = "HomeDeliveryInsideDhaka", addressId = dhaka, paymentMethod = "CashOnDelivery", grandTotal = 1, shippingFee = 0 }));  // injected amounts are ignored
        Assert.Equal(HttpStatusCode.Created, place.StatusCode);
        var placed = await place.ReadAsync<PlaceOrderResult>();
        Assert.Equal(q1.GrandTotal, placed.Order.GrandTotal);
        Assert.Null(placed.Payment?.RedirectUrl);
        Assert.Equal(place.Headers.Location!.ToString(), $"/api/v1/orders/{placed.Order.OrderNumber}");
        Assert.Contains(factory.Emails.Sent, m => m.Subject.Contains(placed.Order.OrderNumber));

        var cart = await (await _client.SendAsync(Req(HttpMethod.Get, "/api/v1/cart", token))).ReadAsync<CartDto>();
        Assert.Empty(cart.Items);

        var detail = await (await _client.SendAsync(Req(HttpMethod.Get, $"/api/v1/orders/{placed.Order.OrderNumber}", token))).ReadAsync<OrderDetailDto>();
        Assert.Equal(OrderStatus.Pending, detail.Status);
        Assert.True(detail.CanCancel);
        Assert.Equal(OrderStatus.Pending, detail.Timeline.Single(t => t.Current).Status);

        var list = await (await _client.SendAsync(Req(HttpMethod.Get, "/api/v1/orders", token))).ReadAsync<PagedResult<OrderSummaryDto>>();
        Assert.Equal(placed.Order.OrderNumber, Assert.Single(list.Items).OrderNumber);

        var cancelled = await (await _client.SendAsync(Req(HttpMethod.Post, $"/api/v1/orders/{placed.Order.OrderNumber}/cancel", token))).ReadAsync<OrderDetailDto>();
        Assert.Equal(OrderStatus.Cancelled, cancelled.Status);
        Assert.Equal(HttpStatusCode.Conflict, (await _client.SendAsync(Req(HttpMethod.Post, $"/api/v1/orders/{placed.Order.OrderNumber}/cancel", token))).StatusCode);
    }

    [Fact]
    public async Task OrdersArePrivate_AndEmptyCartAndBadInputAreProblemDetails()
    {
        var a = await NewUserAsync();
        var b = await NewUserAsync();
        var product = await ProductAsync("inStock=true&category=ssd&maxPrice=7000");
        await CartAsync(a, product);
        var addr = await AddressAsync(a);
        var placed = await (await _client.SendAsync(Req(HttpMethod.Post, "/api/v1/orders", a, new { shippingMethod = "HomeDeliveryOutsideDhaka", addressId = addr, paymentMethod = "CashOnDelivery" }))).ReadAsync<PlaceOrderResult>();

        Assert.Equal(HttpStatusCode.NotFound, (await _client.SendAsync(Req(HttpMethod.Get, $"/api/v1/orders/{placed.Order.OrderNumber}", b))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.SendAsync(Req(HttpMethod.Post, $"/api/v1/orders/{placed.Order.OrderNumber}/cancel", b))).StatusCode);

        var empty = await _client.SendAsync(Req(HttpMethod.Post, "/api/v1/orders", a, new { shippingMethod = "HomeDeliveryOutsideDhaka", addressId = addr, paymentMethod = "CashOnDelivery" }));
        Assert.Equal(HttpStatusCode.Conflict, empty.StatusCode);
        (await empty.ProblemAsync()).Dispose();

        var invalid = await _client.SendAsync(Req(HttpMethod.Post, "/api/v1/orders", a, new { shippingMethod = "HomeDeliveryOutsideDhaka", paymentMethod = "CashOnDelivery" }));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using var problem = await invalid.ProblemAsync();
        Assert.True(problem.RootElement.TryGetProperty("errors", out _));
    }

    [Fact]
    public async Task OnlinePayment_StartsAtTheGateway_IpnSettlesIt_ReplaysAreIdempotent_AndBadAmountsAreRejected()
    {
        var token = await NewUserAsync();
        var product = await ProductAsync("inStock=true&category=ssd&maxPrice=7000");
        await CartAsync(token, product);
        var addr = await AddressAsync(token);
        var placed = await (await _client.SendAsync(Req(HttpMethod.Post, "/api/v1/orders", token,
            new { shippingMethod = "HomeDeliveryInsideDhaka", addressId = addr, paymentMethod = "Online" }))).ReadAsync<PlaceOrderResult>();
        var tran = placed.Order.OrderNumber + "-1";
        Assert.Equal("https://sandbox.gateway.test/pay/" + tran, placed.Payment!.RedirectUrl);

        FormUrlEncodedContent Form(string amount, string sig = "ok", string status = "VALID") => new(new Dictionary<string, string>
            { ["tran_id"] = tran, ["status"] = status, ["amount"] = amount, ["currency"] = "BDT", ["sig"] = sig });

        // forged or underpaid -> rejected (non-200 so the gateway retries/alerts), nothing changes
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsync("/api/v1/payments/sslcommerz/ipn", Form(placed.Order.GrandTotal.ToString("0.00"), sig: "forged"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsync("/api/v1/payments/sslcommerz/ipn", Form("1.00"))).StatusCode);
        var still = await (await _client.SendAsync(Req(HttpMethod.Get, $"/api/v1/orders/{placed.Order.OrderNumber}", token))).ReadAsync<OrderDetailDto>();
        Assert.Equal(PaymentStatus.Unpaid, still.PaymentStatus);

        var amount = placed.Order.GrandTotal.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(HttpStatusCode.OK, (await _client.PostAsync("/api/v1/payments/sslcommerz/ipn", Form(amount))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.PostAsync("/api/v1/payments/sslcommerz/ipn", Form(amount))).StatusCode);   // replay
        // browser redirect after payment -> storefront order page
        var browser = await _client.PostAsync("/api/v1/payments/sslcommerz/success", Form(amount));
        Assert.Equal(HttpStatusCode.Redirect, browser.StatusCode);
        Assert.Equal($"https://shop.test/account/orders/{placed.Order.OrderNumber}?payment=success", browser.Headers.Location!.ToString());

        var paid = await (await _client.SendAsync(Req(HttpMethod.Get, $"/api/v1/orders/{placed.Order.OrderNumber}", token))).ReadAsync<OrderDetailDto>();
        Assert.Equal((PaymentStatus.Paid, OrderStatus.Confirmed), (paid.PaymentStatus, paid.Status));
        Assert.Equal(1, paid.History.Count(h => h.Status == OrderStatus.Confirmed));
        Assert.Equal(1, factory.Emails.Sent.Count(m => m.Subject.StartsWith("Payment received") && m.Subject.Contains(placed.Order.OrderNumber)));
    }

    [Fact]
    public async Task FailedBrowserReturn_SendsTheCustomerBackToRetry_AndUnknownInputsDoNotLeak()
    {
        var token = await NewUserAsync();
        var product = await ProductAsync("inStock=true&category=ssd&maxPrice=7000");
        await CartAsync(token, product);
        var addr = await AddressAsync(token);
        var placed = await (await _client.SendAsync(Req(HttpMethod.Post, "/api/v1/orders", token, new { shippingMethod = "HomeDeliveryInsideDhaka", addressId = addr, paymentMethod = "Online" }))).ReadAsync<PlaceOrderResult>();

        var fail = await _client.PostAsync("/api/v1/payments/sslcommerz/fail", new FormUrlEncodedContent(new Dictionary<string, string> { ["tran_id"] = placed.Order.OrderNumber + "-1", ["status"] = "FAILED" }));
        Assert.Equal($"https://shop.test/account/orders/{placed.Order.OrderNumber}?payment=failed", fail.Headers.Location!.ToString());
        var retry = await (await _client.SendAsync(Req(HttpMethod.Post, $"/api/v1/orders/{placed.Order.OrderNumber}/pay", token))).ReadAsync<PaymentRedirectDto>();
        Assert.EndsWith(placed.Order.OrderNumber + "-2", retry.RedirectUrl);

        var unknown = await _client.PostAsync("/api/v1/payments/sslcommerz/success", new FormUrlEncodedContent(new Dictionary<string, string> { ["tran_id"] = "nope" }));
        Assert.Equal("https://shop.test/account/orders?payment=error", unknown.Headers.Location!.ToString());
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsync("/api/v1/payments/unknown/ipn", new FormUrlEncodedContent([]))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsync("/api/v1/payments/sslcommerz/ipn", JsonContent.Create(new { tran_id = "x" }))).StatusCode);
    }

    [Fact]
    public async Task StorePickup_NeedsNoAddress_AndCharges_Nothing_WhileCouponsAreRecomputedServerSide()
    {
        var token = await NewUserAsync();
        var product = await ProductAsync("inStock=true&category=ssd&maxPrice=7000");
        await CartAsync(token, product, 3);
        var quote = await (await _client.SendAsync(Req(HttpMethod.Post, "/api/v1/checkout/quote", token, new { shippingMethod = "StorePickup", couponCode = "WELCOME10" }))).ReadAsync<CheckoutQuoteDto>();
        Assert.Equal(0m, quote.ShippingFee);
        Assert.True(quote.CouponApplied);
        Assert.Equal(quote.Subtotal - quote.Discount, quote.GrandTotal);

        var bad = await (await _client.SendAsync(Req(HttpMethod.Post, "/api/v1/checkout/quote", token, new { shippingMethod = "StorePickup", couponCode = "NOPE" }))).ReadAsync<CheckoutQuoteDto>();
        Assert.False(bad.CouponApplied);
        Assert.Equal(bad.Subtotal, bad.GrandTotal);

        var placed = await (await _client.SendAsync(Req(HttpMethod.Post, "/api/v1/orders", token,
            new { shippingMethod = "StorePickup", contactName = "Karim", contactPhone = "01812345678", paymentMethod = "CashOnDelivery", couponCode = "WELCOME10" }))).ReadAsync<PlaceOrderResult>();
        Assert.Equal(quote.GrandTotal, placed.Order.GrandTotal);
        Assert.NotNull(placed.Order.Pickup);
        Assert.Equal("WELCOME10", placed.Order.CouponCode);
    }
}
