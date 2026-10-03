using System.Text.RegularExpressions;
using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TechBazar.Application.Auth;
using TechBazar.Application.Common;
using TechBazar.Application.Email;
using TechBazar.Domain.Entities;
using TechBazar.Domain.Enums;
using TechBazar.Infrastructure.Identity;
using TechBazar.UnitTests.Support;

namespace TechBazar.UnitTests.Orders;

/// <summary>Password reset and email verification: the one-time-token rules that matter for account takeover.</summary>
public partial class AccountServiceTests
{
    private const string NewPassword = "NewPassw0rd!";

    [GeneratedRegex(@"#token=([A-Za-z0-9_-]+)")]
    private static partial Regex TokenInLink();

    private static string LastToken(Scenario s, string subjectContains) =>
        TokenInLink().Match(s.Db.Emails.Sent.Last(m => m.Subject.Contains(subjectContains)).TextBody).Groups[1].Value;

    private static IAccountService Account(Scenario s) => s.Get<IAccountService>();
    private static UserManager<ApplicationUser> Users(Scenario s) => s.Get<UserManager<ApplicationUser>>();

    // ================================================================== forgot password
    [Fact]
    public async Task ForgotPassword_ForAnUnknownAddress_SendsNothing_StoresNothing_AndDoesNotThrow()
    {
        using var s = await Scenario.CreateAsync();
        await Account(s).RequestPasswordResetAsync("ghost@example.com", "1.2.3.4");
        Assert.Empty(s.Db.Emails.Sent);
        Assert.Empty(await s.Ctx.AccountTokens.ToListAsync());
    }

    [Fact]
    public async Task ForgotPassword_ForADeactivatedAccount_SendsNothing()
    {
        using var s = await Scenario.CreateAsync();
        var user = (await Users(s).FindByEmailAsync(s.Email))!;
        user.IsActive = false;
        await Users(s).UpdateAsync(user);
        await Account(s).RequestPasswordResetAsync(s.Email, null);
        Assert.Empty(s.Db.Emails.Sent);
    }

    [Fact]
    public async Task ForgotPassword_EmailsALinkFromConfiguration_WithTheTokenInTheFragment()
    {
        using var s = await Scenario.CreateAsync();
        await Account(s).RequestPasswordResetAsync("  RAHIM@Example.com ", "203.0.113.5");   // case / whitespace tolerant

        var mail = Assert.Single(s.Db.Emails.Sent);
        Assert.Equal(s.Email, mail.To);
        Assert.Contains("Reset your password", mail.Subject);
        var link = Regex.Match(mail.TextBody, @"https://\S+").Value;
        Assert.StartsWith("https://shop.test/reset-password#token=", link);          // configured origin, fragment (not query) token
        Assert.DoesNotContain("?", link);
        Assert.Equal(43, link.Split("#token=")[1].Length);                              // 256 bits, base64url
        Assert.Contains("expires in 60 minutes", mail.TextBody);
        Assert.Contains("#token=", mail.HtmlBody);
    }

    [Fact]
    public async Task OnlyTheHashOfATokenIsStored_TheRawValueNeverTouchesTheDatabase()
    {
        using var s = await Scenario.CreateAsync();
        await Account(s).RequestPasswordResetAsync(s.Email, "203.0.113.5");
        var raw = LastToken(s, "Reset");

        var row = Assert.Single(await s.Ctx.AccountTokens.AsNoTracking().ToListAsync());
        Assert.NotEqual(raw, row.TokenHash);
        Assert.Equal(64, row.TokenHash.Length);                                          // SHA-256 hex
        Assert.Matches("^[0-9A-F]{64}$", row.TokenHash);
        Assert.Equal((AccountTokenPurpose.PasswordReset, s.UserId), (row.Purpose, row.UserId));
        Assert.DoesNotContain(raw, string.Join("|", new[] { row.Email, row.CreatedByIp ?? "" }));
        Assert.True((row.ExpiresAt - row.CreatedAt).TotalMinutes is > 59.9 and < 60.1);
    }

    [Fact]
    public async Task ANewRequestSupersedesTheOlderLink()
    {
        using var s = await Scenario.CreateAsync();
        await Account(s).RequestPasswordResetAsync(s.Email, null);
        var first = LastToken(s, "Reset");
        await Account(s).RequestPasswordResetAsync(s.Email, null);
        var second = LastToken(s, "Reset");
        Assert.NotEqual(first, second);

        await Assert.ThrowsAsync<InvalidTokenException>(() => Account(s).ResetPasswordAsync(first, NewPassword, null));
        await Account(s).ResetPasswordAsync(second, NewPassword, null);
    }

