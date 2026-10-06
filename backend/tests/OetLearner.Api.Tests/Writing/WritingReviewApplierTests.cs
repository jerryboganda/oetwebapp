using OetLearner.Api.Services.Rulebook;
using OetLearner.Api.Services.Writing;
using OetLearner.Api.Services.Writing.Review;

namespace OetLearner.Api.Tests.Writing;

/// <summary>
/// The secondary reviewer's deterministic half (owner handoff 6 Oct 2026). Manual tool: nothing runs it in CI.
/// Pins the rules that matter most: a 400+ score is never clipped, a score change needs a finding change, a reviewer-changed
/// /500 stays in its corridor while an untouched one is never rewritten, and removing a Critical raises a tutor flag.
/// </summary>
public sealed class WritingReviewApplierTests
{
    private static readonly WritingReviewOptions Policy = new();

    private static WritingAssessmentRuleFinding Finding(string severity, string criterion = "content", string quote = "fatigue")
        => new(
            RuleId: "AI." + criterion,
            Category: criterion,
            Severity: severity,
            Message: "The wording does not match the case notes.",
            Quote: quote,
            FixSuggestion: "Use the case-note wording.",
            StartOffset: 0,
            EndOffset: quote.Length,
            PrimaryCriterionCode: criterion,
            ProvenanceTag: WritingProvenanceTags.OetOfficial,
            CandidateBehavior: WritingCandidateBehaviors.ScoreBearing);

    private static WritingReviewRequest Request(WritingReviewScores primary, params WritingAssessmentRuleFinding[] findings)
        => new(
            Guid.NewGuid(),
            "user-1",
            1,
            null,
            "practice",
            "medicine",
            "routine_referral",
            "Write a referral letter.",
            "Presenting complaint: fatigue.",
            "Dear Dr Smith, the patient has fatigue.",
            primary,
            "claude-opus-5-5",
            findings.Select((f, index) => WritingReviewFinding.From($"f{index + 1}", WritingReviewFindingOrigin.Ai, f)).ToList(),
            [],
            [],
            null,
            (_, _) => Task.CompletedTask,
            WritingReviewMode.Enforce);

    private static WritingReviewDecision Decision(
        IReadOnlyList<WritingReviewFindingVerdict>? findings = null,
        IReadOnlyDictionary<string, int>? scores = null,
        int? scaled = null,
        IReadOnlyList<WritingReviewScoreChange>? changes = null)
        => new("corrected", null, findings ?? [], [], [], scores, changes ?? [], scaled, [], null, [], "{}");

    private static WritingReviewFindingVerdict Verdict(string id, string verdict)
        => new(id, verdict, null, null, null, null, "internal reason");

    private static Dictionary<string, int> Scores(int c1, int c2, int c3, int c4, int c5, int c6) => new()
    {
        ["purpose"] = c1,
        ["content"] = c2,
        ["conciseness_clarity"] = c3,
        ["genre_style"] = c4,
        ["organisation_layout"] = c5,
        ["language"] = c6,
    };

    [Fact]
    public void A_430_with_no_defects_is_published_as_430_and_never_clipped()
    {
        var primary = new WritingReviewScores(3, 6, 6, 6, 6, 6, 430);

        var outcome = WritingReviewApplier.Apply(Request(primary), Decision(), Policy, enhancedPass: true);

        Assert.Equal(430, outcome.Scores.ScaledScore);
        Assert.Empty(outcome.EnhancedFailures);
        Assert.Equal("B", outcome.Scores.Band);
    }

    [Fact]
    public void A_400_plus_with_a_surviving_major_finding_fails_the_enhanced_checks_instead_of_being_clipped()
    {
        var primary = new WritingReviewScores(3, 6, 6, 6, 6, 6, 400);

        var outcome = WritingReviewApplier.Apply(Request(primary, Finding("major")), Decision(), Policy, enhancedPass: true);

        Assert.Equal(400, outcome.Scores.ScaledScore);
        Assert.Contains("critical_or_major_finding", outcome.EnhancedFailures);
    }

    [Fact]
    public void A_score_decrease_without_a_cited_surviving_finding_is_rejected()
    {
        var primary = new WritingReviewScores(3, 6, 5, 5, 6, 6, 380);
        var request = Request(primary, Finding("major"));

        var rejected = WritingReviewApplier.Apply(request, Decision(scores: Scores(3, 5, 5, 5, 6, 6)), Policy, enhancedPass: false);
        Assert.Equal(6, rejected.Scores.C2);
        Assert.Contains("scores.content:decrease_unsupported", rejected.Notes.Rejected);

        var cited = Decision(
            scores: Scores(3, 5, 5, 5, 6, 6),
            changes: [new WritingReviewScoreChange("content", 6, 5, ["f1"])]);
        var accepted = WritingReviewApplier.Apply(request, cited, Policy, enhancedPass: false);
        Assert.Equal(5, accepted.Scores.C2);
        Assert.Equal(30, accepted.Scores.RawTotal);
    }

