using System.Globalization;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Progress;

/// <summary>
/// Canonical implementation of the Progress v2 feature. Replaces the old
/// <c>LearnerService.GetProgressAsync</c> (which returned hardcoded
/// completion + submission-volume stubs and parsed score ranges naively).
///
/// <para>
/// Key design choices:
/// - One DB round-trip per learner. The service owns the whole aggregation.
/// - ISO-8601 week bucketing so "Week 3" always maps to a real calendar week.
/// - All scaled score values flow through <see cref="OetScoring"/> — no
///   regex parsing of <c>"330-360"</c>.
/// - Country-aware Writing threshold via <see cref="OetScoring.GetWritingPassThreshold"/>.
/// - Mock vs practice separation via <c>Attempt.Context</c>.
/// - ETag keyed on latest <c>Attempt.SubmittedAt/CompletedAt</c> + latest
///   <c>Evaluation.GeneratedAt</c> so the learner gets a cheap 304 when
///   nothing has changed since the last poll.
/// </para>
/// </summary>
public interface IProgressService
{
    Task<ProgressPayload> GetProgressAsync(string userId, string range, CancellationToken ct);

    Task<ComparativeBlock?> GetComparativeAsync(string userId, CancellationToken ct);

    Task<ProgressPolicy> GetPolicyAsync(string examFamilyCode, CancellationToken ct);

    Task<ProgressPolicy> UpdatePolicyAsync(string examFamilyCode, ProgressPolicyUpdate dto, string adminId, CancellationToken ct);
}

public sealed record ProgressPolicyUpdate(
    string? DefaultTimeRange,
    int? SmoothingWindow,
    int? MinCohortSize,
    bool? MockDistinctStyle,
    bool? ShowScoreGuaranteeStrip,
    bool? ShowCriterionConfidenceBand,
    int? MinEvaluationsForTrend,
    bool? ExportPdfEnabled);

public sealed class ProgressService(LearnerDbContext db) : IProgressService
{
    private static readonly string[] SubtestCodes = { "writing", "speaking", "reading", "listening" };
    private static readonly string[] AllowedRanges = { "14d", "30d", "90d", "all" };

