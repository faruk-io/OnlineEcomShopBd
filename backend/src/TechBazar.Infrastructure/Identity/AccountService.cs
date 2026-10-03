using System.Security.Cryptography;
using System.Text;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TechBazar.Application.Auth;
using TechBazar.Application.Common;
using TechBazar.Application.Email;
using TechBazar.Application.Payments;
using TechBazar.Domain.Entities;
using TechBazar.Domain.Enums;
using TechBazar.Infrastructure.Persistence;

namespace TechBazar.Infrastructure.Identity;

/// <summary>
/// Email verification and password reset. Design (see docs/security.md):
/// 256-bit random tokens, only their SHA-256 stored; single use through an atomic UPDATE ... WHERE UsedAt IS NULL; short expiry;
/// purpose- and email-bound; links built from configuration (never the Host header) with the token in the URL FRAGMENT so it
/// never reaches server logs, proxies or Referer headers; the "forgot password" request is enumeration-safe and throttled.
/// </summary>
public sealed class AccountService(
    UserManager<ApplicationUser> users,
    ApplicationDbContext db,
    IEmailSender email,
    IOptions<AccountOptions> accountOptions,
    IOptions<PaymentOptions> paymentOptions,
    TimeProvider clock,
    ILogger<AccountService> logger) : IAccountService
{
    private readonly AccountOptions _opt = accountOptions.Value;

    private string BaseUrl => (string.IsNullOrWhiteSpace(_opt.StorefrontBaseUrl) ? paymentOptions.Value.StorefrontBaseUrl : _opt.StorefrontBaseUrl).TrimEnd('/');

    // ------------------------------------------------------------------ forgot password
    public async Task RequestPasswordResetAsync(string emailAddress, string? ip, CancellationToken ct = default)
    {
        var user = await users.FindByEmailAsync(emailAddress.Trim());
        if (user is null || !user.IsActive || string.IsNullOrEmpty(user.Email))
        {
            logger.LogInformation("Password reset requested for an unknown or inactive account");   // no address in the log
            return;
        }

        var link = await IssueAsync(user, AccountTokenPurpose.PasswordReset, TimeSpan.FromMinutes(_opt.ResetTokenMinutes), "reset-password", ip, ct);
        if (link is null) return;   // throttled
        await SendAsync(AccountEmails.PasswordReset(user.Email, user.FullName, link, _opt.ResetTokenMinutes), user.Id, ct);
    }

    // ------------------------------------------------------------------ reset password
    public async Task ResetPasswordAsync(string token, string newPassword, string? ip, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var row = await FindUsableAsync(token, AccountTokenPurpose.PasswordReset, now, ct) ?? throw new InvalidTokenException();
        var user = await users.FindByIdAsync(row.UserId.ToString());
        if (user is null || !user.IsActive || !SameEmail(user, row)) throw new InvalidTokenException();

        // 1. validate the password BEFORE spending the token: a weak password must not burn the link
        var failures = new List<ValidationFailure>();
        foreach (var validator in users.PasswordValidators)
        {
            var result = await validator.ValidateAsync(users, user, newPassword);
            failures.AddRange(result.Errors.Select(e => new ValidationFailure(nameof(ResetPasswordRequest.NewPassword), e.Description)));
        }
        if (failures.Count > 0) throw new ValidationException(failures);

        // 2. spend the token atomically: of two concurrent requests with the same link exactly one wins
        var spent = await db.AccountTokens.Where(t => t.Id == row.Id && t.UsedAt == null && t.ExpiresAt > now)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.UsedAt, now), ct);
        if (spent == 0) throw new InvalidTokenException();

        // 3. set the password, rotate the security stamp, lift any lockout; the link proves control of the mailbox
        user.PasswordHash = users.PasswordHasher.HashPassword(user, newPassword);
        user.EmailConfirmed = true;
        user.AccessFailedCount = 0;
        user.LockoutEnd = null;
        var update = await users.UpdateSecurityStampAsync(user);   // persists the whole user in one write
        if (!update.Succeeded) throw new ValidationException(update.Errors.Select(e => new ValidationFailure(nameof(ResetPasswordRequest.NewPassword), e.Description)));

        // 4. a changed password ends every session and every other outstanding link
        await db.RefreshTokens.Where(t => t.UserId == user.Id && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now).SetProperty(t => t.RevokedReason, "Password reset"), ct);
        await db.AccountTokens.Where(t => t.UserId == user.Id && t.Purpose == AccountTokenPurpose.PasswordReset && t.UsedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.UsedAt, now), ct);

        logger.LogInformation("Password reset completed for user {UserId}", user.Id);
        await SendAsync(AccountEmails.PasswordChanged(user.Email!, user.FullName, $"{BaseUrl}/forgot-password"), user.Id, ct);
    }

    // ------------------------------------------------------------------ verify email
    public async Task VerifyEmailAsync(string token, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var row = await FindUsableAsync(token, AccountTokenPurpose.EmailVerification, now, ct) ?? throw new InvalidTokenException();
        var user = await users.FindByIdAsync(row.UserId.ToString());
        if (user is null || !user.IsActive || !SameEmail(user, row)) throw new InvalidTokenException();

        var spent = await db.AccountTokens.Where(t => t.Id == row.Id && t.UsedAt == null && t.ExpiresAt > now)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.UsedAt, now), ct);
        if (spent == 0) throw new InvalidTokenException();

        if (!user.EmailConfirmed)
        {
            user.EmailConfirmed = true;
            var result = await users.UpdateAsync(user);
            if (!result.Succeeded) throw new InvalidOperationException("Could not mark the email as verified: " + string.Join("; ", result.Errors.Select(e => e.Code)));
            logger.LogInformation("Email verified for user {UserId}", user.Id);
        }
    }

    // ------------------------------------------------------------------ send / resend verification
    public async Task SendVerificationAsync(Guid userId, string? ip, CancellationToken ct = default)
    {
        var user = await users.FindByIdAsync(userId.ToString());
        if (user is null || !user.IsActive || user.EmailConfirmed || string.IsNullOrEmpty(user.Email)) return;

        var link = await IssueAsync(user, AccountTokenPurpose.EmailVerification, TimeSpan.FromHours(_opt.VerifyTokenHours), "verify-email", ip, ct);
        if (link is null) return;   // throttled
        await SendAsync(AccountEmails.VerifyEmail(user.Email, user.FullName, link, _opt.VerifyTokenHours), user.Id, ct);
    }

    public async Task EnsureCanCheckoutAsync(Guid userId, CancellationToken ct = default)
    {
        if (!_opt.RequireVerifiedEmailForCheckout) return;
        var user = await users.FindByIdAsync(userId.ToString());
        if (user is null || !user.EmailConfirmed) throw new EmailNotVerifiedException();
    }

    // ------------------------------------------------------------------ helpers
    /// <summary>Creates a token (superseding older ones of the same purpose) and returns the link, or null when the account is over its hourly limit.</summary>
    private async Task<string?> IssueAsync(ApplicationUser user, AccountTokenPurpose purpose, TimeSpan lifetime, string path, string? ip, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var recent = await db.AccountTokens.CountAsync(t => t.UserId == user.Id && t.Purpose == purpose && t.CreatedAt > now.AddHours(-1), ct);
        if (recent >= _opt.MaxRequestsPerHour)
        {
            logger.LogWarning("{Purpose} requests throttled for user {UserId}", purpose, user.Id);
            return null;
        }

        // only the newest link works; also opportunistic housekeeping of long-dead rows
        await db.AccountTokens.Where(t => t.UserId == user.Id && t.Purpose == purpose && t.UsedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.UsedAt, now), ct);
        await db.AccountTokens.Where(t => t.UserId == user.Id && t.ExpiresAt < now.AddDays(-7)).ExecuteDeleteAsync(ct);

        var raw = Base64Url(RandomNumberGenerator.GetBytes(32));
        db.AccountTokens.Add(new AccountToken
        {
            UserId = user.Id, Purpose = purpose, TokenHash = Hash(raw), Email = user.NormalizedEmail ?? user.Email!.ToUpperInvariant(),
            CreatedAt = now, ExpiresAt = now.Add(lifetime), CreatedByIp = ip,
        });
        await db.SaveChangesAsync(ct);
        return $"{BaseUrl}/{path}#token={raw}";   // fragment: never sent to servers, logs or Referer headers
    }

    private async Task<AccountToken?> FindUsableAsync(string token, AccountTokenPurpose purpose, DateTime now, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 128) return null;
        var hash = Hash(token.Trim());
        var row = await db.AccountTokens.AsNoTracking().FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        return row is not null && row.Purpose == purpose && row.IsUsable(now) ? row : null;
    }

    private static bool SameEmail(ApplicationUser user, AccountToken row) =>
        string.Equals(user.NormalizedEmail ?? user.Email?.ToUpperInvariant(), row.Email, StringComparison.Ordinal);

    /// <summary>Sending must never break the flow (and must not reveal anything through an error).</summary>
    private async Task SendAsync(EmailMessage message, Guid userId, CancellationToken ct)
    {
        try { await email.SendAsync(message, ct); }
        catch (Exception ex) { logger.LogWarning(ex, "Could not send '{Subject}' to user {UserId}", message.Subject, userId); }
    }

    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
