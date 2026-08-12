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
//   • Best scaled score across those attempts, read only from the latest
//     evaluation/attempt carrying an owner conversion-table version.
//   • MockAttempts where SubtestCode = "listening" or MockType = "full".
//
// Stages (first match wins):
//   "not_started" — 0 completed Listening attempts.
//   "diagnostic"  — exactly 1 completed attempt and no scaled score yet.
//   "drilling"    — approved converted score < 300.
//   "mini_tests"  — no owner-authored pass result yet.
//   "mock_ready"  — owner-authored pass + 0 listening mocks submitted.
//   "exam_ready"  — owner-authored pass + ≥ 1 listening (or full) mock submitted.
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
            .Select(a => new { a.Id, a.SubmittedAt, a.MaxRawScore, a.RequiresAdminReview })
            .ToListAsync(ct);

        var relationalAttempts = await db.ListeningAttempts.AsNoTracking()
            .Where(a => a.UserId == userId && a.Status == ListeningAttemptStatus.Submitted)
            .OrderByDescending(a => a.SubmittedAt)
            .Take(50)
            .Select(a => new
            {
                a.Id,
                a.SubmittedAt,
                a.MaxRawScore,
                a.ScaledScore,
                a.ScoreConversionTableVersionKey,
                a.ScoreConversionPassed,
                a.RequiresAdminReview,
            })
            .ToListAsync(ct);

        // ── Best scaled across those attempts via Evaluation.CriterionScoresJson ──
        int? bestScaled = null;
        if (completedAttempts.Count > 0)
        {
            var attemptIds = completedAttempts.Select(a => a.Id).ToList();
            var evaluations = await db.Evaluations.AsNoTracking()
                .Where(e => attemptIds.Contains(e.AttemptId))
                .Select(e => new
                {
                    e.AttemptId,
                    e.GeneratedAt,
                    e.CriterionScoresJson,
                    e.MaxRawScore,
                    e.ScoreConversionTableVersionKey,
                    e.ScoreConversionPassed,
                })
                .ToListAsync(ct);

            foreach (var evaluation in evaluations
                .GroupBy(e => e.AttemptId, StringComparer.Ordinal)
                .Select(group => group.OrderByDescending(e => e.GeneratedAt).First()))
            {
                if (completedAttempts.FirstOrDefault(a => a.Id == evaluation.AttemptId)?.RequiresAdminReview == true)
                    continue;
                if (evaluation.MaxRawScore != OetScoring.ListeningReadingRawMax
                    || string.IsNullOrWhiteSpace(evaluation.ScoreConversionTableVersionKey)
                    || !evaluation.ScoreConversionPassed.HasValue) continue;
                var scaled = TryReadScaled(evaluation.CriterionScoresJson);
                if (scaled.HasValue && (bestScaled is null || scaled.Value > bestScaled.Value))
                    bestScaled = scaled.Value;
            }
        }

        foreach (var relationalAttempt in relationalAttempts)
        {
            if (!relationalAttempt.RequiresAdminReview
                && relationalAttempt.MaxRawScore == OetScoring.ListeningReadingRawMax
                && !string.IsNullOrWhiteSpace(relationalAttempt.ScoreConversionTableVersionKey)
                && relationalAttempt.ScoreConversionPassed.HasValue
                && relationalAttempt.ScaledScore is int scaled
                && (bestScaled is null || scaled > bestScaled.Value))
                bestScaled = scaled;
        }

        var hasOwnerPassingScore = await HasOwnerPassingListeningScoreAsync(userId, ct);

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
            stage = "foundation";
            nextAction = new ListeningPathwayAction(
                Kind: "start_practice",
                Label: "Start your Listening practice",
                DrillId: null,
                PaperId: anchorPaperId,
                Route: "/listening");
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
        else if (!hasOwnerPassingScore)
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
            new("scaled_300", "Reach 300 on an approved converted score",
                bestScaled is int s1 && s1 >= 300, bestScaled, 300),
            new("scaled_350", "Receive an owner-approved Listening pass",
                hasOwnerPassingScore, hasOwnerPassingScore ? 1 : 0, 1),
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

    private async Task<bool> HasOwnerPassingListeningScoreAsync(string userId, CancellationToken ct)
    {
        var legacyAttemptIds = await db.Attempts.AsNoTracking()
            .Where(a => a.UserId == userId
                && a.SubtestCode == Subtest
                && a.State == AttemptState.Completed
                && !a.RequiresAdminReview)
            .Select(a => a.Id)
            .ToListAsync(ct);
        if (legacyAttemptIds.Count > 0)
        {
            var legacyEvaluations = await db.Evaluations.AsNoTracking()
                .Where(e => legacyAttemptIds.Contains(e.AttemptId))
                .Select(e => new { e.AttemptId, e.GeneratedAt, e.MaxRawScore, e.ScoreConversionTableVersionKey, e.ScoreConversionPassed, e.ScaledScore })
                .ToListAsync(ct);
            if (legacyEvaluations
                .GroupBy(e => e.AttemptId, StringComparer.Ordinal)
                .Select(group => group.OrderByDescending(e => e.GeneratedAt).First())
                .Any(e => e.MaxRawScore == OetScoring.ListeningReadingRawMax
                    && !string.IsNullOrWhiteSpace(e.ScoreConversionTableVersionKey)
                    && e.ScaledScore.HasValue
                    && e.ScoreConversionPassed == true))
            {
                return true;
            }
        }

        return await db.ListeningAttempts.AsNoTracking().AnyAsync(a => a.UserId == userId
            && a.Status == ListeningAttemptStatus.Submitted
            && !a.RequiresAdminReview
            && a.MaxRawScore == OetScoring.ListeningReadingRawMax
            && !string.IsNullOrWhiteSpace(a.ScoreConversionTableVersionKey)
            && a.ScaledScore.HasValue
            && a.ScoreConversionPassed == true, ct);
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
