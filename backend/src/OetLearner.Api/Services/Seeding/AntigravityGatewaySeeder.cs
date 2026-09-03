using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Services.Seeding;

// Antigravity Agent Gateway (Phase: Google Antigravity SDK integration).
//
// Seeds:
//   * The `antigravity-gateway` AiProvider row (OpenAiCompatible dialect,
//     OpenAI-compatible chat/completions base URL). When
//     AGENTGATEWAY_INTERNAL_SERVICE_TOKEN is present, the token is encrypted
//     with the same purpose-scoped DataProtection pipeline used by the admin
//     provider editor; otherwise the row remains an inactive placeholder.
//   * Feature-route rows pointing the supported AI features at the gateway.
//
// Strictly additive - an existing row/route is NEVER overwritten, so admins
// can retune routes via the admin route editor without clobbering.
//
// Gateway does the agent mapping: the model column carries `agent:<name>`
// (e.g. `agent:writing-examiner`) and the gateway resolves it to a persona.
//
// Safe fallback: if the gateway container is down or quota-exhausted, the
// AiFeatureRouteResolver's existing per-feature fallback chain still applies
// (Anthropic / OpenAI-compatible), so learner traffic never hard-fails.

/// <summary>Canonical feature → gateway agent mappings (additive seeds).</summary>
public static class AntigravityGatewayRouteDefaults
{
    public const string ProviderCode = "antigravity-gateway";
    public const string GatewayBaseUrl = "http://oet-agent-gateway:8305/v1";

    /// <summary>
    /// Route takeover is an explicit rollout gate. Keep it false until the
    /// gateway Gemini key and the encrypted backend service token are both
    /// configured and a smoke test is ready.
    /// </summary>
    public static bool RoutesEnabled => string.Equals(
        Environment.GetEnvironmentVariable("ANTIGRAVITY_GATEWAY_ROUTES_ENABLED"),
        "true",
        StringComparison.OrdinalIgnoreCase);

    public static readonly IReadOnlyList<(string FeatureCode, string Agent)> Routes = new[]
    {
        (AiFeatureCodes.WritingGrade, "writing-examiner"),
        (AiFeatureCodes.WritingSampleScore, "writing-examiner"),
        (AiFeatureCodes.WritingDrillGradeV1, "writing-examiner"),
        (AiFeatureCodes.WritingCoachV1, "writing-examiner"),
        (SpeakingAiFeatureCodes.SpeakingPatientTurnV1, "speaking-interlocutor"),
        (AiFeatureCodes.ConversationOpening, "speaking-interlocutor"),
        (AiFeatureCodes.ConversationReply, "speaking-interlocutor"),
        (SpeakingAiFeatureCodes.CardDraftV1, "drill-author"),
        (AiFeatureCodes.ConversationEvaluation, "speaking-interlocutor"),
        (AiFeatureCodes.PronunciationTip, "pronunciation-coach"),
        (AiFeatureCodes.PronunciationFeedback, "pronunciation-coach"),
        (AiFeatureCodes.MockFullGrade, "mock-analysis"),
        (AiFeatureCodes.MockRemediationDraft, "mock-analysis"),
        (AiFeatureCodes.AdminReadingDraft, "reading-item-generator"),
        (AiFeatureCodes.AdminListeningDraft, "listening-item-generator"),
        (AiFeatureCodes.AdminGrammarDraft, "grammar-tutor"),
        (AiFeatureCodes.ReadingExplanation, "reading-item-generator"),
        (AiFeatureCodes.ListeningExplanation, "listening-item-generator"),
    };
}

/// <summary>Idempotent startup seeder for the Antigravity gateway provider row
/// and its feature routes.</summary>
public static class AntigravityGatewaySeeder
{
    private const string ProtectorPurpose = "AiProvider.PlatformKey.v1";

