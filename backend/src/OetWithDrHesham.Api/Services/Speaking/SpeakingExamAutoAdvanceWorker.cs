using Microsoft.EntityFrameworkCore;
using OetWithDrHesham.Api.Data;
using OetWithDrHesham.Api.Domain;

namespace OetWithDrHesham.Api.Services.Speaking;

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
/// </summary>
public sealed class SpeakingExamAutoAdvanceWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<SpeakingExamAutoAdvanceWorker> logger) : BackgroundService
{
    private static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(20);

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

    /// <summary>Advances every overdue exam once. Returns the number of exams
    /// whose state changed. Exposed for tests.</summary>
    public async Task<int> SweepOnceAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<SpeakingExamService>();

        var now = DateTimeOffset.UtcNow;
        var active = await db.SpeakingExamSessions
            .Where(e => e.State != SpeakingExamState.Completed
                && e.State != SpeakingExamState.Cancelled
                && e.State != SpeakingExamState.Expired)
            .OrderBy(e => e.UpdatedAt)
            .Take(500)
            .ToListAsync(ct);

        if (active.Count == 0) return 0;

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
}
