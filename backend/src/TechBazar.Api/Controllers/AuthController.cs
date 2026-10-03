using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TechBazar.Api.Extensions;
using TechBazar.Application.Auth;
using TechBazar.Application.Common;

namespace TechBazar.Api.Controllers;

/// <summary>
/// Two ways to carry the rotating refresh token:
/// <list type="bullet">
/// <item><b>Cookie mode</b> (web storefront, header <c>X-Refresh-Mode: cookie</c>): the token travels only in an <c>HttpOnly; SameSite=Strict</c>
/// cookie scoped to <c>/api/v1/auth</c>, so a script injected into the page (XSS) can never read it. The JSON body then carries <c>refreshToken: null</c>.
/// The custom header also makes cross-site forgery impossible (it forces a CORS pre-flight that is not granted).</item>
/// <item><b>Body mode</b> (other API clients): the token is in the JSON body as before.</item>
/// </list>
/// </summary>
[EnableRateLimiting(Policies.AuthRateLimit)]
public sealed class AuthController(IAuthService auth, IAccountService account, IAccountJobs jobs, IConfiguration config) : ApiControllerBase
{
    public const string CookieName = "tb_rt";
    public const string ModeHeader = "X-Refresh-Mode";
    private const string CookiePath = "/api/v1/auth";

    private string? Ip => HttpContext.Connection.RemoteIpAddress?.ToString();
    private bool CookieMode => string.Equals(Request.Headers[ModeHeader], "cookie", StringComparison.OrdinalIgnoreCase);

    [HttpPost("register")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Register(RegisterRequest request, CancellationToken ct)
    {
        var result = Deliver(await auth.RegisterAsync(request, Ip, ct));
        return Created("/api/v1/auth/me", result);
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest request, CancellationToken ct) =>
        Ok(Deliver(await auth.LoginAsync(request, Ip, ct)));

    /// <summary>
    /// Exchange a refresh token (body, or the HttpOnly cookie in cookie mode) for a new access + refresh token pair; the old refresh token is revoked.
    /// </summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthResponse>> Refresh(RefreshRequest? request, CancellationToken ct)
    {
        var token = request?.RefreshToken;
        // The cookie is only honoured together with the custom header (CSRF defence in depth on top of SameSite=Strict).
        if (string.IsNullOrWhiteSpace(token) && CookieMode) token = Request.Cookies[CookieName];
        try
        {
            return Ok(Deliver(await auth.RefreshAsync(new RefreshRequest(token), Ip, ct)));
        }
        catch (AuthenticationFailedException ex)
        {
            // A dead cookie must not keep being replayed. Answered here (not rethrown): the exception handler resets the response,
            // which would drop the Set-Cookie header that clears it.
            if (CookieMode) ClearCookie();
            return Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Authentication failed.", detail: ex.Message);
        }
    }

    /// <summary>
    /// Revokes the given refresh token (body or cookie). Works without an access token, because it has usually expired by the time the
    /// user clicks "Logout"; the unguessable refresh token itself is the credential. Idempotent: always 204.
    /// </summary>
    [HttpPost("logout")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout(LogoutRequest? request, CancellationToken ct)
    {
        var token = request?.RefreshToken;
        if (string.IsNullOrWhiteSpace(token) && CookieMode) token = Request.Cookies[CookieName];
        if (!string.IsNullOrWhiteSpace(token)) await auth.LogoutAsync(token, ct);
        if (CookieMode) ClearCookie();
        return NoContent();
    }

    /// <summary>Signs the user out of every device: all live refresh tokens are revoked (access tokens expire within minutes).</summary>
    [HttpPost("logout-all")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> LogoutAll(CancellationToken ct)
    {
        await auth.LogoutAllAsync(CurrentUserId, ct);
        if (CookieMode) ClearCookie();
        return NoContent();
    }

    // ------------------------------------------------------------------ account recovery & verification
    private static readonly MessageDto ResetRequested = new("If an account exists for that email address, we have sent a link to reset the password.");

    /// <summary>Always 202 with the same text, whether or not the address has an account (no account enumeration).</summary>
    [HttpPost("forgot-password")]
    [AllowAnonymous]
    [EnableRateLimiting(Policies.RecoveryRateLimit)]
    [ProducesResponseType(typeof(MessageDto), StatusCodes.Status202Accepted)]
    public IActionResult ForgotPassword(ForgotPasswordRequest request)
    {
        // Same work for every address (validate + enqueue): the lookup, token and mail happen in the background worker, so response time
        // cannot reveal whether the account exists.
        jobs.QueuePasswordReset(request.Email, Ip);
        return Accepted(ResetRequested);
    }

    /// <summary>Spends the emailed one-time token and sets a new password. Ends every session of the account.</summary>
    [HttpPost("reset-password")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest request, CancellationToken ct)
    {
        await account.ResetPasswordAsync(request.Token, request.NewPassword, Ip, ct);
        ClearCookie();   // the browser that completed the reset has no session worth keeping either
        return NoContent();
    }

    /// <summary>Confirms the address behind an emailed link. A POST (never a GET): mail scanners and link prefetchers must not be able to spend the token.</summary>
    [HttpPost("verify-email")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> VerifyEmail(VerifyEmailRequest request, CancellationToken ct)
    {
        await account.VerifyEmailAsync(request.Token, ct);
        return NoContent();
    }

    /// <summary>Sends a fresh verification link to the signed-in user (no-op when already verified; throttled per account and per client).</summary>
    [HttpPost("resend-verification")]
    [Authorize]
    [EnableRateLimiting(Policies.RecoveryRateLimit)]
    [ProducesResponseType(typeof(MessageDto), StatusCodes.Status202Accepted)]
    public async Task<IActionResult> ResendVerification(CancellationToken ct)
    {
        await account.SendVerificationAsync(CurrentUserId, Ip, ct);
        return Accepted(new MessageDto("If your email address is not verified yet, we have sent a new link."));
    }

    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<UserDto>> Me(CancellationToken ct) => Ok(await auth.GetProfileAsync(CurrentUserId, ct));

    [HttpPut("me")]
    [Authorize]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<UserDto>> UpdateMe(UpdateProfileRequest request, CancellationToken ct) =>
        Ok(await auth.UpdateProfileAsync(CurrentUserId, request, ct));

    private Guid CurrentUserId =>
        Guid.TryParse(User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var id) ? id : throw new AuthenticationFailedException("Invalid token.");

    /// <summary>In cookie mode moves the refresh token out of the JSON body into the HttpOnly cookie.</summary>
    private AuthResponse Deliver(AuthResponse response)
    {
        if (!CookieMode || response.RefreshToken is null) return response;
        Response.Cookies.Append(CookieName, response.RefreshToken, CookieOptions(response.RefreshTokenExpiresAt));
        return response with { RefreshToken = null };
    }

    private void ClearCookie() => Response.Cookies.Delete(CookieName, CookieOptions(null));

    private CookieOptions CookieOptions(DateTime? expiresUtc) => new()
    {
        HttpOnly = true,
        SameSite = SameSiteMode.Strict,
        Path = CookiePath,
        // "Auto" follows the (forwarded) request scheme; set Auth:RefreshCookie:Secure=Always in production behind TLS.
        Secure = string.Equals(config["Auth:RefreshCookie:Secure"], "Always", StringComparison.OrdinalIgnoreCase) || Request.IsHttps,
        Expires = expiresUtc is { } e ? new DateTimeOffset(DateTime.SpecifyKind(e, DateTimeKind.Utc)) : null,
        IsEssential = true,
    };
}
