using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Writing;

public sealed record WritingDraftV2View(
    string UserId,
    Guid ScenarioId,
    string Mode,
    string Content,
    int WordCount,
    int TimeSpentSeconds,
    DateTimeOffset LastSavedAt,
    Guid DraftId,
    int Version,
    string Status,
    Guid? SubmissionId,
    string? SubmissionStatus,
    string? Phase,
    int? ReadingSecondsRemaining,
    int? WritingSecondsRemaining,
    DateTimeOffset? AttemptStartedAt);

/// <summary><see cref="ExpectedVersion"/>: 0 = create-only, n = compare-and-set,
/// null = legacy unconditional write. Timers are seconds left at this save.</summary>
public sealed record WritingDraftV2SaveRequest(
    string Content,
    int WordCount,
    int TimeSpentSeconds,
    int? ExpectedVersion = null,
    string? Phase = null,
    int? ReadingSecondsRemaining = null,
    int? WritingSecondsRemaining = null);

public interface IWritingDraftServiceV2
{
    Task<WritingDraftV2View?> GetAsync(string userId, Guid scenarioId, string mode, CancellationToken ct);
    Task<WritingDraftV2View> SaveAsync(string userId, Guid scenarioId, string mode, WritingDraftV2SaveRequest request, CancellationToken ct);
    Task DeleteAsync(string userId, Guid scenarioId, string mode, CancellationToken ct);
}

/// <summary>
/// One draft row per (UserId, ScenarioId, Mode). Zero-loss rules (WAI-06):
/// a write carrying <c>ExpectedVersion</c> is a compare-and-set, so a slow
/// retried older PUT (or a client whose GET failed) can never clobber newer
/// text; the exam clock only ever runs down (timers keep the minimum, the
/// phase never goes back); a <c>submitted</c> row ignores legacy writes and
/// starts a new attempt only on a matching version.
/// </summary>
public sealed class WritingDraftServiceV2(LearnerDbContext db, TimeProvider clock) : IWritingDraftServiceV2
{
    private const string PhaseWriting = "writing";
    private const string RetiredRevisionMode = "revision";

    public async Task<WritingDraftV2View?> GetAsync(string userId, Guid scenarioId, string mode, CancellationToken ct)
    {
        var normalizedMode = NormaliseMode(mode);
        var draft = await db.WritingDraftsV2.AsNoTracking()
            .FirstOrDefaultAsync(d => d.UserId == userId && d.ScenarioId == scenarioId && d.Mode == normalizedMode, ct);
        return draft is null ? null : await ToViewAsync(draft, ct);
    }

