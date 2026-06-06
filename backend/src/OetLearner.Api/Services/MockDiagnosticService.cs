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

        // Phase 1 P1.4 — derive level + module recommendations + 4-week study path
        // from the per-sub-test readiness scores. The mapping uses
        // RemediationCatalog as a single source of truth for module ids and routes,
        // so admin overrides automatically propagate to the diagnostic UI.
        var recommendedLevel = ResolveRecommendedLevel(subtestScores);
        var (recommendedModuleIds, studyPath) = BuildStudyPath(subtestScores, weakestSubtest);

        return new ReadinessScore
        {
            UserId = userId,
            OverallScore = overallScore,
            OverallConfidence = subtestScores.Values.All(s => s.Confidence == "high") ? "high"
                : subtestScores.Values.Any(s => s.Confidence == "low") ? "low" : "medium",
            SubtestScores = subtestScores,
            WeakestSubtest = weakestSubtest,
            Recommendation = GenerateRecommendation(subtestScores, weakestSubtest),
            RecommendedLevel = recommendedLevel,
            RecommendedModuleIds = recommendedModuleIds,
            StudyPath = studyPath,
            CalculatedAt = DateTimeOffset.UtcNow
        };
    }

    /// <summary>
    /// Phase 1 P1.4 — Map an overall readiness score (0–100, derived from raw
    /// criterion scores) to one of four canonical study levels. The
    /// thresholds mirror the scaled-score grade bands used by the OET
    /// statement-of-results adapter so the level shown next to the diagnostic
    /// result is consistent with the badge a learner sees on their report.
    ///
    /// <list type="bullet">
    ///   <item><c>beginner</c> — overall &lt; 30 (Grade E/D territory)</item>
    ///   <item><c>improver</c> — 30–49 (Grade C / weak C+)</item>
    ///   <item><c>intermediate</c> — 50–69 (Grade C+ / approaching B)</item>
    ///   <item><c>advanced</c> — ≥ 70 (Grade B+ readiness)</item>
    /// </list>
    /// </summary>
    private static string ResolveRecommendedLevel(Dictionary<string, SubtestReadiness> subtestScores)
    {
        if (subtestScores.Count == 0) return "beginner";
        var avg = subtestScores.Values.Average(s => s.Score);
        if (avg >= 70) return "advanced";
        if (avg >= 50) return "intermediate";
        if (avg >= 30) return "improver";
        return "beginner";
    }

    /// <summary>
    /// Phase 1 P1.4 — Build a 4-week study path by walking the readiness
    /// scores from weakest to strongest, pulling the first drill of each
    /// weakness's <see cref="RemediationCatalog"/> entry. Each step lands one
    /// week apart; the weakest sub-test always starts in week 1 so the
    /// learner's first action is targeted at the biggest deficit. If the
    /// catalog runs out before week 4, the remaining slots are filled with
    /// the next-best drill from the weakest sub-test.
    /// </summary>
    private static (List<string> ModuleIds, List<StudyPathStep> Steps) BuildStudyPath(
        Dictionary<string, SubtestReadiness> subtestScores,
        string weakestSubtest)
    {
        var orderedSubtests = subtestScores.Values
            .OrderBy(s => s.Score)
            .Select(s => s.SubtestCode)
            .ToList();
        if (orderedSubtests.Count == 0)
        {
            orderedSubtests = new List<string> { weakestSubtest };
        }

        var moduleIds = new List<string>();
        var steps = new List<StudyPathStep>();
        var stepNumber = 1;

        foreach (var subtest in orderedSubtests)
        {
            if (stepNumber > 4) break;
            var drills = RemediationCatalog.Resolve($"low_{subtest}");
            if (drills.Count == 0) continue;
            var drill = drills[0];
            moduleIds.Add(drill.DrillId);
            steps.Add(new StudyPathStep
            {
                StepNumber = stepNumber,
                Title = $"Week {stepNumber}: {drill.Label}",
                Description = drill.Description,
                RouteHref = drill.RouteHref,
                SubtestCode = subtest,
                DrillId = drill.DrillId
            });
            stepNumber++;
        }

        // Backfill missing weeks with subsequent drills from the weakest sub-test
        // so the learner always sees a full 4-step plan even when only one
        // weakness was detected.
        if (stepNumber <= 4)
        {
            var weakestDrills = RemediationCatalog.Resolve($"low_{weakestSubtest}");
            var index = 1;
            while (stepNumber <= 4 && index < weakestDrills.Count)
            {
                var drill = weakestDrills[index];
                if (!moduleIds.Contains(drill.DrillId))
                {
                    moduleIds.Add(drill.DrillId);
                    steps.Add(new StudyPathStep
                    {
                        StepNumber = stepNumber,
                        Title = $"Week {stepNumber}: {drill.Label}",
                        Description = drill.Description,
                        RouteHref = drill.RouteHref,
                        SubtestCode = weakestSubtest,
                        DrillId = drill.DrillId
                    });
                    stepNumber++;
                }
                index++;
            }
        }

        return (moduleIds, steps);
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
                c.Id,
                c.Title,
                c.SubtestCode,
                c.Difficulty,
                c.DifficultyRating,
                c.ProfessionId,
                c.ScenarioType,
                c.IsMockEligible,
                c.IsDiagnosticEligible,
                c.QualityScore,
                c.SourceProvenance
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

    /// <summary>
    /// Phase 1 P1.4 — Coarse study level derived from the overall readiness
    /// score. One of <c>beginner</c>, <c>improver</c>, <c>intermediate</c>,
    /// <c>advanced</c>. Front-end surfaces this as a badge above the study
    /// path so the learner instantly knows where they sit.
    /// </summary>
    public string RecommendedLevel { get; set; } = "beginner";

    /// <summary>
    /// Phase 1 P1.4 — Ordered drill IDs (from <see cref="RemediationCatalog"/>)
    /// that the learner should start with. Mirrors the drills referenced by
    /// <see cref="StudyPath"/> so the front-end can deep-link without
    /// re-resolving the catalog.
    /// </summary>
    public List<string> RecommendedModuleIds { get; set; } = new();

    /// <summary>
    /// Phase 1 P1.4 — Ordered 4-week study path (one step per week). Renders
    /// as a numbered list on the diagnostic results page.
    /// </summary>
    public List<StudyPathStep> StudyPath { get; set; } = new();

    public DateTimeOffset CalculatedAt { get; set; }
}

/// <summary>
/// Phase 1 P1.4 — One week of the personalised diagnostic study path. The
/// front-end renders these as a numbered list with a deep-link to the drill
/// surface.
/// </summary>
public class StudyPathStep
{
    public int StepNumber { get; set; }
    public string Title { get; set; } = default!;
    public string Description { get; set; } = default!;
    public string RouteHref { get; set; } = default!;
    public string? SubtestCode { get; set; }
    public string? DrillId { get; set; }
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
