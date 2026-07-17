using Microsoft.EntityFrameworkCore;
using OetWithDrHesham.Api.Data;
using OetWithDrHesham.Api.Domain;

namespace OetWithDrHesham.Api.Services.Listening;

// ═════════════════════════════════════════════════════════════════════════════
// ListeningPartAAiScoringWorker.
//
// Polls for SUBMITTED attempts that still have un-AI-scored Part A short-answer
// answers and runs ListeningPartAAiScoringService on them. Mirrors the existing
// hosted-worker pattern (ListeningTtsJobWorker): scoped per cycle, capped batch,
// best-effort. Idempotency is the AiScoredAt guard, so re-processing is safe and
// crash-free; a provider outage simply leaves answers for the next pass.
//
// Registered only when Listening:PartAAiScoring:Enabled is true (see Program.cs),
// so the AI marking is an explicit opt-in and never runs in tests/CI.
// ═════════════════════════════════════════════════════════════════════════════

public sealed class ListeningPartAAiScoringWorker(
    IServiceProvider services,
    ILogger<ListeningPartAAiScoringWorker> logger) : BackgroundService
{
    private const int BatchAttempts = 10;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("ListeningPartAAiScoringWorker started.");
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unhandled error in ListeningPartAAiScoringWorker loop.");
            }

            await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
        logger.LogInformation("ListeningPartAAiScoringWorker stopping.");
    }

    private async Task ProcessBatchAsync(CancellationToken ct)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var scorer = scope.ServiceProvider.GetRequiredService<IListeningPartAAiScoringService>();

        var attemptIds = await (
            from a in db.ListeningAnswers
            join q in db.ListeningQuestions on a.ListeningQuestionId equals q.Id
            join at in db.ListeningAttempts on a.ListeningAttemptId equals at.Id
            where a.AiScoredAt == null
                && at.Status == ListeningAttemptStatus.Submitted
                && (q.QuestionType == ListeningQuestionType.ShortAnswer
                    || q.QuestionType == ListeningQuestionType.FillInBlank)
            select a.ListeningAttemptId)
            .Distinct()
            .Take(BatchAttempts)
            .ToListAsync(ct);

        foreach (var id in attemptIds)
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                await scorer.ScoreAttemptAsync(id, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Part A AI scoring failed for attempt {AttemptId}.", id);
            }
        }
    }
}
