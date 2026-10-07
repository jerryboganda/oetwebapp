using OetLearner.Api.Services.Ai;
using OetLearner.Api.Services.AiManagement;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Services.Writing;

/// <summary>Bound from <c>Writing:GradeChain</c>. Seconds unless named otherwise.</summary>
public sealed class WritingGradeChainOptions
{
    public const string SectionName = "Writing:GradeChain";

    /// <summary>Per attempt on the Claude Max subscription (live p90 is ~85 s; the lane can queue).</summary>
    public int L1AttemptSeconds { get; set; } = 420;

    /// <summary>The one attempt on the Anthropic API.</summary>
    public int L2AttemptSeconds { get; set; } = 150;

    /// <summary>Per attempt on the Codex subscription.</summary>
    public int L3AttemptSeconds { get; set; } = 420;

    /// <summary>Whole-run budget; kept below <see cref="WritingGradeTimings.StaleClaimLease"/>.</summary>
    public int ChainDeadlineSeconds { get; set; } = 1200;

    /// <summary>Automatic re-queues after a retryable failure before the row reads <c>failed</c>; 0 = never.</summary>
    public int MaxAutoRetries { get; set; } = 4;

    /// <summary>Wait before re-queue N (the last value repeats).</summary>
    public int[] BackoffMinutes { get; set; } = [2, 5, 15, 30];

    /// <summary>Attempts per secondary-review pass on the Codex subscription (typed quota/auth skips the second).</summary>
    public int ReviewAttempts { get; set; } = 2;

    /// <summary>Per attempt of a secondary-review pass (the Codex sidecar is one serial lane).</summary>
    public int ReviewAttemptSeconds { get; set; } = 240;

    /// <summary>Ceiling for ONE review pass; the stage budget (lease minus margin) can only lower it.</summary>
    public int ReviewDeadlineSeconds { get; set; } = 600;

    /// <summary>Kept clear of <see cref="WritingGradeTimings.StaleClaimLease"/> so the cron never reclaims a live review.</summary>
    public int ReviewLeaseMarginSeconds { get; set; } = 90;

    /// <summary>
    /// A reviewer outage holds (re-queues) a letter at most this many times; the next failed review completes the letter on
    /// its primary result, flagged for a tutor, instead of re-queuing it again (owner handoff 6 Oct 2026: a reviewer failure must
    /// never leave a letter Queued). It also bounds the whole review stage: a letter that has been re-queued MORE than this many
    /// times skips the review stage entirely. 0 = the first failed review already falls back.
    /// </summary>
    public int ReviewMaxHolds { get; set; } = 2;

    /// <summary>Minutes after submission past which a failed review no longer holds the letter (the 15-minute release window is near).</summary>
    public int ReviewGiveUpMinutes { get; set; } = 9;
}

/// <summary>The three routes of one grading run, in order.</summary>
public enum WritingGradeHop
{
    ClaudeMax = 0,
    ClaudeApi = 1,
    Codex = 2,
}

/// <summary>The provider answered, but not with a complete scoring contract. Fails over like any provider failure.</summary>
public sealed class WritingRubricUnreadableException(string message) : Exception(message);

/// <summary>
/// The Writing grading chain (owner decisions 2 Oct 2026, rule MAX-ALWAYS-ON). EVERY run starts on
/// the Claude Max subscription: L1 Max ×2 → L2 Anthropic API ×1 → L3 Codex (GPT-6.1 Sol) ×2. A later
/// route is tried only inside the same run, after the earlier one actually failed; nothing carries
/// over to the next run or the next learner.
///
/// Each attempt has its own wall-clock budget (min of its hop budget and what is left of the run
/// deadline; under 20 s left = stop) on a linked token, so the coordinator records an expired attempt
/// as Cancelled (replayable) and the run moves on. Each attempt also has its own AI-operation slot
/// (<see cref="ResourceVersion"/>), so a dead or duplicated slot can never be replayed into the next
/// attempt or the next run. Parsing happens inside the attempt: an unreadable answer fails over too.
///
/// Control-plane refusals (quota, budget, feature policy, ungrounded prompt, mock ban) and the
/// caller's own cancellation are never failed over. A typed quota/auth/invalid-request answer only
/// skips the SAME route's second attempt. When every route failed, one generic exception is thrown.
/// </summary>
public static class WritingGradeChain
{
    internal static readonly TimeSpan MinimumAttemptBudget = TimeSpan.FromSeconds(20);

