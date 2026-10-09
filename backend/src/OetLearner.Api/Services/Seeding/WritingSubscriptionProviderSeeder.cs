using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Services.Seeding;

// Writing AI subscription sidecars (owner directive 2026-09-29).
//
// Seeds the AiProvider rows that front the Claude Max subscriptions (primary)
// and the Codex/ChatGPT subscriptions (fallback) for Writing and Speaking. All
// are plain HTTP facades over the vendor CLIs, reachable only on the internal
// compose network — the SSRF guard must allowlist every host via
// OET_INTERNAL_AI_HOSTS.
//
// No API keys: consumer subscriptions have no API access (verified 21 Sep 2026
// benchmark). The rows therefore carry no EncryptedApiKey; auth lives entirely
// inside the sidecar containers. A non-empty marker key is stored so the
// feature-route key-guard (`IsProviderUsableAsync`) treats the rows as usable
// and the admin connectivity probe can run.
//
// Additive, plus two self-heals on every boot (see SeedAsync): the primary Max
// row is kept active (owner rule MAX-ALWAYS-ON, 2 Oct 2026) and a Codex row
// still on the old gpt-6-sol default moves to GPT-6.1 Sol. Anything else an
// admin tuned via /admin/ai-providers is left alone.
//
// ACCOUNT POOL (owner directives 2026-10-10 + 2026-10-09): one container — and one provider
// row — per subscription account. TWO Claude Max accounts (the primary plus one extra) and
// three ChatGPT Business accounts are seeded here, the extra rows INACTIVE so nothing changes
// until an admin enables an account whose sidecar has completed its device-code login.
// `SubscriptionAccountPool`
// (Services/AiPipeline) orders the accounts of one engine group at run time by remaining
// quota; the saved stage order in AiPipelineStages is never written by it.

/// <summary>Canonical Writing subscription provider registration values.</summary>
public static class WritingSubscriptionProviderDefaults
{
    public const string ClaudeCode = "writing-claude-sub";
    public const string ClaudeName = "Writing Claude (dedicated Max 5x subscription)";
    public const string ClaudeBaseUrl = "http://oet-writing-claude:8080";
    public const string ClaudeModel = "claude-opus-5-5";

    // Extra Claude Max accounts (own credential volume, own serial lane).
    // Owner decision 2026-10-09: the pool is TWO Max accounts (primary + slot 2);
    // the former slot 3 was removed from the compose project and its seeded row
    // deleted, so no ClaudeCode3 exists any more.
    public const string ClaudeCode2 = "writing-claude-sub-2";
    public const string ClaudeName2 = "Writing Claude account 2 (Max subscription)";
    public const string ClaudeBaseUrl2 = "http://oet-writing-claude-2:8080";

    public const string CodexCode = "writing-codex-sub";
    public const string CodexName = "Writing Codex (subscription fallback)";
    public const string CodexBaseUrl = "http://oet-writing-codex:8080";
    // Owner decision 2 Oct 2026: GPT-6.1 Sol High replaces gpt-6-sol.
    public const string CodexModel = "gpt-6.1-sol";
    public const string LegacyCodexModel = "gpt-6-sol";

    // Extra ChatGPT Business (Codex) accounts.
    public const string CodexCode2 = "writing-codex-sub-2";
    public const string CodexName2 = "Writing Codex account 2 (ChatGPT Business subscription)";
    public const string CodexBaseUrl2 = "http://oet-writing-codex-2:8080";
    public const string CodexCode3 = "writing-codex-sub-3";
    public const string CodexName3 = "Writing Codex account 3 (ChatGPT Business subscription)";
    public const string CodexBaseUrl3 = "http://oet-writing-codex-3:8080";

    /// <summary>The Claude Max subscription accounts, primary first. The pool rotates inside this
    /// group only; the Max class is never skipped (owner directive 2026-10-10).</summary>
    public static readonly IReadOnlyList<string> ClaudeGroupCodes = [ClaudeCode, ClaudeCode2];

    /// <summary>The Codex / ChatGPT Business subscription accounts, primary first.</summary>
    public static readonly IReadOnlyList<string> CodexGroupCodes = [CodexCode, CodexCode2, CodexCode3];

