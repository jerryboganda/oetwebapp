using OetLearner.Api.Configuration;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Ai;
using OetLearner.Api.Services.AiManagement;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Services.Speaking;

/// <summary>
/// Owner directive 2026-09-30 — the Speaking grading chain shared by both
/// <c>speaking.grade</c> gateway call sites (classic and v1.1 assessors).
///
/// Level 1 pins the configured provider and model (the Claude Max subscription
/// sidecar); on a provider failure level 2 replays the ORIGINAL unpinned request, i.e.
/// today's default route, so grading works again the moment that route recovers.
/// The configured pin can never be blank (<see cref="SpeakingGradingOptions"/> resolves blank to
/// Max, RULE MAX-ALWAYS-ON); only null options (assessors built without DI, i.e. tests) make a
/// single unpinned call.
///
/// Level 1 runs under ONE wall-clock budget (<see cref="SpeakingGradingOptions.PinnedTimeoutSeconds"/>,
/// default 900 s) that covers the sidecar's queue wait, the CLI run and the gateway's own
/// provider retries. The Anthropic adapter's HttpClient allows 30 minutes per attempt, the sidecar
/// has no queue timeout and the operation lease is 30 minutes, so without a budget a wedged
/// sidecar lane would outlive the lease before level 2 ever got a turn. Expiry counts as a
/// provider failure (level 2 runs); the caller's own cancellation is never swallowed.
///
/// Control-plane refusals (quota, budget, duplicate or conflicting operation,
/// ungrounded prompt, feature policy) and caller cancellation are never failed over:
/// a second provider cannot fix them. The one exception is a duplicate refusal of the
/// PINNED operation while it is Indeterminate (a dropped connection or timeout left its
/// outcome unknown): the coordinator never re-runs such an operation, but level 2 is a
/// different provider under a different operation identity, so it is still tried. Without
/// that, one transient sidecar failure would lock the session out of both routes for good.
/// If both levels fail the LAST exception is
/// rethrown, so the assessor's existing catch still maps it to
/// <c>409 speaking_ai_unavailable</c>. Learner credit is untouched here: the gateway
/// never debits Speaking, and the hold is committed once by the settlement after a grade
/// exists.
/// </summary>
public static class SpeakingGradeChain
{
    public static async Task<AiGatewayResult> CompleteAsync(
        IAiGatewayService gateway,
        AiGatewayRequest template,
        SpeakingGradingOptions? options,
        ILogger logger,
        CancellationToken ct)
    {
        var pinnedProvider = options?.PinnedProviderCode?.Trim();
        if (string.IsNullOrEmpty(pinnedProvider))
            return await gateway.CompleteAsync(template, ct);

        try
        {
            // One wall-clock budget for the WHOLE pinned call. Ending it with a linked token (not the
            // 30-minute HttpClient timeout) also makes the coordinator record level 1 as Cancelled
            // (replayable) instead of Indeterminate (never replayed at the same identity).
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
            if (PinnedBudget(options!) is { } window)
                budget.CancelAfter(window);
            return await gateway.CompleteAsync(
                template with { Provider = pinnedProvider, Model = options!.PinnedModel?.Trim() ?? string.Empty },
                budget.Token);
        }
        catch (Exception ex) when (IsFailoverable(ex, ct))
        {
            // Class code only: provider text is captured (redacted) by the gateway's own log line.
            // IsFailoverable already excluded the caller's own cancellation, so any other
            // OperationCanceledException here is the pinned budget (or an HttpClient timeout).
            logger.LogWarning(
                "Speaking grading via pinned provider {ProviderCode} failed ({ErrorClass}); falling back to the default route.",
                pinnedProvider, FailureClass(ex));
        }

        return await gateway.CompleteAsync(template, ct);
    }

    /// <summary>Whole-chain ceiling for an owner-configured order: keeps every hop plus the reviewer inside the operation lease.</summary>
    internal static readonly TimeSpan ConfiguredChainCeiling = TimeSpan.FromSeconds(1500);

