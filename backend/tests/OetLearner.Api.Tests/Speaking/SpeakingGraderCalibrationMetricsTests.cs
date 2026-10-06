using OetLearner.Api.Services;
using OetLearner.Api.Services.Speaking;

namespace OetLearner.Api.Tests.Speaking;

/// <summary>
/// How closely the AI grader agrees with the OET expert (owner spec 4 Oct 2026): the metrics, the fitted score map and the
/// pass thresholds, over synthetic expert marks and grades. Pure: no database, no model.
/// </summary>
public sealed class SpeakingGraderCalibrationMetricsTests
{
    private static readonly string[] Codes =
    [
        "intelligibility", "fluency", "appropriateness", "grammarExpression",
        "relationshipBuilding", "patientPerspective", "structure", "informationGathering", "informationGiving",
    ];

    private static readonly int[] Max = [6, 6, 6, 6, 3, 3, 3, 3, 3];

    /// <summary>Nine criterion scores that add up to <paramref name="raw"/>: one point at a time, round-robin, within each maximum.</summary>
    private static Dictionary<string, int> ScoresFor(int raw)
    {
        var scores = Codes.ToDictionary(c => c, _ => 0);
        var left = raw;
        while (left > 0)
        {
            var progressed = false;
            for (var i = 0; i < Codes.Length && left > 0; i++)
            {
                if (scores[Codes[i]] >= Max[i]) continue;
                scores[Codes[i]]++;
                left--;
                progressed = true;
            }

            if (!progressed) break;
        }

        return scores;
    }

    /// <summary>36 marked performances: eighteen raw totals twice each, spread over every grade and the pass line (ten of them
    /// marked 320-380), overall from the current map (a monotone expert), all with audio.</summary>
    private static List<SpeakingCalibrationExpert> Experts(bool withAudio = true)
    {
        var raws = new[] { 0, 3, 6, 10, 14, 18, 20, 22, 24, 25, 26, 27, 28, 29, 30, 33, 36, 39 };
        var experts = new List<SpeakingCalibrationExpert>();
        foreach (var raw in raws)
        {
            for (var twin = 1; twin <= 2; twin++)
            {
                experts.Add(new SpeakingCalibrationExpert($"s{raw:00}-{twin}", withAudio, ScoresFor(raw), OetScoring.SpeakingRawToReported[raw]));
            }
        }

        return experts;
    }

    private static List<SpeakingCalibrationObservation> Grades(
        IEnumerable<SpeakingCalibrationExpert> experts,
        int repeats = 2,
        Func<SpeakingCalibrationExpert, int, Dictionary<string, int>>? scores = null,
        string source = "audio")
    {
        var observations = new List<SpeakingCalibrationObservation>();
        foreach (var expert in experts)
        {
            for (var repeat = 1; repeat <= repeats; repeat++)
            {
                observations.Add(new SpeakingCalibrationObservation(
                    expert.SampleId, repeat, scores?.Invoke(expert, repeat) ?? new Dictionary<string, int>(expert.Scores), source));
            }
        }

        return observations;
    }

    // ── A grader that agrees with the expert ───────────────────────────────────────────────

    [Fact]
    public void AGraderThatAgreesWithTheExpert_PassesEveryThreshold()
    {
        var experts = Experts();

        var report = SpeakingGraderCalibrationMetrics.Compute(experts, Grades(experts), requireAudio: true);

        Assert.True(report.Verdict.Passed, string.Join("; ", report.Verdict.Failures));
        Assert.Equal(36, report.Performances);
        Assert.Equal(72, report.Observations);
        Assert.Equal(2, report.Repeats);
        Assert.All(report.Criteria, c =>
        {
            Assert.Equal(0, c.Mae);
            Assert.Equal(0, c.Bias);
            Assert.Equal(1.0, c.Exact.Share);
        });
        Assert.Equal(0, report.RawTotal.Mae);
        Assert.Equal(0, report.Mapping.LeaveOneOut.Mae);
        Assert.Equal(1.0, report.Grade.Exact.Share);
        Assert.Equal(1.0, report.PassFail.Agreement.Share);
        Assert.Equal(0, report.PassFail.FalsePass.Hits);
        Assert.Equal(1.0, report.Repeatability!.CriterionRepeat.Share);

        // Right answers sit on the diagonal of the confusion matrix (expert grade down, grader's across).
        Assert.Equal(72, Enumerable.Range(0, 6).Sum(i => report.Grade.Confusion[i][i]));
        Assert.Equal(6 * 6, report.Grade.Confusion.Sum(row => row.Length));

        // The full comparison: every marked performance beside each of its grades.
        Assert.Equal(36, report.Detail!.Count);
        Assert.All(report.Detail, d =>
        {
            Assert.Equal(2, d.Grades.Count);
            Assert.All(d.Grades, g =>
            {
                Assert.Equal(d.ExpertRaw, g.Raw);
                Assert.Equal(d.ExpertGrade, g.Grade);
                Assert.True(Math.Abs(g.ScaledError) <= 100);
            });
        });
    }

