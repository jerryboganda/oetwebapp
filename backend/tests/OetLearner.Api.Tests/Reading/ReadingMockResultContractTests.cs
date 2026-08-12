using System.Text.Json;
using OetLearner.Api.Contracts;

namespace OetLearner.Api.Tests.Reading;

public sealed class ReadingMockResultContractTests
{
    [Fact]
    public void Mock_result_contract_supports_prioritized_full_review_and_governed_score()
    {
        var result = new MockSessionResultResponse(
            Score: 30,
            TotalQuestions: 42,
            DurationSeconds: 3600,
            ScaledScore: 350,
            ItemReview:
            [
                new MockReviewItemResponse("q-wrong", "A", 1, "MCQ", "Stem", "B", "A", false, false, 0, 1, "distractor", "why", null),
                new MockReviewItemResponse("q-right", "A", 2, "MCQ", "Stem", "A", "A", true, false, 1, 1, null, null, null),
            ],
            ScoreConversionTableVersionKey: "reading-default-v1",
            ScoreConversionPassed: true,
            ScoreConversionGrade: "B");

        Assert.Equal("q-wrong", result.ItemReview![0].QuestionId);
        Assert.Equal("q-right", result.ItemReview[1].QuestionId);
    }

    [Fact]
    public void Mock_result_serializes_governed_conversion_fields()
    {
        var result = new MockSessionResultResponse(
            Score: 30,
            TotalQuestions: 42,
            DurationSeconds: 3600,
            ScaledScore: 350,
            ScoreConversionTableVersionKey: "reading-default-v1",
            ScoreConversionPassed: true,
            ScoreConversionGrade: "B");

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(
            result,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        Assert.Equal(350, document.RootElement.GetProperty("scaledScore").GetInt32());
        Assert.Equal("reading-default-v1", document.RootElement
            .GetProperty("scoreConversionTableVersionKey").GetString());
        Assert.True(document.RootElement.GetProperty("scoreConversionPassed").GetBoolean());
        Assert.Equal("B", document.RootElement.GetProperty("scoreConversionGrade").GetString());
    }
}
