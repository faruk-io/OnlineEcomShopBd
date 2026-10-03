using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.OutputCaching;

namespace TechBazar.Api.Controllers;

public static class ClaimsExtensions
{
    public static Guid UserId(this ClaimsPrincipal user) => Guid.Parse(user.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);
    public static Guid? UserIdOrNull(this ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var id) ? id : null;
    public static string Email(this ClaimsPrincipal user) => user.FindFirst(JwtRegisteredClaimNames.Email)!.Value;
}

/// <summary>Drops cached catalog pages after anything that changes prices, stock or visibility (60 s caches would show stale stock).</summary>
public sealed class CatalogCache(IOutputCacheStore store)
{
    public ValueTask InvalidateAsync(CancellationToken ct = default) => store.EvictByTagAsync("catalog", ct);
}