    [Fact]
    public void Coverage_CountsGradesTheNearPassLineAndAudio()
    {
        var experts = Experts();

        var coverage = SpeakingGraderCalibrationMetrics.Compute(experts, Grades(experts), true).Coverage;

        Assert.Equal(36, coverage.Labelled);
        Assert.Equal(10, coverage.NearPassLine); // overall 320, 330, 350, 360, 370, twice each (a total of 30 is 390)
        Assert.Equal(1.0, coverage.AudioShare);
        Assert.Equal(6, coverage.PerGrade["E"]);
        Assert.Equal(4, coverage.PerGrade["D"]);
        Assert.Equal(6, coverage.PerGrade["C"]);
        Assert.Equal(6, coverage.PerGrade["C+"]);
        Assert.Equal(10, coverage.PerGrade["B"]);
        Assert.Equal(4, coverage.PerGrade["A"]);
    }

    // ── The approved thresholds are pinned ─────────────────────────────────────────────────

    [Fact]
    public void TheApprovedThresholds_ArePinned_SoNoneCanBeRelaxedToMakeTheGraderPass()
    {
        // Owner decision 2026-10-05: keep them strict. Loosening any of these must fail the build.
        Assert.Equal(0.75, SpeakingGraderCalibrationMetrics.Thresholds.LinguisticMae);
        Assert.Equal(0.5, SpeakingGraderCalibrationMetrics.Thresholds.LinguisticBias);
        Assert.Equal(0.90, SpeakingGraderCalibrationMetrics.Thresholds.LinguisticAdjacent);
        Assert.Equal(0.5, SpeakingGraderCalibrationMetrics.Thresholds.ClinicalMae);
        Assert.Equal(0.35, SpeakingGraderCalibrationMetrics.Thresholds.ClinicalBias);
        Assert.Equal(0.60, SpeakingGraderCalibrationMetrics.Thresholds.ClinicalExact);
        Assert.Equal(0.75, SpeakingGraderCalibrationMetrics.Thresholds.AudioIntelligibilityMae);
        Assert.Equal(30, SpeakingGraderCalibrationMetrics.Thresholds.ScaledMae);
        Assert.Equal(15, SpeakingGraderCalibrationMetrics.Thresholds.ScaledBias);
        Assert.Equal(0.80, SpeakingGraderCalibrationMetrics.Thresholds.ScaledWithin40);
        Assert.Equal(0.70, SpeakingGraderCalibrationMetrics.Thresholds.GradeExact);
        Assert.Equal(0.95, SpeakingGraderCalibrationMetrics.Thresholds.GradeAdjacent);
        Assert.Equal(0.85, SpeakingGraderCalibrationMetrics.Thresholds.PassAgreement);
        Assert.Equal(0.10, SpeakingGraderCalibrationMetrics.Thresholds.FalsePass);
        Assert.Equal(0.80, SpeakingGraderCalibrationMetrics.Thresholds.CriterionRepeat);
        Assert.Equal(0.90, SpeakingGraderCalibrationMetrics.Thresholds.ScaledWithin20);
        Assert.Equal(0.95, SpeakingGraderCalibrationMetrics.Thresholds.PassStable);
        Assert.Equal(2, SpeakingGraderCalibrationMetrics.Thresholds.MinimumRepeats);

        // ...and so is the coverage a report needs.
        Assert.Equal(30, SpeakingGraderCalibrationService.RequiredLabelled);
        Assert.Equal(3, SpeakingGraderCalibrationService.RequiredPerGrade);
        Assert.Equal(10, SpeakingGraderCalibrationService.RequiredNearPassLine);
        Assert.Equal(0.8, SpeakingGraderCalibrationService.RequiredAudioShare);
        Assert.Equal(4, SpeakingGraderCalibrationService.RequiredEachSideOfPassLine);
    }

