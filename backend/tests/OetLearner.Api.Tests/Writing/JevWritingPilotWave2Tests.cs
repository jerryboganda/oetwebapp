using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Configuration;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Ai.TypeSafe;

namespace OetLearner.Api.Tests.Writing;

/// <summary>
/// Wave 2 Writing-pilot decision logic: the outcome cross-check (Jev answers confidently AGAINST the
/// grader or it stays silent), the criteria descriptor scale and divergence rules, finding
/// classification (criterion Choice with heuristic fallback + valid-alternative Noul), and the
/// guarantee that every new surface makes zero judgment calls while its flag is off and never throws.
/// </summary>
public sealed class JevWritingPilotWave2Tests
{
    private const string Letter = "Dear Dr Rahman,\n\nRe: Mr K Osei, DOB 14 March 1958\n\nChest pain for two days. Please review urgently.\n\nYours sincerely,\nDr Test";

    private static TypeSafeOptions Flags(Action<TypeSafeOptions>? configure = null)
    {
        var opts = new TypeSafeOptions
        {
            Enabled = true,
            ApiKey = "apikey_test",
            WritingOutcomeEnabled = true,
            WritingFindingsEnabled = true,
            WritingCriteriaEnabled = true,
        };
        configure?.Invoke(opts);
        return opts;
    }

    private static IJevWritingPilot Pilot(ScriptedJudgments judgments, TypeSafeOptions? opts = null) =>
        new JevWritingPilot(judgments, Microsoft.Extensions.Options.Options.Create(opts ?? Flags()), NullLogger<JevWritingPilot>.Instance);

    private static JevJudgmentResult Answer(JevJudgmentRequest request, Func<JevQuestion, JevAnswer> answerFor) =>
        new(JevCallStatus.Ok, "jev-1.13.0", request.Questions.ToDictionary(q => q.Id, answerFor), 500, 10, null);

    private static JevAnswer Noul(double p) => new(JevQuestionKind.Noul, new JevNoulAnswer(p), null, null);

    private static JevAnswer Choice(string choice, double confidence) =>
        new(JevQuestionKind.Choice, null, new JevChoiceAnswer(choice, new Dictionary<string, double>(), confidence), null);

    private static JevAnswer Score(double position, double confidence) =>
        new(JevQuestionKind.Score, null, null, new JevScoreAnswer(position, new Dictionary<string, double>(), confidence));

    // ── Outcome cross-check ─────────────────────────────────────────────────

    [Theory]
    [InlineData(false, 0.85, true)]
    [InlineData(false, 0.70, true)]
    [InlineData(false, 0.69, false)]
    [InlineData(false, 0.50, false)]
    [InlineData(false, 0.10, false)]
    [InlineData(true, 0.05, true)]
    [InlineData(true, 0.20, true)]
    [InlineData(true, 0.35, false)]
    [InlineData(true, 0.50, false)]
    [InlineData(true, 0.90, false)]
    public void OutcomeFlips_OnlyWhenJevIsConfidentlyAgainstTheGrader(bool graderPassed, double passProbability, bool expected)
    {
        Assert.Equal(expected, JevWritingPilot.OutcomeFlips(passProbability, graderPassed, 0.70));
    }

    [Theory]
    [InlineData(false, 0.90, true)]
    [InlineData(false, 0.50, false)]
    [InlineData(false, 0.20, false)]
    [InlineData(true, 0.10, true)]
    [InlineData(true, 0.50, false)]
    [InlineData(true, 0.95, false)]
    public async Task CheckOutcome_FlagsOnlyOnAConfidentDisagreement(bool graderPassed, double jevPassProbability, bool expectedFlag)
    {
        var fake = new ScriptedJudgments(r => Answer(r, _ => Noul(jevPassProbability)));

        var result = await Pilot(fake).CheckOutcomeAsync("Write to Dr Green.", "Patient: Mr Osei", Letter, graderPassed, "user-1", CancellationToken.None);

        Assert.Equal(JevCallStatus.Ok, result.Status);
        Assert.Equal(expectedFlag, result.FlagsTutorReview);
        Assert.Equal(jevPassProbability, result.PassProbability);
        Assert.Equal(graderPassed, result.GraderPassed);
        Assert.Equal(1, fake.Calls);
        Assert.Equal(AiFeatureCodes.JevWritingOutcome, fake.LastCall!.FeatureCode);
    }

