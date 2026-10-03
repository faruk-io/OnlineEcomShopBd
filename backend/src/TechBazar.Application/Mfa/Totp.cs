using System.Security.Cryptography;
using System.Text;

namespace TechBazar.Application.Mfa;

/// <summary>RFC 4648 Base32 (the encoding authenticator apps use for secrets): upper-case, no padding on output, lenient on input.</summary>
public static class Base32
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static string Encode(ReadOnlySpan<byte> data)
    {
        var sb = new StringBuilder((data.Length * 8 + 4) / 5);
        int buffer = 0, bits = 0;
        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5) { sb.Append(Alphabet[(buffer >> (bits - 5)) & 31]); bits -= 5; }
        }
        if (bits > 0) sb.Append(Alphabet[(buffer << (5 - bits)) & 31]);
        return sb.ToString();
    }

    /// <summary>Ignores case, spaces, hyphens and '=' padding. Returns false for any other character.</summary>
    public static bool TryDecode(string? text, out byte[] bytes)
    {
        bytes = [];
        if (string.IsNullOrEmpty(text)) return false;
        var output = new List<byte>(text.Length * 5 / 8);
        int buffer = 0, bits = 0;
        foreach (var raw in text)
        {
            if (raw is ' ' or '-' or '=') continue;
            var i = Alphabet.IndexOf(char.ToUpperInvariant(raw));
            if (i < 0) return false;
            buffer = (buffer << 5) | i;
            bits += 5;
            if (bits >= 8) { output.Add((byte)((buffer >> (bits - 8)) & 0xFF)); bits -= 8; }
        }
        bytes = [.. output];
        return bytes.Length > 0;
    }
}

/// <summary>
/// Time-based one-time passwords per RFC 6238 (HMAC-SHA1, 6 digits, 30 s step) - what Google Authenticator, Microsoft Authenticator, Authy,
/// 1Password etc. implement. Verification returns the matched time step so the caller can refuse to accept the same step twice (replay).
/// </summary>
public static class Totp
{
    public const int StepSeconds = 30;
    public const int Digits = 6;
    /// <summary>Accept the previous, current and next step: tolerates ~30 s of clock drift, no more.</summary>
    public const int DefaultWindow = 1;

    public static long StepAt(DateTimeOffset time) => time.ToUnixTimeSeconds() / StepSeconds;

    public static byte[] NewSecret() => RandomNumberGenerator.GetBytes(20);   // 160 bits, the RFC 4226 recommendation

    /// <summary>HOTP value (RFC 4226) for the counter, left-padded with zeros to <paramref name="digits"/>.</summary>
    public static string Compute(byte[] secret, long step, int digits = Digits)
    {
        Span<byte> counter = stackalloc byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(counter, step);
        Span<byte> hash = stackalloc byte[20];
        HMACSHA1.HashData(secret, counter, hash);
        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        var mod = 1;
        for (var i = 0; i < digits; i++) mod *= 10;
        return (binary % mod).ToString().PadLeft(digits, '0');
    }

    /// <summary>"123 456" / "123-456" -> "123456"; null when it is not exactly six digits.</summary>
    public static string? Normalise(string? input)
    {
        if (input is null) return null;
        var digits = new string(input.Where(c => c is not (' ' or '-')).ToArray());
        return digits.Length == Digits && digits.All(char.IsAsciiDigit) ? digits : null;
    }

    /// <summary>The matching time step, or null. Every candidate step is evaluated and compared in constant time (no early exit).</summary>
    public static long? Verify(byte[] secret, string? code, DateTimeOffset now, int window = DefaultWindow)
    {
        var normalised = Normalise(code);
        if (normalised is null) return null;
        var given = Encoding.ASCII.GetBytes(normalised);
        var current = StepAt(now);
        long? match = null;
        for (var delta = -window; delta <= window; delta++)
        {
            var step = current + delta;
            var expected = Encoding.ASCII.GetBytes(Compute(secret, step));
            if (CryptographicOperations.FixedTimeEquals(given, expected)) match = step;
        }
        return match;
    }

    /// <summary><c>otpauth://</c> URI the authenticator app turns into an account (also what the QR code encodes).</summary>
    public static string ProvisioningUri(string issuer, string account, byte[] secret) =>
        $"otpauth://totp/{Uri.EscapeDataString(issuer)}:{Uri.EscapeDataString(account)}" +
        $"?secret={Base32.Encode(secret)}&issuer={Uri.EscapeDataString(issuer)}&algorithm=SHA1&digits={Digits}&period={StepSeconds}";
}
