using FluentValidation;
using System.Text.RegularExpressions;

namespace TechBazar.Application.Auth;

public sealed record RegisterRequest(string FullName, string Email, string? Phone, string Password);
public sealed record LoginRequest(string Email, string Password);
public sealed record RefreshRequest(string RefreshToken);
public sealed record LogoutRequest(string RefreshToken);
public sealed record UpdateProfileRequest(string FullName, string? Phone);

public sealed record UserDto(Guid Id, string Email, string FullName, string? Phone, IReadOnlyList<string> Roles);

public sealed record AuthResponse(
    string AccessToken,
    DateTime AccessTokenExpiresAt,
    string RefreshToken,
    DateTime RefreshTokenExpiresAt,
    UserDto User);

public interface IAuthService
{
    Task<AuthResponse> RegisterAsync(RegisterRequest request, string? ipAddress, CancellationToken ct = default);
    Task<AuthResponse> LoginAsync(LoginRequest request, string? ipAddress, CancellationToken ct = default);
    /// <summary>Rotates the refresh token: the presented one is revoked and a new pair is issued. Re-use of a revoked token revokes the whole chain.</summary>
    Task<AuthResponse> RefreshAsync(RefreshRequest request, string? ipAddress, CancellationToken ct = default);
    Task LogoutAsync(Guid userId, string refreshToken, CancellationToken ct = default);
    Task<UserDto> GetProfileAsync(Guid userId, CancellationToken ct = default);
    Task<UserDto> UpdateProfileAsync(Guid userId, UpdateProfileRequest request, CancellationToken ct = default);
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
        RuleFor(x => x.Password).NotEmpty().MinimumLength(8).MaximumLength(100)
            .Matches("[A-Z]").WithMessage("Password must contain an uppercase letter.")
            .Matches("[a-z]").WithMessage("Password must contain a lowercase letter.")
            .Matches("[0-9]").WithMessage("Password must contain a digit.");
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
    public RefreshRequestValidator() => RuleFor(x => x.RefreshToken).NotEmpty().MaximumLength(512);
}

public sealed class LogoutRequestValidator : AbstractValidator<LogoutRequest>
{
    public LogoutRequestValidator() => RuleFor(x => x.RefreshToken).NotEmpty().MaximumLength(512);
}
