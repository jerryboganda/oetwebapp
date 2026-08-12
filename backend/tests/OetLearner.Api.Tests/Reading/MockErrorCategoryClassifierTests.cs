using OetLearner.Api.Services.Assessment;

namespace OetLearner.Api.Tests.Reading;

public sealed class MockErrorCategoryClassifierTests
{
    [Theory]
    [InlineData("patient", "patients", null, "number_form")]
    [InlineData("5 mg", "5 mL", null, "unit")]
    [InlineData("aspirin", "asprin", null, "spelling")]
    [InlineData("the outcome", "the clinical outcome", null, "form")]
    [InlineData("option text", "correct text", "inference", "inference")]
    public void ClassifyTyped_returns_diagnostic_category_without_changing_mark(
        string learner,
        string correct,
        string? skillTags,
        string expected)
    {
        Assert.Equal(expected, MockErrorCategoryClassifier.ClassifyTyped(learner, correct, skillTags));
    }
}
