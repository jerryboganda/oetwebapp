using OetLearner.Api.Services.Writing;

namespace OetLearner.Api.Tests.Writing;

/// <summary>
/// Cross-profession repair, 18 Sep 2026 — WritingPatientAgeExtractor.
///
/// The relative check only looked BACKWARD from the age ("His daughter, aged 8"), so the
/// adjectival form, where the relative follows, slipped through: Mrs Jane LaPaglia's notes read
/// "Lives with her 80-year-old husband/carer, Joe", the husband's 80 became the patient's age, and
/// age_dob_inconsistent fired Critical on a Re: line stating the 71 her own note and the source
/// PDF give. The letter's only passing options were to state a false age or drop it.
/// </summary>
public sealed class WritingPatientAgeRelativeTests
{
    [Fact]
    public void The_patients_own_labelled_age_wins_over_a_relatives_adjectival_age()
    {
        const string notes = "Patient: Mrs Jane LaPaglia, age 71. Lives with her 80-year-old husband/carer, Joe.";
        Assert.Equal(71, WritingPatientAgeExtractor.Extract(notes));
    }

    [Theory]
    [InlineData("Lives with her 80-year-old husband/carer, Joe.")]
    [InlineData("Lives with his 78 year old wife.")]
    [InlineData("Cared for by her 45-year-old daughter.")]
    [InlineData("Attends with his 6-year-old son.")]
    [InlineData("His 80-year-old partner assists with medications.")]
    public void A_relative_straight_after_the_age_owns_it(string notes)
    {
        Assert.Null(WritingPatientAgeExtractor.Extract(notes));
    }

    [Theory]
    [InlineData("Mr Allen Mathis is a 61-year-old retired cabinet maker.", 61)]
    [InlineData("Patient: 73-year-old man living alone.", 73)]
    [InlineData("A 4-year-old girl brought in by her mother.", 4)]
    public void The_patients_own_adjectival_age_is_still_read(string notes, int expected)
    {
        Assert.Equal(expected, WritingPatientAgeExtractor.Extract(notes));
    }

    [Fact]
    public void A_relative_before_the_age_is_still_excluded()
    {
        Assert.Null(WritingPatientAgeExtractor.Extract("He is married with 3 children aged 13, 10 and 8."));
        Assert.Null(WritingPatientAgeExtractor.Extract("His daughter, aged 8, attends with him."));
    }
}
