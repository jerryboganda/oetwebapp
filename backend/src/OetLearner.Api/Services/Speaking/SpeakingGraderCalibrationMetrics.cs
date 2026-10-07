namespace OetLearner.Api.Services.Speaking;

/// <summary>An expert's mark of one performance: the ground truth the AI grader is compared with.</summary>
public sealed record SpeakingCalibrationExpert(
    string SampleId, bool HasAudio, IReadOnlyDictionary<string, int> Scores, int OverallScaled);

/// <summary>One grade by the grader under test. A performance is graded several times to measure repeatability.</summary>
public sealed record SpeakingCalibrationObservation(
    string SampleId, int Repeat, IReadOnlyDictionary<string, int> Scores, string IntelligibilitySource);

/// <summary>A share with its 95 % Wilson interval (n is small; an honest interval matters more than the point).</summary>
public sealed record SpeakingCalibrationRate(int Hits, int N, double Share, double Low, double High);

public sealed record SpeakingCalibrationCriterionStats(
    string Code, int N, double Mae, double Bias, SpeakingCalibrationRate Exact, SpeakingCalibrationRate Adjacent);

public sealed record SpeakingCalibrationErrorStats(int N, double Mae, double Bias);

public sealed record SpeakingCalibrationScaledStats(
    int N, double Mae, double Bias, SpeakingCalibrationRate Within40);

public sealed record SpeakingCalibrationGradeStats(
    SpeakingCalibrationRate Exact, SpeakingCalibrationRate Adjacent, int[][] Confusion);

public sealed record SpeakingCalibrationPassStats(
    SpeakingCalibrationRate Agreement, SpeakingCalibrationRate FalsePass, SpeakingCalibrationRate FalseFail);

public sealed record SpeakingCalibrationRepeatability(
    SpeakingCalibrationRate CriterionRepeat,
    double ScaledMeanAbsDelta,
    SpeakingCalibrationRate ScaledWithin20,
    SpeakingCalibrationRate GradeStable,
    SpeakingCalibrationRate PassStable);

/// <param name="V0OnExpert">The current raw-to-reported map applied to the EXPERT's own criterion total, against the
/// expert's overall: how far the platform heuristic is from the expert even with a perfect grader.</param>
/// <param name="V0EndToEnd">What the platform shows today: the grader's scores through the current map, against the expert's overall.</param>
/// <param name="Fitted">The monotone map from raw total (0..39) to reported score fitted on the expert's own pairs: the
/// candidate to replace the heuristic. Rounded to 10, anchored at 0 and 39.</param>
/// <param name="LeaveOneOut">The grader's scores through a map fitted WITHOUT that performance: the honest end-to-end number.</param>
public sealed record SpeakingCalibrationMapping(
    SpeakingCalibrationScaledStats V0OnExpert,
    SpeakingCalibrationScaledStats V0EndToEnd,
    int[] Fitted,
    SpeakingCalibrationScaledStats LeaveOneOut);

public sealed record SpeakingCalibrationCoverageStats(
    int Labelled, IReadOnlyDictionary<string, int> PerGrade, int NearPassLine, double AudioShare,
    int BelowPassLine = 0, int AtOrAbovePassLine = 0);

public sealed record SpeakingCalibrationVerdict(
    bool Passed,
    IReadOnlyList<string> Failures,
    /// <summary><c>pilot</c> | <c>validation</c>. A pilot verdict can never pass: it is an informational
    /// comparison on a small real sample, not the approved validation.</summary>
    string Mode = "validation",
    /// <summary>Pilot mode only: where the grader stood against the bar the full validation will apply.</summary>
    IReadOnlyList<string>? Advisory = null);

/// <summary>One grade of one performance, set beside the expert's mark. <c>ScaledLeaveOneOut</c> is the grader's score through
/// a map fitted without this performance; <c>ScaledError</c> is that minus the expert's overall.</summary>
public sealed record SpeakingCalibrationGradeDetail(
    int Repeat,
    IReadOnlyDictionary<string, int> Scores,
    int Raw,
    int ScaledLeaveOneOut,
    string Grade,
    int ScaledError,
    string IntelligibilitySource);

