using Microsoft.EntityFrameworkCore;
using OetWithDrHesham.Api.Contracts;
using OetWithDrHesham.Api.Data;
using OetWithDrHesham.Api.Domain;

namespace OetWithDrHesham.Api.Services.Writing;

public sealed record WritingAnalyticsBandPoint(Guid SubmissionId, DateTimeOffset Date, int RawTotal, int EstimatedBand, string LetterType, bool IsRevision);

public sealed record WritingAnalyticsCriteriaRadar(int C1, int C2, int C3, int C4, int C5, int C6);

public sealed record WritingAnalyticsLetterTypeRow(string LetterType, int Attempts, double AverageBand);

public sealed record WritingAnalyticsCanonRow(string RuleId, string Severity, int Count, int TrendLast30Days);

public sealed record WritingAnalyticsTimeBucket(string BucketLabel, int Count);

public sealed record WritingAnalyticsActivityCell(DateOnly Date, int Count);

public sealed record WritingAnalyticsDashboard(
    string? LatestBandLabel,
    int? LatestRawTotal,
    int TrendDeltaLastFive,
    string TargetBand,
    int? DaysToExam,
    int StreakDays,
    string? TopWeaknessCriterion,
    string? TopWeaknessLabel);

public sealed record WritingAnalyticsBundle(
    WritingAnalyticsDashboard Dashboard,
    IReadOnlyList<WritingAnalyticsBandPoint> Bands,
    WritingAnalyticsCriteriaRadar Criteria,
    IReadOnlyList<WritingAnalyticsLetterTypeRow> LetterTypes,
    IReadOnlyList<WritingAnalyticsCanonRow> Canon,
    int AverageCompletionSeconds,
    int PercentWithin40Min,
    IReadOnlyList<WritingAnalyticsTimeBucket> TimeDistribution,
    IReadOnlyDictionary<string, double> SkillMastery,
    IReadOnlyList<WritingAnalyticsActivityCell> Calendar);

public interface IWritingAnalyticsServiceV2
{
    Task<WritingAnalyticsBundle> ComputeAsync(string userId, CancellationToken ct);
    Task<WritingAnalyticsDashboard> GetDashboardViewAsync(string userId, CancellationToken ct);
    Task<IReadOnlyList<WritingAnalyticsBandPoint>> GetBandsHistoryAsync(string userId, int take, CancellationToken ct);
    Task<IReadOnlyList<WritingAnalyticsCanonRow>> GetCanonTrackerAsync(string userId, int days, CancellationToken ct);
    Task<IReadOnlyList<WritingAnalyticsActivityCell>> GetActivityHeatmapAsync(string userId, int days, CancellationToken ct);

    // ── V2 endpoint contract adapters ────────────────────────────────────────
    Task<WritingStatsDashboardResponse> GetDashboardAsync(string userId, CancellationToken ct);
    Task<WritingStatsBandsResponse> GetBandsAsync(string userId, CancellationToken ct);
    Task<WritingStatsCriteriaResponse> GetCriteriaAsync(string userId, CancellationToken ct);
    Task<WritingStatsLetterTypesResponse> GetLetterTypesAsync(string userId, CancellationToken ct);
    Task<WritingStatsCanonResponse> GetCanonStatsAsync(string userId, CancellationToken ct);
    Task<WritingStatsTimeResponse> GetTimeStatsAsync(string userId, CancellationToken ct);
    Task<WritingStatsSkillsResponse> GetSkillsAsync(string userId, CancellationToken ct);
    Task<WritingStatsCalendarResponse> GetCalendarAsync(string userId, CancellationToken ct);
    Task<WritingStatsExportResponse> ExportStatsAsync(string userId, string format, CancellationToken ct);
}

public sealed class WritingAnalyticsServiceV2(LearnerDbContext db, TimeProvider clock) : IWritingAnalyticsServiceV2
{
    public async Task<WritingAnalyticsBundle> ComputeAsync(string userId, CancellationToken ct)
    {
        var dashboard = await GetDashboardViewAsync(userId, ct);
        var bands = await GetBandsHistoryAsync(userId, 20, ct);
        var criteria = await GetCriteriaRadarAsync(userId, ct);
        var letterTypes = await GetLetterTypesViewAsync(userId, ct);
        var canon = await GetCanonTrackerAsync(userId, 30, ct);
        var (avgSeconds, withinPct, timeBuckets) = await GetTimeStatsViewAsync(userId, ct);
        var skills = await GetSkillMasteryAsync(userId, ct);
        var calendar = await GetActivityHeatmapAsync(userId, 90, ct);
        return new WritingAnalyticsBundle(dashboard, bands, criteria, letterTypes, canon, avgSeconds, withinPct, timeBuckets, skills, calendar);
    }