    public async Task<ProgressPayload> GetProgressAsync(string userId, string range, CancellationToken ct)
    {
        range = AllowedRanges.Contains(range) ? range : "90d";

        var policy = await GetPolicyAsync("oet", ct);
        var now = DateTimeOffset.UtcNow;
        var fromDate = range switch
        {
            "14d" => now.AddDays(-14),
            "30d" => now.AddDays(-30),
            "90d" => now.AddDays(-90),
            _     => DateTimeOffset.MinValue,
        };

        var goal = await db.Goals.AsNoTracking().FirstOrDefaultAsync(g => g.UserId == userId, ct);
        var country = OetScoring.NormalizeWritingCountry(goal?.TargetCountry);
        var writingThreshold = OetScoring.GetWritingPassThreshold(country);

        // ── Pull attempts + evaluations in-range (single round trip per query) ──
        var attempts = await db.Attempts.AsNoTracking()
            .Where(a => a.UserId == userId
                     && a.State == AttemptState.Completed
                     && (a.SubmittedAt ?? a.CompletedAt ?? a.StartedAt) >= fromDate)
            .ToListAsync(ct);
        var attemptIds = attempts.Select(a => a.Id).ToList();
        var evaluations = (await db.Evaluations.AsNoTracking()
                .Where(e => e.State == AsyncState.Completed && attemptIds.Contains(e.AttemptId))
                .ToListAsync(ct))
            .OrderBy(e => e.GeneratedAt ?? DateTimeOffset.MinValue)
            .ToList();

        var attemptsById = attempts.ToDictionary(a => a.Id);

        // ── Subtest summaries ──
        var subtests = BuildSubtestSummaries(evaluations, attempts, goal, country, writingThreshold, now);

        // ── Weekly trend ──
        var trend = BuildWeeklyTrend(evaluations, attemptsById);

        // ── Criterion trend ──
        var criterionTrend = BuildCriterionTrend(evaluations);

        // ── Completion (real, not stubbed) ──
        var completion = BuildCompletion(attempts, now, days: 7);

        // ── Submission volume (real, not stubbed) ──
        var submissionVolume = BuildSubmissionVolume(attempts, weeks: 5);

        // ── Review usage ──
        var allAttemptIdsEver = await db.Attempts.AsNoTracking()
            .Where(a => a.UserId == userId)
            .Select(a => a.Id)
            .ToListAsync(ct);
        var reviews = await db.ReviewRequests.AsNoTracking()
            .Where(r => allAttemptIdsEver.Contains(r.AttemptId))
            .ToListAsync(ct);
        var completedReviews = reviews.Where(r => r.CompletedAt.HasValue).ToList();
        var reviewUsage = new ReviewUsage(
            TotalRequests: reviews.Count,
            CompletedRequests: completedReviews.Count,
            AverageTurnaroundHours: completedReviews.Count == 0
                ? null
                : Math.Round(completedReviews.Average(r => (r.CompletedAt!.Value - r.CreatedAt).TotalHours), 1),
            CreditsConsumed: reviews.Count(r => string.Equals(r.PaymentSource, "credits", StringComparison.OrdinalIgnoreCase)));

        // ── Goals block ──
        var goals = new Goals(
            TargetWritingScore: goal?.TargetWritingScore,
            TargetSpeakingScore: goal?.TargetSpeakingScore,
            TargetReadingScore: goal?.TargetReadingScore,
            TargetListeningScore: goal?.TargetListeningScore,
            TargetExamDate: goal?.TargetExamDate,
            DaysToExam: goal?.TargetExamDate is { } ted
                ? Math.Max(0, (int)Math.Ceiling((ted.ToDateTime(TimeOnly.MinValue) - now.UtcDateTime).TotalDays))
                : null,
            TargetCountry: country);

        // ── Comparative block (only in full payload if cohort sufficient) ──
        var comparative = await GetComparativeAsync(userId, ct);

        // ── Totals ──
        var totals = new Totals(
            CompletedAttempts: attempts.Count,
            CompletedEvaluations: evaluations.Count,
            MockAttempts: attempts.Count(a => IsMockContext(a.Context)),
            WritingSubmissions: attempts.Count(a => a.SubtestCode == "writing"),
            SpeakingSubmissions: attempts.Count(a => a.SubtestCode == "speaking"));

        var freshness = new ProgressFreshness(
            GeneratedAt: now,
            UsesFallbackSeries: evaluations.Count == 0,
            ETag: BuildETag(userId, attempts, evaluations, range));

        var meta = new ProgressMeta(
            Range: range,
            ExamFamilyCode: "oet",
            TargetCountry: country,
            ScoreAxisMin: OetScoring.ScaledMin,
            ScoreAxisMax: OetScoring.ScaledMax,
            GradeBThreshold: OetScoring.ScaledPassGradeB,
            WritingThreshold: writingThreshold?.Threshold,
            WritingThresholdGrade: writingThreshold?.Grade,
            WritingThresholdReason: writingThreshold is null ? (string.IsNullOrEmpty(country) ? "country_required" : "country_unsupported") : null,
            ShowScoreGuaranteeStrip: policy.ShowScoreGuaranteeStrip,
            ShowCriterionConfidenceBand: policy.ShowCriterionConfidenceBand,
            MinEvaluationsForTrend: policy.MinEvaluationsForTrend);

        return new ProgressPayload(meta, subtests, trend, criterionTrend, completion, submissionVolume, reviewUsage, goals, comparative, totals, freshness);
    }

    // ── Comparative (profession + exam family cohort, excluding self) ──────

