using FluentValidation;

namespace TechBazar.Application.Auth;

public sealed record ForgotPasswordRequest(string Email);
public sealed record ResetPasswordRequest(string Token, string NewPassword);
public sealed record VerifyEmailRequest(string Token);
public sealed record MessageDto(string Message);

public sealed class ForgotPasswordRequestValidator : AbstractValidator<ForgotPasswordRequest>
{
    public ForgotPasswordRequestValidator() => RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
}

public sealed class ResetPasswordRequestValidator : AbstractValidator<ResetPasswordRequest>
{
    public ResetPasswordRequestValidator()
    {
        RuleFor(x => x.Token).NotEmpty().MaximumLength(128);
        RuleFor(x => x.NewPassword).MustBeAStrongPassword();
    }
}

public sealed class VerifyEmailRequestValidator : AbstractValidator<VerifyEmailRequest>
{
    public VerifyEmailRequestValidator() => RuleFor(x => x.Token).NotEmpty().MaximumLength(128);
}

/// <summary>Bound from the "Account" configuration section.</summary>
public sealed class AccountOptions
{
    public const string SectionName = "Account";

    /// <summary>
    /// Origin of the storefront used to build emailed links. Always configuration, NEVER the request's Host header
    /// (that is the classic password-reset poisoning attack). Falls back to <c>Payments:StorefrontBaseUrl</c>.
    /// </summary>
    public string? StorefrontBaseUrl { get; set; }
    public int ResetTokenMinutes { get; set; } = 60;
    public int VerifyTokenHours { get; set; } = 24;
    /// <summary>Emails of one kind per account per hour; further requests are answered identically but silently ignored.</summary>
    public int MaxRequestsPerHour { get; set; } = 3;
    /// <summary>When true, placing an order needs a verified email address (403 <c>email_not_verified</c> otherwise).</summary>
    public bool RequireVerifiedEmailForCheckout { get; set; }
}

/// <summary>
/// Fire-and-forget entry point for "forgot password". The request path only validates and enqueues, so the HTTP response takes the same time
/// whether or not the address has an account (measured: doing the lookup, token insert and mail send inline made known addresses ~3x slower,
/// a timing side channel for account enumeration) and SMTP latency never blocks a request.
/// </summary>
public interface IAccountJobs
{
    void QueuePasswordReset(string email, string? ipAddress);
}

public interface IAccountService
{
    /// <summary>Emails a reset link when the address belongs to an active account. Always completes silently: callers cannot tell whether it did.</summary>
    Task RequestPasswordResetAsync(string email, string? ipAddress, CancellationToken ct = default);

    /// <summary>
    /// Spends the one-time token and sets the new password; revokes every session of the account, clears lockout, marks the email verified
    /// (the user proved control of the mailbox) and sends a "password changed" notice. Throws <see cref="Common.InvalidTokenException"/>
    /// for unknown / expired / used / wrong-purpose tokens, and a validation error (token NOT spent) for a weak password.
    /// </summary>
    Task ResetPasswordAsync(string token, string newPassword, string? ipAddress, CancellationToken ct = default);

    /// <summary>Marks the account's email as verified. Idempotent for an already verified account, still single-use for the token.</summary>
    Task VerifyEmailAsync(string token, CancellationToken ct = default);

    /// <summary>Sends a (new) verification link to a signed-in, not yet verified user. Throttled; a no-op for verified accounts.</summary>
    Task SendVerificationAsync(Guid userId, string? ipAddress, CancellationToken ct = default);

    /// <summary>Throws <see cref="Common.EmailNotVerifiedException"/> when <c>RequireVerifiedEmailForCheckout</c> is on and the user is unverified.</summary>
    Task EnsureCanCheckoutAsync(Guid userId, CancellationToken ct = default);
}
