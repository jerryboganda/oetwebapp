using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Listening;

/// <summary>
/// Listening V2 — recomputes the 12-stage learner pathway after every
/// graded submit. Stage codes are stable strings authored in
/// <see cref="PathwayStages"/>; per-stage unlock predicates are pure
/// functions of the user's submitted attempts so the recompute is
/// idempotent and replay-safe.
/// </summary>
public sealed class ListeningPathwayProgressService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> RecomputeLocks = new(StringComparer.Ordinal);

    private readonly LearnerDbContext _db;
    private readonly TimeProvider _clock;

    public ListeningPathwayProgressService(LearnerDbContext db, TimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    /// <summary>The canonical computer-based stages, in unlock order.</summary>
    public static readonly IReadOnlyList<string> PathwayStages = new[]
    {
        "diagnostic",
        "foundation_partA",
        "foundation_partB",
        "foundation_partC",
        "drill_partA",
        "drill_partB",
        "drill_partC",
        "minitest_partA",
        "minitest_partBC",
        "fullpaper_cbt",
        "exam_simulation",
    };

    /// <summary>
    /// Part-practice entry points. These are always unlocked so a learner can
    /// start Part A, B, or C practice in any order without first completing the
    /// listening diagnostic (the diagnostic is optional, not a gate). Downstream
    /// stages (drills, mini-tests, full paper, exam) keep their linear unlock.
    /// </summary>
    private static readonly HashSet<string> EntryStages = new(StringComparer.Ordinal)
    {
        "foundation_partA",
        "foundation_partB",
        "foundation_partC",
    };

    public async Task RecomputeAsync(string userId, CancellationToken ct)
    {
        var gate = RecomputeLocks.GetOrAdd(userId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            await RecomputeCoreAsync(userId, ct);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task RecomputeCoreAsync(string userId, CancellationToken ct)
    {
        // Pull the small set of completed Listening attempts in one round trip.
        var submitted = await _db.ListeningAttempts
            .Where(a => a.UserId == userId && a.Status == ListeningAttemptStatus.Submitted)
            .Select(a => new
            {
                a.Id,
                a.Mode,
                a.PaperId,
                a.ScopeJson,
                a.MaxRawScore,
                a.ScaledScore,
                a.ScoreConversionTableVersionKey,
                a.ScoreConversionPassed,
                a.RequiresAdminReview,
                a.SubmittedAt,
            })
            .ToListAsync(ct);

        var existing = await _db.ListeningPathwayProgress
            .Where(p => p.UserId == userId)
            .ToListAsync(ct);

        var now = _clock.GetUtcNow();
        var consumedAttemptIds = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < PathwayStages.Count; i++)
        {
            var stage = PathwayStages[i];
            var row = existing.FirstOrDefault(x => x.StageCode == stage);
            if (row is null)
            {
                row = new ListeningPathwayProgress
                {
                    Id = Guid.NewGuid().ToString("N"),
                    UserId = userId,
                    StageCode = stage,
                    Status = ListeningPathwayStageStatus.Locked,
                    CreatedAt = now,
                    UpdatedAt = now,
                };
                _db.ListeningPathwayProgress.Add(row);
                existing.Add(row);
            }

            var prev = i == 0 ? null : existing.FirstOrDefault(x => x.StageCode == PathwayStages[i - 1]);
            var unlocked = i == 0
                || EntryStages.Contains(stage)
                || prev?.Status == ListeningPathwayStageStatus.Completed;

            // Manual override always unlocks.
            if (!string.IsNullOrEmpty(row.UnlockOverrideBy)) unlocked = true;

            // Find the qualifying attempt for this stage.
            var qualifying = submitted
                .Where(a => !consumedAttemptIds.Contains(a.Id)
                    && !a.RequiresAdminReview
                    && StageMatches(stage, a.Mode, a.ScopeJson)
                    && (stage == "diagnostic"
                        || (a.MaxRawScore == OetScoring.ListeningReadingRawMax
                            && a.ScaledScore.HasValue
                            && !string.IsNullOrWhiteSpace(a.ScoreConversionTableVersionKey)
                            && a.ScoreConversionPassed.HasValue)))
                .OrderByDescending(a => a.ScaledScore ?? 0)
                .ThenByDescending(a => a.SubmittedAt ?? DateTimeOffset.MinValue)
                .FirstOrDefault();

            var newStatus = (unlocked, qualifying) switch
            {
                (false, _) => ListeningPathwayStageStatus.Locked,
                (true, null) => ListeningPathwayStageStatus.Unlocked,
                (true, _) when IsOwnerPassingStage(stage)
                    && qualifying.ScoreConversionPassed is true
                    => ListeningPathwayStageStatus.Completed,
                (true, _) when IsOwnerPassingStage(stage)
                    => ListeningPathwayStageStatus.InProgress,
                (true, _) when stage == "diagnostic"
                    => ListeningPathwayStageStatus.Completed,
                (true, _) when HasApprovedScore(qualifying.MaxRawScore, qualifying.ScaledScore, qualifying.ScoreConversionTableVersionKey, qualifying.ScoreConversionPassed)
                    && qualifying.ScaledScore is int scaled
                    && scaled >= ScaledThresholdFor(stage)
                    => ListeningPathwayStageStatus.Completed,
                (true, _) => ListeningPathwayStageStatus.InProgress,
            };

            if (row.Status != newStatus)
            {
                row.Status = newStatus;
                row.UpdatedAt = now;
            }

            if (newStatus == ListeningPathwayStageStatus.InProgress && row.StartedAt is null)
            {
                row.StartedAt = now;
                row.UpdatedAt = now;
            }

            if (newStatus == ListeningPathwayStageStatus.Completed)
            {
                row.CompletedAt ??= now;
                row.UpdatedAt = now;
            }
            else if (row.CompletedAt is not null)
            {
                row.CompletedAt = null;
                row.UpdatedAt = now;
            }

            if (newStatus is ListeningPathwayStageStatus.InProgress or ListeningPathwayStageStatus.Completed
                && qualifying is not null)
            {
                consumedAttemptIds.Add(qualifying.Id);
                var approvedScaled = HasApprovedScore(qualifying.MaxRawScore, qualifying.ScaledScore, qualifying.ScoreConversionTableVersionKey, qualifying.ScoreConversionPassed)
                    ? qualifying.ScaledScore
                    : null;
                if (row.AttemptId != qualifying.Id || row.ScaledScore != approvedScaled)
                {
                    row.AttemptId = qualifying.Id;
                    row.ScaledScore = approvedScaled;
                    row.UpdatedAt = now;
                }
            }
            else if (row.AttemptId is not null || row.ScaledScore is not null)
            {
                row.AttemptId = null;
                row.ScaledScore = null;
                row.UpdatedAt = now;
            }
        }

        await _db.SaveChangesAsync(ct);
    }

    /// <summary>Stage → required attempt mode. Authoring-stable mapping.</summary>
    internal static bool StageMatches(string stage, ListeningAttemptMode mode, string? scopeJson = null)
    {
        var modeMatches = stage switch
        {
            "diagnostic" => mode == ListeningAttemptMode.Diagnostic,
            "foundation_partA" or "foundation_partB" or "foundation_partC"
                => mode is ListeningAttemptMode.Learning or ListeningAttemptMode.Drill,
            "drill_partA" or "drill_partB" or "drill_partC"
                => mode is ListeningAttemptMode.Drill or ListeningAttemptMode.Learning,
            "minitest_partA" or "minitest_partBC"
                => mode is ListeningAttemptMode.MiniTest or ListeningAttemptMode.Learning,
            "fullpaper_cbt" => mode == ListeningAttemptMode.Exam,
            "exam_simulation" => mode is ListeningAttemptMode.Home or ListeningAttemptMode.Exam,
            _ => false,
        };
        if (!modeMatches) return false;

        var scopedStage = ListeningAttemptScope.ReadPathwayStage(scopeJson);
        return !scopedStage.HasScope || string.Equals(scopedStage.Stage, stage, StringComparison.Ordinal);
    }

    private static bool IsOwnerPassingStage(string stage)
        => stage is "fullpaper_cbt" or "exam_simulation";

    private static bool HasApprovedScore(int maxRawScore, int? scaledScore, string? scoreConversionTableVersionKey, bool? scoreConversionPassed)
        => maxRawScore == OetScoring.ListeningReadingRawMax
            && scaledScore.HasValue
            && !string.IsNullOrWhiteSpace(scoreConversionTableVersionKey)
            && scoreConversionPassed.HasValue;

    /// <summary>
    /// Practice stages may use their existing progression bar only after an
    /// approved conversion exists. Full-paper and exam stages use the
    /// owner-authored Passed value instead of a numeric pass fallback.
    /// </summary>
    internal static int ScaledThresholdFor(string stage) => stage switch
    {
        "diagnostic" => 0,                  // attempt counts as completion
        _ => 300,                           // foundation/drill/minitest bar
    };
}
