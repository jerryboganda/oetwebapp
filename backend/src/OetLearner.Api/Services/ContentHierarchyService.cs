using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services;

public class ContentHierarchyService(LearnerDbContext db)
{
    // ── Programs ──

    public async Task<object> GetProgramsAsync(string? type, string? language, string? status, int page, int pageSize, CancellationToken ct)
    {
        var query = db.ContentPrograms.AsQueryable();
        if (!string.IsNullOrEmpty(type)) query = query.Where(p => p.ProgramType == type);
        if (!string.IsNullOrEmpty(language)) query = query.Where(p => p.InstructionLanguage == language);
        if (!string.IsNullOrEmpty(status))
        {
            if (Enum.TryParse<ContentStatus>(status, true, out var s)) query = query.Where(p => p.Status == s);
        }
        var total = await query.CountAsync(ct);
        var items = await query.OrderBy(p => p.DisplayOrder).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new { items, total, page, pageSize };
    }

    public async Task<ContentProgram?> GetProgramAsync(string programId, CancellationToken ct)
        => await db.ContentPrograms.FindAsync([programId], ct);

    public async Task<ContentProgram> CreateProgramAsync(string adminId, ContentProgram program, CancellationToken ct)
    {
        program.Id = $"prg-{Guid.NewGuid():N}"[..32];
        program.CreatedBy = adminId;
        program.CreatedAt = DateTimeOffset.UtcNow;
        program.UpdatedAt = DateTimeOffset.UtcNow;
        db.ContentPrograms.Add(program);
        await db.SaveChangesAsync(ct);
        return program;
    }

    public async Task<ContentProgram?> UpdateProgramAsync(string programId, ContentProgram update, CancellationToken ct)
    {
        var existing = await db.ContentPrograms.FindAsync([programId], ct);
        if (existing is null) return null;
        existing.Title = update.Title;
        existing.Description = update.Description;
        existing.ProfessionId = update.ProfessionId;
        existing.InstructionLanguage = update.InstructionLanguage;
        existing.ProgramType = update.ProgramType;
        existing.ThumbnailUrl = update.ThumbnailUrl;
        existing.DisplayOrder = update.DisplayOrder;
        existing.EstimatedDurationMinutes = update.EstimatedDurationMinutes;
        existing.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return existing;
    }

    // ── Tracks ──

    public async Task<List<ContentTrack>> GetTracksAsync(string programId, CancellationToken ct)
        => await db.ContentTracks.Where(t => t.ProgramId == programId).OrderBy(t => t.DisplayOrder).ToListAsync(ct);

    public async Task<ContentTrack> CreateTrackAsync(ContentTrack track, CancellationToken ct)
    {
        track.Id = $"trk-{Guid.NewGuid():N}"[..32];
        db.ContentTracks.Add(track);
        await db.SaveChangesAsync(ct);
        return track;
    }

    public async Task<ContentTrack?> UpdateTrackAsync(string trackId, ContentTrack update, CancellationToken ct)
    {
        var existing = await db.ContentTracks.FindAsync([trackId], ct);
        if (existing is null) return null;
        existing.Title = update.Title;
        existing.Description = update.Description;
        existing.SubtestCode = update.SubtestCode;
        existing.DisplayOrder = update.DisplayOrder;
        existing.Status = update.Status;
        await db.SaveChangesAsync(ct);
        return existing;
    }

    // ── Modules ──

    public async Task<List<ContentModule>> GetModulesAsync(string trackId, CancellationToken ct)
        => await db.ContentModules.Where(m => m.TrackId == trackId).OrderBy(m => m.DisplayOrder).ToListAsync(ct);

    public async Task<ContentModule> CreateModuleAsync(ContentModule module, CancellationToken ct)
    {
        module.Id = $"mod-{Guid.NewGuid():N}"[..32];
        db.ContentModules.Add(module);
        await db.SaveChangesAsync(ct);
        return module;
    }

    public async Task<ContentModule?> UpdateModuleAsync(string moduleId, ContentModule update, CancellationToken ct)
    {
        var existing = await db.ContentModules.FindAsync([moduleId], ct);
        if (existing is null) return null;
        existing.Title = update.Title;
        existing.Description = update.Description;
        existing.DisplayOrder = update.DisplayOrder;
        existing.EstimatedDurationMinutes = update.EstimatedDurationMinutes;
        existing.PrerequisiteModuleId = update.PrerequisiteModuleId;
        existing.Status = update.Status;
        await db.SaveChangesAsync(ct);
        return existing;
    }

    // ── Lessons ──

    public async Task<List<ContentLesson>> GetLessonsAsync(string moduleId, CancellationToken ct)
        => await db.ContentLessons.Where(l => l.ModuleId == moduleId).OrderBy(l => l.DisplayOrder).ToListAsync(ct);

