using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Speaking;

/// <summary>
/// Speaking module rebuild (2026-06-11 spec). Background sweeper that drives the
/// server-authoritative auto-advance of in-flight Speaking exams.
///
/// The ConversationHub fires <c>TimeUp</c> to advance a card, but that timer
/// lives in-process and is lost on a server restart. This worker is the
/// belt-and-suspenders backstop: every 20 seconds it recomputes overdue
/// transitions for every non-terminal exam (so Card A auto-closes and Card B
/// auto-reveals even with a dead or disconnected client) and expires exams that
/// have been idle in the unscored Intro past <see cref="SpeakingExamService.IdleExpiry"/>.
///
/// The same pass is also the hard server-side cap on a standalone AI role-play:
/// a session the client never ended is finished and its provider sessions
/// hung up once it is past its hard stop (an exam card is also graded; an abandoned
/// practice role-play is not, see <see cref="SweepOverdueRolePlaysAsync"/>),
/// and a role-play that ended any other way (the learner's /end, the exam clock, a cancelled
/// exam) has its provider sessions hung up at the same point (see <see cref="HangUpEndedRolePlaysAsync"/>).
/// </summary>
public sealed class SpeakingExamAutoAdvanceWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<SpeakingExamAutoAdvanceWorker> logger) : BackgroundService
{
    private static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan HoldSweepInterval = TimeSpan.FromHours(1);

    /// <summary>
    /// An overdue role-play is only finished this long after its hard stop. Older Active rows
    /// (abandoned before this sweep existed) are left alone: grading them all at once would flood
    /// the grader, and their credit holds are already refunded by the stale-hold sweep.
    /// </summary>
    internal static readonly TimeSpan RolePlaySweepHorizon = TimeSpan.FromHours(12);
    private const int RolePlaySweepBatch = 500;

    /// <summary>
    /// How long past its hard stop a role-play that ended some other way is still swept for live
    /// OpenAI sessions: long enough to ride out a worker restart, short enough that a restart never
    /// re-hangs-up a whole day of role-plays.
    /// </summary>
    internal static readonly TimeSpan HangUpHorizon = TimeSpan.FromMinutes(10);

    // Session id -> when its hang-up was attempted, so the 20 s pass tries each role-play once.
    // ponytail: one attempt, in process. A failed hang-up is logged, not retried (retries in a slow
    // provider outage would stall this loop, and the OpenAI spend limit is the backstop); a restart
    // repeats the calls once, which is safe (a 404 means already ended). Entries expire with the horizon.
    // Only the sequential sweep loop reads or writes it, so it needs no lock.
    private readonly Dictionary<string, DateTimeOffset> _hangUpAttempted = new(StringComparer.Ordinal);