    /// <summary>Hosts that must appear in OET_INTERNAL_AI_HOSTS for these rows'
    /// plain-HTTP internal base URLs to pass the SSRF guard.</summary>
    public const string RequiredInternalHosts =
        "oet-writing-claude,oet-writing-claude-2," +
        "oet-writing-codex,oet-writing-codex-2,oet-writing-codex-3";

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
                IsActive = true, // initial setup only; the owner may switch it off and a boot never switches it back on
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

        // Owner directive 2026-10-09: a saved decision is never undone by a boot. The Max row is NOT re-activated here
        // any more (insert-only above). Level 3 moves to GPT-6.1 Sol: a Codex row still on the old default is
        // retargeted; a model an admin chose deliberately is left alone.
        var changed = false;

        // Account pool (owner directive 2026-10-10): the extra Claude Max and Codex accounts. Insert-only,
        // inactive until an admin enables them. A Codex account row is dialect OpenAiCompatible; a Claude
        // account row is Anthropic. Both keep the marker key so the keyless-row probe works.
        foreach (var (code, name, baseUrl, dialect, model, hint) in new (string, string, string, AiProviderDialect, string, string)[]
                 {
                     (WritingSubscriptionProviderDefaults.ClaudeCode2, WritingSubscriptionProviderDefaults.ClaudeName2,
                         WritingSubscriptionProviderDefaults.ClaudeBaseUrl2, AiProviderDialect.Anthropic,
                         WritingSubscriptionProviderDefaults.ClaudeModel, "claude-max-5x-2"),
                     (WritingSubscriptionProviderDefaults.CodexCode2, WritingSubscriptionProviderDefaults.CodexName2,
                         WritingSubscriptionProviderDefaults.CodexBaseUrl2, AiProviderDialect.OpenAiCompatible,
                         WritingSubscriptionProviderDefaults.CodexModel, "codex-chatgpt-2"),
                     (WritingSubscriptionProviderDefaults.CodexCode3, WritingSubscriptionProviderDefaults.CodexName3,
                         WritingSubscriptionProviderDefaults.CodexBaseUrl3, AiProviderDialect.OpenAiCompatible,
                         WritingSubscriptionProviderDefaults.CodexModel, "codex-chatgpt-3"),
                 })
        {
            if (await db.AiProviders.AnyAsync(p => p.Code == code, ct)) continue;
            db.AiProviders.Add(AccountRow(code, name, baseUrl, dialect, model, hint));
            inserted++;
        }

        var codex = await db.AiProviders.FirstOrDefaultAsync(
            p => p.Code == WritingSubscriptionProviderDefaults.CodexCode
                && p.DefaultModel == WritingSubscriptionProviderDefaults.LegacyCodexModel, ct);
        if (codex is not null)
        {
            codex.DefaultModel = WritingSubscriptionProviderDefaults.CodexModel;
            if (codex.AllowedModelsCsv == WritingSubscriptionProviderDefaults.LegacyCodexModel)
                codex.AllowedModelsCsv = WritingSubscriptionProviderDefaults.CodexModel;
            changed = true;
        }

        if (inserted > 0 || changed)
            await db.SaveChangesAsync(ct);
        return inserted;
    }

    /// <summary>One extra account row of one engine. Insert-only and seeded INACTIVE: an account
    /// becomes usable when its sidecar has a device-code login and an admin enables the row (or adds
    /// it as a hop on /admin/ai-pipelines, which the resolver checks against this same row).</summary>
    private static AiProvider AccountRow(
        string code, string name, string baseUrl, AiProviderDialect dialect, string model, string hint) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        Code = code,
        Name = name,
        Dialect = dialect,
        Category = AiProviderCategory.TextChat,
        BaseUrl = baseUrl,
        DefaultModel = model,
        EncryptedApiKey = WritingSubscriptionProviderDefaults.MarkerKey,
        ApiKeyHint = hint,
        AllowedModelsCsv = model,
        PricePer1kPromptTokens = 0m,
        PricePer1kCompletionTokens = 0m,
        RetryCount = 1,
        CircuitBreakerThreshold = 5,
        CircuitBreakerWindowSeconds = 60,
        FailoverPriority = 2,
        IsActive = false,
    };
}
