using OetLearner.Api.Services.Companion;
using Xunit;

namespace OetLearner.Api.Tests;

/// <summary>
/// Knowledge Base Master Manifest §5 — the four Final Testing PDFs must never be
/// indexed. The packs are blunt about the consequence: if the assistant can
/// retrieve the test prompts or pass criteria, "the test is contaminated and the
/// result is invalid".
///
/// <para>
/// So this guard protects the validity of the measurement, not the quality of an
/// answer. These cases use text lifted from the real packs, because a guard that
/// only catches synthetic strings proves nothing.
/// </para>
/// </summary>
public sealed class CompanionCorpusGuardTests
{
    [Theory]
    // Verbatim scaffolding from the four acceptance packs.
    [InlineData("PASS CHECK")]
    [InlineData("- Returns the correct current destination and a direct Open action when supported.\nPASS CHECK")]
    [InlineData("SAMI FINAL TESTING PACK 4/4")]
    [InlineData("Tester scorecard")]
    [InlineData("PROMPT TO SEND")]
    [InlineData("SETUP / SEQUENCE\nUse Account A.")]
    [InlineData("RELEASE BLOCKER: Any score of 0 involving official facts")]
    [InlineData("QA ONLY - DO NOT ADD THESE TESTING PDFs OR THEIR PASS CHECKS")]
    [InlineData("Pre-candidate technical release gate")]
    [InlineData("# Scenario Score 0-3 Notes / defect ID")]
    public void Acceptance_pack_text_is_rejected(string text)
    {
        Assert.True(CompanionCorpusGuard.IsDisqualified(text));
    }

    [Fact]
    public void Matching_is_case_insensitive()
    {
        // Extraction from a PDF rarely preserves the original casing.
        Assert.True(CompanionCorpusGuard.IsDisqualified("pass check"));
        Assert.True(CompanionCorpusGuard.IsDisqualified("Prompt To Send"));
    }

    [Theory]
    // Genuine teaching material that uses the same everyday words. The markers
    // are structural headings precisely so these do not trip the guard.
    [InlineData("Check that the patient's date of birth is written in full before you sign off.")]
    [InlineData("A pass in OET Writing requires a purpose-led opening, not a longer letter.")]
    [InlineData("Always check the recipient before you decide which case notes are relevant.")]
    [InlineData("Send the referral letter to the consultant named on the case notes.")]
    [InlineData("The candidate should check their answer against the evidence in the text.")]
    [InlineData("Setup of the consultation matters: greet, confirm identity, then explore.")]
    [InlineData("")]
    [InlineData("   ")]
    public void Genuine_teaching_material_is_not_rejected(string text)
    {
        Assert.False(CompanionCorpusGuard.IsDisqualified(text));
    }

    [Fact]
    public void Null_is_safe()
    {
        Assert.False(CompanionCorpusGuard.IsDisqualified(null));
    }

    [Fact]
    public void The_offending_marker_is_reported_so_an_operator_can_see_why()
    {
        // A bare "rejected" tells an admin nothing; naming the phrase lets them
        // confirm it really was a testing pack rather than a false positive.
        var marker = CompanionCorpusGuard.FindDisqualifyingMarker(
            "03. Open exact Medicine Full Course Reading area\nPROMPT TO SEND\nOpen my materials.");

        Assert.Equal("PROMPT TO SEND", marker);
    }
}
