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
}