/// <summary>The per-performance line of the comparison report: the expert's nine marks and overall, and every grade the
/// grader gave it. Ids only; no learner identity.</summary>
public sealed record SpeakingCalibrationPerformance(
    string SampleId,
    bool HasAudio,
    IReadOnlyDictionary<string, int> ExpertScores,
    int ExpertRaw,
    int ExpertOverall,
    string ExpertGrade,
    IReadOnlyList<SpeakingCalibrationGradeDetail> Grades);

public sealed record SpeakingCalibrationReport(
    int Performances,
    int Observations,
    int Repeats,
    SpeakingCalibrationCoverageStats Coverage,
    IReadOnlyList<SpeakingCalibrationCriterionStats> Criteria,
    SpeakingCalibrationCriterionStats? IntelligibilityFromAudio,
    SpeakingCalibrationCriterionStats? IntelligibilityFromTranscript,
    SpeakingCalibrationErrorStats RawTotal,
    SpeakingCalibrationMapping Mapping,
    SpeakingCalibrationGradeStats Grade,
    SpeakingCalibrationPassStats PassFail,
    SpeakingCalibrationRepeatability? Repeatability,
    SpeakingCalibrationVerdict Verdict,
    /// <summary>Every marked performance beside its grades (null on a report frozen before this field existed).</summary>
    IReadOnlyList<SpeakingCalibrationPerformance>? Detail = null,
    /// <summary>How many grades each exact grader version + model produced; a label is earned per exact version.</summary>
    IReadOnlyDictionary<string, int>? GraderVersions = null);

/// <summary>
/// How closely the AI grader agrees with an OET expert (owner spec 4 Oct 2026). Pure functions over the expert's marks and
/// the grader's grades: unit-tested without a database. The thresholds were APPROVED by the owner on 2026-10-05: keep them
/// strict, never relax one to make the grader pass. A report that meets all of them is what lets a grader version leave the
/// "provisional" label (and then only with the owner's agreement).
/// </summary>
public static class SpeakingGraderCalibrationMetrics
{
    /// <summary>The approved pass thresholds (owner, 2026-10-05). Changing one is an owner decision, not a code tidy-up;
    /// SpeakingGraderCalibrationMetricsTests pins every value.</summary>
    public static class Thresholds
    {
        public const double LinguisticMae = 0.75;
        public const double LinguisticBias = 0.5;
        public const double LinguisticAdjacent = 0.90;
        public const double ClinicalMae = 0.5;
        public const double ClinicalBias = 0.35;
        public const double ClinicalExact = 0.60;
        public const double AudioIntelligibilityMae = 0.75;
        public const double ScaledMae = 30;
        public const double ScaledBias = 15;
        public const double ScaledWithin40 = 0.80;
        public const double GradeExact = 0.70;
        public const double GradeAdjacent = 0.95;
        public const double PassAgreement = 0.85;
        public const double FalsePass = 0.10;
        public const double CriterionRepeat = 0.80;
        public const double ScaledWithin20 = 0.90;
        public const double PassStable = 0.95;
        public const int MinimumRepeats = 2;
    }

    private static readonly string[] GradeLetters = ["A", "B", "C+", "C", "D", "E"];
    private const int NearPassLow = 320;
    private const int NearPassHigh = 380;

