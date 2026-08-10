using OetLearner.Api.Services.Writing;

namespace OetLearner.Api.Tests.Writing;

public sealed class WritingModelAnswerGroundingTests
{
    [Fact]
    public void Every_clinical_sentence_must_map_to_a_case_note_fact()
    {
        var result = WritingModelAnswerGroundingValidator.Validate(
            "The patient has asthma. Please review the inhaler technique.",
            ["Diagnosis: asthma.", "Plan: review inhaler technique."]);

        Assert.True(result.IsGrounded);
        Assert.Empty(result.UnmappedSentences);
    }

    [Fact]
    public void Unmapped_clinical_claim_holds_the_model_answer_for_review()
    {
        var result = WritingModelAnswerGroundingValidator.Validate(
            "The patient has asthma and diabetes.",
            ["Diagnosis: asthma."]);

        Assert.False(result.IsGrounded);
        Assert.Contains(result.UnmappedSentences, sentence => sentence.Contains("diabetes", StringComparison.OrdinalIgnoreCase));
    }
}
