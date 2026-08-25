using System.Reflection;
using OetLearner.Api.Domain;
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

    [Fact]
    public void PaperHomeDto_IncludesSubscriptionGateAndAccessTier()
    {
        var paper = new ContentPaper
        {
            Id = "listen-paper-locked",
            Title = "Premium Listening Paper",
            Slug = "premium-listening-paper",
            Difficulty = "medium",
            EstimatedDurationMinutes = 42,
            TagsCsv = "medicine,access:premium",
            ExtractedTextJson = """{"listeningQuestions":[{"id":"q1"}]}""",
            Assets =
            [
                new ContentPaperAsset { Role = PaperAssetRole.Audio, IsPrimary = true },
                new ContentPaperAsset { Role = PaperAssetRole.QuestionPaper, IsPrimary = true },
                new ContentPaperAsset { Role = PaperAssetRole.AnswerKey, IsPrimary = true },
                new ContentPaperAsset { Role = PaperAssetRole.AudioScript, IsPrimary = true }
            ]
        };

        var method = typeof(ListeningLearnerService).GetMethod("PaperHomeDto", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var dto = method.Invoke(null, [paper, null, 0, true, 0, 0, 0]);
        Assert.NotNull(dto);

        var dtoType = dto.GetType();
        Assert.True((bool)dtoType.GetProperty("requiresSubscription")!.GetValue(dto)!);
        Assert.Equal("premium", dtoType.GetProperty("accessTier")!.GetValue(dto));
        Assert.Equal(1, dtoType.GetProperty("questionCount")!.GetValue(dto));
        Assert.Equal("medicine,access:premium", dtoType.GetProperty("tagsCsv")!.GetValue(dto));
        Assert.Equal(1, dtoType.GetProperty("partACount")!.GetValue(dto));
        Assert.Equal(0, dtoType.GetProperty("partBCount")!.GetValue(dto));
        Assert.Equal(0, dtoType.GetProperty("partCCount")!.GetValue(dto));
        Assert.Equal("/listening/paper/listen-paper-locked", dtoType.GetProperty("route")!.GetValue(dto));
    }
}
