using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Services.Seeding;

// Phase 1 (P1.3) of the OET Speaking module sequential plan.
//
// Seeds the three Speaking-specific feature-route override rows so the AI
// Gateway can route Speaking calls to the correct provider/model without
// an admin having to click through the route editor first:
//
//   * speaking.score.v2        → Anthropic claude-sonnet-5 (caching)
//   * speaking.patient.turn.v1 → Anthropic claude-sonnet-5  (caching)
//   * card.draft.v1            → Anthropic claude-sonnet-5 (caching)
//
// Strictly additive — never overwrites an existing row, so admins can
// retune routes via the editor without their changes being clobbered on
// the next restart. Mirrors the pattern used by
// AiAssistantFeatureRouteSeeder.cs.
//
// Two entry points:
//   * `SeedAsync(db, ct)` — call directly from a startup hook or a test
//     fixture. Returns the number of rows inserted.
//   * `SpeakingAiRouteSeedHostedService` — register in Program.cs via
//     `AddHostedService<SpeakingAiRouteSeedHostedService>()`. Idempotent
//     and safe to leave registered permanently.
//
// Even if no seeder runs, `AiFeatureRouteResolver.ResolveAsync` falls back
// to `SpeakingAiRouteDefaults.Defaults` for the three Speaking codes, so
// the gateway will still route correctly — the seeder just makes the
// route visible/editable in the admin route-editor UI.
public static class SpeakingAiRouteSeed
{
    /// <summary>Insert any missing default routes for the Speaking feature
    /// codes. Returns the number of rows inserted (0 when already
    /// seeded).</summary>
    public static async Task<int> SeedAsync(LearnerDbContext db, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var added = 0;

        foreach (var defaultRoute in SpeakingAiRouteDefaults.Defaults)
        {
            var exists = await db.AiFeatureRoutes
                .AnyAsync(r => r.FeatureCode == defaultRoute.FeatureCode, ct);
            if (exists) continue;

            db.AiFeatureRoutes.Add(new AiFeatureRoute
            {
                Id = Guid.NewGuid().ToString("N"),
                FeatureCode = defaultRoute.FeatureCode,
                ProviderCode = defaultRoute.PrimaryProviderCode,
                Model = defaultRoute.PrimaryModel,
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now,
            });
            added++;
        }

        if (added > 0)
        {
            await db.SaveChangesAsync(ct);
        }
        return added;
    }
}

/// <summary>
/// Hosted-service wrapper around <see cref="SpeakingAiRouteSeed.SeedAsync"/>.
/// Register in Program.cs:
/// <code>builder.Services.AddHostedService&lt;SpeakingAiRouteSeedHostedService&gt;();</code>
/// Mirrors the existing <c>AiAssistantFeatureRouteSeeder</c> pattern.
/// </summary>
public sealed class SpeakingAiRouteSeedHostedService(
    IServiceScopeFactory scopeFactory,
    ILogger<SpeakingAiRouteSeedHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        // Brief delay so any concurrent migration pass completes first.
        try
        {
            await Task.Delay(5000, ct);
        }
        catch (TaskCanceledException)
        {
            return;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        try
        {
            var added = await SpeakingAiRouteSeed.SeedAsync(db, ct);
            if (added > 0)
            {
                logger.LogInformation(
                    "Seeded {Count} Speaking AI feature routes (speaking.score.v2 / speaking.patient.turn.v1 / card.draft.v1).",
                    added);
            }
        }
        catch (Exception ex)
        {
            // Seeding is best-effort — a failure here must not crash the
            // host (the resolver fallback still works without DB rows).
            logger.LogWarning(ex, "Speaking AI route seed failed; resolver fallback will be used.");
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
