using FluentValidation;
using TechBazar.Application.Auth;

namespace TechBazar.Application.Mfa;

/// <summary>Bound from the "Mfa" configuration section.</summary>
public sealed class MfaOptions
{
    public const string SectionName = "Mfa";

    /// <summary>When true (the default) the Admin role can only use the admin API in a session that passed a second factor.</summary>
    public bool EnforceForAdmins { get; set; } = true;
    /// <summary>
    /// Base64 of at least 32 random bytes (<c>openssl rand -base64 32</c>). Encrypts authenticator secrets at rest and keys the recovery-code hashes, so a
    /// database leak alone does not defeat MFA. REQUIRED outside Development (Development derives a throw-away key from Jwt:Key). Keep it in a secret store.
    /// </summary>
    public string? SecretKey { get; set; }
    public string Issuer { get; set; } = "TechBazar BD";
    public int ChallengeMinutes { get; set; } = 5;
    public int RecoveryCodeCount { get; set; } = 10;
}

public sealed record MfaStatusDto(bool Enabled, bool Required, bool SetupPending, int RecoveryCodesRemaining, DateTime? EnabledAt);
public sealed record MfaSetupDto(string Secret, string OtpAuthUri, string Issuer, string Account);
public sealed record MfaChallengeDto(bool MfaRequired, string MfaToken, DateTime ExpiresAt);
public sealed record MfaEnabledDto(IReadOnlyList<string> RecoveryCodes, AuthResponse Auth);
public sealed record RecoveryCodesDto(IReadOnlyList<string> RecoveryCodes);

public sealed record MfaVerifyRequest(string MfaToken, string? Code, string? RecoveryCode);
public sealed record MfaCodeRequest(string Code);
public sealed record MfaDisableRequest(string Password, string? Code, string? RecoveryCode);

public sealed class MfaVerifyRequestValidator : AbstractValidator<MfaVerifyRequest>
{
    public MfaVerifyRequestValidator()
    {
        RuleFor(x => x.MfaToken).NotEmpty().MaximumLength(128);
        RuleFor(x => x.Code).MaximumLength(16);
        RuleFor(x => x.RecoveryCode).MaximumLength(40);
        RuleFor(x => x).Must(x => !string.IsNullOrWhiteSpace(x.Code) ^ !string.IsNullOrWhiteSpace(x.RecoveryCode))
            .WithName("Code").WithMessage("Enter either the 6-digit code or a recovery code.");
    }
}

public sealed class MfaCodeRequestValidator : AbstractValidator<MfaCodeRequest>
{
    public MfaCodeRequestValidator() => RuleFor(x => x.Code).NotEmpty().MaximumLength(16);
}

public sealed class MfaDisableRequestValidator : AbstractValidator<MfaDisableRequest>
{
    public MfaDisableRequestValidator()
    {
        RuleFor(x => x.Password).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Code).MaximumLength(16);
        RuleFor(x => x.RecoveryCode).MaximumLength(40);
        RuleFor(x => x).Must(x => !string.IsNullOrWhiteSpace(x.Code) ^ !string.IsNullOrWhiteSpace(x.RecoveryCode))
            .WithName("Code").WithMessage("Enter either the 6-digit code or a recovery code.");
    }
}

/// <summary>Encrypts authenticator secrets and fingerprints recovery codes with keys that never live in the database.</summary>
public interface IMfaCrypto
{
    /// <summary>AES-256-GCM; <paramref name="context"/> (the user id) is authenticated, so a ciphertext cannot be moved to another account.</summary>
    string Protect(byte[] plaintext, string context);
    byte[] Unprotect(string protectedValue, string context);
    /// <summary>Keyed hash (HMAC-SHA256) of a normalised recovery code, bound to the user.</summary>
    string Fingerprint(string normalisedRecoveryCode, string userContext);
}

public interface IMfaService
{
    bool IsRequiredFor(IEnumerable<string> roles);
    Task<bool> IsEnabledAsync(Guid userId, CancellationToken ct = default);
    Task<MfaStatusDto> GetStatusAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Creates (or replaces) a PENDING secret. Nothing is enforced until <see cref="EnableAsync"/> proves the user can produce codes.</summary>
    Task<MfaSetupDto> BeginSetupAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Confirms the pending secret with a first code, turns MFA on, and returns the one-time recovery codes (shown once, stored only as keyed hashes).</summary>
    Task<IReadOnlyList<string>> EnableAsync(Guid userId, string code, CancellationToken ct = default);

    /// <summary>Needs the password AND a code. Refused for roles that must use MFA. Ends every session.</summary>
    Task DisableAsync(Guid userId, string password, string? code, string? recoveryCode, CancellationToken ct = default);

    /// <summary>Replaces all recovery codes (needs a current authenticator code, not a recovery code).</summary>
    Task<IReadOnlyList<string>> RegenerateRecoveryCodesAsync(Guid userId, string code, CancellationToken ct = default);

    /// <summary>Called after the password was correct for an account with MFA: a short-lived, single-use challenge (never a usable JWT).</summary>
    Task<MfaChallengeDto> CreateChallengeAsync(Guid userId, string? ipAddress, CancellationToken ct = default);

    /// <summary>Completes the challenge with an authenticator code or a recovery code; returns the user id. Every failure is an <c>AuthenticationFailedException</c>.</summary>
    Task<Guid> VerifyChallengeAsync(string mfaToken, string? code, string? recoveryCode, CancellationToken ct = default);

    /// <summary>Operator tool (CLI <c>mfa-reset</c>): removes MFA from an account whose device AND recovery codes are lost. Ends every session.</summary>
    Task<bool> ResetAsync(string email, CancellationToken ct = default);
}
