using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Endpoints;

/// <summary>
/// Read-only status of the TypeSafe / Jev judgment layer for
/// <c>/admin/ai-providers/typesafe</c>: master switch, per-surface flags,
/// thresholds, the pinned model, key presence and 7-day usage. Flags are
/// environment settings (change on the VPS, then deploy), so there is nothing
/// to write here; the key itself lives on the <c>typesafe-jev</c> provider row.
///
/// The API key is never returned: only whether one is configured, plus the
/// provider row's last-4 hint (the same hint <c>/v1/admin/ai/providers</c> shows).
///
/// Authorisation: <c>AdminAiConfig</c>, like the sibling AI admin endpoints.
/// </summary>
public static class TypeSafeAdminEndpoints
{
    internal const int UsageWindowDays = 7;
    private const string EnvPrefix = "TYPESAFE__";

    private sealed record Surface(string Flag, string Name, string FeatureCode, Func<TypeSafeOptions, bool> IsOn);

    private sealed record UsageRow(string FeatureCode, int Calls, int Failures, int AvgLatencyMs, decimal CostUsd);

    // Recommended flip order (docs/env/typesafe.md): triage, response verify, Writing shadow,
    // Speaking shadow, then the only surface that can stop a submission; unplanned surfaces last.
    private static readonly Surface[] Surfaces =
    [
        new(nameof(TypeSafeOptions.DevelopmentTriageEnabled), "Owner console triage", AiFeatureCodes.JevDevelopmentTriage, o => o.DevelopmentTriageEnabled),
        new(nameof(TypeSafeOptions.ResponseVerifyEnabled), "Reading / Listening explanation review", AiFeatureCodes.JevResponseVerify, o => o.ResponseVerifyEnabled),
        new(nameof(TypeSafeOptions.WritingVerifyEnabled), "Writing citation verify", AiFeatureCodes.JevWritingVerify, o => o.WritingVerifyEnabled),
        new(nameof(TypeSafeOptions.WritingCriteriaEnabled), "Writing criteria advisory", AiFeatureCodes.JevWritingCriteria, o => o.WritingCriteriaEnabled),
        new(nameof(TypeSafeOptions.WritingOutcomeEnabled), "Writing pass/fail cross-check", AiFeatureCodes.JevWritingOutcome, o => o.WritingOutcomeEnabled),
        new(nameof(TypeSafeOptions.WritingFindingsEnabled), "Writing finding classification", AiFeatureCodes.JevWritingFindings, o => o.WritingFindingsEnabled),
        new(nameof(TypeSafeOptions.ConversationAdvisoryEnabled), "AI-patient turn advisory", AiFeatureCodes.JevConversationTurn, o => o.ConversationAdvisoryEnabled),
        new(nameof(TypeSafeOptions.SpeakingReadinessEnabled), "Speaking readiness", AiFeatureCodes.JevSpeakingReadiness, o => o.SpeakingReadinessEnabled),
        new(nameof(TypeSafeOptions.SpeakingCrosscheckEnabled), "Speaking cross-check", AiFeatureCodes.JevSpeakingCrosscheck, o => o.SpeakingCrosscheckEnabled),
        new(nameof(TypeSafeOptions.WritingGuardEnabled), "Writing submission guard", AiFeatureCodes.JevWritingGuard, o => o.WritingGuardEnabled),
        new(nameof(TypeSafeOptions.WritingRouteEnabled), "Writing request routing", AiFeatureCodes.JevWritingRoute, o => o.WritingRouteEnabled),
        new(nameof(TypeSafeOptions.CompanionRerankEnabled), "Companion retrieval rerank", AiFeatureCodes.JevCompanionRerank, o => o.CompanionRerankEnabled),
    ];

    public static IEndpointRouteBuilder MapTypeSafeAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/admin/ai/typesafe")
            .RequireAuthorization("AdminAiConfig")
            .RequireRateLimiting("PerUser");

        group.MapGet("/status", async (LearnerDbContext db, IOptions<TypeSafeOptions> options, CancellationToken ct) =>
            Results.Ok(await BuildStatusAsync(db, options.Value, DateTimeOffset.UtcNow, ct)));