    private DateTimeOffset _lastHoldSweepAt = DateTimeOffset.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SweepOnceAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Speaking exam auto-advance sweep failed");
            }

            if (DateTimeOffset.UtcNow - _lastHoldSweepAt >= HoldSweepInterval)
            {
                _lastHoldSweepAt = DateTimeOffset.UtcNow;
                try
                {
                    await SettleStaleCreditHoldsAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Speaking credit-hold settlement sweep failed");
                }
            }

            try
            {
                await Task.Delay(SweepInterval, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                // Shutdown — exit quietly.
            }
        }
    }

    /// <summary>Advances every overdue exam once, finishes every overdue standalone role-play, then
    /// hangs up the OpenAI sessions of role-plays that ended some other way. The three passes are
    /// independent: a failure in one is logged and never skips the others (the hard stop is the
    /// safety net for the exam clock, so it must not depend on the exam pass succeeding). Returns
    /// the number of exams whose state changed. Exposed for tests.</summary>
    public async Task<int> SweepOnceAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var changed = 0;
        // Exams first: the exam clock ends a child card exactly at its deadline (no grace), so the
        // role-play pass below only ever sees a child the exam pass could not end.
        try
        {
            changed = await SweepExamsAsync(now, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Speaking exam auto-advance sweep failed");
        }

        try
        {
            await SweepOverdueRolePlaysAsync(now, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Speaking role-play hard-stop sweep failed");
        }

        // Last, and on its own: it also covers a session the pass above finished but then failed on
        // (a throw after the Active to Finished swap leaves it Finished and not yet hung up).
        try
        {
            await HangUpEndedRolePlaysAsync(now, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Speaking role-play provider hang-up sweep failed");
        }

        // Housekeeping of the live-session admission table: expires abandoned waiters, purges old rows. Not
        // load-bearing (every admission decision expires stale rows itself), so it runs last and on its own.
        try
        {
            await SweepAdmissionsAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Speaking live admission housekeeping failed");
        }
        return changed;
    }

    /// <summary>Expires abandoned waiters and purges ended rows of the live-session admission table. A host
    /// without the admission service registered (a test) does nothing. Returns the rows changed. Exposed for tests.</summary>
    public async Task<int> SweepAdmissionsAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var admission = scope.ServiceProvider.GetService<SpeakingLiveAdmissionService>();
        return admission is null ? 0 : await admission.SweepAsync(ct);
    }

    private async Task<int> SweepExamsAsync(DateTimeOffset now, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();

        var active = await db.SpeakingExamSessions
            .Where(e => e.State != SpeakingExamState.Completed
                && e.State != SpeakingExamState.Cancelled
                && e.State != SpeakingExamState.Expired)
            .OrderBy(e => e.UpdatedAt)
            .Take(500)
            .ToListAsync(ct);

        if (active.Count == 0) return 0;

        var service = scope.ServiceProvider.GetRequiredService<SpeakingExamService>();
        // Optional (a host without the admission service registered, a test, has no queue to protect).
        var admission = scope.ServiceProvider.GetService<SpeakingLiveAdmissionService>();
        var changed = 0;
        foreach (var exam in active)
        {
            try
            {
                // Expire exams left idle in the unscored Intro. A learner queued for a live AI place is NOT idle: the
                // queue never touches the exam, IntroStartedAt is its only clock, and the line's own maximum wait
                // (2 h, counted from joining it) starts after the intro, so the clock alone would expire the tail of
                // a long line. The read is the queue's own rule (fresh heartbeat, inside the maximum wait), so an
                // abandoned waiter is expired on the next sweep and a queued one is never swept from under its page.
                if (exam.State == SpeakingExamState.Intro
                    && exam.IntroStartedAt is { } started
                    && now - started > SpeakingExamService.IdleExpiry
                    && (admission is null
                        || await admission.GetWaitingViewAsync(SpeakingLiveAdmissionKinds.Exam, exam.Id, ct) is null))
                {
                    exam.State = SpeakingExamState.Expired;
                    exam.CompletedAt = now;
                    exam.UpdatedAt = now;
                    changed++;
                    continue;
                }

                if (await service.AdvanceAsync(exam, now, ct))
                {
                    exam.UpdatedAt = now;
                    changed++;
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to auto-advance speaking exam {ExamId}", exam.Id);
            }
        }

        if (changed > 0)
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Speaking exam auto-advance sweep advanced {Count} exams.", changed);
        }
        return changed;
    }

    /// <summary>
    /// The hard server-side cap on an AI role-play the client never ended. A role-play still Active
    /// past <c>RolePlayStartedAt</c> plus the card's (capped) time plus the grace is finished with a
    /// compare-and-swap, audited, (an exam card only) handed to canonical grading once, and its
    /// OpenAI sessions are hung up so a dead or hostile client cannot keep one billing. An abandoned
    /// standalone practice role-play is finished and hung up but NOT graded (see
    /// <see cref="SpeakingSessionService.FinalizeAtHardStopAsync"/>). Idempotent: a finished
    /// session is never a candidate again. Returns the number finished. Exposed for tests.
    /// </summary>
    public async Task<int> SweepOverdueRolePlaysAsync(DateTimeOffset now, CancellationToken ct)
    {
        List<string> overdue;
        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            overdue = await FindOverdueRolePlaysAsync(
                scope.ServiceProvider, now, SpeakingSessionState.Active, RolePlaySweepHorizon, ct);
        }

        var finished = 0;
        foreach (var sessionId in overdue)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                // A scope per session: one bad row cannot poison the change tracker of the next.
                await using var scope = scopeFactory.CreateAsyncScope();
                var sessions = scope.ServiceProvider.GetRequiredService<SpeakingSessionService>();
                if (!await sessions.FinalizeAtHardStopAsync(sessionId, now, ct))
                {
                    continue;
                }

                finished++;
                // After the state change and the grading hand-off: stop provider-side billing. The
                // hang-up pass below must not repeat it for the session just finished.
                _hangUpAttempted[sessionId] = now;
                var closer = scope.ServiceProvider.GetService<ILiveVoiceProviderSessionCloser>();
                if (closer is not null)
                {
                    await closer.CloseProviderSessionsAsync(sessionId, ct);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Failed to hard-stop overdue Speaking role-play {SessionId}", sessionId);
            }
        }

        if (finished > 0)
        {
            logger.LogWarning("Speaking hard-stop sweep finished {Count} role-plays the client never ended.", finished);
        }
        return finished;
    }

    /// <summary>Non-tutor role-plays in <paramref name="state"/> that are past their hard stop by no
    /// more than <paramref name="horizon"/>.</summary>
    private static async Task<List<string>> FindOverdueRolePlaysAsync(
        IServiceProvider services,
        DateTimeOffset now,
        SpeakingSessionState state,
        TimeSpan horizon,
        CancellationToken ct)
    {
        var db = services.GetRequiredService<LearnerDbContext>();
        var options = services.GetService<IOptions<LiveVoiceOptions>>()?.Value;

        var query = db.SpeakingSessions.AsNoTracking()
            .Where(s => s.State == state && s.Mode != SpeakingSessionMode.LiveTutor);
        if (db.Database.IsNpgsql())
        {
            // Postgres narrows by age in SQL so a backlog of ancient Active rows is never loaded;
            // SQLite cannot compare DateTimeOffset in SQL, so every provider re-applies the age
            // filter in memory below.
            var oldestRelevant = now
                - horizon
                - TimeSpan.FromSeconds(1800 + 120);
            query = query.Where(s => (s.RolePlayStartedAt ?? s.UpdatedAt) >= oldestRelevant);
        }

        var candidates = await query
            .OrderBy(s => s.Id)
            .Take(RolePlaySweepBatch)
            .Select(s => new { s.Id, s.RolePlayCardId, s.RolePlayStartedAt, s.UpdatedAt })
            .ToListAsync(ct);
        if (candidates.Count == 0)
        {
            return [];
        }

        var cardIds = candidates.Select(c => c.RolePlayCardId).Distinct(StringComparer.Ordinal).ToArray();
        var cardSeconds = await db.RolePlayCards.AsNoTracking()
            .Where(c => cardIds.Contains(c.Id))
            .Select(c => new { c.Id, c.RolePlayTimeSeconds })
            .ToDictionaryAsync(c => c.Id, c => c.RolePlayTimeSeconds, StringComparer.Ordinal, ct);

        var overdue = new List<string>();
        foreach (var candidate in candidates)
        {
            var window = SpeakingRolePlayLimits.Resolve(
                candidate.RolePlayStartedAt ?? candidate.UpdatedAt,
                cardSeconds.GetValueOrDefault(candidate.RolePlayCardId),
                options);
            if (now >= window.HardStopAt && now - window.HardStopAt <= horizon)
            {
                overdue.Add(candidate.Id);
            }
        }
        return overdue;
    }

    /// <summary>
    /// The hard stop applies however a role-play ended. The learner's /end, the exam clock and a
    /// cancelled exam finish a session without touching its provider sessions, and a browser can
    /// neither be trusted to close its OpenAI connection nor close one that never connected. So every
    /// Finished role-play past its hard stop (and inside <see cref="HangUpHorizon"/>) has its OpenAI
    /// sessions hung up here, once per process. The closer never throws except for cancellation.
    /// Returns how many role-plays were attempted. Exposed for tests.
    /// ponytail: one 500-row batch per tick (first by id), like the finaliser pass; page it if the
    /// last ~40 minutes of Finished role-plays ever exceed that.
    /// </summary>
    public async Task<int> HangUpEndedRolePlaysAsync(DateTimeOffset now, CancellationToken ct)
    {
        foreach (var expired in _hangUpAttempted.Where(entry => now - entry.Value > HangUpHorizon).Select(entry => entry.Key).ToList())
        {
            _hangUpAttempted.Remove(expired);
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var closer = scope.ServiceProvider.GetService<ILiveVoiceProviderSessionCloser>();
        if (closer is null)
        {
            return 0;
        }

        var attempted = 0;
        foreach (var sessionId in await FindOverdueRolePlaysAsync(
                     scope.ServiceProvider, now, SpeakingSessionState.Finished, HangUpHorizon, ct))
        {
            if (!_hangUpAttempted.TryAdd(sessionId, now))
            {
                continue;
            }

            ct.ThrowIfCancellationRequested();
            attempted++;
            try
            {
                await closer.CloseProviderSessionsAsync(sessionId, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Failed to hang up the provider sessions of ended Speaking role-play {SessionId}", sessionId);
            }
        }
        return attempted;
    }

    /// <summary>Refunds Speaking credit holds whose card/exam was never graded
    /// (abandoned or failed) and commits late-graded ones. Exposed for tests.</summary>
    public async Task<int> SettleStaleCreditHoldsAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var reservations = scope.ServiceProvider.GetService<OetLearner.Api.Services.Ai.IAiCreditReservationService>();
        if (reservations is null) return 0;
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var settled = await SpeakingCreditSettlement.SettleStaleHoldsAsync(db, reservations, DateTimeOffset.UtcNow, ct);
        if (settled > 0)
        {
            logger.LogInformation("Settled {Count} stale Speaking credit holds.", settled);
        }
        return settled;
    }
}
