using OetLearner.Api.Services.Writing;

namespace OetLearner.Api.Tests.Writing;

public sealed class WritingFactMapServiceTests
{
    [Fact]
    public void Maps_required_fact_as_included_when_the_candidate_preserves_it()
    {
        var map = WritingFactMapService.Build(
            "Diagnosis: asthma.\nAllergy status: negative.",
            "The patient has asthma and no known allergies.",
            "gp");

        var asthma = Assert.Single(map.Facts, fact => fact.FactText.Contains("asthma", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("included_accurately", asthma.CandidateStatus);
        Assert.Equal("required", asthma.Classification);
    }

    [Fact]
    public void Flags_missing_required_fact_without_penalising_unrequired_omission()
    {
        var map = WritingFactMapService.Build(
            "Diagnosis: asthma.\nAllergy status: negative.\nFamily history: unremarkable.",
            "The patient has asthma.",
            "gp");

        var allergy = Assert.Single(map.Facts, fact => fact.FactText.Contains("allergy", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("missing", allergy.CandidateStatus);
        var family = Assert.Single(map.Facts, fact => fact.FactText.Contains("family history", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("not_required", family.CandidateStatus);
    }

    [Fact]
    public void Flags_known_clinical_claim_that_is_absent_from_case_notes_as_invented()
    {
        var map = WritingFactMapService.Build(
            "Diagnosis: asthma.",
            "The patient has asthma and diabetes.",
            "gp");

        var invented = Assert.Single(map.Facts, fact => fact.CandidateStatus == "invented");
        Assert.Contains("diabetes", invented.FactText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Smoking_and_alcohol_are_optional_for_occupational_therapy_only()
    {
        var nonOt = WritingFactMapService.Build("Smoking: never.\nAlcohol: none.", "The patient is stable.", "gp");
        var ot = WritingFactMapService.Build("Smoking: never.\nAlcohol: none.", "The patient is stable.", "occupational_therapist");

        Assert.All(nonOt.Facts, fact => Assert.Equal("missing", fact.CandidateStatus));
        Assert.All(ot.Facts, fact => Assert.Equal("not_required", fact.CandidateStatus));
    }
}
