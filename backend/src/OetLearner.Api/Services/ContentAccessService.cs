using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services;

public class ContentAccessService(LearnerDbContext db, ContentHierarchyService hierarchy)
{
    private const int MaxPageSize = 100;

    /// <summary>
    /// Browse content items with access annotations.
    /// Returns all published items with isAccessible/isPreview flags based on user's subscription.
    /// </summary>
    public async Task<object> BrowseContentAsync(
        string userId, string? subtestCode, string? professionId,
        string? difficulty, string? language, string? provenance,
        int page, int pageSize, CancellationToken ct)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);
        var offset = (int)Math.Min((long)(page - 1) * pageSize, int.MaxValue);

        var accessibleIds = await hierarchy.ResolveAccessibleContentIdsAsync(userId, ct);

        var query = db.ContentItems
            .AsNoTracking()
            .Where(c => c.Status == ContentStatus.Published && c.FreshnessConfidence != "superseded");

        if (!string.IsNullOrEmpty(subtestCode))
            query = query.Where(c => c.SubtestCode == subtestCode);
        if (!string.IsNullOrEmpty(professionId))
            query = query.Where(c => c.ProfessionId == professionId || c.ProfessionId == null);
        if (!string.IsNullOrEmpty(difficulty))
            query = query.Where(c => c.Difficulty == difficulty);
        if (!string.IsNullOrEmpty(language))
            query = query.Where(c => c.InstructionLanguage == language);
        if (!string.IsNullOrEmpty(provenance))
            query = query.Where(c => c.SourceProvenance == provenance);

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderBy(c => c.SubtestCode).ThenBy(c => c.Title)
            .Skip(offset).Take(pageSize)
            .Select(c => new
            {
                c.Id,
                c.ContentType,
                c.SubtestCode,
                c.ProfessionId,
                c.Title,
                c.Difficulty,
                c.EstimatedDurationMinutes,
                c.ScenarioType,
                c.InstructionLanguage,
                c.SourceProvenance,
                c.QualityScore,
                c.IsPreviewEligible
            })
            .ToListAsync(ct);

        var hasSubscription = accessibleIds.Count > 0;
        var result = items.Select(c => new
        {
            contentId = c.Id,
            contentType = c.ContentType,
            subtest = c.SubtestCode,
            professionId = c.ProfessionId,
            title = c.Title,
            difficulty = c.Difficulty,
            estimatedDurationMinutes = c.EstimatedDurationMinutes,
            scenarioType = c.ScenarioType,
            instructionLanguage = c.InstructionLanguage,
            sourceProvenance = c.SourceProvenance,
            qualityScore = c.QualityScore,

            // Access flags
            isAccessible = hasSubscription && accessibleIds.Contains(c.Id),
            isPreview = c.IsPreviewEligible,
            requiresUpgrade = hasSubscription && !accessibleIds.Contains(c.Id) && !c.IsPreviewEligible,
            noSubscription = !hasSubscription && !c.IsPreviewEligible
        }).ToList();

        return new { items = result, total, page, pageSize };
    }

    /// <summary>
    /// Check whether a specific content item is accessible to the user.
    /// Returns access status and the reason.
    /// </summary>
    public async Task<ContentAccessCheck> CheckAccessAsync(string userId, string contentItemId, CancellationToken ct)
    {
        var item = await db.ContentItems
            .AsNoTracking()
            .Where(content => content.Id == contentItemId)
            .Select(content => new
            {
                content.Status,
                content.FreshnessConfidence,
                content.IsPreviewEligible
            })
            .SingleOrDefaultAsync(ct);
        if (item is null
            || item.Status != ContentStatus.Published
            || item.FreshnessConfidence == "superseded")
            return new ContentAccessCheck(false, "not_found", null);

        if (item.IsPreviewEligible)
            return new ContentAccessCheck(true, "free_preview", null);

        var accessibleIds = await hierarchy.ResolveAccessibleContentIdsAsync(userId, ct);
        if (accessibleIds.Contains(contentItemId))
            return new ContentAccessCheck(true, "subscription", null);

        // Find which package they'd need
        var packageForContent = await db.PackageContentRules
            .AsNoTracking()
            .Where(r => r.TargetId == contentItemId
                        && r.TargetType == "content_item"
                        && (r.RuleType == "include_content_item" || r.RuleType == "include_item"))
            .Select(r => r.PackageId)
            .FirstOrDefaultAsync(ct);

        return new ContentAccessCheck(false, "locked", packageForContent);
    }

    /// <summary>
    /// Browse programs with access info for the user.
    /// </summary>
    public async Task<object> BrowseProgramsWithAccessAsync(
        string userId, string? type, string? language, int page, int pageSize, CancellationToken ct)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);
        var offset = (int)Math.Min((long)(page - 1) * pageSize, int.MaxValue);

        var userPackageIds = await db.ContentPackages
            .AsNoTracking()
            .Where(package => package.Status == ContentStatus.Published
                              && package.BillingPlanId != null
                              && db.Subscriptions.Any(subscription =>
                                  subscription.UserId == userId
                                  && subscription.Status == SubscriptionStatus.Active
                                  && subscription.PlanId == package.BillingPlanId))
            .Select(package => package.Id)
            .Distinct()
            .ToListAsync(ct);

        var query = db.ContentPrograms
            .AsNoTracking()
            .Where(p => p.Status == ContentStatus.Published);
        if (!string.IsNullOrEmpty(type)) query = query.Where(p => p.ProgramType == type);
        if (!string.IsNullOrEmpty(language)) query = query.Where(p => p.InstructionLanguage == language);

        var total = await query.CountAsync(ct);
        var programs = await query.OrderBy(p => p.DisplayOrder)
            .Skip(offset).Take(pageSize)
            .Select(p => new
            {
                p.Id,
                p.Code,
                p.Title,
                p.Description,
                p.ProfessionId,
                p.InstructionLanguage,
                p.ProgramType,
                p.ThumbnailUrl,
                p.DisplayOrder,
                p.EstimatedDurationMinutes
            })
            .ToListAsync(ct);

        var programIds = programs.Select(p => p.Id).ToList();
        var trackCounts = programIds.Count == 0
            ? new Dictionary<string, int>()
            : await db.ContentTracks
                .AsNoTracking()
                .Where(t => programIds.Contains(t.ProgramId))
                .GroupBy(t => t.ProgramId)
                .Select(group => new { ProgramId = group.Key, Count = group.Count() })
                .ToDictionaryAsync(item => item.ProgramId, item => item.Count, ct);

        // Limit the rule query to the user's eligible packages and the current page.
        var accessibleProgramIds = userPackageIds.Count > 0 && programIds.Count > 0
            ? await db.PackageContentRules
                .AsNoTracking()
                .Where(rule => rule.RuleType == "include_program"
                               && userPackageIds.Contains(rule.PackageId)
                               && programIds.Contains(rule.TargetId))
                .Select(rule => rule.TargetId)
                .Distinct()
                .ToHashSetAsync(ct)
            : [];

        var result = programs.Select(p => new
        {
            p.Id, p.Code, p.Title, p.Description, p.ProfessionId,
            p.InstructionLanguage, p.ProgramType, p.ThumbnailUrl,
            p.DisplayOrder, p.EstimatedDurationMinutes,
            isAccessible = accessibleProgramIds.Contains(p.Id),
            trackCount = trackCounts.GetValueOrDefault(p.Id)
        }).ToList();

        return new { items = result, total, page, pageSize };
    }
}

public record ContentAccessCheck(bool IsAccessible, string Reason, string? RequiredPackageId);