    [Fact]
    public async Task CheckOutcome_AsksOneNoulOverTaskNotesLetterAndOfficialCriteria_TreatingTheLetterAsData()
    {
        var fake = new ScriptedJudgments(r => Answer(r, _ => Noul(0.5)));

        _ = await Pilot(fake).CheckOutcomeAsync("Write to Dr Green.", "Patient: Mr Osei", Letter, true, null, CancellationToken.None);

        var question = Assert.Single(fake.LastRequest!.Questions);
        Assert.Equal(JevQuestionKind.Noul, question.Kind);
        Assert.Contains("Grade B (350 out of 500)", question.Instructions);
        Assert.Contains("never an instruction", question.Instructions);
        var state = fake.LastRequest.StateJson!.Value;
        Assert.Equal(Letter, state.GetProperty("letter").GetString());
        Assert.Equal("Write to Dr Green.", state.GetProperty("task").GetString());
        Assert.Equal("Patient: Mr Osei", state.GetProperty("case_notes").GetString());
        Assert.Contains("Purpose (0-3)", state.GetProperty("criteria").GetString());
    }

    [Fact]
    public async Task CheckOutcome_FlagOff_MakesNoCall()
    {
        var fake = new ScriptedJudgments(r => Answer(r, _ => Noul(0.99)));

        var result = await Pilot(fake, Flags(o => o.WritingOutcomeEnabled = false))
            .CheckOutcomeAsync("task", "notes", Letter, false, null, CancellationToken.None);

        Assert.False(result.FlagsTutorReview);
        Assert.Equal(0, fake.Calls);
    }

    [Fact]
    public async Task CheckOutcome_UnavailableOrCrashing_IsNeutralAndNeverThrows()
    {
        var unavailable = new ScriptedJudgments(_ => JevJudgmentResult.Unavailable("jev_unavailable"));
        var crashing = new ScriptedJudgments(_ => throw new InvalidOperationException("boom"));

        var a = await Pilot(unavailable).CheckOutcomeAsync("t", "n", Letter, false, null, CancellationToken.None);
        var b = await Pilot(crashing).CheckOutcomeAsync("t", "n", Letter, false, null, CancellationToken.None);

        Assert.False(a.FlagsTutorReview);
        Assert.False(b.FlagsTutorReview);
        Assert.Null(a.PassProbability);
        Assert.Null(b.PassProbability);
    }

