using OetLearner.Api.Services.Speaking;

namespace OetLearner.Api.Tests.Speaking;

/// <summary>
/// Owner spec 4 Oct 2026 section 4: internal rule IDs (RULE_13, RULE_20, RULE_06 …) are for audit only and must
/// never be shown to a candidate, even if a model writes one into a rationale or a summary.
/// </summary>
public sealed class SpeakingLearnerTextTests
{
    [Theory]
    [InlineData("Good opening (RULE_20).", "Good opening.")]
    [InlineData("You skipped the recap [RULE_20].", "You skipped the recap.")]
    [InlineData("Jargon was used (RULE_06, RULE_13).", "Jargon was used.")]
    [InlineData("You broke RULE_13 here.", "You broke the relevant guideline here.")]
    [InlineData("See RULE_20 and RULE_21 for the recap.", "See the relevant guidelines for the recap.")]
    [InlineData("No identifiers here.", "No identifiers here.")]
    [InlineData("", "")]
    public void ScrubRuleIds_RemovesInternalIdsFromLearnerText(string input, string expected)
        => Assert.Equal(expected, SpeakingLearnerText.ScrubRuleIds(input));

    [Fact]
    public void ScrubRuleIds_HandlesNull()
        => Assert.Equal(string.Empty, SpeakingLearnerText.ScrubRuleIds(null));

    [Fact]
    public void ScrubRuleIds_KeepsLineBreaksAndOtherWords()
    {
        Assert.Equal("Line one\nLine two", SpeakingLearnerText.ScrubRuleIds("Line one (RULE_13)\nLine two"));
        Assert.Equal("The rules are clear. Rule 5 of the exam applies.", SpeakingLearnerText.ScrubRuleIds("The rules are clear. Rule 5 of the exam applies."));
    }
}
