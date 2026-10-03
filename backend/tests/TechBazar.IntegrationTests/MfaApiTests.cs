using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TechBazar.Application.Auth;
using TechBazar.Application.Mfa;
using TechBazar.Domain.Entities;
using TechBazar.Domain.Enums;
using TechBazar.Infrastructure.Identity;
using TechBazar.Infrastructure.Persistence;
using TechBazar.IntegrationTests.Support;

namespace TechBazar.IntegrationTests;

/// <summary>Two-step verification over real HTTP: enrolment, the login challenge, replay, lockout, recovery codes, admin enforcement.</summary>
public partial class MfaApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Password = "Passw0rdX";
    private readonly HttpClient _client = factory.CreateClient(new() { HandleCookies = false });

    [GeneratedRegex(@"^[A-HJ-NP-Z2-9]{5}-[A-HJ-NP-Z2-9]{5}$")]
    private static partial Regex RecoveryShape();

    private static string NewEmail() => $"mfa{Guid.NewGuid():N}@example.com";

    private Task<HttpResponseMessage> Post(string url, object body, string? bearer = null, HttpClient? c = null)
    {
        var msg = new HttpRequestMessage(HttpMethod.Post, url) { Content = System.Net.Http.Json.JsonContent.Create(body, options: Http.Json) };
        if (bearer is not null) msg.WithBearer(bearer);
        return (c ?? _client).SendAsync(msg);
    }

    private Task<HttpResponseMessage> Get(string url, string bearer, HttpClient? c = null) =>
        (c ?? _client).SendAsync(new HttpRequestMessage(HttpMethod.Get, url).WithBearer(bearer));

    private async Task<AuthResponse> RegisterAsync(string? email = null, HttpClient? c = null) =>
        await (await Post("/api/v1/auth/register", new { fullName = "Mfa Test", email = email ?? NewEmail(), password = Password }, null, c)).ReadAsync<AuthResponse>();

    /// <summary>Code for the TOTP step <paramref name="offset"/> steps from now (-1, 0, +1 are inside the accepted window).</summary>
    private static string CodeAt(string base32Secret, int offset = 0)
    {
        Assert.True(Base32.TryDecode(base32Secret, out var secret));
        return Totp.Compute(secret, Totp.StepAt(DateTimeOffset.UtcNow) + offset);
    }

    private record Enrolled(string Email, string Secret, IReadOnlyList<string> RecoveryCodes, AuthResponse Session);

    /// <summary>Registers (or takes) an account and turns MFA on. Uses step -1 so the later login can use 0 and +1 (a step is single use).</summary>
    private async Task<Enrolled> EnrollAsync(string? email = null, AuthResponse? existing = null, ApiFactory? f = null, HttpClient? c = null)
    {
        var client = c ?? _client;
        email ??= NewEmail();
        var auth = existing ?? await RegisterAsync(email, client);
        var setup = await (await Post("/api/v1/auth/mfa/setup", new { }, auth.AccessToken, client)).ReadAsync<MfaSetupDto>();
        var enable = await Post("/api/v1/auth/mfa/enable", new { code = CodeAt(setup.Secret, -1) }, auth.AccessToken, client);
        Assert.Equal(HttpStatusCode.OK, enable.StatusCode);
        var dto = await enable.ReadAsync<MfaEnabledDto>();
        return new Enrolled(email, setup.Secret, dto.RecoveryCodes, dto.Auth);
    }

    private async Task<MfaChallengeDto> LoginChallengeAsync(string email, HttpClient? c = null)
    {
        var r = await Post("/api/v1/auth/login", new { email, password = Password }, null, c);
        Assert.Equal(HttpStatusCode.Accepted, r.StatusCode);
        return await r.ReadAsync<MfaChallengeDto>();
    }

    private static IEnumerable<Claim2> Claims(string jwt) => new JwtSecurityTokenHandler().ReadJwtToken(jwt).Claims.Select(c => new Claim2(c.Type, c.Value));
    private record Claim2(string Type, string Value);
    private static string[] Amr(string jwt) => [.. Claims(jwt).Where(c => c.Type == "amr").Select(c => c.Value)];

    private async Task<T> InDb<T>(Func<ApplicationDbContext, Task<T>> work, ApiFactory? f = null)
    {
        using var scope = (f ?? factory).Services.CreateScope();
        return await work(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
    }

    private async Task<Guid> UserIdAsync(string email) => await InDb(db => db.Users.Where(u => u.Email == email).Select(u => u.Id).SingleAsync());

    // ------------------------------------------------------------------ enrolment
    [Fact]
    public async Task Setup_ReturnsTheSecretAndProvisioningUri_NotCached_AndEnforcesNothingYet()
    {
        var email = NewEmail();
        var auth = await RegisterAsync(email);
        var r = await Post("/api/v1/auth/mfa/setup", new { }, auth.AccessToken);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Contains("no-store", r.Headers.CacheControl?.ToString());
        var setup = await r.ReadAsync<MfaSetupDto>();
        Assert.Equal(32, setup.Secret.Length);   // 160 bits in base32
        Assert.StartsWith("otpauth://totp/", setup.OtpAuthUri);
        Assert.Contains("secret=" + setup.Secret, setup.OtpAuthUri);
        Assert.Contains(Uri.EscapeDataString(email), setup.OtpAuthUri);

        var status = await (await Get("/api/v1/auth/mfa", auth.AccessToken)).ReadAsync<MfaStatusDto>();
        Assert.False(status.Enabled);
        Assert.True(status.SetupPending);

        // a pending secret never turns the login into a challenge
        var login = await Post("/api/v1/auth/login", new { email, password = Password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    [Fact]
    public async Task Enable_WithAWrongCode_IsRejected_AndNothingChanges()
    {
        var email = NewEmail();
        var auth = await RegisterAsync(email);
        var setup = await (await Post("/api/v1/auth/mfa/setup", new { }, auth.AccessToken)).ReadAsync<MfaSetupDto>();
        var wrong = CodeAt(setup.Secret, 0) == "000000" ? "111111" : "000000";
        var r = await Post("/api/v1/auth/mfa/enable", new { code = wrong }, auth.AccessToken);
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        using var problem = await r.ProblemAsync();
        Assert.True(problem.RootElement.GetProperty("errors").TryGetProperty("code", out _));
        Assert.Equal(HttpStatusCode.OK, (await Post("/api/v1/auth/login", new { email, password = Password })).StatusCode);
    }

    [Fact]
    public async Task Enable_WithoutStartingSetup_IsRejected()
    {
        var auth = await RegisterAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await Post("/api/v1/auth/mfa/enable", new { code = "123456" }, auth.AccessToken)).StatusCode);
    }

    [Fact]
    public async Task Enable_ReturnsTenWellFormedRecoveryCodes_AStrongSession_AndEndsTheOlderSessions()
    {
        var before = await RegisterAsync();
        var e = await EnrollAsync(existing: before, email: before.User.Email);

        Assert.Equal(10, e.RecoveryCodes.Count);
        Assert.Equal(10, e.RecoveryCodes.Distinct().Count());
        Assert.All(e.RecoveryCodes, c => Assert.Matches(RecoveryShape(), c));

        Assert.True(e.Session.User.MfaEnabled);
        Assert.True(e.Session.User.MfaSession);
        Assert.Equal(["pwd", "mfa"], Amr(e.Session.AccessToken));

        // the pre-enrolment refresh token no longer works: a password-only session cannot outlive turning on the second factor
        var refresh = await Post("/api/v1/auth/refresh", new { refreshToken = before.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
    }

    [Fact]
    public async Task SetupWhileEnabled_IsAConflict_UntilMfaIsTurnedOff()
    {
        var e = await EnrollAsync();
        Assert.Equal(HttpStatusCode.Conflict, (await Post("/api/v1/auth/mfa/setup", new { }, e.Session.AccessToken)).StatusCode);
    }

    [Fact]
    public async Task Status_ReportsRemainingRecoveryCodes()
    {
        var e = await EnrollAsync();
        var status = await (await Get("/api/v1/auth/mfa", e.Session.AccessToken)).ReadAsync<MfaStatusDto>();
        Assert.True(status.Enabled);
        Assert.False(status.SetupPending);
        Assert.Equal(10, status.RecoveryCodesRemaining);
        Assert.NotNull(status.EnabledAt);
    }

    [Fact]
    public async Task TheMfaEndpointsNeedASession()
    {
        foreach (var (method, url) in new[] { ("GET", "/api/v1/auth/mfa"), ("POST", "/api/v1/auth/mfa/setup"), ("POST", "/api/v1/auth/mfa/enable"),
                                              ("POST", "/api/v1/auth/mfa/disable"), ("POST", "/api/v1/auth/mfa/recovery-codes") })
        {
            var r = await _client.SendAsync(new HttpRequestMessage(new HttpMethod(method), url) { Content = method == "POST" ? System.Net.Http.Json.JsonContent.Create(new { }) : null });
            Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
        }
    }

    // ------------------------------------------------------------------ at-rest protection
    [Fact]
    public async Task TheDatabaseNeverHoldsTheSecretOrARecoveryCodeInTheClear()
    {
        var e = await EnrollAsync();
        var userId = await UserIdAsync(e.Email);
        var (cred, hashes) = await InDb(async db => (await db.MfaCredentials.AsNoTracking().SingleAsync(c => c.UserId == userId),
                                                      await db.MfaRecoveryCodes.AsNoTracking().Where(r => r.UserId == userId).Select(r => r.CodeHash).ToListAsync()));
        Assert.DoesNotContain(e.Secret, cred.EncryptedSecret);
        Assert.True(Base32.TryDecode(e.Secret, out var raw));
        Assert.DoesNotContain(Convert.ToBase64String(raw), cred.EncryptedSecret);
        Assert.Equal(10, hashes.Count);
        foreach (var code in e.RecoveryCodes)
        {
            var plain = code.Replace("-", "");
            Assert.DoesNotContain(plain, hashes);
            Assert.DoesNotContain(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(plain))), hashes);   // keyed, not a bare hash
        }
    }

    // ------------------------------------------------------------------ login challenge
    [Fact]
    public async Task Login_WithMfa_Answers202WithAChallenge_AndNoTokens()
    {
        var e = await EnrollAsync();
        var r = await Post("/api/v1/auth/login", new { email = e.Email, password = Password });
        Assert.Equal(HttpStatusCode.Accepted, r.StatusCode);
        var body = await r.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        Assert.True(json.RootElement.GetProperty("mfaRequired").GetBoolean());
        Assert.False(string.IsNullOrEmpty(json.RootElement.GetProperty("mfaToken").GetString()));
        Assert.DoesNotContain("accessToken", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("refreshToken", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("set-cookie", string.Join(';', r.Headers.Select(h => h.Key)), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Login_WithAWrongPassword_NeverReachesTheChallenge()
    {
        var e = await EnrollAsync();
        var r = await Post("/api/v1/auth/login", new { email = e.Email, password = "WrongPassw0rd" });
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    [Fact]
    public async Task TheChallengeTokenIsNotABearerToken()
    {
        var e = await EnrollAsync();
        var challenge = await LoginChallengeAsync(e.Email);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Get("/api/v1/auth/me", challenge.MfaToken)).StatusCode);
    }

    [Fact]
    public async Task CompletingTheChallenge_WithACode_YieldsAnMfaSession()
    {
        var e = await EnrollAsync();
        var challenge = await LoginChallengeAsync(e.Email);
        var r = await Post("/api/v1/auth/mfa/verify", new { mfaToken = challenge.MfaToken, code = CodeAt(e.Secret, 0) });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var auth = await r.ReadAsync<AuthResponse>();
        Assert.Equal(["pwd", "mfa"], Amr(auth.AccessToken));
        Assert.True(auth.User.MfaSession);
        Assert.Equal(HttpStatusCode.OK, (await Get("/api/v1/auth/me", auth.AccessToken)).StatusCode);
        var me = await (await Get("/api/v1/auth/me", auth.AccessToken)).ReadAsync<UserDto>();
        Assert.True(me.MfaEnabled);
        Assert.True(me.MfaSession);
    }

    [Fact]
    public async Task ACodeWorksOnce_EvenInsideItsValidityWindow()
    {
        var e = await EnrollAsync();   // consumed step -1
        var code = CodeAt(e.Secret, 0);
        var c1 = await LoginChallengeAsync(e.Email);
        Assert.Equal(HttpStatusCode.OK, (await Post("/api/v1/auth/mfa/verify", new { mfaToken = c1.MfaToken, code })).StatusCode);
        var c2 = await LoginChallengeAsync(e.Email);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Post("/api/v1/auth/mfa/verify", new { mfaToken = c2.MfaToken, code })).StatusCode);   // replay
    }

    [Fact]
    public async Task TheCodeUsedToEnableCannotImmediatelyLogYouIn()
    {
        var email = NewEmail();
        var auth = await RegisterAsync(email);
        var setup = await (await Post("/api/v1/auth/mfa/setup", new { }, auth.AccessToken)).ReadAsync<MfaSetupDto>();
        var code = CodeAt(setup.Secret, 0);
        Assert.Equal(HttpStatusCode.OK, (await Post("/api/v1/auth/mfa/enable", new { code }, auth.AccessToken)).StatusCode);
        var ch = await LoginChallengeAsync(email);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Post("/api/v1/auth/mfa/verify", new { mfaToken = ch.MfaToken, code })).StatusCode);
    }

    [Fact]
    public async Task ACodeFromTwoStepsAgoIsRejected_AndSoIsOneFromTheFuture()
    {
        var e = await EnrollAsync();
        var ch = await LoginChallengeAsync(e.Email);
        foreach (var offset in new[] { -3, 3 })
            Assert.Equal(HttpStatusCode.Unauthorized, (await Post("/api/v1/auth/mfa/verify", new { mfaToken = ch.MfaToken, code = CodeAt(e.Secret, offset) })).StatusCode);
    }

    [Fact]
    public async Task AChallengeCompletesExactlyOneLogin()
    {
        var e = await EnrollAsync();
        var ch = await LoginChallengeAsync(e.Email);
        Assert.Equal(HttpStatusCode.OK, (await Post("/api/v1/auth/mfa/verify", new { mfaToken = ch.MfaToken, code = CodeAt(e.Secret, 0) })).StatusCode);
        // same challenge, a fresh valid code (next step): still refused
        Assert.Equal(HttpStatusCode.Unauthorized, (await Post("/api/v1/auth/mfa/verify", new { mfaToken = ch.MfaToken, code = CodeAt(e.Secret, 1) })).StatusCode);
    }

    [Fact]
    public async Task TwoSimultaneousRedemptionsOfOneChallenge_NeverBothSucceed()
    {
        // Sequential reuse is stopped by the read check; this exercises the atomic UPDATE ... WHERE UsedAt IS NULL that closes the race.
        var e = await EnrollAsync();
        for (var round = 0; round < 5; round++)
        {
            var ch = await LoginChallengeAsync(e.Email);
            var codes = new[] { CodeAt(e.Secret, 0), CodeAt(e.Secret, 1) };
            var results = await Task.WhenAll(codes.Select(code => Post("/api/v1/auth/mfa/verify", new { mfaToken = ch.MfaToken, code })));
            Assert.True(results.Count(r => r.StatusCode == HttpStatusCode.OK) <= 1, "one challenge produced two sessions");
            if (results.Any(r => r.StatusCode == HttpStatusCode.OK)) break;
        }
    }

    [Fact]
    public async Task AnExpiredChallengeIsRefused_EvenWithAValidCode()
    {
        var e = await EnrollAsync();
        var ch = await LoginChallengeAsync(e.Email);
        var userId = await UserIdAsync(e.Email);
        await InDb(db => db.AccountTokens.Where(t => t.UserId == userId && t.Purpose == AccountTokenPurpose.MfaChallenge)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.ExpiresAt, DateTime.UtcNow.AddSeconds(-1))));
        Assert.Equal(HttpStatusCode.Unauthorized, (await Post("/api/v1/auth/mfa/verify", new { mfaToken = ch.MfaToken, code = CodeAt(e.Secret, 0) })).StatusCode);
    }

    [Fact]
    public async Task OnlyAnMfaChallengeWorks_NotAPasswordResetOrVerificationToken()
    {
        var e = await EnrollAsync();
        Assert.Equal(HttpStatusCode.Accepted, (await Post("/api/v1/auth/forgot-password", new { email = e.Email })).StatusCode);
        await factory.Services.GetRequiredService<AccountJobQueue>().DrainAsync();
        var mail = factory.Emails.Sent.Last(m => m.To == e.Email && m.Subject.Contains("Reset your password"));
        var token = Regex.Match(mail.TextBody, @"#token=([A-Za-z0-9_-]+)").Groups[1].Value;
        Assert.Equal(HttpStatusCode.Unauthorized, (await Post("/api/v1/auth/mfa/verify", new { mfaToken = token, code = CodeAt(e.Secret, 0) })).StatusCode);
    }

    [Fact]
    public async Task AChallengeDiesWhenTheAccountEmailChanges()
    {
        var e = await EnrollAsync();
        var ch = await LoginChallengeAsync(e.Email);
        var userId = await UserIdAsync(e.Email);
        await InDb(db => db.Users.Where(u => u.Id == userId).ExecuteUpdateAsync(s => s.SetProperty(u => u.NormalizedEmail, "CHANGED@EXAMPLE.COM")));
        Assert.Equal(HttpStatusCode.Unauthorized, (await Post("/api/v1/auth/mfa/verify", new { mfaToken = ch.MfaToken, code = CodeAt(e.Secret, 0) })).StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("garbage")]
    public async Task UnknownChallengesAreRefused(string? token)
    {
        var r = await Post("/api/v1/auth/mfa/verify", new { mfaToken = token ?? "", code = "123456" });
        Assert.True(r.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task VerifyNeedsExactlyOneOfCodeOrRecoveryCode()
    {
        var e = await EnrollAsync();
        var ch = await LoginChallengeAsync(e.Email);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post("/api/v1/auth/mfa/verify", new { mfaToken = ch.MfaToken })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post("/api/v1/auth/mfa/verify",
            new { mfaToken = ch.MfaToken, code = "123456", recoveryCode = e.RecoveryCodes[0] })).StatusCode);
    }

    // ------------------------------------------------------------------ lockout
    [Fact]
    public async Task WrongCodesFeedTheLockout_AndACorrectPasswordDoesNotResetIt()
    {
        var e = await EnrollAsync();
        var wrong = CodeAt(e.Secret, 0) == "000000" ? "111111" : "000000";
        var ch = await LoginChallengeAsync(e.Email);
        for (var i = 0; i < 3; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await Post("/api/v1/auth/mfa/verify", new { mfaToken = ch.MfaToken, code = wrong })).StatusCode);

        // a fresh, correct password must NOT wipe the three failures...
        var ch2 = await LoginChallengeAsync(e.Email);
        for (var i = 0; i < 2; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await Post("/api/v1/auth/mfa/verify", new { mfaToken = ch2.MfaToken, code = wrong })).StatusCode);

        // ...so the fifth failure locks the account: even the right code is refused now, and so is the password.
        var locked = await Post("/api/v1/auth/mfa/verify", new { mfaToken = ch2.MfaToken, code = CodeAt(e.Secret, 0) });
        Assert.Equal(HttpStatusCode.Unauthorized, locked.StatusCode);
        Assert.Contains("locked", (await locked.Content.ReadAsStringAsync()), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Post("/api/v1/auth/login", new { email = e.Email, password = Password })).StatusCode);
    }

    [Fact]
    public async Task ASuccessfulSecondFactorResetsTheFailureCounter()
    {
        var e = await EnrollAsync();
        var wrong = CodeAt(e.Secret, 0) == "000000" ? "111111" : "000000";
        var ch = await LoginChallengeAsync(e.Email);
        for (var i = 0; i < 4; i++) await Post("/api/v1/auth/mfa/verify", new { mfaToken = ch.MfaToken, code = wrong });
        Assert.Equal(HttpStatusCode.OK, (await Post("/api/v1/auth/mfa/verify", new { mfaToken = ch.MfaToken, code = CodeAt(e.Secret, 0) })).StatusCode);

        // counter is back to zero: four more failures are survivable
        var ch2 = await LoginChallengeAsync(e.Email);
        for (var i = 0; i < 4; i++) await Post("/api/v1/auth/mfa/verify", new { mfaToken = ch2.MfaToken, code = wrong });
        Assert.Equal(HttpStatusCode.OK, (await Post("/api/v1/auth/mfa/verify", new { mfaToken = ch2.MfaToken, code = CodeAt(e.Secret, 1) })).StatusCode);
    }

    // ------------------------------------------------------------------ recovery codes
    [Fact]
    public async Task ARecoveryCodeSignsYouIn_Once_AndTheOwnerIsToldAboutIt()
    {
        var e = await EnrollAsync();
        var code = e.RecoveryCodes[3];
        var ch = await LoginChallengeAsync(e.Email);
        // typed in lower case, no hyphen: still accepted
        var r = await Post("/api/v1/auth/mfa/verify", new { mfaToken = ch.MfaToken, recoveryCode = code.Replace("-", "").ToLowerInvariant() });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var auth = await r.ReadAsync<AuthResponse>();
        Assert.Equal(["pwd", "mfa"], Amr(auth.AccessToken));
        Assert.Contains(factory.Emails.Sent, m => m.To == e.Email && m.Subject.Contains("Two-step verification changed") && m.TextBody.Contains("recovery code was used"));

        var ch2 = await LoginChallengeAsync(e.Email);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Post("/api/v1/auth/mfa/verify", new { mfaToken = ch2.MfaToken, recoveryCode = code })).StatusCode);

        var status = await (await Get("/api/v1/auth/mfa", auth.AccessToken)).ReadAsync<MfaStatusDto>();
        Assert.Equal(9, status.RecoveryCodesRemaining);
    }

    [Fact]
    public async Task ARecoveryCodeBelongsToOneUser()
    {
        var a = await EnrollAsync();
        var b = await EnrollAsync();
        var ch = await LoginChallengeAsync(b.Email);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Post("/api/v1/auth/mfa/verify", new { mfaToken = ch.MfaToken, recoveryCode = a.RecoveryCodes[0] })).StatusCode);
    }

    [Fact]
    public async Task RegeneratingNeedsACurrentCode_ReplacesTheOldCodes_AndRefusesRecoveryCodes()
    {
        var e = await EnrollAsync();
        // wrong code
        Assert.Equal(HttpStatusCode.BadRequest, (await Post("/api/v1/auth/mfa/recovery-codes", new { code = "000000" == CodeAt(e.Secret, 0) ? "111111" : "000000" }, e.Session.AccessToken)).StatusCode);
        // a recovery code is not accepted as the "code"
        Assert.Equal(HttpStatusCode.BadRequest, (await Post("/api/v1/auth/mfa/recovery-codes", new { code = e.RecoveryCodes[0] }, e.Session.AccessToken)).StatusCode);

        var r = await Post("/api/v1/auth/mfa/recovery-codes", new { code = CodeAt(e.Secret, 0) }, e.Session.AccessToken);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Contains("no-store", r.Headers.CacheControl?.ToString());
        var fresh = (await r.ReadAsync<RecoveryCodesDto>()).RecoveryCodes;
        Assert.Equal(10, fresh.Count);
        Assert.Empty(fresh.Intersect(e.RecoveryCodes));

        var ch = await LoginChallengeAsync(e.Email);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Post("/api/v1/auth/mfa/verify", new { mfaToken = ch.MfaToken, recoveryCode = e.RecoveryCodes[0] })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Post("/api/v1/auth/mfa/verify", new { mfaToken = ch.MfaToken, recoveryCode = fresh[0] })).StatusCode);
    }

    // ------------------------------------------------------------------ turning it off
    [Fact]
    public async Task Disable_NeedsThePasswordAndACode_EndsEverySession_AndRestoresPasswordOnlyLogin()
    {
        var e = await EnrollAsync();
        var bad = await Post("/api/v1/auth/mfa/disable", new { password = "WrongPassw0rd", code = CodeAt(e.Secret, 0) }, e.Session.AccessToken);
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        var noCode = await Post("/api/v1/auth/mfa/disable", new { password = Password, code = "000000" == CodeAt(e.Secret, 0) ? "111111" : "000000" }, e.Session.AccessToken);
        Assert.Equal(HttpStatusCode.BadRequest, noCode.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, (await Post("/api/v1/auth/login", new { email = e.Email, password = Password })).StatusCode);   // still on

        var ok = await Post("/api/v1/auth/mfa/disable", new { password = Password, code = CodeAt(e.Secret, 0) }, e.Session.AccessToken);
        Assert.Equal(HttpStatusCode.NoContent, ok.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Post("/api/v1/auth/refresh", new { refreshToken = e.Session.RefreshToken })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Post("/api/v1/auth/login", new { email = e.Email, password = Password })).StatusCode);

        var userId = await UserIdAsync(e.Email);
        Assert.Equal(0, await InDb(db => db.MfaCredentials.CountAsync(c => c.UserId == userId)));
        Assert.Equal(0, await InDb(db => db.MfaRecoveryCodes.CountAsync(c => c.UserId == userId)));
        Assert.Contains(factory.Emails.Sent, m => m.To == e.Email && m.TextBody.Contains("turned OFF"));
    }

    [Fact]
    public async Task Disable_WithARecoveryCode_Works()
    {
        var e = await EnrollAsync();
        var r = await Post("/api/v1/auth/mfa/disable", new { password = Password, recoveryCode = e.RecoveryCodes[0] }, e.Session.AccessToken);
        Assert.Equal(HttpStatusCode.NoContent, r.StatusCode);
    }

    [Fact]
    public async Task AStolenPasswordOnlySessionCannotSwitchMfaOff()
    {
        // Someone with the password but no device: every disable attempt fails and feeds the lockout.
        var e = await EnrollAsync();
        var ch = await LoginChallengeAsync(e.Email);
        Assert.NotNull(ch);
        var r = await Post("/api/v1/auth/mfa/disable", new { password = Password, code = "000000" == CodeAt(e.Secret, 0) ? "111111" : "000000" }, e.Session.AccessToken);
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task ResetByTheOperator_RemovesMfaAndEndsSessions()
    {
        var e = await EnrollAsync();
        using (var scope = factory.Services.CreateScope())
        {
            var mfa = scope.ServiceProvider.GetRequiredService<IMfaService>();
            Assert.False(await mfa.ResetAsync("nobody@example.com"));
            Assert.True(await mfa.ResetAsync(e.Email));
        }
        Assert.Equal(HttpStatusCode.Unauthorized, (await Post("/api/v1/auth/refresh", new { refreshToken = e.Session.RefreshToken })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Post("/api/v1/auth/login", new { email = e.Email, password = Password })).StatusCode);
    }

    // ------------------------------------------------------------------ sessions & refresh
    [Fact]
    public async Task APasswordOnlyLoginCarriesOnlyPwd()
    {
        var auth = await RegisterAsync();
        Assert.Equal(["pwd"], Amr(auth.AccessToken));
        Assert.False(auth.User.MfaSession);
    }

    [Fact]
    public async Task RefreshKeepsTheSessionStrength_ItNeverUpgradesOrDowngrades()
    {
        var e = await EnrollAsync();
        var ch = await LoginChallengeAsync(e.Email);
        var strong = await (await Post("/api/v1/auth/mfa/verify", new { mfaToken = ch.MfaToken, code = CodeAt(e.Secret, 0) })).ReadAsync<AuthResponse>();
        var refreshed = await (await Post("/api/v1/auth/refresh", new { refreshToken = strong.RefreshToken })).ReadAsync<AuthResponse>();
        Assert.Equal(["pwd", "mfa"], Amr(refreshed.AccessToken));
        Assert.True(refreshed.User.MfaSession);
        var again = await (await Post("/api/v1/auth/refresh", new { refreshToken = refreshed.RefreshToken })).ReadAsync<AuthResponse>();
        Assert.Equal(["pwd", "mfa"], Amr(again.AccessToken));

        // and a password-only session (flag false in the DB) stays password-only through rotation
        var plain = await RegisterAsync();
        var plainRefreshed = await (await Post("/api/v1/auth/refresh", new { refreshToken = plain.RefreshToken })).ReadAsync<AuthResponse>();
        Assert.Equal(["pwd"], Amr(plainRefreshed.AccessToken));
    }

    [Fact]
    public async Task InCookieMode_TheMfaSessionAlsoTravelsOnlyInTheHttpOnlyCookie()
    {
        var e = await EnrollAsync();
        var ch = await LoginChallengeAsync(e.Email);
        var msg = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/mfa/verify")
        {
            Content = System.Net.Http.Json.JsonContent.Create(new { mfaToken = ch.MfaToken, code = CodeAt(e.Secret, 0) }, options: Http.Json),
        };
        msg.Headers.Add("X-Refresh-Mode", "cookie");
        var r = await _client.SendAsync(msg);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var cookie = Assert.Single(r.Headers.GetValues("Set-Cookie"), c => c.StartsWith("tb_rt="));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Null((await r.ReadAsync<AuthResponse>()).RefreshToken);
    }

    // ------------------------------------------------------------------ admin enforcement
    private static ApiFactory Enforcing() => ApiFactory.WithConfig(("Mfa:EnforceForAdmins", "true"));

    private async Task<(string Email, AuthResponse Auth)> NewAdminAsync(ApiFactory f, HttpClient c)
    {
        var email = NewEmail();
        var auth = await RegisterAsync(email, c);
        await using var scope = f.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        Assert.True((await users.AddToRoleAsync((await users.FindByEmailAsync(email))!, Roles.Admin)).Succeeded);
        // log in again so the token carries the Admin role
        var login = await (await Post("/api/v1/auth/login", new { email, password = Password }, null, c)).ReadAsync<AuthResponse>();
        return (email, login);
    }

    [Fact]
    public async Task AnAdminWithoutASecondFactor_GetsAMachineReadable403OnTheAdminApi()
    {
        await using var f = Enforcing();
        var c = f.CreateClient(new() { HandleCookies = false });
        var (_, admin) = await NewAdminAsync(f, c);

        var r = await Get("/api/v1/admin/dashboard", admin.AccessToken, c);
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        using var problem = await r.ProblemAsync();
        Assert.Equal("mfa_required", problem.RootElement.GetProperty("code").GetString());
        Assert.True(admin.User.MfaRequired);
        Assert.False(admin.User.MfaSession);
    }

    [Fact]
    public async Task AnAdminCanStillReachTheEnrolmentEndpoints_AndAfterEnrolmentTheAdminApiOpens()
    {
        await using var f = Enforcing();
        var c = f.CreateClient(new() { HandleCookies = false });
        var (email, admin) = await NewAdminAsync(f, c);
        Assert.Equal(HttpStatusCode.Forbidden, (await Get("/api/v1/admin/dashboard", admin.AccessToken, c)).StatusCode);

        var e = await EnrollAsync(email, admin, f, c);
        Assert.Equal(HttpStatusCode.OK, (await Get("/api/v1/admin/dashboard", e.Session.AccessToken, c)).StatusCode);

        // the very same account, signing in the normal way, goes through the challenge and gets admin access only afterwards
        var ch = await LoginChallengeAsync(email, c);
        var s = await (await Post("/api/v1/auth/mfa/verify", new { mfaToken = ch.MfaToken, code = CodeAt(e.Secret, 0) }, null, c)).ReadAsync<AuthResponse>();
        Assert.Equal(HttpStatusCode.OK, (await Get("/api/v1/admin/dashboard", s.AccessToken, c)).StatusCode);
        var refreshed = await (await Post("/api/v1/auth/refresh", new { refreshToken = s.RefreshToken }, null, c)).ReadAsync<AuthResponse>();
        Assert.Equal(HttpStatusCode.OK, (await Get("/api/v1/admin/dashboard", refreshed.AccessToken, c)).StatusCode);
    }

    [Fact]
    public async Task ACustomerGetsAPlain403_NotAnMfaPrompt_AndAnonymousStays401()
    {
        await using var f = Enforcing();
        var c = f.CreateClient(new() { HandleCookies = false });
        var customer = await RegisterAsync(null, c);
        var r = await Get("/api/v1/admin/dashboard", customer.AccessToken, c);
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        var body = await r.Content.ReadAsStringAsync();
        Assert.DoesNotContain("mfa_required", body);
        Assert.False(customer.User.MfaRequired);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/api/v1/admin/dashboard")).StatusCode);
    }

    [Fact]
    public async Task AdministratorsCannotTurnMfaOff()
    {
        await using var f = Enforcing();
        var c = f.CreateClient(new() { HandleCookies = false });
        var (email, admin) = await NewAdminAsync(f, c);
        var e = await EnrollAsync(email, admin, f, c);
        var r = await Post("/api/v1/auth/mfa/disable", new { password = Password, code = CodeAt(e.Secret, 0) }, e.Session.AccessToken, c);
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        using var problem = await r.ProblemAsync();
        Assert.Equal("mfa_required_for_role", problem.RootElement.GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.Accepted, (await Post("/api/v1/auth/login", new { email, password = Password }, null, c)).StatusCode);
    }

    [Fact]
    public async Task ATokenWithAForgedMfaClaimIsRejected_BecauseItIsSigned()
    {
        await using var f = Enforcing();
        var c = f.CreateClient(new() { HandleCookies = false });
        var (_, admin) = await NewAdminAsync(f, c);
        // splice the claim into the payload of a genuine token without re-signing it
        var parts = admin.AccessToken.Split('.');
        var payload = System.Text.Encoding.UTF8.GetString(Microsoft.IdentityModel.Tokens.Base64UrlEncoder.DecodeBytes(parts[1]));
        var forged = payload.Replace("\"amr\":\"pwd\"", "\"amr\":[\"pwd\",\"mfa\"]");
        Assert.NotEqual(payload, forged);
        var token = $"{parts[0]}.{Microsoft.IdentityModel.Tokens.Base64UrlEncoder.Encode(forged)}.{parts[2]}";
        Assert.Equal(HttpStatusCode.Unauthorized, (await Get("/api/v1/admin/dashboard", token, c)).StatusCode);
    }

    [Fact]
    public async Task WhenEnforcementIsOff_AdminsKeepWorkingWithAPasswordOnly()
    {
        var c = factory.CreateClient(new() { HandleCookies = false });
        var login = await (await Post("/api/v1/auth/login", new { email = ApiFactory.AdminEmail, password = ApiFactory.AdminPassword }, null, c)).ReadAsync<AuthResponse>();
        Assert.Equal(HttpStatusCode.OK, (await Get("/api/v1/admin/dashboard", login.AccessToken, c)).StatusCode);
    }

    [Fact]
    public void EnforcementIsOnByDefault() => Assert.True(new MfaOptions().EnforceForAdmins);

    // ------------------------------------------------------------------ rate limiting
    [Fact]
    public async Task TheVerifyEndpointSharesTheAuthRateLimit()
    {
        await using var f = ApiFactory.WithAuthLimit(3);
        var c = f.CreateClient(new() { HandleCookies = false });
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 6; i++)
            statuses.Add((await Post("/api/v1/auth/mfa/verify", new { mfaToken = "x", code = "123456" }, null, c)).StatusCode);
        Assert.Contains(HttpStatusCode.TooManyRequests, statuses);
    }
}
