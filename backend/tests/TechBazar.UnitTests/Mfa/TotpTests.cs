using System.Security.Cryptography;
using System.Text;
using TechBazar.Application.Mfa;
using TechBazar.Infrastructure.Identity;

namespace TechBazar.UnitTests.Mfa;

public class TotpTests
{
    // RFC 4226 appendix D / RFC 6238 appendix B share this ASCII secret.
    private static readonly byte[] Rfc = Encoding.ASCII.GetBytes("12345678901234567890");

    [Theory]   // RFC 4226 Appendix D: HOTP values for counters 0..9
    [InlineData(0, "755224")]
    [InlineData(1, "287082")]
    [InlineData(2, "359152")]
    [InlineData(3, "969429")]
    [InlineData(4, "338314")]
    [InlineData(5, "254676")]
    [InlineData(6, "287922")]
    [InlineData(7, "162583")]
    [InlineData(8, "399871")]
    [InlineData(9, "520489")]
    public void Hotp_MatchesRfc4226(long counter, string expected) => Assert.Equal(expected, Totp.Compute(Rfc, counter));

    [Theory]   // RFC 6238 Appendix B, SHA-1, 8 digits
    [InlineData(59L, "94287082")]
    [InlineData(1111111109L, "07081804")]
    [InlineData(1111111111L, "14050471")]
    [InlineData(1234567890L, "89005924")]
    [InlineData(2000000000L, "69279037")]
    [InlineData(20000000000L, "65353130")]
    public void Totp_MatchesRfc6238(long unixSeconds, string expected) =>
        Assert.Equal(expected, Totp.Compute(Rfc, Totp.StepAt(DateTimeOffset.FromUnixTimeSeconds(unixSeconds)), 8));

    [Fact]
    public void Verify_AcceptsThePreviousCurrentAndNextStep_ButNothingFurther()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1_700_000_010);
        var step = Totp.StepAt(now);
        foreach (var delta in new long[] { -1, 0, 1 })
            Assert.Equal(step + delta, Totp.Verify(Rfc, Totp.Compute(Rfc, step + delta), now));
        foreach (var delta in new long[] { -3, -2, 2, 3 })
            Assert.Null(Totp.Verify(Rfc, Totp.Compute(Rfc, step + delta), now));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("1234567")]
    [InlineData("12345a")]
    [InlineData("１２３４５６")]
    [InlineData("123 45")]
    public void Verify_RejectsMalformedInput(string? code) =>
        Assert.Null(Totp.Verify(Rfc, code, DateTimeOffset.FromUnixTimeSeconds(1_700_000_010)));

    [Fact]
    public void Verify_ToleratesTheSpaceOrHyphenAuthenticatorAppsShow()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1_700_000_010);
        var code = Totp.Compute(Rfc, Totp.StepAt(now));
        Assert.NotNull(Totp.Verify(Rfc, code[..3] + " " + code[3..], now));
        Assert.NotNull(Totp.Verify(Rfc, code[..3] + "-" + code[3..], now));
    }

    [Fact]
    public void Verify_ALeadingZeroCodeKeepsItsZeros()
    {
        // step whose code starts with 0 (RFC vector 07081804 -> 6 digits "081804")
        var step = Totp.StepAt(DateTimeOffset.FromUnixTimeSeconds(1111111109));
        Assert.Equal("081804", Totp.Compute(Rfc, step));
        Assert.Equal(step, Totp.Verify(Rfc, "081804", DateTimeOffset.FromUnixTimeSeconds(1111111109)));
    }

    [Fact]
    public void NewSecret_Is160BitsAndNeverRepeats()
    {
        var a = Totp.NewSecret();
        Assert.Equal(20, a.Length);
        Assert.NotEqual(a, Totp.NewSecret());
    }

    [Fact]
    public void ProvisioningUri_IsWhatAuthenticatorAppsExpect()
    {
        var uri = Totp.ProvisioningUri("TechBazar BD", "a+b@example.com", Rfc);
        Assert.StartsWith("otpauth://totp/TechBazar%20BD:a%2Bb%40example.com?", uri);
        Assert.Contains("secret=GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ", uri);   // base32 of the RFC secret
        Assert.Contains("issuer=TechBazar%20BD", uri);
        Assert.Contains("algorithm=SHA1&digits=6&period=30", uri);
    }
}

public class Base32Tests
{
    [Theory]   // RFC 4648 section 10 (padding omitted)
    [InlineData("", "")]
    [InlineData("f", "MY")]
    [InlineData("fo", "MZXQ")]
    [InlineData("foo", "MZXW6")]
    [InlineData("foob", "MZXW6YQ")]
    [InlineData("fooba", "MZXW6YTB")]
    [InlineData("foobar", "MZXW6YTBOI")]
    public void Encode_MatchesRfc4648(string plain, string expected) => Assert.Equal(expected, Base32.Encode(Encoding.ASCII.GetBytes(plain)));

