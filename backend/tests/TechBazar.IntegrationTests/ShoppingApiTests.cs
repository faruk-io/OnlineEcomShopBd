using System.Net;
using System.Net.Http.Json;
using TechBazar.Application.Auth;
using TechBazar.Application.Catalog.Dtos;
using TechBazar.Application.Common;
using TechBazar.Application.Shopping;
using TechBazar.IntegrationTests.Support;

namespace TechBazar.IntegrationTests;

public class ShoppingApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<(string Token, HttpClient Client)> NewUserAsync()
    {
        var r = await _client.PostJsonAsync("/api/v1/auth/register",
            new { fullName = "Test User", email = $"u{Guid.NewGuid():N}@example.com", password = "Passw0rdX" });
        var auth = await r.ReadAsync<AuthResponse>();
        return (auth.AccessToken, _client);
    }

    private static HttpRequestMessage Req(HttpMethod m, string url, string token, object? body = null)
    {
        var msg = new HttpRequestMessage(m, url).WithBearer(token);
        if (body is not null) msg.Content = JsonContent.Create(body, options: Http.Json);
        return msg;
    }

    private async Task<ProductListItemDto> ProductAsync(string query)
    {
        var page = await (await _client.GetAsync($"/api/v1/products?{query}")).ReadAsync<PagedResult<ProductListItemDto>>();
        return page.Items.First();
    }

    [Fact]
    public async Task Cart_And_Wishlist_RequireAuthentication()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.GetAsync("/api/v1/cart")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.GetAsync("/api/v1/wishlist")).StatusCode);
    }

    [Fact]
    public async Task Cart_SetUpdateRemoveClear_ComputesTotalsFromCurrentPrices()
    {
        var (token, c) = await NewUserAsync();
        var sale = await ProductAsync("onSale=true&inStock=true");
        var plain = await ProductAsync("inStock=true&category=ssd&maxPrice=7000");

        var empty = await (await c.SendAsync(Req(HttpMethod.Get, "/api/v1/cart", token))).ReadAsync<CartDto>();
        Assert.Empty(empty.Items);

        await c.SendAsync(Req(HttpMethod.Put, $"/api/v1/cart/items/{sale.Id}", token, new { quantity = 2 }));
        var cart = await (await c.SendAsync(Req(HttpMethod.Put, $"/api/v1/cart/items/{plain.Id}", token, new { quantity = 1 }))).ReadAsync<CartDto>();

        Assert.Equal(2, cart.Items.Count);
        Assert.Equal(3, cart.ItemCount);
        Assert.Equal(sale.EffectivePrice * 2 + plain.EffectivePrice, cart.Subtotal);
        Assert.True(cart.Savings > 0);

        cart = await (await c.SendAsync(Req(HttpMethod.Put, $"/api/v1/cart/items/{sale.Id}", token, new { quantity = 5 }))).ReadAsync<CartDto>();
        Assert.Equal(5, cart.Items.Single(i => i.ProductId == sale.Id).Quantity);

        cart = await (await c.SendAsync(Req(HttpMethod.Delete, $"/api/v1/cart/items/{plain.Id}", token))).ReadAsync<CartDto>();
        Assert.Single(cart.Items);

        // re-adding a removed (soft-deleted) line works thanks to the filtered unique index
        cart = await (await c.SendAsync(Req(HttpMethod.Put, $"/api/v1/cart/items/{plain.Id}", token, new { quantity = 1 }))).ReadAsync<CartDto>();
        Assert.Equal(2, cart.Items.Count);

        cart = await (await c.SendAsync(Req(HttpMethod.Delete, "/api/v1/cart", token))).ReadAsync<CartDto>();
        Assert.Empty(cart.Items);
    }

    [Fact]
    public async Task Cart_RejectsBadQuantityUnknownAndUnavailableProducts()
    {
        var (token, c) = await NewUserAsync();
        var inStock = await ProductAsync("inStock=true");
        var outOfStock = (await (await _client.GetAsync("/api/v1/products/asus-tuf-gaming-radeon-rx-7800-xt-oc-16gb-graphics-card")).ReadAsync<ProductDetailDto>());

        Assert.Equal(HttpStatusCode.BadRequest, (await c.SendAsync(Req(HttpMethod.Put, $"/api/v1/cart/items/{inStock.Id}", token, new { quantity = 0 }))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.SendAsync(Req(HttpMethod.Put, $"/api/v1/cart/items/{inStock.Id}", token, new { quantity = 11 }))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.SendAsync(Req(HttpMethod.Put, "/api/v1/cart/items/999999", token, new { quantity = 1 }))).StatusCode);
        var conflict = await c.SendAsync(Req(HttpMethod.Put, $"/api/v1/cart/items/{outOfStock.Id}", token, new { quantity = 1 }));
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        using var _ = await conflict.ProblemAsync();
    }

    [Fact]
    public async Task Cart_Merge_AddsGuestLines_CapsQuantities_AndSkipsUnavailable()
    {
        var (token, c) = await NewUserAsync();
        var a = await ProductAsync("inStock=true&category=ram");
        var b = await ProductAsync("inStock=true&category=monitor");
        var outOfStock = await (await _client.GetAsync("/api/v1/products/asus-tuf-gaming-radeon-rx-7800-xt-oc-16gb-graphics-card")).ReadAsync<ProductDetailDto>();

        await c.SendAsync(Req(HttpMethod.Put, $"/api/v1/cart/items/{a.Id}", token, new { quantity = 8 }));
        var merged = await (await c.SendAsync(Req(HttpMethod.Post, "/api/v1/cart/merge", token, new
        {
            items = new[] { new { productId = a.Id, quantity = 5 }, new { productId = b.Id, quantity = 2 }, new { productId = outOfStock.Id, quantity = 1 }, new { productId = 999999, quantity = 1 } },
        }))).ReadAsync<CartDto>();

        Assert.Equal(2, merged.Items.Count);
        Assert.Equal(10, merged.Items.Single(i => i.ProductId == a.Id).Quantity); // 8 + 5 capped at 10
        Assert.Equal(2, merged.Items.Single(i => i.ProductId == b.Id).Quantity);
    }

    [Fact]
    public async Task Cart_Preview_PricesGuestCartAnonymously()
    {
        var sale = await ProductAsync("onSale=true&inStock=true");
        var r = await _client.PostJsonAsync("/api/v1/cart/preview", new
        {
            items = new[] { new { productId = sale.Id, quantity = 3 }, new { productId = 999999, quantity = 1 } },
        });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var cart = await r.ReadAsync<CartDto>();
        var line = Assert.Single(cart.Items);
        Assert.Equal(sale.EffectivePrice * 3, cart.Subtotal);
        Assert.Equal(sale.Price, line.ListPrice);
        Assert.True(line.Purchasable);
    }

    [Fact]
    public async Task Carts_AreIsolatedPerUser()
    {
        var (t1, c) = await NewUserAsync();
        var (t2, _) = await NewUserAsync();
        var p = await ProductAsync("inStock=true");
        await c.SendAsync(Req(HttpMethod.Put, $"/api/v1/cart/items/{p.Id}", t1, new { quantity = 1 }));

        var other = await (await c.SendAsync(Req(HttpMethod.Get, "/api/v1/cart", t2))).ReadAsync<CartDto>();
        Assert.Empty(other.Items);
    }

    [Fact]
    public async Task Wishlist_AddIsIdempotent_RemoveAndMerge()
    {
        var (token, c) = await NewUserAsync();
        var a = await ProductAsync("category=processor");
        var b = await ProductAsync("category=monitor");

        await c.SendAsync(Req(HttpMethod.Put, $"/api/v1/wishlist/{a.Id}", token));
        var list = await (await c.SendAsync(Req(HttpMethod.Put, $"/api/v1/wishlist/{a.Id}", token))).ReadAsync<List<ProductListItemDto>>();
        Assert.Single(list);

        list = await (await c.SendAsync(Req(HttpMethod.Post, "/api/v1/wishlist/merge", token, new { productIds = new[] { a.Id, b.Id, 999999 } }))).ReadAsync<List<ProductListItemDto>>();
        Assert.Equal(new[] { a.Id, b.Id }.Order(), list.Select(i => i.Id).Order());

        list = await (await c.SendAsync(Req(HttpMethod.Delete, $"/api/v1/wishlist/{a.Id}", token))).ReadAsync<List<ProductListItemDto>>();
        Assert.Equal(b.Id, Assert.Single(list).Id);

        Assert.Equal(HttpStatusCode.NotFound, (await c.SendAsync(Req(HttpMethod.Put, "/api/v1/wishlist/999999", token))).StatusCode);
    }

    [Fact]
    public async Task Profile_CanBeUpdated_AndValidated()
    {
        var (token, c) = await NewUserAsync();

        var ok = await c.SendAsync(Req(HttpMethod.Put, "/api/v1/auth/me", token, new { fullName = "Karim Hossain", phone = "01812345678" }));
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var user = await ok.ReadAsync<UserDto>();
        Assert.Equal("Karim Hossain", user.FullName);
        Assert.Equal("01812345678", user.Phone);

        var me = await (await c.SendAsync(Req(HttpMethod.Get, "/api/v1/auth/me", token))).ReadAsync<UserDto>();
        Assert.Equal("Karim Hossain", me.FullName);

        Assert.Equal(HttpStatusCode.BadRequest, (await c.SendAsync(Req(HttpMethod.Put, "/api/v1/auth/me", token, new { fullName = "", phone = "123" }))).StatusCode);
    }

    [Fact]
    public async Task Products_OnSaleFilter_ReturnsOnlyDiscountedItems()
    {
        var page = await (await _client.GetAsync("/api/v1/products?onSale=true&pageSize=60")).ReadAsync<PagedResult<ProductListItemDto>>();
        Assert.NotEmpty(page.Items);
        Assert.All(page.Items, p => Assert.True(p.EffectivePrice < p.Price));
    }
}
