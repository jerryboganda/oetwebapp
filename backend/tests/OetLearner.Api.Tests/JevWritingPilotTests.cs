using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Ai.TypeSafe;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Tests;

/// <summary>
/// Phase-1 Writing-pilot decision logic: guard thresholds (block / review /
/// proceed, negative gate only), route confidence gating (redirect only when
/// clear; caller's request stands otherwise), verify fan-out (one call, one
/// Choice per finding, tutor-review flag), advisory merge (extra inert field
/// per c1..c6 object), and flag-off no-ops (zero judgment calls).
/// </summary>
public sealed class JevWritingPilotTests
{
    private const string Letter = "Dear Dr Rahman,\n\nRe: Mr K Osei, DO 14 March 1958\n\nChest pain for two days. ECG shows ST elevation. Please review urgently.\n\nYours sincerely,\nDr Test";

    private static TypeSafeOptions Options(Action<TypeSafeOptions>? configure = null)
    {
        var opts = new TypeSafeOptions
        {
            Enabled = true,
            ApiKey = "apikey_test",
            WritingGuardEnabled = true,
            WritingRouteEnabled = true,
            WritingVerifyEnabled = true,
            WritingCriteriaEnabled = true,
        };
        configure?.Invoke(opts);
        return opts;
    }

    private static IJevWritingPilot Pilot(FakeJudgments judgments, TypeSafeOptions? opts = null) =>
        new JevWritingPilot(judgments, Microsoft.Extensions.Options.Options.Create(opts ?? Options()), NullLogger<JevWritingPilot>.Instance);

    private static JevJudgmentResult Ok(params (string Id, JevAnswer Answer)[] answers) =>
        new(JevCallStatus.Ok, "jev-1.13.0", answers.ToDictionary(a => a.Id, a => a.Answer), 500, 10, null);

    private static JevAnswer Noul(double p) => new(JevQuestionKind.Noul, new JevNoulAnswer(p), null, null);

    private static JevAnswer Choice(string choice, double confidence) =>
        new(JevQuestionKind.Choice, null, new JevChoiceAnswer(choice, new Dictionary<string, double>(), confidence), null);

    // ── Guard ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Guard_BlocksWhenAnySignalAtOrAboveBlockThreshold()
    {
        var fake = new FakeJudgments(Ok(
            ("jev_injection", Noul(0.99)),
            ("jev_rule_evasion", Noul(0.01)),
            ("jev_abuse", Noul(0.02)),
            ("jev_gibberish", Noul(0.01))));

        var result = await Pilot(fake).GuardSubmissionAsync(Letter, "urgent referral", "user-1", CancellationToken.None);

        Assert.Equal(WritingGuardDecision.Block, result.Decision);
        Assert.Equal("jev_injection", result.TriggeredSignal);
        Assert.Equal(1, fake.Calls);
    }

    [Fact]
    public async Task Guard_ReviewsBetweenThresholds_AndProceedsBelow()
    {
        var review = new FakeJudgments(Ok(
            ("jev_injection", Noul(0.55)), ("jev_rule_evasion", Noul(0.1)),
            ("jev_abuse", Noul(0.1)), ("jev_gibberish", Noul(0.1))));
        var clean = new FakeJudgments(Ok(
            ("jev_injection", Noul(0.05)), ("jev_rule_evasion", Noul(0.1)),
            ("jev_abuse", Noul(0.1)), ("jev_gibberish", Noul(0.1))));

        Assert.Equal(WritingGuardDecision.Review, (await Pilot(review).GuardSubmissionAsync(Letter, "t", null, CancellationToken.None)).Decision);
        Assert.Equal(WritingGuardDecision.Proceed, (await Pilot(clean).GuardSubmissionAsync(Letter, "t", null, CancellationToken.None)).Decision);
    }

    [Fact]
    public async Task Guard_IsNegativeGateOnly_NeverBlocksBelowBlockThreshold()
    {
        // Even a "perfect" score everywhere else cannot block: only the four
        // guard signals decide, and the decision ladder is block/review/none.
        var fake = new FakeJudgments(Ok(
            ("jev_injection", Noul(0.79)), ("jev_rule_evasion", Noul(0.79)),
            ("jev_abuse", Noul(0.79)), ("jev_gibberish", Noul(0.79))));

        var result = await Pilot(fake, Options(o => o.GuardBlockThreshold = 0.80)).GuardSubmissionAsync(Letter, "t", null, CancellationToken.None);

        Assert.Equal(WritingGuardDecision.Review, result.Decision);
    }

