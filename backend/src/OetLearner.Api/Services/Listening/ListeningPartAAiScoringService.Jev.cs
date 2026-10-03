using OetLearner.Api.Domain;
using OetLearner.Api.Services.Ai.TypeSafe;

namespace OetLearner.Api.Services.Listening;

// ═════════════════════════════════════════════════════════════════════════════
// Listening Part A advisory review - Jev (TypeSafe) half.
//
// When TypeSafe:Enabled && TypeSafe:ListeningGapVerdictEnabled, the per-gap
// advisory verdicts come from ONE Jev Choice call per attempt (JevListeningGaps)
// INSTEAD of the Claude/UBAG call. Everything else is unchanged: evidence gating
// (items are already filtered to gaps with an effective approved rationale before
// this runs), the durable operation lease, the advisory-only rule and the worker
// registration flag. The rationale persisted next to the verdict stays the stored
// approved rationale - Jev emits no prose.
//
// Fail back, never fail the attempt: flag off, Jev disabled/unavailable/slow,
// low-confidence or invalid answers, or any exception all return null, and the
// caller then runs the existing Claude/UBAG path exactly as before.
// ═════════════════════════════════════════════════════════════════════════════

public sealed partial class ListeningPartAAiScoringService
{
    private sealed record JevServed(ProviderCallOutcome Outcome, string Model);

    private async Task<JevServed?> TryJevGapVerdictsAsync(
        ListeningAttempt attempt,
        IReadOnlyList<GapItem> items,
        string attemptId,
        int attemptNumber,
        CancellationToken ct)
    {
        var options = typeSafeOptions?.Value;
        if (jev is null || !JevListeningGaps.Enabled(options)) return null;

        try
        {
            var advisory = await JevListeningGaps.JudgeAsync(
                jev,
                options!,
                items.Select(i => new JevGapInput(
                    i.Number, i.UserAnswer, i.Canonical, i.Accepted, i.ApprovedRationale, i.CaseSensitive)).ToList(),
                attempt.UserId,
                attemptId,
                attemptNumber,
                ct,
                logger: logger);
            if (advisory is not { Available: true }) return null;

            var byNumber = advisory.Verdicts.GroupBy(v => v.Number).ToDictionary(g => g.Key, g => g.First());
            var verdicts = new List<Verdict>(items.Count);
            foreach (var item in items)
            {
                // A gap Jev did not answer is unusable evidence: use the Claude path for the attempt.
                if (!byNumber.TryGetValue(item.Number, out var v)) return null;
                verdicts.Add(new Verdict(item.Number, v.Verdict, item.ApprovedRationale));
            }

            return new JevServed(ProviderCallOutcome.Succeeded(verdicts, null), advisory.Model ?? options!.Model);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Part A Jev gap verdicts failed for attempt {AttemptId}; falling back to the provider path.", attemptId);
            return null;
        }
    }
}
