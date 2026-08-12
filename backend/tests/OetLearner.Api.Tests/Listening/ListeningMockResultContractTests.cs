using System.Text.Json;
using OetLearner.Api.Contracts;

namespace OetLearner.Api.Tests.Listening;

public sealed class ListeningMockResultContractTests
{
    [Fact]
    public void Mock_item_review_serializes_candidate_safe_fields_and_evidence_timing()
    {
        var result = new MockResultResponse(
            SessionId: Guid.NewGuid(),
            RawScore: 1,
            ScaledScore: null,
            GradeLabel: "Scaled score unavailable",
            SkillRadar: [],
            AccentChart: [],
            PredictedScoreLow: null,
            PredictedScoreHigh: null,
            SubmittedAt: DateTimeOffset.UtcNow,
            ItemReview:
            [
                new MockReviewItemResponse(
                    QuestionId: "q-1",
                    PartCode: "A1",
                    Number: 1,
                    QuestionType: "ShortAnswer",
                    Stem: "Enter the patient's age.",
                    LearnerAnswer: "62",
                    CorrectAnswer: " sixty-two ",
                    IsCorrect: false,
                    IsUnanswered: false,
                    PointsEarned: 0,
                    MaxPoints: 1,
                    ErrorCategory: "incorrect_answer",
                    Explanation: "The authored answer is shown after submission.",
                    Evidence: "The patient is sixty-two years old.",
                    EvidenceStartMilliseconds: 1200,
                    EvidenceEndMilliseconds: 3400),
            ],
            ErrorSummary:
            [
                new MockErrorSummaryResponse("incorrect_answer", 1, ["q-1"]),
            ],
            NextStep: new MockNextStepResponse(
                "Review Listening A1 practice",
                "Focus on incorrect answer items.",
                "/listening/practice/A1"),
            StudyPlanRoute: "/study-plan",
            TotalQuestions: 42);

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(
            result,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var item = document.RootElement.GetProperty("itemReview")[0];

        Assert.Equal("q-1", item.GetProperty("questionId").GetString());
        Assert.Equal("A1", item.GetProperty("partCode").GetString());
        Assert.Equal(" sixty-two ", item.GetProperty("correctAnswer").GetString());
        Assert.Equal(1200, item.GetProperty("evidenceStartMilliseconds").GetInt32());
        Assert.Equal(3400, item.GetProperty("evidenceEndMilliseconds").GetInt32());
        Assert.Equal(1, document.RootElement.GetProperty("errorSummary")[0].GetProperty("count").GetInt32());
        Assert.Equal("/listening/practice/A1", document.RootElement.GetProperty("nextStep").GetProperty("route").GetString());
        Assert.Equal("/study-plan", document.RootElement.GetProperty("studyPlanRoute").GetString());
        Assert.Equal(42, document.RootElement.GetProperty("totalQuestions").GetInt32());

        var breakdown = new MockPartBreakdownResponse("A", 12, 24, 12, 8, 4, 50m);
        using var breakdownDocument = JsonDocument.Parse(JsonSerializer.Serialize(
            breakdown,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Assert.Equal(50m, breakdownDocument.RootElement.GetProperty("accuracyPercentage").GetDecimal());
    }
}
