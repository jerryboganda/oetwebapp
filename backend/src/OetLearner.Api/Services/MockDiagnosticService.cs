using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services;

/// <summary>
/// Phase 6 — Mock exam assembly, diagnostic generation, readiness scoring, and progress tracking.
/// </summary>
public class MockDiagnosticService(LearnerDbContext db)
{
    // ── Mock Exam Assembly ──

    /// <summary>
    /// Build a mock exam from eligible content items, balanced across subtests.
    /// </summary>
    public async Task<MockExamTemplate> AssembleMockExamAsync(string? professionId, string language, CancellationToken ct)
    {
        var eligibleItems = await db.ContentItems
            .Where(c => c.IsMockEligible && c.Status == ContentStatus.Published
                        && c.InstructionLanguage == language
                        && c.FreshnessConfidence != "superseded")
            .ToListAsync(ct);

        if (!string.IsNullOrEmpty(professionId))
        {
            eligibleItems = eligibleItems
                .Where(c => c.ProfessionId == professionId || c.ProfessionId == null)
                .ToList();
        }

        var subtests = new[] { "writing", "speaking", "reading", "listening" };
        var sections = new List<MockExamSection>();

        foreach (var subtest in subtests)
        {
            var subtestItems = eligibleItems
                .Where(c => c.SubtestCode == subtest)
                .OrderBy(_ => Random.Shared.Next())
                .Take(subtest is "writing" or "speaking" ? 1 : 3) // 1 task for productive, 3 for receptive
                .Select(c => c.Id)
                .ToList();

            sections.Add(new MockExamSection
            {
                SubtestCode = subtest,
                ContentItemIds = subtestItems,
                TimeLimitMinutes = subtest switch
                {
                    "writing" => 45,
                    "speaking" => 5,
                    "reading" => 15,
                    "listening" => 12,
                    _ => 15
                }
            });
        }

        return new MockExamTemplate
        {
            Id = $"mock-{Guid.NewGuid():N}"[..32],
            Title = $"Mock Exam — {(professionId ?? "General")} ({language.ToUpper()})",
            ProfessionId = professionId,
            Language = language,
            Sections = sections,
            TotalTimeLimitMinutes = sections.Sum(s => s.TimeLimitMinutes),
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    // ── Diagnostic Generation ──

    /// <summary>
    /// Generate a diagnostic assessment from eligible items, sampling all 4 subtests.
    /// </summary>
    public async Task<DiagnosticTemplate> GenerateDiagnosticAsync(string? professionId, CancellationToken ct)
    {
        var eligibleItems = await db.ContentItems
            .Where(c => c.IsDiagnosticEligible && c.Status == ContentStatus.Published
                        && c.FreshnessConfidence != "superseded")
            .ToListAsync(ct);

        if (!string.IsNullOrEmpty(professionId))
        {
            eligibleItems = eligibleItems
                .Where(c => c.ProfessionId == professionId || c.ProfessionId == null)
                .ToList();
        }

        var subtests = new[] { "writing", "speaking", "reading", "listening" };
        var sections = new List<DiagnosticSection>();

        foreach (var subtest in subtests)
        {
            // Pick items across difficulty range for calibration
            var subtestItems = eligibleItems
                .Where(c => c.SubtestCode == subtest)
                .OrderBy(c => c.DifficultyRating)
                .ToList();

            var selected = new List<string>();
            if (subtestItems.Count >= 3)
            {
                // Pick easy, medium, hard for calibration
                selected.Add(subtestItems[0].Id);
                selected.Add(subtestItems[subtestItems.Count / 2].Id);
                selected.Add(subtestItems[^1].Id);
            }
            else
            {
                selected.AddRange(subtestItems.Select(c => c.Id));
            }

            sections.Add(new DiagnosticSection
            {
                SubtestCode = subtest,
                ContentItemIds = selected,
                CalibrationConfidence = subtestItems.Count >= 5 ? "high" : subtestItems.Count >= 2 ? "medium" : "low"
            });
        }

        return new DiagnosticTemplate
        {
            Id = $"diag-{Guid.NewGuid():N}"[..32],
            ProfessionId = professionId,
            Sections = sections,
            EstimatedDurationMinutes = 30,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    // ── Readiness Scoring ──

    /// <summary>
    /// Calculate readiness score based on attempt history, criterion scores, and time investment.
    /// </summary>
    public async Task<ReadinessScore> CalculateReadinessAsync(string userId, CancellationToken ct)
    {
        var attempts = await db.Attempts
            .Where(a => a.UserId == userId && a.State == AttemptState.Completed)
            .OrderByDescending(a => a.StartedAt)
            .Take(100)
            .ToListAsync(ct);

        var attemptIds = attempts.Select(a => a.Id).ToHashSet();
        var evaluations = await db.Evaluations
            .Where(e => attemptIds.Contains(e.AttemptId))
            .ToListAsync(ct);

        var subtests = new[] { "writing", "speaking", "reading", "listening" };
        var subtestScores = new Dictionary<string, SubtestReadiness>();

        foreach (var subtest in subtests)
        {
            var subtestAttempts = attempts.Where(a => a.SubtestCode == subtest).ToList();
            var subtestEvals = evaluations.Where(e => e.SubtestCode == subtest).ToList();

            var attemptCount = subtestAttempts.Count;
            var scores = subtestEvals
                .Select(e => double.TryParse(e.ScoreRange?.Split('-')[0], out var v) ? v : 0)
                .ToList();
            var avgScore = scores.Count > 0 ? scores.Average() : 0;

            var recentTrend = CalculateTrend(scores.Take(10).ToList());
            var totalMinutes = subtestAttempts.Sum(a =>
                a.SubmittedAt.HasValue ? (int)(a.SubmittedAt.Value - a.StartedAt).TotalMinutes : 0);

            // Readiness formula: weighted combination
            var practiceScore = Math.Min(attemptCount / 10.0, 1.0) * 25; // 25 pts max for volume
            var performanceScore = Math.Min(avgScore / 100.0, 1.0) * 50; // 50 pts max for quality
            var trendBonus = recentTrend > 0 ? Math.Min(recentTrend * 10, 15) : 0; // 15 pts max for improving trend
            var timeScore = Math.Min(totalMinutes / 120.0, 1.0) * 10; // 10 pts max for time invested

            subtestScores[subtest] = new SubtestReadiness
            {
                SubtestCode = subtest,
                Score = Math.Round(practiceScore + performanceScore + trendBonus + timeScore, 1),
                AttemptCount = attemptCount,
                AverageScore = Math.Round(avgScore, 1),
                RecentTrend = Math.Round(recentTrend, 2),
                TotalMinutesInvested = totalMinutes,
                Confidence = attemptCount >= 10 ? "high" : attemptCount >= 3 ? "medium" : "low"
            };
        }

        var overallScore = subtestScores.Values.Any()
            ? Math.Round(subtestScores.Values.Average(s => s.Score), 1)
            : 0;

        var weakestSubtest = subtestScores.Values
            .OrderBy(s => s.Score)
            .FirstOrDefault()?.SubtestCode ?? "writing";

        return new ReadinessScore
        {
            UserId = userId,
            OverallScore = overallScore,
            OverallConfidence = subtestScores.Values.All(s => s.Confidence == "high") ? "high"
                : subtestScores.Values.Any(s => s.Confidence == "low") ? "low" : "medium",
            SubtestScores = subtestScores,
            WeakestSubtest = weakestSubtest,
            Recommendation = GenerateRecommendation(subtestScores, weakestSubtest),
            CalculatedAt = DateTimeOffset.UtcNow
        };
    }

    // ── Skill Tag Mapping ──

    /// <summary>
    /// Get content items tagged for specific skills/subskill areas.
    /// </summary>
    public async Task<object> GetContentBySkillTagAsync(string subtestCode, string? skillTag, int page, int pageSize, CancellationToken ct)
    {
        var query = db.ContentItems
            .Where(c => c.SubtestCode == subtestCode && c.Status == ContentStatus.Published);

        if (!string.IsNullOrEmpty(skillTag))
            query = query.Where(c => c.CriteriaFocusJson.Contains(skillTag));

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderBy(c => c.DifficultyRating)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(c => new
            {
                c.Id, c.Title, c.SubtestCode, c.Difficulty, c.DifficultyRating,
                c.ProfessionId, c.ScenarioType, c.IsMockEligible, c.IsDiagnosticEligible,
                c.QualityScore, c.SourceProvenance
            })
            .ToListAsync(ct);

        return new { items, total, page, pageSize };
    }

    /// <summary>
    /// Tag/untag content items for mock and diagnostic eligibility (admin).
    /// </summary>
    public async Task<bool> UpdateEligibilityAsync(string contentId, bool? isMockEligible, bool? isDiagnosticEligible, CancellationToken ct)
    {
        var item = await db.ContentItems.FindAsync([contentId], ct);
        if (item is null) return false;

        if (isMockEligible.HasValue) item.IsMockEligible = isMockEligible.Value;
        if (isDiagnosticEligible.HasValue) item.IsDiagnosticEligible = isDiagnosticEligible.Value;
        await db.SaveChangesAsync(ct);
        return true;
    }

    private static double CalculateTrend(List<double> scores)
    {
        if (scores.Count < 2) return 0;
        var diffs = new List<double>();
        for (var i = 1; i < scores.Count; i++) diffs.Add(scores[i - 1] - scores[i]);
        return diffs.Average();
    }

    private static string GenerateRecommendation(Dictionary<string, SubtestReadiness> scores, string weakest)
    {
        var weak = scores.GetValueOrDefault(weakest);
        if (weak is null) return "Complete more practice tasks to get personalised recommendations.";

        if (weak.AttemptCount < 3) return $"Focus on {weakest} — you need more practice attempts for accurate calibration.";
        if (weak.Score < 30) return $"Prioritise {weakest} practice — this is your biggest opportunity for improvement.";
        if (weak.RecentTrend < 0) return $"Your {weakest} scores are declining — review recent feedback and try strategy guides.";
        return $"Good progress! Continue balanced practice with extra focus on {weakest}.";
    }
}

// ── DTOs ──

public class MockExamTemplate
{
    public string Id { get; set; } = default!;
    public string Title { get; set; } = default!;
    public string? ProfessionId { get; set; }
    public string Language { get; set; } = "en";
    public List<MockExamSection> Sections { get; set; } = [];
    public int TotalTimeLimitMinutes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class MockExamSection
{
    public string SubtestCode { get; set; } = default!;
    public List<string> ContentItemIds { get; set; } = [];
    public int TimeLimitMinutes { get; set; }
}

public class DiagnosticTemplate
{
    public string Id { get; set; } = default!;
    public string? ProfessionId { get; set; }
    public List<DiagnosticSection> Sections { get; set; } = [];
    public int EstimatedDurationMinutes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class DiagnosticSection
{
    public string SubtestCode { get; set; } = default!;
    public List<string> ContentItemIds { get; set; } = [];
    public string CalibrationConfidence { get; set; } = "low";
}

public class ReadinessScore
{
    public string UserId { get; set; } = default!;
    public double OverallScore { get; set; }
    public string OverallConfidence { get; set; } = "low";
    public Dictionary<string, SubtestReadiness> SubtestScores { get; set; } = new();
    public string WeakestSubtest { get; set; } = default!;
    public string Recommendation { get; set; } = default!;
    public DateTimeOffset CalculatedAt { get; set; }
}

public class SubtestReadiness
{
    public string SubtestCode { get; set; } = default!;
    public double Score { get; set; }
    public int AttemptCount { get; set; }
    public double AverageScore { get; set; }
    public double RecentTrend { get; set; }
    public int TotalMinutesInvested { get; set; }
    public string Confidence { get; set; } = "low";
}
