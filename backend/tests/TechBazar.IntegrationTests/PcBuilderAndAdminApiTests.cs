using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using TechBazar.Application.Admin;
using TechBazar.Application.Auth;
using TechBazar.Application.Catalog.Dtos;
using TechBazar.Application.Common;
using TechBazar.Application.Orders;
using TechBazar.Application.PcBuilder;
using TechBazar.Domain.Enums;
using TechBazar.IntegrationTests.Support;

namespace TechBazar.IntegrationTests;

public class PcBuilderApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<int> IdAsync(string search) =>
        (await (await _client.GetAsync($"/api/v1/products?q={Uri.EscapeDataString(search)}&pageSize=1")).ReadAsync<PagedResult<ProductListItemDto>>()).Items.First().Id;

    [Fact]
    public async Task Slots_AreAnonymous_AndCoverTheNineParts()
    {
        var slots = await (await _client.GetAsync("/api/v1/pc-builder/slots")).ReadAsync<List<BuilderSlotDto>>();
        Assert.Equal(9, slots.Count);
        Assert.Contains(slots, s => s.Slot == BuildSlot.Cooler);
        Assert.Contains(slots, s => s.Slot == BuildSlot.Ram && s.AllowMultiple);
    }

    [Fact]
    public async Task Evaluate_FlagsASocketMismatch_ComputedOnTheServer()
    {
        var amd = await IdAsync("Ryzen 5 5600");
        var board = (await (await _client.GetAsync("/api/v1/products?category=motherboard&pageSize=50")).ReadAsync<PagedResult<ProductListItemDto>>()).Items
            .First(p => p.Name.Contains("LGA1700", StringComparison.OrdinalIgnoreCase) || p.Name.Contains("B760") || p.Name.Contains("H610"));
        var r = await _client.PostJsonAsync("/api/v1/pc-builder/evaluate", new { items = new[] { new { slot = "Cpu", productId = amd }, new { slot = "Motherboard", productId = board.Id } } });
        var report = await r.ReadAsync<BuildReportDto>();

        Assert.Contains(report.Compatibility.Issues, i => i.Code == "SOCKET_MISMATCH" && i.Severity == IssueSeverity.Error);
        Assert.False(report.Compatibility.IsCompatible);
        Assert.Equal(report.Lines.Sum(l => l.LineTotal), report.Total);
    }

    [Fact]
    public async Task Evaluate_RejectsBadInput_WithProblemDetails()
    {
        var unknown = await _client.PostJsonAsync("/api/v1/pc-builder/evaluate", new { items = new[] { new { slot = "Cpu", productId = 99999999 } } });
        Assert.True(unknown.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest or HttpStatusCode.Conflict);
        (await unknown.ProblemAsync()).Dispose();
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostJsonAsync("/api/v1/pc-builder/evaluate", new { items = new[] { new { slot = "Banana", productId = 1 } } })).StatusCode);
    }

    [Fact]
    public async Task SavedBuilds_GetAShareCode_AnyoneCanOpen()
    {
        var cpu = await IdAsync("Ryzen 5 5600");
        var save = await _client.PostJsonAsync("/api/v1/pc-builder/builds", new { name = "Budget gaming", items = new[] { new { slot = "Cpu", productId = cpu } } });
        Assert.Equal(HttpStatusCode.Created, save.StatusCode);
        var saved = await save.ReadAsync<SavedBuildDto>();
        Assert.Matches("^[A-Za-z0-9]{6,}$", saved.Code);

        var again = await (await factory.CreateClient().GetAsync($"/api/v1/pc-builder/builds/{saved.Code}")).ReadAsync<SavedBuildDto>();
        Assert.Equal("Budget gaming", again.Name);
        Assert.Equal(saved.Report.Total, again.Report.Total);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/api/v1/pc-builder/builds/NOSUCHCODE")).StatusCode);
    }
}

