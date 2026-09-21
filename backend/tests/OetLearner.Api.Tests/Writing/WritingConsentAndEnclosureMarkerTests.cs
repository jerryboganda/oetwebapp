using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Tests.Writing;

/// <summary>
/// Cross-profession repair, 18 Sep 2026 — two more demands that could only be met by writing
/// something the notes do not say.
///
/// closure_mentions_consent_if_flagged accepted only the SINGULAR "has consented", so Kevin
/// Brown's notes ("Parents consented to referral to the family doctor") had no faithful wording:
/// "his parents have consented" failed, and the stored answer had satisfied the rule with an
/// invented sentence instead.
///
/// ResultsEnclosed counted anything attached, so Mr Adam White's note — "an alert sticker has been
/// attached to his paperwork" — made enclosure_results_phrase demand "Please find enclosed a copy
/// of the pathology results." while his notes say those results are still expected.
/// </summary>
public sealed class WritingConsentAndEnclosureMarkerTests
{
    private static readonly WritingRuleEngine Engine = new(new RulebookLoader());

    private static IReadOnlyList<LintFinding> Lint(string letter, string notes) =>
        Engine.Lint(new WritingLintInput(
            LetterText: letter,
            LetterType: "routine_referral",
            CaseNotesText: notes,
            IsModelAnswer: true,
            CaseNotesMarkers: WritingCaseNotesMarkerExtractor.Derive(notes)));

    // ---- consent ----

    // Kevin Brown's notes as they actually read: note 22 records consent in the bare form that sets
    // the marker, note 34 records the parents'. My first version of this fixture used note 34
    // alone, which sets nothing — consent does not match "consented", so the rule never fired
    // and the "still fires" case failed. That under-firing is real but separate: it affects two
    // tasks in the whole catalogue (Dentistry Mr Hunter, Pharmacy Ms King), and widening the marker
    // would newly DEMAND a consent sentence in letters already written, so it is reported, not
    // changed here.
    private const string ParentConsentNotes =
        "20 September 2020: bullying continued; a home visit was organised with Kevin's consent. | "
        + "Parents consented to referral to the family doctor for investigation and treatment.";

    [Theory]
    [InlineData("His parents have consented to this referral.")]
    [InlineData("Kevin's parents have consented to this referral.")]
    [InlineData("Consent was obtained from both parents.")]
    [InlineData("His family has consented to this referral.")]
    public void Plural_or_obtained_consent_satisfies_the_consent_rule(string sentence)
    {
        Assert.DoesNotContain(Lint(sentence, ParentConsentNotes),
            f => f.RuleId.Contains("closure_mentions_consent_if_flagged"));
    }

    [Fact]
    public void A_letter_that_states_no_consent_at_all_still_fires()
    {
        Assert.Contains(Lint("I would be grateful if you could investigate further.", ParentConsentNotes),
            f => f.RuleId.Contains("closure_mentions_consent_if_flagged"));
    }

    // ---- enclosure ----

    [Theory]
    [InlineData("He has no known allergies but is sensitive to codeine; an alert sticker has been attached to his paperwork.")]
    [InlineData("A falls-risk band was attached on admission.")]
    [InlineData("Blood tests are expected before the procedure.")]
    public void Something_other_than_results_being_attached_is_not_an_enclosure(string notes)
    {
        Assert.False(WritingCaseNotesMarkerExtractor.Derive(notes).ResultsEnclosed);
    }

    [Theory]
    [InlineData("Pathology results attached.")]
    [InlineData("Please find enclosed a copy of the pathology results.")]
    [InlineData("A copy of the imaging is enclosed.")]
    [InlineData("Endoscopy and biopsy performed (results attached).")]
    public void Results_really_being_enclosed_still_sets_the_marker(string notes)
    {
        Assert.True(WritingCaseNotesMarkerExtractor.Derive(notes).ResultsEnclosed);
    }

    [Fact]
    public void An_alert_sticker_no_longer_forces_a_false_enclosure_sentence()
    {
        const string notes = "He is sensitive to codeine; an alert sticker has been attached to his paperwork. "
                             + "Full blood examination, urea and electrolytes and liver function tests are expected before the procedure.";
        Assert.DoesNotContain(Lint("I would be grateful if you could notify all staff of his codeine sensitivity.", notes),
            f => f.RuleId.Contains("enclosure_results_phrase"));
    }
}
