using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Mocks;

namespace OetLearner.Api.Services.Readiness;

/// <summary>
/// Aggregates every learner-activity signal into a single structured
/// <see cref="ReadinessSnapshot"/>: per-sub-test readiness, vocab readiness,
/// overall risk, weakest sub-test, recommended study hours, forecast
/// probability, and ranked actionable blockers.
///
/// Replaces the previous stub that created an empty payload with
/// <c>overallRisk = "unknown"</c> and zeroed evidence.
/// </summary>
public sealed class ReadinessComputationService(
    LearnerDbContext db,
    ReadinessForecastCalculator forecastCalculator,
    ReadinessBlockerRules blockerRules)
{
    public const int GradeBScaledThreshold = 350;
    public const int VocabularyMasteryTarget = 600;
    public const decimal ImprovementPointsPerStudyHour = 0.4m;
    public const int MinDataPointsForConfidence = 5;
    public const int MaxRecommendedHoursPerWeek = 25;
    public static readonly TimeSpan SnapshotCacheTtl = TimeSpan.FromHours(24);
    public static readonly TimeSpan DebounceWindow = TimeSpan.FromSeconds(60);

    private static readonly string[] SubtestCodes = ["writing", "speaking", "reading", "listening"];

    public async Task<ReadinessSnapshot> GetOrComputeAsync(string userId, CancellationToken ct)
    {
        var latest = await GetLatestSnapshotAsync(userId, ct);
        if (latest is not null && latest.ExpiresAt > DateTimeOffset.UtcNow)
        {
            return latest;
        }

        return await ComputeAsync(userId, ct);
    }

    public async Task<ReadinessSnapshot> ForceRefreshAsync(string userId, CancellationToken ct)
        => await ComputeAsync(userId, ct);

    public async Task<ReadinessSnapshot> ComputeAsync(string userId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("User id is required.", nameof(userId));
        }

        var now = DateTimeOffset.UtcNow;
        var cutoff = now.AddDays(-90);
        var cutoff14 = now.AddDays(-14);
        var cutoff30 = now.AddDays(-30);

        var goal = await db.Goals.AsNoTracking().FirstOrDefaultAsync(g => g.UserId == userId, ct);

        var userMockAttemptIds = await db.MockAttempts.AsNoTracking()
            .Where(a => a.UserId == userId)
            .Select(a => a.Id)
            .ToListAsync(ct);
        List<MockReport> mockReports;
        if (userMockAttemptIds.Count == 0)
        {
            mockReports = new List<MockReport>();
        }
        else if (db.Database.IsSqlite())
        {
            // SQLite cannot translate the combined Join + DateTimeOffset
            // comparison reliably; load filtered set and finish in memory.
            var rows = await db.MockReports.AsNoTracking()
                .Where(r => userMockAttemptIds.Contains(r.MockAttemptId)
                            && r.State == AsyncState.Completed
                            && r.GeneratedAt != null)
                .ToListAsync(ct);
            mockReports = rows
                .Where(r => r.GeneratedAt >= cutoff)
                .OrderByDescending(r => r.GeneratedAt)
                .Take(8)
                .ToList();
        }
        else
        {
            mockReports = await db.MockReports.AsNoTracking()
                .Where(r => userMockAttemptIds.Contains(r.MockAttemptId)
                            && r.State == AsyncState.Completed
                            && r.GeneratedAt != null
                            && r.GeneratedAt >= cutoff)
                .OrderByDescending(r => r.GeneratedAt)
                .Take(8)
                .ToListAsync(ct);
        }

        var isSqlite = db.Database.IsSqlite();

        var allAttempts = await db.Attempts.AsNoTracking()
            .Where(a => a.UserId == userId && a.State == AttemptState.Completed)
            .Select(a => new { a.Id, a.SubtestCode, a.CompletedAt, a.AnalysisJson })
            .ToListAsync(ct);
        var attemptIds = allAttempts.Where(a => a.CompletedAt.HasValue && a.CompletedAt.Value >= cutoff).ToList();

        var attemptIdList = attemptIds.Select(a => a.Id).ToList();
        var evaluations = attemptIdList.Count == 0
            ? new List<Evaluation>()
            : (isSqlite
                ? (await db.Evaluations.AsNoTracking()
                    .Where(e => e.State == AsyncState.Completed && attemptIdList.Contains(e.AttemptId))
                    .ToListAsync(ct))
                  .Where(e => e.GeneratedAt.HasValue && e.GeneratedAt.Value >= cutoff)
                  .ToList()
                : await db.Evaluations.AsNoTracking()
                    .Where(e => e.State == AsyncState.Completed && attemptIdList.Contains(e.AttemptId) && e.GeneratedAt >= cutoff)
                    .ToListAsync(ct));

        var reviews = attemptIdList.Count == 0
            ? new List<ReviewRequest>()
            : (isSqlite
                ? (await db.ReviewRequests.AsNoTracking()
                    .Where(r => r.State == ReviewRequestState.Completed && attemptIdList.Contains(r.AttemptId))
                    .ToListAsync(ct))
                  .Where(r => r.CompletedAt.HasValue && r.CompletedAt.Value >= cutoff)
                  .ToList()
                : await db.ReviewRequests.AsNoTracking()
                    .Where(r => r.State == ReviewRequestState.Completed && attemptIdList.Contains(r.AttemptId) && r.CompletedAt >= cutoff)
                    .ToListAsync(ct));

        var vocabStats = await db.LearnerVocabularies.AsNoTracking()
            .Where(v => v.UserId == userId)
            .Select(v => new VocabularyStatRow(v.Mastery, v.ReviewCount, v.CorrectCount, v.LastReviewedAt))
            .ToListAsync(ct);

        var streak = await db.LearnerStreaks.AsNoTracking().FirstOrDefaultAsync(s => s.UserId == userId, ct);

        var planIds = await db.StudyPlans.AsNoTracking()
            .Where(plan => plan.UserId == userId)
            .Select(plan => plan.Id)
            .ToListAsync(ct);
        var planItems = planIds.Count == 0
            ? new List<PlanItemRow>()
            : await db.StudyPlanItems.AsNoTracking()
                .Where(p => planIds.Contains(p.StudyPlanId))
                .Select(p => new PlanItemRow(p.Status, p.DueDate, p.CompletedAt, p.SubtestCode))
                .ToListAsync(ct);

        var attemptsBySubtest = attemptIds
            .GroupBy(a => NormalizeSubtest(a.SubtestCode))
            .ToDictionary(g => g.Key, g => g.ToList());
        var evaluationsByAttemptId = evaluations.ToDictionary(e => e.AttemptId, e => e);
        var reviewsBySubtest = reviews
            .GroupBy(r => NormalizeSubtest(r.SubtestCode))
            .ToDictionary(g => g.Key, g => g.ToList());

        var targetDate = goal?.TargetExamDate ?? DateOnly.FromDateTime(now.UtcDateTime.AddMonths(3));
        var daysToTarget = (targetDate.ToDateTime(TimeOnly.MinValue) - now.UtcDateTime.Date).TotalDays;
        var weeksRemaining = Math.Max(0, (int)Math.Ceiling(daysToTarget / 7.0));

        var subtestResults = new Dictionary<string, SubtestComputationResult>();
        foreach (var code in SubtestCodes)
        {
            var target = ResolveSubtestTarget(code, goal);
            var attemptsForSubtest = attemptsBySubtest.GetValueOrDefault(code) ?? [];
            var reviewsForSubtest = reviewsBySubtest.GetValueOrDefault(code) ?? [];
            subtestResults[code] = ComputeSubtestReadiness(
                code,
                target,
                mockReports,
                attemptsForSubtest.Select(a => (a.Id, a.CompletedAt, a.AnalysisJson)).ToList(),
                evaluationsByAttemptId,
                reviewsForSubtest,
                now);
        }

        var vocabResult = ComputeVocabularyReadiness(vocabStats, cutoff30, now);

        var overall = ComputeOverall(subtestResults);
        var dataPointCount = subtestResults.Values.Sum(s => s.DataPoints) + vocabResult.DataPoints;
        var confidence = ResolveConfidence(dataPointCount, subtestResults);
        var weakestSubtest = subtestResults
            .Where(kv => kv.Value.Current.HasValue)
            .OrderBy(kv => kv.Value.Current!.Value - kv.Value.Target)
            .Select(kv => kv.Key)
            .FirstOrDefault();

        var historyOrdered = await db.ReadinessHistories.AsNoTracking()
            .Where(h => h.UserId == userId)
            .OrderBy(h => h.WeekStartDate)
            .ToListAsync(ct);

        var forecast = forecastCalculator.Compute(overall, ResolveOverallTarget(goal), weeksRemaining, historyOrdered);

        var risk = ResolveRisk(overall, ResolveOverallTarget(goal), weeksRemaining, dataPointCount, historyOrdered);
        var recommendedHours = ResolveRecommendedHours(overall, ResolveOverallTarget(goal), weeksRemaining, goal);

        var blockers = blockerRules.Build(new ReadinessBlockerContext(
            userId,
            now,
            subtestResults,
            vocabResult,
            mockReports,
            streak,
            planItems.Select(p => new ReadinessPlanItem(p.Status, p.DueDate, p.CompletedAt, p.SubtestCode)).ToList(),
            reviews,
            historyOrdered,
            weeksRemaining,
            ResolveOverallTarget(goal),
            overall));

        var snapshot = await UpsertSnapshotAsync(
            userId,
            now,
            targetDate,
            weeksRemaining,
            overall,
            subtestResults,
            vocabResult,
            risk,
            forecast,
            weakestSubtest,
            recommendedHours,
            confidence,
            dataPointCount,
            blockers,
            mockReports.Count,
            attemptIds.Count,
            reviews.Count,
            vocabStats.Count(v => v.LastReviewedAt >= cutoff30),
            streak,
            goal,
            ct);

        await AppendHistoryAsync(userId, now, overall, subtestResults, vocabResult, risk, forecast?.Probability, ct);

        return snapshot;
    }

    private async Task<ReadinessSnapshot?> GetLatestSnapshotAsync(string userId, CancellationToken ct)
    {
        var query = db.ReadinessSnapshots.Where(x => x.UserId == userId);
        if (!db.Database.IsSqlite())
        {
            return await query.OrderByDescending(x => x.ComputedAt).FirstOrDefaultAsync(ct);
        }

        var list = await query.ToListAsync(ct);
        return list.OrderByDescending(x => x.ComputedAt).FirstOrDefault();
    }

    private async Task<ReadinessSnapshot> UpsertSnapshotAsync(
        string userId,
        DateTimeOffset now,
        DateOnly targetDate,
        int weeksRemaining,
        decimal overall,
        Dictionary<string, SubtestComputationResult> subtests,
        VocabularyComputationResult vocab,
        string risk,
        ForecastResult? forecast,
        string? weakestSubtest,
        int recommendedHours,
        string confidence,
        int dataPointCount,
        IReadOnlyList<ReadinessBlockerDto> blockers,
        int mocksCompleted,
        int practiceCount,
        int expertReviewsCount,
        int vocabReviewed30d,
        LearnerStreak? streak,
        LearnerGoal? goal,
        CancellationToken ct)
    {
        var existing = await GetLatestSnapshotAsync(userId, ct);
        var snapshot = existing ?? new ReadinessSnapshot
        {
            Id = $"rs-{Guid.NewGuid():N}",
            UserId = userId,
            Version = 0
        };

        snapshot.ComputedAt = now;
        snapshot.ExpiresAt = now.Add(SnapshotCacheTtl);
        snapshot.Version += 1;
        snapshot.OverallReadiness = Round2(overall);
        snapshot.WritingReadiness = Round2(subtests["writing"].Current ?? 0);
        snapshot.SpeakingReadiness = Round2(subtests["speaking"].Current ?? 0);
        snapshot.ReadingReadiness = Round2(subtests["reading"].Current ?? 0);
        snapshot.ListeningReadiness = Round2(subtests["listening"].Current ?? 0);
        snapshot.VocabularyReadiness = Round2(vocab.Current);
        snapshot.OverallRisk = risk;
        snapshot.TargetDateProbability = forecast?.Probability is { } p ? Round2(p) : null;
        snapshot.WeakestSubtest = weakestSubtest;
        snapshot.RecommendedStudyHoursPerWeek = recommendedHours;
        snapshot.ConfidenceLevel = confidence;
        snapshot.DataPointCount = dataPointCount;

        snapshot.PayloadJson = JsonSupport.Serialize(new
        {
            targetDate = targetDate.ToString("yyyy-MM-dd"),
            weeksRemaining,
            overallRisk = risk,
            overallReadiness = snapshot.OverallReadiness,
            targetDateProbability = snapshot.TargetDateProbability,
            recommendedStudyHours = recommendedHours,
            recommendedStudyHoursRationale = BuildHoursRationale(overall, ResolveOverallTarget(goal), weeksRemaining),
            confidenceLevel = confidence,
            dataPointCount,
            weakestLink = weakestSubtest ?? "Insufficient data — practice to unlock readiness",
            subTests = subtests.Select(kv => new
            {
                id = kv.Key,
                code = kv.Key,
                name = CapitalizeSubtest(kv.Key),
                readiness = kv.Value.Current.HasValue ? Round2(kv.Value.Current.Value) : 0,
                target = kv.Value.Target,
                status = kv.Value.Current.HasValue ? StatusFor(kv.Value.Current.Value, kv.Value.Target) : "Insufficient data",
                isWeakest = kv.Key == weakestSubtest,
                confidenceBand = kv.Value.Confidence,
                dataPoints = kv.Value.DataPoints
            }).ToArray(),
            vocabulary = new
            {
                readiness = Round2(vocab.Current),
                target = 100,
                mastered = vocab.MasteredCount,
                masteryTarget = VocabularyMasteryTarget,
                accuracy30d = Round2(vocab.AccuracyLast30d),
                dataPoints = vocab.DataPoints
            },
            blockers = blockers.Select(b => new
            {
                id = b.Id,
                title = b.Title,
                description = b.Description,
                actionLabel = b.ActionLabel,
                actionHref = b.ActionHref,
                impactScore = b.ImpactScore,
                severity = b.Severity
            }).ToArray(),
            riskFactors = BuildRiskFactors(streak, subtests, vocab, weeksRemaining).Select(rf => new
            {
                label = rf.Label,
                severity = rf.Severity,
                impact = rf.Impact,
                description = rf.Description,
                actionHref = rf.ActionHref
            }).ToArray(),
            evidence = new
            {
                source = mocksCompleted + practiceCount + expertReviewsCount > 0 ? "live" : "no_evidence",
                mocksCompleted = mocksCompleted,
                practiceQuestions = practiceCount,
                expertReviews = expertReviewsCount,
                vocabReviewed30d,
                recentTrend = BuildRecentTrendMessage(snapshot.OverallReadiness, dataPointCount),
                lastUpdated = now
            }
        });

        if (existing is null)
        {
            db.ReadinessSnapshots.Add(snapshot);
        }

        await db.SaveChangesAsync(ct);
        return snapshot;
    }

    private async Task AppendHistoryAsync(
        string userId,
        DateTimeOffset now,
        decimal overall,
        Dictionary<string, SubtestComputationResult> subtests,
        VocabularyComputationResult vocab,
        string risk,
        decimal? probability,
        CancellationToken ct)
    {
        var weekStart = StartOfIsoWeek(DateOnly.FromDateTime(now.UtcDateTime));
        var existing = await db.ReadinessHistories
            .FirstOrDefaultAsync(h => h.UserId == userId && h.WeekStartDate == weekStart, ct);

        var entry = existing ?? new ReadinessHistory
        {
            Id = $"rh-{Guid.NewGuid():N}",
            UserId = userId,
            WeekStartDate = weekStart
        };

        entry.RecordedAt = now;
        entry.Overall = Round2(overall);
        entry.Writing = Round2(subtests["writing"].Current ?? 0);
        entry.Speaking = Round2(subtests["speaking"].Current ?? 0);
        entry.Reading = Round2(subtests["reading"].Current ?? 0);
        entry.Listening = Round2(subtests["listening"].Current ?? 0);
        entry.Vocabulary = Round2(vocab.Current);
        entry.Risk = risk;
        entry.TargetDateProbability = probability;

        if (existing is null)
        {
            db.ReadinessHistories.Add(entry);
        }

        await db.SaveChangesAsync(ct);
    }

    private static SubtestComputationResult ComputeSubtestReadiness(
        string subtestCode,
        decimal target,
        IReadOnlyList<MockReport> mocks,
        IReadOnlyList<(string Id, DateTimeOffset? CompletedAt, string AnalysisJson)> attempts,
        IReadOnlyDictionary<string, Evaluation> evaluationsByAttemptId,
        IReadOnlyList<ReviewRequest> reviews,
        DateTimeOffset now)
    {
        decimal weightedSum = 0;
        decimal weightTotal = 0;
        int dataPoints = 0;

        foreach (var mock in mocks)
        {
            var score = ExtractMockSubtestScore(mock.PayloadJson, subtestCode);
            if (!score.HasValue || mock.GeneratedAt is null) continue;
            var normalized = ClampReadiness(score.Value / 5m);
            var ageDays = Math.Max(0, (now - mock.GeneratedAt.Value).TotalDays);
            var weight = 2.0m * (decimal)Math.Exp(-ageDays / 14.0);
            weightedSum += normalized * weight;
            weightTotal += weight;
            dataPoints++;
        }

        foreach (var attempt in attempts)
        {
            if (!evaluationsByAttemptId.TryGetValue(attempt.Id, out var eval)) continue;
            var score = ExtractEvaluationScore(eval);
            if (!score.HasValue || attempt.CompletedAt is null) continue;
            var ageDays = Math.Max(0, (now - attempt.CompletedAt.Value).TotalDays);
            var weight = 1.0m * (decimal)Math.Exp(-ageDays / 14.0);
            weightedSum += score.Value * weight;
            weightTotal += weight;
            dataPoints++;
        }

        foreach (var review in reviews)
        {
            if (review.CompletedAt is null) continue;
            var score = ExtractReviewScore(review);
            if (!score.HasValue) continue;
            var ageDays = Math.Max(0, (now - review.CompletedAt.Value).TotalDays);
            var weight = 1.5m * (decimal)Math.Exp(-ageDays / 14.0);
            weightedSum += score.Value * weight;
            weightTotal += weight;
            dataPoints++;
        }

        decimal? current = weightTotal > 0 ? weightedSum / weightTotal : null;
        var confidence = dataPoints switch
        {
            0 => "None",
            <= 2 => "Low",
            <= 5 => "Medium",
            _ => "High"
        };

        return new SubtestComputationResult(current.HasValue ? ClampReadiness(current.Value) : null, target, dataPoints, confidence);
    }

    private static VocabularyComputationResult ComputeVocabularyReadiness(
        IReadOnlyCollection<VocabularyStatRow> stats,
        DateTimeOffset cutoff30,
        DateTimeOffset now)
    {
        if (stats.Count == 0)
        {
            return new VocabularyComputationResult(0, 0, 0, 0);
        }

        int mastered = 0;
        int reviewCount30d = 0;
        int correctCount30d = 0;
        foreach (var s in stats)
        {
            if (string.Equals(s.Mastery, "mastered", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(s.Mastery, "review", StringComparison.OrdinalIgnoreCase))
            {
                mastered++;
            }

            if (s.LastReviewedAt.HasValue && s.LastReviewedAt.Value >= cutoff30)
            {
                reviewCount30d += s.ReviewCount;
                correctCount30d += s.CorrectCount;
            }
        }

        decimal masteredScore = Math.Min(100m, (decimal)mastered / VocabularyMasteryTarget * 100m);
        decimal accuracy = reviewCount30d == 0 ? 0 : (decimal)correctCount30d / reviewCount30d * 100m;
        decimal readiness = ClampReadiness(0.6m * masteredScore + 0.4m * accuracy);
        int dataPoints = mastered + reviewCount30d;
        return new VocabularyComputationResult(readiness, mastered, accuracy, dataPoints);
    }

    private static decimal ComputeOverall(Dictionary<string, SubtestComputationResult> subtests)
    {
        var weights = new Dictionary<string, decimal>
        {
            ["writing"] = 0.30m,
            ["speaking"] = 0.30m,
            ["reading"] = 0.20m,
            ["listening"] = 0.20m
        };

        decimal sum = 0;
        decimal weightSum = 0;
        foreach (var (code, weight) in weights)
        {
            var result = subtests[code];
            if (!result.Current.HasValue) continue;
            sum += result.Current.Value * weight;
            weightSum += weight;
        }

        return weightSum == 0 ? 0 : ClampReadiness(sum / weightSum);
    }

    private static string ResolveConfidence(int totalDataPoints, Dictionary<string, SubtestComputationResult> subtests)
    {
        if (totalDataPoints < MinDataPointsForConfidence) return "Low";
        if (subtests.Values.Any(s => s.DataPoints == 0)) return "Medium";
        return totalDataPoints >= 15 ? "High" : "Medium";
    }

    private static string ResolveRisk(
        decimal overall,
        decimal target,
        int weeksRemaining,
        int dataPointCount,
        IReadOnlyList<ReadinessHistory> history)
    {
        if (dataPointCount < MinDataPointsForConfidence) return "Unknown";

        var gap = target - overall;
        if (gap > 15 && weeksRemaining <= 4) return "High";
        if (gap > 15) return "High";
        if (weeksRemaining <= 2 && gap > 5) return "High";

        if (gap <= 0)
        {
            if (history.Count >= 2)
            {
                var lastTwo = history.TakeLast(2).ToList();
                if (lastTwo.All(h => h.Overall >= target)) return "Low";
            }
            return "Moderate";
        }

        return gap <= 15 ? "Moderate" : "High";
    }

    private static int ResolveRecommendedHours(decimal overall, decimal target, int weeksRemaining, LearnerGoal? goal)
    {
        var weeklyFloor = goal?.StudyHoursPerWeek > 0 ? goal.StudyHoursPerWeek : 5;
        if (target <= overall || weeksRemaining <= 0) return weeklyFloor;
        var requiredImprovement = target - overall;
        var totalHoursNeeded = requiredImprovement / ImprovementPointsPerStudyHour;
        var weekly = (int)Math.Ceiling(totalHoursNeeded / Math.Max(weeksRemaining, 1) * 1.2m);
        return Math.Clamp(Math.Max(weekly, weeklyFloor), weeklyFloor, MaxRecommendedHoursPerWeek);
    }

    private static decimal ResolveOverallTarget(LearnerGoal? goal)
    {
        // Default target = Grade B equivalent (350/500 = 70). Honor explicit
        // sub-test targets if set on the goal by averaging them.
        if (goal is null) return 70m;
        var explicitTargets = new[] { goal.TargetWritingScore, goal.TargetSpeakingScore, goal.TargetReadingScore, goal.TargetListeningScore }
            .Where(x => x.HasValue && x.Value > 0)
            .Select(x => (decimal)x!.Value)
            .ToList();
        if (explicitTargets.Count == 0) return 70m;
        return ClampReadiness(explicitTargets.Average() / 5m);
    }

    private static decimal ResolveSubtestTarget(string code, LearnerGoal? goal)
    {
        if (goal is null) return 70m;
        var raw = code switch
        {
            "writing" => goal.TargetWritingScore,
            "speaking" => goal.TargetSpeakingScore,
            "reading" => goal.TargetReadingScore,
            "listening" => goal.TargetListeningScore,
            _ => null
        };
        return raw is > 0 ? ClampReadiness((decimal)raw.Value / 5m) : 70m;
    }

    private static IReadOnlyList<RiskFactorDto> BuildRiskFactors(
        LearnerStreak? streak,
        Dictionary<string, SubtestComputationResult> subtests,
        VocabularyComputationResult vocab,
        int weeksRemaining)
    {
        var factors = new List<RiskFactorDto>();
        if (streak is { CurrentStreak: 0 })
        {
            factors.Add(new RiskFactorDto("No active practice streak", "high", 25m, "Consistency improves outcomes — start a streak by completing one practice task today.", "/study-plan"));
        }
        if (weeksRemaining <= 4)
        {
            factors.Add(new RiskFactorDto("Exam approaching", "high", 30m, $"Only {weeksRemaining} weeks until your target date.", null));
        }
        foreach (var (code, result) in subtests)
        {
            if (result.Current.HasValue && result.Current.Value < result.Target - 15)
            {
                factors.Add(new RiskFactorDto(
                    $"{CapitalizeSubtest(code)} far below target",
                    "medium",
                    Math.Min(50m, result.Target - result.Current.Value),
                    $"Current {CapitalizeSubtest(code)} readiness is {Round2(result.Current.Value)} vs target {result.Target}.",
                    PracticeHref(code)));
            }
        }
        if (vocab.Current < 40 && vocab.DataPoints > 0)
        {
            factors.Add(new RiskFactorDto("Vocabulary mastery low", "medium", 20m, $"Mastered {vocab.MasteredCount}/{VocabularyMasteryTarget} target terms.", "/vocabulary?filter=due"));
        }
        return factors;
    }

    private static int? ExtractMockSubtestScore(string payloadJson, string subtestCode)
    {
        var payload = JsonSupport.Deserialize<Dictionary<string, object?>>(payloadJson, new Dictionary<string, object?>());
        if (!payload.TryGetValue("subTests", out var subTestsRaw)) return null;
        if (subTestsRaw is not System.Text.Json.JsonElement element || element.ValueKind != System.Text.Json.JsonValueKind.Array) return null;
        foreach (var item in element.EnumerateArray())
        {
            if (!item.TryGetProperty("subtestCode", out var codeProp) && !item.TryGetProperty("code", out codeProp)) continue;
            var code = codeProp.GetString();
            if (!string.Equals(code, subtestCode, StringComparison.OrdinalIgnoreCase)) continue;
            if (item.TryGetProperty("scaledScore", out var scaled) && scaled.ValueKind == System.Text.Json.JsonValueKind.Number)
            {
                return scaled.GetInt32();
            }
            if (item.TryGetProperty("score", out var raw) && raw.ValueKind == System.Text.Json.JsonValueKind.Number)
            {
                return raw.GetInt32();
            }
        }
        return null;
    }

    private static decimal? ExtractEvaluationScore(Evaluation eval)
    {
        if (string.IsNullOrWhiteSpace(eval.ScoreRange)) return null;
        var parts = eval.ScoreRange.Split('-');
        if (parts.Length == 2 && int.TryParse(parts[0], out var lo) && int.TryParse(parts[1], out var hi))
        {
            return ClampReadiness((decimal)(lo + hi) / 2m / 5m);
        }
        if (int.TryParse(eval.ScoreRange, out var single))
        {
            return ClampReadiness((decimal)single / 5m);
        }
        return null;
    }

    private static decimal? ExtractReviewScore(ReviewRequest review)
    {
        var payload = JsonSupport.Deserialize<Dictionary<string, object?>>(review.EligibilitySnapshotJson, new Dictionary<string, object?>());
        if (payload.TryGetValue("grade", out var gradeObj))
        {
            var grade = gradeObj?.ToString();
            return grade?.ToUpperInvariant() switch
            {
                "A" => 90m,
                "B" => 75m,
                "C" => 55m,
                _ => null
            };
        }
        return null;
    }

    private static string BuildHoursRationale(decimal overall, decimal target, int weeksRemaining)
    {
        if (weeksRemaining <= 0) return "Your target date has arrived — practice consistently leading up to exam day.";
        if (overall >= target) return $"You are on track. Maintain your current pace through exam day.";
        var gap = target - overall;
        return $"To close the {Round2(gap)}-point gap to your target with {weeksRemaining} weeks remaining, plan focused study sessions on your weakest sub-test.";
    }

    private static string BuildRecentTrendMessage(decimal overall, int dataPointCount)
        => dataPointCount switch
        {
            0 => "Complete practice, mocks, or tutor reviews to unlock live readiness analytics.",
            < MinDataPointsForConfidence => "Limited data — keep practicing to improve confidence in your readiness estimate.",
            _ => $"Current overall readiness {Round2(overall)}/100 based on recent activity."
        };

    private static string StatusFor(decimal current, decimal target)
    {
        var gap = target - current;
        if (gap <= 0) return "On track";
        if (gap <= 10) return "Close to target";
        if (gap <= 20) return "Needs focus";
        return "Needs urgent attention";
    }

    private static string CapitalizeSubtest(string code) => code switch
    {
        "writing" => "Writing",
        "speaking" => "Speaking",
        "reading" => "Reading",
        "listening" => "Listening",
        _ => code
    };

    private static string PracticeHref(string code) => code switch
    {
        "writing" => "/writing",
        "speaking" => "/speaking",
        "reading" => "/reading",
        "listening" => "/listening",
        _ => "/study-plan"
    };

    private static string NormalizeSubtest(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) return string.Empty;
        var lower = code.Trim().ToLowerInvariant();
        if (lower.StartsWith("writing")) return "writing";
        if (lower.StartsWith("speaking")) return "speaking";
        if (lower.StartsWith("reading")) return "reading";
        if (lower.StartsWith("listening")) return "listening";
        return lower;
    }

    private static decimal ClampReadiness(decimal value) => Math.Max(0m, Math.Min(100m, value));

    private static decimal Round2(decimal value) => Math.Round(value, 2);

    private static DateOnly StartOfIsoWeek(DateOnly date)
    {
        var dt = date.ToDateTime(TimeOnly.MinValue);
        int diff = (7 + (int)dt.DayOfWeek - (int)DayOfWeek.Monday) % 7;
        return DateOnly.FromDateTime(dt.AddDays(-diff));
    }
}

public sealed record SubtestComputationResult(decimal? Current, decimal Target, int DataPoints, string Confidence);
public sealed record VocabularyComputationResult(decimal Current, int MasteredCount, decimal AccuracyLast30d, int DataPoints);
public sealed record RiskFactorDto(string Label, string Severity, decimal Impact, string Description, string? ActionHref);
public sealed record VocabularyStatRow(string Mastery, int ReviewCount, int CorrectCount, DateTimeOffset? LastReviewedAt);
public sealed record PlanItemRow(StudyPlanItemStatus Status, DateOnly DueDate, DateTimeOffset? CompletedAt, string SubtestCode);
