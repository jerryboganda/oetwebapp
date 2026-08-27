using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Listening;

// ═════════════════════════════════════════════════════════════════════════════
// ListeningPartAAiScoringWorker.
//
// Polls for SUBMITTED attempts that still have Part A short-answer answers
// eligible for the AI advisory review and runs ListeningPartAAiScoringService on
// them. Mirrors the existing hosted-worker pattern (ListeningTtsJobWorker):
// scoped per cycle, capped batch, best-effort.
//
// W0 (2026-08-27, incident INC-2026-CLAUDE-01): eligibility is no longer just
// "AiScoredAt is null". A row must also be non-terminal (no AiSkipReason), under
// the durable attempt cap, and past any scheduled backoff — otherwise the same
// attempt was re-selected every 20 s in BOTH API slots, for ever.
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
                await RunOnceAsync(stoppingToken);
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

    /// <summary>
    /// The single definition of "this attempt still has AI advisory work to do".
    /// Shared by the worker loop and its tests so the eligibility contract cannot
    /// drift: not already AI-scored, not terminally skipped, under the durable
    /// attempt cap, and not scheduled into the future.
    ///
    /// The result is deterministically ordered oldest-work-first — earliest due
    /// instant (<c>AiNextAttemptAt</c> when a backoff is armed, otherwise the
    /// attempt's submission instant, falling back to its start) with the attempt
    /// id as a stable tie-break. Without an explicit ORDER BY, the batch
    /// <c>Take</c> below would slice an unordered set, so a backlog larger than
    /// one batch could starve the oldest candidate indefinitely and the two API
    /// slots could disagree about which rows they picked. The GROUP BY /
    /// MIN(COALESCE(...)) shape is deliberate: it is EF-translatable to a single
    /// SQL statement (no client-side evaluation) and it de-duplicates the
    /// attempt ids that the answer-level join produces, replacing the previous
    /// <c>Distinct()</c> — which cannot be combined with an ORDER BY over a
    /// column outside the projection.
    /// </summary>
    public static IQueryable<string> EligibleAttemptIds(LearnerDbContext db, DateTimeOffset now)
        => from a in db.ListeningAnswers
           join q in db.ListeningQuestions on a.ListeningQuestionId equals q.Id
           join at in db.ListeningAttempts on a.ListeningAttemptId equals at.Id
           where a.AiScoredAt == null
               && a.AiSkipReason == null
               && a.AiAttemptCount < ListeningPartAAiRetryPolicy.MaxAttempts
               && (a.AiNextAttemptAt == null || a.AiNextAttemptAt <= now)
               && at.Status == ListeningAttemptStatus.Submitted
               && (q.QuestionType == ListeningQuestionType.ShortAnswer
                   || q.QuestionType == ListeningQuestionType.FillInBlank)
           group new { a.AiNextAttemptAt, at.SubmittedAt, at.StartedAt }
               by a.ListeningAttemptId into g
           orderby g.Min(x => x.AiNextAttemptAt ?? x.SubmittedAt ?? x.StartedAt), g.Key
           select g.Key;

    /// <summary>Process one batch. Returns the number of attempts handed to the
    /// scorer. Also the test seam, matching <c>AiAccountQuotaResetWorker</c>.</summary>
    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var scorer = scope.ServiceProvider.GetRequiredService<IListeningPartAAiScoringService>();
        var clock = scope.ServiceProvider.GetService<TimeProvider>() ?? TimeProvider.System;

        // EligibleAttemptIds is already ordered oldest-due-first with a stable id
        // tie-break, so this batch slice is deterministic and cannot starve the
        // oldest backlog entry.
        var attemptIds = await EligibleAttemptIds(db, clock.GetUtcNow())
            .Take(BatchAttempts)
            .ToListAsync(ct);

        var processed = 0;
        foreach (var id in attemptIds)
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                await scorer.ScoreAttemptAsync(id, ct);
                processed++;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Part A AI scoring failed for attempt {AttemptId}.", id);
            }
        }

        return processed;
    }
}