    public async Task<ComparativeBlock?> GetComparativeAsync(string userId, CancellationToken ct)
    {
        var policy = await GetPolicyAsync("oet", ct);
        var ninety = DateTimeOffset.UtcNow.AddDays(-90);
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
        var userGoal = await db.Goals.AsNoTracking().FirstOrDefaultAsync(g => g.UserId == userId, ct);
        var professionId = userGoal?.ProfessionId;
        var examFamily = userGoal?.ExamFamilyCode ?? "oet";

        // Cohort peers: same profession + exam family, excluding the current learner.
        // Empty profession means fall back to platform-wide cohort (still excludes self).
        var peerQuery = db.Goals.AsNoTracking()
            .Where(g => g.ExamFamilyCode == examFamily && g.UserId != userId);
        if (!string.IsNullOrEmpty(professionId))
            peerQuery = peerQuery.Where(g => g.ProfessionId == professionId);
        var peerUserIds = await peerQuery.Select(g => g.UserId).ToListAsync(ct);
        var cohortSize = peerUserIds.Count;

        var description = string.IsNullOrEmpty(professionId)
            ? $"All {examFamily.ToUpperInvariant()} learners (last 90 days)"
            : $"{examFamily.ToUpperInvariant()} · {professionId} learners (last 90 days)";

        // If cohort is too small we still want to return a block (learner deserves a
        // friendly explanation) but with HasSufficientCohort=false and empty subtests.
        if (cohortSize < policy.MinCohortSize)
        {
            return new ComparativeBlock(
                Subtests: Array.Empty<SubtestComparative>(),
                CohortSize: cohortSize,
                MinCohortSize: policy.MinCohortSize,
                HasSufficientCohort: false,
                CohortScopeDescription: description);
        }

        var peerSet = peerUserIds.ToHashSet();
        var rows = new List<SubtestComparative>();
        foreach (var subtest in SubtestCodes)
        {
            // Learner's latest 5 scores for this subtest
            var userAttemptIds = await db.Attempts.AsNoTracking()
                .Where(a => a.UserId == userId && a.SubtestCode == subtest && a.State == AttemptState.Completed)
                .Select(a => a.Id).ToListAsync(ct);
            if (userAttemptIds.Count == 0) continue;
            var userEvals = await db.Evaluations.AsNoTracking()
                .Where(e => userAttemptIds.Contains(e.AttemptId) && e.State == AsyncState.Completed && e.SubtestCode == subtest)
                .OrderByDescending(e => e.GeneratedAt).Take(5)
                .ToListAsync(ct);
            var userScores = userEvals.Select(e => ParseScaledScore(e.ScoreRange)).Where(v => v.HasValue).Select(v => (double)v!.Value).ToList();
            if (userScores.Count == 0) continue;
            var userAvg = (int)Math.Round(userScores.Average());

            // Peer cohort scores (90 days)
            var peerAttemptIds = await db.Attempts.AsNoTracking()
                .Where(a => peerSet.Contains(a.UserId) && a.SubtestCode == subtest && a.State == AttemptState.Completed)
                .Select(a => a.Id).ToListAsync(ct);
            if (peerAttemptIds.Count == 0) continue;
            var peerEvals = await db.Evaluations.AsNoTracking()
                .Where(e => peerAttemptIds.Contains(e.AttemptId) && e.State == AsyncState.Completed && e.SubtestCode == subtest && e.GeneratedAt >= ninety)
                .ToListAsync(ct);
            var peerScores = peerEvals.Select(e => ParseScaledScore(e.ScoreRange)).Where(v => v.HasValue).Select(v => v!.Value).OrderBy(v => v).ToList();
            if (peerScores.Count == 0) continue;

            var cohortAverage = (int)Math.Round(peerScores.Average());
            var cohortMedian = peerScores[peerScores.Count / 2];
            // Percentile: share of peers scoring strictly below the learner's average.
            var below = peerScores.Count(s => s < userAvg);
            var percentile = Math.Round(100.0 * below / peerScores.Count, 1);
            rows.Add(new SubtestComparative(
                SubtestCode: subtest,
                YourScaled: userAvg,
                CohortAverage: cohortAverage,
                CohortMedian: cohortMedian,
                Percentile: percentile,
                Tier: ClassifyTier(percentile)));
        }

        return new ComparativeBlock(
            Subtests: rows,
            CohortSize: cohortSize,
            MinCohortSize: policy.MinCohortSize,
            HasSufficientCohort: true,
            CohortScopeDescription: description);
    }

    // ── Policy CRUD ────────────────────────────────────────────────────────

