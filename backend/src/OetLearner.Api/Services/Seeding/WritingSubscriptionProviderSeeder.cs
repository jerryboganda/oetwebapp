using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Services.Seeding;

// Writing AI subscription sidecars (owner directive 2026-09-29).
//
// Seeds the two AiProvider rows that front the dedicated Claude Max 5x
// subscription (primary) and the Codex/ChatGPT subscription (fallback) for
// Writing AI. Both are plain HTTP facades over the vendor CLIs, reachable only
// on the internal compose network — the SSRF guard must allowlist both hosts
// via OET_INTERNAL_AI_HOSTS.
//
// No API keys: consumer subscriptions have no API access (verified 21 Sep 2026
// benchmark). The rows therefore carry no EncryptedApiKey; auth lives entirely
// inside the sidecar containers. A non-empty marker key is stored so the
// feature-route key-guard (`IsProviderUsableAsync`) treats the rows as usable
// and the admin connectivity probe can run.
//
// Strictly additive — existing rows are never overwritten so admins can retune
// via /admin/ai-providers. Routing stays OFF (IsActive=false) until the sidecars
// are deployed + the admin enables them, so this seed is safe to merge before
// the containers exist.

/// <summary>Canonical Writing subscription provider registration values.</summary>
public static class WritingSubscriptionProviderDefaults
{
    public const string ClaudeCode = "writing-claude-sub";
    public const string ClaudeName = "Writing Claude (dedicated Max 5x subscription)";
    public const string ClaudeBaseUrl = "http://oet-writing-claude:8080";
    public const string ClaudeModel = "claude-opus-5-5";

    public const string CodexCode = "writing-codex-sub";
    public const string CodexName = "Writing Codex (subscription fallback)";
    public const string CodexBaseUrl = "http://oet-writing-codex:8080";
    public const string CodexModel = "gpt-6-sol";

    /// <summary>Hosts that must appear in OET_INTERNAL_AI_HOSTS for these rows'
    /// plain-HTTP internal base URLs to pass the SSRF guard.</summary>
    public const string RequiredInternalHosts = "oet-writing-claude,oet-writing-codex";

    /// <summary>Literal stored in <c>AiProvider.EncryptedApiKey</c> of the keyless subscription
    /// sidecar rows. It is NOT Data Protection ciphertext and not a secret: the sidecars ignore
    /// the key header, so the registry hands it back as the key instead of failing to decrypt.</summary>
    public const string MarkerKey = "subscription-sidecar";

    public static bool IsMarkerKey(string? storedKey)
        => string.Equals(storedKey, MarkerKey, StringComparison.Ordinal);
}

/// <summary>Idempotent startup seeder for the two Writing subscription provider rows.</summary>
public static class WritingSubscriptionProviderSeeder
{
    public static async Task<int> SeedAsync(LearnerDbContext db, CancellationToken ct = default)
    {
        var inserted = 0;

        if (!await db.AiProviders.AnyAsync(p => p.Code == WritingSubscriptionProviderDefaults.ClaudeCode, ct))
        {
            db.AiProviders.Add(new AiProvider
            {
                Id = Guid.NewGuid().ToString("N"),
                Code = WritingSubscriptionProviderDefaults.ClaudeCode,
                Name = WritingSubscriptionProviderDefaults.ClaudeName,
                Dialect = AiProviderDialect.Anthropic,
                Category = AiProviderCategory.TextChat,
                BaseUrl = WritingSubscriptionProviderDefaults.ClaudeBaseUrl,
                DefaultModel = WritingSubscriptionProviderDefaults.ClaudeModel,
                // Marker only — the sidecar authenticates with the subscription,
                // not a key. Present so the route key-guard + probe treat the row
                // as credentialed.
                EncryptedApiKey = WritingSubscriptionProviderDefaults.MarkerKey,
                ApiKeyHint = "claude-max-5x",
                AllowedModelsCsv = WritingSubscriptionProviderDefaults.ClaudeModel,
                PricePer1kPromptTokens = 0m,
                PricePer1kCompletionTokens = 0m,
                RetryCount = 1,
                CircuitBreakerThreshold = 5,
                CircuitBreakerWindowSeconds = 60,
                FailoverPriority = 1,
                IsActive = false, // admin enables after sidecars deploy
            });
            inserted++;
        }

        if (!await db.AiProviders.AnyAsync(p => p.Code == WritingSubscriptionProviderDefaults.CodexCode, ct))
        {
            db.AiProviders.Add(new AiProvider
            {
                Id = Guid.NewGuid().ToString("N"),
                Code = WritingSubscriptionProviderDefaults.CodexCode,
                Name = WritingSubscriptionProviderDefaults.CodexName,
                Dialect = AiProviderDialect.OpenAiCompatible,
                Category = AiProviderCategory.TextChat,
                BaseUrl = WritingSubscriptionProviderDefaults.CodexBaseUrl,
                DefaultModel = WritingSubscriptionProviderDefaults.CodexModel,
                EncryptedApiKey = WritingSubscriptionProviderDefaults.MarkerKey,
                ApiKeyHint = "codex-chatgpt",
                AllowedModelsCsv = WritingSubscriptionProviderDefaults.CodexModel,
                PricePer1kPromptTokens = 0m,
                PricePer1kCompletionTokens = 0m,
                RetryCount = 1,
                CircuitBreakerThreshold = 5,
                CircuitBreakerWindowSeconds = 60,
                FailoverPriority = 2,
                IsActive = false, // admin enables after sidecars deploy
            });
            inserted++;
        }

        if (inserted > 0)
            await db.SaveChangesAsync(ct);
        return inserted;
    }
}