    public async Task<ContentLesson> CreateLessonAsync(ContentLesson lesson, CancellationToken ct)
    {
        lesson.Id = $"lsn-{Guid.NewGuid():N}"[..32];
        db.ContentLessons.Add(lesson);
        await db.SaveChangesAsync(ct);
        return lesson;
    }

    public async Task<ContentLesson?> UpdateLessonAsync(string lessonId, ContentLesson update, CancellationToken ct)
    {
        var existing = await db.ContentLessons.FindAsync([lessonId], ct);
        if (existing is null) return null;
        existing.ModuleId = update.ModuleId;
        existing.ContentItemId = update.ContentItemId;
        existing.Title = update.Title;
        existing.LessonType = update.LessonType;
        existing.MediaAssetId = update.MediaAssetId;
        existing.DisplayOrder = update.DisplayOrder;
        existing.Status = update.Status;
        await db.SaveChangesAsync(ct);
        return existing;
    }

    // ── Packages ──

    public async Task<object> GetPackagesAsync(string? type, string? status, int page, int pageSize, CancellationToken ct)
    {
        var query = db.ContentPackages.AsQueryable();
        if (!string.IsNullOrEmpty(type)) query = query.Where(p => p.PackageType == type);
        if (!string.IsNullOrEmpty(status))
        {
            if (Enum.TryParse<ContentStatus>(status, true, out var s)) query = query.Where(p => p.Status == s);
        }
        var total = await query.CountAsync(ct);
        var items = await query.OrderBy(p => p.DisplayOrder).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new { items, total, page, pageSize };
    }

    public async Task<ContentPackage?> GetPackageAsync(string packageId, CancellationToken ct)
        => await db.ContentPackages.FindAsync([packageId], ct);

    public async Task<ContentPackage> CreatePackageAsync(ContentPackage package, CancellationToken ct)
    {
        package.Id = $"pkg-{Guid.NewGuid():N}"[..32];
        package.CreatedAt = DateTimeOffset.UtcNow;
        package.UpdatedAt = DateTimeOffset.UtcNow;
        db.ContentPackages.Add(package);
        await db.SaveChangesAsync(ct);
        return package;
    }

    public async Task<ContentPackage?> UpdatePackageAsync(string packageId, ContentPackage update, CancellationToken ct)
    {
        var existing = await db.ContentPackages.FindAsync([packageId], ct);
        if (existing is null) return null;
        existing.Title = update.Title;
        existing.Description = update.Description;
        existing.PackageType = update.PackageType;
        existing.ProfessionId = update.ProfessionId;
        existing.InstructionLanguage = update.InstructionLanguage;
        existing.BillingPlanId = update.BillingPlanId;
        existing.ThumbnailUrl = update.ThumbnailUrl;
        existing.ComparisonFeaturesJson = update.ComparisonFeaturesJson;
        existing.DisplayOrder = update.DisplayOrder;
        existing.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return existing;
    }

    // ── Package Content Rules ──

    public async Task<List<PackageContentRule>> GetPackageRulesAsync(string packageId, CancellationToken ct)
        => await db.PackageContentRules.Where(r => r.PackageId == packageId).ToListAsync(ct);

    public async Task<PackageContentRule> AddPackageRuleAsync(PackageContentRule rule, CancellationToken ct)
    {
        rule.Id = $"pcr-{Guid.NewGuid():N}"[..32];
        db.PackageContentRules.Add(rule);
        await db.SaveChangesAsync(ct);
        return rule;
    }

    public async Task<bool> RemovePackageRuleAsync(string ruleId, CancellationToken ct)
    {
        var rule = await db.PackageContentRules.FindAsync([ruleId], ct);
        if (rule is null) return false;
        db.PackageContentRules.Remove(rule);
        await db.SaveChangesAsync(ct);
        return true;
    }

    // ── Package Access Resolution ──