    public static SpeakingCalibrationReport Compute(
        IReadOnlyList<SpeakingCalibrationExpert> experts,
        IReadOnlyList<SpeakingCalibrationObservation> observations,
        bool requireAudio,
        bool pilot = false)
    {
        var expertById = experts.ToDictionary(e => e.SampleId, StringComparer.Ordinal);
        var graded = observations.Where(o => expertById.ContainsKey(o.SampleId)).ToList();
        var gradedIds = graded.Select(o => o.SampleId).Distinct(StringComparer.Ordinal).ToList();
        var repeats = graded.Count == 0 ? 0 : graded.GroupBy(o => o.SampleId).Max(g => g.Count());

        var criteria = SpeakingGraderCalibrationService.Criteria
            .Select(c => CriterionStats(c.Code, graded, expertById))
            .ToList();
        var fromAudio = graded.Where(o => o.IntelligibilitySource == "audio").ToList();
        var fromTranscript = graded.Where(o => o.IntelligibilitySource != "audio").ToList();

        var rawErrors = graded.Select(o => (double)(Raw(o.Scores) - Raw(expertById[o.SampleId].Scores))).ToList();

        // The end-to-end number: every grade through a map fitted WITHOUT its own performance.
        var looTables = gradedIds.ToDictionary(
            id => id,
            id => FitMapping(experts.Where(e => e.SampleId != id).Select(e => (Raw(e.Scores), e.OverallScaled))),
            StringComparer.Ordinal);
        var loo = graded
            .Select(o => (Observation: o, Expert: expertById[o.SampleId], Scaled: looTables[o.SampleId][Raw(o.Scores)]))
            .ToList();
        var fitted = FitMapping(experts.Select(e => (Raw(e.Scores), e.OverallScaled)));

        var v0 = SpeakingCalibrationV0();
        var mapping = new SpeakingCalibrationMapping(
            ScaledStats(experts.Select(e => (V0Scaled(v0, Raw(e.Scores)), e.OverallScaled))),
            ScaledStats(graded.Select(o => (V0Scaled(v0, Raw(o.Scores)), expertById[o.SampleId].OverallScaled))),
            fitted,
            ScaledStats(loo.Select(x => (x.Scaled, x.Expert.OverallScaled))));

        var gradeHits = loo.Count(x => Ordinal(x.Scaled) == Ordinal(x.Expert.OverallScaled));
        var gradeNear = loo.Count(x => Math.Abs(Ordinal(x.Scaled) - Ordinal(x.Expert.OverallScaled)) <= 1);
        var confusion = Enumerable.Range(0, GradeLetters.Length).Select(_ => new int[GradeLetters.Length]).ToArray();
        foreach (var x in loo) confusion[Ordinal(x.Expert.OverallScaled)][Ordinal(x.Scaled)]++;

        var expertPass = loo.Where(x => OetScoring.IsSpeakingPass(x.Expert.OverallScaled)).ToList();
        var expertFail = loo.Where(x => !OetScoring.IsSpeakingPass(x.Expert.OverallScaled)).ToList();
        var passStats = new SpeakingCalibrationPassStats(
            Rate(loo.Count(x => OetScoring.IsSpeakingPass(x.Scaled) == OetScoring.IsSpeakingPass(x.Expert.OverallScaled)), loo.Count),
            Rate(expertFail.Count(x => OetScoring.IsSpeakingPass(x.Scaled)), expertFail.Count),
            Rate(expertPass.Count(x => !OetScoring.IsSpeakingPass(x.Scaled)), expertPass.Count));

        var report = new SpeakingCalibrationReport(
            experts.Count,
            graded.Count,
            repeats,
            Coverage(experts),
            criteria,
            fromAudio.Count == 0 ? null : CriterionStats("intelligibility", fromAudio, expertById),
            fromTranscript.Count == 0 ? null : CriterionStats("intelligibility", fromTranscript, expertById),
            new SpeakingCalibrationErrorStats(rawErrors.Count, R(Mean(rawErrors.Select(Math.Abs))), R(Mean(rawErrors))),
            mapping,
            new SpeakingCalibrationGradeStats(Rate(gradeHits, loo.Count), Rate(gradeNear, loo.Count), confusion),
            passStats,
            Repeatability(graded, looTables),
            new SpeakingCalibrationVerdict(false, []),
            experts.OrderBy(e => e.SampleId, StringComparer.Ordinal)
                .Select(e => new SpeakingCalibrationPerformance(
                    e.SampleId,
                    e.HasAudio,
                    e.Scores,
                    Raw(e.Scores),
                    e.OverallScaled,
                    GradeLetters[Ordinal(e.OverallScaled)],
                    loo.Where(x => x.Observation.SampleId == e.SampleId)
                        .OrderBy(x => x.Observation.Repeat)
                        .Select(x => new SpeakingCalibrationGradeDetail(
                            x.Observation.Repeat,
                            x.Observation.Scores,
                            Raw(x.Observation.Scores),
                            x.Scaled,
                            GradeLetters[Ordinal(x.Scaled)],
                            x.Scaled - e.OverallScaled,
                            x.Observation.IntelligibilitySource))
                        .ToList()))
                .ToList());

        return report with { Verdict = Evaluate(report, experts.Count, gradedIds.Count, requireAudio, pilot) };
    }

