using OetLearner.Api.Contracts;
using OetLearner.Api.Services.Assessment;

namespace OetLearner.Api.Tests.Reading;

public sealed class MockResultReviewOrderingTests
{
    [Fact]
    public void Prioritize_places_unanswered_then_mistakes_then_correct_and_preserves_order()
    {
        var items = new[]
        {
            Item("correct-first", isCorrect: true, isUnanswered: false),
            Item("mistake-first", isCorrect: false, isUnanswered: false),
            Item("unanswered", isCorrect: false, isUnanswered: true),
            Item("mistake-second", isCorrect: false, isUnanswered: false),
            Item("correct-second", isCorrect: true, isUnanswered: false),
        };

        var ordered = MockResultReviewOrdering.Prioritize(items);

        Assert.Equal(
            new[] { "unanswered", "mistake-first", "mistake-second", "correct-first", "correct-second" },
            ordered.Select(item => item.QuestionId));
    }

    private static MockReviewItemResponse Item(
        string id,
        bool isCorrect,
        bool isUnanswered)
        => new(
            id,
            "A",
            1,
            "MCQ",
            "Stem",
            isUnanswered ? null : "A",
            "A",
            isCorrect,
            isUnanswered,
            isCorrect ? 1 : 0,
            1,
            isCorrect || isUnanswered ? null : "distractor",
            null,
            null);
}
