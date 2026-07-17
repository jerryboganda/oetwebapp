using OetWithDrHesham.Api.Domain;
using OetWithDrHesham.Api.Services.Rulebook;

namespace OetWithDrHesham.Api.Services.Ai;

/// <summary>
/// Records <see cref="AiUsageRecord"/> rows for AI calls that do NOT go through
/// <see cref="IAiGatewayService"/> — direct OCR / STT / Anthropic calls. Wraps
/// the scoped <see cref="IAiUsageRecorder"/> behind a singleton-safe scope so it
/// can be injected into singleton providers (Whisper ASR selectors, etc.).
/// <para>
/// Every method is fail-soft: a telemetry failure must never break the learner
/// or admin call that triggered it. Exceptions are logged, not propagated.
/// </para>
/// </summary>
public interface IDirectAiCallRecorder
{
    Task RecordSuccessAsync(
        AiUsageContext context,
        string providerId,
        string model,
        AiUsage? usage,
        int latencyMs,
        string? policyTrace,
        decimal costEstimateUsd,
        CancellationToken ct);

    Task RecordFailureAsync(
        AiUsageContext context,
        string? providerId,
        string? model,
        AiCallOutcome outcome,
        string errorCode,
        string? errorMessage,
        int latencyMs,
        string? policyTrace,
        CancellationToken ct);
}

public sealed class DirectAiCallRecorder(
    IServiceScopeFactory scopeFactory,
    ILogger<DirectAiCallRecorder> logger) : IDirectAiCallRecorder
{
    public async Task RecordSuccessAsync(
        AiUsageContext context,
        string providerId,
        string model,
        AiUsage? usage,
        int latencyMs,
        string? policyTrace,
        decimal costEstimateUsd,
        CancellationToken ct)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var recorder = scope.ServiceProvider.GetRequiredService<IAiUsageRecorder>();
            await recorder.RecordSuccessAsync(
                context, providerId, model, AiKeySource.Platform, usage,
                latencyMs, retryCount: 0, policyTrace, ct, costEstimateUsd: costEstimateUsd);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "DirectAiCallRecorder: failed to record success for feature {Feature} provider {Provider}",
                context.FeatureCode, providerId);
        }
    }

    public async Task RecordFailureAsync(
        AiUsageContext context,
        string? providerId,
        string? model,
        AiCallOutcome outcome,
        string errorCode,
        string? errorMessage,
        int latencyMs,
        string? policyTrace,
        CancellationToken ct)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var recorder = scope.ServiceProvider.GetRequiredService<IAiUsageRecorder>();
            await recorder.RecordFailureAsync(
                context, providerId, model, AiKeySource.Platform, outcome,
                errorCode, errorMessage, latencyMs, retryCount: 0, policyTrace, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "DirectAiCallRecorder: failed to record failure for feature {Feature} provider {Provider}",
                context.FeatureCode, providerId);
        }
    }
}
