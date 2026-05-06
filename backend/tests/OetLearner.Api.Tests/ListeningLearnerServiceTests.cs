using OetLearner.Api.Services.Listening;

namespace OetLearner.Api.Tests;

/// <summary>
/// Unit tests for the static helpers on <see cref="ListeningLearnerService"/>.
/// Today this only covers <c>ResolveAccessTier</c>; the broader integration
/// surface lives in <see cref="ListeningRelationalRuntimeTests"/>.
/// </summary>
public class ListeningLearnerServiceTests
{
    [Theory]
    [InlineData("access:free", "free")]
    [InlineData("medicine,access:free", "free")]
    [InlineData("access:preview-first-extract,medicine", "preview")]
    [InlineData("access:preview", "preview")]
    [InlineData("ACCESS:FREE", "free")]
    [InlineData("medicine,nursing", "premium")]
    [InlineData("", "premium")]
    [InlineData(null, "premium")]
    public void ResolveAccessTier_MapsTokensToTiers(string? tagsCsv, string expected)
    {
        Assert.Equal(expected, ListeningLearnerService.ResolveAccessTier(tagsCsv));
    }
}