    [Fact]
    public async Task ResetRequests_AreCappedPerAccountPerHour_AnsweredSilently_AndRecoverLater()
    {
        using var s = await Scenario.CreateAsync();
        for (var i = 0; i < 6; i++) await Account(s).RequestPasswordResetAsync(s.Email, null);   // no exception, no hint
        Assert.Equal(3, s.Db.Emails.Sent.Count(m => m.Subject.Contains("Reset")));                // default cap: 3 / hour

        s.Db.Clock.Advance(TimeSpan.FromMinutes(61));
        await Account(s).RequestPasswordResetAsync(s.Email, null);
        Assert.Equal(4, s.Db.Emails.Sent.Count(m => m.Subject.Contains("Reset")));
    }

    // ================================================================== reset password
    [Fact]
    public async Task Reset_SetsTheNewPassword_AndTheOldOneStopsWorking()
    {
        using var s = await Scenario.CreateAsync();
        await Account(s).RequestPasswordResetAsync(s.Email, null);
        await Account(s).ResetPasswordAsync(LastToken(s, "Reset"), NewPassword, "203.0.113.5");

        var user = (await Users(s).FindByEmailAsync(s.Email))!;
        Assert.True(await Users(s).CheckPasswordAsync(user, NewPassword));
        Assert.False(await Users(s).CheckPasswordAsync(user, "Passw0rd!"));
    }

    [Fact]
    public async Task Reset_EndsEverySession_LiftsLockout_ConfirmsTheEmail_AndRotatesTheSecurityStamp()
    {
        using var s = await Scenario.CreateAsync();
        var user = (await Users(s).FindByEmailAsync(s.Email))!;
        var stampBefore = user.SecurityStamp;
        s.Ctx.RefreshTokens.AddRange(
            new RefreshToken { UserId = s.UserId, TokenHash = "A1", CreatedAt = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow.AddDays(10) },
            new RefreshToken { UserId = s.UserId, TokenHash = "A2", CreatedAt = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow.AddDays(10) });
        user.AccessFailedCount = 4;
        user.LockoutEnd = DateTimeOffset.UtcNow.AddMinutes(15);
        await Users(s).UpdateAsync(user);
        await s.Ctx.SaveChangesAsync();
        Assert.False(user.EmailConfirmed);

        await Account(s).RequestPasswordResetAsync(s.Email, null);
        await Account(s).ResetPasswordAsync(LastToken(s, "Reset"), NewPassword, null);

        s.Ctx.ChangeTracker.Clear();
        Assert.All(await s.Ctx.RefreshTokens.Where(t => t.UserId == s.UserId).ToListAsync(), t => Assert.NotNull(t.RevokedAt));
        var after = (await Users(s).FindByEmailAsync(s.Email))!;
        Assert.Equal((0, null), (after.AccessFailedCount, after.LockoutEnd));
        Assert.True(after.EmailConfirmed);                                                  // the link proved control of the mailbox
        Assert.NotEqual(stampBefore, after.SecurityStamp);
    }

    [Fact]
    public async Task Reset_SendsAPasswordChangedNotice()
    {
        using var s = await Scenario.CreateAsync();
        await Account(s).RequestPasswordResetAsync(s.Email, null);
        await Account(s).ResetPasswordAsync(LastToken(s, "Reset"), NewPassword, null);

        var notice = s.Db.Emails.Sent.Last();
        Assert.Contains("password was changed", notice.Subject);
        Assert.Equal(s.Email, notice.To);
        Assert.DoesNotContain(NewPassword, notice.TextBody + notice.HtmlBody);
        Assert.Contains("https://shop.test/forgot-password", notice.TextBody);
    }

    [Fact]
    public async Task ATokenWorksExactlyOnce()
    {
        using var s = await Scenario.CreateAsync();
        await Account(s).RequestPasswordResetAsync(s.Email, null);
        var token = LastToken(s, "Reset");
        await Account(s).ResetPasswordAsync(token, NewPassword, null);
        await Assert.ThrowsAsync<InvalidTokenException>(() => Account(s).ResetPasswordAsync(token, "Another1Pass", null));
        Assert.True(await Users(s).CheckPasswordAsync((await Users(s).FindByEmailAsync(s.Email))!, NewPassword));   // second attempt changed nothing
    }

