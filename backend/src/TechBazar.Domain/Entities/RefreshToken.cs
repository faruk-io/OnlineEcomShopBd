namespace TechBazar.Domain.Entities;

/// <summary>Server-side record of an issued refresh token. Only the SHA-256 hash is stored.</summary>
public class RefreshToken
{
    public int Id { get; set; }
    public Guid UserId { get; set; }
    public string TokenHash { get; set; } = default!;
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public string? CreatedByIp { get; set; }
    public DateTime? RevokedAt { get; set; }
    public string? ReplacedByTokenHash { get; set; }
    public string? RevokedReason { get; set; }

    public bool IsRevoked => RevokedAt.HasValue;
    public bool IsExpired(DateTime utcNow) => utcNow >= ExpiresAt;
    public bool IsActive(DateTime utcNow) => !IsRevoked && !IsExpired(utcNow);
}