    /// <summary>
    /// Disjoint from the legacy versions (null, 2-10, 1001-1010, 2001-2010); 16 apart so the
    /// coordinator's replay walk (at most 10 bumps) never reaches the next slot.
    /// </summary>
    public static int ResourceVersion(int epoch, WritingGradeHop hop, int attempt)
        => 100_000 + epoch * 256 + (int)hop * 64 + attempt * 16;

    /// <summary>
    /// Slot of one secondary-review attempt. Disjoint from <see cref="ResourceVersion"/> and the legacy
    /// versions (and a different resource type and feature code besides), 16 apart so the coordinator's
    /// replay walk never reaches the next slot: <c>pass</c> is 0..3 (review plus up to two enhanced
    /// rounds), <c>attempt</c> 0..1.
    /// </summary>
    public static int ReviewResourceVersion(int epoch, int pass, int attempt)
        => 5_000_000 + epoch * 256 + pass * 64 + attempt * 16;

    /// <summary>Slot of the API-fallback review attempt (Anthropic row). Disjoint from both the grading
    /// slots and the Codex review slots, so the coordinator never replays the fallback into a Codex slot.</summary>
    public static int ReviewApiResourceVersion(int epoch, int pass)
        => 7_000_000 + epoch * 256 + pass * 64;

    internal sealed record Step(WritingGradeHop Hop, string Provider, string Model, int Attempts, int BudgetSeconds);

    /// <summary>The run plan. The API hop exists only behind the Max hop (a test host's stub
    /// selector routes L1 elsewhere).</summary>
    internal static IReadOnlyList<Step> Plan(WritingSubscriptionDecision decision, WritingGradeChainOptions options)
    {
        var plan = new List<Step> { new(WritingGradeHop.ClaudeMax, decision.ProviderCode, decision.Model, 2, options.L1AttemptSeconds) };
        if (decision.ProviderCode == WritingSubscriptionProviders.Claude)
        {
            plan.Add(new(WritingGradeHop.ClaudeApi, WritingSubscriptionProviders.ClaudeApi, WritingSubscriptionProviders.ClaudeModel, 1, options.L2AttemptSeconds));
        }

        plan.Add(new(WritingGradeHop.Codex, WritingSubscriptionProviders.Codex, WritingSubscriptionProviders.CodexModel, 2, options.L3AttemptSeconds));
        return plan;
    }

    /// <param name="template">The grading request; only Provider, Model and ResourceVersion change per attempt.</param>
    /// <param name="injectFault">QA fault switch (WAI-05): true fails that hop before any provider call.</param>
    public static Task<T> RunAsync<T>(
        IAiGatewayService gateway,
        AiGatewayRequest template,
        WritingSubscriptionDecision decision,
        int epoch,
        Func<AiGatewayResult, T> parse,
        WritingGradeChainOptions options,
        TimeProvider clock,
        ILogger logger,
        Func<WritingGradeHop, bool>? injectFault,
        CancellationToken ct)
        => RunPlanAsync(
            gateway,
            template,
            Plan(decision, options),
            (step, attempt) => ResourceVersion(epoch, step.Hop, attempt),
            parse,
            TimeSpan.FromSeconds(options.ChainDeadlineSeconds),
            clock,
            logger,
            "grading",
            injectFault,
            ct);

    /// <summary>
    /// One pass of the secondary Writing reviewer: a ONE-route plan on the Codex subscription (GPT-6.1 Sol),
    /// never the paid API and never Max (the reviewer is not a grade hop). Same per-attempt budgets, typed
    /// failure handling and slot discipline as <see cref="RunAsync{T}"/>; the pass is bounded by the
    /// smaller of <see cref="WritingGradeChainOptions.ReviewDeadlineSeconds"/> and <paramref name="stageBudget"/>.
    /// Parsing happens inside the attempt, so an unreadable reply fails the attempt over like any failure.
    /// </summary>
    public static Task<T> RunReviewAsync<T>(
        IAiGatewayService gateway,
        AiGatewayRequest template,
        int epoch,
        int pass,
        Func<AiGatewayResult, T> parse,
        WritingGradeChainOptions options,
        TimeSpan stageBudget,
        TimeProvider clock,
        ILogger logger,
        CancellationToken ct)
    {
        var plan = new List<Step>
        {
            new(
                WritingGradeHop.Codex,
                WritingSubscriptionProviders.Codex,
                WritingSubscriptionProviders.CodexModel,
                Math.Max(1, options.ReviewAttempts),
                options.ReviewAttemptSeconds),
        };
        var ceiling = TimeSpan.FromSeconds(options.ReviewDeadlineSeconds);
        return RunPlanAsync(
            gateway,
            template,
            plan,
            (step, attempt) => ReviewResourceVersion(epoch, pass, attempt),
            parse,
            stageBudget < ceiling ? stageBudget : ceiling,
            clock,
            logger,
            "review",
            injectFault: null,
            ct);
    }

