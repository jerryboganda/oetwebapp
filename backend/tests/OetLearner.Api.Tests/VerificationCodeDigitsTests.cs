using OetLearner.Api.Services;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests;

/// <summary>
/// Universal OTP compatibility (owner brief, 15 Sep 2026).
///
/// A learner on an Arabic keyboard types Arabic-Indic digits. Those satisfy
/// <c>char.IsDigit</c>, so every length/shape guard in the auth stack accepts
/// them — and then the ASCII comparison in <see cref="AuthenticatorTotp"/> and
/// the SHA-256 payload in <c>EmailOtpService.HashOtp</c> see different bytes and
/// the correct code is rejected. These pin the normalization that closes it.
/// </summary>
public class VerificationCodeDigitsTests
{
    [Fact]
    public void AsciiDigitsPassThroughUnchanged()
    {
        Assert.Equal("123456", VerificationCodeDigits.Normalize("123456"));
    }

    [Fact]
    public void ArabicIndicDigitsNormalizeToAscii()
    {
        Assert.Equal("0123456789", VerificationCodeDigits.Normalize("\u0660\u0661\u0662\u0663\u0664\u0665\u0666\u0667\u0668\u0669"));
    }

    [Fact]
    public void ExtendedArabicIndicDigitsNormalizeToAscii()
    {
        Assert.Equal("0123456789", VerificationCodeDigits.Normalize("\u06F0\u06F1\u06F2\u06F3\u06F4\u06F5\u06F6\u06F7\u06F8\u06F9"));
    }

    [Fact]
    public void MixedScriptCodeNormalizes()
    {
        Assert.Equal("123456", VerificationCodeDigits.Normalize("12\u0663\u0664\u06F5\u06F6"));
    }

    [Fact]
    public void SurroundingWhitespaceIsTrimmed()
    {
        Assert.Equal("123456", VerificationCodeDigits.Normalize("  123456\t"));
    }

    [Fact]
    public void NullAndEmptyAreSafe()
    {
        Assert.Equal(string.Empty, VerificationCodeDigits.Normalize(null));
        Assert.Equal(string.Empty, VerificationCodeDigits.Normalize(""));
        Assert.Equal(string.Empty, VerificationCodeDigits.Normalize("   "));
    }

    [Fact]
    public void NonDigitCharactersAreMappedNotStripped()
    {
        // This normalizer maps digits; it must not silently delete anything,
        // so it stays safe for any future non-numeric code shape.
        Assert.Equal("AB-12", VerificationCodeDigits.Normalize("AB-\u0661\u0662"));
    }

    [Fact]
    public void TotpAcceptsACodeTypedOnAnArabicKeyboard()
    {
        var secret = AuthenticatorTotp.GenerateSecretKey();
        var now = DateTimeOffset.UtcNow;
        var asciiCode = TestWebApplicationFactory.GenerateTotpCode(secret, now);

        var arabicCode = new string(asciiCode.Select(c => (char)('\u0660' + (c - '0'))).ToArray());
        Assert.NotEqual(asciiCode, arabicCode);

        Assert.True(AuthenticatorTotp.VerifyCode(secret, asciiCode, now));
        Assert.True(AuthenticatorTotp.VerifyCode(secret, arabicCode, now));
    }

    [Fact]
    public void TotpStillRejectsAWrongCode()
    {
        var secret = AuthenticatorTotp.GenerateSecretKey();
        var now = DateTimeOffset.UtcNow;
        Assert.False(AuthenticatorTotp.VerifyCode(secret, "000000", now.AddMinutes(45)));
        Assert.False(AuthenticatorTotp.VerifyCode(secret, "abcdef", now));
        Assert.False(AuthenticatorTotp.VerifyCode(secret, "12345", now));
    }
}
