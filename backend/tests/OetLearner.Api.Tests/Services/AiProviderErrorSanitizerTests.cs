using OetLearner.Api.Services.Ai;

namespace OetLearner.Api.Tests.Services;

public sealed class AiProviderErrorSanitizerTests
{
    [Fact]
    public void Never_echoes_provider_body()
    {
        var sanitized = AiProviderErrorSanitizer.Sanitize(500, "timeout");
        Assert.Equal("timeout:http_500", sanitized);
        Assert.DoesNotContain("RAW", sanitized, StringComparison.Ordinal);
    }
}
