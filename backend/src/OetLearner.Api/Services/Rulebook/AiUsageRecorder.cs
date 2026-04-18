using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Rulebook;

/// <summary>
/// Persists one <see cref="AiUsageRecord"/> per AI gateway call, regardless
/// of outcome. The recorder is deliberately fail-soft: an exception here must
/// never prevent the caller from receiving the completion (or the original
/// gateway exception). Failures are logged, not propagated.
///
/// <para>
/// Call sites never touch the recorder directly — the gateway invokes it. The
/// only public surface for feature code is <see cref="IAiGatewayService"/>.
/// </para>
///
/// <para>
/// Policy: we store hashes of prompts, not bodies. See
/// <c>docs/AI-USAGE-POLICY.md</c> §8 for the body-retention option.
/// </para>
/// </summary>
public interface IAiUsageRecorder
{
    /// <summary>Record a successful call. <paramref name="usage"/> may be null
    /// if the provider did not report token counts; in that case zeros are
    /// persisted and the caller is expected to log a warning.</summary>
    Task RecordSuccessAsync(
        AiUsageContext context,
        string providerId,
        string model,
        AiKeySource keySource,
        AiUsage? usage,
        int latencyMs,
        int retryCount,
        string? policyTrace,
        CancellationToken ct);

    /// <summary>Record a call that did not succeed. <paramref name="outcome"/>
    /// must not be <see cref="AiCallOutcome.Success"/>.</summary>
    Task RecordFailureAsync(
        AiUsageContext context,
        string? providerId,
        string? model,
        AiKeySource keySource,
        AiCallOutcome outcome,
        string errorCode,
        string? errorMessage,
        int latencyMs,
        int retryCount,
        string? policyTrace,
        CancellationToken ct);
}

/// <summary>
/// All the contextual data the recorder needs. Assembled by the gateway from
/// the incoming <c>AiGatewayRequest</c> and the grounded prompt. Passing a
/// single struct keeps the recorder signature stable as more fields appear
/// in later slices (tenant, prompt template version, etc.).
/// </summary>
public readonly record struct AiUsageContext(
    string? UserId,
    string? AuthAccountId,
    string? TenantId,
    string FeatureCode,
    string? RulebookVersion,
    string? PromptTemplateId,
    string? SystemPrompt,
    string? UserPrompt,
    DateTimeOffset StartedAt);

public sealed class AiUsageRecorder(LearnerDbContext db, ILogger<AiUsageRecorder> logger) : IAiUsageRecorder
{
    public Task RecordSuccessAsync(
        AiUsageContext context,
        string providerId,
        string model,
        AiKeySource keySource,
        AiUsage? usage,
        int latencyMs,
        int retryCount,
        string? policyTrace,
        CancellationToken ct)
        => PersistAsync(
            context,
            providerId,
            model,
            keySource,
            AiCallOutcome.Success,
            errorCode: null,
            errorMessage: null,
            usage: usage,
            latencyMs: latencyMs,
            retryCount: retryCount,
            policyTrace: policyTrace,
            ct);

    public Task RecordFailureAsync(
        AiUsageContext context,
        string? providerId,
        string? model,
        AiKeySource keySource,
        AiCallOutcome outcome,
        string errorCode,
        string? errorMessage,
        int latencyMs,
        int retryCount,
        string? policyTrace,
        CancellationToken ct)
    {
        if (outcome == AiCallOutcome.Success)
        {
            throw new ArgumentException("RecordFailureAsync must not be used for successful calls.", nameof(outcome));
        }

        return PersistAsync(
            context,
            providerId,
            model,
            keySource,
            outcome,
            errorCode,
            errorMessage,
            usage: null,
            latencyMs: latencyMs,
            retryCount: retryCount,
            policyTrace: policyTrace,
            ct);
    }

    private async Task PersistAsync(
        AiUsageContext context,
        string? providerId,
        string? model,
        AiKeySource keySource,
        AiCallOutcome outcome,
        string? errorCode,
        string? errorMessage,
        AiUsage? usage,
        int latencyMs,
        int retryCount,
        string? policyTrace,
        CancellationToken ct)
    {
        try
        {
            var createdAt = context.StartedAt == default ? DateTimeOffset.UtcNow : context.StartedAt;

            var record = new AiUsageRecord
            {
                Id = Guid.NewGuid().ToString("N"),
                UserId = context.UserId,
                AuthAccountId = context.AuthAccountId,
                TenantId = context.TenantId,
                FeatureCode = string.IsNullOrWhiteSpace(context.FeatureCode)
                    ? AiFeatureCodes.Unclassified
                    : context.FeatureCode,
                ProviderId = providerId,
                Model = model,
                KeySource = keySource,
                RulebookVersion = context.RulebookVersion,
                PromptTemplateId = context.PromptTemplateId,
                SystemPromptHash = HashOrNull(context.SystemPrompt),
                UserPromptHash = HashOrNull(context.UserPrompt),
                PromptTokens = usage?.PromptTokens ?? 0,
                CompletionTokens = usage?.CompletionTokens ?? 0,
                // CostEstimateUsd is wired in Slice 5 when the provider registry
                // carries a rate card. Until then we persist 0 and rely on
                // token counts for admin analytics.
                CostEstimateUsd = 0m,
                Outcome = outcome,
                ErrorCode = Truncate(errorCode, 64),
                ErrorMessage = Truncate(errorMessage, 512),
                LatencyMs = latencyMs,
                RetryCount = retryCount,
                PolicyTrace = Truncate(policyTrace, 256),
                CreatedAt = createdAt,
                PeriodMonthKey = createdAt.ToString("yyyy-MM"),
                PeriodDayKey = createdAt.ToString("yyyy-MM-dd"),
            };

            db.AiUsageRecords.Add(record);
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            // Fail-soft: recorder errors must never break the caller.
            // But make it loud in logs so ops notice.
            logger.LogError(
                ex,
                "AiUsageRecorder failed to persist usage for feature {Feature} provider {Provider} outcome {Outcome}",
                context.FeatureCode,
                providerId,
                outcome);
        }
    }

    private static string? HashOrNull(string? value)
    {
        if (string.IsNullOrEmpty(value)) return null;
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string? Truncate(string? value, int max)
    {
        if (string.IsNullOrEmpty(value)) return value;
        return value.Length <= max ? value : value[..max];
    }
}
