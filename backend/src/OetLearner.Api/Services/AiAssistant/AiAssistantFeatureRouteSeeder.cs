using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.AiAssistant;

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

        // Seed default routes: all assistant features -> openai/gpt-4o
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
                    ProviderCode = "openai",
                    Model = "gpt-4o",
                    IsActive = true,
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow,
                });
            }
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("AI Assistant feature routes seeded");
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
