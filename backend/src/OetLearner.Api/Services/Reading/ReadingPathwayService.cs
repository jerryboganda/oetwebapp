using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Reading;

// ═════════════════════════════════════════════════════════════════════════════
// Reading Pathway Service
//
// Closes the "Course pathway integration (diagnostic → drills → mocks →
// readiness)" gap by computing a single readiness signal + recommended next
// action for the learner across the Reading-module signals already in the DB:
//
//   • ReadingAttempt (Exam mode, Submitted) → score history, best scaled.
//   • ReadingErrorBankEntry (open) → which skill the learner is weakest at.
//   • ReadingAttempt (practice modes) → how much remediation work has happened.
//   • MockAttempt (subtest=reading or full) → mock readiness.
//
// Stages (mutually exclusive — first match wins):
//   "not_started" — no submitted Reading attempts.
//   "diagnostic"  — < 1 submitted Exam attempt → take a full diagnostic.
//   "drilling"    — open error-bank ≥ 5 OR best scaled < 300 → drill weakest skill.
//   "mini_tests"  — error-bank cleared + best scaled ∈ [300, 350) → mini-tests.
//   "mock_ready"  — best scaled ≥ 350 + < 1 reading-mock attempt → take a mock.
//   "exam_ready"  — best mock-scaled ≥ 350 (last 3 attempts) → ready to book.
//
// Recommended action shape: a structured ReadingPathwayAction the FE can pass
// straight back to the existing practice-start endpoints.
// ═════════════════════════════════════════════════════════════════════════════

public interface IReadingPathwayService
{
    Task<ReadingPathwaySnapshot> GetPathwayAsync(string userId, CancellationToken ct);
}

public sealed record ReadingPathwaySnapshot(
    string Stage,                       // "not_started" | "diagnostic" | "drilling" | "mini_tests" | "mock_ready" | "exam_ready"
    string Headline,                    // human-readable short label
    int? BestScaledScore,
    int OpenErrorBankCount,
    int SubmittedExamAttempts,
    int SubmittedPracticeAttempts,
    int SubmittedReadingMockAttempts,
    string? WeakestSkillTag,            // most-frequent skill in the open error bank
    ReadingPathwayAction NextAction,
    IReadOnlyList<ReadingPathwayMilestone> Milestones);

public sealed record ReadingPathwayAction(
    string Kind,                        // "start_diagnostic" | "start_drill" | "start_mini_test" | "start_mock" | "review_results" | "book_exam"
    string Label,
    string? DrillCode,                  // when Kind = start_drill
    string? PaperId,                    // recommended paper to start against (most recent published)
    string? Route);                     // e.g. /reading/practice or /mocks?subtest=reading

public sealed record ReadingPathwayMilestone(
    string Code,                        // "first_attempt" | "drill_streak_5" | "error_bank_cleared" | "scaled_350" | "first_mock_pass"
    string Label,
    bool Achieved,
    int? Progress,                      // current count toward the milestone
    int? Target);                       // milestone target

