using OetLearner.Api.Services;

namespace OetLearner.Api.Tests;

public sealed class DeviceVerificationExemptionTests
{
    [Fact]
    public void AreAnyDeviceVerificationExempt_MatchesLearnerProfileEmailWhenAuthEmailDiffers()
    {
        var result = AuthService.AreAnyDeviceVerificationExempt(
            ["legacy-auth@example.test", "drahmedhesham19951995@gmail.com"],
            "DRAHMEDHESHAM19951995@GMAIL.COM");

        Assert.True(result);
    }

    [Theory]
    [InlineData("drahmedhesham19951995@gmail.com")]
    [InlineData("drahmedhesham9595@gmail.com")]
    [InlineData("tutorcommerceacademy2026@gmail.com")]
    [InlineData("drhagermurad2026@gmail.com")]
    public void IsDeviceVerificationExempt_MatchesConfiguredOwnerSafetyAddresses(string email)
    {
        Assert.True(AuthService.IsDeviceVerificationExempt(email, email));
    }

    [Fact]
    public void IsDeviceVerificationExempt_DoesNotKeepADeletedAddressAsAHiddenDefault()
    {
        Assert.False(AuthService.IsDeviceVerificationExempt("drhagermurad2026@gmail.com", null));
    }
}
