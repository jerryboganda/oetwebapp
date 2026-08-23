using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Services.Seeding;

// Antigravity Agent Gateway (Phase: Google Antigravity SDK integration).
//
// Seeds:
//   * The `antigravity-gateway` AiProvider row (OpenAiCompatible dialect,
//     OpenAI-compatible chat/completions base URL). Keys are NOT seeded -
//     the admin pastes the internal service token in /admin/ai-providers,
//     exactly like CoreAiProviderSeeder (keyless rows, encrypted on save).
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
    public static async Task<int> SeedAsync(LearnerDbContext db, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var inserted = 0;

        var providerExists = await db.AiProviders.AsNoTracking()
            .AnyAsync(p => p.Code == AntigravityGatewayRouteDefaults.ProviderCode, ct);
        if (!providerExists)
        {
            db.AiProviders.Add(new AiProvider
            {
                Id = Guid.NewGuid().ToString("N"),
                Code = AntigravityGatewayRouteDefaults.ProviderCode,
                Name = "Antigravity Agent Gateway",
                Dialect = AiProviderDialect.OpenAiCompatible,
                Category = AiProviderCategory.TextChat,
                BaseUrl = AntigravityGatewayRouteDefaults.GatewayBaseUrl,
                DefaultModel = "agent:writing-examiner",
                EncryptedApiKey = string.Empty,
                ApiKeyHint = "paste the gateway internal service token here",
                AllowedModelsCsv = "agent:writing-examiner,agent:speaking-interlocutor,agent:pronunciation-coach,agent:grammar-tutor,agent:reading-item-generator,agent:listening-item-generator,agent:drill-author,agent:mock-analysis",
                PricePer1kPromptTokens = 0m,
                PricePer1kCompletionTokens = 0m,
                FailoverPriority = 60,
                IsActive = true,
            });
            inserted++;
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
        try
        {
            var added = await AntigravityGatewaySeeder.SeedAsync(db, ct);
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
