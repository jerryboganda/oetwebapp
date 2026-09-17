using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Tests.Writing;

/// <summary>
/// Cross-profession repair, 18 Sep 2026 (nursing, Mr Derek Shepherd).
///
/// A bare "patient requested" in the canonical notes set PatientInitiatedReferral for ANY
/// topic, so "patient requested information on simple low-fat recipes for home" made
/// closure_mentions_patient_request_if_flagged demand "upon his request" in a PHYSIOTHERAPY
/// referral the patient had never asked for. The only way to satisfy the detector was to write
/// an unsupported claim, which is exactly the class of defect the owner rejects, so the marker
/// was narrowed instead of the letter being bent to fit it.
///
/// The marker's only consumer is DetectClosurePatientRequest, so these tests also pin that
/// narrowing it can never raise a NEW finding on a letter that already passes.
/// </summary>
public sealed class WritingPatientRequestMarkerRegressionTests
{
    [Theory]
    // The Shepherd note that caused this: a request about diet, not about the referral.
    [InlineData("Low-fat diet after discharge; patient requested information on simple low-fat recipes for home")]
    [InlineData("Patient requested a copy of his discharge summary")]
    [InlineData("Patient requested information about smoking cessation services")]
    [InlineData("Patient asked for advice on returning to work")]
    public void A_request_about_something_other_than_the_referral_is_not_a_patient_initiated_referral(string notes)
    {
        Assert.False(WritingCaseNotesMarkerExtractor.Derive(notes).PatientInitiatedReferral);
    }

    [Theory]
    [InlineData("Patient requested referral to a specialist")]
    [InlineData("Patient requested a second opinion")]
    [InlineData("She requested a referral to the pain clinic")]
    [InlineData("He requested an appointment with the consultant")]
    [InlineData("Patient asked to be referred for physiotherapy")]
    [InlineData("Patient asked to see a dermatologist")]
    [InlineData("Referral made upon his request")]
    [InlineData("Seen at her own request")]
    [InlineData("Attended at Mr Shepherd's request")]
    public void A_genuine_patient_initiated_referral_still_sets_the_marker(string notes)
    {
        Assert.True(WritingCaseNotesMarkerExtractor.Derive(notes).PatientInitiatedReferral);
    }

    [Fact]
    public void The_detector_stays_silent_when_the_notes_only_record_an_unrelated_request()
    {
        var engine = new WritingRuleEngine(new RulebookLoader());
        const string notes = "Discharge plan: low-fat diet after discharge; patient requested information on simple low-fat recipes for home.";

        // No "upon his request" anywhere in the letter, and none is owed.
        var findings = engine.Lint(new WritingLintInput(
            LetterText: "I would be grateful if you could supervise his home-based exercise programme.",
            CaseNotesText: notes,
            IsModelAnswer: true,
            CaseNotesMarkers: WritingCaseNotesMarkerExtractor.Derive(notes)));

        Assert.DoesNotContain(findings, f => f.RuleId.Contains("closure_mentions_patient_request_if_flagged"));
    }

    [Fact]
    public void The_detector_still_fires_when_the_patient_did_ask_for_the_referral()
    {
        var engine = new WritingRuleEngine(new RulebookLoader());
        const string notes = "Patient requested referral to the pain clinic for further management.";

        var findings = engine.Lint(new WritingLintInput(
            LetterText: "I would be grateful if you could assess and manage his ongoing pain.",
            CaseNotesText: notes,
            IsModelAnswer: true,
            CaseNotesMarkers: WritingCaseNotesMarkerExtractor.Derive(notes)));

        Assert.Contains(findings, f => f.RuleId.Contains("closure_mentions_patient_request_if_flagged"));
    }
}
