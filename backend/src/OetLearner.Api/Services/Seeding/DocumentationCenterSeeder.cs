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
/// Versioning: each seed carries a <see cref="DocumentationModuleSeed.Revision"/>.
/// A module with no rows is inserted at that version. A module whose highest stored
/// version is lower than the seed's revision gets the seed published as a new
/// version; the old current version is kept but marked <c>Superseded</c> (so earlier
/// PDF exports stay traceable) and the module's evidence rows are synced to the seed.
/// A module whose stored version is already at or above the seed's revision is never
/// touched, so re-runs are no-ops and any later, higher version is kept.
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
            var existingModules = await db.DocumentationModules
                .ToDictionaryAsync(m => m.Id, StringComparer.OrdinalIgnoreCase, cancellationToken);
            var latestVersions = await db.DocumentationVersions.AsNoTracking()
                .GroupBy(v => v.ModuleId)
                .Select(g => new { ModuleId = g.Key, Latest = g.Max(v => v.VersionNumber) })
                .ToDictionaryAsync(x => x.ModuleId, x => x.Latest, StringComparer.OrdinalIgnoreCase, cancellationToken);

            var now = DateTimeOffset.UtcNow;
            var insertedModules = 0;
            var revisedModules = 0;
            foreach (var seed in modules)
            {
                if (!existingModules.TryGetValue(seed.Code, out var module))
                {
                    db.DocumentationModules.Add(new DocumentationModule
                    {
                        Id = seed.Code,
                        Title = seed.Title,
                        Description = seed.Description,
                        SortOrder = seed.SortOrder,
                    });
                    AddPublishedVersion(db, seed.Code, seed, now);
                    foreach (var ev in seed.Evidence) AddEvidence(db, seed.Code, ev, now);
                    insertedModules++;
                    continue;
                }

                if (latestVersions.TryGetValue(module.Id, out var latest) && latest >= seed.Revision) continue;

                // Revised seed: supersede (never delete) the current version, publish the new one.
                var currentVersions = await db.DocumentationVersions
                    .Where(v => v.ModuleId == module.Id && v.IsCurrent)
                    .ToListAsync(cancellationToken);
                foreach (var v in currentVersions)
                {
                    v.IsCurrent = false;
                    v.Status = DocumentationVersionStatus.Superseded;
                }
                module.Title = seed.Title;
                module.Description = seed.Description;
                module.SortOrder = seed.SortOrder;
                AddPublishedVersion(db, module.Id, seed, now);

                // Evidence is keyed by the globally unique EvidenceId: update in place, add new, drop retired.
                var seededEvidence = seed.Evidence.ToDictionary(e => e.EvidenceId, StringComparer.OrdinalIgnoreCase);
                var storedEvidence = await db.DocumentationEvidenceItems
                    .Where(e => e.ModuleId == module.Id)
                    .ToListAsync(cancellationToken);
                foreach (var item in storedEvidence)
                {
                    if (seededEvidence.Remove(item.EvidenceId, out var ev))
                    {
                        item.EvidenceType = ev.EvidenceType;
                        item.Description = ev.Description;
                        item.SourceReference = ev.SourceReference;
                        item.IsInternalOnly = ev.IsInternalOnly;
                    }
                    else
                    {
                        db.DocumentationEvidenceItems.Remove(item);
                    }
                }
                foreach (var ev in seededEvidence.Values) AddEvidence(db, module.Id, ev, now);
                revisedModules++;
            }

            if (insertedModules > 0 || revisedModules > 0)
            {
                await db.SaveChangesAsync(cancellationToken);
                logger.LogInformation(
                    "DocumentationCenterSeeder: inserted {Inserted} modules, published revised content for {Revised} modules.",
                    insertedModules, revisedModules);
            }
            else
            {
                logger.LogInformation("DocumentationCenterSeeder: all modules already at their seeded revision.");
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "DocumentationCenterSeeder: skipped (DB unavailable or migration pending).");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static void AddPublishedVersion(LearnerDbContext db, string moduleId, DocumentationModuleSeed seed, DateTimeOffset now) =>
        db.DocumentationVersions.Add(new DocumentationVersion
        {
            Id = $"DOCVER-{Guid.NewGuid():N}",
            ModuleId = moduleId,
            VersionNumber = seed.Revision,
            Status = DocumentationVersionStatus.Published,
            ContentJson = JsonSerializer.Serialize(seed.Sections, JsonOpts),
            GeneratedAt = now,
            ApprovedByName = "Dr Ahmed Hesham",
            ApprovedAt = now,
            IsCurrent = true,
        });

    private static void AddEvidence(LearnerDbContext db, string moduleId, DocumentationEvidenceSeed ev, DateTimeOffset now) =>
        db.DocumentationEvidenceItems.Add(new DocumentationEvidenceItem
        {
            Id = $"DOCEV-{Guid.NewGuid():N}",
            EvidenceId = ev.EvidenceId,
            ModuleId = moduleId,
            EvidenceType = ev.EvidenceType,
            Description = ev.Description,
            SourceReference = ev.SourceReference,
            IsInternalOnly = ev.IsInternalOnly,
            CreatedAt = now,
        });
}
