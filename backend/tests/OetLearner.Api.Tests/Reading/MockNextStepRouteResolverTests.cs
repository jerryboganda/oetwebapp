using OetLearner.Api.Contracts;
using OetLearner.Api.Services.Assessment;

namespace OetLearner.Api.Tests.Reading;

public sealed class MockNextStepRouteResolverTests
{
    [Fact]
    public void Reading_route_focuses_the_missed_part_and_error_bank()
    {
        var route = MockNextStepRouteResolver.Reading("b");

        Assert.Equal("/reading/practice?focus=B&tab=errors", route);
    }

    [Theory]
    [InlineData("spelling", "/listening/drills/spelling")]
    [InlineData("number_form", "/listening/drills/numbers_and_frequencies")]
    [InlineData("unit", "/listening/drills/numbers_and_frequencies")]
    [InlineData("distractor_confusion", "/listening/drills/distractor_confusion")]
    [InlineData("inference", "/listening/drills/detail_capture")]
    public void Listening_route_maps_taxonomy_to_an_existing_drill(string category, string expected)
    {
        Assert.Equal(expected, MockNextStepRouteResolver.Listening(category));
    }

    [Fact]
    public void Next_step_is_null_when_the_mock_has_no_misses()
    {
        var item = new MockReviewItemResponse(
            "q-1", "A", 1, "MCQ", "Stem", "A", "A", true, false, 1, 1, null, null, null);

        Assert.Null(MockNextStepRouteResolver.BuildReading(new[] { item }));
        Assert.Null(MockNextStepRouteResolver.BuildListening(new[] { item }));
    }
}
