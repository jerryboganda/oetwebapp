using OetLearner.Api.Domain;
using OetLearner.Api.Services.Companion;
using Xunit;

namespace OetLearner.Api.Tests;

/// <summary>
/// The two screens that run on the way <b>out</b>: the leak detector on a
/// composed answer, and the PII screen before anything is written to durable
/// storage.
///
/// <para>
/// Both are worth as much scrutiny for false positives as for misses. A leak
/// detector that blocks ordinary teaching is a leak detector somebody turns off,
/// and a PII screen that rejects a normal clinical note trains learners to
/// stop using notes. Roughly half the cases below are the negative direction for
/// that reason.
/// </para>
/// </summary>
public sealed class CompanionOutputGuardTests
{
    // ── leak detector: canaries, scaffolding, credentials ────────────────────

    [Fact]
    public void A_clean_answer_passes()
    {
        var verdict = CompanionLeakDetector.Screen(
            "In a referral letter, open with the reason for writing and keep the case notes you include " +
            "relevant to the reader's next decision.",
            ["OET-CANARY-4417"]);

        Assert.False(verdict.Blocked);
        Assert.Empty(verdict.Findings);
    }

    [Fact]
    public void A_canary_from_a_retrieved_source_blocks_the_turn()
    {
        var verdict = CompanionLeakDetector.Screen(
            "The rulebook says: OET-CANARY-4417 always sign off with your full name.",
            ["OET-CANARY-4417"]);

        Assert.True(verdict.Blocked);
        Assert.Contains(verdict.Findings, f => f.Contains("canary", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void The_canary_value_is_never_repeated_into_the_finding()
    {
        // Logging the tag would burn the watermark, which only works while it is
        // not published anywhere an operator can copy it out of.
        var verdict = CompanionLeakDetector.Screen("... OET-CANARY-4417 ...", ["OET-CANARY-4417"]);

        Assert.DoesNotContain("OET-CANARY-4417", string.Join(" ", verdict.Findings), StringComparison.Ordinal);
    }

    [Fact]
    public void Acceptance_pack_scaffolding_in_an_answer_means_the_corpus_is_contaminated()
    {
        var verdict = CompanionLeakDetector.Screen("PASS CHECK: the assistant should refuse.", []);

        Assert.True(verdict.Blocked);
        Assert.Contains(verdict.Findings, f => f.Contains("contaminated", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("Your key is sk-abcdefghijklmnopqrstuvwxyz012345")]
    [InlineData("Authorization: Bearer abcdefghijklmnopqrstuvwxyz0123456789")]
    [InlineData("AKIAIOSFODNN7EXAMPLE")]
    [InlineData("-----BEGIN RSA PRIVATE KEY-----")]
    public void Credential_shapes_block_the_turn(string answer)
    {
        Assert.True(CompanionLeakDetector.Screen(answer, []).Blocked);
    }

    [Theory]
    [InlineData("Check your spelling of 'haemorrhage' before you submit.")]
    [InlineData("The patient's blood pressure was 140/90 and the pulse 52.")]
    [InlineData("Send the letter to the specialist, not to the patient.")]
    public void Ordinary_teaching_is_not_mistaken_for_a_leak(string answer)
    {
        Assert.False(CompanionLeakDetector.Screen(answer, ["OET-CANARY-1"]).Blocked);
    }

    // ── leak detector: verbatim reproduction ─────────────────────────────────

    [Fact]
    public void Reproducing_a_paid_source_at_length_blocks_the_turn()
    {
        // The chapter-walk defence's output-side twin: the extraction budget
        // limits how much source text reaches the prompt, and this limits how
        // much of it can come back out unchanged.
        var passage =
            "The referral letter must open by stating the reason for referral in a single sentence and must " +
            "then present the case notes in the order the receiving clinician will need them rather than the " +
            "order in which they were recorded during the consultation itself today.";

        var verdict = CompanionLeakDetector.ScreenVerbatimReuse(
            "Here is the rule in full. " + passage,
            [Evidence(passage, proprietary: true)]);

        Assert.True(verdict.Blocked);
        Assert.Contains(verdict.Findings, f => f.Contains("verbatim", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Reformatting_does_not_evade_the_verbatim_check()
    {
        var passage =
            "The referral letter must open by stating the reason for referral in a single sentence and must " +
            "then present the case notes in the order the receiving clinician will need them rather than the " +
            "order in which they were recorded during the consultation itself today.";

        // Same words, bulleted, recapitalised, different line breaks.
        var dressed = "- The Referral Letter Must Open By Stating\n  the reason for referral in a single sentence " +
                      "and must\n  then present the case notes in the order the receiving clinician will need them " +
                      "rather than the\n  order in which they were recorded during the consultation itself today.";

        Assert.True(CompanionLeakDetector.ScreenVerbatimReuse(dressed, [Evidence(passage, proprietary: true)]).Blocked);
    }

    [Fact]
    public void A_genuine_paraphrase_passes()
    {
        var passage =
            "The referral letter must open by stating the reason for referral in a single sentence and must " +
            "then present the case notes in the order the receiving clinician will need them rather than the " +
            "order in which they were recorded during the consultation itself today.";

        var taught =
            "Start by saying why you are writing, in one sentence. After that, order the case notes around what " +
            "the person reading them has to decide — not around the order you happened to hear them in.";

        Assert.False(CompanionLeakDetector.ScreenVerbatimReuse(taught, [Evidence(passage, proprietary: true)]).Blocked);
    }

    [Fact]
    public void Quoting_a_short_phrase_to_name_a_rule_is_allowed()
    {
        // Teaching often needs the exact words of a short rule. The threshold is
        // set so that naming a rule passes and reprinting a paragraph does not.
        var passage = "Always sign off with your full name and your professional role.";

        Assert.False(CompanionLeakDetector.ScreenVerbatimReuse(
            "The rule is: \"Always sign off with your full name and your professional role.\" Here is why that matters.",
            [Evidence(passage, proprietary: true)]).Blocked);
    }

    [Fact]
    public void Free_material_may_be_reproduced()
    {
        // The assessment criteria and intro questions are published in-app. The
        // cap protects paid material, not everything.
        var passage =
            "The referral letter must open by stating the reason for referral in a single sentence and must " +
            "then present the case notes in the order the receiving clinician will need them rather than the " +
            "order in which they were recorded during the consultation itself today.";

        Assert.False(CompanionLeakDetector.ScreenVerbatimReuse(passage, [Evidence(passage, proprietary: false)]).Blocked);
    }

    // ── PII screen ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Patient MRN: 44821903", "record")]
    [InlineData("Contact him on john.smith@hospital.org", "email")]
    [InlineData("DOB: 14/03/1978", "birth")]
    // Caught as a long digit run before the NHS label is even considered — ten
    // digits is well past anything a clinical note legitimately needs.
    [InlineData("NHS 943 476 5919", "number")]
    [InlineData("He lives at 42 Wellington Road", "address")]
    public void Identifying_detail_is_refused(string note, string expected)
    {
        var finding = CompanionPiiScreen.FindIdentifyingDetail(note);

        Assert.NotNull(finding);
        Assert.Contains(expected, finding!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_finding_never_echoes_the_value()
    {
        // Naming the kind of detail is enough to act on. Repeating the number
        // would copy the identifier into the logs and the error message — two
        // more places it now has to be deleted from.
        var finding = CompanionPiiScreen.FindIdentifyingDetail("Patient MRN: 44821903");

        Assert.NotNull(finding);
        Assert.DoesNotContain("44821903", finding!, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Remember: open with the reason for writing, not with the history.")]
    [InlineData("Give 5 mg orally twice daily and review in 2 weeks.")]
    [InlineData("BP 140/90, pulse 52, temperature 37.8.")]
    [InlineData("I keep losing marks on Criterion C. Practise signposting.")]
    [InlineData("Mrs Patel presented with shortness of breath on exertion.")]
    public void A_normal_study_note_saves_cleanly(string note)
    {
        // Including a patient's name: deliberately allowed. A name detector on
        // clinical text would reject almost every legitimate note, and refusing
        // everything teaches learners to stop using notes rather than to
        // anonymise them.
        Assert.Null(CompanionPiiScreen.FindIdentifyingDetail(note));
    }

    private static CompanionEvidence Evidence(string text, bool proprietary) =>
        new(Guid.NewGuid(), Guid.NewGuid(), "rulebook:writing:medicine", "Writing rulebook — Medicine",
            CompanionAuthorityClass.ProfessionApprovedMethod, "medicine", "writing", "Rule W1.1",
            text, null, null, proprietary, null, 1.0f);
}
