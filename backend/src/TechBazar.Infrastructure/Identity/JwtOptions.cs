using System.ComponentModel.DataAnnotations;

namespace TechBazar.Infrastructure.Identity;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    [Required] public string Issuer { get; set; } = "TechBazarBD";
    [Required] public string Audience { get; set; } = "TechBazarBD.Clients";
    /// <summary>Symmetric signing key (>= 32 chars). Provide via user-secrets / environment variable, never commit it.</summary>
    [Required, MinLength(32)] public string Key { get; set; } = default!;
    [Range(1, 1440)] public int AccessTokenMinutes { get; set; } = 15;
    [Range(1, 90)] public int RefreshTokenDays { get; set; } = 14;
}
