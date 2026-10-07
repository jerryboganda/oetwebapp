using OetLearner.Api.Services.AiManagement;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Services.Ai.Review;

/// <summary>Metadata about one shared-runner execution, surfaced through structured logs and metrics.</summary>
public sealed record SharedReviewerRunInfo(
    bool UsedApiFallback,
    string? FallbackReason,
    string? FinalProvider,
    int CodexAttempts,
    TimeSpan QueueWait,
    TimeSpan TotalDuration);

/// <summary>
/// The shared reviewer execution policy for GPT-6.1 Sol / Codex used by BOTH Writing and Speaking.
/// Order: bounded FIFO gate wait → Codex attempt phase (bounded attempts x per-attempt timeout, transient
/// retries only, one bounded wall-clock phase) → automatic fallback to the API reviewer route → the caller's
/// own failure semantics (Writing holds the letter; Speaking keeps the primary grade) only after BOTH fail.
/// Never waits for a quota reset: gate saturation, quota/auth/unavailable, timeout and retry exhaustion all
/// route to the API provider inside their own bounded budgets.
/// </summary>
public static class SharedReviewerRunner
{
    /// <summary>
    /// Runs <paramref name="codexAttempt"/> through the shared gate and <paramref name="apiFallback"/> on any
    /// bounded-policy exhaustion. Control-plane refusals (local quota, budget, feature policy, grounding,
    /// mock ban) and the caller's own cancellation are never failed over. Transient provider failures
    /// (timeout, network, overloaded, rate-limit, server error) retry once inside the Codex phase before the
    /// fallback; permanent ones (provider quota, auth, invalid request) fall back immediately.
    /// </summary>
    public static async Task<(T Value, SharedReviewerRunInfo Info)> RunAsync<T>(
        SharedReviewerOptions options,
        CodexReviewerGate gate,
        string assessmentType,
        string assessmentId,
        Func<CancellationToken, Task<T>> codexAttempt,
        Func<CancellationToken, Task<T>> apiFallback,
        ILogger logger,
        CancellationToken ct)
    {
        var started = DateTimeOffset.UtcNow;
        var metrics = ReviewerQueueMetrics.Instance;
        string? fallbackReason = null;
        Exception? codexLast = null;
        var codexAttempts = 0;
        var queueWait = TimeSpan.Zero;

        // 1. Bounded FIFO gate wait → straight to the API fallback when saturated.
        (CodexReviewerPermit? permit, TimeSpan wait) = await TryEnterGateAsync(assessmentType, assessmentId, gate, ct);
        queueWait = wait;

        using (permit)
        {
            if (permit is not null)
            {
                var (completed, value, last, attempts) = await RunCodexPhaseAsync();
                codexAttempts = attempts;
                if (completed)
                {
                    metrics.ReviewCompleted(assessmentType, usedApiFallback: false, (long)(DateTimeOffset.UtcNow - started).TotalMilliseconds);
                    logger.LogInformation(
                        "reviewer.codex.success assessmentType={AssessmentType} assessmentId={AssessmentId} queueWaitMs={QueueWaitMs} codexAttempts={CodexAttempts} durationMs={DurationMs}",
                        assessmentType, assessmentId, (long)queueWait.TotalMilliseconds, codexAttempts,
                        (long)(DateTimeOffset.UtcNow - started).TotalMilliseconds);
                    return (value!, new SharedReviewerRunInfo(false, null, "writing-codex-sub", codexAttempts, queueWait, DateTimeOffset.UtcNow - started));
                }

                codexLast = last;
                fallbackReason = last is null ? "codex_budget_exhausted" : ReasonOf(last);
                metrics.ApiFallbackStarted(assessmentType, fallbackReason);
                logger.LogWarning(
                    "reviewer.codex.fallback assessmentType={AssessmentType} assessmentId={AssessmentId} reason={Reason} codexAttempts={CodexAttempts} errorClass={ErrorClass}",
                    assessmentType, assessmentId, fallbackReason, codexAttempts, ErrorClassOf(last));
            }
            else
            {
                fallbackReason = "gate_saturated";
                metrics.ApiFallbackStarted(assessmentType, fallbackReason);
                logger.LogWarning(
                    "reviewer.gate.saturated assessmentType={AssessmentType} assessmentId={AssessmentId} waitMs={WaitMs}",
                    assessmentType, assessmentId, (long)queueWait.TotalMilliseconds);
            }

            // 2. API fallback with its own bounded budget — it never waits on Codex again.
            var apiAttempts = 0;
            Exception? apiLast = null;
            for (var attempt = 0; attempt < Math.Max(1, options.ApiAttempts); attempt++)
            {
                apiAttempts++;
                using var apiTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(1, options.ApiAttemptSeconds)));
                using var apiLinked = CancellationTokenSource.CreateLinkedTokenSource(ct, apiTimeout.Token);
                try
                {
                    var fallbackValue = await apiFallback(apiLinked.Token);
                    metrics.ReviewCompleted(assessmentType, usedApiFallback: true, (long)(DateTimeOffset.UtcNow - started).TotalMilliseconds);
                    logger.LogInformation(
                        "reviewer.api_fallback.success assessmentType={AssessmentType} assessmentId={AssessmentId} reason={Reason} fallbackProvider={Provider} durationMs={DurationMs}",
                        assessmentType, assessmentId, fallbackReason, options.ApiFallbackProvider,
                        (long)(DateTimeOffset.UtcNow - started).TotalMilliseconds);
                    return (fallbackValue, new SharedReviewerRunInfo(true, fallbackReason, options.ApiFallbackProvider, codexAttempts, queueWait, DateTimeOffset.UtcNow - started));
                }
                catch (Exception ex) when (IsFailoverable(ex, ct))
                {
                    apiLast = ex;
                    logger.LogWarning(
                        "reviewer.api_fallback.attempt_failed assessmentType={AssessmentType} assessmentId={AssessmentId} attempt={Attempt} errorClass={ErrorClass}",
                        assessmentType, assessmentId, apiAttempts, ErrorClassOf(ex));
                }
            }

