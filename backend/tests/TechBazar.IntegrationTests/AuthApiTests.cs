using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using TechBazar.Application.Auth;
using TechBazar.IntegrationTests.Support;

namespace TechBazar.IntegrationTests;

public class AuthApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();
    private const string Password = "Passw0rdX";

    private static string NewEmail() => $"user{Guid.NewGuid():N}@example.com";

    private async Task<AuthResponse> RegisterAsync(string? email = null)
    {
        var r = await _client.PostJsonAsync("/api/v1/auth/register", new { fullName = "Rahim Uddin", email = email ?? NewEmail(), phone = "01712345678", password = Password });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        return await r.ReadAsync<AuthResponse>();
    }

    private Task<HttpResponseMessage> RefreshAsync(string token) => _client.PostJsonAsync("/api/v1/auth/refresh", new { refreshToken = token });

    [Fact]
    public async Task Register_ReturnsTokensAndCustomerRole()
    {
        var auth = await RegisterAsync();

        Assert.False(string.IsNullOrEmpty(auth.AccessToken));
        Assert.False(string.IsNullOrEmpty(auth.RefreshToken));
        Assert.Equal(["Customer"], auth.User.Roles);
        Assert.True(auth.AccessTokenExpiresAt > DateTime.UtcNow);
        Assert.True(auth.RefreshTokenExpiresAt > auth.AccessTokenExpiresAt);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(auth.AccessToken);
        Assert.Contains(jwt.Claims, c => c is { Type: "role", Value: "Customer" });
        Assert.Equal("TechBazarBD", jwt.Issuer);
    }

    [Fact]
    public async Task Register_DuplicateEmail_Returns409()
    {
        var email = NewEmail();
        await RegisterAsync(email);
        var r = await _client.PostJsonAsync("/api/v1/auth/register", new { fullName = "X Y", email = email.ToUpperInvariant(), password = Password });
        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        using var _ = await r.ProblemAsync();
    }

    [Fact]
    public async Task Register_WeakPasswordAndBadInput_Returns400WithFieldErrors()
    {
        var r = await _client.PostJsonAsync("/api/v1/auth/register", new { fullName = "", email = "nope", phone = "123", password = "weak" });
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        using var p = await r.ProblemAsync();
        var errors = p.RootElement.GetProperty("errors");
        foreach (var f in new[] { "fullName", "email", "phone", "password" }) Assert.True(errors.TryGetProperty(f, out _), f);
    }

    [Fact]
    public async Task Login_Succeeds_ThenFailsWithGeneric401ForWrongPasswordOrUnknownUser()
    {
        var email = NewEmail();
        await RegisterAsync(email);

        var ok = await _client.PostJsonAsync("/api/v1/auth/login", new { email, password = Password });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);

        var wrong = await _client.PostJsonAsync("/api/v1/auth/login", new { email, password = "WrongPassw0rd" });
        var unknown = await _client.PostJsonAsync("/api/v1/auth/login", new { email = NewEmail(), password = Password });
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
        using var a = await wrong.ProblemAsync();
        using var b = await unknown.ProblemAsync();
        Assert.Equal(a.RootElement.GetProperty("detail").GetString(), b.RootElement.GetProperty("detail").GetString()); // no user enumeration
    }

    [Fact]
    public async Task Login_LocksAccountAfterRepeatedFailures()
    {
        var email = NewEmail();
        await RegisterAsync(email);
        for (var i = 0; i < 5; i++)
            await _client.PostJsonAsync("/api/v1/auth/login", new { email, password = "WrongPassw0rd" });

        var r = await _client.PostJsonAsync("/api/v1/auth/login", new { email, password = Password });
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
        using var p = await r.ProblemAsync();
        Assert.Contains("locked", p.RootElement.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Me_RequiresBearerToken_AndReturnsProfile()
    {
        var noAuth = await _client.GetAsync("/api/v1/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, noAuth.StatusCode);
        using var _ = await noAuth.ProblemAsync();

        var auth = await RegisterAsync();
        var me = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/me").WithBearer(auth.AccessToken));
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        var user = await me.ReadAsync<UserDto>();
        Assert.Equal(auth.User.Email, user.Email);
        Assert.Equal("Rahim Uddin", user.FullName);
    }

    [Fact]
    public async Task Me_RejectsTamperedToken()
    {
        var auth = await RegisterAsync();
        var tampered = auth.AccessToken[..^3] + (auth.AccessToken.EndsWith("AAA") ? "BBB" : "AAA");
        var r = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/me").WithBearer(tampered));
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    [Fact]
    public async Task Refresh_RotatesToken_AndOldTokenCannotBeReused()
    {
        var first = await RegisterAsync();

        var r1 = await RefreshAsync(first.RefreshToken);
        Assert.Equal(HttpStatusCode.OK, r1.StatusCode);
        var second = await r1.ReadAsync<AuthResponse>();
        Assert.NotEqual(first.RefreshToken, second.RefreshToken);
        Assert.NotEqual(first.AccessToken, second.AccessToken);

        // replaying the rotated token is rejected...
        var replay = await RefreshAsync(first.RefreshToken);
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);

        // ...and treated as theft: the descendant token is revoked as well
        var descendant = await RefreshAsync(second.RefreshToken);
        Assert.Equal(HttpStatusCode.Unauthorized, descendant.StatusCode);
    }

    [Fact]
    public async Task Refresh_WithGarbageToken_Returns401() =>
        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync("not-a-real-token")).StatusCode);

    [Fact]
    public async Task Refresh_WithEmptyToken_Returns400() =>
        Assert.Equal(HttpStatusCode.BadRequest, (await RefreshAsync("")).StatusCode);

    [Fact]
    public async Task Logout_RevokesRefreshToken_AndIsIdempotent()
    {
        var auth = await RegisterAsync();

        HttpRequestMessage Logout() => new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/logout")
            { Content = JsonContent.Create(new { refreshToken = auth.RefreshToken }) }.WithBearer(auth.AccessToken);

        Assert.Equal(HttpStatusCode.NoContent, (await _client.SendAsync(Logout())).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await _client.SendAsync(Logout())).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(auth.RefreshToken)).StatusCode);
    }

    [Fact]
    public async Task Logout_WithoutAccessToken_Returns401() =>
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.PostJsonAsync("/api/v1/auth/logout", new { refreshToken = "x" })).StatusCode);

    [Fact]
    public async Task SeededAdmin_CanLogin_AndHasAdminRole()
    {
        var r = await _client.PostJsonAsync("/api/v1/auth/login", new { email = ApiFactory.AdminEmail, password = ApiFactory.AdminPassword });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var auth = await r.ReadAsync<AuthResponse>();
        Assert.Equal(["Admin"], auth.User.Roles);
    }
}

public class AuthRateLimitTests
{
    [Fact]
    public async Task AuthEndpoints_AreRateLimitedPerClient_With429Problem()
    {
        using var factory = ApiFactory.WithAuthLimit(3);
        var client = factory.CreateClient();

        for (var i = 0; i < 3; i++)
            Assert.Equal(HttpStatusCode.Unauthorized,
                (await client.PostJsonAsync("/api/v1/auth/login", new { email = "a@b.com", password = "x" })).StatusCode);

        var limited = await client.PostJsonAsync("/api/v1/auth/login", new { email = "a@b.com", password = "x" });
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.True(limited.Headers.Contains("Retry-After"));
        using var _ = await limited.ProblemAsync();

        // catalog endpoints are not subject to the auth limiter
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/brands")).StatusCode);
    }
}
