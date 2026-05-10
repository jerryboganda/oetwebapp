using System.Text.Json;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Content;

namespace OetLearner.Api.Tests;

public class WritingContentStructureTests
{
    [Fact]
    public void Validate_allows_canonical_letter_type_for_profession()
    {
        var paper = BuildValidWritingPaper();
        paper.ProfessionId = "medicine";
        paper.AppliesToAllProfessions = false;
        paper.LetterType = "routine_referral";

        var report = WritingContentStructure.Validate(paper);

        Assert.DoesNotContain(report.Issues, i => i.Code == "profession_letter_type");
        Assert.True(report.IsPublishReady);
    }

    [Fact]
    public void Validate_does_not_pile_on_when_letter_type_is_not_canonical()
    {
        var paper = BuildValidWritingPaper();
        paper.ProfessionId = "medicine";
        paper.AppliesToAllProfessions = false;
        paper.LetterType = "some_unknown_type";

        var report = WritingContentStructure.Validate(paper);

        // Canonical-letter-type check fires.
        Assert.Contains(report.Issues, i => i.Code == "letter_type" && i.Severity == "error");
        // Profession-letter-type check must NOT pile on for a non-canonical value.
        Assert.DoesNotContain(report.Issues, i => i.Code == "profession_letter_type");
    }

    [Fact]
    public void Validate_skips_profession_check_when_paper_applies_to_all_professions()
    {
        var paper = BuildValidWritingPaper();
        paper.AppliesToAllProfessions = true;
        paper.ProfessionId = null;
        paper.LetterType = "routine_referral";

        var report = WritingContentStructure.Validate(paper);

        Assert.DoesNotContain(report.Issues, i => i.Code == "profession_letter_type");
    }

    [Fact]
    public void Validate_allows_universal_transfer_letter_for_profession()
    {
        var paper = BuildValidWritingPaper();
        paper.ProfessionId = "medicine";
        paper.AppliesToAllProfessions = false;
        paper.LetterType = "transfer_letter";

        var report = WritingContentStructure.Validate(paper);

        Assert.DoesNotContain(report.Issues, i => i.Code == "profession_letter_type");
        Assert.True(report.IsPublishReady);
    }

    [Fact]
    public void Validate_VeterinaryWithNonMedicalReferral_BlocksPublish()
    {
        var paper = BuildValidWritingPaper();
        paper.ProfessionId = "veterinary";
        paper.AppliesToAllProfessions = false;
        paper.LetterType = "non_medical_referral";

        var report = WritingContentStructure.Validate(paper);

        Assert.False(report.IsPublishReady);
        var issue = Assert.Single(report.Issues, i => i.Code == "profession_letter_type");
        Assert.Equal("error", issue.Severity);
        Assert.Contains("veterinary", issue.Message);
        Assert.Contains("non_medical_referral", issue.Message);
    }

    [Fact]
    public void Validate_MedicineWithNonMedicalReferral_AllowsPublish()
    {
        var paper = BuildValidWritingPaper();
        paper.ProfessionId = "medicine";
        paper.AppliesToAllProfessions = false;
        paper.LetterType = "non_medical_referral";

        var report = WritingContentStructure.Validate(paper);

        Assert.DoesNotContain(report.Issues, i => i.Code == "profession_letter_type");
    }

    private static ContentPaper BuildValidWritingPaper()
    {
        var structure = new
        {
            writingStructure = new
            {
                taskPrompt = "Write a referral letter to Dr Smith regarding the patient.",
                caseNotes = "Patient: John Doe\nAge: 45\nDiagnosis: Hypertension",
                modelAnswer = "Dear Dr Smith,\n\nI am writing to refer the patient.",
                criteriaFocus = new[] { "purpose", "content" },
            },
        };

        return new ContentPaper
        {
            Id = Guid.NewGuid().ToString("n"),
            Slug = "writing-test-paper",
            Title = "Test Writing Paper",
            SubtestCode = "writing",
            LetterType = "routine_referral",
            ProfessionId = "medicine",
            AppliesToAllProfessions = false,
            ExtractedTextJson = JsonSerializer.Serialize(structure),
            SourceProvenance = "test-suite",
        };
    }
}
