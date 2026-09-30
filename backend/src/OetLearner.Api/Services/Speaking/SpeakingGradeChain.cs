using OetLearner.Api.Configuration;
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
/// With no pinned provider configured this is a single unpinned call, exactly the
/// behaviour before the chain existed.
///
/// Control-plane refusals (quota, budget, duplicate or conflicting operation,
/// ungrounded prompt, feature policy) and caller cancellation are never failed over:
/// a second provider cannot fix them. If both levels fail the LAST exception is
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
            return await gateway.CompleteAsync(
                template with { Provider = pinnedProvider, Model = options!.PinnedModel?.Trim() ?? string.Empty },
                ct);
        }
        catch (Exception ex) when (IsFailoverable(ex, ct))
        {
            // Class code only: provider text is captured (redacted) by the gateway's own log line.
            logger.LogWarning(
                "Speaking grading via pinned provider {ProviderCode} failed ({ErrorClass}); falling back to the default route.",
                pinnedProvider, AiProviderErrorParser.Classify(ex).ToCode());
        }

        return await gateway.CompleteAsync(template, ct);
    }

    /// <summary>True for any provider-side failure; false for control-plane refusals and caller cancellation.</summary>
    internal static bool IsFailoverable(Exception ex, CancellationToken ct)
        => ex is not (
            AiQuotaDeniedException
            or AiBudgetExhaustedException
            or AiOperationDuplicateResultUnavailableException
            or AiOperationConflictException
            or AiOperationInFlightException
            or PromptNotGroundedException
            or AiFeaturePolicyRefusedException
            or MockAssessmentForbiddenException)
           && !(ex is OperationCanceledException && ct.IsCancellationRequested);
}