    [Fact]
    public void AReportWhoseNearPassLineBlockSitsOnOneSideOfTheLine_FailsCoverage()
    {
        // Thirty-six performances, ten of them marked 350-380 and none marked 320-340.
        var raws = new[] { 0, 3, 6, 10, 14, 18, 20, 22, 24, 25, 26, 27, 28, 29, 30, 33, 36, 39 };
        var experts = new List<SpeakingCalibrationExpert>();
        foreach (var raw in raws)
        {
            for (var twin = 1; twin <= 2; twin++)
            {
                var overall = OetScoring.SpeakingRawToReported[raw];
                if (overall is >= 320 and < 350) overall = 360; // pull the "just below" ones above the line
                experts.Add(new SpeakingCalibrationExpert($"s{raw:00}-{twin}", true, ScoresFor(raw), overall));
            }
        }

        var report = SpeakingGraderCalibrationMetrics.Compute(experts, Grades(experts), requireAudio: true);

        Assert.Equal(0, report.Coverage.BelowPassLine);
        Assert.False(report.Verdict.Passed);
        Assert.Contains(report.Verdict.Failures, f => f.Contains("320-340"));
    }

    // ── A grader that does not ─────────────────────────────────────────────────────────────

    [Fact]
    public void AGraderThatScoresEveryLinguisticCriterionTooHigh_FailsOnErrorAndBias()
    {
        var experts = Experts();
        var generous = Grades(experts, scores: (expert, _) => expert.Scores.ToDictionary(
            kv => kv.Key,
            kv => Array.IndexOf(Codes, kv.Key) < 4 ? Math.Min(6, kv.Value + 1) : kv.Value));

        var report = SpeakingGraderCalibrationMetrics.Compute(experts, generous, requireAudio: true);

        Assert.False(report.Verdict.Passed);
        Assert.Contains(report.Verdict.Failures, f => f.StartsWith("intelligibility: mean error"));
        Assert.Contains(report.Verdict.Failures, f => f.StartsWith("fluency: bias"));
        Assert.True(report.Criteria[0].Bias > 0.5);
        // Clinical criteria were untouched and are not blamed.
        Assert.DoesNotContain(report.Verdict.Failures, f => f.StartsWith("structure:"));
        Assert.True(report.Mapping.V0EndToEnd.Bias > 0, "a generous grader inflates the score the platform shows today");
    }

    [Fact]
    public void AnUnstableGrader_FailsRepeatability_EvenWhenItIsRightOnAverage()
    {
        var experts = Experts();
        // Repeat 2 moves one linguistic criterion up or down by two bands in turn: right on average, never repeatable.
        var unstable = Grades(experts, scores: (expert, repeat) =>
        {
            var scores = new Dictionary<string, int>(expert.Scores);
            if (repeat == 2)
            {
                var up = expert.SampleId.Sum(ch => (int)ch) % 2 == 0;
                foreach (var code in Codes.Take(4))
                {
                    scores[code] = Math.Clamp(scores[code] + (up ? 2 : -2), 0, 6);
                }
            }

            return scores;
        });

        var report = SpeakingGraderCalibrationMetrics.Compute(experts, unstable, requireAudio: true);

        Assert.False(report.Verdict.Passed);
        Assert.NotNull(report.Repeatability);
        Assert.True(report.Repeatability!.CriterionRepeat.Share < 0.8);
        Assert.Contains(report.Verdict.Failures, f => f.StartsWith("repeatability:"));
    }

    [Fact]
    public void WithoutASecondGradeOfEachPerformance_RepeatabilityCannotBeShown()
    {
        var experts = Experts();

        var report = SpeakingGraderCalibrationMetrics.Compute(experts, Grades(experts, repeats: 1), requireAudio: true);

        Assert.Null(report.Repeatability);
        Assert.False(report.Verdict.Passed);
        Assert.Contains(report.Verdict.Failures, f => f.Contains("graded at least 2 times"));
        Assert.Contains(report.Verdict.Failures, f => f.Contains("no performance has been graded twice"));
    }

