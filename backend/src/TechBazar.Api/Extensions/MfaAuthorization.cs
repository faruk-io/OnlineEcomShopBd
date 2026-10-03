using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.Extensions.Options;
using TechBazar.Application.Mfa;
using TechBazar.Domain.Enums;

namespace TechBazar.Api.Extensions;

/// <summary>The session behind the access token must have passed a second factor (<c>amr</c> contains <c>mfa</c>) when policy demands it.</summary>
public sealed class MfaRequirement : IAuthorizationRequirement;

public sealed class MfaAuthorizationHandler(IOptions<MfaOptions> options) : AuthorizationHandler<MfaRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, MfaRequirement requirement)
    {
        // Not an admin: the role requirement decides (so customers get a plain 403, not an MFA prompt). Enforcement off: nothing to check.
        if (!options.Value.EnforceForAdmins || !context.User.IsInRole(Roles.Admin)) { context.Succeed(requirement); return Task.CompletedTask; }
        if (context.User.HasClaim("amr", "mfa")) context.Succeed(requirement);
        return Task.CompletedTask;
    }
}

/// <summary>An admin without a second-factor session gets 403 <c>code: mfa_required</c> (RFC 7807) so the UI can send them to the security page.</summary>
public sealed class MfaAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _default = new();

    public async Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Forbidden && authorizeResult.AuthorizationFailure?.FailedRequirements.OfType<MfaRequirement>().Any() == true)
        {
            var problem = new Microsoft.AspNetCore.Mvc.ProblemDetails
            {
                Status = StatusCodes.Status403Forbidden, Title = "Two-step verification required.",
                Detail = "Administrators must sign in with two-step verification. Set it up in your account security page, then sign in again.",
            };
            problem.Extensions["code"] = "mfa_required";
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            var service = context.RequestServices.GetRequiredService<IProblemDetailsService>();
            await service.TryWriteAsync(new ProblemDetailsContext { HttpContext = context, ProblemDetails = problem });
            return;
        }
        await _default.HandleAsync(next, context, policy, authorizeResult);
    }
}
