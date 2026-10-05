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
}

public sealed record SpeakingLiveAdmissionResult(
    SpeakingLiveAdmissionOutcome Outcome,
    SpeakingLiveAdmissionView? Waiting = null,
    string? BypassReason = null)
{
    /// <summary>True when the caller has to stop before any credit hold or timer.</summary>
    public bool MustWait => Outcome == SpeakingLiveAdmissionOutcome.Waiting;
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
/// call that finds a free place admits and starts atomically.
///
/// HOW it is atomic: every decision runs under one Postgres advisory lock (cross-process: both API slots and
/// the ai-worker), inside a short transaction. The line is FIFO by a strictly increasing ticket
/// (<see cref="SpeakingLiveAdmission.Seq"/>); a caller is admitted only when its rank among live waiters is below
/// the number of free slots, so a newcomer can never overtake an earlier waiter even while that waiter's page has
/// not polled yet. A waiter silent for the heartbeat window has left the line (its ticket stops counting).
///
/// WHAT counts as a held slot (<see cref="CountHoldingAsync"/>): an Admitted row that is inside its safety TTL AND
/// either was admitted within the claim window (it is about to start) or whose subject is running (exam
/// PrepA..ActiveB, practice Prep/Active). A finished, cancelled or expired subject therefore frees its slot at
/// once with no release hook on any of the many terminal paths, and a start that failed after the admission (a
/// refused credit hold) frees it after the short claim window with no compensation step. A running session is
/// never evicted: lowering the cap only stops new admissions until the count drains.
///
/// FAIL OPEN: if the gate itself throws (a missing table, a database fault) the learner is let through with a
/// logged error; a capacity check must never be the reason a paying learner cannot start. The one deliberate
/// refusal is a full line (<c>speaking_live_queue_full</c>, a retryable 503).
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
    /// it in) the line. Idempotent: an already admitted subject is admitted again, a waiting one refreshes its
    /// heartbeat and keeps its ticket. <paramref name="liveVoiceAvailable"/> false means no live provider is healthy:
    /// the learner uses the recorder fallback, which consumes no live capacity, so the gate does not apply.
    /// </summary>
    public async Task<SpeakingLiveAdmissionResult> AdmitOrQueueAsync(
        string userId,
        string kind,
        string subjectId,
        bool liveVoiceAvailable,
        CancellationToken ct)
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

        try
        {
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

    private async Task<SpeakingLiveAdmissionResult> AdmitOrQueueLockedAsync(
        string userId, string kind, string subjectId, CancellationToken ct)
    {
        var opt = options.Value;
        var now = time.GetUtcNow();
        var settings = await ResolveSettingsAsync(ct);
        var id = RowId(kind, subjectId);

        if (!settings.Enabled)
        {
            // The admin kill switch was flipped while this learner waited: let them through, count nothing.
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
        if (row is { State: SpeakingLiveAdmissionState.Admitted })
        {
            return new SpeakingLiveAdmissionResult(SpeakingLiveAdmissionOutcome.Admitted);
        }

        if (row is null || row.State != SpeakingLiveAdmissionState.Waiting)
        {
            var waitingNow = await db.SpeakingLiveAdmissions
                .CountAsync(a => a.State == SpeakingLiveAdmissionState.Waiting, ct);
            if (waitingNow >= opt.MaxQueueLengthResolved())
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

            // A fresh ticket for a new, expired or released place: the back of the line.
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
            return new SpeakingLiveAdmissionResult(SpeakingLiveAdmissionOutcome.Admitted);
        }

        await db.SaveChangesAsync(ct);
        var queueLength = await db.SpeakingLiveAdmissions
            .CountAsync(a => a.State == SpeakingLiveAdmissionState.Waiting, ct);
        return new SpeakingLiveAdmissionResult(
            SpeakingLiveAdmissionOutcome.Waiting,
            Waiting: BuildView(rank, queueLength, settings.MaxConcurrent));
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
    /// it only keeps the table small. Returns the number of rows changed or removed.
    /// </summary>
    public Task<int> SweepAsync(CancellationToken ct)
        => WithLockAsync(async () =>
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

    // ─────────────────────────────────────────────────────────────────
    // Internals
    // ─────────────────────────────────────────────────────────────────

    private static SpeakingLiveAdmissionResult Bypassed(string reason)
        => new(SpeakingLiveAdmissionOutcome.Bypassed, BypassReason: reason);

    private SpeakingLiveAdmissionView BuildView(int rank, int queueLength, int cap)
    {
        var opt = options.Value;
        // Slots free at cap per average session length, so the (rank + 1)-th place opens in about this long.
        var estimate = (int)Math.Ceiling((rank + 1.0) * opt.AverageSessionSecondsResolved() / Math.Max(1, cap));
        return new SpeakingLiveAdmissionView(
            Status: SpeakingLiveAdmissionView.WaitingStatus,
            Position: rank + 1,
            QueueLength: Math.Max(queueLength, rank + 1),
            EstimatedWaitSeconds: Math.Clamp(estimate, 5, 7200),
            PollAfterSeconds: opt.PollAfterSecondsResolved());
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

    /// <summary>The number of slots in use now (see the class summary). Read straight from the database.</summary>
    private async Task<int> CountHoldingAsync(DateTimeOffset now, CancellationToken ct)
    {
        var claimCutoff = now - options.Value.ClaimWindow();
        return await db.SpeakingLiveAdmissions.AsNoTracking()
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
                        && (s.State == SpeakingSessionState.Prep || s.State == SpeakingSessionState.Active))))
            .CountAsync(ct);
    }

    /// <summary>Runs <paramref name="body"/> serialised against every other admission decision: a Postgres
    /// transaction-scoped advisory lock (cross-process) inside a short transaction, joining an ambient
    /// transaction when the caller already has one; a process gate on providers without advisory locks.</summary>
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

        await using var transaction = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(ct)
            : null;
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({LockName}, 0));",
            ct);
        var result = await body();
        if (transaction is not null)
        {
            await transaction.CommitAsync(ct);
        }
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
