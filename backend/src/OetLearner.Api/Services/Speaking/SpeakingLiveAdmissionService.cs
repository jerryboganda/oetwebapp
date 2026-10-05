using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Speaking;

public enum SpeakingLiveAdmissionOutcome
{
    /// <summary>A place was found: the caller proceeds to hold the credit and start the clock.</summary>
    Admitted,

    /// <summary>The cap is full: the caller must NOT hold a credit or start a clock and returns the wait view.</summary>
    Waiting,

    /// <summary>The gate does not apply (kill switch, no healthy live provider, or the gate itself failed): proceed as before.</summary>
    Bypassed,
}

public static class SpeakingLiveAdmissionBypassReasons
{
    public const string Disabled = "disabled";
    public const string LiveVoiceUnavailable = "live_voice_unavailable";
    public const string AdmissionUnavailable = "admission_unavailable";

    /// <summary>The caller already owns a database transaction (the admin corpus harness): the gate never takes
    /// its platform-wide lock inside someone else's transaction, so it does not apply.</summary>
    public const string AmbientTransaction = "ambient_transaction";
}

public sealed record SpeakingLiveAdmissionResult(
    SpeakingLiveAdmissionOutcome Outcome,
    SpeakingLiveAdmissionView? Waiting = null,
    string? BypassReason = null,
    bool NewPlace = false)
{
    /// <summary>True when the caller has to stop before any credit hold or timer.</summary>
    public bool MustWait => Outcome == SpeakingLiveAdmissionOutcome.Waiting;

    /// <summary>True only for the call that just took the place (not an idempotent repeat): the one call that
    /// may give it back at once if the start that follows fails.</summary>
    public bool TookNewPlace => Outcome == SpeakingLiveAdmissionOutcome.Admitted && NewPlace;
}

internal readonly record struct EffectiveAdmissionSettings(bool Enabled, int MaxConcurrent, string Source);

