using OetLearner.Api.Services.Listening;

namespace OetLearner.Api.Tests.Listening;

public sealed class ListeningTtsProviderPolicyTests
{
    [Fact]
    public void Production_rejects_the_silence_stub()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            ListeningTtsProviderPolicy.EnsureAllowedForEnvironment("stub", isProduction: true));

        Assert.Contains("forbidden in production", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Development_can_use_the_stub_for_local_pipeline_checks()
    {
        var exception = Record.Exception(() =>
            ListeningTtsProviderPolicy.EnsureAllowedForEnvironment("stub", isProduction: false));

        Assert.Null(exception);
    }

    [Theory]
    [InlineData(null, "stub")]
    [InlineData(" ElevenLabs ", "elevenlabs")]
    public void Provider_names_are_normalized(string? configured, string expected)
    {
        Assert.Equal(expected, ListeningTtsProviderPolicy.Normalize(configured));
    }
}