    /// <summary>
    /// Owner directive 2026-10-09: runs the owner-SAVED order (AI Pipeline Control Center) when a store is available,
    /// otherwise the legacy pin-then-default-route behaviour above. Each enabled, usable hop is pinned in turn (one
    /// attempt each: a Speaking request has no per-attempt slot) under its own time limit and the shared ceiling.
    /// The saved order is read once per grade and never changed by what the grade observes. Failover rules are the
    /// same as the legacy chain; an empty plan fails the grade rather than falling back to the built-in default.
    /// </summary>
    public static async Task<AiGatewayResult> CompleteAsync(
        IAiGatewayService gateway,
        AiGatewayRequest template,
        SpeakingGradingOptions? options,
        OetLearner.Api.Services.AiPipeline.IAiPipelineStore? store,
        ILogger logger,
        CancellationToken ct,
        // Subscription-account rotation (owner directive 2026-10-10): same hops, permuted inside each
        // engine group per run. Optional: absent = the saved order runs exactly as saved.
        OetLearner.Api.Services.AiPipeline.ISubscriptionAccountPool? accountPool = null,
        OetLearner.Api.Services.AiPipeline.ISubscriptionAccountStateProvider? accountState = null)
    {
        if (store is null)
            return await CompleteAsync(gateway, template, options, logger, ct);

        var plan = await store.ResolvePlanAsync(OetLearner.Api.Services.AiPipeline.AiPipelineStageKeys.SpeakingGrade, ct);
        if (accountPool is not null && accountState is not null)
        {
            plan = await accountPool.OrderAsync(accountState, plan, ct);
        }
        logger.LogInformation(
            "Speaking grading plan: version {Version} ({Source}), hops [{Hops}], skipped [{Skipped}].",
            plan.Version, plan.Source, string.Join(", ", plan.Hops.Select(h => h.Provider)), string.Join("; ", plan.Skipped));
        if (plan.Hops.Count == 0)
            throw new InvalidOperationException($"Speaking grading has no usable provider in the saved order ({string.Join("; ", plan.Skipped)}).");

        var started = DateTimeOffset.UtcNow;
        Exception? last = null;
        foreach (var hop in plan.Hops)
        {
            var remaining = ConfiguredChainCeiling - (DateTimeOffset.UtcNow - started);
            if (remaining < TimeSpan.FromSeconds(20))
                break;
            var window = TimeSpan.FromSeconds(Math.Clamp(hop.BudgetSeconds, 10, SpeakingGradingOptions.MaxPinnedTimeoutSeconds));
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
            budget.CancelAfter(window < remaining ? window : remaining);
            try
            {
                return await gateway.CompleteAsync(
                    template with { Provider = hop.Provider, Model = hop.Model },
                    budget.Token);
            }
            catch (Exception ex) when (IsFailoverable(ex, ct))
            {
                last = ex;
                logger.LogWarning(
                    "Speaking grading via {ProviderCode} failed ({ErrorClass}); trying the next step of the saved order.",
                    hop.Provider, FailureClass(ex));
            }
        }

        throw new InvalidOperationException("Speaking grading failed on every step of the saved order.", last);
    }

    /// <summary>The wall-clock budget for the pinned call: null (no cap) for 0 or less, otherwise the
    /// configured seconds clamped to <see cref="SpeakingGradingOptions.MaxPinnedTimeoutSeconds"/>.</summary>
    internal static TimeSpan? PinnedBudget(SpeakingGradingOptions options)
        => options.PinnedTimeoutSeconds <= 0
            ? null
            : TimeSpan.FromSeconds(Math.Min(options.PinnedTimeoutSeconds, SpeakingGradingOptions.MaxPinnedTimeoutSeconds));

    private static string FailureClass(Exception ex) => ex switch
    {
        OperationCanceledException => "timeout",
        AiOperationDuplicateResultUnavailableException => "operation_indeterminate",
        _ => AiProviderErrorParser.Classify(ex).ToCode(),
    };

    /// <summary>
    /// True for any provider-side failure; false for control-plane refusals and caller cancellation.
    /// A duplicate that resolves to an Indeterminate operation IS failed over: that operation is the pinned
    /// route's own identity (request hash plus provider:model route), it is never replayed, and the default
    /// route is a different identity that can still succeed. Every other duplicate state (in-window Completed,
    /// Leased, ...) stays a refusal so the default route is never paid twice.
    /// </summary>
    internal static bool IsFailoverable(Exception ex, CancellationToken ct)
        => ex is not (
            AiQuotaDeniedException
            or AiBudgetExhaustedException
            or AiOperationDuplicateResultUnavailableException { State: not AiOperationState.Indeterminate }
            or AiOperationConflictException
            or AiOperationInFlightException
            or PromptNotGroundedException
            or AiFeaturePolicyRefusedException
            or MockAssessmentForbiddenException)
           && !(ex is OperationCanceledException && ct.IsCancellationRequested);
}