    [Fact]
    public async Task AnExpiredTokenIsRejected_AndNotAfterJustBeforeExpiry()
    {
        using var s = await Scenario.CreateAsync();
        await Account(s).RequestPasswordResetAsync(s.Email, null);
        var token = LastToken(s, "Reset");

        s.Db.Clock.Advance(TimeSpan.FromMinutes(61));
        await Assert.ThrowsAsync<InvalidTokenException>(() => Account(s).ResetPasswordAsync(token, NewPassword, null));

        await Account(s).RequestPasswordResetAsync(s.Email, null);                          // a fresh one, then check the boundary
        var fresh = LastToken(s, "Reset");
        s.Db.Clock.Advance(TimeSpan.FromMinutes(59));
        await Account(s).ResetPasswordAsync(fresh, NewPassword, null);
    }

    [Fact]
    public async Task AWeakPasswordIsRejectedWithoutSpendingTheToken()
    {
        using var s = await Scenario.CreateAsync();
        await Account(s).RequestPasswordResetAsync(s.Email, null);
        var token = LastToken(s, "Reset");

        var ex = await Assert.ThrowsAsync<ValidationException>(() => Account(s).ResetPasswordAsync(token, "weak", null));
        Assert.Contains(ex.Errors, e => e.PropertyName == "NewPassword");
        Assert.True(await Users(s).CheckPasswordAsync((await Users(s).FindByEmailAsync(s.Email))!, "Passw0rd!"));   // unchanged
        Assert.Null((await s.Ctx.AccountTokens.AsNoTracking().SingleAsync()).UsedAt);                                // link still usable

        await Account(s).ResetPasswordAsync(token, NewPassword, null);                      // same link, strong password: works
    }

    public static IEnumerable<object[]> GarbageTokens() =>
        [[""], ["   "], ["not-a-token"], ["'; DROP TABLE AccountTokens;--"], ["%00"], [new string('A', 129)], [new string('A', 43)], ["../../etc/passwd"]];

    [Theory, MemberData(nameof(GarbageTokens))]
    public async Task UnknownOrMalformedTokensAreRejectedUniformly(string token)
    {
        using var s = await Scenario.CreateAsync();
        await Assert.ThrowsAsync<InvalidTokenException>(() => Account(s).ResetPasswordAsync(token, NewPassword, null));
        await Assert.ThrowsAsync<InvalidTokenException>(() => Account(s).VerifyEmailAsync(token));
    }

    [Fact]
    public async Task ATokenIsBoundToItsPurpose_VerificationLinksCannotResetPasswords_AndViceVersa()
    {
        using var s = await Scenario.CreateAsync();
        await Account(s).SendVerificationAsync(s.UserId, null);
        await Account(s).RequestPasswordResetAsync(s.Email, null);
        var verify = LastToken(s, "Verify");
        var reset = LastToken(s, "Reset");

        await Assert.ThrowsAsync<InvalidTokenException>(() => Account(s).ResetPasswordAsync(verify, NewPassword, null));
        await Assert.ThrowsAsync<InvalidTokenException>(() => Account(s).VerifyEmailAsync(reset));
        // and the failed cross-use did not spend either of them
        await Account(s).VerifyEmailAsync(verify);
        await Account(s).ResetPasswordAsync(reset, NewPassword, null);
    }

    [Fact]
    public async Task ALinkDiesIfTheAddressChangedAfterItWasIssued()
    {
        using var s = await Scenario.CreateAsync();
        await Account(s).RequestPasswordResetAsync(s.Email, null);
        var token = LastToken(s, "Reset");

        var user = (await Users(s).FindByEmailAsync(s.Email))!;
        await Users(s).SetEmailAsync(user, "new.address@example.com");
        await Assert.ThrowsAsync<InvalidTokenException>(() => Account(s).ResetPasswordAsync(token, NewPassword, null));
    }

    [Fact]
    public async Task ALinkDiesIfTheAccountWasDeactivatedAfterItWasIssued()
    {
        using var s = await Scenario.CreateAsync();
        await Account(s).RequestPasswordResetAsync(s.Email, null);
        var token = LastToken(s, "Reset");
        var user = (await Users(s).FindByEmailAsync(s.Email))!;
        user.IsActive = false;
        await Users(s).UpdateAsync(user);
        await Assert.ThrowsAsync<InvalidTokenException>(() => Account(s).ResetPasswordAsync(token, NewPassword, null));
    }

