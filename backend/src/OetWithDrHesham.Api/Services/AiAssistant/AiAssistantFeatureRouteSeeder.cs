using Microsoft.EntityFrameworkCore;
using OetWithDrHesham.Api.Data;
using OetWithDrHesham.Api.Domain;

namespace OetWithDrHesham.Api.Services.AiAssistant;

/// <summary>
/// Seeds default AI feature routes for the AI Assistant on startup.
/// Strictly additive — never overwrites existing rows.
/// </summary>
public sealed class AiAssistantFeatureRouteSeeder(
    IServiceScopeFactory scopeFactory,
    ILogger<AiAssistantFeatureRouteSeeder> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        // Wait briefly for DB to be ready
        await Task.Delay(5000, ct);

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();

        var defaultProvider = await db.AiProviders.AsNoTracking()
            .Where(provider => provider.IsActive
                               && provider.Category == AiProviderCategory.TextChat
                               && !string.IsNullOrWhiteSpace(provider.EncryptedApiKey))
            .OrderBy(provider => provider.FailoverPriority)
            .FirstOrDefaultAsync(ct);
        if (defaultProvider is null)
        {
            logger.LogInformation("AI Assistant feature route seeding skipped; no active credentialed text-chat provider exists yet.");
            return;
        }

        var providerCode = defaultProvider.Code;
        var model = defaultProvider.DefaultModel;

        // Seed default routes from the active text-chat provider row when one
        // exists. Leaving Model empty lets the gateway use the provider default.
        var featureCodes = new[]
        {
            AiFeatureCodes.AiAssistantAdmin,
            AiFeatureCodes.AiAssistantExpert,
            AiFeatureCodes.AiAssistantLearner,
        };

        foreach (var code in featureCodes)
        {
            var exists = await db.AiFeatureRoutes
                .AnyAsync(r => r.FeatureCode == code, ct);
            if (!exists)
            {
                db.AiFeatureRoutes.Add(new AiFeatureRoute
                {
                    Id = Guid.NewGuid().ToString("N"),
                    FeatureCode = code,
                    ProviderCode = providerCode,
                    Model = model,
                    IsActive = true,
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow,
                });
            }
            else
            {
                var route = await db.AiFeatureRoutes.FirstAsync(r => r.FeatureCode == code, ct);
                if (IsLegacyOpenAiAssistantRoute(route.ProviderCode, route.Model))
                {
                    route.ProviderCode = providerCode;
                    route.Model = model;
                    route.UpdatedAt = DateTimeOffset.UtcNow;
                }
            }
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("AI Assistant feature routes seeded");
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

    private static bool IsLegacyOpenAiAssistantRoute(string? providerCode, string? model)
    {
        var providerIsLegacy = string.Equals(providerCode, "openai", StringComparison.OrdinalIgnoreCase)
                               || string.Equals(providerCode, "openai-compatible", StringComparison.OrdinalIgnoreCase);
        var modelIsLegacy = !string.IsNullOrWhiteSpace(model)
                            && (model.StartsWith("gpt-4", StringComparison.OrdinalIgnoreCase)
                                || model.StartsWith("gpt-5", StringComparison.OrdinalIgnoreCase));
        return providerIsLegacy || modelIsLegacy;
    }
}