    public async Task<WritingAnalyticsDashboard> GetDashboardViewAsync(string userId, CancellationToken ct)
    {
        var profile = await db.LearnerWritingProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == userId, ct);
        var grades = await db.WritingGrades.AsNoTracking()
            .Join(db.WritingSubmissions.AsNoTracking(), g => g.SubmissionId, s => s.Id, (g, s) => new { g, s })
            .Where(x => x.s.UserId == userId)
            .OrderByDescending(x => x.g.GradedAt)
            .Take(5)
            .Select(x => new { x.g.RawTotal, x.g.BandLabel })
            .ToListAsync(ct);
        var latest = grades.FirstOrDefault();
        var avg = grades.Count == 0 ? 0 : grades.Average(g => (double)g.RawTotal);
        var trendDelta = latest is null ? 0 : (int)Math.Round(latest.RawTotal - avg);
        var daysToExam = profile?.ExamDate is null ? (int?)null : Math.Max(0, (int)Math.Ceiling((profile.ExamDate.Value - clock.GetUtcNow()).TotalDays));
        var streak = await ComputeStreakAsync(userId, ct);
        var (topWeakness, topLabel) = await GetTopWeaknessAsync(userId, ct);
        return new WritingAnalyticsDashboard(
            latest?.BandLabel,
            latest?.RawTotal,
            trendDelta,
            profile?.TargetBand ?? "B",
            daysToExam,
            streak,
            topWeakness,
            topLabel);
    }

    public async Task<IReadOnlyList<WritingAnalyticsBandPoint>> GetBandsHistoryAsync(string userId, int take, CancellationToken ct)
    {
        var rows = await db.WritingGrades.AsNoTracking()
            .Join(db.WritingSubmissions.AsNoTracking(), g => g.SubmissionId, s => s.Id, (g, s) => new { g, s })
            .Join(db.WritingScenarios.AsNoTracking(), x => x.s.ScenarioId, s => s.Id, (x, s) => new { x.g, x.s, s.LetterType })
            .Where(x => x.s.UserId == userId)
            .OrderByDescending(x => x.g.GradedAt)
            .Take(Math.Clamp(take, 1, 100))
            .Select(x => new { x.g.SubmissionId, x.g.GradedAt, x.g.RawTotal, x.g.EstimatedBand, x.LetterType, x.s.IsRevision })
            .ToListAsync(ct);
        return rows
            .OrderBy(r => r.GradedAt)
            .Select(r => new WritingAnalyticsBandPoint(r.SubmissionId, r.GradedAt, r.RawTotal, r.EstimatedBand, r.LetterType, r.IsRevision))
            .ToList();
    }

    public async Task<IReadOnlyList<WritingAnalyticsCanonRow>> GetCanonTrackerAsync(string userId, int days, CancellationToken ct)
    {
        var since = clock.GetUtcNow().AddDays(-Math.Clamp(days, 1, 365));
        // Project the grouped aggregates into an anonymous type so the ORDER BY /
        // TAKE stay translatable in SQL — EF cannot translate ordering over a
        // property of a positional-record constructor projection
        // (WritingAnalyticsCanonRow), only over the group/anonymous shape.
        var grouped = await db.WritingCanonViolations.AsNoTracking()
            .Join(db.WritingSubmissions.AsNoTracking(), v => v.SubmissionId, s => s.Id, (v, s) => new { v, s })
            .Where(x => x.s.UserId == userId && x.v.DetectedAt >= since)
            .GroupBy(x => new { x.v.RuleId, x.v.Severity })
            .Select(g => new { g.Key.RuleId, g.Key.Severity, Count = g.Count() })
            .OrderByDescending(r => r.Count)
            .Take(15)
            .ToListAsync(ct);
        return grouped
            .Select(r => new WritingAnalyticsCanonRow(r.RuleId, r.Severity, r.Count, r.Count))
            .ToList();
    }

    public async Task<IReadOnlyList<WritingAnalyticsActivityCell>> GetActivityHeatmapAsync(string userId, int days, CancellationToken ct)
    {
        var since = clock.GetUtcNow().AddDays(-Math.Clamp(days, 1, 365));
        var raw = await db.WritingSubmissions.AsNoTracking()
            .Where(s => s.UserId == userId && s.SubmittedAt >= since)
            .Select(s => s.SubmittedAt)
            .ToListAsync(ct);
        return raw
            .GroupBy(d => d.UtcDateTime.Date)
            .Select(g => new WritingAnalyticsActivityCell(DateOnly.FromDateTime(g.Key), g.Count()))
            .OrderBy(c => c.Date)
            .ToList();
    }

    private async Task<WritingAnalyticsCriteriaRadar> GetCriteriaRadarAsync(string userId, CancellationToken ct)
    {
        var grades = await db.WritingGrades.AsNoTracking()
            .Join(db.WritingSubmissions.AsNoTracking(), g => g.SubmissionId, s => s.Id, (g, s) => new { g, s })
            .Where(x => x.s.UserId == userId)
            .OrderByDescending(x => x.g.GradedAt)
            .Take(5)
            .Select(x => new { x.g.C1Purpose, x.g.C2Content, x.g.C3Conciseness, x.g.C4Genre, x.g.C5Organisation, x.g.C6Language })
            .ToListAsync(ct);
        if (grades.Count == 0) return new WritingAnalyticsCriteriaRadar(0, 0, 0, 0, 0, 0);
        return new WritingAnalyticsCriteriaRadar(
            (int)Math.Round(grades.Average(g => (double)g.C1Purpose)),
            (int)Math.Round(grades.Average(g => (double)g.C2Content)),
            (int)Math.Round(grades.Average(g => (double)g.C3Conciseness)),
            (int)Math.Round(grades.Average(g => (double)g.C4Genre)),
            (int)Math.Round(grades.Average(g => (double)g.C5Organisation)),
            (int)Math.Round(grades.Average(g => (double)g.C6Language)));
    }

    private async Task<IReadOnlyList<WritingAnalyticsLetterTypeRow>> GetLetterTypesViewAsync(string userId, CancellationToken ct)
    {
        return await db.WritingGrades.AsNoTracking()
            .Join(db.WritingSubmissions.AsNoTracking(), g => g.SubmissionId, s => s.Id, (g, s) => new { g, s })
            .Join(db.WritingScenarios.AsNoTracking(), x => x.s.ScenarioId, s => s.Id, (x, scenario) => new { x.g, x.s.UserId, scenario.LetterType })
            .Where(x => x.UserId == userId && x.LetterType != null)
            .GroupBy(x => x.LetterType)
            .Select(g => new WritingAnalyticsLetterTypeRow(g.Key, g.Count(), Math.Round(g.Average(x => (double)x.g.RawTotal), 2)))
            .ToListAsync(ct);
    }

    private async Task<(int AverageSeconds, int PercentWithin40Min, IReadOnlyList<WritingAnalyticsTimeBucket> Distribution)> GetTimeStatsViewAsync(string userId, CancellationToken ct)
    {
        var subs = await db.WritingSubmissions.AsNoTracking()
            .Where(s => s.UserId == userId && s.Status == "graded")
            .OrderByDescending(s => s.SubmittedAt)
            .Take(50)
            .Select(s => s.TimeSpentSeconds)
            .ToListAsync(ct);
        if (subs.Count == 0) return (0, 0, Array.Empty<WritingAnalyticsTimeBucket>());
        var avg = (int)Math.Round(subs.Average());
        var withinPct = (int)Math.Round(100.0 * subs.Count(t => t <= 40 * 60) / subs.Count);
        var distribution = subs
            .GroupBy(t => Bucket(t))
            .Select(g => new WritingAnalyticsTimeBucket(g.Key, g.Count()))
            .OrderBy(b => b.BucketLabel)
            .ToList();
        return (avg, withinPct, distribution);
    }

    private async Task<IReadOnlyDictionary<string, double>> GetSkillMasteryAsync(string userId, CancellationToken ct)
    {
        var completions = await db.WritingLessonCompletionsV2.AsNoTracking()
            .Where(c => c.UserId == userId)
            .Select(c => c.LessonId)
            .ToListAsync(ct);
        if (completions.Count == 0) return new Dictionary<string, double>();
        var lessons = await db.WritingLessonsV2.AsNoTracking()
            .Where(l => completions.Contains(l.Id))
            .GroupBy(l => l.SubSkill)
            .Select(g => new { Skill = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        var total = lessons.Sum(l => l.Count);
        return lessons.ToDictionary(l => l.Skill, l => total == 0 ? 0 : Math.Round(100.0 * l.Count / total, 2));
    }

    private async Task<int> ComputeStreakAsync(string userId, CancellationToken ct)
    {
        var raw = await db.WritingSubmissions.AsNoTracking()
            .Where(s => s.UserId == userId)
            .Select(s => s.SubmittedAt)
            .ToListAsync(ct);
        var dates = raw
            .Select(d => d.UtcDateTime.Date)
            .Distinct()
            .OrderByDescending(d => d)
            .Take(60)
            .ToList();
        if (dates.Count == 0) return 0;
        var streak = 0;
        var cursor = clock.GetUtcNow().UtcDateTime.Date;
        foreach (var date in dates)
        {
            if (date == cursor)
            {
                streak++;
                cursor = cursor.AddDays(-1);
            }
            else if (date < cursor)
            {
                break;
            }
        }
        return streak;
    }

    private async Task<(string? Criterion, string? Label)> GetTopWeaknessAsync(string userId, CancellationToken ct)
    {
        var since = clock.GetUtcNow().AddDays(-30);
        var grades = await db.WritingGrades.AsNoTracking()
            .Join(db.WritingSubmissions.AsNoTracking(), g => g.SubmissionId, s => s.Id, (g, s) => new { g, s })
            .Where(x => x.s.UserId == userId && x.g.GradedAt >= since)
            .Select(x => new { x.g.C1Purpose, x.g.C2Content, x.g.C3Conciseness, x.g.C4Genre, x.g.C5Organisation, x.g.C6Language })
            .ToListAsync(ct);
        if (grades.Count == 0) return (null, null);
        var ratios = new[]
        {
            ("c1", grades.Average(g => g.C1Purpose / 3.0)),
            ("c2", grades.Average(g => g.C2Content / 7.0)),
            ("c3", grades.Average(g => g.C3Conciseness / 7.0)),
            ("c4", grades.Average(g => g.C4Genre / 7.0)),
            ("c5", grades.Average(g => g.C5Organisation / 7.0)),
            ("c6", grades.Average(g => g.C6Language / 7.0)),
        };
        var weakest = ratios.OrderBy(r => r.Item2).First();
        var label = weakest.Item1 switch
        {
            "c1" => "Purpose",
            "c2" => "Content",
            "c3" => "Conciseness & Clarity",
            "c4" => "Genre & Style",
            "c5" => "Organisation & Layout",
            "c6" => "Language",
            _ => weakest.Item1,
        };
        return (weakest.Item1, label);
    }

    private static string Bucket(int seconds) => seconds switch
    {
        < 10 * 60 => "<10m",
        < 20 * 60 => "10-20m",
        < 30 * 60 => "20-30m",
        < 40 * 60 => "30-40m",
        < 50 * 60 => "40-50m",
        _ => "50m+",
    };

    // ─────────────────────────────────────────────────────────────────────────
    // V2 endpoint adapters
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<WritingStatsDashboardResponse> GetDashboardAsync(string userId, CancellationToken ct)
    {
        var view = await GetDashboardViewAsync(userId, ct);
        return new WritingStatsDashboardResponse(
            LatestBand: view.LatestBandLabel,
            LatestRawTotal: view.LatestRawTotal,
            TrendDeltaLastFive: view.TrendDeltaLastFive,
            TargetBand: view.TargetBand,
            DaysToExam: view.DaysToExam,
            StreakDays: view.StreakDays,
            TopWeaknessCriterion: view.TopWeaknessCriterion,
            TopWeaknessLabel: view.TopWeaknessLabel,
            Readiness: null);
    }

    public async Task<WritingStatsBandsResponse> GetBandsAsync(string userId, CancellationToken ct)
    {
        var rows = await GetBandsHistoryAsync(userId, 30, ct);
        var profile = await db.LearnerWritingProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == userId, ct);
        int? targetBand = profile?.TargetBand switch
        {
            "A" => 38,
            "B+" => 34,
            "B" => 30,
            _ => null,
        };
        var points = rows.Select(r => new WritingBandHistoryPointResponse(
            SubmissionId: r.SubmissionId,
            Date: r.Date.ToString("O"),
            RawTotal: r.RawTotal,
            EstimatedBand: r.EstimatedBand,
            LetterType: r.LetterType,
            IsRevision: r.IsRevision)).ToList();
        return new WritingStatsBandsResponse(points, targetBand);
    }

    public async Task<WritingStatsCriteriaResponse> GetCriteriaAsync(string userId, CancellationToken ct)
    {
        var radar = await GetCriteriaRadarAsync(userId, ct);
        var profile = await db.LearnerWritingProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == userId, ct);
        var targetMax = profile?.TargetBand == "A" ? 7.0 : profile?.TargetBand == "B+" ? 6.0 : 5.0;
        return new WritingStatsCriteriaResponse(
            Current: new WritingCriteriaScoresResponse(radar.C1, radar.C2, radar.C3, radar.C4, radar.C5, radar.C6),
            Target: new WritingCriteriaScoresResponse(3, targetMax, targetMax, targetMax, targetMax, targetMax));
    }

    public async Task<WritingStatsLetterTypesResponse> GetLetterTypesAsync(string userId, CancellationToken ct)
    {
        var rows = await GetLetterTypesViewAsync(userId, ct);
        return new WritingStatsLetterTypesResponse(rows.Select(r => new WritingStatsLetterTypeRowResponse(r.LetterType, r.Attempts, r.AverageBand)).ToList());
    }

    public async Task<WritingStatsCanonResponse> GetCanonStatsAsync(string userId, CancellationToken ct)
    {
        var rows = await GetCanonTrackerAsync(userId, 30, ct);
        var ruleText = await db.WritingCanonRules.AsNoTracking()
            .Where(r => rows.Select(x => x.RuleId).Contains(r.Id))
            .ToDictionaryAsync(r => r.Id, r => r.RuleText, ct);
        var top = rows.Select(r => new WritingStatsCanonRuleStatResponse(
            RuleId: r.RuleId,
            RuleText: ruleText.TryGetValue(r.RuleId, out var t) ? t : string.Empty,
            Count: r.Count,
            TrendLast30Days: r.TrendLast30Days)).ToList();
        return new WritingStatsCanonResponse(top);
    }

    public async Task<WritingStatsTimeResponse> GetTimeStatsAsync(string userId, CancellationToken ct)
    {
        var (avg, pct, dist) = await GetTimeStatsViewAsync(userId, ct);
        return new WritingStatsTimeResponse(
            AverageCompletionSeconds: avg,
            PercentCompletedWithin40Min: pct,
            Distribution: dist.Select(b => new WritingStatsTimeBucketResponse(b.BucketLabel, b.Count)).ToList());
    }

    public async Task<WritingStatsSkillsResponse> GetSkillsAsync(string userId, CancellationToken ct)
    {
        var mastery = await GetSkillMasteryAsync(userId, ct);
        return new WritingStatsSkillsResponse(mastery);
    }

    public async Task<WritingStatsCalendarResponse> GetCalendarAsync(string userId, CancellationToken ct)
    {
        var rows = await GetActivityHeatmapAsync(userId, 90, ct);
        return new WritingStatsCalendarResponse(rows.Select(c => new WritingStatsCalendarDayResponse(c.Date.ToString("O"), c.Count)).ToList());
    }

    public Task<WritingStatsExportResponse> ExportStatsAsync(string userId, string format, CancellationToken ct)
    {
        _ = userId;
        // Export job creation lives outside this read-only service in spec §27.20.
        // Until that pipeline lands, we return a stable URL pointer that the
        // worker will populate. The caller treats null/empty as "not ready".
        return Task.FromResult(new WritingStatsExportResponse(string.Empty));
    }
}
