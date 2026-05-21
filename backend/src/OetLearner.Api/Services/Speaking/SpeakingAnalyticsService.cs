using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Speaking;

// Phase 6 / Section G of the OET Speaking module roadmap.
//
// SpeakingAnalyticsService surfaces the learner-, class-, tutor-, and
// content-level analytics that power the three dashboards described in
// plan section B.6 + B.7. All four methods are read-only and cache their
// projections in `IMemoryCache` for 5 minutes per `(userId, methodName)`
// key, which keeps the dashboards snappy without serving stale data for
// more than one tick.
public sealed class SpeakingAnalyticsService(
    LearnerDbContext db,
    IMemoryCache cache)
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);
    private const string CachePrefix = "speaking-analytics:";

    private static readonly string[] LinguisticCriteria =
    {
        "intelligibility", "fluency", "appropriateness", "grammarExpression",
    };

    private static readonly string[] ClinicalCriteria =
    {
        "relationshipBuilding", "patientPerspective", "structure",
        "informationGathering", "informationGiving",
    };

    // ── 1. Learner analytics ────────────────────────────────────────────────
    public async Task<object> GetLearnerAnalyticsAsync(string userId, CancellationToken ct)
    {
        var key = $"{CachePrefix}learner:{userId}";
        if (cache.TryGetValue(key, out object? cached) && cached is not null) return cached;

        var since = DateTimeOffset.UtcNow.AddDays(-30);

        // Pull learner sessions + their AI assessments + transcripts.
        var sessions = await db.SpeakingSessions
            .AsNoTracking()
            .Where(s => s.UserId == userId && s.CreatedAt >= since)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync(ct);

        var sessionIds = sessions.Select(s => s.Id).ToList();

        var assessments = await db.SpeakingAiAssessments
            .AsNoTracking()
            .Where(a => sessionIds.Contains(a.SpeakingSessionId))
            .OrderByDescending(a => a.GeneratedAt)
            .ToListAsync(ct);

        var transcripts = await db.SpeakingTranscripts
            .AsNoTracking()
            .Where(t => sessionIds.Contains(t.SpeakingSessionId) && t.IsLatest)
            .ToListAsync(ct);

        var latest = assessments.FirstOrDefault();

        // Estimated band uses the most recent AI assessment's `ReadinessBand`
        // when available; otherwise we fall back to `not_enough_data`.
        var estimatedBand = latest?.ReadinessBand ?? "not_enough_data";
        var currentScaled = latest?.EstimatedScaledScore ?? 0;
        var sessionCount = sessions.Count;

        // Criterion trends — group by ISO week.
        var trends = new List<object>();
        foreach (var a in assessments)
        {
            var weekStart = StartOfWeek(a.GeneratedAt);
            var date = weekStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            foreach (var (code, score) in EnumerateCriteria(a))
            {
                trends.Add(new
                {
                    date,
                    criterion = code,
                    score,
                });
            }
        }

        // Speed = sum(words) / sum(minutes) over last 5 sessions.
        var lastFiveSessionIds = sessions.Take(5).Select(s => s.Id).ToHashSet();
        var totalWords = 0;
        var totalMinutes = 0.0;
        var totalElapsedSeconds = 0;
        var ctr = 0;
        foreach (var s in sessions.Where(s => lastFiveSessionIds.Contains(s.Id)))
        {
            var transcript = transcripts.FirstOrDefault(t => t.SpeakingSessionId == s.Id);
            if (transcript is null) continue;
            var minutes = s.ElapsedSeconds <= 0 ? 0 : s.ElapsedSeconds / 60.0;
            if (minutes <= 0.05) continue;
            totalWords += transcript.WordCount;
            totalMinutes += minutes;
            totalElapsedSeconds += s.ElapsedSeconds;
            ctr++;
        }
        var speakingSpeedWpm = totalMinutes > 0 ? Math.Round(totalWords / totalMinutes, 1) : 0;
        var avgRolePlayLengthSeconds = ctr > 0 ? totalElapsedSeconds / (double)ctr : 0;

        // Recurring issues — count rulebook codes across the last 30 days
        // and surface the top-5 most frequent.
        var issueCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in assessments)
        {
            try
            {
                var findings = JsonSerializer.Deserialize<List<string>>(a.RulebookFindingsJson)
                               ?? new List<string>();
                foreach (var f in findings)
                {
                    if (string.IsNullOrWhiteSpace(f)) continue;
                    issueCounts[f] = issueCounts.GetValueOrDefault(f, 0) + 1;
                }
            }
            catch (JsonException)
            {
                // Tolerate older rows with bad JSON.
            }
        }
        var recurringIssues = issueCounts
            .OrderByDescending(kv => kv.Value)
            .Take(5)
            .Select(kv => kv.Key)
            .ToArray();

        // Find weakest + strongest criterion (averaged across last 30 days).
        var criterionAverages = new Dictionary<string, (double sum, int count)>();
        foreach (var a in assessments)
        {
            foreach (var (code, score) in EnumerateCriteria(a))
            {
                var (sum, count) = criterionAverages.GetValueOrDefault(code, (0.0, 0));
                criterionAverages[code] = (sum + score, count + 1);
            }
        }
        string? weakestCriterion = null;
        string? strongestCriterion = null;
        var weakestAvg = double.MaxValue;
        var strongestAvg = double.MinValue;
        foreach (var (code, (sum, count)) in criterionAverages)
        {
            if (count == 0) continue;
            var avg = sum / count;
            // Normalize clinical (0..3) → 0..6 for comparison.
            var normalized = ClinicalCriteria.Contains(code) ? avg * 2 : avg;
            if (normalized < weakestAvg) { weakestAvg = normalized; weakestCriterion = code; }
            if (normalized > strongestAvg) { strongestAvg = normalized; strongestCriterion = code; }
        }

        // Readiness status — naive mapping from latest band string.
        var readinessStatus = MapReadinessStatus(estimatedBand);

        var result = new
        {
            estimatedBand,
            currentScaled,
            sessionCount,
            criterionTrends = trends,
            avgRolePlayLengthSeconds = Math.Round(avgRolePlayLengthSeconds, 1),
            speakingSpeedWpm,
            recurringIssues,
            readinessStatus,
            weakestCriterion = weakestCriterion ?? string.Empty,
            strongestCriterion = strongestCriterion ?? string.Empty,
        };

        cache.Set(key, (object)result, CacheTtl);
        return result;
    }

    // ── 2. Class analytics (teacher view) ───────────────────────────────────
    public async Task<object> GetClassAnalyticsAsync(
        string? cohortId,
        string? professionId,
        CancellationToken ct)
    {
        var key = $"{CachePrefix}class:{cohortId ?? "_"}:{professionId ?? "_"}";
        if (cache.TryGetValue(key, out object? cached) && cached is not null) return cached;

        // The cohort/profession filters here are best-effort; the actual
        // `cohort` notion lives on a separate `LearnerCohort` table (TODO:
        // wire that in once it ships in Section L). For now we filter on
        // profession via the user's active profession.
        IQueryable<SpeakingSession> q = db.SpeakingSessions.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(professionId))
        {
            var profUserIds = await db.Users
                .AsNoTracking()
                .Where(u => u.ActiveProfessionId == professionId)
                .Select(u => u.Id)
                .ToListAsync(ct);
            q = q.Where(s => profUserIds.Contains(s.UserId));
        }

        var since7d = DateTimeOffset.UtcNow.AddDays(-7);
        var since30d = DateTimeOffset.UtcNow.AddDays(-30);

        var sessions = await q.Where(s => s.CreatedAt >= since30d).ToListAsync(ct);
        var sessionIds = sessions.Select(s => s.Id).ToList();

        var assessments = await db.SpeakingAiAssessments
            .AsNoTracking()
            .Where(a => sessionIds.Contains(a.SpeakingSessionId))
            .ToListAsync(ct);

        // Avg estimated band — coerce string bands ("not_ready", "borderline",
        // "ready") to a 1..3 score for averaging.
        double avgEstimatedBand = 0;
        if (assessments.Count > 0)
        {
            avgEstimatedBand = assessments.Average(a => BandStringToScore(a.ReadinessBand));
        }

        // Weakest criterion across the class.
        var criterionAverages = new Dictionary<string, (double sum, int count)>();
        foreach (var a in assessments)
        {
            foreach (var (code, score) in EnumerateCriteria(a))
            {
                var (sum, count) = criterionAverages.GetValueOrDefault(code, (0.0, 0));
                criterionAverages[code] = (sum + score, count + 1);
            }
        }
        string? weakest = null;
        var weakestAvg = double.MaxValue;
        foreach (var (code, (sum, count)) in criterionAverages)
        {
            if (count == 0) continue;
            var avg = sum / count;
            var normalized = ClinicalCriteria.Contains(code) ? avg * 2 : avg;
            if (normalized < weakestAvg) { weakestAvg = normalized; weakest = code; }
        }

        // Common issues across the class.
        var issueCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in assessments)
        {
            try
            {
                var findings = JsonSerializer.Deserialize<List<string>>(a.RulebookFindingsJson)
                               ?? new List<string>();
                foreach (var f in findings)
                {
                    if (string.IsNullOrWhiteSpace(f)) continue;
                    issueCounts[f] = issueCounts.GetValueOrDefault(f, 0) + 1;
                }
            }
            catch (JsonException) { }
        }
        var commonIssues = issueCounts
            .OrderByDescending(kv => kv.Value)
            .Take(10)
            .Select(kv => new { code = kv.Key, count = kv.Value })
            .ToArray();

        var sessionVolume7d = sessions.Count(s => s.CreatedAt >= since7d);

        // Tutor activity — count of distinct interlocutors used in the last 7d.
        var tutorActivityCount = sessions
            .Where(s => s.CreatedAt >= since7d && !string.IsNullOrWhiteSpace(s.InterlocutorActorId))
            .Select(s => s.InterlocutorActorId)
            .Distinct()
            .Count();

        var result = new
        {
            avgEstimatedBand = Math.Round(avgEstimatedBand, 2),
            weakestCriterionAcrossClass = weakest ?? string.Empty,
            commonIssues,
            sessionVolume7d,
            tutorActivityCount,
            totalLearners = sessions.Select(s => s.UserId).Distinct().Count(),
            totalSessions30d = sessions.Count,
        };

        cache.Set(key, (object)result, CacheTtl);
        return result;
    }

    // ── 3. Tutor consistency ───────────────────────────────────────────────
    public async Task<object> GetTutorConsistencyAsync(string? tutorId, CancellationToken ct)
    {
        var key = $"{CachePrefix}tutor-consistency:{tutorId ?? "_all"}";
        if (cache.TryGetValue(key, out object? cached) && cached is not null) return cached;

        // 3a. MAE vs gold — averaged TotalAbsoluteError across the tutor's
        // SpeakingCalibrationScore rows (divided by 9 to get per-criterion).
        IQueryable<SpeakingCalibrationScore> calibrationQuery = db.SpeakingCalibrationScores.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(tutorId))
        {
            calibrationQuery = calibrationQuery.Where(s => s.TutorId == tutorId);
        }
        var calibrationScores = await calibrationQuery.ToListAsync(ct);

        double meanAbsoluteErrorVsGold = 0;
        if (calibrationScores.Count > 0)
        {
            // 9 criteria => divide by 9 to get average per-criterion.
            meanAbsoluteErrorVsGold = calibrationScores.Average(s => s.TotalAbsoluteError) / 9.0;
        }

        // 3b. MAE vs AI — parse `CalibrationDeltaJson` on the tutor's
        // SpeakingTutorAssessment rows.
        IQueryable<SpeakingTutorAssessment> tutorQuery = db.SpeakingTutorAssessments.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(tutorId))
        {
            tutorQuery = tutorQuery.Where(t => t.TutorId == tutorId);
        }
        var tutorAssessments = await tutorQuery
            .Where(t => t.IsFinal && t.CalibrationDeltaJson != null)
            .ToListAsync(ct);

        double meanAbsoluteErrorVsAi = 0;
        var aiAssessmentCount = 0;
        var totalDelta = 0.0;
        foreach (var ta in tutorAssessments)
        {
            try
            {
                var deltas = JsonSerializer.Deserialize<Dictionary<string, double>>(
                    ta.CalibrationDeltaJson!) ?? new();
                foreach (var (_, v) in deltas)
                {
                    totalDelta += Math.Abs(v);
                }
                if (deltas.Count > 0) aiAssessmentCount++;
            }
            catch (JsonException) { }
        }
        if (aiAssessmentCount > 0)
        {
            meanAbsoluteErrorVsAi = totalDelta / (aiAssessmentCount * 9.0);
        }

        var result = new
        {
            tutorId,
            meanAbsoluteErrorVsGold = Math.Round(meanAbsoluteErrorVsGold, 3),
            meanAbsoluteErrorVsAi = Math.Round(meanAbsoluteErrorVsAi, 3),
            calibrationSamples = calibrationScores.Count,
            tutorAssessmentsScored = tutorAssessments.Count,
        };

        cache.Set(key, (object)result, CacheTtl);
        return result;
    }

    // ── 4. Content difficulty (admin) ──────────────────────────────────────
    public async Task<object> GetContentDifficultyAsync(string? professionId, CancellationToken ct)
    {
        var key = $"{CachePrefix}content-difficulty:{professionId ?? "_all"}";
        if (cache.TryGetValue(key, out object? cached) && cached is not null) return cached;

        IQueryable<RolePlayCard> cardQuery = db.RolePlayCards.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(professionId))
        {
            cardQuery = cardQuery.Where(c => c.ProfessionId == professionId);
        }
        var cards = await cardQuery.ToListAsync(ct);
        var cardIds = cards.Select(c => c.Id).ToList();

        var sessions = await db.SpeakingSessions
            .AsNoTracking()
            .Where(s => cardIds.Contains(s.RolePlayCardId))
            .ToListAsync(ct);

        var sessionIds = sessions.Select(s => s.Id).ToList();
        var assessments = await db.SpeakingAiAssessments
            .AsNoTracking()
            .Where(a => sessionIds.Contains(a.SpeakingSessionId))
            .ToListAsync(ct);

        var byCard = sessions
            .GroupBy(s => s.RolePlayCardId)
            .Select(g =>
            {
                var attempts = g.Count();
                var completed = g.Count(s => s.State == SpeakingSessionState.Finished);
                var completionRate = attempts > 0 ? Math.Round(completed * 100.0 / attempts, 1) : 0;
                var avgTimeOnCard = attempts > 0
                    ? Math.Round(g.Average(s => (double)s.ElapsedSeconds), 1)
                    : 0;
                var cardSessionIds = g.Select(s => s.Id).ToHashSet();
                var cardAssessments = assessments.Where(a => cardSessionIds.Contains(a.SpeakingSessionId)).ToList();
                var avgScaledScore = cardAssessments.Count > 0
                    ? Math.Round(cardAssessments.Average(a => (double)a.EstimatedScaledScore), 1)
                    : 0;
                var title = cards.FirstOrDefault(c => c.Id == g.Key)?.ScenarioTitle ?? g.Key;
                return new
                {
                    rolePlayCardId = g.Key,
                    title,
                    attempts,
                    completionRate,
                    avgScaledScore,
                    avgTimeOnCardSeconds = avgTimeOnCard,
                };
            })
            .OrderByDescending(x => x.attempts)
            .ToArray();

        var result = new { cards = byCard };
        cache.Set(key, (object)result, CacheTtl);
        return result;
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static IEnumerable<(string code, int score)> EnumerateCriteria(SpeakingAiAssessment a)
    {
        yield return ("intelligibility", a.Intelligibility);
        yield return ("fluency", a.Fluency);
        yield return ("appropriateness", a.Appropriateness);
        yield return ("grammarExpression", a.GrammarExpression);
        yield return ("relationshipBuilding", a.RelationshipBuilding);
        yield return ("patientPerspective", a.PatientPerspective);
        yield return ("structure", a.Structure);
        yield return ("informationGathering", a.InformationGathering);
        yield return ("informationGiving", a.InformationGiving);
    }

    private static DateTimeOffset StartOfWeek(DateTimeOffset dt)
    {
        var diff = (7 + (dt.DayOfWeek - DayOfWeek.Monday)) % 7;
        return new DateTimeOffset(dt.Date.AddDays(-diff), TimeSpan.Zero);
    }

    private static int BandStringToScore(string band) => (band ?? string.Empty).ToLowerInvariant() switch
    {
        "ready" => 3,
        "borderline" or "borderline_ready" => 2,
        "not_ready" or "not-ready" => 1,
        _ => 0,
    };

    private static string MapReadinessStatus(string band) => (band ?? string.Empty).ToLowerInvariant() switch
    {
        "ready" => "on_track",
        "borderline" or "borderline_ready" => "needs_focus",
        "not_ready" or "not-ready" => "at_risk",
        _ => "not_enough_data",
    };
}
