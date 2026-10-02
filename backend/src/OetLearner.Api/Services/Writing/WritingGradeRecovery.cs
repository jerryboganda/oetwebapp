using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Writing;

/// <summary>
/// Candidate-safe reasons a grading run did not finish (stored on
/// <see cref="WritingSubmission.FailureCode"/>, mirrored in lib/writing/types.ts). Never a provider name.
/// </summary>
public static class WritingGradeFailureCodes
{
    /// <summary>Every route was busy or failed; the letter's provider result (if any) is kept.</summary>
    public const string GradingDelayed = "grading_delayed";
    /// <summary>Plan or credit refusal: Retry works after a top-up.</summary>
    public const string CreditsInsufficient = "credits_insufficient";
    /// <summary>A platform gate is closed (budget, feature policy): the server keeps retrying.</summary>
    public const string ServicePaused = "service_paused";
    public const string TaskNotReady = "task_not_ready";
    public const string ManualReview = "manual_review";
    public const string LetterInvalid = "letter_invalid";
}

/// <summary>
/// Grading recovery (WAI-03): failure classification, the stale-claim reclaim and due-row sweep of
/// the batch cron, and the shutdown requeue. Static over the DbContext so the
/// <see cref="IWritingSubmissionEvaluationPipeline"/> stubs never need new members.
/// </summary>
public static class WritingGradeRecovery
{
    /// <summary>Rows graded per cron tick (graders are serial behind one Max lane).</summary>
    public const int SweepBatchSize = 5;

    /// <summary>Prefix of every claim this process takes; the shutdown requeue matches it.</summary>
    public static string ProcessOwnerPrefix { get; } = $"{Environment.MachineName}:{Environment.ProcessId}:";

    public sealed record Failure(string Code, bool Retryable, bool AutoRetry);

    /// <summary>Maps what <c>EvaluateAsync</c> threw to the candidate-safe outcome.</summary>
    public static Failure Classify(Exception ex) => (ex as ApiException)?.ErrorCode switch
    {
        "ai_credits_insufficient" or "no_ai_package_credits" or "ai_package_expired"
            => new(WritingGradeFailureCodes.CreditsInsufficient, Retryable: true, AutoRetry: false),
        "ai_platform_budget_exhausted" or "writing_grading_paused" or "writing_assessment_preflight_unavailable"
            => new(WritingGradeFailureCodes.ServicePaused, Retryable: true, AutoRetry: true),
        "writing_assessment_missing_input" or "writing_assessment_release_blocked" or "writing_assessment_profession_unsupported"
            => new(WritingGradeFailureCodes.TaskNotReady, Retryable: false, AutoRetry: false),
        "writing_assessment_requires_review" or "writing_submission_flagged"
            => new(WritingGradeFailureCodes.ManualReview, Retryable: false, AutoRetry: false),
        "writing_submission_too_long"
            => new(WritingGradeFailureCodes.LetterInvalid, Retryable: false, AutoRetry: false),
        _ => new(WritingGradeFailureCodes.GradingDelayed, Retryable: true, AutoRetry: true),
    };

    /// <summary>A queued row nobody picked up within the lease (the worker is down): Retry may restart it.</summary>
    public static bool IsStaleQueued(WritingSubmission s, DateTimeOffset now)
        => IsStaleQueued(s.Status, s.NextAutoRetryAt, s.SubmittedAt, now);

    /// <summary>Projection-friendly form: lists read a few columns, not the entity.</summary>
    public static bool IsStaleQueued(string status, DateTimeOffset? nextAutoRetryAt, DateTimeOffset submittedAt, DateTimeOffset now)
        => status == WritingSubmissionStatuses.Queued
           && (nextAutoRetryAt ?? submittedAt) <= now - WritingGradeTimings.StaleClaimLease;

    /// <summary>A grading row whose grader died or was redeployed.</summary>
    public static bool IsStaleGrading(WritingSubmission s, DateTimeOffset now)
        => IsStaleGrading(s.Status, s.ClaimedAt, now);

