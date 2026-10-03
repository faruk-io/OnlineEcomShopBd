namespace TechBazar.Domain.Entities;

/// <summary>A user's authenticator (TOTP) secret. Pending until <see cref="ConfirmedAt"/> is set by presenting a valid first code.</summary>
public class MfaCredential
{
    public int Id { get; set; }
    public Guid UserId { get; set; }
    /// <summary>AES-GCM ciphertext of the 160-bit secret (key held outside the database).</summary>
    public string EncryptedSecret { get; set; } = default!;
    public DateTime CreatedAt { get; set; }
    public DateTime? ConfirmedAt { get; set; }
    /// <summary>Highest TOTP time step already accepted: a code can be used once, even inside its validity window (replay protection).</summary>
    public long LastUsedStep { get; set; }
}

/// <summary>One single-use recovery code. Only a keyed hash (HMAC) is stored.</summary>
public class MfaRecoveryCode
{
    public int Id { get; set; }
    public Guid UserId { get; set; }
    public string CodeHash { get; set; } = default!;
    public DateTime CreatedAt { get; set; }
    public DateTime? UsedAt { get; set; }
}