        return app;
    }

    internal static async Task<object> BuildStatusAsync(LearnerDbContext db, TypeSafeOptions opts, DateTimeOffset now, CancellationToken ct)
    {
        var since = now.AddDays(-UsageWindowDays);
        var usageRaw = await db.AiUsageRecords
            .AsNoTracking()
            .Where(r => r.ProviderId == TypeSafeOptions.ProviderCode && r.CreatedAt >= since)
            .GroupBy(r => r.FeatureCode)
            .Select(g => new
            {
                FeatureCode = g.Key,
                Calls = g.Count(),
                Failures = g.Count(r => r.Outcome != AiCallOutcome.Success),
                AvgLatencyMs = g.Average(r => (double)r.LatencyMs),
                CostUsd = g.Sum(r => r.CostEstimateUsd),
            })
            .ToListAsync(ct);
        var usage = usageRaw
            .Select(r => new UsageRow(r.FeatureCode, r.Calls, r.Failures, (int)Math.Round(r.AvgLatencyMs), decimal.Round(r.CostUsd, 6)))
            .ToDictionary(r => r.FeatureCode, StringComparer.Ordinal);

        // The encrypted key never leaves the database: only "is it set" is projected.
        var row = await db.AiProviders
            .AsNoTracking()
            .Where(p => p.Code == TypeSafeOptions.ProviderCode)
            .Select(p => new { p.IsActive, p.ApiKeyHint, HasKey = p.EncryptedApiKey != "" })
            .FirstOrDefaultAsync(ct);

        var envKeyConfigured = !string.IsNullOrWhiteSpace(opts.ApiKey);
        var rowKeyConfigured = row?.HasKey ?? false;
        var rowActive = row?.IsActive ?? false;

        var known = Surfaces.Select(s => s.FeatureCode).ToHashSet(StringComparer.Ordinal);
        return new
        {
            enabled = opts.Enabled,
            model = opts.Model,
            providerCode = TypeSafeOptions.ProviderCode,
            guardEnforced = opts.WritingGuardEnforced,
            key = new
            {
                envKeyConfigured,
                providerRowExists = row is not null,
                providerRowKeyConfigured = rowKeyConfigured,
                providerRowActive = rowActive,
                apiKeyHint = rowKeyConfigured ? row!.ApiKeyHint : null,
                // Mirrors the call path: an active provider-row key wins, the env key is the fallback.
                effectiveKeyAvailable = (rowActive && rowKeyConfigured) || envKeyConfigured,
            },
            surfaces = Surfaces.Select(s =>
            {
                var u = usage.GetValueOrDefault(s.FeatureCode);
                return new
                {
                    flag = s.Flag,
                    envVar = EnvPrefix + s.Flag.ToUpperInvariant(),
                    name = s.Name,
                    featureCode = s.FeatureCode,
                    enabled = s.IsOn(opts),
                    calls = u?.Calls ?? 0,
                    failures = u?.Failures ?? 0,
                    avgLatencyMs = u?.AvgLatencyMs ?? 0,
                    costUsd = u?.CostUsd ?? 0m,
                };
            }).ToArray(),
            // Jev feature codes recorded in the window that no surface above owns (a newer wave).
            otherUsage = usage.Values
                .Where(u => !known.Contains(u.FeatureCode))
                .OrderBy(u => u.FeatureCode, StringComparer.Ordinal)
                .Select(u => new { featureCode = u.FeatureCode, calls = u.Calls, failures = u.Failures, avgLatencyMs = u.AvgLatencyMs, costUsd = u.CostUsd })
                .ToArray(),
            thresholds = new[]
            {
                Setting(nameof(TypeSafeOptions.GuardBlockThreshold), opts.GuardBlockThreshold),
                Setting(nameof(TypeSafeOptions.GuardReviewThreshold), opts.GuardReviewThreshold),
                Setting(nameof(TypeSafeOptions.RouteConfidenceThreshold), opts.RouteConfidenceThreshold),
                Setting(nameof(TypeSafeOptions.VerifyConfidenceThreshold), opts.VerifyConfidenceThreshold),
                Setting(nameof(TypeSafeOptions.OutcomeConfidenceThreshold), opts.OutcomeConfidenceThreshold),
                Setting(nameof(TypeSafeOptions.CrosscheckDivergenceThreshold), opts.CrosscheckDivergenceThreshold),
                Setting(nameof(TypeSafeOptions.CrosscheckConfidenceThreshold), opts.CrosscheckConfidenceThreshold),
                Setting(nameof(TypeSafeOptions.ReadinessFlagThreshold), opts.ReadinessFlagThreshold),
                Setting(nameof(TypeSafeOptions.ResponseConfidenceThreshold), opts.ResponseConfidenceThreshold),
                Setting(nameof(TypeSafeOptions.DevelopmentConfidenceThreshold), opts.DevelopmentConfidenceThreshold),
            },
            limits = new[]
            {
                Setting(nameof(TypeSafeOptions.TimeoutSeconds), opts.TimeoutSeconds),
                Setting(nameof(TypeSafeOptions.MaxRetries), opts.MaxRetries),
                Setting(nameof(TypeSafeOptions.VerifyMaxFindingsPerCall), opts.VerifyMaxFindingsPerCall),
                Setting(nameof(TypeSafeOptions.BreakerFailureThreshold), opts.BreakerFailureThreshold),
                Setting(nameof(TypeSafeOptions.BreakerCooldownSeconds), opts.BreakerCooldownSeconds),
            },
            usageWindowDays = UsageWindowDays,
        };
    }

    private static object Setting(string name, double value) => new { name, envVar = EnvPrefix + name.ToUpperInvariant(), value };
}