    // ── Coverage ─────────────────────────────────────────────────────────

    private static SpeakingCalibrationCoverageStats Coverage(IReadOnlyList<SpeakingCalibrationExpert> experts)
    {
        var perGrade = GradeLetters.ToDictionary(g => g, _ => 0);
        foreach (var e in experts) perGrade[GradeLetters[Ordinal(e.OverallScaled)]]++;
        return new SpeakingCalibrationCoverageStats(
            experts.Count,
            perGrade,
            experts.Count(e => e.OverallScaled is >= NearPassLow and <= NearPassHigh),
            experts.Count == 0 ? 0 : R(experts.Count(e => e.HasAudio) / (double)experts.Count),
            experts.Count(e => e.OverallScaled is >= NearPassLow and < 350),
            experts.Count(e => e.OverallScaled is >= 350 and <= NearPassHigh));
    }

    // ── Per-criterion agreement ──────────────────────────────────────────

    private static SpeakingCalibrationCriterionStats CriterionStats(
        string code,
        IReadOnlyList<SpeakingCalibrationObservation> observations,
        IReadOnlyDictionary<string, SpeakingCalibrationExpert> experts)
    {
        var errors = new List<int>();
        foreach (var o in observations)
        {
            if (o.Scores.TryGetValue(code, out var ai) && experts[o.SampleId].Scores.TryGetValue(code, out var expert))
            {
                errors.Add(ai - expert);
            }
        }

        return new SpeakingCalibrationCriterionStats(
            code,
            errors.Count,
            R(Mean(errors.Select(e => (double)Math.Abs(e)))),
            R(Mean(errors.Select(e => (double)e))),
            Rate(errors.Count(e => e == 0), errors.Count),
            Rate(errors.Count(e => Math.Abs(e) <= 1), errors.Count));
    }

    // ── Scores through a map ─────────────────────────────────────────────

    private static SpeakingCalibrationScaledStats ScaledStats(IEnumerable<(int Ai, int Expert)> pairs)
    {
        var errors = pairs.Select(p => (double)(p.Ai - p.Expert)).ToList();
        return new SpeakingCalibrationScaledStats(
            errors.Count,
            R(Mean(errors.Select(Math.Abs))),
            R(Mean(errors)),
            Rate(errors.Count(e => Math.Abs(e) <= 40), errors.Count));
    }

    private static int[] SpeakingCalibrationV0() => OetScoring.SpeakingRawToReported.ToArray();

    private static int V0Scaled(int[] table, int raw) => table[Math.Clamp(raw, 0, table.Length - 1)];

    /// <summary>
    /// A monotone raw-total to reported-score map fitted on the expert's own pairs: pool-adjacent-violators (isotonic)
    /// regression with fixed anchors 0 to 0 and 39 to 500, linear between observed totals, rounded to ten. The AI grader's
    /// own per-criterion bias is fixed in the prompt, not hidden in this map.
    /// </summary>
    internal static int[] FitMapping(IEnumerable<(int Raw, int Overall)> pairs)
    {
        const int maxRaw = 39;
        var points = pairs
            .Select(p => (X: Math.Clamp(p.Raw, 0, maxRaw), Y: (double)p.Overall))
            .Append((X: 0, Y: 0.0))
            .Append((X: maxRaw, Y: 500.0))
            .GroupBy(p => p.X)
            .OrderBy(g => g.Key)
            .Select(g => (X: g.Key, W: (double)g.Count(), Y: g.Average(p => p.Y)))
            .ToList();

        // Pool adjacent violators: merge neighbouring blocks until the block means never decrease.
        var blocks = new List<(double SumWy, double W, int From, int To)>();
        for (var i = 0; i < points.Count; i++)
        {
            blocks.Add((points[i].W * points[i].Y, points[i].W, i, i));
            while (blocks.Count > 1 && Mean(blocks[^2]) > Mean(blocks[^1]))
            {
                var last = blocks[^1];
                var previous = blocks[^2];
                blocks.RemoveRange(blocks.Count - 2, 2);
                blocks.Add((previous.SumWy + last.SumWy, previous.W + last.W, previous.From, last.To));
            }
        }

        var fittedAtObserved = new double[points.Count];
        foreach (var block in blocks)
        {
            for (var i = block.From; i <= block.To; i++) fittedAtObserved[i] = Mean(block);
        }

        var table = new int[maxRaw + 1];
        var segment = 0;
        for (var x = 0; x <= maxRaw; x++)
        {
            while (segment < points.Count - 2 && x > points[segment + 1].X) segment++;
            var (x0, x1) = (points[segment].X, points[segment + 1].X);
            var (y0, y1) = (fittedAtObserved[segment], fittedAtObserved[segment + 1]);
            var value = x1 == x0 ? y0 : y0 + ((y1 - y0) * (x - x0) / (x1 - x0));
            table[x] = Math.Clamp((int)(Math.Round(value / 10.0, MidpointRounding.AwayFromZero) * 10), 0, 500);
        }

        return table;
    }

