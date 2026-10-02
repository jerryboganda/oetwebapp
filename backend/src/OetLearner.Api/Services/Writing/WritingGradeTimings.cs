namespace OetLearner.Api.Services.Writing;

/// <summary>
/// Shared Writing grading timings. One source for the stale-claim lease used by
/// retry-grade, the grading recovery cron and the Post Submissions list, so the
/// three can never disagree about when a row stuck in <c>grading</c> is orphaned.
/// </summary>
public static class WritingGradeTimings
{
    /// <summary>
    /// A row left in <c>grading</c> longer than this is presumed orphaned (its
    /// grader died or was redeployed). Sits above the 20-minute grade-chain
    /// deadline and below the former 30-minute lease, so a live grader always
    /// finishes inside its own deadline before anything reclaims its row.
    /// </summary>
    public static readonly TimeSpan StaleClaimLease = TimeSpan.FromMinutes(25);
}
