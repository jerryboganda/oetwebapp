using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;

namespace OetLearner.Api.Services.Writing;

/// <summary>
/// WAI-05 — QA-only per-learner fault switch (owner-approved 2 Oct 2026, off by default, no seed).
/// Lets the live QA harness prove failover and Retry on ONE named learner without spending anything:
/// <list type="bullet">
///   <item><c>writing_grade_fault:{userId}</c> — every hop of the run fails;</item>
///   <item><c>writing_grade_fault_l1l2:{userId}</c> — Max and the API fail, Codex serves for real.</item>
/// </list>
/// Created in Admin › Feature Flags (audited there). <c>RolloutPercentage</c> 1-5 = the first N runs of
/// each submission fail (0 ⇒ 1, capped at 5), so run N+1 — the Retry — grades even with the flag on.
/// While a flag is active the learner's failed runs are not re-queued automatically, so a real
/// <c>failed</c> row and its Retry are what gets proven. A failed hop throws before any provider call:
/// no usage row, no circuit or quota side effect. Fails closed: an absent, disabled, unreadable or
/// day-old flag is off. The key embeds the server-side submission owner, never request data.
/// Removal = delete this file, its DI line and the two pipeline call sites.
/// </summary>
public sealed class WritingQaFault(LearnerDbContext db, TimeProvider clock, ILogger<WritingQaFault> logger)
{
    public const int MaxRuns = 5;
    internal static readonly TimeSpan StaleAfter = TimeSpan.FromHours(24);

    internal static string AllHopsKey(string userId) => $"writing_grade_fault:{userId}";
    internal static string L1L2Key(string userId) => $"writing_grade_fault_l1l2:{userId}";

    public sealed record Fault(bool AllHops, int Runs)
    {
        /// <param name="run">1-based grading run of the submission (its GradeEpoch).</param>
        public bool ShouldFailHop(WritingGradeHop hop, int run)
            => run <= Runs && (AllHops || hop != WritingGradeHop.Codex);
    }

    /// <summary>The learner's active QA fault, or null.</summary>
    public async Task<Fault?> ReadAsync(string userId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userId)) return null;
        var allHops = AllHopsKey(userId);
        var l1l2 = L1L2Key(userId);
        try
        {
            var freshSince = clock.GetUtcNow() - StaleAfter;
            var flags = await db.FeatureFlags.AsNoTracking()
                .Where(f => (f.Key == allHops || f.Key == l1l2) && f.Enabled && f.UpdatedAt >= freshSince)
                .ToListAsync(ct);
            if (flags.Count == 0) return null;
            var flag = flags.FirstOrDefault(f => f.Key == allHops) ?? flags[0];
            return new Fault(flag.Key == allHops, Math.Clamp(flag.RolloutPercentage, 1, MaxRuns));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Type only: never the message, which can carry connection details.
            logger.LogWarning(
                "Writing QA fault switch ignored for user {UserId}: the flag could not be read ({ErrorType}).",
                userId, ex.GetType().Name);
            return null;
        }
    }
}
