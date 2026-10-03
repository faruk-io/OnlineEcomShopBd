using System.Text.RegularExpressions;
using FluentValidation;

namespace TechBazar.Application.Auth;

public sealed record RegisterRequest(string FullName, string Email, string? Phone, string Password);
public sealed record LoginRequest(string Email, string Password);
/// <summary>The token may be omitted when the client uses the HttpOnly refresh cookie (web storefront).</summary>
public sealed record RefreshRequest(string? RefreshToken);
public sealed record LogoutRequest(string? RefreshToken);
public sealed record UpdateProfileRequest(string FullName, string? Phone);

public sealed record UserDto(Guid Id, string Email, string FullName, string? Phone, IReadOnlyList<string> Roles, bool EmailConfirmed = false,
    /// <summary>The account has a confirmed authenticator.</summary>
    bool MfaEnabled = false,
    /// <summary>Policy demands MFA for this account (Admin role while <c>Mfa:EnforceForAdmins</c>).</summary>
    bool MfaRequired = false,
    /// <summary>THIS session passed a second factor (the <c>amr</c> claim of the access token).</summary>
    bool MfaSession = false);

/// <summary>Either tokens, or (when the account has MFA) a challenge that must be completed via <c>mfa/verify</c>.</summary>
public sealed record LoginResult(AuthResponse? Auth, TechBazar.Application.Mfa.MfaChallengeDto? Challenge);

public sealed record AuthResponse(
    string AccessToken,
    DateTime AccessTokenExpiresAt,
    /// <summary>Null when the refresh token was delivered in an HttpOnly cookie instead of the JSON body.</summary>
    string? RefreshToken,
    DateTime RefreshTokenExpiresAt,
    UserDto User);

public interface IAuthService
{
    Task<AuthResponse> RegisterAsync(RegisterRequest request, string? ipAddress, CancellationToken ct = default);
    Task<LoginResult> LoginAsync(LoginRequest request, string? ipAddress, CancellationToken ct = default);
    /// <summary>Second step of an MFA login: trades the challenge + a code (or recovery code) for a session flagged <c>amr: mfa</c>.</summary>
    Task<AuthResponse> CompleteMfaLoginAsync(TechBazar.Application.Mfa.MfaVerifyRequest request, string? ipAddress, CancellationToken ct = default);
    /// <summary>Confirms the pending authenticator, ends all other sessions and returns recovery codes plus a fresh MFA-verified session.</summary>
    Task<TechBazar.Application.Mfa.MfaEnabledDto> EnableMfaAsync(Guid userId, string code, string? ipAddress, CancellationToken ct = default);
    /// <summary>Rotates the refresh token: the presented one is revoked and a new pair is issued. Re-use of a revoked token revokes the whole chain.</summary>
    Task<AuthResponse> RefreshAsync(RefreshRequest request, string? ipAddress, CancellationToken ct = default);
    /// <summary>Revokes one refresh token by possession (no access token needed: it may already have expired). Idempotent, never reveals whether the token existed.</summary>
    Task LogoutAsync(string refreshToken, CancellationToken ct = default);
    /// <summary>"Sign out everywhere": revokes every live refresh token of the user.</summary>
    Task LogoutAllAsync(Guid userId, CancellationToken ct = default);
    Task<UserDto> GetProfileAsync(Guid userId, bool mfaSession = false, CancellationToken ct = default);
    Task<UserDto> UpdateProfileAsync(Guid userId, UpdateProfileRequest request, bool mfaSession = false, CancellationToken ct = default);
}

/// <summary>The single password policy used by registration and password reset (mirrors the Identity options).</summary>
public static class PasswordRules
{
    public static IRuleBuilderOptions<T, string> MustBeAStrongPassword<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty().MinimumLength(8).MaximumLength(100)
            .Matches("[A-Z]").WithMessage("Password must contain an uppercase letter.")
            .Matches("[a-z]").WithMessage("Password must contain a lowercase letter.")
            .Matches("[0-9]").WithMessage("Password must contain a digit.");
}

public static partial class BdPhoneRule
{
    // Bangladeshi mobile: 01XXXXXXXXX, optionally prefixed with +88 / 88.
    [GeneratedRegex(@"^(?:\+?88)?01[3-9]\d{8}$")]
    public static partial Regex Pattern();
}

public sealed class UpdateProfileRequestValidator : AbstractValidator<UpdateProfileRequest>
{
    public UpdateProfileRequestValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Phone).Matches(BdPhoneRule.Pattern()).When(x => !string.IsNullOrWhiteSpace(x.Phone))
            .WithMessage("Phone must be a valid Bangladeshi mobile number, e.g. 01712345678.");
    }
}

public sealed partial class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    public RegisterRequestValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        // Bangladeshi mobile: 01XXXXXXXXX, optionally prefixed with +88 / 88.
        RuleFor(x => x.Phone).Matches(BdPhone()).When(x => !string.IsNullOrWhiteSpace(x.Phone))
            .WithMessage("Phone must be a valid Bangladeshi mobile number, e.g. 01712345678.");
        RuleFor(x => x.Password).MustBeAStrongPassword();
    }

    [GeneratedRegex(@"^(?:\+?88)?01[3-9]\d{8}$")]
    private static partial Regex BdPhone();
}

public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Password).NotEmpty();
    }
}

public sealed class RefreshRequestValidator : AbstractValidator<RefreshRequest>
{
    public RefreshRequestValidator() => RuleFor(x => x.RefreshToken).MaximumLength(512);
}

public sealed class LogoutRequestValidator : AbstractValidator<LogoutRequest>
{
    public LogoutRequestValidator() => RuleFor(x => x.RefreshToken).MaximumLength(512);
}
