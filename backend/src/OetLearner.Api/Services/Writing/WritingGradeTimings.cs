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
    /// The lease now bounds the whole run: primary chain AND secondary reviewer. The reviewer's
    /// stage budget is clamped to <c>ClaimedAt + StaleClaimLease - margin</c>
    /// (<see cref="WritingGradeChainOptions.ReviewLeaseMarginSeconds"/>), so a slow primary can starve
    /// the review into a hold, but a live review can never be reclaimed from under itself.
    /// </summary>
    public static readonly TimeSpan StaleClaimLease = TimeSpan.FromMinutes(25);

    /// <summary>
    /// Deliberate result-release window (owner handoff, 6 Oct 2026): a normal candidate's
    /// final Writing assessment is released at <c>SubmittedAt + ResultReleaseWindow</c>, and only
    /// once the grade AND the secondary review are complete. Derived at read time from the
    /// already-persisted <c>SubmittedAt</c>, so refresh, app close and device restart can never
    /// reset it. See <see cref="WritingResultRelease"/>.
    /// </summary>
    public static readonly TimeSpan ResultReleaseWindow = TimeSpan.FromMinutes(15);
}
