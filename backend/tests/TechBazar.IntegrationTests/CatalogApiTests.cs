using System.Net;
using System.Text.Json;
using TechBazar.Application.Catalog.Dtos;
using TechBazar.Application.Common;
using TechBazar.Domain.Enums;
using TechBazar.IntegrationTests.Support;

namespace TechBazar.IntegrationTests;

public class CatalogApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<PagedResult<ProductListItemDto>> ListAsync(string query)
    {
        var r = await _client.GetAsync($"/api/v1/products{query}");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return await r.ReadAsync<PagedResult<ProductListItemDto>>();
    }

    [Fact]
    public async Task List_ReturnsPagedEnvelope()
    {
        var page = await ListAsync("?pageSize=5&page=2");
        Assert.Equal(5, page.Items.Count);
        Assert.Equal(2, page.Page);
        Assert.True(page.TotalCount >= 40);
        Assert.All(page.Items, i => Assert.True(i.EffectivePrice > 0 && !string.IsNullOrEmpty(i.Slug)));
    }

    [Fact]
    public async Task List_SerialisesEnumsAsStrings()
    {
        var json = await (await _client.GetAsync("/api/v1/products?pageSize=1")).Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.String, doc.RootElement.GetProperty("items")[0].GetProperty("stockStatus").ValueKind);
    }

    [Fact]
    public async Task List_FiltersByCategoryBrandPriceAndSpec()
    {
        var page = await ListAsync("?category=motherboard&brand=gigabyte&brand=msi&minPrice=10000&maxPrice=20000&spec=Socket:AM5&sort=price_asc");
        Assert.NotEmpty(page.Items);
        Assert.All(page.Items, p =>
        {
            Assert.Equal("motherboard", p.CategorySlug);
            Assert.Contains(p.BrandSlug, new[] { "gigabyte", "msi" });
            Assert.InRange(p.EffectivePrice, 10000m, 20000m);
        });
        Assert.Contains(page.Items, p => p.Name.Contains("B650M"));
    }

    [Fact]
    public async Task List_SortsByPriceDescending()
    {
        var page = await ListAsync("?sort=price_desc&pageSize=30");
        Assert.Equal(page.Items.OrderByDescending(i => i.EffectivePrice).Select(i => i.Id).Count(), page.Items.Count);
        Assert.Equal(page.Items.Select(i => i.EffectivePrice).OrderDescending(), page.Items.Select(i => i.EffectivePrice));
    }

    [Fact]
    public async Task List_InStock_OnlyReturnsInStock()
    {
        var page = await ListAsync("?inStock=true&pageSize=60");
        Assert.All(page.Items, p => Assert.Equal(StockStatus.InStock, p.StockStatus));
    }

    [Theory]
    [InlineData("?pageSize=1000", "pageSize")]
    [InlineData("?page=0", "page")]
    [InlineData("?sort=cheapest", "sort")]
    [InlineData("?minPrice=500&maxPrice=100", "minPrice")]
    [InlineData("?spec=oops", "spec")]
    public async Task List_InvalidQuery_Returns400ValidationProblem(string query, string field)
    {
        var r = await _client.GetAsync($"/api/v1/products{query}");
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        using var problem = await r.ProblemAsync();
        Assert.Equal(400, problem.RootElement.GetProperty("status").GetInt32());
        Assert.True(problem.RootElement.GetProperty("errors").TryGetProperty(field, out _), $"no error for '{field}'");
        Assert.True(problem.RootElement.TryGetProperty("traceId", out _));
    }

    [Fact]
    public async Task List_UnparseableValue_Returns400Problem()
    {
        var r = await _client.GetAsync("/api/v1/products?page=abc");
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        using var _ = await r.ProblemAsync();
    }

    [Fact]
    public async Task Details_ReturnsSpecsBreadcrumbsAndPricing()
    {
        var r = await _client.GetAsync("/api/v1/products/asus-dual-geforce-rtx-4060-oc-edition-8gb-graphics-card");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var d = await r.ReadAsync<ProductDetailDto>();

        Assert.Equal("ASUS", d.Brand.Name);
        Assert.Equal(["Component", "Graphics Card"], d.Breadcrumbs.Select(b => b.Name));
        Assert.Contains(d.Specifications.SelectMany(g => g.Items), i => i is { Key: "TDP", Value: "115 W" });
        Assert.Equal(42500m, d.EffectivePrice);
        Assert.Equal(0, d.DiscountPercent);
    }

    [Fact]
    public async Task Details_Unknown_Returns404Problem()
    {
        var r = await _client.GetAsync("/api/v1/products/nope");
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
        using var p = await r.ProblemAsync();
        Assert.Equal(404, p.RootElement.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task Related_ReturnsSameCategoryWithoutSelf()
    {
        var r = await _client.GetAsync("/api/v1/products/amd-ryzen-5-5600-processor/related?count=4");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var items = await r.ReadAsync<List<ProductListItemDto>>();
        Assert.Equal(4, items.Count);
        Assert.DoesNotContain(items, i => i.Slug == "amd-ryzen-5-5600-processor");
        Assert.All(items, i => Assert.Equal("processor", i.CategorySlug));
    }

    [Fact]
    public async Task Facets_ExposeFilterableSpecs()
    {
        var f = await (await _client.GetAsync("/api/v1/products/facets?category=processor")).ReadAsync<ProductFacetsDto>();
        Assert.Contains(f.Specifications, s => s.Key == "Socket" && s.Values.Any(v => v.Value == "AM5"));
        Assert.Contains(f.Brands, b => b.Slug == "intel");
    }

    [Fact]
    public async Task Categories_ReturnsTreeWithNestedComponents()
    {
        var tree = await (await _client.GetAsync("/api/v1/categories")).ReadAsync<List<CategoryTreeNodeDto>>();
        var component = Assert.Single(tree, t => t.Slug == "component");
        Assert.Contains(component.Children, c => c.Slug == "processor" && c.ProductCount > 0);
    }

    [Fact]
    public async Task CategoryBySlug_UnknownReturns404() =>
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/api/v1/categories/zzz")).StatusCode);

    [Fact]
    public async Task Brands_ReturnsAtLeastTen()
    {
        var brands = await (await _client.GetAsync("/api/v1/brands")).ReadAsync<List<BrandListItemDto>>();
        Assert.True(brands.Count >= 10);
    }

    [Fact]
    public async Task Search_RequiresQuery_AndFindsProducts()
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync("/api/v1/search")).StatusCode);

        var page = await (await _client.GetAsync("/api/v1/search?q=RTX%204060")).ReadAsync<PagedResult<ProductListItemDto>>();
        Assert.NotEmpty(page.Items);
        Assert.All(page.Items, p => Assert.Contains("4060", p.Name));
    }

    [Fact]
    public async Task Autocomplete_ReturnsSuggestions_AndValidatesInput()
    {
        var r = await _client.GetAsync("/api/v1/search/autocomplete?q=kings&limit=3");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var result = await r.ReadAsync<AutocompleteResultDto>();
        Assert.InRange(result.Products.Count, 1, 3);
        Assert.Contains(result.Brands, b => b.Slug == "kingston");

        Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync("/api/v1/search/autocomplete?q=k")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync("/api/v1/search/autocomplete")).StatusCode);
    }

    [Fact]
    public async Task Autocomplete_DefaultLimitIsApplied()
    {
        var result = await (await _client.GetAsync("/api/v1/search/autocomplete?q=a")).Content.ReadAsStringAsync();
        Assert.Contains("errors", result); // single-character query rejected, not defaulted
        var ok = await (await _client.GetAsync("/api/v1/search/autocomplete?q=ga")).ReadAsync<AutocompleteResultDto>();
        Assert.True(ok.Products.Count <= 8);
    }

    [Fact]
    public async Task OutputCache_ServesRepeatedCatalogRequestsConsistently()
    {
        var a = await _client.GetStringAsync("/api/v1/products?category=ssd");
        var b = await _client.GetStringAsync("/api/v1/products?category=ssd");
        Assert.Equal(a, b);
        var c = await _client.GetStringAsync("/api/v1/products?category=ram");
        Assert.NotEqual(a, c); // cache is varied by query string
    }

    [Fact]
    public async Task Cors_AllowsAngularDevOrigin_AndRejectsOthers()
    {
        var allowed = new HttpRequestMessage(HttpMethod.Options, "/api/v1/products");
        allowed.Headers.Add("Origin", "http://localhost:4200");
        allowed.Headers.Add("Access-Control-Request-Method", "GET");
        var ok = await _client.SendAsync(allowed);
        Assert.Equal("http://localhost:4200", ok.Headers.GetValues("Access-Control-Allow-Origin").Single());

        var denied = new HttpRequestMessage(HttpMethod.Options, "/api/v1/products");
        denied.Headers.Add("Origin", "http://evil.example");
        denied.Headers.Add("Access-Control-Request-Method", "GET");
        Assert.False((await _client.SendAsync(denied)).Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task Swagger_WhenExplicitlyEnabled_PublishesDocumentWithBearerScheme()
    {
        // Swagger is off outside Development unless Swagger:Enabled=true (see SecurityApiTests for the default-off check).
        using var f = ApiFactory.WithConfig(("Swagger:Enabled", "true"));
        using var c = f.CreateClient();
        var r = await c.GetAsync("/swagger/v1/swagger.json");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        using var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
        Assert.Equal("bearer", doc.RootElement.GetProperty("components").GetProperty("securitySchemes").GetProperty("Bearer").GetProperty("scheme").GetString());
        var paths = doc.RootElement.GetProperty("paths");
        foreach (var p in new[] { "/api/v1/products", "/api/v1/products/{slug}", "/api/v1/auth/login", "/api/v1/search/autocomplete", "/api/v1/categories", "/api/v1/brands" })
            Assert.True(paths.TryGetProperty(p, out _), p);
    }

    [Fact]
    public async Task UnknownRoute_IsDeniedAsAProblem_WithoutRevealingWhichRoutesExist()
    {
        // Deny-by-default: anonymous callers get 401 for unknown AND for protected routes alike (no route enumeration).
        var r = await _client.GetAsync("/api/v1/nothing-here");
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
        using var _ = await r.ProblemAsync();
    }
}
