using OetLearner.Api.Services.Writing;

namespace OetLearner.Api.Tests.Writing;

/// <summary>
/// Owner decision (19 Sep 2026) — WritingPatientAgeExtractor, two defects that each produced a
/// wrong age for the patient.
///
/// 1. An age written in words was not read at all. Jonathon Apple "is a ten-year-old boy" (pharmacy
///    602cee04), so the child was classed as an adult and re_line_full_name demanded "Mr" for him in
///    the same run in which paragraph_start_patient_name told the writer to use his first name —
///    owner decision C.7 on minors was unreachable.
/// 2. An OBJECT's age was read as the patient's. Optometry's Mr Arthur Reed (DOB 12 January 1949)
///    became a 4-year-old from "Wears bifocal spectacles; current pair approximately 4 years old".
///
/// The age extractor also runs on candidate submissions, so both fixes remove a wrong penalty.
/// </summary>
public sealed class WritingPatientAgeWordAndObjectTests
{
    [Theory]
    [InlineData("Jonathon Apple is a ten-year-old boy with Type 1 diabetes.", 10)]
    [InlineData("Emily is a thirteen year old girl.", 13)]
    [InlineData("Noah is a seven-year-old boy who attends school.", 7)]
    [InlineData("She is a nineteen-year-old student.", 19)]
    public void An_age_written_in_words_from_one_to_nineteen_is_read(string notes, int expected)
        => Assert.Equal(expected, WritingPatientAgeExtractor.Extract(notes));

    [Theory]
    [InlineData("Lives with her nine-year-old daughter.")]
    [InlineData("Attends with his six year old son.")]
    [InlineData("His eight-year-old brother assists at home.")]
    public void A_relatives_age_in_words_is_never_the_patients(string notes)
        => Assert.Null(WritingPatientAgeExtractor.Extract(notes));

    [Fact]
    public void A_digit_age_is_read_as_before()
    {
        Assert.Equal(45, WritingPatientAgeExtractor.Extract("Mr Smith is a 45-year-old accountant."));
        Assert.Equal(45, WritingPatientAgeExtractor.Extract("Mr Smith, aged 45, has two sons aged 4 and 7."));
    }

    [Fact]
    public void The_digit_age_still_wins_when_the_word_form_also_appears()
        => Assert.Equal(62, WritingPatientAgeExtractor.Extract("Patient aged 62. Lives with her nine-year-old daughter."));

    [Theory]
    [InlineData("Wears bifocal spectacles; current pair approximately 4 years old.")]
    [InlineData("Wears bifocal spectacles; current pair approximately four years old.")]
    [InlineData("Wears glasses. The current pair is 3 years old.")]
    [InlineData("Uses a wheelchair, which is 6 years old.")]
    [InlineData("Dentures fitted; the upper denture is 5 years old.")]
    public void The_age_of_an_object_is_not_the_patients(string notes)
        => Assert.Null(WritingPatientAgeExtractor.Extract(notes));

    [Fact]
    public void A_patients_age_after_a_comma_is_kept_even_when_an_object_is_mentioned()
        => Assert.Equal(45, WritingPatientAgeExtractor.Extract("Wears glasses, aged 45."));

    [Fact]
    public void A_semicolon_ends_the_clause_so_an_earlier_relative_does_not_own_a_later_age()
        => Assert.Equal(45, WritingPatientAgeExtractor.Extract("Lives with her son; patient aged 45."));
}