    private static double Mean((double SumWy, double W, int From, int To) block) => block.SumWy / block.W;

    // ── Repeatability ────────────────────────────────────────────────────

    private static SpeakingCalibrationRepeatability? Repeatability(
        IReadOnlyList<SpeakingCalibrationObservation> observations,
        IReadOnlyDictionary<string, int[]> looTables)
    {
        var criterionHits = 0;
        var criterionTotal = 0;
        var deltas = new List<double>();
        var gradeStable = 0;
        var passStable = 0;
        var pairs = 0;

        foreach (var group in observations.GroupBy(o => o.SampleId))
        {
            var repeatsOfSample = group.OrderBy(o => o.Repeat).ToList();
            for (var a = 0; a < repeatsOfSample.Count; a++)
            {
                for (var b = a + 1; b < repeatsOfSample.Count; b++)
                {
                    pairs++;
                    foreach (var c in SpeakingGraderCalibrationService.Criteria)
                    {
                        if (!repeatsOfSample[a].Scores.TryGetValue(c.Code, out var one)
                            || !repeatsOfSample[b].Scores.TryGetValue(c.Code, out var two))
                        {
                            continue;
                        }

                        criterionTotal++;
                        if (one == two) criterionHits++;
                    }

                    var table = looTables[group.Key];
                    var scaledOne = table[Raw(repeatsOfSample[a].Scores)];
                    var scaledTwo = table[Raw(repeatsOfSample[b].Scores)];
                    deltas.Add(Math.Abs(scaledOne - scaledTwo));
                    if (Ordinal(scaledOne) == Ordinal(scaledTwo)) gradeStable++;
                    if (OetScoring.IsSpeakingPass(scaledOne) == OetScoring.IsSpeakingPass(scaledTwo)) passStable++;
                }
            }
        }

        return pairs == 0
            ? null
            : new SpeakingCalibrationRepeatability(
                Rate(criterionHits, criterionTotal),
                R(Mean(deltas)),
                Rate(deltas.Count(d => d <= 20), deltas.Count),
                Rate(gradeStable, pairs),
                Rate(passStable, pairs));
    }

    // ── The verdict ──────────────────────────────────────────────────────

