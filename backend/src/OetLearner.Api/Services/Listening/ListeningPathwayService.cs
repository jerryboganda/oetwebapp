using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Listening;

// ═════════════════════════════════════════════════════════════════════════════
// Listening Pathway Service
//
// Mirrors the just-shipped Reading pathway (`ReadingPathwayService`) for
// Listening. Joins the learner-facing Listening signals already in the DB
// into a single readiness "stage" + one structured next-action that the FE
// can route straight to an existing launcher.
//
// Listening supports both the legacy generic `Attempt` path for demo/legacy
// tasks and the relational `ListeningAttempt` path for authored papers. The
// pathway merges both so progress remains continuous while old content is
// migrated.
//
//   • Completed (`AttemptState.Completed`) Listening attempts.
//   • Best scaled score across those attempts, read from
//     `Evaluation.CriterionScoresJson[0].scaledScore` (matches
//     `ListeningLearnerService.ResolveScoreFromEvaluation`).
//   • MockAttempts where SubtestCode = "listening" or MockType = "full".
//
// Stages (first match wins):
//   "not_started" — 0 completed Listening attempts.
//   "diagnostic"  — exactly 1 completed attempt and no scaled score yet.
//   "drilling"    — best scaled < 300.
//   "mini_tests"  — best scaled in [300, 350).
//   "mock_ready"  — best scaled ≥ 350 + 0 listening mocks submitted.
//   "exam_ready"  — ≥ 1 listening (or full) mock submitted with scaled ≥ 350.
//
// When Phase 2 lands (relational entities + skill-tagged questions + error
// bank), the drilling branch can switch to a skill-targeted drill code the
// way Reading does.
// ═════════════════════════════════════════════════════════════════════════════

public interface IListeningPathwayService
{
    Task<ListeningPathwaySnapshot> GetPathwayAsync(string userId, CancellationToken ct);
}

public sealed record ListeningPathwaySnapshot(
    string Stage,                       // "not_started" | "diagnostic" | "drilling" | "mini_tests" | "mock_ready" | "exam_ready"
    string Headline,                    // human-readable short label
    int? BestScaledScore,
    int SubmittedAttempts,
    int SubmittedListeningMockAttempts,
    ListeningPathwayAction NextAction,
    IReadOnlyList<ListeningPathwayMilestone> Milestones);

public sealed record ListeningPathwayAction(
    string Kind,                        // "start_diagnostic" | "start_drill" | "start_mini_test" | "start_mock" | "review_results" | "book_exam"
    string Label,
    string? DrillId,                    // when Kind = start_drill (today wired to BuildDrill drill ids)
    string? PaperId,                    // recommended paper to start against
    string? Route);

public sealed record ListeningPathwayMilestone(
    string Code,
    string Label,
    bool Achieved,
    int? Progress,
    int? Target);

public sealed class ListeningPathwayService(LearnerDbContext db) : IListeningPathwayService
{
    private const string Subtest = "listening";