    public static async Task<int> SeedAsync(
        LearnerDbContext db,
        IDataProtectionProvider dataProtectionProvider,
        CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var inserted = 0;
        var gatewayToken = Environment.GetEnvironmentVariable("AGENTGATEWAY_INTERNAL_SERVICE_TOKEN")?.Trim();
        var protector = dataProtectionProvider.CreateProtector(ProtectorPurpose);

        var provider = await db.AiProviders
            .FirstOrDefaultAsync(p => p.Code == AntigravityGatewayRouteDefaults.ProviderCode, ct);
        var providerReady = provider is not null && !string.IsNullOrWhiteSpace(provider.EncryptedApiKey);
        if (provider is null)
        {
            provider = new AiProvider
            {
                Id = Guid.NewGuid().ToString("N"),
                Code = AntigravityGatewayRouteDefaults.ProviderCode,
                Name = "Antigravity Agent Gateway",
                Dialect = AiProviderDialect.OpenAiCompatible,
                Category = AiProviderCategory.TextChat,
                BaseUrl = AntigravityGatewayRouteDefaults.GatewayBaseUrl,
                DefaultModel = "agent:writing-examiner",
                EncryptedApiKey = string.IsNullOrWhiteSpace(gatewayToken)
                    ? string.Empty
                    : protector.Protect(gatewayToken),
                ApiKeyHint = string.IsNullOrWhiteSpace(gatewayToken) ? string.Empty : "gateway-token",
                AllowedModelsCsv = "agent:writing-examiner,agent:speaking-interlocutor,agent:pronunciation-coach,agent:grammar-tutor,agent:reading-item-generator,agent:listening-item-generator,agent:drill-author,agent:mock-analysis",
                PricePer1kPromptTokens = 0m,
                PricePer1kCompletionTokens = 0m,
                FailoverPriority = 60,
                IsActive = false,
            };
            db.AiProviders.Add(provider);
            providerReady = !string.IsNullOrWhiteSpace(gatewayToken);
            inserted++;
        }
        else if (!providerReady && !string.IsNullOrWhiteSpace(gatewayToken))
        {
            provider.EncryptedApiKey = protector.Protect(gatewayToken);
            provider.ApiKeyHint = "gateway-token";
            providerReady = true;
            inserted++;
        }

        if (AntigravityGatewayRouteDefaults.RoutesEnabled && providerReady)
        {
            if (provider is not null && !provider.IsActive)
            {
                provider.IsActive = true;
            }

            foreach (var (featureCode, agent) in AntigravityGatewayRouteDefaults.Routes)
            {
                var exists = await db.AiFeatureRoutes
                    .AnyAsync(r => r.FeatureCode == featureCode, ct);
                if (exists) continue;

                db.AiFeatureRoutes.Add(new AiFeatureRoute
                {
                    Id = Guid.NewGuid().ToString("N"),
                    FeatureCode = featureCode,
                    ProviderCode = AntigravityGatewayRouteDefaults.ProviderCode,
                    Model = $"agent:{agent}",
                    IsActive = true,
                    CreatedAt = now,
                    UpdatedAt = now,
                });
                inserted++;
            }
        }

        if (inserted > 0)
        {
            await db.SaveChangesAsync(ct);
        }

        return inserted;
    }
}

/// <summary>Hosted wrapper mirroring <see cref="SpeakingAiRouteSeedHostedService"/>:
/// best-effort, 5s grace for migrations, crash-safe.</summary>
public sealed class AntigravityGatewaySeedHostedService(
    IServiceScopeFactory scopeFactory,
    ILogger<AntigravityGatewaySeedHostedService> logger) : IHostedService
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
            var added = await AntigravityGatewaySeeder.SeedAsync(db, dataProtectionProvider, ct);
            if (added > 0)
            {
                logger.LogInformation(
                    "Seeded Antigravity gateway provider + {Count} feature-route rows.", added);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Antigravity gateway seed failed; resolver fallback will be used.");
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
