using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Seeding.DocumentationContent;
using System.Text.Json;

namespace OetLearner.Api.Services.Seeding;

/// <summary>
/// Idempotent startup hook that seeds the Admin Documentation Center's 15 specialist
/// reports (the Master Dossier is DOC-01) straight to <c>Published</c> — owner
/// directive 2026-09-23: this is a real evidence pack for a Golden Residence
/// nomination, so nothing ships in Draft/Pending. Content lives in
/// <see cref="DocumentationContentCatalog"/> (one file per module, each sourced from
/// real code/config/git history — never invented facts).
///
/// Safety: additive only. Re-runs never touch a module that already has a current
/// version — an admin who edits content through the API keeps their edit; this
/// seeder only backfills modules that have no version yet.
/// </summary>
public sealed class DocumentationCenterSeeder(
    IServiceProvider services,
    ILogger<DocumentationCenterSeeder> logger) : IHostedService
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();

            var modules = DocumentationContentCatalog.GetAll();
            var existingModuleIds = new HashSet<string>(
                await db.DocumentationModules.AsNoTracking().Select(m => m.Id).ToListAsync(cancellationToken),
                StringComparer.OrdinalIgnoreCase);

            var now = DateTimeOffset.UtcNow;
            var insertedModules = 0;
            foreach (var seed in modules)
            {
                if (existingModuleIds.Contains(seed.Code)) continue;

                db.DocumentationModules.Add(new DocumentationModule
                {
                    Id = seed.Code,
                    Title = seed.Title,
                    Description = seed.Description,
                    SortOrder = seed.SortOrder,
                });

                db.DocumentationVersions.Add(new DocumentationVersion
                {
                    Id = $"DOCVER-{Guid.NewGuid():N}",
                    ModuleId = seed.Code,
                    VersionNumber = 1,
                    Status = DocumentationVersionStatus.Published,
                    ContentJson = JsonSerializer.Serialize(seed.Sections, JsonOpts),
                    GeneratedAt = now,
                    ApprovedByName = "Dr Ahmed Hesham",
                    ApprovedAt = now,
                    IsCurrent = true,
                });

                foreach (var ev in seed.Evidence)
                {
                    db.DocumentationEvidenceItems.Add(new DocumentationEvidenceItem
                    {
                        Id = $"DOCEV-{Guid.NewGuid():N}",
                        EvidenceId = ev.EvidenceId,
                        ModuleId = seed.Code,
                        EvidenceType = ev.EvidenceType,
                        Description = ev.Description,
                        SourceReference = ev.SourceReference,
                        IsInternalOnly = ev.IsInternalOnly,
                        CreatedAt = now,
                    });
                }

                insertedModules++;
            }

            if (insertedModules > 0)
            {
                await db.SaveChangesAsync(cancellationToken);
                logger.LogInformation("DocumentationCenterSeeder: inserted {Count} modules.", insertedModules);
            }
            else
            {
                logger.LogInformation("DocumentationCenterSeeder: all modules already exist.");
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "DocumentationCenterSeeder: skipped (DB unavailable or migration pending).");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