    public async Task<HashSet<string>> ResolveAccessibleContentIdsAsync(string userId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        // Resolve all eligible rules in one command. Joining subscriptions here preserves
        // the union of every active plan while ensuring only published packages participate.
        var rules = await (
                from rule in db.PackageContentRules.AsNoTracking()
                join package in db.ContentPackages.AsNoTracking()
                    on rule.PackageId equals package.Id
                join subscription in db.Subscriptions.AsNoTracking()
                    on package.BillingPlanId equals subscription.PlanId
                where subscription.UserId == userId
                      && subscription.Status == SubscriptionStatus.Active
                      && package.Status == ContentStatus.Published
                select new { rule.PackageId, rule.RuleType, rule.TargetType, rule.TargetId })
            .Distinct()
            .ToListAsync(ct);

        if (rules.Count == 0) return [];

        var relevantRules = rules
            .Where(rule => rule.RuleType.StartsWith("include_", StringComparison.Ordinal)
                           || rule.RuleType.StartsWith("exclude_", StringComparison.Ordinal))
            .ToList();
        var expandedTargets =
            new Dictionary<(string TargetType, string TargetId), HashSet<string>>();

        static void AddExpandedTarget(
            Dictionary<(string TargetType, string TargetId), HashSet<string>> targets,
            string targetType,
            string targetId,
            string contentId)
        {
            if (!targets.TryGetValue((targetType, targetId), out var contentIds))
            {
                contentIds = new HashSet<string>(StringComparer.Ordinal);
                targets[(targetType, targetId)] = contentIds;
            }

            contentIds.Add(contentId);
        }

        ct.ThrowIfCancellationRequested();
        var itemTargetIds = relevantRules
            .Where(rule => rule.TargetType == "content_item")
            .Select(rule => rule.TargetId)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (itemTargetIds.Length > 0)
        {
            var publishedItemIds = await db.ContentItems
                .AsNoTracking()
                .Where(item => itemTargetIds.Contains(item.Id)
                               && item.Status == ContentStatus.Published
                               && item.FreshnessConfidence != "superseded")
                .Select(item => item.Id)
                .ToListAsync(ct);

            foreach (var contentId in publishedItemIds)
                AddExpandedTarget(expandedTargets, "content_item", contentId, contentId);
        }

        ct.ThrowIfCancellationRequested();
        var moduleTargetIds = relevantRules
            .Where(rule => rule.TargetType == "module")
            .Select(rule => rule.TargetId)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (moduleTargetIds.Length > 0)
        {
            var moduleContent = await (
                    from module in db.ContentModules.AsNoTracking()
                    join lesson in db.ContentLessons.AsNoTracking()
                        on module.Id equals lesson.ModuleId
                    join item in db.ContentItems.AsNoTracking()
                        on lesson.ContentItemId equals item.Id
                    where moduleTargetIds.Contains(module.Id)
                          && module.Status == ContentStatus.Published
                          && lesson.Status == ContentStatus.Published
                          && item.Status == ContentStatus.Published
                          && item.FreshnessConfidence != "superseded"
                    select new { TargetId = module.Id, ContentId = item.Id })
                .Distinct()
                .ToListAsync(ct);

            foreach (var row in moduleContent)
                AddExpandedTarget(expandedTargets, "module", row.TargetId, row.ContentId);
        }

        ct.ThrowIfCancellationRequested();
        var trackTargetIds = relevantRules
            .Where(rule => rule.TargetType == "track")
            .Select(rule => rule.TargetId)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (trackTargetIds.Length > 0)
        {
            var trackContent = await (
                    from track in db.ContentTracks.AsNoTracking()
                    join module in db.ContentModules.AsNoTracking()
                        on track.Id equals module.TrackId
                    join lesson in db.ContentLessons.AsNoTracking()
                        on module.Id equals lesson.ModuleId
                    join item in db.ContentItems.AsNoTracking()
                        on lesson.ContentItemId equals item.Id
                    where trackTargetIds.Contains(track.Id)
                          && track.Status == ContentStatus.Published
                          && module.Status == ContentStatus.Published
                          && lesson.Status == ContentStatus.Published
                          && item.Status == ContentStatus.Published
                          && item.FreshnessConfidence != "superseded"
                    select new { TargetId = track.Id, ContentId = item.Id })
                .Distinct()
                .ToListAsync(ct);

            foreach (var row in trackContent)
                AddExpandedTarget(expandedTargets, "track", row.TargetId, row.ContentId);
        }

        ct.ThrowIfCancellationRequested();
        var programTargetIds = relevantRules
            .Where(rule => rule.TargetType == "program")
            .Select(rule => rule.TargetId)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (programTargetIds.Length > 0)
        {
            var programContent = await (
                    from program in db.ContentPrograms.AsNoTracking()
                    join track in db.ContentTracks.AsNoTracking()
                        on program.Id equals track.ProgramId
                    join module in db.ContentModules.AsNoTracking()
                        on track.Id equals module.TrackId
                    join lesson in db.ContentLessons.AsNoTracking()
                        on module.Id equals lesson.ModuleId
                    join item in db.ContentItems.AsNoTracking()
                        on lesson.ContentItemId equals item.Id
                    where programTargetIds.Contains(program.Id)
                          && program.Status == ContentStatus.Published
                          && track.Status == ContentStatus.Published
                          && module.Status == ContentStatus.Published
                          && lesson.Status == ContentStatus.Published
                          && item.Status == ContentStatus.Published
                          && item.FreshnessConfidence != "superseded"
                    select new { TargetId = program.Id, ContentId = item.Id })
                .Distinct()
                .ToListAsync(ct);

            foreach (var row in programContent)
                AddExpandedTarget(expandedTargets, "program", row.TargetId, row.ContentId);
        }

        // Exclusions carve content out of their own package. The learner's effective
        // entitlement is the union of each active package's resolved content.
        var accessibleContentIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var packageRules in relevantRules.GroupBy(rule => rule.PackageId))
        {
            ct.ThrowIfCancellationRequested();
            var packageIncludes = new HashSet<string>(StringComparer.Ordinal);
            var packageExcludes = new HashSet<string>(StringComparer.Ordinal);

            foreach (var rule in packageRules)
            {
                if (!expandedTargets.TryGetValue(
                        (rule.TargetType, rule.TargetId),
                        out var targetContentIds))
                {
                    continue;
                }

                if (rule.RuleType.StartsWith("include_", StringComparison.Ordinal))
                    packageIncludes.UnionWith(targetContentIds);
                else
                    packageExcludes.UnionWith(targetContentIds);
            }

            packageIncludes.ExceptWith(packageExcludes);
            accessibleContentIds.UnionWith(packageIncludes);
        }

