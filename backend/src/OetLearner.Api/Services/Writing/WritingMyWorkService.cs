using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.FreeSamples;

namespace OetLearner.Api.Services.Writing;

/// <summary>
/// Post Submissions (GET /v1/writing/my-work): the learner's unconsumed
/// drafts and every non-mock Writing submission in one list, newest activity
/// first, with server-computed states and actions. Learner-scoped; never
/// carries letter text. Keyset paging: <c>before</c> = the last item's
/// <c>lastActivityAt</c> (a draft's last save, a submission's submit time).
/// A graded letter still inside its 15-minute release window reads as grading
/// (<see cref="WritingResultRelease"/>): it never offers <c>open_result</c> early.
/// </summary>
public sealed class WritingMyWorkService(LearnerDbContext db, TimeProvider clock)
{
    public const int DefaultLimit = 20;
    public const int MaxLimit = 50;
    private const string MockMode = "mock";
    // Revise & Resubmit is retired: drafts left in this mode are never listed or resumed.
    private const string RetiredRevisionMode = "revision";

    private sealed record Row(DateTimeOffset At, WritingDraftV2? Draft, SubmissionRow? Submission)
    {
        public Guid ScenarioId => Draft?.ScenarioId ?? Submission!.ScenarioId;
    }

    private sealed record SubmissionRow(
        Guid Id, Guid ScenarioId, string Mode, bool IsRevision, string Status, int WordCount, DateTimeOffset CreatedAt,
        DateTimeOffset? ClaimedAt, DateTimeOffset SubmittedAt, DateTimeOffset? NextAutoRetryAt, int AutoRetryCount, bool? FailureRetryable);