    [Fact]
    public void TooFewMarkedPerformances_IsStatedPlainly_AndNeverPasses()
    {
        var few = Experts().Take(5).ToList();

        var report = SpeakingGraderCalibrationMetrics.Compute(few, Grades(few), requireAudio: true);

        Assert.False(report.Verdict.Passed);
        Assert.Contains(report.Verdict.Failures, f => f.Contains("at least 30 expert-marked performances (has 5)"));
        Assert.Contains(report.Verdict.Failures, f => f.Contains("320-380"));
    }

    [Fact]
    public void APilotRun_ShowsTheComparisonAsAdvisory_AndCanNeverPass()
    {
        // An owner pilot (owner request 7 Oct 2026): the same tiny set, computed in pilot mode. The coverage gates are
        // not applied, every threshold miss becomes an advisory note, and the verdict can never pass — the approved
        // validation thresholds are untouched (validation mode above still fails on all of them).
        var few = Experts().Take(5).ToList();

        var report = SpeakingGraderCalibrationMetrics.Compute(few, Grades(few), requireAudio: true, pilot: true);

        Assert.Equal("pilot", report.Verdict.Mode);
        Assert.False(report.Verdict.Passed); // by design, even when every number agrees
        Assert.Empty(report.Verdict.Failures);
        Assert.Contains(report.Verdict.Advisory ?? [], a => a.Contains("OWNER PILOT"));
        // The numbers the owner reads are all still there.
        Assert.Equal(5, report.Performances);
        Assert.Equal(10, report.Observations);
        Assert.Equal(9, report.Criteria.Count);
        Assert.NotNull(report.Repeatability);
    }

    [Fact]
    public void APilotRun_KeepsCompletenessFailures_SoAnUnfinishedRunIsNotMistakenForAResult()
    {
        var experts = Experts().Take(5).ToList();
        var partial = Grades(experts.Take(2)); // three of the five performances never graded

        var report = SpeakingGraderCalibrationMetrics.Compute(experts, partial, requireAudio: true, pilot: true);

        Assert.Equal("pilot", report.Verdict.Mode);
        Assert.False(report.Verdict.Passed);
        Assert.Contains(report.Verdict.Failures, f => f.Contains("only 2 of 5 marked performances have been graded"));
        Assert.DoesNotContain(report.Verdict.Failures, f => f.Contains("at least 30"));
    }

    [Fact]
    public void PerformancesNotYetGraded_AreSaid_NotSilentlyDropped()
    {
        var experts = Experts();
        var partial = Grades(experts.Take(30));

        var report = SpeakingGraderCalibrationMetrics.Compute(experts, partial, requireAudio: true);

        Assert.False(report.Verdict.Passed);
        Assert.Contains(report.Verdict.Failures, f => f.Contains("only 30 of 36 marked performances have been graded"));
    }

    [Fact]
    public void Intelligibility_IsReportedSeparately_ForAudioAndForTranscriptOnlyGrades()
    {
        var experts = Experts();
        var observations = Grades(experts.Take(18), source: "audio")
            .Concat(Grades(experts.Skip(18), source: "transcript_only", scores: (expert, _) =>
                expert.Scores.ToDictionary(kv => kv.Key, kv => kv.Key == "intelligibility" ? Math.Min(6, kv.Value + 2) : kv.Value)))
            .ToList();

        var report = SpeakingGraderCalibrationMetrics.Compute(experts, observations, requireAudio: true);

        Assert.Equal(0, report.IntelligibilityFromAudio!.Mae);
        Assert.True(report.IntelligibilityFromTranscript!.Mae > 1);
        Assert.Equal(36, report.IntelligibilityFromAudio.N);
    }

    [Fact]
    public void AnAudioRun_WithNoGradeJudgedFromAudio_DoesNotPass()
    {
        var experts = Experts();

        var report = SpeakingGraderCalibrationMetrics.Compute(experts, Grades(experts, source: "transcript_only"), requireAudio: true);

        Assert.Null(report.IntelligibilityFromAudio);
        Assert.Contains(report.Verdict.Failures, f => f.Contains("no grade was judged from audio"));
        // The same grades are acceptable as a transcript-only calibration.
        Assert.DoesNotContain(
            SpeakingGraderCalibrationMetrics.Compute(experts, Grades(experts, source: "transcript_only"), requireAudio: false).Verdict.Failures,
            f => f.Contains("judged from audio"));
    }