    public async Task<WritingDraftV2View> SaveAsync(string userId, Guid scenarioId, string mode, WritingDraftV2SaveRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var normalizedMode = NormaliseMode(mode);
        // Revise & Resubmit is retired: a stale client can no longer open or grow an invisible revision draft.
        if (normalizedMode == RetiredRevisionMode)
        {
            throw ApiException.Conflict(
                "writing_revise_retired",
                "This option is no longer available. To try this task again, start a new attempt from the task page.");
        }
        for (var attempt = 1; ; attempt++)
        {
            var entity = await db.WritingDraftsV2
                .FirstOrDefaultAsync(d => d.UserId == userId && d.ScenarioId == scenarioId && d.Mode == normalizedMode, ct);
            var creating = entity is null;
            var now = clock.GetUtcNow();
            if (entity is null)
            {
                // Only a create-only or a legacy write may create the row.
                if (request.ExpectedVersion is not (null or 0)) throw VersionConflict();
                entity = new WritingDraftV2
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    ScenarioId = scenarioId,
                    Mode = normalizedMode,
                    CreatedAt = now,
                    Version = 0,
                };
                db.WritingDraftsV2.Add(entity);
                StartAttempt(entity, request, now);
            }
            else if (request.ExpectedVersion is null)
            {
                // A late legacy autosave must never resurrect a consumed draft.
                if (entity.Status == WritingDraftStatuses.Submitted) return await ToViewAsync(entity, ct);
                RunClockDown(entity, request);
            }
            else if (request.ExpectedVersion != entity.Version)
            {
                throw VersionConflict();
            }
            else if (entity.Status == WritingDraftStatuses.Submitted)
            {
                StartAttempt(entity, request, now); // "Practice this again"
            }
            else
            {
                RunClockDown(entity, request);
            }

            entity.Content = request.Content ?? string.Empty;
            entity.WordCount = Math.Max(0, request.WordCount);
            entity.TimeSpentSeconds = Math.Max(0, request.TimeSpentSeconds);
            entity.LastSavedAt = now;
            entity.Version++;
            try
            {
                await db.SaveChangesAsync(ct); // UPDATE ... WHERE "Version" = <loaded version>
                return await ToViewAsync(entity, ct);
            }
            catch (DbUpdateException ex) when (ex is DbUpdateConcurrencyException || creating)
            {
                db.Entry(entity).State = EntityState.Detached;
                // An insert that failed while no row exists is a real error, not a race.
                if (creating && !await db.WritingDraftsV2.AnyAsync(
                        d => d.UserId == userId && d.ScenarioId == scenarioId && d.Mode == normalizedMode, ct))
                {
                    throw;
                }
                // Another write committed first. A compare-and-set write is
                // refused; a legacy write is re-applied once on the winner.
                if (request.ExpectedVersion is not null || attempt > 1) throw VersionConflict();
            }
        }
    }

    public async Task DeleteAsync(string userId, Guid scenarioId, string mode, CancellationToken ct)
    {
        var normalizedMode = NormaliseMode(mode);
        var entity = await db.WritingDraftsV2.FirstOrDefaultAsync(d => d.UserId == userId && d.ScenarioId == scenarioId && d.Mode == normalizedMode, ct);
        if (entity is null) return;
        db.WritingDraftsV2.Remove(entity);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Marks the learner's active draft for (scenario, mode) as consumed by
    /// <paramref name="submissionId"/>. Best-effort: the letter is already
    /// persisted, so a draft problem is logged and swallowed and never fails
    /// the submit. Idempotent: an already-submitted draft is left alone.
    /// </summary>
    public static async Task ConsumeAsync(
        LearnerDbContext db, string userId, Guid scenarioId, string mode, Guid submissionId, ILogger logger, CancellationToken ct)
    {
        var normalizedMode = NormaliseMode(mode);
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            WritingDraftV2? draft = null;
            try
            {
                draft = await db.WritingDraftsV2
                    .FirstOrDefaultAsync(d => d.UserId == userId && d.ScenarioId == scenarioId && d.Mode == normalizedMode, ct);
                if (draft is null || draft.Status != WritingDraftStatuses.Active) return;
                draft.Status = WritingDraftStatuses.Submitted;
                draft.SubmissionId = submissionId;
                draft.Version++;
                await db.SaveChangesAsync(ct);
                return;
            }
            catch (Exception ex)
            {
                // Never leave a failed draft write tracked: the caller's next
                // SaveChanges would retry it and fail the submit.
                if (draft is not null) db.Entry(draft).State = EntityState.Detached;
                if (ex is DbUpdateConcurrencyException && attempt == 1) continue; // an autosave won: re-read
                logger.LogWarning(ex, "Writing draft consume failed for submission {SubmissionId}.", submissionId);
                return;
            }
        }
    }

    /// <summary>
    /// Start of the learner's current attempt for (scenario, mode): row
    /// creation or the last "Practice this again". Null when there is no row
    /// or the row predates attempt tracking (legacy behaviour applies).
    /// </summary>
    public static Task<DateTimeOffset?> GetAttemptStartedAtAsync(
        LearnerDbContext db, string userId, Guid scenarioId, string mode, CancellationToken ct)
    {
        var normalizedMode = NormaliseMode(mode);
        return db.WritingDraftsV2.AsNoTracking()
            .Where(d => d.UserId == userId && d.ScenarioId == scenarioId && d.Mode == normalizedMode)
            .Select(d => d.AttemptStartedAt)
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>A new attempt (row creation or after submit): the client's clock is stored as sent.</summary>
    private static void StartAttempt(WritingDraftV2 entity, WritingDraftV2SaveRequest request, DateTimeOffset now)
    {
        entity.Status = WritingDraftStatuses.Active;
        entity.SubmissionId = null;
        entity.Phase = NormalisePhase(request.Phase);
        entity.ReadingSecondsRemaining = NonNegative(request.ReadingSecondsRemaining);
        entity.WritingSecondsRemaining = NonNegative(request.WritingSecondsRemaining);
        entity.AttemptStartedAt = now;
    }

    /// <summary>Same attempt: a stale save can never reset the clock. Null = untouched.</summary>
    private static void RunClockDown(WritingDraftV2 entity, WritingDraftV2SaveRequest request)
    {
        var phase = NormalisePhase(request.Phase);
        if (phase is not null && entity.Phase != PhaseWriting) entity.Phase = phase;
        entity.ReadingSecondsRemaining = Earlier(entity.ReadingSecondsRemaining, NonNegative(request.ReadingSecondsRemaining));
        entity.WritingSecondsRemaining = Earlier(entity.WritingSecondsRemaining, NonNegative(request.WritingSecondsRemaining));
    }

    private static int? Earlier(int? stored, int? sent)
        => sent is null ? stored : stored is null ? sent : Math.Min(stored.Value, sent.Value);

    private static int? NonNegative(int? seconds) => seconds is null ? null : Math.Max(0, seconds.Value);

    private static string? NormalisePhase(string? phase)
        => phase?.Trim().ToLowerInvariant() switch
        {
            "reading" => "reading",
            PhaseWriting => PhaseWriting,
            _ => null,
        };

    private static ApiException VersionConflict()
        => ApiException.Conflict("draft_version_conflict", "This draft was changed elsewhere. Reload it to continue.");

    private async Task<WritingDraftV2View> ToViewAsync(WritingDraftV2 entity, CancellationToken ct)
    {
        string? submissionStatus = null;
        if (entity.SubmissionId is { } submissionId)
        {
            var submission = await db.WritingSubmissions.AsNoTracking()
                .Where(s => s.Id == submissionId && s.UserId == entity.UserId)
                .Select(s => new { s.Status, s.SubmittedAt })
                .FirstOrDefaultAsync(ct);
            submissionStatus = submission?.Status;
            if (submission?.Status == WritingSubmissionStatuses.Graded)
            {
                // Effective status: a graded letter still inside its release window reads as grading, so
                // the practice/paper pages route to the countdown instead of opening a second paid attempt.
                var unrestricted = await WritingUnrestrictedAccounts.IsUnrestrictedAsync(db, entity.UserId, ct);
                submissionStatus = WritingResultRelease.Describe(
                    submission.Status, submission.SubmittedAt, unrestricted, clock.GetUtcNow()).EffectiveStatus;
            }
        }
        return new(entity.UserId, entity.ScenarioId, entity.Mode, entity.Content, entity.WordCount, entity.TimeSpentSeconds,
            entity.LastSavedAt, entity.Id, entity.Version, entity.Status, entity.SubmissionId, submissionStatus,
            entity.Phase, entity.ReadingSecondsRemaining, entity.WritingSecondsRemaining, entity.AttemptStartedAt);
    }

    private static string NormaliseMode(string? mode)
        => string.IsNullOrWhiteSpace(mode) ? "practice" : mode.Trim().ToLowerInvariant();
}
