using System.Net;
using System.Net.Http.Json;
using TechBazar.Application.Auth;
using TechBazar.Application.Catalog.Dtos;
using TechBazar.Application.Common;
using TechBazar.Application.Orders;
using TechBazar.Application.PcBuilder;
using TechBazar.IntegrationTests.Support;
using Xunit.Abstractions;

namespace TechBazar.IntegrationTests;

/// <summary>
/// Database-round-trip budgets per endpoint, measured with an EF Core command interceptor against the seeded catalogue.
/// An N+1 pattern shows up as a command count that GROWS with the amount of data returned, so the key assertions compare the count
/// for a small and a large result: they must be equal. The absolute budgets are deliberately a little above today's numbers.
/// </summary>
public class QueryBudgetTests(ApiFactory factory, ITestOutputHelper output) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private int _lastSelects;

    private async Task<int> Queries(Func<Task<HttpResponseMessage>> call, string label, HttpStatusCode expected = HttpStatusCode.OK)
    {
        factory.Sql.Reset();
        var r = await call();
        Assert.Equal(expected, r.StatusCode);
        await r.Content.ReadAsStringAsync();
        var n = factory.Sql.Count;
        _lastSelects = factory.Sql.Commands.Count(c => c.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase));
        var kinds = factory.Sql.Commands.GroupBy(c => c.TrimStart().Split(' ', 2)[0].ToUpperInvariant()).OrderBy(g => g.Key).Select(g => $"{g.Key}={g.Count()}");
        output.WriteLine($"{n,3} SQL commands  {label}   [{string.Join(' ', kinds)}]");
        return n;
    }

    private async Task<string> NewUserToken()
    {
        var r = await _client.PostJsonAsync("/api/v1/auth/register", new { fullName = "Perf User", email = $"p{Guid.NewGuid():N}@example.com", password = "Passw0rdX" });
        return (await r.ReadAsync<AuthResponse>()).AccessToken;
    }

    private static HttpRequestMessage Req(HttpMethod m, string url, string token, object? body = null)
    {
        var msg = new HttpRequestMessage(m, url).WithBearer(token);
        if (body is not null) msg.Content = JsonContent.Create(body, options: Http.Json);
        return msg;
    }

    // ------------------------------------------------------------------ catalog (public, output-cached in production; measured uncached)
    [Fact]
    public async Task ProductListing_UsesTheSameNumberOfQueriesForFiveOrSixtyProducts()
    {
        var small = await Queries(() => _client.GetAsync("/api/v1/products?pageSize=5&sort=newest"), "GET /products?pageSize=5");
        var large = await Queries(() => _client.GetAsync("/api/v1/products?pageSize=60&sort=price_asc"), "GET /products?pageSize=60");
        Assert.Equal(small, large);
        Assert.True(large <= 3, $"listing used {large} queries");
    }

    [Fact]
    public async Task ProductDetail_IsAFixedNumberOfQueries_RegardlessOfSpecsImagesOrReviews()
    {
        var first = await Queries(() => _client.GetAsync("/api/v1/products/amd-ryzen-5-5600-processor"), "GET /products/{slug} (ryzen)");
        var other = await Queries(() => _client.GetAsync("/api/v1/products/corsair-cv550-550w-80-plus-bronze-power-supply"), "GET /products/{slug} (psu)");
        Assert.Equal(first, other);
        Assert.True(first <= 6, $"detail used {first} queries");
    }

    [Fact]
    public async Task CatalogReadsStayWithinBudget()
    {
        var facetsCpu = await Queries(() => _client.GetAsync("/api/v1/products/facets?category=processor"), "GET /products/facets (processor)");
        var facetsAll = await Queries(() => _client.GetAsync("/api/v1/products/facets"), "GET /products/facets (all)");
        var facetsSsd = await Queries(() => _client.GetAsync("/api/v1/products/facets?category=ssd"), "GET /products/facets (ssd)");
        Assert.Equal(facetsCpu, facetsSsd);          // independent of how many spec keys the category has
        Assert.True(facetsAll <= 8 && facetsCpu <= 8);
        Assert.True(await Queries(() => _client.GetAsync("/api/v1/products/amd-ryzen-5-5600-processor/related?count=8"), "GET /products/{slug}/related") <= 4);
        Assert.True(await Queries(() => _client.GetAsync("/api/v1/categories"), "GET /categories") <= 2);
        Assert.True(await Queries(() => _client.GetAsync("/api/v1/brands"), "GET /brands") <= 2);
        Assert.True(await Queries(() => _client.GetAsync("/api/v1/search/autocomplete?q=ryz"), "GET /search/autocomplete") <= 4);
        Assert.True(await Queries(() => _client.GetAsync("/api/v1/search?q=ryzen&pageSize=40"), "GET /search") <= 3);
    }

    // ------------------------------------------------------------------ shopping & orders
    [Fact]
    public async Task Mfa_StatusAndProfile_QueryCountIsConstant_RegardlessOfRecoveryCodes()
    {
        var token = await NewUserToken();
        var profileOff = await Queries(() => _client.SendAsync(Req(HttpMethod.Get, "/api/v1/auth/me", token)), "GET /auth/me (no MFA)");
        var statusOff = await Queries(() => _client.SendAsync(Req(HttpMethod.Get, "/api/v1/auth/mfa", token)), "GET /auth/mfa (no MFA)");

        var setup = await (await _client.SendAsync(Req(HttpMethod.Post, "/api/v1/auth/mfa/setup", token, new { }))).ReadAsync<TechBazar.Application.Mfa.MfaSetupDto>();
        Assert.True(TechBazar.Application.Mfa.Base32.TryDecode(setup.Secret, out var secret));
        var code = TechBazar.Application.Mfa.Totp.Compute(secret, TechBazar.Application.Mfa.Totp.StepAt(DateTimeOffset.UtcNow) - 1);
        var enabled = await (await _client.SendAsync(Req(HttpMethod.Post, "/api/v1/auth/mfa/enable", token, new { code }))).ReadAsync<TechBazar.Application.Mfa.MfaEnabledDto>();

        // 10 recovery codes now exist: the status must still be a fixed number of aggregate queries, not one per code
        var profileOn = await Queries(() => _client.SendAsync(Req(HttpMethod.Get, "/api/v1/auth/me", enabled.Auth.AccessToken)), "GET /auth/me (MFA, 10 codes)");
        var statusOn = await Queries(() => _client.SendAsync(Req(HttpMethod.Get, "/api/v1/auth/mfa", enabled.Auth.AccessToken)), "GET /auth/mfa (MFA, 10 codes)");
        Assert.True(profileOn - profileOff <= 1, $"/auth/me grew from {profileOff} to {profileOn}");
        Assert.True(statusOn - statusOff <= 1, $"/auth/mfa grew from {statusOff} to {statusOn}");
        Assert.True(statusOn <= 6, $"/auth/mfa used {statusOn} commands");
    }

    [Fact]
    public async Task Cart_QueryCountDoesNotGrowWithTheNumberOfLines()
    {
        var token = await NewUserToken();
        var products = (await (await _client.GetAsync("/api/v1/products?inStock=true&pageSize=12")).ReadAsync<PagedResult<ProductListItemDto>>()).Items;
        await _client.SendAsync(Req(HttpMethod.Put, $"/api/v1/cart/items/{products[0].Id}", token, new { quantity = 1 }));
        var one = await Queries(() => _client.SendAsync(Req(HttpMethod.Get, "/api/v1/cart", token)), "GET /cart (1 line)");
        foreach (var p in products.Skip(1).Take(9)) await _client.SendAsync(Req(HttpMethod.Put, $"/api/v1/cart/items/{p.Id}", token, new { quantity = 1 }));
        var ten = await Queries(() => _client.SendAsync(Req(HttpMethod.Get, "/api/v1/cart", token)), "GET /cart (10 lines)");
        Assert.Equal(one, ten);

        var preview1 = await Queries(() => _client.PostAsJsonAsync("/api/v1/cart/preview", new { items = products.Take(1).Select(p => new { productId = p.Id, quantity = 1 }) }), "POST /cart/preview (1)");
        var preview10 = await Queries(() => _client.PostAsJsonAsync("/api/v1/cart/preview", new { items = products.Take(10).Select(p => new { productId = p.Id, quantity = 1 }) }), "POST /cart/preview (10)");
        Assert.Equal(preview1, preview10);
    }

    [Fact]
    public async Task Checkout_Quote_PlaceOrder_AndOrderReads_DoNotScaleWithCartOrOrderCount()
    {
        var token = await NewUserToken();
        var addr = await (await _client.SendAsync(Req(HttpMethod.Post, "/api/v1/addresses", token,
            new { label = "Home", fullName = "Perf", phone = "01712345678", division = "Dhaka", district = "Dhaka", addressLine = "Road 1", isDefault = true }))).ReadAsync<AddressDto>();
        var products = (await (await _client.GetAsync("/api/v1/products?inStock=true&pageSize=12")).ReadAsync<PagedResult<ProductListItemDto>>()).Items;

        async Task Fill(int n) { foreach (var p in products.Take(n)) await _client.SendAsync(Req(HttpMethod.Put, $"/api/v1/cart/items/{p.Id}", token, new { quantity = 1 })); }
        var quote = new { shippingMethod = "HomeDeliveryInsideDhaka", addressId = addr.Id };
        var place = new { shippingMethod = "HomeDeliveryInsideDhaka", addressId = addr.Id, paymentMethod = "CashOnDelivery" };

        await Fill(1);
        var q1 = await Queries(() => _client.SendAsync(Req(HttpMethod.Post, "/api/v1/checkout/quote", token, quote)), "POST /checkout/quote (1 line)");
        var p1 = await Queries(() => _client.SendAsync(Req(HttpMethod.Post, "/api/v1/orders", token, place)), "POST /orders (1 line)", HttpStatusCode.Created);
        var p1Selects = _lastSelects;

        await Fill(10);
        var q10 = await Queries(() => _client.SendAsync(Req(HttpMethod.Post, "/api/v1/checkout/quote", token, quote)), "POST /checkout/quote (10 lines)");
        var p10 = await Queries(() => _client.SendAsync(Req(HttpMethod.Post, "/api/v1/orders", token, place)), "POST /orders (10 lines)", HttpStatusCode.Created);
        Assert.Equal(q1, q10);
        // READS must not grow with the cart (stock rows are loaded together, not one by one). Writes do grow per row on SQLite - which
        // executes one INSERT/UPDATE per command - but SQL Server sends them as batches, so they are not round trips there.
        Assert.Equal(p1Selects, _lastSelects);
        Assert.True(p10 <= p1 + 3 * 10, $"placing a 10-line order used {p10} commands vs {p1} for 1 line");

        for (var i = 0; i < 13; i++) { await Fill(1); await _client.SendAsync(Req(HttpMethod.Post, "/api/v1/orders", token, place)); }
        var list = await (await _client.SendAsync(Req(HttpMethod.Get, "/api/v1/orders?pageSize=3", token))).ReadAsync<PagedResult<OrderSummaryDto>>();
        var l3 = await Queries(() => _client.SendAsync(Req(HttpMethod.Get, "/api/v1/orders?pageSize=3", token)), "GET /orders?pageSize=3");
        var l15 = await Queries(() => _client.SendAsync(Req(HttpMethod.Get, "/api/v1/orders?pageSize=15", token)), "GET /orders?pageSize=15");
        Assert.Equal(l3, l15);

        var number = list.Items[0].OrderNumber;
        var d = await Queries(() => _client.SendAsync(Req(HttpMethod.Get, $"/api/v1/orders/{number}", token)), "GET /orders/{number}");
        Assert.True(d <= 6, $"order detail used {d} queries");
    }

    // ------------------------------------------------------------------ PC builder & admin
    [Fact]
    public async Task PcBuilderEvaluation_IsOneBatchOfQueries_NotOnePerPart()
    {
        async Task<int> Items(int count)
        {
            var parts = new List<object>();
            foreach (var (slot, cat) in new[] { ("Cpu", "processor"), ("Motherboard", "motherboard"), ("Ram", "ram"), ("Storage", "ssd"), ("Gpu", "graphics-card"), ("Psu", "power-supply"), ("Case", "casing") }.Take(count))
            {
                var p = (await (await _client.GetAsync($"/api/v1/products?category={cat}&inStock=true&pageSize=1")).ReadAsync<PagedResult<ProductListItemDto>>()).Items.First();
                parts.Add(new { slot, productId = p.Id });
            }
            return await Queries(() => _client.PostAsJsonAsync("/api/v1/pc-builder/evaluate", new { items = parts }, Http.Json), $"POST /pc-builder/evaluate ({count} parts)");
        }
        var two = await Items(2);
        var seven = await Items(7);
        Assert.Equal(two, seven);
    }

    [Fact]
    public async Task AdminLists_AreBoundedByThePage_NotByTheTableSize()
    {
        var admin = (await (await _client.PostJsonAsync("/api/v1/auth/login", new { email = ApiFactory.AdminEmail, password = ApiFactory.AdminPassword })).ReadAsync<AuthResponse>()).AccessToken;
        var a5 = await Queries(() => _client.SendAsync(Req(HttpMethod.Get, "/api/v1/admin/products?pageSize=5", admin)), "GET /admin/products?pageSize=5");
        var a100 = await Queries(() => _client.SendAsync(Req(HttpMethod.Get, "/api/v1/admin/products?pageSize=100", admin)), "GET /admin/products?pageSize=100");
        Assert.Equal(a5, a100);
        var o = await Queries(() => _client.SendAsync(Req(HttpMethod.Get, "/api/v1/admin/orders?pageSize=100", admin)), "GET /admin/orders?pageSize=100");
        Assert.True(o <= 3);
        var dash = await Queries(() => _client.SendAsync(Req(HttpMethod.Get, "/api/v1/admin/dashboard?days=90", admin)), "GET /admin/dashboard?days=90");
        Assert.True(dash <= 12, $"dashboard used {dash} queries");
        Assert.True(await Queries(() => _client.SendAsync(Req(HttpMethod.Get, "/api/v1/admin/categories", admin)), "GET /admin/categories") <= 3);
    }
}