    // ── The score map ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void TheFittedMap_IsMonotone_RoundedToTen_AndAnchoredAtBothEnds()
    {
        var experts = Experts();

        var fitted = SpeakingGraderCalibrationMetrics.Compute(experts, Grades(experts), true).Mapping.Fitted;

        Assert.Equal(40, fitted.Length);
        Assert.Equal(0, fitted[0]);
        Assert.Equal(500, fitted[39]);
        Assert.All(fitted, value => Assert.Equal(0, value % 10));
        for (var raw = 1; raw < fitted.Length; raw++) Assert.True(fitted[raw] >= fitted[raw - 1], $"not monotone at {raw}");
        // On a monotone expert it reproduces the expert's own overall at every marked total.
        Assert.All(experts, e => Assert.Equal(e.OverallScaled, fitted[SpeakingGraderCalibrationMetrics.Raw(e.Scores)]));
    }

    [Fact]
    public void TheFit_PoolsAnExpertWhoScoresAHigherTotalLower_IntoOneLevel()
    {
        // Totals 10 and 20 marked 300 and 200: an expert is not monotone here, the map must be.
        var table = SpeakingGraderCalibrationMetrics.FitMapping([(10, 300), (20, 200)]);

        Assert.Equal(table[10], table[20]);
        Assert.Equal(250, table[10]);
        Assert.Equal(0, table[0]);
        Assert.Equal(500, table[39]);
        for (var raw = 1; raw < table.Length; raw++) Assert.True(table[raw] >= table[raw - 1]);
    }

    [Fact]
    public void TheFit_InterpolatesBetweenMarkedTotals_AndNeverLeavesTheRange()
    {
        var table = SpeakingGraderCalibrationMetrics.FitMapping([(20, 250), (30, 350)]);

        Assert.Equal(250, table[20]);
        Assert.Equal(350, table[30]);
        Assert.Equal(300, table[25]);
        Assert.All(table, v => Assert.InRange(v, 0, 500));
    }

    [Fact]
    public void TheEndToEndScore_ForEveryPerformance_UsesAMapFittedWithoutIt()
    {
        // The only performance at raw 15 is marked 100 (an outlier); the neighbours say about 190.
        // Honest leave-one-out cannot "know" the outlier, so its grade lands far from the expert's number.
        var experts = Experts().Where(e => SpeakingGraderCalibrationMetrics.Raw(e.Scores) != 14).ToList();
        experts.Add(new SpeakingCalibrationExpert("outlier", true, ScoresFor(14), 100));

        var report = SpeakingGraderCalibrationMetrics.Compute(experts, Grades(experts, repeats: 2), requireAudio: true);

        Assert.True(report.Mapping.LeaveOneOut.Mae > 0, "the outlier's own mark must not be learnt from itself");
        Assert.Equal(0, report.RawTotal.Mae); // the grader itself agreed with the expert's criteria
    }

    [Theory]
    [InlineData(8, 10, 0.8, 0.49, 0.943)]
    [InlineData(10, 10, 1.0, 0.722, 1.0)]
    [InlineData(0, 10, 0.0, 0.0, 0.278)]
    public void Rate_CarriesAnHonestWilsonInterval(int hits, int n, double share, double low, double high)
    {
        var rate = SpeakingGraderCalibrationMetrics.Rate(hits, n);

        Assert.Equal(share, rate.Share, precision: 3);
        Assert.Equal(low, rate.Low, precision: 2);
        Assert.Equal(high, rate.High, precision: 2);
    }

    [Fact]
    public void Rate_WithNothingToCount_IsTheWholeRange_NotAMadeUpShare()
    {
        var rate = SpeakingGraderCalibrationMetrics.Rate(0, 0);

        Assert.Equal(0, rate.N);
        Assert.Equal(0, rate.Low);
        Assert.Equal(1, rate.High);
    }

    [Fact]
    public void TheRawTotal_AddsTheNineCriteria_WithinTheirMaximums()
    {
        Assert.Equal(31, SpeakingGraderCalibrationMetrics.Raw(ScoresFor(31)));
        Assert.Equal(39, SpeakingGraderCalibrationMetrics.Raw(ScoresFor(39)));
        // An out-of-range score is clamped like the grader does, never trusted.
        Assert.Equal(39, SpeakingGraderCalibrationMetrics.Raw(Codes.ToDictionary(c => c, _ => 99)));
    }
}
