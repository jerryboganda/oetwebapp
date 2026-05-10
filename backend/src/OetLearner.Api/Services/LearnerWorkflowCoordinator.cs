using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services;

public static class LearnerWorkflowCoordinator
{
    public static async Task AttachAttemptToDiagnosticAsync(LearnerDbContext db, Attempt attempt, CancellationToken cancellationToken)
    {
        if (!string.Equals(attempt.Context, "diagnostic", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var link = await FindDiagnosticLinkAsync(db, attempt, cancellationToken);
        if (link is null)
        {
            return;
        }

        var (session, subtest) = link.Value;
        subtest.AttemptId = attempt.Id;
        subtest.State = MapDiagnosticState(attempt.State);
        subtest.CompletedAt = subtest.State == AttemptState.Completed
            ? attempt.CompletedAt ?? DateTimeOffset.UtcNow
            : null;

        if (session.State != AttemptState.Completed)
        {
            session.State = AttemptState.InProgress;
        }
    }

    public static async Task UpdateDiagnosticProgressAsync(LearnerDbContext db, Attempt attempt, AttemptState state, CancellationToken cancellationToken)
    {
        if (!string.Equals(attempt.Context, "diagnostic", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var link = await FindDiagnosticLinkAsync(db, attempt, cancellationToken);
        if (link is null)
        {
            return;
        }

        var (session, subtest) = link.Value;
        subtest.AttemptId = attempt.Id;
        subtest.State = MapDiagnosticState(state);
        subtest.CompletedAt = subtest.State == AttemptState.Completed
            ? attempt.CompletedAt ?? DateTimeOffset.UtcNow
            : null;

        var subtests = await db.DiagnosticSubtests
            .Where(x => x.DiagnosticSessionId == session.Id)
            .OrderBy(x => x.SubtestCode)
            .ToListAsync(cancellationToken);

        var activeSubtests = subtests
            .Where(x => !string.Equals(x.SubtestCode, "reading", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var completed = activeSubtests.Count > 0 && activeSubtests.All(x => x.State == AttemptState.Completed);
        if (completed)
        {
            if (session.State != AttemptState.Completed)
            {
                session.State = AttemptState.Completed;
                session.CompletedAt ??= DateTimeOffset.UtcNow;
                db.AnalyticsEvents.Add(new AnalyticsEventRecord
                {
                    Id = $"evt-{Guid.NewGuid():N}",
                    UserId = attempt.UserId,
                    EventName = "diagnostic_completed",
                    PayloadJson = JsonSupport.Serialize(new
                    {
                        diagnosticId = session.Id,
                        attemptId = attempt.Id
                    }),
                    OccurredAt = DateTimeOffset.UtcNow
                });
            }

            return;
        }

        session.State = AttemptState.InProgress;
        session.CompletedAt = null;
    }

    public static async Task QueueStudyPlanRegenerationAsync(LearnerDbContext db, string userId, CancellationToken cancellationToken)
    {
        var plans = await db.StudyPlans
            .Where(x => x.UserId == userId)
            .ToListAsync(cancellationToken);
        var plan = plans
            .OrderByDescending(x => x.GeneratedAt)
            .FirstOrDefault();

        if (plan is null)
        {
            return;
        }

        plan.State = AsyncState.Queued;

        var existingQueuedJob = await db.BackgroundJobs.AnyAsync(
            x => x.Type == JobType.StudyPlanRegeneration
                 && x.ResourceId == plan.Id
                 && (x.State == AsyncState.Queued || x.State == AsyncState.Processing),
            cancellationToken);

        if (existingQueuedJob)
        {
            return;
        }

        db.BackgroundJobs.Add(new BackgroundJobItem
        {
            Id = $"job-{Guid.NewGuid():N}",
            Type = JobType.StudyPlanRegeneration,
            State = AsyncState.Queued,
            ResourceId = plan.Id,
            CreatedAt = DateTimeOffset.UtcNow,
            AvailableAt = DateTimeOffset.UtcNow.AddSeconds(1),
            LastTransitionAt = DateTimeOffset.UtcNow,
            StatusReasonCode = "queued",
            StatusMessage = "Queued",
            Retryable = true,
            RetryAfterMs = 2000
        });
    }

    private static AttemptState MapDiagnosticState(AttemptState state) => state switch
    {
        AttemptState.Completed => AttemptState.Completed,
        AttemptState.Failed => AttemptState.Failed,
        AttemptState.Submitted => AttemptState.Evaluating,
        AttemptState.Evaluating => AttemptState.Evaluating,
        AttemptState.Paused => AttemptState.Paused,
        AttemptState.NotStarted => AttemptState.NotStarted,
        _ => AttemptState.InProgress
    };

    private static async Task<(DiagnosticSession Session, DiagnosticSubtestStatus Subtest)?> FindDiagnosticLinkAsync(
        LearnerDbContext db,
        Attempt attempt,
        CancellationToken cancellationToken)
    {
        var existingSubtest = await db.DiagnosticSubtests
            .FirstOrDefaultAsync(x => x.AttemptId == attempt.Id, cancellationToken);

        if (existingSubtest is not null)
        {
            var existingSession = await db.DiagnosticSessions
                .FirstOrDefaultAsync(x => x.Id == existingSubtest.DiagnosticSessionId && x.UserId == attempt.UserId, cancellationToken);

            if (existingSession is not null)
            {
                return (existingSession, existingSubtest);
            }
        }

        var sessions = await db.DiagnosticSessions
            .Where(x => x.UserId == attempt.UserId && x.State != AttemptState.Completed)
            .ToListAsync(cancellationToken);
        var session = sessions
            .OrderByDescending(x => x.StartedAt)
            .FirstOrDefault();

        if (session is null)
        {
            return null;
        }

        var subtest = await db.DiagnosticSubtests
            .FirstOrDefaultAsync(
                x => x.DiagnosticSessionId == session.Id && x.SubtestCode == attempt.SubtestCode,
                cancellationToken);

        return subtest is null ? null : (session, subtest);
    }
}