    [Fact]
    public void A_score_change_larger_than_the_cap_is_rejected()
    {
        var primary = new WritingReviewScores(3, 6, 5, 5, 6, 6, 380);
        var request = Request(primary, Finding("major"));
        var decision = Decision(
            scores: Scores(3, 3, 5, 5, 6, 6),
            changes: [new WritingReviewScoreChange("content", 6, 3, ["f1"])]);

        var outcome = WritingReviewApplier.Apply(request, decision, Policy, enhancedPass: false);

        Assert.Equal(6, outcome.Scores.C2);
        Assert.Contains("scores.content:delta_over_cap", outcome.Notes.Rejected);
    }

    [Fact]
    public void Removing_a_critical_finding_raises_the_tutor_override_flag()
    {
        var primary = new WritingReviewScores(3, 5, 5, 5, 6, 6, 380);
        var request = Request(primary, Finding("critical"));

        var outcome = WritingReviewApplier.Apply(request, Decision([Verdict("f1", "false_positive")]), Policy, enhancedPass: false);

        Assert.Empty(outcome.Findings);
        Assert.Contains("rv_override", outcome.TutorReasons);
    }

    [Fact]
    public void A_reviewer_changed_scaled_score_is_held_inside_the_corridor_around_the_raw_total()
    {
        // Raw 30: the corridor is 10 x 30 + 20 = 320 to 10 x 30 + 100 = 400. A primary of 430 is already outside it
        // (and inside the 400+ verification zone), so the reviewer's recalibration is a justified change.
        var primary = new WritingReviewScores(3, 5, 5, 5, 6, 6, 430);

        var outcome = WritingReviewApplier.Apply(Request(primary), Decision(scaled: 440), Policy, enhancedPass: false);

        Assert.Equal(400, outcome.Scores.ScaledScore);
        Assert.Contains("consistency_enforced", outcome.Notes.Flags);
    }

    [Fact]
    public void A_different_scaled_score_with_no_criterion_or_finding_change_is_an_opinion_and_is_rejected()
    {
        // Raw 30 and 380 sit inside the corridor and below 400: nothing justifies a different /500.
        var primary = new WritingReviewScores(3, 5, 5, 5, 6, 6, 380);

        var outcome = WritingReviewApplier.Apply(Request(primary), Decision(scaled: 340), Policy, enhancedPass: false);

        Assert.Equal(380, outcome.Scores.ScaledScore);
        Assert.Contains("scaled:unjustified", outcome.Notes.Rejected);
    }

    [Fact]
    public void A_template_echo_of_zero_for_the_scaled_score_never_lowers_a_graded_letter()
    {
        var primary = new WritingReviewScores(3, 6, 6, 6, 6, 6, 430);

        var outcome = WritingReviewApplier.Apply(Request(primary), Decision(scaled: 0), Policy, enhancedPass: false);

        Assert.Equal(430, outcome.Scores.ScaledScore);
        Assert.Contains("scaled:placeholder_zero", outcome.Notes.Rejected);
    }

    [Fact]
    public void A_primary_scaled_score_the_reviewer_did_not_touch_is_never_rewritten()
    {
        var primary = new WritingReviewScores(1, 2, 2, 2, 2, 2, 450);

        var outcome = WritingReviewApplier.Apply(Request(primary), Decision(), Policy, enhancedPass: false);

        Assert.Equal(450, outcome.Scores.ScaledScore);
        Assert.Contains("primary_outside_corridor", outcome.Notes.Flags);
    }

    [Fact]
    public void The_parser_reads_a_reply_wrapped_in_prose_and_drops_unknown_finding_ids()
    {
        const string reply = "My review:\n{ \"decision\": \"corrected\", \"findings\": [ { \"id\": \"f1\", \"verdict\": \"false_positive\" }, { \"id\": \"f99\", \"verdict\": \"advisory\" } ], \"estimatedScaledScore\": \"360\" }\nEnd.";

        Assert.True(WritingReviewDecisionParser.TryParse(reply, ["f1"], out var decision));

        Assert.Equal("corrected", decision.Decision);
        Assert.Equal("f1", Assert.Single(decision.Findings).Id);
        Assert.Equal(360, decision.EstimatedScaledScore);
        Assert.Contains("unknown_finding_id:f99", decision.Anomalies);
        Assert.False(WritingReviewDecisionParser.TryParse("no json here", ["f1"], out _));
    }

    [Fact]
    public void Review_resource_slots_never_collide_with_each_other_or_the_grade_slots()
    {
        var review = (from epoch in Enumerable.Range(0, 40)
                      from pass in new[] { 0, 1, 2, 3 }
                      from attempt in new[] { 0, 1 }
                      select WritingGradeChain.ReviewResourceVersion(epoch, pass, attempt)).OrderBy(v => v).ToList();

        Assert.Equal(review.Count, review.Distinct().Count());
        Assert.All(review.Zip(review.Skip(1)), pair => Assert.True(pair.Second - pair.First >= 16));
        var grade = (from epoch in Enumerable.Range(0, 40)
                     from hop in Enum.GetValues<WritingGradeHop>()
                     from attempt in new[] { 0, 1 }
                     select WritingGradeChain.ResourceVersion(epoch, hop, attempt)).ToHashSet();
        Assert.DoesNotContain(review, grade.Contains);
    }
}