    public async Task<ListeningPathwaySnapshot> GetPathwayAsync(string userId, CancellationToken ct)
    {
        // ── Listening attempts (any mode) that have been graded ──────────
        var completedAttempts = await db.Attempts.AsNoTracking()
            .Where(a => a.UserId == userId
                && a.SubtestCode == Subtest
                && a.State == AttemptState.Completed)
            .OrderByDescending(a => a.SubmittedAt ?? a.CompletedAt)
            .Take(50)
            .Select(a => new { a.Id, a.SubmittedAt })
            .ToListAsync(ct);

        var relationalAttempts = await db.ListeningAttempts.AsNoTracking()
            .Where(a => a.UserId == userId && a.Status == ListeningAttemptStatus.Submitted)
            .OrderByDescending(a => a.SubmittedAt)
            .Take(50)
            .Select(a => new { a.Id, a.SubmittedAt, a.ScaledScore })
            .ToListAsync(ct);

        // ── Best scaled across those attempts via Evaluation.CriterionScoresJson ──
        int? bestScaled = null;
        if (completedAttempts.Count > 0)
        {
            var attemptIds = completedAttempts.Select(a => a.Id).ToList();
            var evaluations = await db.Evaluations.AsNoTracking()
                .Where(e => attemptIds.Contains(e.AttemptId))
                .Select(e => e.CriterionScoresJson)
                .ToListAsync(ct);

            foreach (var json in evaluations)
            {
                var scaled = TryReadScaled(json);
                if (scaled.HasValue && (bestScaled is null || scaled.Value > bestScaled.Value))
                    bestScaled = scaled.Value;
            }
        }

        foreach (var relationalAttempt in relationalAttempts)
        {
            if (relationalAttempt.ScaledScore is int scaled && (bestScaled is null || scaled > bestScaled.Value))
                bestScaled = scaled;
        }

        var submittedAttemptCount = completedAttempts.Count + relationalAttempts.Count;

        // ── Listening (or full) mock attempts ─────────────────────────────
        var listeningMockCount = await db.MockAttempts.AsNoTracking()
            .CountAsync(m => m.UserId == userId
                && m.State == AttemptState.Submitted
                && (m.SubtestCode == Subtest || m.MockType == "full"), ct);

        // ── Anchor paper (most recent published Listening paper) ─────────
        var anchorPaperId = await db.ContentPapers.AsNoTracking()
            .Where(p => p.SubtestCode == Subtest && p.Status == ContentStatus.Published)
            .OrderByDescending(p => p.UpdatedAt)
            .Select(p => p.Id)
            .FirstOrDefaultAsync(ct);

        // ── Stage decision (first match wins) ─────────────────────────────
        string stage;
        ListeningPathwayAction nextAction;

        if (submittedAttemptCount == 0)
        {
            stage = "not_started";
            nextAction = new ListeningPathwayAction(
                Kind: "start_diagnostic",
                Label: "Take a Listening diagnostic",
                DrillId: null,
                PaperId: anchorPaperId,
                Route: "/diagnostic/listening");
        }
        else if (submittedAttemptCount == 1 && bestScaled is null)
        {
            stage = "diagnostic";
            nextAction = new ListeningPathwayAction(
                Kind: "start_diagnostic",
                Label: "Finish your Listening diagnostic",
                DrillId: null,
                PaperId: anchorPaperId,
                Route: "/diagnostic/listening");
        }
        else if (bestScaled is int bs && bs < 300)
        {
            stage = "drilling";
            // No skill-tagged error bank yet — point the learner at the
            // existing post-attempt drills surface, which BuildDrill already
            // populates from latest error clusters.
            nextAction = new ListeningPathwayAction(
                Kind: "start_drill",
                Label: "Drill detail capture and distractors",
                DrillId: "distractor_confusion",
                PaperId: anchorPaperId,
                Route: "/listening");
        }
        else if (bestScaled is int bs2 && bs2 < 350)
        {
            stage = "mini_tests";
            nextAction = new ListeningPathwayAction(
                Kind: "start_mini_test",
                Label: "Run a mixed Listening practice",
                DrillId: null,
                PaperId: anchorPaperId,
                Route: "/listening");
        }
        else if (listeningMockCount < 1)
        {
            stage = "mock_ready";
            nextAction = new ListeningPathwayAction(
                Kind: "start_mock",
                Label: "Take a full Listening mock",
                DrillId: null,
                PaperId: anchorPaperId,
                Route: "/mocks?subtest=listening");
        }
        else
        {
            stage = "exam_ready";
            nextAction = new ListeningPathwayAction(
                Kind: "book_exam",
                Label: "You're ready — book your OET sitting",
                DrillId: null,
                PaperId: null,
                Route: "/exam-booking");
        }

        var headline = stage switch
        {
            "not_started" => "Start with a diagnostic",
            "diagnostic" => "Finish your diagnostic",
            "drilling" => "Drill weak skills",
            "mini_tests" => "Mini-tests to lift your score",
            "mock_ready" => "Take a full mock",
            "exam_ready" => "Exam-ready",
            _ => "Keep practising",
        };

        var milestones = new List<ListeningPathwayMilestone>
        {
            new("first_attempt", "First Listening attempt",
                submittedAttemptCount >= 1, submittedAttemptCount, 1),
            new("practice_streak_5", "Complete 5 Listening attempts",
                submittedAttemptCount >= 5, Math.Min(submittedAttemptCount, 5), 5),
            new("scaled_300", "Reach 300 scaled",
                bestScaled is int s1 && s1 >= 300, bestScaled, 300),
            new("scaled_350", "Reach 350 scaled (Grade B)",
                bestScaled is int s2 && s2 >= 350, bestScaled, 350),
            new("first_mock_pass", "Pass a Listening mock",
                listeningMockCount >= 1, listeningMockCount, 1),
        };

        return new ListeningPathwaySnapshot(
            Stage: stage,
            Headline: headline,
            BestScaledScore: bestScaled,
            SubmittedAttempts: submittedAttemptCount,
            SubmittedListeningMockAttempts: listeningMockCount,
            NextAction: nextAction,
            Milestones: milestones);
    }

    /// <summary>
    /// Best-effort scaled-score read from Evaluation.CriterionScoresJson —
    /// matches the parsing contract used by ListeningLearnerService.
    /// </summary>
    private static int? TryReadScaled(string? criterionScoresJson)
    {
        if (string.IsNullOrWhiteSpace(criterionScoresJson)) return null;
        try
        {
            using var doc = JsonDocument.Parse(criterionScoresJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;
            if (doc.RootElement.GetArrayLength() == 0) return null;
            var first = doc.RootElement[0];
            if (first.ValueKind != JsonValueKind.Object) return null;
            if (!first.TryGetProperty("scaledScore", out var scaled)) return null;
            return scaled.ValueKind switch
            {
                JsonValueKind.Number when scaled.TryGetInt32(out var n) => n,
                JsonValueKind.String when int.TryParse(scaled.GetString(), out var n) => n,
                _ => null,
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