            metrics.ReviewCompleted(assessmentType, usedApiFallback: true, (long)(DateTimeOffset.UtcNow - started).TotalMilliseconds);
            throw new InvalidOperationException(
                $"Shared reviewer failed on Codex ({fallbackReason}) and on the API fallback.",
                codexLast is null && apiLast is null
                    ? null
                    : new AggregateException(
                        (codexLast is null ? Array.Empty<Exception>() : new[] { codexLast })
                            .Concat(apiLast is null ? Array.Empty<Exception>() : new[] { apiLast })
                            .ToArray()));
        }

        async Task<(CodexReviewerPermit? Permit, TimeSpan Wait)> TryEnterGateAsync(
            string type, string id, CodexReviewerGate g, CancellationToken token)
        {
            var waitStart = DateTimeOffset.UtcNow;
            try
            {
                var p = await g.WaitAsync(type, id, token);
                return (p, DateTimeOffset.UtcNow - waitStart);
            }
            catch (ReviewerCapacitySaturatedException)
            {
                return (null, DateTimeOffset.UtcNow - waitStart);
            }
        }

        async Task<(bool Completed, T? Value, Exception? Last, int Attempts)> RunCodexPhaseAsync()
        {
            var phaseDeadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(Math.Max(1, options.CodexBudgetSeconds));
            Exception? phaseLast = null;
            var attempts = 0;
            for (var i = 0; i < Math.Max(1, options.CodexAttempts); i++)
            {
                var remaining = phaseDeadline - DateTimeOffset.UtcNow;
                // No time left in the phase: go to the API fallback rather than start an attempt that cannot finish.
                if (remaining <= TimeSpan.Zero) break;
                attempts++;

                using var phaseToken = CancellationTokenSource.CreateLinkedTokenSource(ct);
                phaseToken.CancelAfter(remaining);
                using var attemptTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(1, options.CodexAttemptSeconds)));
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, phaseToken.Token, attemptTimeout.Token);
                try
                {
                    var v = await codexAttempt(linked.Token);
                    metrics.CodexOutcome(assessmentType, "success");
                    return (true, v, null, attempts);
                }
                catch (Exception ex) when (IsFailoverable(ex, ct))
                {
                    phaseLast = ex;
                    metrics.CodexOutcome(assessmentType, OutcomeOf(ex));
                    logger.LogWarning(
                        "reviewer.codex.attempt_failed assessmentType={AssessmentType} assessmentId={AssessmentId} attempt={Attempt} errorClass={ErrorClass}",
                        assessmentType, assessmentId, attempts, ErrorClassOf(ex));

                    if (IsPermanent(ex)) break;
                    if (i + 1 < options.CodexAttempts && options.CodexRetryDelaySeconds > 0)
                    {
                        try { await Task.Delay(TimeSpan.FromSeconds(options.CodexRetryDelaySeconds), ct); }
                        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                    }
                }
            }

            return (false, default, phaseLast, attempts);
        }
    }

    private static string ReasonOf(Exception ex) => ex switch
    {
        OperationCanceledException => "codex_timeout",
        _ => ErrorClassOf(ex) switch
        {
            "quota_exhausted" => "codex_quota",
            "auth" => "codex_unavailable",
            "invalid_request" => "codex_invalid_request",
            "overloaded" or "rate_limited" or "server_error" => "codex_unavailable",
            "network" => "codex_unavailable",
            _ => "codex_failed",
        },
    };

    private static string ErrorClassOf(Exception? ex)
        => ex is null ? "none" : ex is OperationCanceledException ? "timeout" : AiProviderErrorParser.Classify(ex).ToCode();

    private static string OutcomeOf(Exception ex) => ex switch
    {
        OperationCanceledException => "timeout",
        _ => ErrorClassOf(ex) switch
        {
            "quota_exhausted" => "quota",
            "auth" or "invalid_request" or "network" or "server_error" or "overloaded" or "rate_limited" => "unavailable",
            _ => "other",
        },
    };

    private static bool IsPermanent(Exception ex)
        => ErrorClassOf(ex) is "quota_exhausted" or "auth" or "invalid_request";

    private static bool IsFailoverable(Exception ex, CancellationToken ct)
        => ex is not (
            AiQuotaDeniedException
            or AiBudgetExhaustedException
            or AiFeaturePolicyRefusedException
            or PromptNotGroundedException
            or MockAssessmentForbiddenException)
           && !(ex is OperationCanceledException && ct.IsCancellationRequested);
}