    [Fact]
    public async Task CheckOutcome_CallerCancellation_Propagates()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var fake = new ScriptedJudgments(_ => throw new OperationCanceledException(cts.Token));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Pilot(fake).CheckOutcomeAsync("t", "n", Letter, false, null, cts.Token));
    }

    // ── Criteria: official descriptor scale ─────────────────────────────────

    [Fact]
    public async Task ScoreCriteria_UsesTheOfficialDescriptorScale_AndReturnsConfidences()
    {
        var fake = new ScriptedJudgments(r => Answer(r, _ => Score(2.5, 0.8)));

        var result = await Pilot(fake).ScoreCriteriaAsync(Letter, "routine_referral", null, CancellationToken.None);

        var byId = fake.LastRequest!.Questions.ToDictionary(q => q.Id);
        Assert.Equal(6, byId.Count);
        Assert.Equal(4, byId["c1_purpose"].ScoreLevels!.Count); // Purpose 0-3
        foreach (var id in new[] { "c2_content", "c3_conciseness", "c4_genre", "c5_organisation", "c6_language" })
        {
            Assert.Equal(8, byId[id].ScoreLevels!.Count); // one level per score 0-7
        }

        Assert.All(byId.Values, q => Assert.All(q.ScoreLevels!, level => Assert.False(string.IsNullOrWhiteSpace(level))));
        Assert.Equal(6, result.Confidences!.Count);
        Assert.Equal(0.8, result.Confidences["c3"]);
        Assert.Equal(2.5, result.AdvisoryScores["c3"]);
    }

    // ── Criteria: divergence ────────────────────────────────────────────────

    private static readonly IReadOnlyDictionary<string, int> GraderScores = new Dictionary<string, int>
    {
        ["c1"] = 3, ["c2"] = 6, ["c3"] = 5, ["c4"] = 7, ["c5"] = 4, ["c6"] = 6,
    };

    private static WritingCriteriaResult Advisory(Action<Dictionary<string, double>>? tweak = null, double confidence = 0.8)
    {
        var positions = GraderScores.ToDictionary(p => p.Key, p => (double)p.Value);
        tweak?.Invoke(positions);
        return new WritingCriteriaResult(
            positions, JevCallStatus.Ok, null, positions.ToDictionary(p => p.Key, _ => confidence));
    }

    [Fact]
    public void Divergence_TwoCriteriaBeyondTheThreshold_FlagsTutorReview()
    {
        // |6-2.0|/7 = 0.57 and |6-3.0|/7 = 0.43, both over 0.34 at confidence 0.8.
        var advisory = Advisory(p => { p["c2"] = 2.0; p["c6"] = 3.0; });

        var result = Pilot(new ScriptedJudgments(_ => throw new InvalidOperationException())).AssessCriteriaDivergence(GraderScores, advisory);

        Assert.True(result.FlagsTutorReview);
        Assert.Equal(new[] { "c2", "c6" }, result.DivergentCriteria);
    }

    [Fact]
    public void Divergence_OneModerateCriterionAlone_DoesNotFlag()
    {
        var advisory = Advisory(p => p["c2"] = 2.0); // 0.57: over 0.34 but under twice the threshold (0.68)

        var result = Pilot(new ScriptedJudgments(_ => throw new InvalidOperationException())).AssessCriteriaDivergence(GraderScores, advisory);

        Assert.False(result.FlagsTutorReview);
        Assert.Equal(new[] { "c2" }, result.DivergentCriteria);
    }

    [Fact]
    public void Divergence_OneCriterionByTwiceTheThreshold_Flags()
    {
        var advisory = Advisory(p => p["c4"] = 1.5); // |7-1.5|/7 = 0.79 >= 0.68

        var result = Pilot(new ScriptedJudgments(_ => throw new InvalidOperationException())).AssessCriteriaDivergence(GraderScores, advisory);

        Assert.True(result.FlagsTutorReview);
        Assert.Equal(new[] { "c4" }, result.DivergentCriteria);
    }

    [Fact]
    public void Divergence_LowJevConfidence_IsNoSignal()
    {
        var advisory = Advisory(p => { p["c2"] = 1.0; p["c6"] = 1.0; }, confidence: 0.50);

        var result = Pilot(new ScriptedJudgments(_ => throw new InvalidOperationException())).AssessCriteriaDivergence(GraderScores, advisory);

        Assert.False(result.FlagsTutorReview);
        Assert.Empty(result.DivergentCriteria);
    }

    [Fact]
    public void Divergence_PurposeIsNormalisedByItsOwnZeroToThreeScale()
    {
        // |3-1.5|/3 = 0.50 diverges; on a 0-7 scale it would be 0.21 and not diverge.
        var advisory = Advisory(p => p["c1"] = 1.5);

        var result = Pilot(new ScriptedJudgments(_ => throw new InvalidOperationException())).AssessCriteriaDivergence(GraderScores, advisory);

        Assert.Equal(new[] { "c1" }, result.DivergentCriteria);
        Assert.False(result.FlagsTutorReview); // 0.50 < 0.68, a single criterion
    }

    [Fact]
    public void Divergence_AgreementOrNoAdvisory_IsNone()
    {
        var pilot = Pilot(new ScriptedJudgments(_ => throw new InvalidOperationException()));

        Assert.False(pilot.AssessCriteriaDivergence(GraderScores, Advisory()).FlagsTutorReview);
        Assert.False(pilot.AssessCriteriaDivergence(GraderScores, WritingCriteriaResult.Neutral("criteria_disabled")).FlagsTutorReview);
    }

    [Fact]
    public void Divergence_RespectsTheConfiguredThresholds()
    {
        var advisory = Advisory(p => p["c2"] = 2.0); // 0.57
        var strict = Pilot(new ScriptedJudgments(_ => throw new InvalidOperationException()), Flags(o => o.CrosscheckDivergenceThreshold = 0.20));

        // 0.57 >= 2 x 0.20, so a single criterion now flags.
        Assert.True(strict.AssessCriteriaDivergence(GraderScores, advisory).FlagsTutorReview);
    }

    // ── Findings classification ─────────────────────────────────────────────

    private static List<WritingFindingInput> Findings(params bool[] needsCriterion) =>
        needsCriterion
            .Select((needs, i) => new WritingFindingInput($"finding_{i}", $"Claim {i} about the letter.", "quoted words", null, needs))
            .ToList();

    [Fact]
    public async Task Classify_OneCall_ChoiceOnlyWhereNeeded_NoulForEveryFinding()
    {
        var fake = new ScriptedJudgments(r => Answer(r, q => q.Id.StartsWith("crit_", StringComparison.Ordinal) ? Choice("language", 0.9) : Noul(0.1)));

        var result = await Pilot(fake).ClassifyFindingsAsync(Letter, Findings(true, false, true), "user-1", CancellationToken.None);

        Assert.Equal(1, fake.Calls);
        Assert.Equal(AiFeatureCodes.JevWritingFindings, fake.LastCall!.FeatureCode);
        Assert.Equal(
            new[] { "crit_0", "alt_0", "alt_1", "crit_2", "alt_2" },
            fake.LastRequest!.Questions.Select(q => q.Id));
        var choice = fake.LastRequest.Questions.First(q => q.Id == "crit_0");
        Assert.Equal(JevQuestionKind.Choice, choice.Kind);
        Assert.Equal(7, choice.ChoiceCriteria!.Count); // six criteria + unclear
        Assert.True(choice.ChoiceCriteria.ContainsKey("organisation_layout"));
        Assert.True(choice.ChoiceCriteria.ContainsKey("unclear"));

        Assert.Equal(JevCallStatus.Ok, result.Status);
        Assert.Equal("language", result.Items[0].Criterion);
        Assert.Null(result.Items[1].Criterion); // grader-assigned: never reclassified
        Assert.Equal("language", result.Items[2].Criterion);
        Assert.False(result.FlagsTutorReview);
    }

    [Theory]
    [InlineData("language", 0.40)]      // below the cross-check confidence floor
    [InlineData("unclear", 0.95)]       // Jev itself says no clear criterion
    [InlineData("not_a_criterion", 0.95)] // outside the closed set
    public async Task Classify_LowConfidenceUnclearOrUnknown_KeepsTheHeuristic(string chosen, double confidence)
    {
        var fake = new ScriptedJudgments(r => Answer(r, q => q.Id.StartsWith("crit_", StringComparison.Ordinal) ? Choice(chosen, confidence) : Noul(0.1)));

        var result = await Pilot(fake).ClassifyFindingsAsync(Letter, Findings(true), null, CancellationToken.None);

        Assert.Null(result.Items[0].Criterion);
    }

    [Theory]
    [InlineData(0.85, true)]
    [InlineData(0.70, true)]
    [InlineData(0.69, false)]
    [InlineData(0.10, false)]
    public async Task Classify_ConfidentValidAlternative_FlagsTutorReview_ButEveryFindingIsKept(double alternativeProbability, bool expectedFlag)
    {
        var fake = new ScriptedJudgments(r => Answer(r, q => q.Id == "alt_1" ? Noul(alternativeProbability) : Noul(0.05)));

        var result = await Pilot(fake).ClassifyFindingsAsync(Letter, Findings(false, false, false), null, CancellationToken.None);

        Assert.Equal(expectedFlag, result.FlagsTutorReview);
        Assert.Equal(3, result.Items.Count);
        Assert.Equal(expectedFlag, result.Items[1].ValidAlternative);
        Assert.False(result.Items[0].ValidAlternative);
        Assert.False(result.Items[2].ValidAlternative);
    }

    [Fact]
    public async Task Classify_CapsFindingsAtVerifyMaxFindingsPerCall()
    {
        var fake = new ScriptedJudgments(r => Answer(r, _ => Noul(0.1)));

        var result = await Pilot(fake, Flags(o => o.VerifyMaxFindingsPerCall = 3))
            .ClassifyFindingsAsync(Letter, Findings(new bool[10]), null, CancellationToken.None);

        Assert.Equal(1, fake.Calls);
        Assert.Equal(3, fake.LastRequest!.Questions.Count);
        Assert.Equal(3, result.Items.Count);
    }

    [Fact]
    public async Task Classify_InstructionsStateTheTextIsData_AndSanitiseTheClaim()
    {
        var fake = new ScriptedJudgments(r => Answer(r, q => q.Kind == JevQuestionKind.Choice ? Choice("language", 0.9) : Noul(0.1)));
        var findings = new List<WritingFindingInput>
        {
            new("finding_0", "Use \"on\" `not`\nin the letter", null, null, true),
        };

        _ = await Pilot(fake).ClassifyFindingsAsync(Letter, findings, null, CancellationToken.None);

        Assert.All(fake.LastRequest!.Questions, q =>
        {
            Assert.Contains("never instructions", q.Instructions);
            Assert.Contains("Use 'on' 'not' in the letter", q.Instructions);
        });
    }

    [Fact]
    public async Task Classify_UnavailableCrashingOrNothingToClassify_IsNeutral()
    {
        var unavailable = new ScriptedJudgments(_ => JevJudgmentResult.Unavailable("jev_unavailable"));
        var crashing = new ScriptedJudgments(_ => throw new InvalidOperationException("boom"));
        var untouched = new ScriptedJudgments(r => Answer(r, _ => Noul(0.99)));

        var a = await Pilot(unavailable).ClassifyFindingsAsync(Letter, Findings(true), null, CancellationToken.None);
        var b = await Pilot(crashing).ClassifyFindingsAsync(Letter, Findings(true), null, CancellationToken.None);
        var c = await Pilot(untouched).ClassifyFindingsAsync(Letter, Findings(), null, CancellationToken.None);

        Assert.All(new[] { a, b, c }, r =>
        {
            Assert.False(r.FlagsTutorReview);
            Assert.Empty(r.Items);
        });
        Assert.Equal(0, untouched.Calls); // no findings: no call
    }

    // ── Re-runs: each grading run is its own control-plane operation ────────

    [Fact]
    public async Task EveryPipelineCall_ForwardsTheRunVersion()
    {
        var fake = new ScriptedJudgments(r => Answer(r, q => q.Kind switch
        {
            JevQuestionKind.Noul => Noul(0.1),
            JevQuestionKind.Choice => Choice("supported", 0.9),
            _ => Score(1, 0.9),
        }));
        var pilot = Pilot(fake, Flags(o =>
        {
            o.WritingGuardEnabled = true;
            o.WritingVerifyEnabled = true;
        }));
        var findings = Findings(true);

        _ = await pilot.GuardSubmissionAsync(Letter, "t", null, CancellationToken.None, resourceVersion: 7);
        Assert.Equal(7, fake.LastCall!.ResourceVersion);
        _ = await pilot.VerifyFindingsAsync(Letter, findings, null, CancellationToken.None, resourceVersion: 7);
        Assert.Equal(7, fake.LastCall.ResourceVersion);
        _ = await pilot.ClassifyFindingsAsync(Letter, findings, null, CancellationToken.None, resourceVersion: 7);
        Assert.Equal(7, fake.LastCall.ResourceVersion);
        _ = await pilot.ScoreCriteriaAsync(Letter, "routine_referral", null, CancellationToken.None, resourceVersion: 7);
        Assert.Equal(7, fake.LastCall.ResourceVersion);
        _ = await pilot.CheckOutcomeAsync("t", "n", Letter, true, null, CancellationToken.None, resourceVersion: 7);
        Assert.Equal(7, fake.LastCall.ResourceVersion);

        // No version given: the call carries none, exactly as before.
        _ = await pilot.CheckOutcomeAsync("t", "n", Letter, true, null, CancellationToken.None);
        Assert.Null(fake.LastCall.ResourceVersion);
    }

    // ── Flags off: zero judgment calls ──────────────────────────────────────

    [Fact]
    public async Task EveryWave2Surface_WithItsFlagOff_MakesZeroJudgmentCalls()
    {
        var fake = new ScriptedJudgments(r => Answer(r, _ => Noul(0.99)));
        var pilot = Pilot(fake, new TypeSafeOptions { Enabled = true, ApiKey = "apikey_test" }); // every Jev flag at its default: off

        var outcome = await pilot.CheckOutcomeAsync("t", "n", Letter, true, null, CancellationToken.None);
        var findings = await pilot.ClassifyFindingsAsync(Letter, Findings(true, true), null, CancellationToken.None);
        var criteria = await pilot.ScoreCriteriaAsync(Letter, "routine_referral", null, CancellationToken.None);
        var divergence = pilot.AssessCriteriaDivergence(GraderScores, criteria);

        Assert.Equal(0, fake.Calls);
        Assert.False(outcome.FlagsTutorReview);
        Assert.False(findings.FlagsTutorReview);
        Assert.False(divergence.FlagsTutorReview);
    }

    // ── helpers ─────────────────────────────────────────────────────────────

    private sealed class ScriptedJudgments(Func<JevJudgmentRequest, JevJudgmentResult> script) : ITypeSafeJudgmentService
    {
        public int Calls { get; private set; }
        public JevJudgmentRequest? LastRequest { get; private set; }
        public JevCallMetadata? LastCall { get; private set; }

        public Task<JevJudgmentResult> AskAsync(JevJudgmentRequest request, JevCallMetadata call, CancellationToken ct)
        {
            Calls++;
            LastRequest = request;
            LastCall = call;
            return Task.FromResult(script(request));
        }
    }
}