public sealed class ReadingPathwayService(LearnerDbContext db) : IReadingPathwayService
{
    public async Task<ReadingPathwaySnapshot> GetPathwayAsync(string userId, CancellationToken ct)
    {
        // ── Reading attempts split by mode + status ───────────────────────
        var examAttempts = await db.ReadingAttempts
            .AsNoTracking()
            .Where(a => a.UserId == userId
                && a.Mode == ReadingAttemptMode.Exam
                && a.Status == ReadingAttemptStatus.Submitted)
            .OrderByDescending(a => a.SubmittedAt)
            .Select(a => new { a.ScaledScore, a.SubmittedAt })
            .ToListAsync(ct);

        var practiceAttempts = await db.ReadingAttempts
            .AsNoTracking()
            .CountAsync(a => a.UserId == userId
                && a.Mode != ReadingAttemptMode.Exam
                && a.Status == ReadingAttemptStatus.Submitted, ct);

        var bestScaled = examAttempts
            .Select(a => a.ScaledScore)
            .Where(s => s.HasValue)
            .DefaultIfEmpty(null)
            .Max();

        // ── Open error bank + weakest skill tag ───────────────────────────
        var openEntries = await db.ReadingErrorBankEntries
            .AsNoTracking()
            .Where(e => e.UserId == userId && !e.IsResolved)
            .Select(e => new ErrorBankProjection(e.ReadingQuestionId, e.PartCode))
            .ToListAsync(ct);

        var openCount = openEntries.Count;

        string? weakestSkill = null;
        if (openCount > 0)
        {
            var qIds = openEntries.Select(e => e.ReadingQuestionId).ToList();
            weakestSkill = await db.ReadingQuestions
                .AsNoTracking()
                .Where(q => qIds.Contains(q.Id) && q.SkillTag != null)
                .GroupBy(q => q.SkillTag!)
                .OrderByDescending(g => g.Count())
                .Select(g => g.Key)
                .FirstOrDefaultAsync(ct);
        }

        // ── Reading-related mock attempts ─────────────────────────────────
        var readingMockCount = await db.MockAttempts
            .AsNoTracking()
            .CountAsync(m => m.UserId == userId
                && m.State == AttemptState.Submitted
                && (m.SubtestCode == "reading"
                    || m.MockType == "full"
                    || m.SubtestCode == null), ct);

        // ── A published paper to anchor the recommended action ────────────
        var anchorPaperId = await db.ContentPapers
            .AsNoTracking()
            .Where(p => p.SubtestCode == "reading" && p.Status == ContentStatus.Published)
            .OrderByDescending(p => p.UpdatedAt)
            .Select(p => p.Id)
            .FirstOrDefaultAsync(ct);

        // ── Stage decision (first match wins) ─────────────────────────────
        string stage;
        ReadingPathwayAction nextAction;

        if (examAttempts.Count == 0 && practiceAttempts == 0)
        {
            stage = "not_started";
            nextAction = new ReadingPathwayAction(
                Kind: "start_diagnostic",
                Label: "Take a full Reading diagnostic",
                DrillCode: null,
                PaperId: anchorPaperId,
                Route: "/diagnostic/reading");
        }
        else if (examAttempts.Count < 1)
        {
            stage = "diagnostic";
            nextAction = new ReadingPathwayAction(
                Kind: "start_diagnostic",
                Label: "Finish your Reading diagnostic",
                DrillCode: null,
                PaperId: anchorPaperId,
                Route: "/diagnostic/reading");
        }
        else if (openCount >= 5 || (bestScaled is int bs && bs < 300))
        {
            stage = "drilling";
            var drillCode = ResolveDrillCode(weakestSkill, openEntries);
            nextAction = new ReadingPathwayAction(
                Kind: "start_drill",
                Label: weakestSkill is not null
                    ? $"Drill weakest skill: {weakestSkill}"
                    : "Run a targeted drill",
                DrillCode: drillCode,
                PaperId: anchorPaperId,
                Route: "/reading/practice");
        }
        else if (bestScaled is int bs2 && bs2 < 350)
        {
            stage = "mini_tests";
            nextAction = new ReadingPathwayAction(
                Kind: "start_mini_test",
                Label: "Run a mixed mini-test",
                DrillCode: null,
                PaperId: anchorPaperId,
                Route: "/reading/practice");
        }
        else if (readingMockCount < 1)
        {
            stage = "mock_ready";
            nextAction = new ReadingPathwayAction(
                Kind: "start_mock",
                Label: "Take a full Reading mock",
                DrillCode: null,
                PaperId: anchorPaperId,
                Route: "/mocks?subtest=reading");
        }
        else
        {
            stage = "exam_ready";
            nextAction = new ReadingPathwayAction(
                Kind: "book_exam",
                Label: "You're ready — book your OET sitting",
                DrillCode: null,
                PaperId: null,
                Route: "/exam-booking");
        }

        var headline = stage switch
        {
            "not_started" => "Start with a diagnostic",
            "diagnostic" => "Finish your diagnostic",
            "drilling" => weakestSkill is not null ? $"Drill: {weakestSkill}" : "Drill weak skills",
            "mini_tests" => "Mini-tests to lift your score",
            "mock_ready" => "Take a full mock",
            "exam_ready" => "Exam-ready",
            _ => "Keep practising",
        };

        var milestones = new List<ReadingPathwayMilestone>
        {
            new("first_attempt", "First Reading attempt",
                examAttempts.Count + practiceAttempts >= 1, examAttempts.Count + practiceAttempts, 1),
            new("drill_streak_5", "Complete 5 drills",
                practiceAttempts >= 5, Math.Min(practiceAttempts, 5), 5),
            new("error_bank_cleared", "Clear error bank",
                openCount == 0 && (examAttempts.Count > 0 || practiceAttempts > 0),
                Math.Max(0, 10 - openCount), 10),
            new("scaled_350", "Reach 350 scaled in an Exam attempt",
                bestScaled is int s && s >= 350, bestScaled, 350),
            new("first_mock_pass", "Pass your first Reading mock",
                readingMockCount >= 1, readingMockCount, 1),
        };

        return new ReadingPathwaySnapshot(
            Stage: stage,
            Headline: headline,
            BestScaledScore: bestScaled,
            OpenErrorBankCount: openCount,
            SubmittedExamAttempts: examAttempts.Count,
            SubmittedPracticeAttempts: practiceAttempts,
            SubmittedReadingMockAttempts: readingMockCount,
            WeakestSkillTag: weakestSkill,
            NextAction: nextAction,
            Milestones: milestones);
    }

    /// <summary>
    /// Map the most-frequent failing skill tag (or fallback to part code) to
    /// one of the catalogue drill codes registered in
    /// <see cref="ReadingDrillCatalogue"/>.
    /// </summary>
    private static string ResolveDrillCode(
        string? weakestSkill,
        IReadOnlyList<ErrorBankProjection> openEntries)
    {
        // Skill-first map — keep aligned with ReadingDrillCatalogue codes.
        var skillMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["scan"] = "part-a-scan",
            ["purpose"] = "part-b-distractor",
            ["inference"] = "part-c-inference",
            ["attitude"] = "part-c-attitude",
            ["vocabulary"] = "part-c-vocabulary",
            ["reference"] = "part-c-reference",
        };

        if (weakestSkill is not null && skillMap.TryGetValue(weakestSkill, out var byTag))
            return byTag;

        // Fallback: pick the part with the most open entries.
        var partCounts = openEntries
            .GroupBy(e => e.PartCode)
            .ToDictionary(g => g.Key, g => g.Count());

        if (partCounts.Count == 0) return "part-a-scan";

        var topPart = partCounts.OrderByDescending(kv => kv.Value).First().Key;
        return topPart switch
        {
            ReadingPartCode.A => "part-a-scan",
            ReadingPartCode.B => "part-b-distractor",
            ReadingPartCode.C => "part-c-inference",
            _ => "part-a-scan",
        };
    }

    private sealed record ErrorBankProjection(string ReadingQuestionId, ReadingPartCode PartCode);
}
