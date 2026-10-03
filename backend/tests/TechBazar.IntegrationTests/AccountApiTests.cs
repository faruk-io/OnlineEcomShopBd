using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TechBazar.Application.Auth;
using TechBazar.Application.Catalog.Dtos;
using TechBazar.Application.Common;
using TechBazar.Application.Orders;
using TechBazar.Infrastructure.Identity;
using TechBazar.IntegrationTests.Support;

namespace TechBazar.IntegrationTests;

/// <summary>Password reset and email verification over real HTTP (the full pipeline: rate limits, headers, cookies, problem details).</summary>
public partial class AccountApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Password = "Passw0rdX";
    private const string NewPassword = "NewPassw0rdY";
    private readonly HttpClient _client = factory.CreateClient(new() { HandleCookies = false });

    [GeneratedRegex(@"(https?://\S+?)#token=([A-Za-z0-9_-]+)")]
    private static partial Regex Link();

    private static string NewEmail() => $"acct{Guid.NewGuid():N}@example.com";

    private async Task<AuthResponse> RegisterAsync(string? email = null, HttpClient? client = null) =>
        await (await (client ?? _client).PostJsonAsync("/api/v1/auth/register", new { fullName = "Account Test", email = email ?? NewEmail(), password = Password })).ReadAsync<AuthResponse>();

    private static string TokenFor(ApiFactory f, string email, string subjectContains) =>
        Link().Match(f.Emails.Sent.Last(m => m.To == email && m.Subject.Contains(subjectContains)).TextBody).Groups[2].Value;

    private string TokenFor(string email, string subjectContains) => TokenFor(factory, email, subjectContains);

    /// <summary>"Forgot password" mail is sent by a background worker; wait for it.</summary>
    private Task Drain() => factory.Services.GetRequiredService<AccountJobQueue>().DrainAsync();

    private async Task ForgotAsync(string email)
    {
        Assert.Equal(HttpStatusCode.Accepted, (await Post("/api/v1/auth/forgot-password", new { email })).StatusCode);
        await Drain();
    }

    private Task<HttpResponseMessage> Post(string url, object body, HttpClient? c = null) => (c ?? _client).PostJsonAsync(url, body);

    private static HttpRequestMessage Bearer(HttpMethod m, string url, string token) => new HttpRequestMessage(m, url).WithBearer(token);

    // ------------------------------------------------------------------ verification
    [Fact]
    public async Task Registering_SendsAVerificationEmail_AndTheAccountStartsUnverified()
    {
        var email = NewEmail();
        var auth = await RegisterAsync(email);
        Assert.False(auth.User.EmailConfirmed);
        var mail = Assert.Single(factory.Emails.Sent, m => m.To == email);
        Assert.Contains("Verify your email", mail.Subject);
        Assert.StartsWith("https://shop.test/verify-email#token=", Link().Match(mail.TextBody).Value);
    }

    [Fact]
    public async Task VerifyingTheEmail_Flips_TheFlagShownByMe_AndTheLinkIsSingleUse()
    {
        var email = NewEmail();
        var auth = await RegisterAsync(email);
        var token = TokenFor(email, "Verify");

        var ok = await Post("/api/v1/auth/verify-email", new { token });
        Assert.Equal(HttpStatusCode.NoContent, ok.StatusCode);

        var me = await (await _client.SendAsync(Bearer(HttpMethod.Get, "/api/v1/auth/me", auth.AccessToken))).ReadAsync<UserDto>();
        Assert.True(me.EmailConfirmed);

        var again = await Post("/api/v1/auth/verify-email", new { token });
        Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);
        using var problem = await again.ProblemAsync();
        Assert.Equal("Invalid or expired link.", problem.RootElement.GetProperty("title").GetString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("nope")]
    [InlineData("A1b2C3d4E5f6G7h8I9j0K1l2M3n4O5p6Q7r8S9t0U1v")]
    public async Task ABadVerificationToken_IsRejected(string token)
    {
        var r = await Post("/api/v1/auth/verify-email", new { token });
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        (await r.ProblemAsync()).Dispose();
    }

    [Fact]
    public async Task VerificationMustBeAPost_SoLinkPrefetchersCannotSpendTheToken()
    {
        var email = NewEmail();
        await RegisterAsync(email);
        var token = TokenFor(email, "Verify");
        var get = await _client.GetAsync($"/api/v1/auth/verify-email?token={token}");
        Assert.True(get.StatusCode is HttpStatusCode.MethodNotAllowed or HttpStatusCode.NotFound or HttpStatusCode.Unauthorized, get.StatusCode.ToString());
        Assert.Equal(HttpStatusCode.NoContent, (await Post("/api/v1/auth/verify-email", new { token })).StatusCode);   // untouched by the GET
    }

    [Fact]
    public async Task Resend_NeedsASession_SendsAFreshLink_AndTheOldOneStopsWorking()
    {
        var email = NewEmail();
        var auth = await RegisterAsync(email);
        var old = TokenFor(email, "Verify");

        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.PostAsync("/api/v1/auth/resend-verification", null)).StatusCode);
        var resend = await _client.SendAsync(Bearer(HttpMethod.Post, "/api/v1/auth/resend-verification", auth.AccessToken));
        Assert.Equal(HttpStatusCode.Accepted, resend.StatusCode);
        Assert.Equal(2, factory.Emails.Sent.Count(m => m.To == email && m.Subject.Contains("Verify")));

        Assert.Equal(HttpStatusCode.BadRequest, (await Post("/api/v1/auth/verify-email", new { token = old })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Post("/api/v1/auth/verify-email", new { token = TokenFor(email, "Verify") })).StatusCode);

        // verified now: the same neutral answer, but no further email
        var before = factory.Emails.Sent.Count(m => m.To == email);
        Assert.Equal(HttpStatusCode.Accepted, (await _client.SendAsync(Bearer(HttpMethod.Post, "/api/v1/auth/resend-verification", auth.AccessToken))).StatusCode);
        Assert.Equal(before, factory.Emails.Sent.Count(m => m.To == email));
    }

    [Fact]
    public async Task TheSeededAdminIsAlreadyVerified()
    {
        var admin = await (await Post("/api/v1/auth/login", new { email = ApiFactory.AdminEmail, password = ApiFactory.AdminPassword })).ReadAsync<AuthResponse>();
        Assert.True(admin.User.EmailConfirmed);
    }

    // ------------------------------------------------------------------ forgot password
    [Fact]
    public async Task ForgotPassword_AnswersIdenticallyForKnownAndUnknownAddresses()
    {
        var known = NewEmail();
        await RegisterAsync(known);
        var ghost = NewEmail();

        var a = await Post("/api/v1/auth/forgot-password", new { email = known });
        var b = await Post("/api/v1/auth/forgot-password", new { email = ghost });
        Assert.Equal((HttpStatusCode.Accepted, HttpStatusCode.Accepted), (a.StatusCode, b.StatusCode));
        Assert.Equal(await a.Content.ReadAsStringAsync(), await b.Content.ReadAsStringAsync());
        Assert.Equal(a.Content.Headers.ContentType?.ToString(), b.Content.Headers.ContentType?.ToString());
        Assert.Equal("no-store", a.Headers.CacheControl?.ToString());

        await Drain();
        Assert.Contains(factory.Emails.Sent, m => m.To == known && m.Subject.Contains("Reset"));
        Assert.DoesNotContain(factory.Emails.Sent, m => m.To == ghost);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-email")]
    [InlineData("two@@example.com")]
    public async Task ForgotPassword_ValidatesTheAddressFormat(string email)
    {
        var r = await Post("/api/v1/auth/forgot-password", new { email });
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task ForgotPassword_IsRateLimitedPerClient()
    {
        using var f = ApiFactory.WithConfig(("RateLimiting:Recovery:PermitLimit", "2"));
        using var c = f.CreateClient();
        var codes = new List<HttpStatusCode>();
        for (var i = 0; i < 4; i++) codes.Add((await c.PostJsonAsync("/api/v1/auth/forgot-password", new { email = NewEmail() })).StatusCode);
        Assert.Equal([HttpStatusCode.Accepted, HttpStatusCode.Accepted, HttpStatusCode.TooManyRequests, HttpStatusCode.TooManyRequests], codes);
    }

    [Fact]
    public async Task ResetLinks_AreBuiltFromConfiguration_NotFromTheHostHeader()
    {
        var email = NewEmail();
        await RegisterAsync(email);
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/forgot-password") { Content = JsonContent.Create(new { email }) };
        req.Headers.Host = "evil.example";
        req.Headers.Add("X-Forwarded-Host", "evil.example");
        req.Headers.Add("Origin", "https://evil.example");
        req.Headers.Add("Referer", "https://evil.example/phish");
        Assert.Equal(HttpStatusCode.Accepted, (await _client.SendAsync(req)).StatusCode);
        await Drain();

        var mail = factory.Emails.Sent.Last(m => m.To == email && m.Subject.Contains("Reset"));
        Assert.StartsWith("https://shop.test/reset-password#token=", Link().Match(mail.TextBody).Value);
        Assert.DoesNotContain("evil.example", mail.TextBody + mail.HtmlBody);
    }

    // ------------------------------------------------------------------ reset password
    [Fact]
    public async Task ResetPassword_FullJourney_EndsEverySession_AndAllowsTheNewPasswordOnly()
    {
        var email = NewEmail();
        var phone = await RegisterAsync(email);
        var laptop = await (await Post("/api/v1/auth/login", new { email, password = Password })).ReadAsync<AuthResponse>();
        await ForgotAsync(email);
        var token = TokenFor(email, "Reset");

        var reset = await Post("/api/v1/auth/reset-password", new { token, newPassword = NewPassword });
        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);
        Assert.Equal("no-store", reset.Headers.CacheControl?.ToString());
        Assert.Contains(reset.Headers.GetValues("Set-Cookie"), c => c.StartsWith("tb_rt=", StringComparison.Ordinal));       // the browser's cookie is cleared too

        // every refresh token died
        Assert.Equal(HttpStatusCode.Unauthorized, (await Post("/api/v1/auth/refresh", new { refreshToken = phone.RefreshToken })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Post("/api/v1/auth/refresh", new { refreshToken = laptop.RefreshToken })).StatusCode);
        // old password out, new password in
        Assert.Equal(HttpStatusCode.Unauthorized, (await Post("/api/v1/auth/login", new { email, password = Password })).StatusCode);
        var login = await Post("/api/v1/auth/login", new { email, password = NewPassword });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.True((await login.ReadAsync<AuthResponse>()).User.EmailConfirmed);                                            // mailbox control proven

        Assert.Contains(factory.Emails.Sent, m => m.To == email && m.Subject.Contains("password was changed"));
        // the link is spent
        Assert.Equal(HttpStatusCode.BadRequest, (await Post("/api/v1/auth/reset-password", new { token, newPassword = "Third1Password" })).StatusCode);
    }

    [Fact]
    public async Task ResetPassword_WithAWeakPassword_ExplainsTheField_AndKeepsTheLinkUsable()
    {
        var email = NewEmail();
        await RegisterAsync(email);
        await ForgotAsync(email);
        var token = TokenFor(email, "Reset");

        var weak = await Post("/api/v1/auth/reset-password", new { token, newPassword = "weak" });
        Assert.Equal(HttpStatusCode.BadRequest, weak.StatusCode);
        using var problem = await weak.ProblemAsync();
        Assert.True(problem.RootElement.GetProperty("errors").TryGetProperty("newPassword", out var errs) && errs.GetArrayLength() > 0);

        Assert.Equal(HttpStatusCode.NoContent, (await Post("/api/v1/auth/reset-password", new { token, newPassword = NewPassword })).StatusCode);
    }

    [Fact]
    public async Task ResetPassword_RejectsUnknownTokens_WithTheSameGenericProblem()
    {
        foreach (var token in new[] { "nope", new string('x', 43), "A1b2C3d4E5f6G7h8I9j0K1l2M3n4O5p6Q7r8S9t0U1v" })
        {
            var r = await Post("/api/v1/auth/reset-password", new { token, newPassword = NewPassword });
            Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
            using var p = await r.ProblemAsync();
            Assert.Equal("Invalid or expired link.", p.RootElement.GetProperty("title").GetString());
        }
    }

    [Fact]
    public async Task AVerificationLinkCannotBeUsedToResetAPassword()
    {
        var email = NewEmail();
        await RegisterAsync(email);
        var verifyToken = TokenFor(email, "Verify");
        var r = await Post("/api/v1/auth/reset-password", new { token = verifyToken, newPassword = NewPassword });
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Post("/api/v1/auth/login", new { email, password = Password })).StatusCode);   // password untouched
    }

    [Fact]
    public async Task ResetUnlocksALockedOutAccount()
    {
        var email = NewEmail();
        await RegisterAsync(email);
        for (var i = 0; i < 5; i++) await Post("/api/v1/auth/login", new { email, password = "WrongPassw0rd" });
        Assert.Equal(HttpStatusCode.Unauthorized, (await Post("/api/v1/auth/login", new { email, password = Password })).StatusCode);   // locked

        await ForgotAsync(email);
        await Post("/api/v1/auth/reset-password", new { token = TokenFor(email, "Reset"), newPassword = NewPassword });
        Assert.Equal(HttpStatusCode.OK, (await Post("/api/v1/auth/login", new { email, password = NewPassword })).StatusCode);
    }

    // ------------------------------------------------------------------ checkout gate (optional policy)
    [Fact]
    public async Task WhenRequired_UnverifiedCustomersCannotPlaceOrders_UntilTheyVerify()
    {
        using var f = ApiFactory.WithConfig(("Account:RequireVerifiedEmailForCheckout", "true"));
        using var c = f.CreateClient(new() { HandleCookies = false });
        var email = NewEmail();
        var auth = await RegisterAsync(email, c);

        var options = await (await c.GetAsync("/api/v1/checkout/options")).ReadAsync<CheckoutOptionsDto>();
        Assert.True(options.RequireVerifiedEmail);

        HttpRequestMessage Authed(HttpMethod m, string url, object? body = null) =>
            new HttpRequestMessage(m, url) { Content = body is null ? null : JsonContent.Create(body, options: Http.Json) }.WithBearer(auth.AccessToken);
        var product = (await (await c.GetAsync("/api/v1/products?inStock=true&pageSize=1")).ReadAsync<PagedResult<ProductListItemDto>>()).Items.First();
        await c.SendAsync(Authed(HttpMethod.Put, $"/api/v1/cart/items/{product.Id}", new { quantity = 1 }));
        var order = new { shippingMethod = "StorePickup", contactName = "Rahim", contactPhone = "01712345678", paymentMethod = "CashOnDelivery" };

        var blocked = await c.SendAsync(Authed(HttpMethod.Post, "/api/v1/orders", order));
        Assert.Equal(HttpStatusCode.Forbidden, blocked.StatusCode);
        using (var problem = await blocked.ProblemAsync())
            Assert.Equal("email_not_verified", problem.RootElement.GetProperty("code").GetString());

        Assert.Equal(HttpStatusCode.NoContent, (await c.PostJsonAsync("/api/v1/auth/verify-email", new { token = TokenFor(f, email, "Verify") })).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await c.SendAsync(Authed(HttpMethod.Post, "/api/v1/orders", order))).StatusCode);
    }

    [Fact]
    public async Task ByDefault_VerificationIsNotRequiredToCheckOut()
    {
        var options = await (await _client.GetAsync("/api/v1/checkout/options")).ReadAsync<CheckoutOptionsDto>();
        Assert.False(options.RequireVerifiedEmail);
    }

    // ------------------------------------------------------------------ no timing side channel
    private sealed class RecordingJobs : IAccountJobs
    {
        public List<(string Email, string? Ip)> Queued { get; } = [];
        public void QueuePasswordReset(string email, string? ipAddress) { lock (Queued) Queued.Add((email, ipAddress)); }
    }

    [Fact]
    public async Task ForgotPassword_OnlyEnqueues_SoTheRequestDoesTheSameWorkForKnownAndUnknownAddresses()
    {
        var jobs = new RecordingJobs();
        using var f = ApiFactory.WithServices(s => s.Replace(ServiceDescriptor.Singleton<IAccountJobs>(jobs)));
        using var c = f.CreateClient();
        var known = NewEmail();
        await RegisterAsync(known, c);
        var ghost = NewEmail();
        var before = f.Sql.Count;

        Assert.Equal(HttpStatusCode.Accepted, (await c.PostJsonAsync("/api/v1/auth/forgot-password", new { email = known })).StatusCode);
        var knownSql = f.Sql.Count - before;
        before = f.Sql.Count;
        Assert.Equal(HttpStatusCode.Accepted, (await c.PostJsonAsync("/api/v1/auth/forgot-password", new { email = ghost })).StatusCode);
        var ghostSql = f.Sql.Count - before;

        Assert.Equal([known, ghost], jobs.Queued.Select(j => j.Email));
        Assert.Equal((0, 0), (knownSql, ghostSql));                                                   // the request path never touches the database
        Assert.DoesNotContain(f.Emails.Sent, m => m.To == known && m.Subject.Contains("Reset"));      // ... nor sends mail inline
    }
}
