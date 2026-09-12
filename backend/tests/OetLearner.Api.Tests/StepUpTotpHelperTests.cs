using OetLearner.Api.Services;
using OetLearner.Api.Tests.Infrastructure;
using Xunit;

namespace OetLearner.Api.Tests;

/// <summary>
/// Guards the test-side TOTP generator against drift from the server's verifier.
/// If these ever disagree, every step-up gated admin test fails with an opaque
/// "invalid code" and the cause is not obvious.
/// </summary>
public class StepUpTotpHelperTests
{
    [Fact]
    public void GeneratedCode_VerifiesAgainstServerTotp()
    {
        var secret = AuthenticatorTotp.GenerateSecretKey();
        var now = DateTimeOffset.UtcNow;
        var code = TestWebApplicationFactory.GenerateTotpCode(secret, now);
        Assert.True(AuthenticatorTotp.VerifyCode(secret, code, now, 1));
    }

    [Fact]
    public void GeneratedCode_IsSixDigits()
    {
        var secret = AuthenticatorTotp.GenerateSecretKey();
        var code = TestWebApplicationFactory.GenerateTotpCode(secret, DateTimeOffset.UtcNow);
        Assert.Equal(6, code.Length);
        Assert.All(code, character => Assert.True(char.IsDigit(character)));
    }
}