/// <summary>
/// Live AI Speaking admission control (owner decision 5 Oct 2026): at most <c>MaxConcurrent</c> live AI
/// patient sessions at once (default 100), a FIFO wait queue beyond that.
///
/// WHERE it sits: at the two unscored gates that precede every credit hold and every clock, so a learner who
/// has to wait has paid nothing and started nothing. <see cref="SpeakingExamService.FinishIntroAsync"/> (an AI
/// exam, one slot for both cards) and <see cref="SpeakingSessionService.FinishWarmupAsync"/> (a standalone AI
/// practice card) call <see cref="AdmitOrQueueAsync"/> first; only an <c>Admitted</c> or <c>Bypassed</c> call
/// goes on to hold the credit and start the clock. A waiting learner's page simply repeats the same call, so the
/// call that finds a free place admits and starts atomically. A learner who cannot fund the hold is refused (402)
/// by the caller's <c>beforeNewPlace</c> check BEFORE taking a place or a line position, and a start that fails after
/// the admission gives the place back at once (<see cref="ReleaseAsync"/>).
///
/// HOW it is atomic: every decision runs under one Postgres advisory lock (cross-process: both API slots and
/// the ai-worker), inside a short transaction of its own that waits at most five seconds for the lock (a hung holder
/// turns into the fail-open path below, never a connection held for ever). The line is FIFO by a strictly increasing
/// ticket (<see cref="SpeakingLiveAdmission.Seq"/>); a caller is admitted only when its rank among live waiters is
/// below the number of free slots, so a newcomer can never overtake an earlier waiter even while that waiter's page
/// has not polled yet. A waiter silent for the heartbeat window has left the line (its ticket stops counting).
/// A learner already waiting who still cannot be admitted (the common poll of a long line) takes a lock-free path
/// that only refreshes its own heartbeat (<see cref="TryKeepWaitingAsync"/>); only an enqueue, an admission or an
/// expiry takes the lock, so a long line polling every few seconds never queues on it. That path never admits, so
/// a stale read there can at worst delay an admission by one poll. A caller that already owns a database transaction
/// (the admin corpus harness) is never gated: the lock is transaction-scoped and would be held, platform-wide, until
/// that foreign transaction ends.
///
/// ONE PLACE PER LEARNER: a learner holds at most one live place (in the line or holding a slot) at a time. A request
/// for a DIFFERENT subject while another place is waiting or holding is refused with a 409
/// <c>speaking_live_session_active</c>, so one account cannot fill the line or the cap by creating many sessions.
///
/// WHAT counts as a held slot (<see cref="HoldingRows"/>): an Admitted row that is inside its safety TTL AND
/// either was admitted within the claim window (it is about to start) or whose subject is running (exam
/// PrepA..ActiveB, practice Prep/Active). A finished, cancelled or expired subject therefore frees its slot at
/// once with no release hook on any of the many terminal paths. An Admitted row that no longer holds (its claim
/// window is over and the subject never started: a refused credit hold, a lost response) is NOT admitted again for
/// free: it asks for a place again and the cap is re-checked like anyone else's. A running session is never
/// evicted: lowering the cap only stops new admissions until the count drains.
///
/// FAIL OPEN: if the gate itself throws (a missing table, a database fault, a lock wait that timed out) the learner
/// is let through with a logged error; a capacity check must never be the reason a paying learner cannot start. The
/// deliberate refusals are a full line (<c>speaking_live_queue_full</c>, a retryable 503), a second place for the same
/// learner (409) and, from the caller's check, an unfunded learner (402).
/// </summary>
public sealed class SpeakingLiveAdmissionService(
    LearnerDbContext db,
    IOptions<SpeakingLiveAdmissionOptions> options,
    TimeProvider? clock = null,
    ILogger<SpeakingLiveAdmissionService>? logger = null)
{
    internal const string SettingsId = "global";
    private const string LockName = "speaking-live-admission";

    // SQLite (desktop) and the in-memory provider (tests) have no advisory locks; a process gate keeps them atomic.
    private static readonly SemaphoreSlim NonPostgresGate = new(1, 1);

    private readonly TimeProvider time = clock ?? TimeProvider.System;

    /// <summary>The primary key of a subject's admission row: one row per subject, ever.</summary>
    public static string RowId(string kind, string subjectId) => string.Concat(kind, ":", subjectId);

    // ─────────────────────────────────────────────────────────────────
    // Admission
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Admits the subject if a place is free and nobody earlier is waiting for it, otherwise puts it in (or keeps
    /// it in) the line. Idempotent: an already admitted subject that still holds its place is admitted again, a
    /// waiting one refreshes its heartbeat and keeps its ticket. <paramref name="liveVoiceAvailable"/> false means no
    /// live provider is healthy: the learner uses the recorder fallback, which consumes no live capacity, so the gate
    /// does not apply.
    /// <paramref name="beforeNewPlace"/> (optional) runs OUTSIDE the lock, only when this call is about to take, or look
    /// for, a place: not on the poll of a learner who is already waiting and still cannot be admitted. The caller uses
    /// it for the read-only "can this learner pay for the hold" check; whatever it throws (a 402) propagates and
    /// nothing is queued.
    /// </summary>
    public async Task<SpeakingLiveAdmissionResult> AdmitOrQueueAsync(
        string userId,
        string kind,
        string subjectId,
        bool liveVoiceAvailable,
        CancellationToken ct,
        Func<CancellationToken, Task>? beforeNewPlace = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectId);
        if (!SpeakingLiveAdmissionKinds.IsKnown(kind))
        {
            throw new ArgumentException("Unknown live admission kind.", nameof(kind));
        }

        if (!liveVoiceAvailable)
        {
            return Bypassed(SpeakingLiveAdmissionBypassReasons.LiveVoiceUnavailable);
        }

        if (!options.Value.Enabled)
        {
            return Bypassed(SpeakingLiveAdmissionBypassReasons.Disabled);
        }

        // The decision lock is transaction-scoped: taken inside a caller-owned transaction it would be held until
        // THAT transaction ends, blocking every learner platform-wide. Never gate such a caller.
        if (db.Database.CurrentTransaction is not null)
        {
            return Bypassed(SpeakingLiveAdmissionBypassReasons.AmbientTransaction);
        }

        try
        {
            var settings = await ResolveSettingsAsync(ct);
            if (!settings.Enabled)
            {
                // The admin kill switch is the lever for a stuck or slow lock holder, so it must never itself wait
                // on that lock: a waiting learner's row is released by a single-row UPDATE and the learner goes through.
                await ReleaseRowAsync(kind, subjectId, includeAdmitted: false, ct);
                return Bypassed(SpeakingLiveAdmissionBypassReasons.Disabled);
            }

            var settled = await TryKeepWaitingAsync(kind, subjectId, settings, ct);
            if (settled is not null)
            {
                return settled;
            }

            if (beforeNewPlace is not null)
            {
                try
                {
                    await beforeNewPlace(ct);
                }
                catch (ApiException)
                {
                    // A refusal (the learner can no longer pay) ends this learner's wait: a line position it already
                    // had goes at once, instead of blocking everyone behind it until its heartbeat lapses.
                    await LeaveQueueAsync(kind, subjectId, CancellationToken.None);
                    throw;
                }
            }

            return await WithLockAsync(() => AdmitOrQueueLockedAsync(userId, kind, subjectId, ct), ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ApiException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Rows this attempt added or changed must not be flushed by the caller's next SaveChanges.
            DetachAdmissionEntries();
            logger?.LogError(ex,
                "Live Speaking admission failed for {Kind} {SubjectId}; letting the learner through without a capacity check.",
                kind, subjectId);
            return Bypassed(SpeakingLiveAdmissionBypassReasons.AdmissionUnavailable);
        }
    }

    /// <summary>
    /// Gives the subject's place back at once: a waiting row leaves the line, an admitted row whose subject is NOT
    /// running stops holding a slot. Called when a start fails after the admission (a refused credit hold) and when an
    /// exam is cancelled, so a place never sits idle for the claim window. Never touches a running subject, never takes
    /// the lock (one single-row UPDATE that cannot flush the caller's pending changes) and never throws: if it fails
    /// the claim window frees the place anyway.
    /// </summary>
    public async Task ReleaseAsync(string kind, string subjectId, CancellationToken ct)
    {
        if (!SpeakingLiveAdmissionKinds.IsKnown(kind) || string.IsNullOrWhiteSpace(subjectId))
        {
            return;
        }

        try
        {
            var running = await IsSubjectRunningAsync(kind, subjectId, ct);
            await ReleaseRowAsync(kind, subjectId, includeAdmitted: !running, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Could not release the live Speaking place of {Kind} {SubjectId}; the claim window will.", kind, subjectId);
        }
    }

    /// <summary>
    /// The learner left the line (the "Leave the queue" button): a WAITING row is released at once so it stops
    /// counting towards every other waiter's position. An admitted row is left alone. Same single-row, lock-free,
    /// never-throwing update as <see cref="ReleaseAsync"/>.
    /// </summary>
    public async Task LeaveQueueAsync(string kind, string subjectId, CancellationToken ct)
    {
        if (!SpeakingLiveAdmissionKinds.IsKnown(kind) || string.IsNullOrWhiteSpace(subjectId))
        {
            return;
        }

        try
        {
            await ReleaseRowAsync(kind, subjectId, includeAdmitted: false, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Could not release the live Speaking queue place of {Kind} {SubjectId}; the heartbeat window will.", kind, subjectId);
        }
    }

    /// <summary>
    /// The lock-free path of a subject that is already in the system: an admitted one that still holds its place is
    /// admitted again (idempotent), and a waiting one that cannot be admitted yet only has its own heartbeat
    /// refreshed and its place reported. Returns null whenever the locked decision is needed: not queued yet,
    /// expired or abandoned, an admission whose place lapsed (the cap is re-checked), or a place may be free for it.
    /// Reads are untracked and the heartbeat is a single-row update that never touches the caller's change tracker.
    /// It never admits a waiting subject, so a stale read here can only delay an admission by one poll, never exceed
    /// the cap.
    /// </summary>
    private async Task<SpeakingLiveAdmissionResult?> TryKeepWaitingAsync(
        string kind, string subjectId, EffectiveAdmissionSettings settings, CancellationToken ct)
    {
        var opt = options.Value;
        var now = time.GetUtcNow();
        var heartbeatCutoff = now - opt.WaiterHeartbeat();
        var maxWaitCutoff = now - opt.MaxWait();
        var id = RowId(kind, subjectId);
        var row = await db.SpeakingLiveAdmissions.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct);
        if (row is null)
        {
            return null;
        }

        if (row.State == SpeakingLiveAdmissionState.Admitted)
        {
            // Idempotent only while the row still HOLDS its place. A lapsed claim (the subject never started) holds
            // nothing: it goes through the locked path, which re-checks the cap like any new request.
            return await HoldingRows(now).AnyAsync(a => a.Id == id, ct)
                ? new SpeakingLiveAdmissionResult(SpeakingLiveAdmissionOutcome.Admitted)
                : null;
        }

        if (row.State != SpeakingLiveAdmissionState.Waiting
            || row.LastSeenAt < heartbeatCutoff
            || row.EnqueuedAt < maxWaitCutoff)
        {
            return null;
        }

        var mySeq = row.Seq;
        var held = await CountHoldingAsync(now, ct);
        var rank = await db.SpeakingLiveAdmissions.AsNoTracking()
            .CountAsync(a => a.State == SpeakingLiveAdmissionState.Waiting
                && a.Seq < mySeq
                && a.LastSeenAt >= heartbeatCutoff, ct);
        if (rank < Math.Max(0, settings.MaxConcurrent - held))
        {
            return null;
        }

        await TouchWaitingAsync(id, now, ct);
        var queueLength = await db.SpeakingLiveAdmissions.AsNoTracking()
            .CountAsync(a => a.State == SpeakingLiveAdmissionState.Waiting && a.LastSeenAt >= heartbeatCutoff, ct);
        return new SpeakingLiveAdmissionResult(
            SpeakingLiveAdmissionOutcome.Waiting,
            Waiting: BuildView(rank, queueLength, settings.MaxConcurrent));
    }

    /// <summary>Refreshes a waiting row's heartbeat: one UPDATE that does not touch the change tracker on PostgreSQL
    /// (production). The in-memory and SQLite providers do not reliably translate a chained bulk update (see
    /// <c>BillingCouponRedemptionAtomic.TryReserveAsync</c>), so they use a tracked save, where nothing else is pending.</summary>
    private async Task TouchWaitingAsync(string id, DateTimeOffset now, CancellationToken ct)
    {
        if (db.Database.IsNpgsql())
        {
            await db.SpeakingLiveAdmissions
                .Where(a => a.Id == id && a.State == SpeakingLiveAdmissionState.Waiting)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(a => a.LastSeenAt, now)
                    .SetProperty(a => a.UpdatedAt, now), ct);
            return;
        }

        var tracked = await db.SpeakingLiveAdmissions
            .FirstOrDefaultAsync(a => a.Id == id && a.State == SpeakingLiveAdmissionState.Waiting, ct);
        if (tracked is not null)
        {
            tracked.LastSeenAt = now;
            tracked.UpdatedAt = now;
            await db.SaveChangesAsync(ct);
        }
    }

    /// <summary>
    /// Marks a subject's WAITING row (and, with <paramref name="includeAdmitted"/>, its ADMITTED row) Released with one
    /// single-row UPDATE: no lock, no change-tracker flush on any relational provider (production is PostgreSQL). Safe
    /// without the lock because releasing can only free capacity or shorten the line; the worst a concurrent
    /// decision can do is re-admit a subject whose learner just left, which the claim window frees. The in-memory
    /// provider (tests) has no bulk update and uses a tracked save of that one row.
    /// </summary>
    private async Task ReleaseRowAsync(string kind, string subjectId, bool includeAdmitted, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var id = RowId(kind, subjectId);
        var also = includeAdmitted ? SpeakingLiveAdmissionState.Admitted : SpeakingLiveAdmissionState.Waiting;

        if (db.Database.IsRelational())
        {
            await db.SpeakingLiveAdmissions
                .Where(a => a.Id == id
                    && (a.State == SpeakingLiveAdmissionState.Waiting || a.State == also))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(a => a.State, SpeakingLiveAdmissionState.Released)
                    .SetProperty(a => a.EndedAt, now)
                    .SetProperty(a => a.UpdatedAt, now), ct);
            return;
        }

        var tracked = await db.SpeakingLiveAdmissions
            .FirstOrDefaultAsync(a => a.Id == id
                && (a.State == SpeakingLiveAdmissionState.Waiting || a.State == also), ct);
        if (tracked is not null)
        {
            tracked.State = SpeakingLiveAdmissionState.Released;
            tracked.EndedAt = now;
            tracked.UpdatedAt = now;
            await db.SaveChangesAsync(ct);
        }
    }

    private async Task<SpeakingLiveAdmissionResult> AdmitOrQueueLockedAsync(
        string userId, string kind, string subjectId, CancellationToken ct)
    {
        var opt = options.Value;
        var now = time.GetUtcNow();
        var settings = await ResolveSettingsAsync(ct);
        var id = RowId(kind, subjectId);

        if (!settings.Enabled)
        {
            // The admin kill switch was flipped while this call waited for the lock: let the learner through,
            // count nothing.
            var open = await db.SpeakingLiveAdmissions
                .FirstOrDefaultAsync(a => a.Id == id && a.State == SpeakingLiveAdmissionState.Waiting, ct);
            if (open is not null)
            {
                open.State = SpeakingLiveAdmissionState.Released;
                open.EndedAt = now;
                open.UpdatedAt = now;
                await db.SaveChangesAsync(ct);
            }
            return Bypassed(SpeakingLiveAdmissionBypassReasons.Disabled);
        }

        // Abandoned waiters and over-TTL admissions stop counting before anyone is ranked.
        await ExpireStaleAsync(now, ct);

        var row = await db.SpeakingLiveAdmissions.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (row is { State: SpeakingLiveAdmissionState.Admitted }
            && await HoldingRows(now).AnyAsync(a => a.Id == id, ct))
        {
            return new SpeakingLiveAdmissionResult(SpeakingLiveAdmissionOutcome.Admitted);
        }

        // A row that is new, expired, released, or admitted-but-no-longer-holding (its claim lapsed with the subject
        // never started) asks for a place again: a fresh ticket, the cap re-checked like anyone else's.
        if (row is null || row.State != SpeakingLiveAdmissionState.Waiting)
        {
            if (await UserHoldsAnotherPlaceAsync(userId, id, now, ct))
            {
                throw ApiException.Conflict(
                    "speaking_live_session_active",
                    "You already have a live Speaking session open or waiting. Finish it, or leave the queue, before starting another.");
            }

            var waitingNow = await db.SpeakingLiveAdmissions
                .CountAsync(a => a.State == SpeakingLiveAdmissionState.Waiting, ct);
            if (waitingNow >= opt.MaxLineLengthFor(settings.MaxConcurrent))
            {
                throw ApiException.ServiceUnavailable(
                    "speaking_live_queue_full",
                    "Live Speaking practice is very busy right now. Please try again in a few minutes.");
            }

            var maxSeq = await db.SpeakingLiveAdmissions
                .Select(a => (long?)a.Seq)
                .MaxAsync(ct) ?? 0L;
            if (row is null)
            {
                row = new SpeakingLiveAdmission { Id = id, SubjectKind = kind, SubjectId = subjectId };
                db.SpeakingLiveAdmissions.Add(row);
            }

            row.UserId = userId;
            row.State = SpeakingLiveAdmissionState.Waiting;
            row.Seq = maxSeq + 1;
            row.EnqueuedAt = now;
            row.LastSeenAt = now;
            row.AdmittedAt = null;
            row.ExpiresAt = null;
            row.EndedAt = null;
            row.UpdatedAt = now;
        }
        else
        {
            // Still waiting: the poll is the heartbeat, the ticket (position) is kept.
            row.LastSeenAt = now;
            row.UpdatedAt = now;
        }

        var mySeq = row.Seq;
        var held = await CountHoldingAsync(now, ct);
        var free = Math.Max(0, settings.MaxConcurrent - held);
        var rank = await db.SpeakingLiveAdmissions
            .CountAsync(a => a.State == SpeakingLiveAdmissionState.Waiting && a.Seq < mySeq, ct);

        if (rank < free)
        {
            row.State = SpeakingLiveAdmissionState.Admitted;
            row.AdmittedAt = now;
            row.ExpiresAt = now + (kind == SpeakingLiveAdmissionKinds.Exam ? opt.ExamAdmittedTtl() : opt.PracticeAdmittedTtl());
            row.UpdatedAt = now;
            await db.SaveChangesAsync(ct);
            return new SpeakingLiveAdmissionResult(SpeakingLiveAdmissionOutcome.Admitted, NewPlace: true);
        }

        await db.SaveChangesAsync(ct);
        var queueLength = await db.SpeakingLiveAdmissions
            .CountAsync(a => a.State == SpeakingLiveAdmissionState.Waiting, ct);
        return new SpeakingLiveAdmissionResult(
            SpeakingLiveAdmissionOutcome.Waiting,
            Waiting: BuildView(rank, queueLength, settings.MaxConcurrent));
    }

    /// <summary>True when the learner already has ANOTHER live place: a waiting row with a fresh heartbeat, or an
    /// admitted row that still holds a slot (see <see cref="HoldingRows"/>). Must run under the admission lock.</summary>
    private async Task<bool> UserHoldsAnotherPlaceAsync(string userId, string id, DateTimeOffset now, CancellationToken ct)
    {
        var heartbeatCutoff = now - options.Value.WaiterHeartbeat();
        if (await db.SpeakingLiveAdmissions.AsNoTracking()
                .AnyAsync(a => a.UserId == userId
                    && a.Id != id
                    && a.State == SpeakingLiveAdmissionState.Waiting
                    && a.LastSeenAt >= heartbeatCutoff, ct))
        {
            return true;
        }

        return await HoldingRows(now).AnyAsync(a => a.UserId == userId && a.Id != id, ct);
    }

    /// <summary>
    /// Read-only: where a subject stands in the line right now, or null when it is not waiting (never queued,
    /// admitted, abandoned or expired). Used to project the waiting state on a plain GET; it never refreshes the
    /// heartbeat, so only the learner page's retry keeps a place. Never throws: a failure reads as "not waiting".
    /// </summary>
    public async Task<SpeakingLiveAdmissionView?> GetWaitingViewAsync(string kind, string subjectId, CancellationToken ct)
    {
        try
        {
            var opt = options.Value;
            var now = time.GetUtcNow();
            var heartbeatCutoff = now - opt.WaiterHeartbeat();
            var maxWaitCutoff = now - opt.MaxWait();
            var id = RowId(kind, subjectId);

            var row = await db.SpeakingLiveAdmissions.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct);
            if (row is null
                || row.State != SpeakingLiveAdmissionState.Waiting
                || row.LastSeenAt < heartbeatCutoff
                || row.EnqueuedAt < maxWaitCutoff)
            {
                return null;
            }

            var mySeq = row.Seq;
            var rank = await db.SpeakingLiveAdmissions.AsNoTracking()
                .CountAsync(a => a.State == SpeakingLiveAdmissionState.Waiting
                    && a.Seq < mySeq
                    && a.LastSeenAt >= heartbeatCutoff, ct);
            var queueLength = await db.SpeakingLiveAdmissions.AsNoTracking()
                .CountAsync(a => a.State == SpeakingLiveAdmissionState.Waiting
                    && a.LastSeenAt >= heartbeatCutoff, ct);
            var settings = await ResolveSettingsAsync(ct);
            return BuildView(rank, queueLength, settings.MaxConcurrent);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Could not read the live Speaking admission state of {Kind} {SubjectId}.", kind, subjectId);
            return null;
        }
    }

    // ─────────────────────────────────────────────────────────────────
    // Admin: settings, counts, housekeeping
    // ─────────────────────────────────────────────────────────────────

    /// <summary>The effective cap and what is in the gate now. DB-derived: counts every API slot and the worker.</summary>
    public async Task<SpeakingLiveAdmissionCounts> GetCountsAsync(CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var settings = await ResolveSettingsAsync(ct);
        var held = await CountHoldingAsync(now, ct);
        var heartbeatCutoff = now - options.Value.WaiterHeartbeat();
        var waiting = db.SpeakingLiveAdmissions.AsNoTracking()
            .Where(a => a.State == SpeakingLiveAdmissionState.Waiting && a.LastSeenAt >= heartbeatCutoff);
        var waitingCount = await waiting.CountAsync(ct);
        DateTimeOffset? oldest = waitingCount == 0
            ? null
            : await waiting.MinAsync(a => (DateTimeOffset?)a.EnqueuedAt, ct);

        return new SpeakingLiveAdmissionCounts(
            Enabled: settings.Enabled,
            MaxConcurrent: settings.MaxConcurrent,
            Source: settings.Source,
            Admitted: held,
            Waiting: waitingCount,
            Free: Math.Max(0, settings.MaxConcurrent - held),
            OldestWaitingSince: oldest,
            OldestWaitSeconds: oldest is { } since ? (int)Math.Max(0, (now - since).TotalSeconds) : null);
    }

    /// <summary>
    /// Sets the kill switch and/or the cap (either may be omitted). Creates the singleton row on first use.
    /// The caller adds its own audit event first so both are saved together.
    /// </summary>
    public async Task<SpeakingLiveAdmissionCounts> UpdateSettingsAsync(
        bool? enabled, int? maxConcurrent, string actorId, CancellationToken ct)
    {
        if (maxConcurrent is { } cap
            && (cap < SpeakingLiveAdmissionOptions.MinConcurrent || cap > SpeakingLiveAdmissionOptions.MaxConcurrent))
        {
            throw ApiException.Validation(
                "speaking_live_admission_cap_invalid",
                $"maxConcurrent must be between {SpeakingLiveAdmissionOptions.MinConcurrent} and {SpeakingLiveAdmissionOptions.MaxConcurrent}.");
        }

        var row = await db.SpeakingLiveAdmissionSettings.FirstOrDefaultAsync(s => s.Id == SettingsId, ct);
        if (row is null)
        {
            row = new SpeakingLiveAdmissionSettings
            {
                Id = SettingsId,
                Enabled = true,
                MaxConcurrent = options.Value.DefaultMaxConcurrentResolved(),
            };
            db.SpeakingLiveAdmissionSettings.Add(row);
        }

        if (enabled is { } isEnabled)
        {
            row.Enabled = isEnabled;
        }
        if (maxConcurrent is { } newCap)
        {
            row.MaxConcurrent = newCap;
        }
        row.UpdatedAt = time.GetUtcNow();
        row.UpdatedById = actorId;
        await db.SaveChangesAsync(ct);
        return await GetCountsAsync(ct);
    }

    /// <summary>
    /// Housekeeping for the sweeper: expires abandoned waiters and over-TTL admissions, purges ended rows older
    /// than the retention. Correctness never depends on it (every admission decision expires stale rows itself);
    /// it only keeps the table small. Returns the number of rows changed or removed. A caller-owned transaction is
    /// never joined (the lock is transaction-scoped): such a call does nothing.
    /// </summary>
    public Task<int> SweepAsync(CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is not null)
        {
            return Task.FromResult(0);
        }

        return WithLockAsync(async () =>
        {
            var now = time.GetUtcNow();
            var changed = await ExpireStaleAsync(now, ct);
            var purgeBefore = now - options.Value.Retention();
            var old = await db.SpeakingLiveAdmissions
                .Where(a => (a.State == SpeakingLiveAdmissionState.Released || a.State == SpeakingLiveAdmissionState.Expired)
                    && a.UpdatedAt < purgeBefore)
                .OrderBy(a => a.UpdatedAt)
                .Take(500)
                .ToListAsync(ct);
            if (old.Count > 0)
            {
                db.SpeakingLiveAdmissions.RemoveRange(old);
                await db.SaveChangesAsync(ct);
            }
            return changed + old.Count;
        }, ct);
    }

    // ─────────────────────────────────────────────────────────────────
    // Internals
    // ─────────────────────────────────────────────────────────────────

    private static SpeakingLiveAdmissionResult Bypassed(string reason)
        => new(SpeakingLiveAdmissionOutcome.Bypassed, BypassReason: reason);

    private SpeakingLiveAdmissionView BuildView(int rank, int queueLength, int cap)
    {
        var opt = options.Value;
        // Slots free at cap per average session length, so the (rank + 1)-th place opens in about this long. Never
        // shown beyond the maximum wait (the line is never longer than that can serve, see MaxLineLengthFor).
        var estimate = (int)Math.Ceiling((rank + 1.0) * opt.AverageSessionSecondsResolved() / Math.Max(1, cap));
        // The page repeats the start call at this interval: a long line backs off (+1 s per 25 waiters, at most 20 s,
        // always inside the heartbeat window) so the waiters' own polling never becomes the load it is meant to cap.
        var basePoll = opt.PollAfterSecondsResolved();
        var poll = Math.Clamp(basePoll + queueLength / 25, basePoll, Math.Max(basePoll, 20));
        return new SpeakingLiveAdmissionView(
            Status: SpeakingLiveAdmissionView.WaitingStatus,
            Position: rank + 1,
            QueueLength: Math.Max(queueLength, rank + 1),
            EstimatedWaitSeconds: Math.Clamp(estimate, 5, (int)opt.MaxWait().TotalSeconds),
            PollAfterSeconds: poll);
    }

    private async Task<EffectiveAdmissionSettings> ResolveSettingsAsync(CancellationToken ct)
    {
        var opt = options.Value;
        var row = await db.SpeakingLiveAdmissionSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == SettingsId, ct);
        if (row is null)
        {
            return new EffectiveAdmissionSettings(opt.Enabled, opt.DefaultMaxConcurrentResolved(), "default");
        }

        return new EffectiveAdmissionSettings(
            opt.Enabled && row.Enabled,
            Math.Clamp(row.MaxConcurrent, SpeakingLiveAdmissionOptions.MinConcurrent, SpeakingLiveAdmissionOptions.MaxConcurrent),
            "admin");
    }

    /// <summary>Marks abandoned waiters (no heartbeat, or waiting past the maximum) and admissions past their
    /// safety TTL as expired. Must run under the admission lock. Returns how many rows changed.</summary>
    private async Task<int> ExpireStaleAsync(DateTimeOffset now, CancellationToken ct)
    {
        var opt = options.Value;
        var heartbeatCutoff = now - opt.WaiterHeartbeat();
        var maxWaitCutoff = now - opt.MaxWait();
        var stale = await db.SpeakingLiveAdmissions
            .Where(a => (a.State == SpeakingLiveAdmissionState.Waiting
                    && (a.LastSeenAt < heartbeatCutoff || a.EnqueuedAt < maxWaitCutoff))
                || (a.State == SpeakingLiveAdmissionState.Admitted
                    && a.ExpiresAt != null && a.ExpiresAt <= now))
            .ToListAsync(ct);
        foreach (var row in stale)
        {
            row.State = SpeakingLiveAdmissionState.Expired;
            row.EndedAt = now;
            row.UpdatedAt = now;
        }
        if (stale.Count > 0)
        {
            await db.SaveChangesAsync(ct);
        }
        return stale.Count;
    }

    /// <summary>The admitted rows that hold a slot now (see the class summary): inside the safety TTL AND either
    /// admitted within the claim window or whose subject is running. Untracked, read straight from the database.</summary>
    private IQueryable<SpeakingLiveAdmission> HoldingRows(DateTimeOffset now)
    {
        var claimCutoff = now - options.Value.ClaimWindow();
        return db.SpeakingLiveAdmissions.AsNoTracking()
            .Where(a => a.State == SpeakingLiveAdmissionState.Admitted && a.ExpiresAt > now)
            .Where(a => a.AdmittedAt > claimCutoff
                || (a.SubjectKind == SpeakingLiveAdmissionKinds.Exam
                    && db.SpeakingExamSessions.Any(e => e.Id == a.SubjectId
                        && (e.State == SpeakingExamState.PrepA
                            || e.State == SpeakingExamState.ActiveA
                            || e.State == SpeakingExamState.PrepB
                            || e.State == SpeakingExamState.ActiveB)))
                || (a.SubjectKind == SpeakingLiveAdmissionKinds.Practice
                    && db.SpeakingSessions.Any(s => s.Id == a.SubjectId
                        && (s.State == SpeakingSessionState.Prep || s.State == SpeakingSessionState.Active))));
    }

    /// <summary>The number of slots in use now (see the class summary). Read straight from the database.</summary>
    private Task<int> CountHoldingAsync(DateTimeOffset now, CancellationToken ct)
        => HoldingRows(now).CountAsync(ct);

    /// <summary>True while the subject itself is running (exam PrepA..ActiveB, practice Prep/Active): its place is
    /// then in use and is never given back by <see cref="ReleaseAsync"/>.</summary>
    private Task<bool> IsSubjectRunningAsync(string kind, string subjectId, CancellationToken ct)
        => kind == SpeakingLiveAdmissionKinds.Exam
            ? db.SpeakingExamSessions.AsNoTracking()
                .AnyAsync(e => e.Id == subjectId
                    && (e.State == SpeakingExamState.PrepA
                        || e.State == SpeakingExamState.ActiveA
                        || e.State == SpeakingExamState.PrepB
                        || e.State == SpeakingExamState.ActiveB), ct)
            : db.SpeakingSessions.AsNoTracking()
                .AnyAsync(s => s.Id == subjectId
                    && (s.State == SpeakingSessionState.Prep || s.State == SpeakingSessionState.Active), ct);

    /// <summary>Runs <paramref name="body"/> serialised against every other admission decision: a Postgres
    /// transaction-scoped advisory lock (cross-process) inside a short transaction of its own that waits at most five
    /// seconds for the lock; a process gate on providers without advisory locks. Every caller has already refused a
    /// caller-owned transaction (the lock would outlive the decision inside it).</summary>
    private async Task<T> WithLockAsync<T>(Func<Task<T>> body, CancellationToken ct)
    {
        if (!db.Database.IsNpgsql())
        {
            await NonPostgresGate.WaitAsync(ct);
            try
            {
                return await body();
            }
            finally
            {
                NonPostgresGate.Release();
            }
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // A hung lock holder must turn into a fast failure (the gate then fails open), never a pooled connection
        // waiting for ever. SET LOCAL lasts for this transaction only.
        await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '5s';", ct);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({LockName}, 0));",
            ct);
        var result = await body();
        await transaction.CommitAsync(ct);
        return result;
    }

    private void DetachAdmissionEntries()
    {
        foreach (var entry in db.ChangeTracker.Entries()
                     .Where(e => e.Entity is SpeakingLiveAdmission or SpeakingLiveAdmissionSettings)
                     .ToList())
        {
            entry.State = EntityState.Detached;
        }
    }
}
