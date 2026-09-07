using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Services.Seeding;

// UBAG OpenAI-compat facade (Phase: UBAG-as-OET-AI-provider integration).
//
// Seeds the `ubag` AiProvider row (OpenAiCompatible dialect) pointing at the
// UBAG gateway's OpenAI facade over the private compose network:
//   http://ubag-vps-gateway-1:8080/v1/openai
// The .NET registry then calls {BaseUrl}/chat/completions with stream:false —
// the same consumption pattern as the antigravity-gateway row — so no new
// provider dialect or IAiModelProvider implementation is required.
//
// Auth: the row carries the tenant-scoped UBAG PAT (service role,
// tenant_oet/oet-platform) encrypted with the same purpose-scoped
// DataProtection pipeline as every other provider key. The PAT is supplied
// via the UBAG_OET_PAT environment variable (oet-api containers) or pasted
// in /admin/ai-providers; without it the row stays an inactive placeholder.
//
// Strictly additive — an existing row is NEVER overwritten, so admins can
// retune the row via the admin provider editor without clobbering.
//
// No feature routes are seeded: routing is owned exclusively by the admin
// UBAG toggle board (/admin/ai-providers/ubag), which writes AiFeatureRoute
// rows. Everything starts OFF; the admin enables group by group.
//
// Model naming: the facade accepts a UBAG target (e.g. `chatgpt_web`) or
// `target|setting` (e.g. `chatgpt_web|GPT-5.6 Sol`); see
// GET /v1/openai/models. DefaultModel stays `mock` so the admin "Test
// connection" probe runs a fast mock job instead of a browser session;
// per-feature Model overrides carry the real targets.
//
// Safe fallback: UBAG failures (including the private-network guard when
// OET_INTERNAL_AI_HOSTS is unset) surface as provider errors, and features
// without a UBAG route keep their Anthropic/OpenAI defaults.

/// <summary>Canonical UBAG provider registration values.</summary>
public static class UbagProviderRouteDefaults
{
    public const string ProviderCode = "ubag";
    public const string ProviderName = "UBAG (browser AI providers)";
    public const string FacadeBaseUrl = "http://ubag-vps-gateway-1:8080/v1/openai";
    public const string DefaultModel = "mock";

    /// <summary>
    /// Reachable only when AiProviderConnectionTester.GetUnsafeBaseUrlReason
    /// accepts the facade URL: OET_INTERNAL_AI_HOSTS must list
    /// <c>ubag-vps-gateway-1</c> on the oet-api containers.
    /// </summary>
    public const string RequiredInternalHost = "ubag-vps-gateway-1";

    /// <summary>Full facade model catalog as served by GET /v1/openai/models.</summary>
    public const string AllowedModelsCsv =
        "mock,mock|mock-fast,mock|mock-deep,mock|standard,mock|extended," +
        "deepseek_web,deepseek_web|Expert,deepseek_web|Instant,deepseek_web|Vision," +
        "chatgpt_web,chatgpt_web|GPT-5.6 Sol,chatgpt_web|GPT-5.5,chatgpt_web|GPT-5.4,chatgpt_web|GPT-5.3,chatgpt_web|o3,chatgpt_web|Instant,chatgpt_web|Medium,chatgpt_web|High," +
        "claude_web," +
        "gemini_web,gemini_web|3.7 Flash,gemini_web|3.6 Flash,gemini_web|3.5 Flash,gemini_web|3.1 Flash-Lite,gemini_web|3.1 Pro," +
        "mistral_lechat,perplexity_web,generic_chat,generic_form";
}

/// <summary>Idempotent startup seeder for the UBAG provider row.</summary>
public static class UbagProviderSeeder
{
    private const string ProtectorPurpose = "AiProvider.PlatformKey.v1";

    public static async Task<int> SeedAsync(
        LearnerDbContext db,
        IDataProtectionProvider dataProtectionProvider,
        CancellationToken ct = default)
    {
        var inserted = 0;
        var pat = Environment.GetEnvironmentVariable("UBAG_OET_PAT")?.Trim();
        var protector = dataProtectionProvider.CreateProtector(ProtectorPurpose);

        var provider = await db.AiProviders
            .FirstOrDefaultAsync(p => p.Code == UbagProviderRouteDefaults.ProviderCode, ct);
        var providerReady = provider is not null && !string.IsNullOrWhiteSpace(provider.EncryptedApiKey);
        if (provider is null)
        {
            db.AiProviders.Add(new AiProvider
            {
                Id = Guid.NewGuid().ToString("N"),
                Code = UbagProviderRouteDefaults.ProviderCode,
                Name = UbagProviderRouteDefaults.ProviderName,
                Dialect = AiProviderDialect.OpenAiCompatible,
                Category = AiProviderCategory.TextChat,
                BaseUrl = UbagProviderRouteDefaults.FacadeBaseUrl,
                DefaultModel = UbagProviderRouteDefaults.DefaultModel,
                EncryptedApiKey = string.IsNullOrWhiteSpace(pat)
                    ? string.Empty
                    : protector.Protect(pat),
                ApiKeyHint = string.IsNullOrWhiteSpace(pat) ? string.Empty : "ubag-pat",
                AllowedModelsCsv = UbagProviderRouteDefaults.AllowedModelsCsv,
                PricePer1kPromptTokens = 0m,
                PricePer1kCompletionTokens = 0m,
                RetryCount = 2,
                CircuitBreakerThreshold = 5,
                CircuitBreakerWindowSeconds = 30,
                FailoverPriority = 70,
                IsActive = false,
            });
            providerReady = !string.IsNullOrWhiteSpace(pat);
            inserted++;
        }
        else if (!providerReady && !string.IsNullOrWhiteSpace(pat))
        {
            provider.EncryptedApiKey = protector.Protect(pat);
            provider.ApiKeyHint = "ubag-pat";
            providerReady = true;
            inserted++;
        }

        if (inserted > 0)
        {
            await db.SaveChangesAsync(ct);
        }

        return inserted;
    }
}

/// <summary>Hosted wrapper mirroring <see cref="AntigravityGatewaySeedHostedService"/>:
/// best-effort, 5s grace for migrations, crash-safe.</summary>
public sealed class UbagProviderSeedHostedService(
    IServiceScopeFactory scopeFactory,
    ILogger<UbagProviderSeedHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(5000, ct);
        }
        catch (TaskCanceledException)
        {
            return;
        }
        catch (Exception)
        {
            return;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var dataProtectionProvider = scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>();
        try
        {
            var added = await UbagProviderSeeder.SeedAsync(db, dataProtectionProvider, ct);
            if (added > 0)
            {
                logger.LogInformation(
                    "Seeded UBAG provider row (+{Count} change(s)).", added);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "UBAG provider seed failed; resolver fallback will be used.");
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