    public async Task<ProgressPolicy> GetPolicyAsync(string examFamilyCode, CancellationToken ct)
    {
        var policy = await db.ProgressPolicies.FirstOrDefaultAsync(p => p.ExamFamilyCode == examFamilyCode, ct);
        if (policy is not null) return policy;
        policy = new ProgressPolicy
        {
            Id = $"pp-{examFamilyCode}",
            ExamFamilyCode = examFamilyCode,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.ProgressPolicies.Add(policy);
        await db.SaveChangesAsync(ct);
        return policy;
    }

    public async Task<ProgressPolicy> UpdatePolicyAsync(string examFamilyCode, ProgressPolicyUpdate dto, string adminId, CancellationToken ct)
    {
        var p = await GetPolicyAsync(examFamilyCode, ct);
        if (dto.DefaultTimeRange is not null)
        {
            if (!AllowedRanges.Contains(dto.DefaultTimeRange))
                throw new InvalidOperationException($"invalid DefaultTimeRange '{dto.DefaultTimeRange}'");
            p.DefaultTimeRange = dto.DefaultTimeRange;
        }
        if (dto.SmoothingWindow is int sw) p.SmoothingWindow = Math.Clamp(sw, 0, 10);
        if (dto.MinCohortSize is int mcs) p.MinCohortSize = Math.Clamp(mcs, 1, 1000);
        if (dto.MockDistinctStyle is bool mds) p.MockDistinctStyle = mds;
        if (dto.ShowScoreGuaranteeStrip is bool ss) p.ShowScoreGuaranteeStrip = ss;
        if (dto.ShowCriterionConfidenceBand is bool ci) p.ShowCriterionConfidenceBand = ci;
        if (dto.MinEvaluationsForTrend is int met) p.MinEvaluationsForTrend = Math.Clamp(met, 1, 50);
        if (dto.ExportPdfEnabled is bool ex) p.ExportPdfEnabled = ex;
        p.UpdatedByAdminId = adminId;
        p.UpdatedAt = DateTimeOffset.UtcNow;

        db.AuditEvents.Add(new AuditEvent
        {
            Id = $"AUD-{Guid.NewGuid():N}",
            OccurredAt = DateTimeOffset.UtcNow,
            ActorId = adminId,
            ActorName = "Admin",
            Action = "Updated",
            ResourceType = "ProgressPolicy",
            ResourceId = p.Id,
            Details = $"Updated Progress policy for '{examFamilyCode}'",
        });
        await db.SaveChangesAsync(ct);
        return p;
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static IReadOnlyList<SubtestSummary> BuildSubtestSummaries(
        IReadOnlyList<Evaluation> evaluations,
        IReadOnlyList<Attempt> attempts,
        LearnerGoal? goal,
        string? country,
        WritingThreshold? writingThreshold,
        DateTimeOffset now)
    {
        var result = new List<SubtestSummary>();
        foreach (var subtest in SubtestCodes)
        {
            var evalsForSubtest = evaluations.Where(e => string.Equals(e.SubtestCode, subtest, StringComparison.OrdinalIgnoreCase)).ToList();
            var attemptsForSubtest = attempts.Where(a => string.Equals(a.SubtestCode, subtest, StringComparison.OrdinalIgnoreCase)).ToList();
            var latest = evalsForSubtest.OrderByDescending(e => e.GeneratedAt ?? DateTimeOffset.MinValue).FirstOrDefault();
            var latestScaled = latest is null ? (int?)null : ParseScaledScore(latest.ScoreRange);

            var thirtyDaysAgoEvals = evaluations
                .Where(e => string.Equals(e.SubtestCode, subtest, StringComparison.OrdinalIgnoreCase)
                         && e.GeneratedAt is not null && e.GeneratedAt <= now.AddDays(-30))
                .OrderByDescending(e => e.GeneratedAt).FirstOrDefault();
            int? deltaLast30 = null;
            if (latestScaled.HasValue && thirtyDaysAgoEvals is not null)
            {
                var old = ParseScaledScore(thirtyDaysAgoEvals.ScoreRange);
                if (old.HasValue) deltaLast30 = latestScaled.Value - old.Value;
            }

            var target = subtest switch
            {
                "writing" => goal?.TargetWritingScore,
                "speaking" => goal?.TargetSpeakingScore,
                "reading" => goal?.TargetReadingScore,
                "listening" => goal?.TargetListeningScore,
                _ => null,
            };

            (int? threshold, string? reason) = subtest switch
            {
                "writing" => (writingThreshold?.Threshold, writingThreshold is null
                    ? (string.IsNullOrEmpty(country) ? "country_required" : "country_unsupported")
                    : null),
                // listening/reading/speaking always 350/500 grade B
                _ => (OetScoring.ScaledPassGradeB, (string?)null),
            };

            result.Add(new SubtestSummary(
                SubtestCode: subtest,
                LatestScaled: latestScaled,
                LatestGrade: latestScaled is null ? null : OetScoring.OetGradeLetterFromScaled(latestScaled.Value),
                TargetScaled: target,
                GapToTarget: target.HasValue && latestScaled.HasValue ? target.Value - latestScaled.Value : null,
                DeltaLast30Days: deltaLast30,
                AttemptCount: attemptsForSubtest.Count,
                EvaluationCount: evalsForSubtest.Count,
                ThresholdScaled: threshold,
                ThresholdReason: reason));
        }
        return result;
    }

    private static IReadOnlyList<WeeklyTrendPoint> BuildWeeklyTrend(IReadOnlyList<Evaluation> evaluations, IReadOnlyDictionary<string, Attempt> attemptsById)
    {
        var buckets = new SortedDictionary<string, WeeklyTrendBuilder>(StringComparer.Ordinal);
        foreach (var e in evaluations)
        {
            if (e.GeneratedAt is null) continue;
            var weekStart = IsoWeekStart(e.GeneratedAt.Value);
            var key = IsoWeekKey(weekStart);
            if (!buckets.TryGetValue(key, out var bucket))
            {
                bucket = new WeeklyTrendBuilder { WeekKey = key, WeekStart = weekStart };
                buckets[key] = bucket;
            }
            var scaled = ParseScaledScore(e.ScoreRange);
            if (!scaled.HasValue) continue;
            var isMock = attemptsById.TryGetValue(e.AttemptId, out var attempt) && IsMockContext(attempt.Context);
            var dict = isMock ? bucket.MockSums : bucket.PracticeSums;
            var countDict = isMock ? bucket.MockCounts : bucket.PracticeCounts;
            dict[e.SubtestCode] = dict.GetValueOrDefault(e.SubtestCode, 0) + scaled.Value;
            countDict[e.SubtestCode] = countDict.GetValueOrDefault(e.SubtestCode, 0) + 1;
        }

        return buckets.Values.Select(b =>
        {
            var subtestScaled = new Dictionary<string, int?>();
            var subtestCount = new Dictionary<string, int>();
            var mockScaled = new Dictionary<string, int?>();
            var mockCount = new Dictionary<string, int>();
            foreach (var subtest in SubtestCodes)
            {
                subtestScaled[subtest] = b.PracticeCounts.GetValueOrDefault(subtest, 0) == 0
                    ? (int?)null
                    : (int)Math.Round(b.PracticeSums[subtest] / (double)b.PracticeCounts[subtest]);
                subtestCount[subtest] = b.PracticeCounts.GetValueOrDefault(subtest, 0);
                mockScaled[subtest] = b.MockCounts.GetValueOrDefault(subtest, 0) == 0
                    ? (int?)null
                    : (int)Math.Round(b.MockSums[subtest] / (double)b.MockCounts[subtest]);
                mockCount[subtest] = b.MockCounts.GetValueOrDefault(subtest, 0);
            }
            return new WeeklyTrendPoint(b.WeekKey, b.WeekStart, subtestScaled, subtestCount, mockScaled, mockCount);
        }).ToList();
    }

    private static IReadOnlyList<CriterionTrendPoint> BuildCriterionTrend(IReadOnlyList<Evaluation> evaluations)
    {
        // criterion score JSON payload entries are `{ criterionCode, scoreRange: "4/6" }`.
        // We convert each criterion scoreRange to a scaled-equivalent integer (out of 500)
        // so the frontend can plot criterion trend lines on the same canonical axis.
        var points = new List<CriterionTrendPoint>();
        var grouped = evaluations
            .Where(e => e.GeneratedAt is not null)
            .GroupBy(e => (SubtestCode: e.SubtestCode, Week: IsoWeekKey(IsoWeekStart(e.GeneratedAt!.Value))));
        foreach (var group in grouped)
        {
            var weekStart = IsoWeekStart(group.First().GeneratedAt!.Value);
            var perCriterion = new Dictionary<string, (int Sum, int Count)>();
            foreach (var e in group)
            {
                var list = JsonSupport.Deserialize<List<Dictionary<string, object?>>>(e.CriterionScoresJson, []);
                foreach (var c in list)
                {
                    var code = c.GetValueOrDefault("criterionCode")?.ToString();
                    if (string.IsNullOrWhiteSpace(code)) continue;
                    var scaled = ScaleCriterionScore(c.GetValueOrDefault("scoreRange")?.ToString());
                    if (!scaled.HasValue) continue;
                    var prev = perCriterion.TryGetValue(code, out var existing) ? existing : (Sum: 0, Count: 0);
                    perCriterion[code] = (prev.Sum + scaled.Value, prev.Count + 1);
                }
            }
            foreach (var kvp in perCriterion)
            {
                if (kvp.Value.Count == 0) continue;
                points.Add(new CriterionTrendPoint(
                    WeekKey: group.Key.Week,
                    WeekStart: weekStart,
                    SubtestCode: group.Key.SubtestCode,
                    CriterionCode: kvp.Key,
                    CriterionLabel: CriterionLabel(kvp.Key),
                    AverageScaled: (int)Math.Round(kvp.Value.Sum / (double)kvp.Value.Count),
                    SampleCount: kvp.Value.Count));
            }
        }
        return points.OrderBy(p => p.WeekStart).ThenBy(p => p.SubtestCode).ThenBy(p => p.CriterionCode).ToList();
    }

    private static IReadOnlyList<CompletionPoint> BuildCompletion(IReadOnlyList<Attempt> attempts, DateTimeOffset now, int days)
    {
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var result = new List<CompletionPoint>();
        for (var offset = days - 1; offset >= 0; offset--)
        {
            var date = today.AddDays(-offset);
            var count = attempts.Count(a =>
            {
                var when = a.SubmittedAt ?? a.CompletedAt ?? a.StartedAt;
                return DateOnly.FromDateTime(when.UtcDateTime) == date;
            });
            result.Add(new CompletionPoint(date, count));
        }
        return result;
    }

    private static IReadOnlyList<SubmissionVolumePoint> BuildSubmissionVolume(IReadOnlyList<Attempt> attempts, int weeks)
    {
        var now = DateTimeOffset.UtcNow;
        var points = new List<SubmissionVolumePoint>();
        for (var offset = weeks - 1; offset >= 0; offset--)
        {
            var weekStart = IsoWeekStart(now.AddDays(-7 * offset));
            var weekEnd = weekStart.AddDays(7);
            var inWindow = attempts.Where(a =>
            {
                var when = a.SubmittedAt ?? a.CompletedAt ?? a.StartedAt;
                return when >= weekStart && when < weekEnd;
            }).ToList();
            points.Add(new SubmissionVolumePoint(
                WeekKey: IsoWeekKey(weekStart),
                WeekStart: weekStart,
                Writing: inWindow.Count(a => a.SubtestCode == "writing"),
                Speaking: inWindow.Count(a => a.SubtestCode == "speaking")));
        }
        return points;
    }

    internal static bool IsMockContext(string? context) =>
        string.Equals(context, "mock", StringComparison.OrdinalIgnoreCase);

    // ── Score parsing utilities (canonical) ───────────────────────────────

    /// <summary>
    /// Extract a scaled (0–500) score from an <c>Evaluation.ScoreRange</c>
    /// string like <c>"330-360"</c>. Returns the midpoint. Never use regex
    /// on this field; this is the one authorised parser and it clamps
    /// to [0, 500] per OetScoring invariants.
    /// </summary>
    public static int? ParseScaledScore(string? range)
    {
        if (string.IsNullOrWhiteSpace(range)) return null;
        var trimmed = range.Trim();
        var slashIdx = trimmed.IndexOf('/');
        if (slashIdx >= 0) trimmed = trimmed[..slashIdx];
        var dashIdx = trimmed.IndexOf('-');
        if (dashIdx >= 0)
        {
            var left = trimmed[..dashIdx];
            var right = trimmed[(dashIdx + 1)..];
            if (int.TryParse(left, NumberStyles.Integer, CultureInfo.InvariantCulture, out var a)
                && int.TryParse(right, NumberStyles.Integer, CultureInfo.InvariantCulture, out var b))
            {
                return Math.Clamp((a + b) / 2, OetScoring.ScaledMin, OetScoring.ScaledMax);
            }
            return null;
        }
        if (int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var single))
        {
            return Math.Clamp(single, OetScoring.ScaledMin, OetScoring.ScaledMax);
        }
        return null;
    }

    /// <summary>
    /// Criterion scoreRange strings are shaped like <c>"4-5/6"</c>. We take
    /// the midpoint and scale to 0–500 so the UI can plot criterion lines
    /// on the same axis as the subtest trend.
    /// </summary>
    public static int? ScaleCriterionScore(string? range)
    {
        if (string.IsNullOrWhiteSpace(range)) return null;
        var trimmed = range.Trim();
        var slashIdx = trimmed.IndexOf('/');
        var numeric = slashIdx >= 0 ? trimmed[..slashIdx] : trimmed;
        double midpoint;
        var dashIdx = numeric.IndexOf('-');
        if (dashIdx >= 0)
        {
            var left = numeric[..dashIdx];
            var right = numeric[(dashIdx + 1)..];
            if (!double.TryParse(left, NumberStyles.Float, CultureInfo.InvariantCulture, out var a)
                || !double.TryParse(right, NumberStyles.Float, CultureInfo.InvariantCulture, out var b))
                return null;
            midpoint = (a + b) / 2.0;
        }
        else
        {
            if (!double.TryParse(numeric, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) return null;
            midpoint = v;
        }
        // Denominator (6 by default for writing/speaking criterion rubrics)
        double denom = 6;
        if (slashIdx >= 0 && double.TryParse(trimmed[(slashIdx + 1)..], NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && d > 0)
            denom = d;
        var ratio = Math.Clamp(midpoint / denom, 0, 1);
        return (int)Math.Round(ratio * OetScoring.ScaledMax);
    }

    private static string CriterionLabel(string code) => code switch
    {
        "purpose" => "Purpose",
        "content" => "Content",
        "conciseness" => "Conciseness & Clarity",
        "genre" => "Genre & Style",
        "organization" => "Organisation & Layout",
        "language" => "Language",
        "intelligibility" => "Intelligibility",
        "fluency" => "Fluency",
        "appropriateness" => "Appropriateness of Language",
        "grammar_expression" => "Resources of Grammar & Expression",
        _ => char.ToUpperInvariant(code[0]) + code[1..],
    };

    private static string ClassifyTier(double percentile) => percentile switch
    {
        >= 90 => "top10",
        >= 75 => "top25",
        >= 50 => "aboveMedian",
        _ => "belowMedian",
    };

    /// <summary>
    /// Returns the UTC Monday that opens the ISO-8601 week containing <paramref name="moment"/>.
    /// Lightweight and well-defined (see RFC 3339 + ISO 8601 §3.2.2).
    /// </summary>
    public static DateTimeOffset IsoWeekStart(DateTimeOffset moment)
    {
        var local = moment.UtcDateTime.Date;
        var dayOfWeek = (int)local.DayOfWeek;
        // ISO weeks start on Monday (DayOfWeek.Monday = 1, Sunday = 0).
        var diff = dayOfWeek == 0 ? 6 : dayOfWeek - 1;
        return new DateTimeOffset(local.AddDays(-diff), TimeSpan.Zero);
    }

    public static string IsoWeekKey(DateTimeOffset weekStart)
    {
        var iso = ISOWeek.GetWeekOfYear(weekStart.UtcDateTime);
        var year = ISOWeek.GetYear(weekStart.UtcDateTime);
        return $"{year:D4}-W{iso:D2}";
    }

    private static string BuildETag(string userId, IReadOnlyList<Attempt> attempts, IReadOnlyList<Evaluation> evaluations, string range)
    {
        var latestAttempt = attempts.Count == 0 ? DateTimeOffset.MinValue : attempts.Max(a => a.SubmittedAt ?? a.CompletedAt ?? a.StartedAt);
        var latestEval = evaluations.Count == 0 ? DateTimeOffset.MinValue : evaluations.Max(e => e.GeneratedAt ?? DateTimeOffset.MinValue);
        var raw = $"{userId}:{range}:{latestAttempt.ToUnixTimeSeconds()}:{latestEval.ToUnixTimeSeconds()}:{attempts.Count}:{evaluations.Count}";
        return $"W/\"{Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(raw)))[..16]}\"";
    }

    private sealed class WeeklyTrendBuilder
    {
        public string WeekKey { get; set; } = "";
        public DateTimeOffset WeekStart { get; set; }
        public Dictionary<string, int> PracticeSums { get; } = new();
        public Dictionary<string, int> PracticeCounts { get; } = new();
        public Dictionary<string, int> MockSums { get; } = new();
        public Dictionary<string, int> MockCounts { get; } = new();
    }
}