    private static SpeakingCalibrationVerdict Evaluate(
        SpeakingCalibrationReport report, int experts, int gradedPerformances, bool requireAudio, bool pilot)
    {
        var failures = new List<string>();
        // In an owner pilot a threshold miss is not a failure — the verdict can never pass anyway; the point is
        // to show the owner where the grader stands against the bar the approved validation will apply. The
        // coverage gates (30 performances, grade distribution, pass-line straddle) are not even evaluated.
        var advisory = pilot ? new List<string>() : null;
        void Issue(string message)
        {
            if (advisory is null) failures.Add(message); else advisory.Add(message);
        }

        if (!pilot)
        {
            if (experts < SpeakingGraderCalibrationService.RequiredLabelled)
                failures.Add($"needs at least {SpeakingGraderCalibrationService.RequiredLabelled} expert-marked performances (has {experts})");
            foreach (var (grade, count) in report.Coverage.PerGrade)
            {
                if (count < SpeakingGraderCalibrationService.RequiredPerGrade)
                    failures.Add($"needs at least {SpeakingGraderCalibrationService.RequiredPerGrade} performances the expert marked grade {grade} (has {count})");
            }

            if (report.Coverage.NearPassLine < SpeakingGraderCalibrationService.RequiredNearPassLine)
                failures.Add($"needs at least {SpeakingGraderCalibrationService.RequiredNearPassLine} performances the expert marked 320-380 (has {report.Coverage.NearPassLine})");
            if (report.Coverage.BelowPassLine < SpeakingGraderCalibrationService.RequiredEachSideOfPassLine)
                failures.Add($"needs at least {SpeakingGraderCalibrationService.RequiredEachSideOfPassLine} performances the expert marked 320-340, just below the pass line (has {report.Coverage.BelowPassLine})");
            if (report.Coverage.AtOrAbovePassLine < SpeakingGraderCalibrationService.RequiredEachSideOfPassLine)
                failures.Add($"needs at least {SpeakingGraderCalibrationService.RequiredEachSideOfPassLine} performances the expert marked 350-380, at or just above the pass line (has {report.Coverage.AtOrAbovePassLine})");
            if (report.Coverage.AudioShare < SpeakingGraderCalibrationService.RequiredAudioShare)
                failures.Add($"needs at least {SpeakingGraderCalibrationService.RequiredAudioShare:P0} of the performances to have audio (has {report.Coverage.AudioShare:P0})");
        }

        // The harness's own completeness holds in both modes: a pilot must still have graded what it set out to grade.
        if (gradedPerformances < experts)
            failures.Add($"only {gradedPerformances} of {experts} marked performances have been graded");
        if (report.Repeats < Thresholds.MinimumRepeats)
            failures.Add($"needs every performance graded at least {Thresholds.MinimumRepeats} times (most is {report.Repeats})");

        foreach (var stats in report.Criteria)
        {
            var linguistic = SpeakingGraderCalibrationService.Criteria.First(c => c.Code == stats.Code).Family == "linguistic";
            var name = stats.Code;
            if (stats.N == 0) { Issue($"{name}: no grades"); continue; }
            if (linguistic)
            {
                if (stats.Mae > Thresholds.LinguisticMae) Issue($"{name}: mean error {stats.Mae} is above {Thresholds.LinguisticMae}");
                if (Math.Abs(stats.Bias) > Thresholds.LinguisticBias) Issue($"{name}: bias {stats.Bias} is outside ±{Thresholds.LinguisticBias}");
                if (stats.Adjacent.Share < Thresholds.LinguisticAdjacent) Issue($"{name}: within one band only {stats.Adjacent.Share:P0} (needs {Thresholds.LinguisticAdjacent:P0})");
            }
            else
            {
                if (stats.Mae > Thresholds.ClinicalMae) Issue($"{name}: mean error {stats.Mae} is above {Thresholds.ClinicalMae}");
                if (Math.Abs(stats.Bias) > Thresholds.ClinicalBias) Issue($"{name}: bias {stats.Bias} is outside ±{Thresholds.ClinicalBias}");
                if (stats.Exact.Share < Thresholds.ClinicalExact) Issue($"{name}: exact agreement only {stats.Exact.Share:P0} (needs {Thresholds.ClinicalExact:P0})");
            }
        }

        if (report.IntelligibilityFromAudio is { } audio)
        {
            if (audio.Mae > Thresholds.AudioIntelligibilityMae)
                Issue($"Intelligibility judged from audio: mean error {audio.Mae} is above {Thresholds.AudioIntelligibilityMae}");
        }
        else if (requireAudio)
        {
            Issue("the run asked for the audio judge but no grade was judged from audio");
        }

        var scaled = report.Mapping.LeaveOneOut;
        if (scaled.N > 0)
        {
            if (scaled.Mae > Thresholds.ScaledMae) Issue($"score: mean error {scaled.Mae} is above {Thresholds.ScaledMae} points");
            if (Math.Abs(scaled.Bias) > Thresholds.ScaledBias) Issue($"score: bias {scaled.Bias} is outside ±{Thresholds.ScaledBias} points");
            if (scaled.Within40.Share < Thresholds.ScaledWithin40) Issue($"score: within 40 points only {scaled.Within40.Share:P0} (needs {Thresholds.ScaledWithin40:P0})");
            if (report.Grade.Exact.Share < Thresholds.GradeExact) Issue($"grade: exact agreement only {report.Grade.Exact.Share:P0} (needs {Thresholds.GradeExact:P0})");
            if (report.Grade.Adjacent.Share < Thresholds.GradeAdjacent) Issue($"grade: within one grade only {report.Grade.Adjacent.Share:P0} (needs {Thresholds.GradeAdjacent:P0})");
            if (report.PassFail.Agreement.Share < Thresholds.PassAgreement) Issue($"pass/fail agreement only {report.PassFail.Agreement.Share:P0} (needs {Thresholds.PassAgreement:P0})");
            if (report.PassFail.FalsePass.Share > Thresholds.FalsePass) Issue($"false passes {report.PassFail.FalsePass.Share:P0} are above {Thresholds.FalsePass:P0}");
        }
        else
        {
            Issue("score: nothing to compare");
        }

        if (report.Repeatability is { } repeat)
        {
            if (repeat.CriterionRepeat.Share < Thresholds.CriterionRepeat) Issue($"repeatability: criterion scores repeat only {repeat.CriterionRepeat.Share:P0} (needs {Thresholds.CriterionRepeat:P0})");
            if (repeat.ScaledWithin20.Share < Thresholds.ScaledWithin20) Issue($"repeatability: score within 20 points only {repeat.ScaledWithin20.Share:P0} (needs {Thresholds.ScaledWithin20:P0})");
            if (repeat.PassStable.Share < Thresholds.PassStable) Issue($"repeatability: pass/fail flips {1 - repeat.PassStable.Share:P0} of the time (allowed {1 - Thresholds.PassStable:P0})");
        }
        else
        {
            Issue("repeatability: no performance has been graded twice yet");
        }

        advisory?.Add(
            "OWNER PILOT: an informational comparison on a small real sample — not statistical validation. The Speaking score stays 'Provisional'; the notes above are where the grader stood against the thresholds the approved full validation will apply (owner, 5 Oct 2026, unchanged).");

        return new SpeakingCalibrationVerdict(
            !pilot && failures.Count == 0,
            failures,
            pilot ? "pilot" : "validation",
            advisory);
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static OetScoring.SpeakingCriterionScores ToScores(IReadOnlyDictionary<string, int> s)
    {
        static int Get(IReadOnlyDictionary<string, int> map, string code) => map.TryGetValue(code, out var value) ? value : 0;
        return new OetScoring.SpeakingCriterionScores(
            Get(s, "intelligibility"), Get(s, "fluency"), Get(s, "appropriateness"), Get(s, "grammarExpression"),
            Get(s, "relationshipBuilding"), Get(s, "patientPerspective"), Get(s, "structure"),
            Get(s, "informationGathering"), Get(s, "informationGiving"));
    }

    /// <summary>The clamped raw rubric total 0..39 of a criterion-score map.</summary>
    internal static int Raw(IReadOnlyDictionary<string, int> scores) => OetScoring.SpeakingRawTotal(ToScores(scores));

    private static int Ordinal(int scaled) => Array.IndexOf(GradeLetters, OetScoring.OetGradeLetterFromScaled(scaled));

    private static double Mean(IEnumerable<double> values)
    {
        var list = values as IList<double> ?? values.ToList();
        return list.Count == 0 ? 0 : list.Sum() / list.Count;
    }

    private static double R(double value) => Math.Round(value, 3, MidpointRounding.AwayFromZero);

    /// <summary>A share with its 95 % Wilson score interval.</summary>
    internal static SpeakingCalibrationRate Rate(int hits, int n)
    {
        if (n <= 0) return new SpeakingCalibrationRate(0, 0, 0, 0, 1);
        const double z = 1.96;
        var p = hits / (double)n;
        var denominator = 1 + (z * z / n);
        var centre = (p + (z * z / (2 * n))) / denominator;
        var margin = z * Math.Sqrt((p * (1 - p) / n) + (z * z / (4.0 * n * n))) / denominator;
        return new SpeakingCalibrationRate(hits, n, R(p), R(Math.Max(0, centre - margin)), R(Math.Min(1, centre + margin)));
    }
}
