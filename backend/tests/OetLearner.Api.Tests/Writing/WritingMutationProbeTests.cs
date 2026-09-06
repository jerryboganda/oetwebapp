using OetLearner.Api.Services.Rulebook;
using OetLearner.Api.Services.Writing;

namespace OetLearner.Api.Tests.Writing;

/// <summary>
/// Mutation probes for the deterministic Writing rule engine: each probe takes
/// a clean base letter, applies ONE systematic mutation, and asserts the
/// detector response observed against the canonical rulebooks. Guards the
/// six-criterion grading inputs against silent detector regressions.
///
/// Rule IDs pinned here were verified live against the canonical registry
/// build (see TempProbeDump calibration); if the registry changes them, update
/// the pins — never delete the probe.
///
/// NOTE: this is a standing in-repo battery. It is not a port of any external
/// probe set; an external set should be added alongside, never swapped blindly.
/// </summary>
public sealed class WritingMutationProbeTests
{
    private static readonly WritingRuleEngine Engine = new(new RulebookLoader());

    private const string CleanLetter =
        "12 March 2025\nDear Dr Green,\nRe: Mrs Smith, review\n\n"
        + "I am writing to refer Mrs Smith for review of her ongoing condition. Please assess her at your earliest convenience.\n\n"
        + "She was seen recently with stable observations. Advise on further management if needed. Thank you for your continued care.\n\n"
        + "Yours sincerely,\nDoctor";

    private static IReadOnlyList<LintFinding> Lint(string letter)
        => Engine.Lint(new WritingLintInput(
            LetterText: letter,
            LetterType: "routine_referral",
            Profession: ExamProfession.Medicine));

    private static bool Fires(IReadOnlyList<LintFinding> findings, string ruleId)
        => findings.Any(f => string.Equals(f.RuleId, ruleId, StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void Probe01_CleanBase_HasNoCriticalFindings()
    {
        Assert.DoesNotContain(Lint(CleanLetter), f => f.Severity == RuleSeverity.Critical);
    }

    [Fact]
    public void Probe02_Contraction_Fires_NoContractions()
    {
        Assert.True(Fires(Lint(CleanLetter.Replace("She was seen", "She wasn't seen")), "BUILTIN.no_contractions"));
    }

    [Fact]
    public void Probe03_DatePrefix_Fires_NoDatePrefix()
    {
        Assert.True(Fires(Lint("Date: 12 March 2025\n\n" + CleanLetter), "BUILTIN.no_date_prefix"));
    }

    [Fact]
    public void Probe04_MissingSalutation_Fires_Critical()
    {
        var findings = Lint(CleanLetter.Replace("Dear Dr Green,\n", string.Empty));
        Assert.Contains(findings, f => f.Severity == RuleSeverity.Critical);
    }

    [Fact]
    public void Probe05_MissingSignoff_Fires_Critical()
    {
        var findings = Lint(CleanLetter.Replace("Yours sincerely,\nDoctor", string.Empty));
        Assert.Contains(findings, f => f.Severity == RuleSeverity.Critical);
    }

    [Fact]
    public void Probe06_PurposeStripped_Fires_IntroPurpose()
    {
        Assert.True(Fires(
            Lint(CleanLetter.Replace("I am writing to refer Mrs Smith for review of her ongoing condition.", "Hello there.")),
            "BUILTIN.intro_contains_purpose"));
    }

    [Fact]
    public void Probe07_AllCaps_Fires_SignoffCapitalisation()
    {
        Assert.True(Fires(Lint(CleanLetter.ToUpperInvariant()), "BUILTIN.yours_sincerely_capitalisation"));
    }

    [Fact]
    public void Probe08_EmptyLetter_Yields_Findings_Not_Exception()
    {
        var findings = Lint(string.Empty);
        Assert.NotNull(findings);
        Assert.Contains(findings, f => f.Severity == RuleSeverity.Critical);
    }

    [Fact]
    public void Probe09_TrailingWhitespace_DoesNot_Change_Findings()
    {
        var baseline = Lint(CleanLetter);
        var noisy = Lint(CleanLetter + "   \n\n");
        Assert.Equal(baseline.Count, noisy.Count);
        Assert.DoesNotContain(noisy, f => f.Severity == RuleSeverity.Critical);
    }
}
