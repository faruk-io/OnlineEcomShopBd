using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using TechBazar.Application.Auth;
using TechBazar.Application.Catalog.Dtos;
using TechBazar.Application.Common;
using TechBazar.Application.Orders;
using TechBazar.IntegrationTests.Support;

namespace TechBazar.IntegrationTests;

/// <summary>
/// OWASP-oriented regression tests: broken access control (A01), cryptographic/token failures (A02), injection (A03),
/// insecure design / security misconfiguration (A04/A05), identification failures (A07) and logging/monitoring-adjacent hygiene.
/// </summary>
public class SecurityApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    // ------------------------------------------------------------------ A01: authorization inventory
    private sealed record Ep(string Method, string Route, bool Anonymous, bool Authorized, string? Policy, string? Roles);

    private IReadOnlyList<Ep> Endpoints()
    {
        var list = new List<Ep>();
        foreach (var e in factory.Services.GetServices<EndpointDataSource>().SelectMany(s => s.Endpoints).OfType<RouteEndpoint>())
        {
            if (e.Metadata.GetMetadata<ControllerActionDescriptor>() is null) continue;
            var methods = e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["*"];
            var auth = e.Metadata.GetOrderedMetadata<IAuthorizeData>();
            foreach (var m in methods)
                list.Add(new Ep(m, "/" + e.RoutePattern.RawText!.TrimStart('/'), e.Metadata.GetMetadata<IAllowAnonymous>() is not null, auth.Count > 0,
                    auth.Select(a => a.Policy).FirstOrDefault(p => p is not null), auth.Select(a => a.Roles).FirstOrDefault(r => r is not null)));
        }
        return list;
    }

    [Fact]
    public void EveryApiEndpointDeclaresItsAccessExplicitly()
    {
        var eps = Endpoints();
        Assert.True(eps.Count >= 65, $"expected the full API surface, found {eps.Count}");
        var undeclared = eps.Where(e => !e.Anonymous && !e.Authorized).Select(e => $"{e.Method} {e.Route}").ToList();
        Assert.True(undeclared.Count == 0, "Endpoints without [Authorize] or [AllowAnonymous]: " + string.Join(", ", undeclared));
    }

    /// <summary>
    /// The complete list of endpoints reachable without signing in. Adding a public endpoint must be a conscious decision: update this list in the same PR.
    /// </summary>
    private static readonly string[] AnonymousAllowList =
    [
        "GET /api/v1/brands", "GET /api/v1/categories", "GET /api/v1/categories/{slug}",
        "GET /api/v1/checkout/options",
        "GET /api/v1/pc-builder/builds/{code}", "GET /api/v1/pc-builder/slots", "POST /api/v1/pc-builder/builds", "POST /api/v1/pc-builder/evaluate",
        "GET /api/v1/products", "GET /api/v1/products/facets", "GET /api/v1/products/{slug}", "GET /api/v1/products/{slug}/related",
        "GET /api/v1/search", "GET /api/v1/search/autocomplete",
        "POST /api/v1/auth/login", "POST /api/v1/auth/logout", "POST /api/v1/auth/refresh", "POST /api/v1/auth/register",
        "POST /api/v1/cart/preview",
        "POST /api/v1/payments/{gateway}/cancel", "POST /api/v1/payments/{gateway}/fail", "POST /api/v1/payments/{gateway}/ipn", "POST /api/v1/payments/{gateway}/success",
    ];

    [Fact]
    public void OnlyTheReviewedEndpointsAreAnonymous()
    {
        var actual = Endpoints().Where(e => e.Anonymous).Select(e => $"{e.Method} {e.Route}").Distinct().OrderBy(x => x, StringComparer.Ordinal).ToList();
        var expected = AnonymousAllowList.OrderBy(x => x, StringComparer.Ordinal).ToList();
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void EveryAdminEndpointRequiresTheAdminPolicy()
    {
        var admin = Endpoints().Where(e => e.Route.StartsWith("/api/v1/admin/")).ToList();
        Assert.True(admin.Count >= 20, $"found {admin.Count} admin endpoints");
        Assert.All(admin, e => { Assert.False(e.Anonymous, $"{e.Method} {e.Route} is anonymous"); Assert.Equal("AdminOnly", e.Policy); });
    }

    private static string Concrete(string route) => System.Text.RegularExpressions.Regex.Replace(route, @"\{(\w+)(?::(\w+))?\}", m => m.Groups[2].Value == "int" ? "1" : "x");

    [Fact]
    public async Task EveryNonPublicEndpointRejectsAnonymousCallers_AndAdminEndpointsRejectCustomers()
    {
        var customer = (await (await _client.PostJsonAsync("/api/v1/auth/register",
            new { fullName = "Sec Test", email = $"s{Guid.NewGuid():N}@example.com", password = "Passw0rdX" })).ReadAsync<AuthResponse>()).AccessToken;
        var failures = new List<string>();
        foreach (var e in Endpoints().Where(x => !x.Anonymous))
        {
            HttpContent Body() => e.Route.EndsWith("/uploads/images") ? new MultipartFormDataContent() : JsonContent.Create(new { });
            var anon = await _client.SendAsync(new HttpRequestMessage(new HttpMethod(e.Method), Concrete(e.Route)) { Content = Body() });
            if (anon.StatusCode != HttpStatusCode.Unauthorized) failures.Add($"anonymous {e.Method} {e.Route} -> {(int)anon.StatusCode}");

            if (e.Policy == "AdminOnly")
            {
                var asCustomer = await _client.SendAsync(new HttpRequestMessage(new HttpMethod(e.Method), Concrete(e.Route)) { Content = Body() }.WithBearer(customer));
                if (asCustomer.StatusCode != HttpStatusCode.Forbidden) failures.Add($"customer {e.Method} {e.Route} -> {(int)asCustomer.StatusCode}");
            }
        }
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    [Theory]
    [InlineData("/does-not-exist")]
    [InlineData("/api/v1/nothing-here")]
    [InlineData("/.env")]
    [InlineData("/appsettings.json")]
    [InlineData("/swagger/v1/swagger.json")]
    [InlineData("/swagger/index.html")]
    public async Task UnknownAndSensitivePathsAreNotServed(string path)
    {
        var r = await _client.GetAsync(path);
        // Deny-by-default answers 401 even for routes that do not exist (no route enumeration); either way nothing is served.
        Assert.True(r.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Unauthorized, $"{path} -> {(int)r.StatusCode}");
        var body = await r.Content.ReadAsStringAsync();
        Assert.DoesNotContain("openapi", body, StringComparison.OrdinalIgnoreCase);   // swagger is development-only
        Assert.DoesNotContain("ConnectionStrings", body, StringComparison.OrdinalIgnoreCase);
    }

    // ------------------------------------------------------------------ A05: security headers
    [Fact]
    public async Task ApiResponsesCarryDefensiveHeaders()
    {
        var r = await _client.GetAsync("/api/v1/categories");
        Assert.Equal("nosniff", r.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", r.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("no-referrer", r.Headers.GetValues("Referrer-Policy").Single());
        Assert.Contains("default-src 'none'", r.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Contains("frame-ancestors 'none'", r.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Contains("camera=()", r.Headers.GetValues("Permissions-Policy").Single());
        Assert.False(r.Headers.Contains("Server") && r.Headers.GetValues("Server").Any(v => v.Contains("Kestrel")));
        Assert.False(r.Headers.Contains("X-Powered-By"));
    }

    [Fact]
    public async Task ErrorResponsesAreHardenedToo()
    {
        var r = await _client.GetAsync("/api/v1/products/no-such-product");
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
        Assert.Equal("nosniff", r.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("application/problem+json", r.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task TokensAndPrivateDataAreNeverCacheable()
    {
        var reg = await _client.PostJsonAsync("/api/v1/auth/register", new { fullName = "Cache Test", email = $"c{Guid.NewGuid():N}@example.com", password = "Passw0rdX" });
        Assert.Equal("no-store", reg.Headers.CacheControl?.ToString());
        var token = (await reg.ReadAsync<AuthResponse>()).AccessToken;
        foreach (var url in new[] { "/api/v1/auth/me", "/api/v1/cart", "/api/v1/orders", "/api/v1/addresses" })
        {
            var r = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Get, url).WithBearer(token));
            Assert.Equal("no-store", r.Headers.CacheControl?.ToString());
        }
    }

    [Fact]
    public async Task ResponsesAreCompressed_ExceptTokenBearingAuthEndpoints()
    {
        var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/products?pageSize=60");
        req.Headers.AcceptEncoding.ParseAdd("br, gzip");
        var catalog = await _client.SendAsync(req);
        Assert.Contains(catalog.Content.Headers.ContentEncoding, e => e is "br" or "gzip");

        var login = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login") { Content = JsonContent.Create(new { email = "nobody@example.com", password = "x" }) };
        login.Headers.AcceptEncoding.ParseAdd("br, gzip");
        var auth = await _client.SendAsync(login);
        Assert.Empty(auth.Content.Headers.ContentEncoding);
    }

    // ------------------------------------------------------------------ A02 / A07: tokens
    private static string Token(Action<JwtSecurityToken>? _ = null, string alg = SecurityAlgorithms.HmacSha256, string key = ApiFactory.TestJwtKey,
        string issuer = "TechBazarBD", string audience = "TechBazarBD.Clients", DateTime? expires = null, string role = "Admin")
    {
        var creds = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), alg);
        var claims = new[] { new Claim("sub", Guid.NewGuid().ToString()), new Claim("role", role), new Claim("name", "x") };
        var jwt = new JwtSecurityToken(issuer, audience, claims, DateTime.UtcNow.AddMinutes(-5), expires ?? DateTime.UtcNow.AddMinutes(10), creds);
        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }

    private async Task<HttpStatusCode> AdminCall(string token) =>
        (await _client.SendAsync(new HttpRequestMessage(HttpMethod.Get, "/api/v1/admin/dashboard").WithBearer(token))).StatusCode;

    [Fact]
    public async Task ASelfSignedAdminTokenWithTheRightKeyIsAcceptedButEveryTamperedVariantIsRejected()
    {
        // control: proves the helper builds tokens the API would accept, so the rejections below are meaningful
        Assert.Equal(HttpStatusCode.OK, await AdminCall(Token()));

        Assert.Equal(HttpStatusCode.Unauthorized, await AdminCall(Token(key: "another-signing-key-0123456789-abcdefghij-xyz")));   // wrong key
        Assert.Equal(HttpStatusCode.Unauthorized, await AdminCall(Token(issuer: "evil")));
        Assert.Equal(HttpStatusCode.Unauthorized, await AdminCall(Token(audience: "evil")));
        Assert.Equal(HttpStatusCode.Unauthorized, await AdminCall(Token(expires: DateTime.UtcNow.AddMinutes(-2))));                 // expired (beyond the 30 s skew)
    }

    [Fact]
    public async Task TheSigningAlgorithmIsPinnedToHs256()
    {
        // With a key long enough for HS512, a correctly signed HS512 token is rejected only because the algorithm is pinned.
        const string longKey = "a-much-longer-signing-key-for-the-algorithm-pinning-test-0123456789-abcdefghijklmnopqrstuvwxyz";
        using var f = ApiFactory.WithConfig(("Jwt:Key", longKey));
        using var c = f.CreateClient();
        async Task<HttpStatusCode> Call(string t) => (await c.SendAsync(new HttpRequestMessage(HttpMethod.Get, "/api/v1/admin/dashboard").WithBearer(t))).StatusCode;
        Assert.Equal(HttpStatusCode.OK, await Call(Token(key: longKey)));                                     // HS256: accepted
        Assert.Equal(HttpStatusCode.Unauthorized, await Call(Token(key: longKey, alg: SecurityAlgorithms.HmacSha512)));   // HS512 with the right key: refused
        Assert.Equal(HttpStatusCode.Unauthorized, await Call(Token(key: longKey, alg: SecurityAlgorithms.HmacSha384)));
    }

    [Fact]
    public async Task AnUnsignedAlgNoneTokenIsRejected()
    {
        string B64(string s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var exp = DateTimeOffset.UtcNow.AddMinutes(10).ToUnixTimeSeconds();
        var none = $"{B64("{\"alg\":\"none\",\"typ\":\"JWT\"}")}.{B64($"{{\"sub\":\"{Guid.NewGuid()}\",\"role\":\"Admin\",\"iss\":\"TechBazarBD\",\"aud\":\"TechBazarBD.Clients\",\"exp\":{exp}}}")}.";
        Assert.Equal(HttpStatusCode.Unauthorized, await AdminCall(none));
        Assert.Equal(HttpStatusCode.Unauthorized, await AdminCall("not-a-jwt"));
    }

    [Fact]
    public async Task ACustomerRoleClaimInTheTokenNeverGrantsAdmin() =>
        Assert.Equal(HttpStatusCode.Forbidden, await AdminCall(Token(role: "Customer")));

    // ------------------------------------------------------------------ A07: refresh-token handling
    private async Task<AuthResponse> RegisterAsync()
    {
        var r = await _client.PostJsonAsync("/api/v1/auth/register", new { fullName = "Tok Test", email = $"t{Guid.NewGuid():N}@example.com", password = "Passw0rdX" });
        return await r.ReadAsync<AuthResponse>();
    }

    private Task<HttpResponseMessage> Refresh(string token) => _client.PostJsonAsync("/api/v1/auth/refresh", new { refreshToken = token });

    [Fact]
    public async Task ReplayingARotatedRefreshTokenRevokesTheWholeSession()
    {
        var first = await RegisterAsync();
        var second = await (await Refresh(first.RefreshToken!)).ReadAsync<AuthResponse>();
        Assert.Equal(HttpStatusCode.Unauthorized, (await Refresh(first.RefreshToken!)).StatusCode);    // theft signal
        Assert.Equal(HttpStatusCode.Unauthorized, (await Refresh(second.RefreshToken!)).StatusCode);   // descendant is dead too
    }

    [Fact]
    public async Task LogoutEverywhereRevokesEveryDevice()
    {
        var email = $"all{Guid.NewGuid():N}@example.com";
        var phone = await (await _client.PostJsonAsync("/api/v1/auth/register", new { fullName = "Multi Device", email, password = "Passw0rdX" })).ReadAsync<AuthResponse>();
        var laptop = await (await _client.PostJsonAsync("/api/v1/auth/login", new { email, password = "Passw0rdX" })).ReadAsync<AuthResponse>();

        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.PostAsync("/api/v1/auth/logout-all", null)).StatusCode);   // needs a session
        var all = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/logout-all").WithBearer(laptop.AccessToken));
        Assert.Equal(HttpStatusCode.NoContent, all.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Refresh(phone.RefreshToken!)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Refresh(laptop.RefreshToken!)).StatusCode);
    }

    [Fact]
    public async Task LoginDoesNotRevealWhetherAnAccountExists()
    {
        var known = (await RegisterAsync()).User.Email;
        var wrongPassword = await _client.PostJsonAsync("/api/v1/auth/login", new { email = known, password = "WrongPassw0rd" });
        var unknown = await _client.PostJsonAsync("/api/v1/auth/login", new { email = $"ghost{Guid.NewGuid():N}@example.com", password = "WrongPassw0rd" });
        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
        using var a = await wrongPassword.ProblemAsync();
        using var b = await unknown.ProblemAsync();
        Assert.Equal(a.RootElement.GetProperty("detail").GetString(), b.RootElement.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task RepeatedFailedLoginsLockTheAccount_EvenForTheCorrectPassword()
    {
        var email = (await RegisterAsync()).User.Email;
        for (var i = 0; i < 5; i++) await _client.PostJsonAsync("/api/v1/auth/login", new { email, password = "WrongPassw0rd" });
        var r = await _client.PostJsonAsync("/api/v1/auth/login", new { email, password = "Passw0rdX" });
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    // ---- HttpOnly cookie mode (web storefront)
    private readonly HttpClient _manual = factory.CreateClient(new() { HandleCookies = false });

    private static (string Value, string Raw) SetCookie(HttpResponseMessage r)
    {
        var raw = r.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("tb_rt="));
        return (raw.Split(';')[0]["tb_rt=".Length..], raw);
    }

    private static HttpRequestMessage CookieReq(string url, string? cookie, bool header = true, object? body = null)
    {
        var m = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body ?? new { }) };
        if (header) m.Headers.Add("X-Refresh-Mode", "cookie");
        if (cookie is not null) m.Headers.Add("Cookie", $"tb_rt={cookie}");
        return m;
    }

    [Fact]
    public async Task CookieMode_KeepsTheRefreshTokenOutOfJavaScriptsReach()
    {
        var reg = await _manual.SendAsync(CookieReq("/api/v1/auth/register", null, body: new { fullName = "Cookie Test", email = $"k{Guid.NewGuid():N}@example.com", password = "Passw0rdX" }));
        Assert.Equal(HttpStatusCode.Created, reg.StatusCode);
        var (_, raw) = SetCookie(reg);
        Assert.Contains("httponly", raw, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", raw, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/api/v1/auth", raw, StringComparison.OrdinalIgnoreCase);
        var body = await reg.ReadAsync<AuthResponse>();
        Assert.Null(body.RefreshToken);                       // never in the JSON a script can read
        Assert.False(string.IsNullOrEmpty(body.AccessToken));
    }

    [Fact]
    public async Task CookieMode_RefreshRotatesTheCookie_AndOldCookiesAreDead()
    {
        var reg = await _manual.SendAsync(CookieReq("/api/v1/auth/register", null, body: new { fullName = "Cookie Test", email = $"k{Guid.NewGuid():N}@example.com", password = "Passw0rdX" }));
        var (c1, _) = SetCookie(reg);

        var r1 = await _manual.SendAsync(CookieReq("/api/v1/auth/refresh", c1));
        Assert.Equal(HttpStatusCode.OK, r1.StatusCode);
        var (c2, _) = SetCookie(r1);
        Assert.NotEqual(c1, c2);
        Assert.Null((await r1.ReadAsync<AuthResponse>()).RefreshToken);

        var replay = await _manual.SendAsync(CookieReq("/api/v1/auth/refresh", c1));
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
        Assert.Contains(replay.Headers.GetValues("Set-Cookie"), c => c.StartsWith("tb_rt=;", StringComparison.Ordinal) || c.Contains("expires=Thu, 01 Jan 1970"));  // dead cookie is cleared
        Assert.Equal(HttpStatusCode.Unauthorized, (await _manual.SendAsync(CookieReq("/api/v1/auth/refresh", c2))).StatusCode);   // reuse killed the family
    }

    [Fact]
    public async Task CookieIsIgnoredWithoutTheCustomHeader_SoAForgedCrossSiteRequestCannotUseIt()
    {
        var reg = await _manual.SendAsync(CookieReq("/api/v1/auth/register", null, body: new { fullName = "Csrf Test", email = $"x{Guid.NewGuid():N}@example.com", password = "Passw0rdX" }));
        var (cookie, _) = SetCookie(reg);
        var forged = await _manual.SendAsync(CookieReq("/api/v1/auth/refresh", cookie, header: false));
        Assert.Equal(HttpStatusCode.Unauthorized, forged.StatusCode);
        // and the legitimate session is untouched
        Assert.Equal(HttpStatusCode.OK, (await _manual.SendAsync(CookieReq("/api/v1/auth/refresh", cookie))).StatusCode);
    }

    [Fact]
    public async Task CookieMode_LogoutRevokesServerSideAndClearsTheCookie()
    {
        var reg = await _manual.SendAsync(CookieReq("/api/v1/auth/register", null, body: new { fullName = "Bye Test", email = $"b{Guid.NewGuid():N}@example.com", password = "Passw0rdX" }));
        var (cookie, _) = SetCookie(reg);
        var out1 = await _manual.SendAsync(CookieReq("/api/v1/auth/logout", cookie));
        Assert.Equal(HttpStatusCode.NoContent, out1.StatusCode);
        Assert.Contains(out1.Headers.GetValues("Set-Cookie"), c => c.StartsWith("tb_rt=", StringComparison.Ordinal) && c.Contains("expires=Thu, 01 Jan 1970"));
        Assert.Equal(HttpStatusCode.Unauthorized, (await _manual.SendAsync(CookieReq("/api/v1/auth/refresh", cookie))).StatusCode);   // revoked on the server, not just deleted in the browser
    }

    [Fact]
    public async Task TheCookieIsMarkedSecureWhenConfigured()
    {
        using var f = ApiFactory.WithConfig(("Auth:RefreshCookie:Secure", "Always"));
        using var c = f.CreateClient(new() { HandleCookies = false });
        var r = await c.SendAsync(CookieReq("/api/v1/auth/register", null, body: new { fullName = "Sec Cookie", email = $"s{Guid.NewGuid():N}@example.com", password = "Passw0rdX" }));
        Assert.Contains("secure", SetCookie(r).Raw, StringComparison.OrdinalIgnoreCase);
    }

    // ------------------------------------------------------------------ abuse protection
    [Fact]
    public async Task TheGlobalRateLimitCapsAnonymousApiCallsPerClient_ButNotStaticFiles()
    {
        using var f = ApiFactory.WithConfig(("RateLimiting:Global:PermitLimit", "5"));
        using var c = f.CreateClient();
        var codes = new List<HttpStatusCode>();
        for (var i = 0; i < 8; i++) codes.Add((await c.GetAsync("/api/v1/checkout/options")).StatusCode);
        Assert.Equal(5, codes.Count(x => x == HttpStatusCode.OK));
        Assert.Equal(3, codes.Count(x => x == HttpStatusCode.TooManyRequests));
        var limited = await c.GetAsync("/api/v1/checkout/options");
        Assert.True(limited.Headers.Contains("Retry-After"));
        using var problem = await limited.ProblemAsync();
        Assert.Equal(429, problem.RootElement.GetProperty("status").GetInt32());
    }

    [Fact]
    public void ForwardedHeadersTrustOnlyConfiguredProxies()
    {
        using var f = ApiFactory.WithConfig(("ForwardedHeaders:KnownProxies:0", "10.1.2.3"), ("ForwardedHeaders:KnownNetworks:0", "172.16.0.0/12"));
        var o = f.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<Microsoft.AspNetCore.Builder.ForwardedHeadersOptions>>().Value;
        Assert.Contains(System.Net.IPAddress.Parse("10.1.2.3"), o.KnownProxies);
        Assert.Single(o.KnownNetworks);
        Assert.Equal(Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto, o.ForwardedHeaders);

        // the default (nothing configured) trusts nobody except loopback, i.e. spoofed X-Forwarded-For from the internet is ignored
        var def = factory.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<Microsoft.AspNetCore.Builder.ForwardedHeadersOptions>>().Value;
        Assert.Empty(def.KnownNetworks);
        Assert.Empty(def.KnownProxies);
    }

    // ------------------------------------------------------------------ A03: injection probes
    public static IEnumerable<object[]> HostilePayloads() =>
    [
        ["'; DROP TABLE Products;--"], ["' OR '1'='1"], ["\" OR \"\"=\""], ["1; WAITFOR DELAY '0:0:5'--"], ["%"], ["_"], ["[a-z]"], ["\\"],
        ["<script>alert(1)</script>"], ["${jndi:ldap://x}"], ["{{7*7}}"], ["../../etc/passwd"], [new string('A', 5000)],
    ];

    [Theory, MemberData(nameof(HostilePayloads))]
    public async Task HostileInputNeverCausesServerErrors_OrChangesData(string payload)
    {
        var before = (await (await _client.GetAsync("/api/v1/products?pageSize=1")).ReadAsync<PagedResult<ProductListItemDto>>()).TotalCount;
        var e = Uri.EscapeDataString(payload);
        foreach (var url in new[]
                 {
                     $"/api/v1/products?q={e}", $"/api/v1/products?category={e}", $"/api/v1/products?brand={e}", $"/api/v1/products?spec={e}", $"/api/v1/products?sort={e}",
                     $"/api/v1/search?q={e}", $"/api/v1/search/autocomplete?q={e}", $"/api/v1/products/{e}", $"/api/v1/categories/{e}", $"/api/v1/pc-builder/builds/{e}",
                 })
        {
            var r = await _client.GetAsync(url);
            Assert.True((int)r.StatusCode < 500, $"{url} -> {(int)r.StatusCode}");
        }
        var after = (await (await _client.GetAsync("/api/v1/products?pageSize=1")).ReadAsync<PagedResult<ProductListItemDto>>()).TotalCount;
        Assert.Equal(before, after);
    }

    [Fact]
    public async Task LikeWildcardsInSearchAreTreatedAsLiteralText()
    {
        // "%" / "_" must not match everything
        var all = (await (await _client.GetAsync("/api/v1/products?pageSize=1")).ReadAsync<PagedResult<ProductListItemDto>>()).TotalCount;
        foreach (var q in new[] { "%%", "__", "%_%" })
        {
            var r = await (await _client.GetAsync($"/api/v1/search?q={Uri.EscapeDataString(q)}")).ReadAsync<PagedResult<ProductListItemDto>>();
            Assert.True(r.TotalCount < all, $"'{q}' matched {r.TotalCount} of {all}");
        }
    }

    [Fact]
    public async Task MalformedBodiesAreClientErrorsNotCrashes()
    {
        foreach (var body in new[] { "{", "[]", "null", "\"str\"", "{\"items\":\"x\"}", "{\"items\":[{\"slot\":999,\"productId\":-1}]}", new string('x', 4000) })
        {
            var r = await _client.PostAsync("/api/v1/pc-builder/evaluate", new StringContent(body, Encoding.UTF8, "application/json"));
            Assert.True((int)r.StatusCode is >= 400 and < 500, $"{body[..Math.Min(20, body.Length)]} -> {(int)r.StatusCode}");
        }
        var wrongType = await _client.PostAsync("/api/v1/auth/login", new StringContent("email=a&password=b", Encoding.UTF8, "application/x-www-form-urlencoded"));
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, wrongType.StatusCode);
    }

    [Fact]
    public async Task OversizedCollectionsAreRejected()
    {
        var items = Enumerable.Range(1, 500).Select(i => new { productId = i, quantity = 1 }).ToArray();
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostJsonAsync("/api/v1/cart/preview", new { items })).StatusCode);
        var many = string.Join("&", Enumerable.Range(0, 60).Select(i => $"spec=Key{i}:V"));
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync($"/api/v1/products?{many}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync("/api/v1/products?pageSize=100000")).StatusCode);
    }

    // ------------------------------------------------------------------ A04: IDOR on user-owned resources
    [Fact]
    public async Task UsersCannotReadOrChangeEachOthersAddressesOrOrders()
    {
        var a = await RegisterAsync();
        var b = await RegisterAsync();
        HttpRequestMessage As(string token, HttpMethod m, string url, object? body = null) =>
            new HttpRequestMessage(m, url) { Content = body is null ? null : JsonContent.Create(body, options: Http.Json) }.WithBearer(token);

        var created = await _client.SendAsync(As(a.AccessToken, HttpMethod.Post, "/api/v1/addresses",
            new { label = "Home", fullName = "A", phone = "01712345678", division = "Dhaka", district = "Dhaka", addressLine = "Road 1", isDefault = true }));
        var id = (await created.ReadAsync<AddressDto>()).Id;

        Assert.Empty(await (await _client.SendAsync(As(b.AccessToken, HttpMethod.Get, "/api/v1/addresses"))).ReadAsync<List<AddressDto>>());
        Assert.Equal(HttpStatusCode.NotFound, (await _client.SendAsync(As(b.AccessToken, HttpMethod.Delete, $"/api/v1/addresses/{id}"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.SendAsync(As(b.AccessToken, HttpMethod.Put, $"/api/v1/addresses/{id}/default"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.SendAsync(As(b.AccessToken, HttpMethod.Get, "/api/v1/orders/TB-000000-AAAAA"))).StatusCode);

        // ordering with someone else's address id must fail as well
        var product = (await (await _client.GetAsync("/api/v1/products?inStock=true&pageSize=1")).ReadAsync<PagedResult<ProductListItemDto>>()).Items.First();
        await _client.SendAsync(As(b.AccessToken, HttpMethod.Put, $"/api/v1/cart/items/{product.Id}", new { quantity = 1 }));
        var steal = await _client.SendAsync(As(b.AccessToken, HttpMethod.Post, "/api/v1/orders",
            new { shippingMethod = "HomeDeliveryInsideDhaka", addressId = id, paymentMethod = "CashOnDelivery" }));
        Assert.Equal(HttpStatusCode.NotFound, steal.StatusCode);
    }
}