        return accessibleContentIds;
    }

    // ── Import Batches ──

    public async Task<object> GetImportBatchesAsync(int page, int pageSize, CancellationToken ct)
    {
        var total = await db.ContentImportBatches.CountAsync(ct);
        var items = await db.ContentImportBatches.OrderByDescending(b => b.CreatedAt)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new { items, total, page, pageSize };
    }

    public async Task<ContentImportBatch> CreateImportBatchAsync(string adminId, string title, CancellationToken ct)
    {
        var batch = new ContentImportBatch
        {
            Id = $"batch-{Guid.NewGuid():N}"[..32],
            Title = title,
            Status = "pending",
            CreatedBy = adminId,
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.ContentImportBatches.Add(batch);
        await db.SaveChangesAsync(ct);
        return batch;
    }

    public async Task<bool> RollbackImportBatchAsync(string batchId, CancellationToken ct)
    {
        var batch = await db.ContentImportBatches.FindAsync([batchId], ct);
        if (batch is null) return false;

        // Remove all content items in this batch
        var items = await db.ContentItems.Where(c => c.ImportBatchId == batchId).ToListAsync(ct);
        db.ContentItems.RemoveRange(items);

        batch.Status = "rolled_back";
        await db.SaveChangesAsync(ct);
        return true;
    }

    // ── Free Preview Assets ──

    public async Task<List<FreePreviewAssetLearnerDto>> GetFreePreviewAssetsAsync(CancellationToken ct)
        => await db.FreePreviewAssets
            .AsNoTracking()
            .Where(a => a.Status == ContentStatus.Published)
            .OrderBy(a => a.DisplayOrder)
            .Select(a => new FreePreviewAssetLearnerDto(
                a.Id,
                a.Title,
                a.PreviewType,
                a.ContentItemId,
                a.ConversionCtaText,
                a.TargetPackageId,
                a.Status,
                a.DisplayOrder,
                a.CreatedAt))
            .ToListAsync(ct);

    // ── Testimonials ──

    public async Task<object> GetTestimonialsAsync(int page, int pageSize, CancellationToken ct)
    {
        var total = await db.TestimonialAssets.CountAsync(ct);
        var items = await db.TestimonialAssets
            .OrderBy(t => t.DisplayOrder)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(ct);
        return new { items, total, page, pageSize };
    }

    public async Task<TestimonialAsset> CreateTestimonialAsync(TestimonialAsset testimonial, CancellationToken ct)
    {
        testimonial.Id = $"tst-{Guid.NewGuid():N}"[..32];
        testimonial.CreatedAt = DateTimeOffset.UtcNow;
        db.TestimonialAssets.Add(testimonial);
        await db.SaveChangesAsync(ct);
        return testimonial;
    }

    // ── Foundation Resources ──

    public async Task<List<FoundationResource>> GetFoundationResourcesAsync(string? type, CancellationToken ct)
    {
        var query = db.FoundationResources.AsQueryable();
        if (!string.IsNullOrEmpty(type)) query = query.Where(r => r.ResourceType == type);
        return await query.OrderBy(r => r.DisplayOrder).ToListAsync(ct);
    }

    public async Task<FoundationResource> CreateFoundationResourceAsync(FoundationResource resource, CancellationToken ct)
    {
        resource.Id = $"fnd-{Guid.NewGuid():N}"[..32];
        resource.CreatedAt = DateTimeOffset.UtcNow;
        resource.UpdatedAt = DateTimeOffset.UtcNow;
        db.FoundationResources.Add(resource);
        await db.SaveChangesAsync(ct);
        return resource;
    }
}

public sealed record FreePreviewAssetLearnerDto(
    string Id,
    string Title,
    string PreviewType,
    string? ContentItemId,
    string? ConversionCtaText,
    string? TargetPackageId,
    ContentStatus Status,
    int DisplayOrder,
    DateTimeOffset CreatedAt);