    [Fact]
    public async Task Guard_FlagOff_MakesNoJudgmentCalls()
    {
        var fake = new FakeJudgments(Ok());

        var result = await Pilot(fake, Options(o =>
        {
            o.WritingGuardEnabled = false;
            o.WritingRouteEnabled = false;
            o.WritingVerifyEnabled = false;
            o.WritingCriteriaEnabled = false;
        })).GuardSubmissionAsync(Letter, "t", null, CancellationToken.None);

        Assert.Equal(WritingGuardDecision.Proceed, result.Decision);
        Assert.Equal(JevCallStatus.Disabled, result.Status);
        Assert.Equal(0, fake.Calls);
    }

    [Fact]
    public async Task Guard_JudgmentUnavailable_Proceeds()
    {
        var fake = new FakeJudgments(JevJudgmentResult.Unavailable("jev_unavailable"));

        var result = await Pilot(fake).GuardSubmissionAsync(Letter, "t", null, CancellationToken.None);

        Assert.Equal(WritingGuardDecision.Proceed, result.Decision);
    }

    [Fact]
    public async Task Guard_ClientCrash_ProceedsNeverThrows()
    {
        var fake = new FakeJudgments(Ok()) { Throw = true };

        var result = await Pilot(fake).GuardSubmissionAsync(Letter, "t", null, CancellationToken.None);

        Assert.Equal(WritingGuardDecision.Proceed, result.Decision);
    }

    // ── Route ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Route_RedirectsOnlyAboveConfidenceThreshold()
    {
        var clear = new FakeJudgments(Ok(("route", Choice("writing_coach_suggest", 0.95))));
        var murky = new FakeJudgments(Ok(("route", Choice("writing_coach_suggest", 0.40))));

        var redirected = await Pilot(clear).RouteWritingRequestAsync("score", Letter, null, CancellationToken.None);
        var kept = await Pilot(murky).RouteWritingRequestAsync("score", Letter, null, CancellationToken.None);

        Assert.Equal(WritingRouteTarget.CoachSuggest, redirected.Redirect);
        Assert.Null(kept.Redirect);
        Assert.Equal(WritingRouteTarget.CoachSuggest, kept.Target); // observed but not applied
    }

    [Fact]
    public async Task Route_UnclearNeverRedirects()
    {
        var fake = new FakeJudgments(Ok(("route", Choice("unclear", 0.99))));

        var result = await Pilot(fake).RouteWritingRequestAsync("score", Letter, null, CancellationToken.None);

        Assert.Null(result.Redirect);
    }

    [Fact]
    public void RouteToTask_MapsTargetsAndKeepsOriginalForNone()
    {
        Assert.Equal(AiTaskMode.Score, JevWritingPilot.RouteToTask(WritingRouteTarget.Grade, AiTaskMode.Coach));
        Assert.Equal(AiTaskMode.Correct, JevWritingPilot.RouteToTask(WritingRouteTarget.CoachSuggest, AiTaskMode.Score));
        Assert.Equal(AiTaskMode.Coach, JevWritingPilot.RouteToTask(WritingRouteTarget.CoachExplain, AiTaskMode.Score));
        Assert.Equal(AiTaskMode.Summarise, JevWritingPilot.RouteToTask(WritingRouteTarget.None, AiTaskMode.Summarise));
    }

    // ── Verify ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Verify_FlagsContradictedAndWeakSupported()
    {
        var fake = new FakeJudgments(Ok(
            ("finding_0", Choice("supported", 0.95)),
            ("finding_1", Choice("contradicted", 0.9)),
            ("finding_2", Choice("supported", 0.2)),
            ("finding_3", Choice("not_in_evidence", 0.8))));

        var result = await Pilot(fake).VerifyFindingsAsync(Letter, Findings(4), null, CancellationToken.None);

        Assert.True(result.FlagsTutorReview);
        Assert.Equal(WritingVerifyVerdict.Supported, result.Verdicts[0].Verdict);
        Assert.Equal(WritingVerifyVerdict.Contradicted, result.Verdicts[1].Verdict);
        Assert.Equal(WritingVerifyVerdict.Supported, result.Verdicts[2].Verdict); // supported but weak
        Assert.Equal(WritingVerifyVerdict.NotInEvidence, result.Verdicts[3].Verdict);
    }

