using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using TechBazar.Application.Mfa;

namespace TechBazar.Infrastructure.Identity;

/// <summary>
/// AES-256-GCM for authenticator secrets and HMAC-SHA256 for recovery codes. Two independent sub-keys are derived (HKDF-SHA256) from the
/// application-held master key, which is NOT in the database: a stolen database yields neither usable TOTP secrets nor checkable recovery codes.
/// Stored form: base64(nonce[12] | tag[16] | ciphertext). The user id is authenticated data, so a ciphertext copied to another row fails to decrypt.
/// </summary>
public sealed class MfaCrypto : IMfaCrypto
{
    private readonly byte[] _encKey;
    private readonly byte[] _macKey;

    public MfaCrypto(byte[] masterKey)
    {
        if (masterKey.Length < 32) throw new ArgumentException("The MFA master key must be at least 32 bytes.", nameof(masterKey));
        _encKey = HKDF.DeriveKey(HashAlgorithmName.SHA256, masterKey, 32, info: "techbazar/mfa/enc/v1"u8.ToArray());
        _macKey = HKDF.DeriveKey(HashAlgorithmName.SHA256, masterKey, 32, info: "techbazar/mfa/mac/v1"u8.ToArray());
    }

    /// <summary>Master key from <c>Mfa:SecretKey</c> (base64, >= 32 bytes). Only when <paramref name="allowDerived"/> (Development) a throw-away key is derived from the JWT key.</summary>
    public static MfaCrypto FromOptions(MfaOptions options, string? jwtKey, bool allowDerived)
    {
        if (!string.IsNullOrWhiteSpace(options.SecretKey))
        {
            byte[] bytes;
            try { bytes = Convert.FromBase64String(options.SecretKey.Trim()); }
            catch (FormatException) { throw new InvalidOperationException("Mfa:SecretKey must be base64 (generate one with: openssl rand -base64 32)."); }
            if (bytes.Length < 32) throw new InvalidOperationException("Mfa:SecretKey must decode to at least 32 bytes (openssl rand -base64 32).");
            return new MfaCrypto(bytes);
        }
        if (allowDerived && !string.IsNullOrEmpty(jwtKey))
            return new MfaCrypto(SHA256.HashData(Encoding.UTF8.GetBytes("techbazar/mfa/dev-fallback|" + jwtKey)));
        throw new InvalidOperationException("Mfa:SecretKey is not configured. Set it via user-secrets / an environment variable (Mfa__SecretKey): openssl rand -base64 32.");
    }

    public string Protect(byte[] plaintext, string context)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var tag = new byte[16];
        var cipher = new byte[plaintext.Length];
        using var aes = new AesGcm(_encKey, 16);
        aes.Encrypt(nonce, plaintext, cipher, tag, Encoding.UTF8.GetBytes(context));
        return Convert.ToBase64String([.. nonce, .. tag, .. cipher]);
    }

    public byte[] Unprotect(string protectedValue, string context)
    {
        var all = Convert.FromBase64String(protectedValue);
        if (all.Length < 28) throw new CryptographicException("Malformed MFA secret.");
        var plain = new byte[all.Length - 28];
        using var aes = new AesGcm(_encKey, 16);
        aes.Decrypt(all.AsSpan(0, 12), all.AsSpan(28), all.AsSpan(12, 16), plain, Encoding.UTF8.GetBytes(context));
        return plain;
    }

    public string Fingerprint(string normalisedRecoveryCode, string userContext) =>
        Convert.ToHexString(HMACSHA256.HashData(_macKey, Encoding.UTF8.GetBytes(userContext + "|" + normalisedRecoveryCode)));
}
