namespace TechBazar.Application.Common;

public class NotFoundException(string message) : Exception(message);

public class ConflictException(string message) : Exception(message);

/// <summary>Bad credentials / invalid or expired refresh token (maps to HTTP 401).</summary>
public class AuthenticationFailedException(string message) : Exception(message);

/// <summary>An emailed link (verify email / reset password) is unknown, expired, already used or for another purpose. Deliberately not more specific.</summary>
public class InvalidTokenException() : Exception("This link is invalid or has expired.");

/// <summary>The action needs a verified email address (see <c>Account:RequireVerifiedEmailForCheckout</c>).</summary>
public class EmailNotVerifiedException() : Exception("Please verify your email address to continue.");

/// <summary>An action is understood but refused (403) with a machine-readable <see cref="Code"/> the UI can react to.</summary>
public class ForbiddenException(string message, string code) : Exception(message)
{
    public string Code { get; } = code;
}