    [Fact]
    public async Task Verify_AllSupportedAtConfidence_DoesNotFlag()
    {
        var fake = new FakeJudgments(Ok(
            ("finding_0", Choice("supported", 0.9)),
            ("finding_1", Choice("supported", 0.85))));

        var result = await Pilot(fake).VerifyFindingsAsync(Letter, Findings(2), null, CancellationToken.None);

        Assert.False(result.FlagsTutorReview);
    }

    [Fact]
    public async Task Verify_SendsOneCallWithOneQuestionPerFinding()
    {
        var fake = new FakeJudgments(Ok(
            ("finding_0", Choice("supported", 0.9)),
            ("finding_1", Choice("supported", 0.9))));

        _ = await Pilot(fake).VerifyFindingsAsync(Letter, Findings(2), null, CancellationToken.None);

        Assert.Equal(1, fake.Calls);
        Assert.Equal(2, fake.LastRequest!.Questions.Count);
        Assert.All(fake.LastRequest.Questions, q => Assert.Equal(JevQuestionKind.Choice, q.Kind));
    }

    [Fact]
    public async Task Verify_CapsFindingsPerCall()
    {
        var answers = Enumerable.Range(0, 3)
            .Select(i => ($"finding_{i}", Choice("supported", 0.9)))
            .ToArray();
        var fake = new FakeJudgments(Ok(answers));

        var result = await Pilot(fake, Options(o => o.VerifyMaxFindingsPerCall = 3))
            .VerifyFindingsAsync(Letter, Findings(10), null, CancellationToken.None);

        Assert.Equal(1, fake.Calls);
        Assert.Equal(3, fake.LastRequest!.Questions.Count);
        Assert.Equal(3, result.Verdicts.Count);
    }

    // ── Criteria advisory ───────────────────────────────────────────────────

    [Fact]
    public async Task Criteria_ReturnsScoresPerCriterion()
    {
        var answers = new (string, JevAnswer)[]
        {
            ("c1_purpose", new JevAnswer(JevQuestionKind.Score, null, null, new JevScoreAnswer(3.2, new Dictionary<string, double>(), 0.8))),
            ("c6_language", new JevAnswer(JevQuestionKind.Score, null, null, new JevScoreAnswer(1.4, new Dictionary<string, double>(), 0.7))),
        };
        var fake = new FakeJudgments(Ok(answers));

        var result = await Pilot(fake).ScoreCriteriaAsync(Letter, "urgent_referral", null, CancellationToken.None);

        Assert.Equal(JevCallStatus.Ok, result.Status);
        Assert.Equal(3.2, result.AdvisoryScores["c1"]);
        Assert.Equal(1.4, result.AdvisoryScores["c6"]);
        Assert.Equal(6, fake.LastRequest!.Questions.Count);
    }

    [Fact]
    public void MergeAdvisory_AddsInertFieldWithoutDisturbingKnownFields()
    {
        const string original = """{"c1":{"score":4,"feedback":"ok","citedRuleIds":["r1"]},"c2":{"score":3,"feedback":""}}""";
        var pilot = Pilot(new FakeJudgments(Ok()));

        var merged = pilot.MergeAdvisoryIntoPerCriterionJson(original, new Dictionary<string, double> { ["c1"] = 3.4, ["c9_unknown"] = 1.0 });

        var root = System.Text.Json.JsonDocument.Parse(merged).RootElement;
        Assert.Equal(2, root.EnumerateObject().Count()); // no new keys
        var c1 = root.GetProperty("c1");
        Assert.Equal(4, c1.GetProperty("score").GetInt32());
        Assert.Equal("ok", c1.GetProperty("feedback").GetString());
        Assert.Equal(3.4, c1.GetProperty("jevAdvisory").GetDouble());
        Assert.False(root.GetProperty("c2").TryGetProperty("jevAdvisory", out _));
    }

    // ── helpers ─────────────────────────────────────────────────────────────

    private static List<WritingFindingInput> Findings(int count) =>
        Enumerable.Range(0, count)
            .Select(i => new WritingFindingInput($"finding_{i}", $"Claim {i} about the letter.", null, $"rule_{i}"))
            .ToList();

    private sealed class FakeJudgments(JevJudgmentResult result) : ITypeSafeJudgmentService
    {
        public int Calls { get; private set; }
        public JevJudgmentRequest? LastRequest { get; private set; }
        public bool Throw { get; init; }

        public Task<JevJudgmentResult> AskAsync(JevJudgmentRequest request, JevCallMetadata call, CancellationToken ct)
        {
            Calls++;
            LastRequest = request;
            if (Throw) throw new InvalidOperationException("boom");
            return Task.FromResult(result);
        }
    }
}
