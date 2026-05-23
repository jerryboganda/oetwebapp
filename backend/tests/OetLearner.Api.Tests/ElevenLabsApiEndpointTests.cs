using OetLearner.Api.Services;
using OetLearner.Api.Services.Conversation.Tts;

namespace OetLearner.Api.Tests;

public class ElevenLabsApiEndpointTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("https://api.elevenlabs.io/v1")]
    [InlineData("https://api.elevenlabs.io/v1/")]
    public void NormalizeBaseUrlAllowsOnlyOfficialElevenLabsV1Endpoint(string? value)
    {
        var normalized = ElevenLabsApiEndpoint.NormalizeBaseUrl(value);

        Assert.Equal(ElevenLabsApiEndpoint.DefaultBaseUrl, normalized);
    }

    [Theory]
    [InlineData("http://api.elevenlabs.io/v1")]
    [InlineData("https://api.elevenlabs.io:8443/v1")]
    [InlineData("https://evil.example/v1")]
    [InlineData("https://api.elevenlabs.io.evil.example/v1")]
    [InlineData("https://api.elevenlabs.io/v2")]
    public void NormalizeBaseUrlRejectsEndpointsThatCouldReceiveElevenLabsSecrets(string value)
    {
        var ex = Assert.Throws<ApiException>(() => ElevenLabsApiEndpoint.NormalizeBaseUrl(value));

        Assert.Equal("elevenlabs_tts_base_url_invalid", ex.ErrorCode);
    }
}