    [Fact]
    public async Task OnePersonsTokenNeverAffectsAnotherAccount()
    {
        using var s = await Scenario.CreateAsync();
        var (otherId, otherEmail) = await s.NewUserAsync("karim@example.com");
        await Account(s).RequestPasswordResetAsync(s.Email, null);
        await Account(s).ResetPasswordAsync(LastToken(s, "Reset"), NewPassword, null);

        var other = (await Users(s).FindByEmailAsync(otherEmail))!;
        Assert.True(await Users(s).CheckPasswordAsync(other, "Passw0rd!"));
        Assert.Equal(otherId, other.Id);
    }

    // ================================================================== verify email
    [Fact]
    public async Task Verify_ConfirmsTheEmail_AndTheLinkWorksOnce()
    {
        using var s = await Scenario.CreateAsync();
        Assert.False((await Users(s).FindByEmailAsync(s.Email))!.EmailConfirmed);

        await Account(s).SendVerificationAsync(s.UserId, "203.0.113.5");
        var mail = Assert.Single(s.Db.Emails.Sent);
        Assert.Contains("Verify your email", mail.Subject);
        Assert.StartsWith("https://shop.test/verify-email#token=", Regex.Match(mail.TextBody, @"https://\S+").Value);
        var token = LastToken(s, "Verify");

        await Account(s).VerifyEmailAsync(token);
        Assert.True((await Users(s).FindByEmailAsync(s.Email))!.EmailConfirmed);
        await Assert.ThrowsAsync<InvalidTokenException>(() => Account(s).VerifyEmailAsync(token));
    }

    [Fact]
    public async Task Verify_Expires_AfterTheConfiguredLifetime()
    {
        using var s = await Scenario.CreateAsync();
        await Account(s).SendVerificationAsync(s.UserId, null);
        var token = LastToken(s, "Verify");
        s.Db.Clock.Advance(TimeSpan.FromHours(24).Add(TimeSpan.FromMinutes(1)));
        await Assert.ThrowsAsync<InvalidTokenException>(() => Account(s).VerifyEmailAsync(token));
        Assert.False((await Users(s).FindByEmailAsync(s.Email))!.EmailConfirmed);
    }

    [Fact]
    public async Task Resend_IssuesANewLink_AndTheOldOneStopsWorking()
    {
        using var s = await Scenario.CreateAsync();
        await Account(s).SendVerificationAsync(s.UserId, null);
        var old = LastToken(s, "Verify");
        await Account(s).SendVerificationAsync(s.UserId, null);
        var fresh = LastToken(s, "Verify");
        Assert.NotEqual(old, fresh);
        await Assert.ThrowsAsync<InvalidTokenException>(() => Account(s).VerifyEmailAsync(old));
        await Account(s).VerifyEmailAsync(fresh);
    }

    [Fact]
    public async Task Resend_IsANoOpForVerifiedAccounts_AndThrottledPerHour()
    {
        using var s = await Scenario.CreateAsync();
        for (var i = 0; i < 5; i++) await Account(s).SendVerificationAsync(s.UserId, null);
        Assert.Equal(3, s.Db.Emails.Sent.Count);

        await Account(s).VerifyEmailAsync(LastToken(s, "Verify"));
        var count = s.Db.Emails.Sent.Count;
        await Account(s).SendVerificationAsync(s.UserId, null);                              // already verified: nothing
        Assert.Equal(count, s.Db.Emails.Sent.Count);
    }

    [Fact]
    public async Task VerifyingAnAlreadyVerifiedAccountIsHarmless()
    {
        using var s = await Scenario.CreateAsync();
        await Account(s).SendVerificationAsync(s.UserId, null);
        var token = LastToken(s, "Verify");
        var user = (await Users(s).FindByEmailAsync(s.Email))!;
        user.EmailConfirmed = true;                                                          // e.g. verified through a password reset meanwhile
        await Users(s).UpdateAsync(user);
        await Account(s).VerifyEmailAsync(token);
        Assert.True((await Users(s).FindByEmailAsync(s.Email))!.EmailConfirmed);
    }

    // ================================================================== checkout gate
    [Fact]
    public async Task TheCheckoutGateIsOffByDefault()
    {
        using var s = await Scenario.CreateAsync();
        await Account(s).EnsureCanCheckoutAsync(s.UserId);
    }

