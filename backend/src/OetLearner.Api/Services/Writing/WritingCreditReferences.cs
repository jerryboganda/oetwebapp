using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Writing;

/// <summary>
/// The ledger references a Writing letter can be paid under (WAI-01, owner decision
/// 2 Oct 2026: charge ONCE, when the task is opened). The start gate and the grade
/// hold both derive their references here, so the two can never drift apart.
/// </summary>
internal static class WritingCreditReferences
{
    /// <summary>
    /// The start gate's reference: this learner's attempt number
    /// <paramref name="gradedCount"/> at this task. It only advances once an attempt
    /// is graded, so a refresh, resume or retry of the same attempt reuses it.
    /// </summary>
    public static string Start(string? userId, Guid scenarioId, int gradedCount)
        => $"writing-v2:{userId}:{scenarioId:D}:{gradedCount}";

    /// <summary>
    /// A letter paid at grade time under its own reference: a free sample, a historical
    /// revision row (Revise &amp; Resubmit is retired, but old and in-flight rows still grade),
    /// or a second letter written under an attempt whose start reference already pays
    /// for another letter.
    /// </summary>
    public static string Grade(Guid submissionId) => $"writing-grade:{submissionId:N}";

    /// <summary>
    /// Graded, non-mock, non-revision submissions of this learner at this task. Terminal =
    /// graded: a queued, grading or failed letter is still the same resumable attempt, so it
    /// must not advance the count. Mock submissions bill their own mock allowance, and a
    /// historical REVISION row pays its own <see cref="Grade"/> reference at grade time, so one
    /// finishing grading while the learner opens a new attempt can never move that attempt's
    /// start reference (which would charge the new letter a second time).
    /// </summary>
    public static Task<int> GradedCountAsync(LearnerDbContext db, string? userId, Guid scenarioId, CancellationToken ct)
        => string.IsNullOrWhiteSpace(userId)
            ? Task.FromResult(0)
            : db.WritingSubmissions.AsNoTracking()
                .CountAsync(s => s.UserId == userId
                    && s.ScenarioId == scenarioId
                    && s.Mode != "mock"
                    && !s.IsRevision
                    && s.Status == WritingSubmissionStatuses.Graded, ct);
}
