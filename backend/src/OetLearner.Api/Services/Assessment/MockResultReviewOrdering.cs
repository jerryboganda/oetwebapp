using OetLearner.Api.Contracts;

namespace OetLearner.Api.Services.Assessment;

internal static class MockResultReviewOrdering
{
    public static List<MockReviewItemResponse> Prioritize(
        IEnumerable<MockReviewItemResponse> items)
        => items
            .Select((item, index) => new { item, index })
            .OrderBy(entry => entry.item.IsCorrect ? 2 : entry.item.IsUnanswered ? 0 : 1)
            .ThenBy(entry => entry.index)
            .Select(entry => entry.item)
            .ToList();
}