    [Fact]
    public async Task WhenEnabled_UnverifiedUsersCannotCheckOut_AndVerifiedOnesCan()
    {
        using var s = await Scenario.CreateAsync(configure: svc => svc.Configure<AccountOptions>(o => o.RequireVerifiedEmailForCheckout = true));
        await Assert.ThrowsAsync<EmailNotVerifiedException>(() => Account(s).EnsureCanCheckoutAsync(s.UserId));
        await Assert.ThrowsAsync<EmailNotVerifiedException>(() => Account(s).EnsureCanCheckoutAsync(Guid.NewGuid()));   // unknown user: same

        await Account(s).SendVerificationAsync(s.UserId, null);
        await Account(s).VerifyEmailAsync(LastToken(s, "Verify"));
        await Account(s).EnsureCanCheckoutAsync(s.UserId);
    }

    [Fact]
    public async Task CheckoutOptionsTellTheUiWhetherVerificationIsRequired()
    {
        using var off = await Scenario.CreateAsync();
        using var on = await Scenario.CreateAsync(configure: svc => svc.Configure<AccountOptions>(o => o.RequireVerifiedEmailForCheckout = true));
        Assert.False(off.Get<TechBazar.Application.Orders.ICheckoutService>().Options().RequireVerifiedEmail);
        Assert.True(on.Get<TechBazar.Application.Orders.ICheckoutService>().Options().RequireVerifiedEmail);
    }

    // ================================================================== configuration / hygiene
    [Fact]
    public async Task LinksUseTheConfiguredOrigin_AndAccountOptionsOverridePaymentOptions()
    {
        using var s = await Scenario.CreateAsync(configure: svc => svc.Configure<AccountOptions>(o => o.StorefrontBaseUrl = "https://www.techbazar.example/"));
        await Account(s).RequestPasswordResetAsync(s.Email, null);
        Assert.StartsWith("https://www.techbazar.example/reset-password#token=", Regex.Match(s.Db.Emails.Sent.Single().TextBody, @"https://\S+").Value);
    }

    [Fact]
    public async Task NeitherTokensNorEmailAddressesOrPasswordsAreWrittenToTheLogs()
    {
        using var s = await Scenario.CreateAsync();
        var log = new CapturingLogger<AccountService>();
        var service = ActivatorUtilities.CreateInstance<AccountService>(s.Scope.ServiceProvider, (ILogger<AccountService>)log);

        await service.RequestPasswordResetAsync("ghost@example.com", null);
        await service.SendVerificationAsync(s.UserId, null);                                  // verification first: a reset would mark the email verified
        var verify = LastToken(s, "Verify");
        await service.VerifyEmailAsync(verify);
        await Assert.ThrowsAsync<InvalidTokenException>(() => service.VerifyEmailAsync(verify));
        await service.RequestPasswordResetAsync(s.Email, null);
        var token = LastToken(s, "Reset");
        await service.ResetPasswordAsync(token, NewPassword, null);

        var all = string.Join("\n", log.Entries.Select(e => e.Message));
        Assert.NotEmpty(log.Entries);
        foreach (var secret in new[] { token, verify, NewPassword, s.Email, "ghost@example.com", "rahim" })
            Assert.DoesNotContain(secret, all, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AFailingMailServerNeverBreaksTheRequest_AndRevealsNothing()
    {
        using var s = await Scenario.CreateAsync();
        s.Db.Emails.Throw = true;
        await Account(s).RequestPasswordResetAsync(s.Email, null);        // no exception: indistinguishable from success
        await Account(s).SendVerificationAsync(s.UserId, null);
        Assert.Equal(2, await s.Ctx.AccountTokens.CountAsync());
    }

    [Fact]
    public void ThePasswordPolicyIsSharedBetweenRegistrationAndReset()
    {
        var reset = new ResetPasswordRequestValidator();
        var register = new RegisterRequestValidator();
        foreach (var pw in new[] { "short1A", "alllowercase1", "ALLUPPERCASE1", "NoDigitsHere", new string('a', 101) + "A1" })
        {
            Assert.False(reset.Validate(new ResetPasswordRequest("t", pw)).IsValid, pw);
            Assert.False(register.Validate(new RegisterRequest("N", "a@b.co", null, pw)).IsValid, pw);
        }
        Assert.True(reset.Validate(new ResetPasswordRequest("t", NewPassword)).IsValid);
        Assert.False(reset.Validate(new ResetPasswordRequest("", NewPassword)).IsValid);
        Assert.False(reset.Validate(new ResetPasswordRequest(new string('t', 129), NewPassword)).IsValid);
    }
}