    public async Task<WritingMyWorkResponse> ListAsync(string userId, int? limit, DateTimeOffset? before, CancellationToken ct)
    {
        var take = limit is > 0 ? Math.Min(limit.Value, MaxLimit) : DefaultLimit;
        var cursor = before?.ToUniversalTime(); // Npgsql only writes UTC timestamptz parameters

        var draftQuery = db.WritingDraftsV2.AsNoTracking()
            .Where(d => d.UserId == userId && d.Status == WritingDraftStatuses.Active
                && d.Mode != MockMode && d.Mode != RetiredRevisionMode);
        var submissionQuery = db.WritingSubmissions.AsNoTracking()
            .Where(s => s.UserId == userId && s.Mode != MockMode);
        if (cursor is { } at)
        {
            draftQuery = draftQuery.Where(d => d.LastSavedAt < at);
            submissionQuery = submissionQuery.Where(s => s.CreatedAt < at);
        }

        var drafts = await draftQuery.OrderByDescending(d => d.LastSavedAt).Take(take + 1).ToListAsync(ct);
        var submissions = await submissionQuery
            .OrderByDescending(s => s.CreatedAt) // IX_WritingSubmissions_User_CreatedAt
            .Take(take + 1)
            .Select(s => new SubmissionRow(s.Id, s.ScenarioId, s.Mode, s.IsRevision, s.Status, s.WordCount, s.CreatedAt,
                s.ClaimedAt, s.SubmittedAt, s.NextAutoRetryAt, s.AutoRetryCount, s.FailureRetryable))
            .ToListAsync(ct);

        var rows = drafts.Select(d => new Row(d.LastSavedAt, d, null))
            .Concat(submissions.Select(s => new Row(s.CreatedAt, null, s)))
            .OrderByDescending(r => r.At)
            .Take(take + 1)
            .ToList();
        var hasMore = rows.Count > take;
        var page = rows.Take(take).ToList();

        var scenarioIds = page.Select(r => r.ScenarioId).Distinct().ToList();
        var scenarios = await db.WritingScenarios.AsNoTracking()
            .Where(s => scenarioIds.Contains(s.Id))
            .Select(s => new { s.Id, s.Title, s.LetterType })
            .ToDictionaryAsync(s => s.Id, ct);

        // Free-sample uses bind the submission id ("N"); legacy rows may hold "D".
        var resourceIds = page.Where(r => r.Submission is not null)
            .SelectMany(r => new[] { r.Submission!.Id.ToString("N"), r.Submission.Id.ToString("D") })
            .ToList();
        var freeSampleIds = (await db.FreeSampleUses.AsNoTracking()
                .Where(u => u.UserId == userId && u.Subtest == FreeSampleService.Writing
                    && u.ResourceKind == FreeSampleUse.KindWritingSubmission && resourceIds.Contains(u.ResourceId))
                .Select(u => u.ResourceId)
                .ToListAsync(ct))
            .Select(id => Guid.TryParse(id, out var parsed) ? parsed : Guid.Empty)
            .ToHashSet();

        var now = clock.GetUtcNow();
        var unrestricted = await WritingUnrestrictedAccounts.IsUnrestrictedAsync(db, userId, ct);
        var items = page.Select(row =>
        {
            scenarios.TryGetValue(row.ScenarioId, out var scenario);
            // Real data only: a deleted task leaves an empty title for the UI's own label.
            var title = scenario?.Title ?? string.Empty;
            var letterType = scenario is null || string.IsNullOrWhiteSpace(scenario.LetterType) ? null : scenario.LetterType;
            if (row.Draft is { } d)
            {
                return new WritingMyWorkItemResponse(
                    $"draft:{d.Id}", "draft", "draft", d.Status, d.ScenarioId, title, letterType, d.Mode,
                    IsRevision: false, IsFreeSample: false, d.Id, SubmissionId: null, d.WordCount,
                    d.Phase, d.ReadingSecondsRemaining, d.WritingSecondsRemaining, d.LastSavedAt,
                    CanRetry: false, AutoRetrying: false,
                    [new WritingMyWorkActionResponse("resume", $"/writing/practice/session/{d.ScenarioId}")]);
            }

            var s = row.Submission!;
            var release = WritingResultRelease.Describe(s.Status, s.SubmittedAt, unrestricted, now);
            var (state, canRetry, autoRetrying) = ComputeRetryState(
                s.Status, s.ClaimedAt, s.SubmittedAt, s.NextAutoRetryAt, s.AutoRetryCount, s.FailureRetryable, now);
            // Graded but inside the release window: still "being assessed" - never open_result, never Retry.
            if (release.State == WritingResultRelease.Held)
            {
                state = "grading";
                canRetry = false;
            }
            var root = $"/writing/submissions/{s.Id}";
            IReadOnlyList<WritingMyWorkActionResponse> actions = state == "graded"
                ? [new("open_result", $"{root}/results"), new("view_letter", root)]
                : [new(canRetry ? "retry" : "wait", $"{root}/grading"), new("view_letter", root)];
            return new WritingMyWorkItemResponse(
                $"submission:{s.Id}", "submission", state, release.EffectiveStatus, s.ScenarioId, title, letterType, s.Mode,
                s.IsRevision, freeSampleIds.Contains(s.Id), DraftId: null, s.Id, s.WordCount,
                Phase: null, ReadingSecondsRemaining: null, WritingSecondsRemaining: null, s.CreatedAt,
                canRetry, autoRetrying, actions,
                ReleaseState: release.State, ReleaseAt: release.ReleaseAt);
        }).ToList();

        return new WritingMyWorkResponse(items, hasMore, ServerNow: now);
    }

    /// <summary>
    /// List state + Retry availability. The same rules as the grading-status response
    /// (<c>WritingV2ResponseMapper.ToSubmissionResponse</c>): a failed row can be retried unless the
    /// failure is final (<c>FailureRetryable == false</c>); a grading claim silent past the lease shows
    /// as failed + Retry; a queued row is live grading (the cron or an auto-retry owns it) and only
    /// offers Retry once nothing has picked it up within the lease; <c>autoRetrying</c> = the server
    /// re-queued a failed run by itself.
    /// </summary>
    internal static (string State, bool CanRetry, bool AutoRetrying) ComputeRetryState(
        string status, DateTimeOffset? claimedAt, DateTimeOffset submittedAt, DateTimeOffset? nextAutoRetryAt,
        int autoRetryCount, bool? failureRetryable, DateTimeOffset now)
    {
        if (status == WritingSubmissionStatuses.Graded) return ("graded", false, false);
        if (status == WritingSubmissionStatuses.Failed) return ("failed", failureRetryable != false, false);
        if (WritingGradeRecovery.IsStaleGrading(status, claimedAt, now)) return ("failed", true, false);
        return (
            "grading",
            WritingGradeRecovery.IsStaleQueued(status, nextAutoRetryAt, submittedAt, now),
            status == WritingSubmissionStatuses.Queued && autoRetryCount > 0);
    }
}