    /// <summary>The attempt loop shared by the grade chain and the review pass.</summary>
    private static async Task<T> RunPlanAsync<T>(
        IAiGatewayService gateway,
        AiGatewayRequest template,
        IReadOnlyList<Step> plan,
        Func<Step, int, int> resourceVersionFor,
        Func<AiGatewayResult, T> parse,
        TimeSpan runBudget,
        TimeProvider clock,
        ILogger logger,
        string stage,
        Func<WritingGradeHop, bool>? injectFault,
        CancellationToken ct)
    {
        var deadline = clock.GetUtcNow() + runBudget;
        Exception? last = null;
        foreach (var step in plan)
        {
            for (var attempt = 0; attempt < step.Attempts; attempt++)
            {
                var remaining = deadline - clock.GetUtcNow();
                if (remaining < MinimumAttemptBudget)
                {
                    throw new InvalidOperationException("Writing grading reached its run deadline.", last);
                }

                var budget = TimeSpan.FromSeconds(Math.Max(1, step.BudgetSeconds));
                using var timeout = new CancellationTokenSource(budget < remaining ? budget : remaining, clock);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
                try
                {
                    if (injectFault?.Invoke(step.Hop) == true)
                    {
                        logger.LogWarning(
                            "QA fault switch failed Writing grading hop {Hop} for learner {UserId} before any provider call.",
                            step.Hop, template.UserId);
                        throw new HttpRequestException(HttpRequestError.ConnectionError, "QA fault switch (no provider call).");
                    }

                    var result = await gateway.CompleteAsync(
                        template with
                        {
                            Provider = step.Provider,
                            Model = step.Model,
                            ResourceVersion = resourceVersionFor(step, attempt),
                        },
                        linked.Token);
                    return parse(result);
                }
                catch (Exception ex) when (IsFailoverable(ex, ct))
                {
                    last = ex;
                    var typed = AiProviderErrorParser.FindHttpException(ex)?.ErrorClass;
                    // Class code only: provider text is logged (redacted) by the gateway itself.
                    logger.LogWarning(
                        "Writing {Stage} hop {Hop} attempt {Attempt} via {Provider} failed ({ErrorClass}).",
                        stage, step.Hop, attempt + 1, step.Provider, FailureClass(ex));
                    if (typed is AiProviderErrorClass.QuotaExhausted or AiProviderErrorClass.Auth or AiProviderErrorClass.InvalidRequest)
                    {
                        break; // the same route cannot help this run; the next route still can
                    }
                }
            }
        }

        // One generic failure for the whole run; a raw slot conflict must not escape to a version walk.
        throw new InvalidOperationException("Writing grading failed on every route.", last);
    }

    /// <summary>True for any provider-side failure, including a duplicate/conflicting/in-flight
    /// operation slot (each attempt owns a fresh slot, so the next one is new work).</summary>
    internal static bool IsFailoverable(Exception ex, CancellationToken ct)
        => ex is not (
            AiQuotaDeniedException
            or AiBudgetExhaustedException
            or AiFeaturePolicyRefusedException
            or PromptNotGroundedException
            or MockAssessmentForbiddenException)
           && !(ex is OperationCanceledException && ct.IsCancellationRequested);

    private static string FailureClass(Exception ex) => ex switch
    {
        OperationCanceledException => "timeout",
        WritingRubricUnreadableException => "unreadable",
        AiOperationDuplicateResultUnavailableException or AiOperationConflictException or AiOperationInFlightException => "operation_slot",
        _ => AiProviderErrorParser.Classify(ex).ToCode(),
    };
}