public class AdminApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();
    private string? _admin, _customer;

    private async Task<string> AdminToken() => _admin ??= (await (await _client.PostJsonAsync("/api/v1/auth/login",
        new { email = ApiFactory.AdminEmail, password = ApiFactory.AdminPassword })).ReadAsync<AuthResponse>()).AccessToken;

    private async Task<string> CustomerToken() => _customer ??= (await (await _client.PostJsonAsync("/api/v1/auth/register",
        new { fullName = "Cust", email = $"c{Guid.NewGuid():N}@example.com", password = "Passw0rdX" })).ReadAsync<AuthResponse>()).AccessToken;

    private async Task<HttpResponseMessage> Send(HttpMethod m, string url, string? token, object? body = null)
    {
        var msg = new HttpRequestMessage(m, url);
        if (token is not null) msg.WithBearer(token);
        if (body is not null) msg.Content = JsonContent.Create(body, options: Http.Json);
        return await _client.SendAsync(msg);
    }

    public static IEnumerable<object[]> AdminEndpoints() =>
    [
        ["GET", "/api/v1/admin/dashboard"], ["GET", "/api/v1/admin/products"], ["GET", "/api/v1/admin/products/1"],
        ["GET", "/api/v1/admin/categories"], ["GET", "/api/v1/admin/brands"], ["GET", "/api/v1/admin/coupons"],
        ["GET", "/api/v1/admin/orders"], ["GET", "/api/v1/admin/orders/TB-1"], ["POST", "/api/v1/admin/products"],
        ["POST", "/api/v1/admin/coupons"], ["DELETE", "/api/v1/admin/products/1"], ["PUT", "/api/v1/admin/orders/TB-1/status"],
        ["POST", "/api/v1/admin/orders/TB-1/mark-paid"],
    ];

    [Theory, MemberData(nameof(AdminEndpoints))]
    public async Task EveryAdminEndpoint_IsClosedToAnonymousAndCustomers(string method, string url)
    {
        var m = new HttpMethod(method);
        var anon = await Send(m, url, null, method == "GET" ? null : new { });
        var cust = await Send(m, url, await CustomerToken(), method == "GET" ? null : new { });
        Assert.Equal(HttpStatusCode.Unauthorized, anon.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, cust.StatusCode);
    }

    [Fact]
    public async Task AdminCanReadTheReadEndpoints()
    {
        var t = await AdminToken();
        foreach (var url in new[] { "/api/v1/admin/dashboard", "/api/v1/admin/products", "/api/v1/admin/categories", "/api/v1/admin/brands", "/api/v1/admin/coupons", "/api/v1/admin/orders" })
            Assert.Equal(HttpStatusCode.OK, (await Send(HttpMethod.Get, url, t)).StatusCode);
    }

    [Fact]
    public async Task ProductCrud_WithSpecsAndImages_ShowsUpInThePublicCatalog_AndDeletesSoftly()
    {
        var t = await AdminToken();
        var cats = await (await Send(HttpMethod.Get, "/api/v1/admin/categories", t)).ReadAsync<List<AdminCategoryDto>>();
        var brands = await (await Send(HttpMethod.Get, "/api/v1/admin/brands", t)).ReadAsync<List<AdminBrandDto>>();
        var sku = "IT-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        object Body(string name, decimal price) => new
        {
            name, sku, categoryId = cats.First(c => c.Slug == "processor").Id, brandId = brands.First().Id, price, discountPrice = (decimal?)null,
            stockStatus = "InStock", stockQuantity = 7, warrantyMonths = 36, shortDescription = "x", description = "y", isFeatured = false, isActive = true,
            keyFeatures = new[] { "8 cores" },
            specifications = new[] { new { group = "General", key = "Socket", value = "AM4", isFilterable = (bool?)true }, new { group = "General", key = "TDP", value = "65 W", isFilterable = (bool?)null } },
            images = new[] { new { url = "/uploads/a.png", altText = "a", isPrimary = true } },
        };

        var create = await Send(HttpMethod.Post, "/api/v1/admin/products", t, Body("Integration Test CPU", 12345));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var created = await create.ReadAsync<AdminProductDetailDto>();
        Assert.Equal("integration-test-cpu", created.Slug);

        var pub = await (await _client.GetAsync($"/api/v1/products/{created.Slug}")).ReadAsync<ProductDetailDto>();
        Assert.Equal(12345m, pub.Price);
        Assert.Contains(pub.Specifications.SelectMany(g => g.Items), s => s.Key == "Socket" && s.Value == "AM4");

        // the duplicate SKU is a 409 problem; an invalid body is a 400 with field errors
        Assert.Equal(HttpStatusCode.Conflict, (await Send(HttpMethod.Post, "/api/v1/admin/products", t, Body("Another", 1))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(HttpMethod.Post, "/api/v1/admin/products", t, new { name = "" })).StatusCode);

        var update = await Send(HttpMethod.Put, $"/api/v1/admin/products/{created.Id}", t, Body("Integration Test CPU", 11111));
        Assert.Equal(11111m, (await update.ReadAsync<AdminProductDetailDto>()).Price);
        // the catalog cache is evicted, so the public page shows the new price immediately
        Assert.Equal(11111m, (await (await _client.GetAsync($"/api/v1/products/{created.Slug}")).ReadAsync<ProductDetailDto>()).Price);

        Assert.Equal(HttpStatusCode.NoContent, (await Send(HttpMethod.Delete, $"/api/v1/admin/products/{created.Id}", t)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/v1/products/{created.Slug}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Send(HttpMethod.Get, $"/api/v1/admin/products/{created.Id}", t)).StatusCode);
    }

    [Fact]
    public async Task CategoriesAndBrands_CanBeCreatedEditedAndGuardedAgainstOrphans()
    {
        var t = await AdminToken();
        var cat = await (await Send(HttpMethod.Post, "/api/v1/admin/categories", t, new { name = "IT Gadgets", displayOrder = 99, isActive = true })).ReadAsync<AdminCategoryDto>();
        Assert.Equal("it-gadgets", cat.Slug);
        var renamed = await (await Send(HttpMethod.Put, $"/api/v1/admin/categories/{cat.Id}", t, new { name = "IT Gadgets 2", slug = "it-gadgets", displayOrder = 5, isActive = true })).ReadAsync<AdminCategoryDto>();
        Assert.Equal("IT Gadgets 2", renamed.Name);
        Assert.Equal(HttpStatusCode.Conflict, (await Send(HttpMethod.Post, "/api/v1/admin/categories", t, new { name = "Dup", slug = "it-gadgets", displayOrder = 1, isActive = true })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Send(HttpMethod.Delete, $"/api/v1/admin/categories/{cat.Id}", t)).StatusCode);

        var processor = (await (await Send(HttpMethod.Get, "/api/v1/admin/categories", t)).ReadAsync<List<AdminCategoryDto>>()).First(c => c.Slug == "processor");
        Assert.Equal(HttpStatusCode.Conflict, (await Send(HttpMethod.Delete, $"/api/v1/admin/categories/{processor.Id}", t)).StatusCode);   // still has products

        var brand = await (await Send(HttpMethod.Post, "/api/v1/admin/brands", t, new { name = "IT Brand", isActive = true })).ReadAsync<AdminBrandDto>();
        Assert.Equal("it-brand", brand.Slug);
        Assert.Equal(HttpStatusCode.NoContent, (await Send(HttpMethod.Delete, $"/api/v1/admin/brands/{brand.Id}", t)).StatusCode);
    }

    [Fact]
    public async Task CouponsAreManaged_AndValidated()
    {
        var t = await AdminToken();
        var code = "IT" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var created = await Send(HttpMethod.Post, "/api/v1/admin/coupons", t, new { code = code.ToLowerInvariant(), discountType = "Percentage", value = 15, maxDiscountAmount = 500, isActive = true });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var dto = await created.ReadAsync<AdminCouponDto>();
        Assert.Equal(code, dto.Code);                                     // codes are stored upper-case
        Assert.Equal(HttpStatusCode.Conflict, (await Send(HttpMethod.Post, "/api/v1/admin/coupons", t, new { code, discountType = "FixedAmount", value = 10, isActive = true })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(HttpMethod.Post, "/api/v1/admin/coupons", t, new { code = "BAD1", discountType = "Percentage", value = 150, isActive = true })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Send(HttpMethod.Delete, $"/api/v1/admin/coupons/{dto.Id}", t)).StatusCode);
    }

    [Fact]
    public async Task OrderManagement_DrivesTheCustomersTimeline_AndRejectsIllegalMoves()
    {
        var admin = await AdminToken();
        var cust = (await (await _client.PostJsonAsync("/api/v1/auth/register", new { fullName = "Buyer", email = $"b{Guid.NewGuid():N}@example.com", password = "Passw0rdX" })).ReadAsync<AuthResponse>()).AccessToken;
        var product = (await (await _client.GetAsync("/api/v1/products?inStock=true&category=ssd&maxPrice=7000")).ReadAsync<PagedResult<ProductListItemDto>>()).Items.First();
        await Send(HttpMethod.Put, $"/api/v1/cart/items/{product.Id}", cust, new { quantity = 1 });
        var addr = await (await Send(HttpMethod.Post, "/api/v1/addresses", cust, new { label = "Home", fullName = "Buyer", phone = "01712345678", division = "Dhaka", district = "Dhaka", addressLine = "Road 1", isDefault = true })).ReadAsync<AddressDto>();
        var order = (await (await Send(HttpMethod.Post, "/api/v1/orders", cust, new { shippingMethod = "HomeDeliveryInsideDhaka", addressId = addr.Id, paymentMethod = "CashOnDelivery" })).ReadAsync<PlaceOrderResult>()).Order;

        var listed = await (await Send(HttpMethod.Get, $"/api/v1/admin/orders?search={order.OrderNumber}", admin)).ReadAsync<PagedResult<AdminOrderListItemDto>>();
        Assert.Equal(order.OrderNumber, Assert.Single(listed.Items).OrderNumber);

        Assert.Equal(HttpStatusCode.Conflict, (await Send(HttpMethod.Put, $"/api/v1/admin/orders/{order.OrderNumber}/status", admin, new { status = "Delivered" })).StatusCode);
        foreach (var s in new[] { "Confirmed", "Processing", "Shipped", "Delivered" })
            Assert.Equal(HttpStatusCode.OK, (await Send(HttpMethod.Put, $"/api/v1/admin/orders/{order.OrderNumber}/status", admin, new { status = s, note = "via test" })).StatusCode);

        var mine = await (await Send(HttpMethod.Get, $"/api/v1/orders/{order.OrderNumber}", cust)).ReadAsync<OrderDetailDto>();
        Assert.Equal(OrderStatus.Delivered, mine.Status);
        Assert.Equal(PaymentStatus.Paid, mine.PaymentStatus);            // COD settles on delivery
        Assert.All(mine.Timeline, t => Assert.True(t.Done));
        Assert.Contains(factory.Emails.Sent, m => m.Subject.Contains(order.OrderNumber));
    }

    [Fact]
    public async Task ImageUpload_AcceptsRealImages_RejectsDisguisedFiles_AndServesWhatWasStored()
    {
        var t = await AdminToken();
        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0, 1, 2, 3, 4 };
        HttpRequestMessage Upload(byte[] bytes, string name, string? token)
        {
            var form = new MultipartFormDataContent();
            var file = new ByteArrayContent(bytes); file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            form.Add(file, "file", name);
            var m = new HttpRequestMessage(HttpMethod.Post, "/api/v1/admin/uploads/images") { Content = form };
            return token is null ? m : m.WithBearer(token);
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.SendAsync(Upload(png, "a.png", null))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _client.SendAsync(Upload(png, "a.png", await CustomerToken()))).StatusCode);

        var ok = await _client.SendAsync(Upload(png, "evil.php.png", t));
        Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
        var url = (await ok.ReadAsync<AdminUploadResult>()).Url;
        Assert.StartsWith("/uploads/", url);
        Assert.EndsWith(".png", url);
        Assert.DoesNotContain("evil", url);                              // the client's file name is never used
        var served = await _client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, served.StatusCode);
        Assert.Equal(png, await served.Content.ReadAsByteArrayAsync());

        var svg = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg'><script>alert(1)</script></svg>");
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.SendAsync(Upload(svg, "x.png", t))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.SendAsync(Upload([], "empty.png", t))).StatusCode);
    }

    private sealed record AdminUploadResult(string Url);

    [Fact]
    public async Task Dashboard_ReportsSalesFromRealOrders_AndLowStock()
    {
        var t = await AdminToken();
        var d = await (await Send(HttpMethod.Get, "/api/v1/admin/dashboard?days=7&lowStock=1000000", t)).ReadAsync<DashboardDto>();
        Assert.Equal(7, d.Days);
        Assert.Equal(7, d.SalesByDay.Count);
        Assert.NotEmpty(d.LowStock);
        Assert.All(d.LowStock, p => Assert.True(p.StockQuantity <= 1_000_000));
        Assert.Equal(d.SalesByDay.Sum(s => s.Revenue), d.Revenue);
    }
}
