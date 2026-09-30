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
/// a session the client never ended is finished, graded and its provider sessions
/// hung up once it is past its hard stop (see <see cref="SweepOverdueRolePlaysAsync"/>).
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

    /// <summary>Advances every overdue exam once, then finishes every overdue standalone
    /// role-play. Returns the number of exams whose state changed. Exposed for tests.</summary>
    public async Task<int> SweepOnceAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        // Exams first: the exam clock ends a child card exactly at its deadline (no grace), so the
        // role-play pass below only ever sees a child the exam pass could not end.
        var changed = await SweepExamsAsync(now, ct);
        try
        {
            await SweepOverdueRolePlaysAsync(now, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Speaking role-play hard-stop sweep failed");
        }
        return changed;
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
        var changed = 0;
        foreach (var exam in active)
        {
            try
            {
                // Expire exams left idle in the unscored Intro.
                if (exam.State == SpeakingExamState.Intro
                    && exam.IntroStartedAt is { } started
                    && now - started > SpeakingExamService.IdleExpiry)
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
    /// The hard server-side cap on an AI role-play the client never ended. A standalone role-play
    /// still Active past <c>RolePlayStartedAt</c> plus the card's (capped) time plus the grace is
    /// finished with a compare-and-swap, handed to canonical grading once, audited, and its OpenAI
    /// sessions are hung up so a dead or hostile client cannot keep one billing. Idempotent:
    /// a finished session is never a candidate again. Returns the number finished. Exposed for tests.
    /// </summary>
    public async Task<int> SweepOverdueRolePlaysAsync(DateTimeOffset now, CancellationToken ct)
    {
        List<string> overdue;
        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            overdue = await FindOverdueRolePlaysAsync(scope.ServiceProvider, now, ct);
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
                // After the state change and the grading hand-off: stop provider-side billing.
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

    private static async Task<List<string>> FindOverdueRolePlaysAsync(
        IServiceProvider services,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var db = services.GetRequiredService<LearnerDbContext>();
        var options = services.GetService<IOptions<LiveVoiceOptions>>()?.Value;

        var query = db.SpeakingSessions.AsNoTracking()
            .Where(s => s.State == SpeakingSessionState.Active && s.Mode != SpeakingSessionMode.LiveTutor);
        if (db.Database.IsNpgsql())
        {
            // Postgres narrows by age in SQL so a backlog of ancient Active rows is never loaded;
            // SQLite cannot compare DateTimeOffset in SQL, so every provider re-applies the age
            // filter in memory below.
            var oldestRelevant = now
                - RolePlaySweepHorizon
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
            if (now >= window.HardStopAt && now - window.HardStopAt <= RolePlaySweepHorizon)
            {
                overdue.Add(candidate.Id);
            }
        }
        return overdue;
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
