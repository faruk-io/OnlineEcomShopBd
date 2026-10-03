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
using TechBazar.Application.Mfa;
using TechBazar.Domain.Entities;
using TechBazar.Domain.Enums;
using TechBazar.Infrastructure.Persistence;

namespace TechBazar.Infrastructure.Identity;

/// <summary>
/// TOTP second factor. Invariants (each has a test):
/// a pending secret enforces nothing until a valid code proves the device works; a TOTP step is accepted once (atomic <c>LastUsedStep</c> bump);
/// a recovery code is accepted once (atomic <c>UsedAt</c>); wrong codes feed the Identity lockout; the challenge is single use and expires;
/// disabling needs password + code, ends every session, and is refused for roles that must use MFA.
/// </summary>
public sealed class MfaService(
    UserManager<ApplicationUser> users,
    ApplicationDbContext db,
    IMfaCrypto crypto,
    IEmailSender email,
    IOptions<MfaOptions> options,
    IOptions<AccountOptions> accountOptions,
    IOptions<TechBazar.Application.Payments.PaymentOptions> paymentOptions,
    ILogger<MfaService> logger) : IMfaService
{
    private readonly MfaOptions _o = options.Value;
    private const string InvalidChallenge = "This sign-in attempt is invalid or has expired. Please sign in again.";
    private const string RecoveryAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";   // no 0/O/1/I

    public bool IsRequiredFor(IEnumerable<string> roles) => _o.EnforceForAdmins && roles.Contains(Roles.Admin);

    public Task<bool> IsEnabledAsync(Guid userId, CancellationToken ct = default) =>
        db.MfaCredentials.AsNoTracking().AnyAsync(c => c.UserId == userId && c.ConfirmedAt != null, ct);

    public async Task<MfaStatusDto> GetStatusAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await GetUserAsync(userId);
        var cred = await db.MfaCredentials.AsNoTracking().FirstOrDefaultAsync(c => c.UserId == userId, ct);
        var enabled = cred?.ConfirmedAt is not null;
        var remaining = enabled ? await db.MfaRecoveryCodes.CountAsync(r => r.UserId == userId && r.UsedAt == null, ct) : 0;
        return new MfaStatusDto(enabled, IsRequiredFor(await users.GetRolesAsync(user)), cred is not null && !enabled, remaining, cred?.ConfirmedAt);
    }

    public async Task<MfaSetupDto> BeginSetupAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await GetUserAsync(userId);
        var cred = await db.MfaCredentials.FirstOrDefaultAsync(c => c.UserId == userId, ct);
        if (cred?.ConfirmedAt is not null)
            throw new ConflictException("Two-step verification is already on. Turn it off first to register a different authenticator.");

        var secret = Totp.NewSecret();
        var encrypted = crypto.Protect(secret, userId.ToString());
        if (cred is null)
            db.MfaCredentials.Add(new MfaCredential { UserId = userId, EncryptedSecret = encrypted, CreatedAt = DateTime.UtcNow });
        else
        {
            cred.EncryptedSecret = encrypted;   // restarting setup replaces the pending secret
            cred.CreatedAt = DateTime.UtcNow;
            cred.LastUsedStep = 0;
        }
        await db.SaveChangesAsync(ct);
        return new MfaSetupDto(Base32.Encode(secret), Totp.ProvisioningUri(_o.Issuer, user.Email!, secret), _o.Issuer, user.Email!);
    }

    public async Task<IReadOnlyList<string>> EnableAsync(Guid userId, string code, CancellationToken ct = default)
    {
        var user = await GetUserAsync(userId);
        var cred = await db.MfaCredentials.FirstOrDefaultAsync(c => c.UserId == userId, ct)
                   ?? throw Invalid("code", "Start the setup first, then enter a code from your authenticator app.");
        if (cred.ConfirmedAt is not null) throw new ConflictException("Two-step verification is already on.");

        if (await TryConsumeTotpAsync(cred, code, ct) is false)
        {
            await users.AccessFailedAsync(user);
            throw Invalid("code", "That code is not valid. Check the time on your phone and try the newest code.");
        }

        var now = DateTime.UtcNow;
        await db.MfaCredentials.Where(c => c.Id == cred.Id && c.ConfirmedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.ConfirmedAt, now), ct);
        var codes = await ReplaceRecoveryCodesAsync(userId, ct);
        await NotifyAsync(user, "Two-step verification was turned ON for your account.");
        logger.LogInformation("MFA enabled for user {UserId}", userId);
        return codes;
    }

    public async Task DisableAsync(Guid userId, string password, string? code, string? recoveryCode, CancellationToken ct = default)
    {
        var user = await GetUserAsync(userId);
        if (IsRequiredFor(await users.GetRolesAsync(user)))
            throw new ForbiddenException("Two-step verification is mandatory for your role and cannot be turned off.", "mfa_required_for_role");
        if (await users.IsLockedOutAsync(user)) throw new AuthenticationFailedException("Account temporarily locked. Try again later.");

        // Both factors, every time: a stolen session alone must not be able to remove the second factor.
        var passwordOk = await users.CheckPasswordAsync(user, password);
        var codeOk = passwordOk && await VerifyAnyAsync(userId, code, recoveryCode, ct);
        if (!passwordOk || !codeOk)
        {
            await users.AccessFailedAsync(user);
            throw Invalid(passwordOk ? "code" : "password", passwordOk ? "That code is not valid." : "That password is not correct.");
        }

        await db.MfaRecoveryCodes.Where(r => r.UserId == userId).ExecuteDeleteAsync(ct);
        await db.MfaCredentials.Where(c => c.UserId == userId).ExecuteDeleteAsync(ct);
        await RevokeSessionsAsync(userId, "MFA disabled", ct);
        await NotifyAsync(user, "Two-step verification was turned OFF for your account and you were signed out on all devices.");
        logger.LogInformation("MFA disabled for user {UserId}", userId);
    }

    public async Task<IReadOnlyList<string>> RegenerateRecoveryCodesAsync(Guid userId, string code, CancellationToken ct = default)
    {
        var user = await GetUserAsync(userId);
        var cred = await db.MfaCredentials.FirstOrDefaultAsync(c => c.UserId == userId && c.ConfirmedAt != null, ct)
                   ?? throw new ConflictException("Two-step verification is not on.");
        if (await TryConsumeTotpAsync(cred, code, ct) is false)
        {
            await users.AccessFailedAsync(user);
            throw Invalid("code", "That code is not valid.");
        }
        var codes = await ReplaceRecoveryCodesAsync(userId, ct);
        await NotifyAsync(user, "Your recovery codes were regenerated; the old ones no longer work.");
        return codes;
    }

    public async Task<MfaChallengeDto> CreateChallengeAsync(Guid userId, string? ipAddress, CancellationToken ct = default)
    {
        var user = await GetUserAsync(userId);
        var now = DateTime.UtcNow;
        await db.AccountTokens.Where(t => t.UserId == userId && t.Purpose == AccountTokenPurpose.MfaChallenge && t.ExpiresAt < now.AddHours(-1))
            .ExecuteDeleteAsync(ct);   // housekeeping only; parallel logins (two tabs) keep their own challenge

        var raw = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var expires = now.AddMinutes(_o.ChallengeMinutes);
        db.AccountTokens.Add(new AccountToken
        {
            UserId = userId, Purpose = AccountTokenPurpose.MfaChallenge, TokenHash = Sha256(raw),
            Email = user.NormalizedEmail ?? user.Email!.ToUpperInvariant(), CreatedAt = now, ExpiresAt = expires, CreatedByIp = ipAddress,
        });
        await db.SaveChangesAsync(ct);
        return new MfaChallengeDto(true, raw, expires);
    }

    public async Task<Guid> VerifyChallengeAsync(string mfaToken, string? code, string? recoveryCode, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        if (string.IsNullOrWhiteSpace(mfaToken) || mfaToken.Length > 128) throw new AuthenticationFailedException(InvalidChallenge);
        var hash = Sha256(mfaToken.Trim());
        var row = await db.AccountTokens.AsNoTracking().FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (row is null || row.Purpose != AccountTokenPurpose.MfaChallenge || !row.IsUsable(now)) throw new AuthenticationFailedException(InvalidChallenge);

        var user = await users.FindByIdAsync(row.UserId.ToString());
        if (user is null || !user.IsActive || !string.Equals(user.NormalizedEmail ?? user.Email?.ToUpperInvariant(), row.Email, StringComparison.Ordinal))
            throw new AuthenticationFailedException(InvalidChallenge);
        if (await users.IsLockedOutAsync(user)) throw new AuthenticationFailedException("Account temporarily locked. Try again later.");

        var usedRecovery = string.IsNullOrWhiteSpace(code) && !string.IsNullOrWhiteSpace(recoveryCode);
        if (!await VerifyAnyAsync(user.Id, code, recoveryCode, ct))
        {
            // Failed second factors count towards the same lockout as failed passwords, and a correct password never resets it
            // (see AuthService.LoginAsync), so a stolen password cannot be used to guess codes without limit.
            await users.AccessFailedAsync(user);
            throw new AuthenticationFailedException("That code is not valid.");
        }

        // Single use: the challenge can complete exactly one login.
        var spent = await db.AccountTokens.Where(t => t.Id == row.Id && t.UsedAt == null).ExecuteUpdateAsync(s => s.SetProperty(t => t.UsedAt, now), ct);
        if (spent == 0) throw new AuthenticationFailedException(InvalidChallenge);

        await users.ResetAccessFailedCountAsync(user);
        if (usedRecovery)
        {
            var left = await db.MfaRecoveryCodes.CountAsync(r => r.UserId == user.Id && r.UsedAt == null, ct);
            await NotifyAsync(user, $"A recovery code was used to sign in to your account ({left} left). If this was not you, act now.");
        }
        return user.Id;
    }

    public async Task<bool> ResetAsync(string email, CancellationToken ct = default)
    {
        var user = await users.FindByEmailAsync(email.Trim());
        if (user is null) return false;
        await db.MfaRecoveryCodes.Where(r => r.UserId == user.Id).ExecuteDeleteAsync(ct);
        await db.MfaCredentials.Where(c => c.UserId == user.Id).ExecuteDeleteAsync(ct);
        await RevokeSessionsAsync(user.Id, "MFA reset by operator", ct);
        logger.LogWarning("MFA reset by operator for user {UserId}", user.Id);
        return true;
    }

    // ------------------------------------------------------------------ internals
    private async Task<bool> VerifyAnyAsync(Guid userId, string? code, string? recoveryCode, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(code))
        {
            var cred = await db.MfaCredentials.FirstOrDefaultAsync(c => c.UserId == userId && c.ConfirmedAt != null, ct);
            return cred is not null && await TryConsumeTotpAsync(cred, code, ct);
        }
        if (!string.IsNullOrWhiteSpace(recoveryCode))
        {
            var normalised = NormaliseRecovery(recoveryCode);
            if (normalised is null) return false;
            var fingerprint = crypto.Fingerprint(normalised, userId.ToString());
            var now = DateTime.UtcNow;
            // atomic single use: only one of two concurrent attempts can flip UsedAt
            return await db.MfaRecoveryCodes.Where(r => r.UserId == userId && r.CodeHash == fingerprint && r.UsedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.UsedAt, now), ct) == 1;
        }
        return false;
    }

    /// <summary>True when the code is valid AND its time step has not been used before (replay protection, atomic).</summary>
    private async Task<bool> TryConsumeTotpAsync(MfaCredential cred, string code, CancellationToken ct)
    {
        byte[] secret;
        try { secret = crypto.Unprotect(cred.EncryptedSecret, cred.UserId.ToString()); }
        catch (CryptographicException ex)
        {
            logger.LogError(ex, "Could not decrypt the MFA secret of user {UserId}: wrong Mfa:SecretKey?", cred.UserId);
            return false;
        }
        var step = Totp.Verify(secret, code, DateTimeOffset.UtcNow);
        if (step is not { } s) return false;
        return await db.MfaCredentials.Where(c => c.Id == cred.Id && c.LastUsedStep < s)
            .ExecuteUpdateAsync(u => u.SetProperty(c => c.LastUsedStep, s), ct) == 1;
    }

    private async Task<IReadOnlyList<string>> ReplaceRecoveryCodesAsync(Guid userId, CancellationToken ct)
    {
        await db.MfaRecoveryCodes.Where(r => r.UserId == userId).ExecuteDeleteAsync(ct);
        var now = DateTime.UtcNow;
        var plain = new List<string>();
        for (var i = 0; i < _o.RecoveryCodeCount; i++)
        {
            var raw = new string(RandomNumberGenerator.GetItems<char>(RecoveryAlphabet, 10));   // 10 chars x 5 bits = 50 bits
            plain.Add($"{raw[..5]}-{raw[5..]}");
            db.MfaRecoveryCodes.Add(new MfaRecoveryCode { UserId = userId, CodeHash = crypto.Fingerprint(raw, userId.ToString()), CreatedAt = now });
        }
        await db.SaveChangesAsync(ct);
        return plain;
    }

    /// <summary>"abcde-fghij" -> "ABCDEFGHIJ"; null when not 10 characters from the recovery alphabet.</summary>
    internal static string? NormaliseRecovery(string input)
    {
        var s = new string(input.Where(c => c is not (' ' or '-')).ToArray()).ToUpperInvariant();
        return s.Length == 10 && s.All(c => RecoveryAlphabet.Contains(c)) ? s : null;
    }

    private Task<int> RevokeSessionsAsync(Guid userId, string reason, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        return db.RefreshTokens.Where(t => t.UserId == userId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now).SetProperty(t => t.RevokedReason, reason), ct);
    }

    private async Task NotifyAsync(ApplicationUser user, string what)
    {
        try
        {
            var baseUrl = string.IsNullOrWhiteSpace(accountOptions.Value.StorefrontBaseUrl) ? paymentOptions.Value.StorefrontBaseUrl : accountOptions.Value.StorefrontBaseUrl;
            var link = $"{baseUrl.TrimEnd('/')}/forgot-password";
            await email.SendAsync(AccountEmails.MfaChanged(user.Email!, user.FullName, what, link));
        }
        catch (Exception ex) { logger.LogWarning(ex, "Could not send the MFA notice to user {UserId}", user.Id); }
    }

    private async Task<ApplicationUser> GetUserAsync(Guid userId) =>
        await users.FindByIdAsync(userId.ToString()) ?? throw new NotFoundException("User not found.");

    private static ValidationException Invalid(string property, string message) => new([new ValidationFailure(property, message)]);
    private static string Sha256(string s) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s)));
}