    [Theory]
    [InlineData("MZXW6YTBOI", "foobar")]
    [InlineData("mzxw6ytboi", "foobar")]
    [InlineData("MZXW 6YTB-OI======", "foobar")]
    public void Decode_IsLenientAboutCaseSpacesAndPadding(string text, string plain)
    {
        Assert.True(Base32.TryDecode(text, out var bytes));
        Assert.Equal(plain, Encoding.ASCII.GetString(bytes));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("MZXW1")]
    [InlineData("MZ!W6")]
    public void Decode_RejectsGarbage(string? text) => Assert.False(Base32.TryDecode(text, out _));

    [Fact]
    public void RoundTrips_RandomSecrets()
    {
        for (var i = 0; i < 200; i++)
        {
            var secret = RandomNumberGenerator.GetBytes(1 + i % 40);
            Assert.True(Base32.TryDecode(Base32.Encode(secret), out var back));
            Assert.Equal(secret, back);
        }
    }
}

public class MfaCryptoTests
{
    private static MfaCrypto New(byte fill = 7) => new(Enumerable.Repeat(fill, 32).ToArray());
    private const string UserA = "11111111-1111-1111-1111-111111111111";
    private const string UserB = "22222222-2222-2222-2222-222222222222";

    [Fact]
    public void ProtectThenUnprotect_RoundTrips_AndTheCiphertextHidesThePlaintext()
    {
        var crypto = New();
        var secret = Totp.NewSecret();
        var stored = crypto.Protect(secret, UserA);
        Assert.Equal(secret, crypto.Unprotect(stored, UserA));
        Assert.DoesNotContain(Base32.Encode(secret), stored);
        Assert.DoesNotContain(Convert.ToBase64String(secret), stored);
        Assert.True(stored.Length <= 200, "must fit the column");
    }

    [Fact]
    public void EncryptionIsRandomised() =>
        Assert.NotEqual(New().Protect(Rfc(), UserA), New().Protect(Rfc(), UserA));

    [Fact]
    public void ACiphertextCopiedToAnotherUsersRowDoesNotDecrypt() =>
        Assert.ThrowsAny<CryptographicException>(() => New().Unprotect(New().Protect(Rfc(), UserA), UserB));

    [Fact]
    public void AnyBitFlipIsDetected()
    {
        var crypto = New();
        var raw = Convert.FromBase64String(crypto.Protect(Rfc(), UserA));
        for (var i = 0; i < raw.Length; i++)
        {
            var copy = (byte[])raw.Clone();
            copy[i] ^= 1;
            Assert.ThrowsAny<CryptographicException>(() => crypto.Unprotect(Convert.ToBase64String(copy), UserA));
        }
    }

    [Fact]
    public void AnotherMasterKeyCannotDecrypt() =>
        Assert.ThrowsAny<CryptographicException>(() => New(8).Unprotect(New(7).Protect(Rfc(), UserA), UserA));

    [Fact]
    public void Fingerprint_IsKeyedAndUserBound()
    {
        var a = New().Fingerprint("ABCDEFGHJK", UserA);
        Assert.Equal(64, a.Length);
        Assert.Equal(a, New().Fingerprint("ABCDEFGHJK", UserA));
        Assert.NotEqual(a, New().Fingerprint("ABCDEFGHJK", UserB));    // same code, other user
        Assert.NotEqual(a, New(8).Fingerprint("ABCDEFGHJK", UserA));   // other key: a database thief cannot precompute
        Assert.NotEqual(a, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("ABCDEFGHJK"))));   // not a bare hash
    }

    [Fact]
    public void ShortMasterKeysAreRefused() => Assert.Throws<ArgumentException>(() => new MfaCrypto(new byte[31]));

    [Fact]
    public void FromOptions_RequiresAKeyOutsideDevelopment_AndValidatesIt()
    {
        var good = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        Assert.NotNull(MfaCrypto.FromOptions(new MfaOptions { SecretKey = good }, "jwt", allowDerived: false));
        Assert.Throws<InvalidOperationException>(() => MfaCrypto.FromOptions(new MfaOptions(), "jwt", allowDerived: false));
        Assert.Throws<InvalidOperationException>(() => MfaCrypto.FromOptions(new MfaOptions { SecretKey = "not base64!!" }, "jwt", allowDerived: true));
        Assert.Throws<InvalidOperationException>(() => MfaCrypto.FromOptions(new MfaOptions { SecretKey = Convert.ToBase64String(new byte[16]) }, "jwt", allowDerived: true));
        Assert.NotNull(MfaCrypto.FromOptions(new MfaOptions(), "jwt-key-0123456789", allowDerived: true));   // Development fallback only
    }

    private static byte[] Rfc() => Encoding.ASCII.GetBytes("12345678901234567890");
}
