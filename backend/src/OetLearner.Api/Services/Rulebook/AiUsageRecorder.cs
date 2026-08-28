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
    Task<string?> RecordSuccessAsync(
        AiUsageContext context,
        string providerId,
        string model,
        AiKeySource keySource,
        AiUsage? usage,
        int latencyMs,
        int retryCount,
        string? policyTrace,
        CancellationToken ct,
        string? accountId = null,
        string? failoverTrace = null,
        decimal costEstimateUsd = 0m,
        string? usageRecordId = null,
        string? operationId = null,
        int? attemptNumber = null,
        AiCacheTokenBreakdown? cacheTokens = null,
        bool? providerInvoked = null);

    /// <summary>Record a call that did not succeed. <paramref name="outcome"/>
    /// must not be <see cref="AiCallOutcome.Success"/>. Returns the persisted
    /// row id, or null when the row could not be committed (the recorder is
    /// fail-soft), so a caller can tell "recorded" from "silently lost".</summary>
    Task<string?> RecordFailureAsync(
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
        CancellationToken ct,
        string? accountId = null,
        string? failoverTrace = null,
        AiUsage? usage = null,
        decimal costEstimateUsd = 0m,
        string? usageRecordId = null,
        string? operationId = null,
        int? attemptNumber = null,
        bool? providerInvoked = null);
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

/// <summary>
/// W2 of the AI cost/reliability remediation — Anthropic prompt-caching
/// token breakdown for a single physical call. <see cref="NormalInputTokens"/>/
/// <see cref="NormalOutputTokens"/> are the non-cached counterparts already
/// carried by <see cref="AiUsage"/>; this record adds the two additional
/// disjoint buckets a caching-aware provider reports
/// (<c>cache_creation_input_tokens</c>/<c>cache_read_input_tokens</c> in the
/// Anthropic Messages API), plus the resolved pricing provenance so
/// <see cref="AiUsageRecord.CalculatedCostUsd"/> is auditable against
/// <see cref="AiUsageRecord.PricingVersion"/> independent of the flat
/// <see cref="AiUsageRecord.CostEstimateUsd"/> rate-card estimate.
/// </summary>
public sealed record AiCacheTokenBreakdown(
    int CacheWriteTokens,
    int CacheReadTokens,
    string? PricingVersion,
    decimal? CalculatedCostUsd,
    string? BilledTokenClass = null);

public sealed class AiUsageRecorder(LearnerDbContext db, ILogger<AiUsageRecorder> logger) : IAiUsageRecorder
{
    public Task<string?> RecordSuccessAsync(
        AiUsageContext context,
        string providerId,
        string model,
        AiKeySource keySource,
        AiUsage? usage,
        int latencyMs,
        int retryCount,
        string? policyTrace,
        CancellationToken ct,
        string? accountId = null,
        string? failoverTrace = null,
        decimal costEstimateUsd = 0m,
        string? usageRecordId = null,
        string? operationId = null,
        int? attemptNumber = null,
        AiCacheTokenBreakdown? cacheTokens = null,
        bool? providerInvoked = null)
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
            accountId: accountId,
            failoverTrace: failoverTrace,
            costEstimateUsd: costEstimateUsd,
            usageRecordId: usageRecordId,
            operationId: operationId,
            attemptNumber: attemptNumber,
            cacheTokens: cacheTokens,
            providerInvoked: providerInvoked,
            ct: ct);

    public Task<string?> RecordFailureAsync(
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
        CancellationToken ct,
        string? accountId = null,
        string? failoverTrace = null,
        AiUsage? usage = null,
        decimal costEstimateUsd = 0m,
        string? usageRecordId = null,
        string? operationId = null,
        int? attemptNumber = null,
        bool? providerInvoked = null)
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
            usage: usage,
            latencyMs: latencyMs,
            retryCount: retryCount,
            policyTrace: policyTrace,
            accountId: accountId,
            failoverTrace: failoverTrace,
            costEstimateUsd: costEstimateUsd,
            usageRecordId: usageRecordId,
            operationId: operationId,
            attemptNumber: attemptNumber,
            cacheTokens: null,
            providerInvoked: providerInvoked,
            ct: ct);
    }

    private async Task<string?> PersistAsync(
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
        string? accountId,
        string? failoverTrace,
        decimal costEstimateUsd,
        string? usageRecordId,
        string? operationId,
        int? attemptNumber,
        AiCacheTokenBreakdown? cacheTokens,
        bool? providerInvoked,
        CancellationToken ct)
    {
        try
        {
            var createdAt = context.StartedAt == default ? DateTimeOffset.UtcNow : context.StartedAt;

            var record = new AiUsageRecord
            {
                Id = string.IsNullOrWhiteSpace(usageRecordId) ? Guid.NewGuid().ToString("N") : usageRecordId,
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
                CostEstimateUsd = Math.Max(0m, costEstimateUsd),
                Outcome = outcome,
                ErrorCode = Truncate(errorCode, 64),
                ErrorMessage = Truncate(errorMessage, 512),
                LatencyMs = latencyMs,
                RetryCount = retryCount,
                PolicyTrace = Truncate(policyTrace, 256),
                AccountId = Truncate(accountId, 64),
                FailoverTrace = Truncate(failoverTrace, 1024),
                CreatedAt = createdAt,
                PeriodMonthKey = createdAt.ToString("yyyy-MM"),
                PeriodDayKey = createdAt.ToString("yyyy-MM-dd"),
                OperationId = operationId,
                AttemptNumber = attemptNumber,
                ProviderInvoked = providerInvoked ?? (operationId is null ? null : true),
                NormalInputTokens = cacheTokens is null ? null : usage?.PromptTokens ?? 0,
                NormalOutputTokens = cacheTokens is null ? null : usage?.CompletionTokens ?? 0,
                CacheWriteTokens = cacheTokens?.CacheWriteTokens,
                CacheReadTokens = cacheTokens?.CacheReadTokens,
                BilledTokenClass = cacheTokens?.BilledTokenClass,
                PricingVersion = cacheTokens?.PricingVersion,
                CalculatedCostUsd = cacheTokens?.CalculatedCostUsd,
            };

            db.AiUsageRecords.Add(record);

            // W2 of the AI cost/reliability remediation: when this row is one
            // physical attempt against a durable AiOperation (coordinator
            // path), also persist the matching AiOperationAttempt row in the
            // same SaveChanges call — the composite (OperationId,
            // AttemptNumber) primary key is the structural guarantee that a
            // retry can never silently double-write the same attempt.
            if (!string.IsNullOrWhiteSpace(operationId) && attemptNumber is { } number)
            {
                db.AiOperationAttempts.Add(new AiOperationAttempt
                {
                    OperationId = operationId,
                    AttemptNumber = number,
                    AiUsageRecordId = record.Id,
                    ProviderInvoked = record.ProviderInvoked ?? true,
                    ErrorClass = Truncate(errorCode, 64),
                    CreatedAt = createdAt,
                });
            }

            await db.SaveChangesAsync(ct);
            return record.Id;
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
            return null;
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
