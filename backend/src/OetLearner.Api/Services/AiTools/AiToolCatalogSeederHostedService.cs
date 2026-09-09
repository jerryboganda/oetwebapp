using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.AiAssistant.Safety;

namespace OetLearner.Api.Services.AiTools;

/// <summary>
/// Phase 5 — Idempotent startup hook that ensures every registered
/// <see cref="IAiToolExecutor"/> has a matching active row in the
/// <c>AiTools</c> catalog. Runs once on host start; tolerates partial
/// failure (e.g. database not yet migrated) so the API still boots.
/// </summary>
public sealed class AiToolCatalogSeederHostedService(
    IServiceProvider services,
    ILogger<AiToolCatalogSeederHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = services.CreateScope();
            var registry = scope.ServiceProvider.GetRequiredService<IAiToolRegistry>();
            await registry.SeedCatalogAsync(cancellationToken);
            await SeedCompanionGrantsAsync(scope.ServiceProvider, cancellationToken);
            logger.LogInformation("AiToolCatalogSeeder: catalog reconciled.");
        }
        catch (Exception ex)
        {
            // Never block boot for this — tools simply won't resolve until
            // the catalog seeder succeeds on a later restart.
            logger.LogWarning(ex, "AiToolCatalogSeeder: skipping (DB unavailable or migration pending).");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// The AI Learning Companion action layer (S1.5) is only useful if the
    /// learner turn can actually resolve its tools, so the grants ship with the
    /// code rather than waiting for a manual admin step on every environment.
    ///
    /// <para>
    /// This is not a weakening of deny-by-default. These specific tool codes are
    /// on <c>AiToolRegistry.LearnerSafeToolCodes</c>, each resolves its target
    /// server-side, and every action additionally honours the
    /// <c>companion_actions</c> kill switch — which ships OFF. Existing grants
    /// are never modified, so an operator who deactivates one keeps it off.
    /// </para>
    ///
    /// <para>
    /// The grants are attached to <see cref="AiFeatureCodes.AiAssistantLearner"/>
    /// because that is the feature code the learner turn actually resolves
    /// today. Moving the learner branch onto <c>companion.chat.v1</c> for cost
    /// attribution is separate work (S1.7) and needs a quota plan row first.
    /// </para>
    /// </summary>
    private static readonly string[] CompanionToolCodes =
    [
        "companion_find_destination",
        "companion_open_destination",
        "companion_continue_last_activity",
        "companion_show_allowance",
        "companion_add_plan_item",
        "lookup_rulebook_rule",
        "lookup_vocabulary_term",
        "get_user_recent_attempts",
        "save_user_note",
        "bookmark_recall_term",
    ];

    /// <summary>
    /// Read-only toolset for the admin chatbot (`ai_assistant.admin`).
    /// Mutation tools (write_file / run_command / git_operations / deploy)
    /// are never granted here and are denied at SafetyGuard even if a
    /// leftover DB grant exists.
    /// </summary>
    private static readonly string[] AdminAssistantToolCodes =
    [
        "read_file",
        "search_codebase",
        "retrieve_codebase",
        "list_directory",
        "query_database",
        "web_search",
    ];

    private async Task SeedCompanionGrantsAsync(IServiceProvider provider, CancellationToken ct)
    {
        var db = provider.GetRequiredService<LearnerDbContext>();
        await SeedGrantsForFeatureAsync(db, provider, AiFeatureCodes.AiAssistantLearner, CompanionToolCodes, ct);
        await SeedGrantsForFeatureAsync(db, provider, AiFeatureCodes.AiAssistantAdmin, AdminAssistantToolCodes, ct);
        await DeactivateAdminMutationGrantsAsync(db, provider, ct);
    }

    /// <summary>
    /// Existing rows are otherwise never modified. Mutation grants for the
    /// admin chatbot are the exception: they must stay off.
    /// </summary>
    private async Task DeactivateAdminMutationGrantsAsync(
        LearnerDbContext db,
        IServiceProvider provider,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var grants = await db.AiFeatureToolGrants
            .Where(g => g.FeatureCode == AiFeatureCodes.AiAssistantAdmin && g.IsActive)
            .ToListAsync(ct);

        var deactivated = 0;
        foreach (var grant in grants)
        {
            if (!SafetyGuard.IsMutationTool(grant.ToolCode))
                continue;
            grant.IsActive = false;
            grant.UpdatedAt = now;
            deactivated++;
        }

        if (deactivated == 0) return;

        await db.SaveChangesAsync(ct);
        provider.GetRequiredService<IAiToolRegistry>().InvalidateFeature(AiFeatureCodes.AiAssistantAdmin);
        logger.LogInformation(
            "AiToolCatalogSeeder: deactivated {Count} mutation grant(s) on {FeatureCode}.",
            deactivated, AiFeatureCodes.AiAssistantAdmin);
    }

    private async Task SeedGrantsForFeatureAsync(
        LearnerDbContext db,
        IServiceProvider provider,
        string featureCode,
        IReadOnlyList<string> toolCodes,
        CancellationToken ct)
    {
        var existing = await db.AiFeatureToolGrants
            .Where(g => g.FeatureCode == featureCode)
            .Select(g => g.ToolCode)
            .ToListAsync(ct);

        var missing = toolCodes
            .Except(existing, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (missing.Count == 0) return;

        var now = DateTimeOffset.UtcNow;
        foreach (var toolCode in missing)
        {
            db.AiFeatureToolGrants.Add(new AiFeatureToolGrant
            {
                Id = Guid.NewGuid().ToString("N"),
                FeatureCode = featureCode,
                ToolCode = toolCode,
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now,
            });
        }

        await db.SaveChangesAsync(ct);
        provider.GetRequiredService<IAiToolRegistry>().InvalidateFeature(featureCode);
        logger.LogInformation(
            "AiToolCatalogSeeder: granted {Count} tool(s) to {FeatureCode}.",
            missing.Count, featureCode);
    }
}