    /// <summary>Projection-friendly form: lists read a few columns, not the entity.</summary>
    public static bool IsStaleGrading(string status, DateTimeOffset? claimedAt, DateTimeOffset now)
        => status == WritingSubmissionStatuses.Grading
           && claimedAt is { } claimed
           && claimed <= now - WritingGradeTimings.StaleClaimLease;

    /// <summary>
    /// grading → queued (due now) for every claim older than the lease. Clearing the owner fences the
    /// lost grader: its late failure write no longer matches.
    /// </summary>
    public static async Task<int> ReclaimStaleGradingAsync(LearnerDbContext db, DateTimeOffset now, CancellationToken ct)
    {
        var cutoff = now - WritingGradeTimings.StaleClaimLease;
        if (!db.Database.IsInMemory())
        {
            return await db.WritingSubmissions
                .Where(s => s.Status == WritingSubmissionStatuses.Grading && s.ClaimedAt != null && s.ClaimedAt <= cutoff)
                .ExecuteUpdateAsync(set => set
                    .SetProperty(s => s.Status, WritingSubmissionStatuses.Queued)
                    .SetProperty(s => s.ClaimOwner, (string?)null)
                    .SetProperty(s => s.ClaimedAt, (DateTimeOffset?)null)
                    .SetProperty(s => s.NextAutoRetryAt, now), ct);
        }

        var rows = await db.WritingSubmissions
            .Where(s => s.Status == WritingSubmissionStatuses.Grading && s.ClaimedAt != null && s.ClaimedAt <= cutoff)
            .ToListAsync(ct);
        foreach (var row in rows) Requeue(row, now);
        await db.SaveChangesAsync(ct);
        return rows.Count;
    }

    /// <summary>
    /// Queued rows due for grading: a re-queued row once <see cref="WritingSubmission.NextAutoRetryAt"/>
    /// passed; otherwise batched rows, and express rows whose detached grade never started (2 min grace).
    /// </summary>
    public static Task<List<Guid>> DueQueuedIdsAsync(LearnerDbContext db, DateTimeOffset now, int take, CancellationToken ct)
    {
        var staleExpressCutoff = now.AddMinutes(-2);
        return db.WritingSubmissions.AsNoTracking()
            .Where(s => s.Status == WritingSubmissionStatuses.Queued
                && ((s.NextAutoRetryAt == null && (s.GradingTier == "batched" || s.SubmittedAt < staleExpressCutoff))
                    || s.NextAutoRetryAt <= now))
            .OrderBy(s => s.SubmittedAt)
            .Select(s => s.Id)
            .Take(take)
            .ToListAsync(ct);
    }

    /// <summary>On shutdown: give this process's own in-flight claims back to the queue, due now.</summary>
    public static async Task<int> RequeueOwnClaimsAsync(
        LearnerDbContext db, string ownerPrefix, DateTimeOffset now, CancellationToken ct)
    {
        if (!db.Database.IsInMemory())
        {
            return await db.WritingSubmissions
                .Where(s => s.Status == WritingSubmissionStatuses.Grading && s.ClaimOwner != null && s.ClaimOwner.StartsWith(ownerPrefix))
                .ExecuteUpdateAsync(set => set
                    .SetProperty(s => s.Status, WritingSubmissionStatuses.Queued)
                    .SetProperty(s => s.ClaimOwner, (string?)null)
                    .SetProperty(s => s.ClaimedAt, (DateTimeOffset?)null)
                    .SetProperty(s => s.NextAutoRetryAt, now), ct);
        }

        var rows = await db.WritingSubmissions
            .Where(s => s.Status == WritingSubmissionStatuses.Grading && s.ClaimOwner != null && s.ClaimOwner.StartsWith(ownerPrefix))
            .ToListAsync(ct);
        foreach (var row in rows) Requeue(row, now);
        await db.SaveChangesAsync(ct);
        return rows.Count;
    }

    private static void Requeue(WritingSubmission row, DateTimeOffset now)
    {
        row.Status = WritingSubmissionStatuses.Queued;
        row.ClaimOwner = null;
        row.ClaimedAt = null;
        row.NextAutoRetryAt = now;
    }
}
