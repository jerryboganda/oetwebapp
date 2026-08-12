using OetLearner.Api.Services.Reading;

namespace OetLearner.Api.Tests.Reading;

public sealed class ReadingExtractionFallbackTests
{
    [Fact]
    public void Provider_failure_fallback_is_empty_and_non_approvable()
    {
        var result = ReadingExtractionFallback.Create("provider unavailable");

        Assert.True(result.IsStub);
        Assert.Equal("provider unavailable", result.StubReason);
        Assert.Empty(result.Manifest.Parts);
        Assert.Null(result.RawResponseJson);
    }
}
