using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace TechBazar.Infrastructure.Identity;

public sealed record AccessToken(string Value, DateTime ExpiresAt);

public interface ITokenService
{
    AccessToken CreateAccessToken(ApplicationUser user, IEnumerable<string> roles);
    /// <summary>Returns the opaque token to hand to the client and its SHA-256 hash to persist.</summary>
    (string Token, string Hash) CreateRefreshToken();
    string Hash(string token);
}

public sealed class TokenService(IOptions<JwtOptions> options) : ITokenService
{
    private readonly JwtOptions _o = options.Value;

    public AccessToken CreateAccessToken(ApplicationUser user, IEnumerable<string> roles)
    {
        var expires = DateTime.UtcNow.AddMinutes(_o.AccessTokenMinutes);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email!),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new("name", user.FullName),
        };
        claims.AddRange(roles.Select(r => new Claim("role", r)));

        var creds = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_o.Key)), SecurityAlgorithms.HmacSha256);
        var jwt = new JwtSecurityToken(_o.Issuer, _o.Audience, claims, notBefore: DateTime.UtcNow, expires: expires, signingCredentials: creds);
        return new AccessToken(new JwtSecurityTokenHandler().WriteToken(jwt), expires);
    }

    public (string Token, string Hash) CreateRefreshToken()
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        return (token, Hash(token));
    }

    public string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
