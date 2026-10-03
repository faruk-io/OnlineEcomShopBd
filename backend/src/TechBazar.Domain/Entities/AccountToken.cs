using TechBazar.Domain.Enums;

namespace TechBazar.Domain.Entities;

/// <summary>
/// One-time token behind an emailed link (verify email / reset password). Only the SHA-256 of the 256-bit random token is stored, so a
/// database leak does not yield usable links. Single use (<see cref="UsedAt"/> is set atomically), short-lived, bound to a purpose and to
/// the email address it was issued for.
/// </summary>
public class AccountToken
{
    public int Id { get; set; }
    public Guid UserId { get; set; }
    public AccountTokenPurpose Purpose { get; set; }
    public string TokenHash { get; set; } = default!;
    /// <summary>Normalised email at issue time: a token never verifies / resets after the address has changed.</summary>
    public string Email { get; set; } = default!;
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    /// <summary>Set when the token was spent OR superseded by a newer request.</summary>
    public DateTime? UsedAt { get; set; }
    public string? CreatedByIp { get; set; }

    public bool IsUsable(DateTime utcNow) => UsedAt is null && utcNow < ExpiresAt;
}
