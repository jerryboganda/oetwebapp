using OetLearner.Api.Services.Listening;

namespace OetLearner.Api.Tests.Listening;

public class ListeningQuestionSanitizationTests
{
    [Theory]
    [InlineData("====== PAGE 4 ======\nPractice Test 1\nWhat is the nurse discussing with the patient?", "What is the nurse discussing with the patient?")]
    [InlineData("PAGE 5\nWhat is the nurse discussing with the patient?", "What is the nurse discussing with the patient?")]
    [InlineData("Practice Test 1\nWhat is the nurse discussing with the patient?", "What is the nurse discussing with the patient?")]
    [InlineData("See PDF", "")]
    [InlineData("CPDF", "")]
    [InlineData("PDF", "")]
    [InlineData("View PDF", "")]
    [InlineData("====== PAGE 4 ======", "")]
    [InlineData("PAGE 5", "")]
    [InlineData("Practice Test 1", "")]
    [InlineData("PAGE 4 Question 25 What is the patient's condition?", "Question 25 What is the patient's condition?")]
    [InlineData("Practice Test 1 : You hear a doctor talking to a nurse.", "You hear a doctor talking to a nurse.")]
    [InlineData("In general practice, hypertension is common.", "In general practice, hypertension is common.")]
    [InlineData("The patient presented with Paget disease.", "The patient presented with Paget disease.")]
    public void SanitizeQuestionPrompt_StripsExtractionMarkersAndSentinels(string? input, string expected)
    {
        var sanitized = ListeningLearnerService.SanitizeQuestionPrompt(input);
        Assert.Equal(expected, sanitized);
    }

    [Theory]
    [InlineData("Option A", "")]
    [InlineData("Option B", "")]
    [InlineData("Option C", "")]
    [InlineData("See PDF", "")]
    [InlineData("====== PAGE 4 ======\nTake 500mg paracetamol", "Take 500mg paracetamol")]
    [InlineData("Take 500mg paracetamol orally", "Take 500mg paracetamol orally")]
    public void SanitizeOptionText_StripsPlaceholdersAndArtifacts(string? input, string expected)
    {
        var sanitized = ListeningLearnerService.SanitizeOptionText(input);
        Assert.Equal(expected, sanitized);
    }

    [Theory]
    [InlineData("You hear a nurse discussing a patient's discharge plan. What does she recommend?", true)]
    [InlineData("See PDF", false)]
    [InlineData("PART B — WORKPLACE EXTRACTS", false)]
    [InlineData("Q25 PART B - WORKPLACE EXTRACTS", false)]
    [InlineData("QUESTION 25", false)]
    [InlineData("What does the speaker identify as the main clinical priority?", false)]
    [InlineData("What is the speaker's main point in this extract?", false)]
    public void IsUsablePartBCStem_RejectsSentinelsHeadingsAndGenericFallbacks(string? input, bool expected)
    {
        Assert.Equal(expected, ListeningLearnerService.IsUsablePartBCStem(input));
    }

    [Theory]
    [InlineData(null, 25, "B1")]
    [InlineData("B", 30, "B6")]
    [InlineData(null, 31, "C1")]
    [InlineData("C", 42, "C2")]
    [InlineData(null, 13, "A2")]
    [InlineData("C2", 31, "C2")]
    public void ResolveQuestionPartCode_UsesExplicitCodeOrCanonicalNumberRange(
        string? rawPartCode,
        int questionNumber,
        string expected)
    {
        Assert.Equal(expected, ListeningLearnerService.ResolveQuestionPartCode(rawPartCode, questionNumber));
    }
}
