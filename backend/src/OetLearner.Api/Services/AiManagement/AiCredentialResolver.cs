using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.AiManagement;

// ═════════════════════════════════════════════════════════════════════════════
// Credential Resolver — docs/AI-USAGE-POLICY.md §11 precedence, implemented
// as one deterministic function. Returns the full decision, including a
// policy trace the gateway stores in AiUsageRecord.PolicyTrace.
// ═════════════════════════════════════════════════════════════════════════════

public interface IAiCredentialResolver
{
    /// <summary>Resolve which credential and provider to use for this call.
    /// Pure: no side effects on the DB (the gateway records the outcome).</summary>
    Task<AiCredentialResolution> ResolveAsync(
        string? userId,
        string featureCode,
        string? callerRequestedProviderCode,
        CancellationToken ct);
}

public sealed record AiCredentialResolution(
    AiKeySource KeySource,
    string ProviderCode,
    string? ApiKeyPlaintext,
    string? BaseUrlOverride,
    string? CredentialId,
    string PolicyTrace);

public sealed class AiCredentialResolver(
    LearnerDbContext db,
    IAiQuotaService quotaService,
    IAiCredentialVault vault) : IAiCredentialResolver
{
    /// <summary>Feature codes classified as scoring-critical. Platform-only
    /// scoring rows are still listed here; the platform-only guard below takes
    /// precedence over §1 <c>AllowByokOnScoringFeatures</c>. Mirrors
    /// docs/AI-USAGE-POLICY.md §5.</summary>
    private static readonly HashSet<string> ScoringCriticalFeatures = new(StringComparer.OrdinalIgnoreCase)
    {
        AiFeatureCodes.WritingGrade,
        AiFeatureCodes.WritingSampleScore,
        AiFeatureCodes.SpeakingGrade,
        AiFeatureCodes.MockFullGrade,
        AiFeatureCodes.PronunciationScore,
        AiFeatureCodes.ConversationEvaluation,
    };

    /// <summary>Features that must never use BYOK.</summary>
    private static readonly HashSet<string> PlatformOnlyFeatures = new(StringComparer.OrdinalIgnoreCase)
    {
        AiFeatureCodes.PronunciationScore,
        AiFeatureCodes.PronunciationFeedback,
        AiFeatureCodes.ConversationEvaluation,
        AiFeatureCodes.AdminContentGeneration,
        AiFeatureCodes.AdminGrammarDraft,
        AiFeatureCodes.AdminPronunciationDraft,
        AiFeatureCodes.AdminVocabularyDraft,
        AiFeatureCodes.AdminConversationDraft,
    };

    public async Task<AiCredentialResolution> ResolveAsync(
        string? userId,
        string featureCode,
        string? callerRequestedProviderCode,
        CancellationToken ct)
    {
        var global = await quotaService.GetGlobalPolicyAsync(ct);

        // Platform-keyed default target. This is what we use whenever BYOK
        // is not applicable or not available.
        var defaultPlatformProvider = string.IsNullOrWhiteSpace(callerRequestedProviderCode)
            ? global.DefaultPlatformProviderId
            : callerRequestedProviderCode;

        // ── Rule 1: no user ⇒ platform-only (admin tooling, trials) ─────────
        if (string.IsNullOrWhiteSpace(userId))
        {
            return new AiCredentialResolution(
                AiKeySource.Platform,
                defaultPlatformProvider,
                ApiKeyPlaintext: null,
                BaseUrlOverride: null,
                CredentialId: null,
                PolicyTrace: "no_user → platform");
        }

        // ── Rule 2: platform-only features ──────────────────────────────────
        if (PlatformOnlyFeatures.Contains(featureCode))
        {
            return new AiCredentialResolution(
                AiKeySource.Platform,
                defaultPlatformProvider, null, null, null,
                PolicyTrace: $"feature.{featureCode}.platform_only");
        }

        // ── Rule 3: scoring-critical + global switch disables BYOK ──────────
        if (ScoringCriticalFeatures.Contains(featureCode) && !global.AllowByokOnScoringFeatures)
        {
            return new AiCredentialResolution(
                AiKeySource.Platform,
                defaultPlatformProvider, null, null, null,
                PolicyTrace: $"scoring_critical.{featureCode}.platform_only");
        }

        // ── Rule 4: non-scoring feature + global switch disables BYOK ───────
        if (!ScoringCriticalFeatures.Contains(featureCode) && !global.AllowByokOnNonScoringFeatures)
        {
            return new AiCredentialResolution(
                AiKeySource.Platform,
                defaultPlatformProvider, null, null, null,
                PolicyTrace: $"non_scoring.{featureCode}.platform_only_by_policy");
        }

        // ── Rule 5: resolve the learner's preference ────────────────────────
        var prefs = await db.UserAiPreferences.AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == userId, ct);
        var mode = prefs?.Mode ?? AiCredentialMode.Auto;

        if (mode == AiCredentialMode.PlatformOnly)
        {
            return new AiCredentialResolution(
                AiKeySource.Platform,
                defaultPlatformProvider, null, null, null,
                PolicyTrace: "user.platform_only");
        }

        // ── Rule 6: try BYOK (auto or byok-only) ────────────────────────────
        // Find the best matching BYOK credential. Precedence:
        //  1. Caller-requested provider (if BYOK stored for it).
        //  2. First active, non-cooldown credential this user has.
        var candidates = await db.UserAiCredentials.AsNoTracking()
            .Where(c => c.UserId == userId && c.Status == AiCredentialStatus.Active)
            .ToListAsync(ct);
        var usable = candidates
            .Where(c => !c.CooldownUntil.HasValue || c.CooldownUntil.Value <= DateTimeOffset.UtcNow)
            .ToList();

        UserAiCredential? chosen = null;
        if (!string.IsNullOrWhiteSpace(callerRequestedProviderCode))
        {
            chosen = usable.FirstOrDefault(c =>
                string.Equals(c.ProviderCode, callerRequestedProviderCode, StringComparison.OrdinalIgnoreCase));
        }
        chosen ??= usable.FirstOrDefault();

        if (chosen is not null)
        {
            var plaintext = await vault.ResolvePlaintextAsync(userId, chosen.ProviderCode, ct);
            if (!string.IsNullOrEmpty(plaintext))
            {
                return new AiCredentialResolution(
                    AiKeySource.Byok,
                    chosen.ProviderCode,
                    plaintext,
                    BaseUrlOverride: DefaultBaseUrlFor(chosen.ProviderCode),
                    CredentialId: chosen.Id,
                    PolicyTrace: $"user.{mode.ToString().ToLowerInvariant()}.byok.{chosen.ProviderCode}");
            }
        }

        // ── Rule 7: byok-only mode + no BYOK available ⇒ refuse via platform,
        //          but signal via policyTrace so the caller UI can show the
        //          right message ("add a key to continue"). We still route to
        //          platform to keep the feature usable unless the user
        //          explicitly forbade it.
        if (mode == AiCredentialMode.ByokOnly)
        {
            return new AiCredentialResolution(
                AiKeySource.None,
                defaultPlatformProvider, null, null, null,
                PolicyTrace: "user.byok_only.no_key_available");
        }

        // ── Rule 8: auto + no BYOK available ⇒ platform ─────────────────────
        return new AiCredentialResolution(
            AiKeySource.Platform,
            defaultPlatformProvider, null, null, null,
            PolicyTrace: "user.auto.platform_fallback");
    }

    /// <summary>Well-known base URLs for the OpenAI-compatible dialect.
    /// Used when BYOK targets a provider other than the default platform
    /// endpoint. Slice 5 makes this fully DB-driven.</summary>
    private static string? DefaultBaseUrlFor(string providerCode) => providerCode switch
    {
        "openai-platform" => "https://api.openai.com/v1",
        "openrouter" => "https://openrouter.ai/api/v1",
        // Anthropic uses a different dialect — handled by AnthropicProvider
        // in Slice 5, not by OpenAiCompatibleProvider, so we return null and
        // the gateway will dispatch by provider name.
        _ => null,
    };
}
