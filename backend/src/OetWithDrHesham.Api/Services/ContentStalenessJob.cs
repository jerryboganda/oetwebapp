using Microsoft.EntityFrameworkCore;
using OetWithDrHesham.Api.Data;
using OetWithDrHesham.Api.Domain;

namespace OetWithDrHesham.Api.Services;

/// <summary>
/// Background job that computes content staleness assessments for all published content.
/// Per docs/product-strategy/06_feature_strategy_and_blueprint.md:
/// "Content provenance/QA analytics" requires periodic staleness computation.
/// </summary>
public interface IContentStalenessService
{
    Task<List<ContentStalenessAssessment>> ComputeAllAsync(CancellationToken ct);
    Task<ContentStalenessAssessment?> ComputeForContentAsync(string contentItemId, CancellationToken ct);
    Task<List<ContentStalenessAssessment>> GetStaleContentAsync(int thresholdDays, CancellationToken ct);
}

public sealed record ContentStalenessAssessment(
    string ContentItemId,
    string Title,
    int DaysSinceLastEdit,
    int? DaysSinceLastUsage,
    int UsageCountLast90Days,
    double RubricCoveragePercent,
    List<string> MissingRubricCriteria,
    bool IsStale,
    string StalenessReason,
    string RecommendedAction); // no_action, minor_refresh, major_revision, archive

public sealed class ContentStalenessService(LearnerDbContext db, ILogger<ContentStalenessService> logger) : IContentStalenessService
{
    private const int StalenessThresholdDays = 180; // 6 months
    private const int MajorRevisionThresholdDays = 365; // 1 year
    private const int LowUsageThreshold = 5; // fewer than 5 uses in 90 days

    public async Task<List<ContentStalenessAssessment>> ComputeAllAsync(CancellationToken ct)
    {
        var publishedContent = await db.ContentItems
            .AsNoTracking()
            .Where(c => c.Status == ContentStatus.Published)
            .ToListAsync(ct);

        if (publishedContent.Count == 0)
        {
            logger.LogInformation("Computed staleness for 0 content items");
            return [];
        }

        var now = DateTimeOffset.UtcNow;
        var ninetyDaysAgo = now.AddDays(-90);
        var contentIds = publishedContent.Select(content => content.Id).ToArray();
        var subtestCodes = publishedContent
            .Select(content => content.SubtestCode)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var attemptAggregates = await db.Attempts
            .AsNoTracking()
            .Where(attempt => contentIds.Contains(attempt.ContentId))
            .GroupBy(attempt => attempt.ContentId)
            .Select(group => new
            {
                ContentItemId = group.Key,
                UsageCountLast90Days = group.Count(attempt => attempt.CreatedAt >= ninetyDaysAgo),
                LastUsage = group.Max(attempt => (DateTimeOffset?)attempt.CreatedAt)
            })
            .ToListAsync(ct);
        var attemptsByContent = attemptAggregates.ToDictionary(
            aggregate => aggregate.ContentItemId,
            aggregate => new AttemptUsageAggregate(
                aggregate.UsageCountLast90Days,
                aggregate.LastUsage),
            StringComparer.Ordinal);

        var criteriaRows = await db.Criteria
            .AsNoTracking()
            .Where(criterion => subtestCodes.Contains(criterion.SubtestCode))
            .Select(criterion => new { criterion.SubtestCode, criterion.Code })
            .ToListAsync(ct);
        var criteriaBySubtest = criteriaRows
            .GroupBy(criterion => criterion.SubtestCode, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<string>)group.Select(criterion => criterion.Code).ToList(),
                StringComparer.Ordinal);

        var results = new List<ContentStalenessAssessment>(publishedContent.Count);
        foreach (var content in publishedContent)
        {
            attemptsByContent.TryGetValue(content.Id, out var usage);
            criteriaBySubtest.TryGetValue(content.SubtestCode, out var criteria);
            results.Add(AssessContent(
                content,
                usage ?? AttemptUsageAggregate.Empty,
                criteria ?? [],
                now));
        }

        logger.LogInformation("Computed staleness for {Count} content items", results.Count);
        return results;
    }

    public async Task<ContentStalenessAssessment?> ComputeForContentAsync(string contentItemId, CancellationToken ct)
    {
        var content = await db.ContentItems
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == contentItemId, ct);

        if (content is null) return null;

        var now = DateTimeOffset.UtcNow;
        var ninetyDaysAgo = now.AddDays(-90);

        // Usage count in last 90 days
        var usageCount = await db.Attempts
            .AsNoTracking()
            .CountAsync(a => a.ContentId == contentItemId && a.CreatedAt >= ninetyDaysAgo, ct);

        // Days since last usage
        var lastUsage = await db.Attempts
            .AsNoTracking()
            .Where(a => a.ContentId == contentItemId)
            .OrderByDescending(a => a.CreatedAt)
            .Select(a => (DateTimeOffset?)a.CreatedAt)
            .FirstOrDefaultAsync(ct);
        var daysSinceUsage = lastUsage is not null ? (int?)(now - lastUsage.Value).Days : null;

        var allCriteria = await db.Criteria
            .AsNoTracking()
            .Where(c => c.SubtestCode == content.SubtestCode)
            .Select(c => c.Code)
            .ToListAsync(ct);

        return AssessContent(
            content,
            new AttemptUsageAggregate(usageCount, lastUsage),
            allCriteria,
            now);
    }

    private static ContentStalenessAssessment AssessContent(
        ContentItem content,
        AttemptUsageAggregate usage,
        IReadOnlyList<string> allCriteria,
        DateTimeOffset now)
    {
        // Days since last edit
        var lastEdit = content.UpdatedAt > content.CreatedAt ? content.UpdatedAt : content.CreatedAt;
        var daysSinceEdit = (now - lastEdit).Days;
        var usageCount = usage.UsageCountLast90Days;
        var daysSinceUsage = usage.LastUsage is not null ? (int?)(now - usage.LastUsage.Value).Days : null;

        // Rubric coverage
        var rubricCriteria = !string.IsNullOrWhiteSpace(content.CriteriaFocusJson) && content.CriteriaFocusJson != "[]"
            ? System.Text.Json.JsonSerializer.Deserialize<List<string>>(content.CriteriaFocusJson) ?? new List<string>()
            : new List<string>();
        var covered = rubricCriteria.Count;
        var total = allCriteria.Count;
        var coveragePercent = total > 0 ? (covered * 100.0 / total) : 100.0;
        var missing = allCriteria.Except(rubricCriteria, StringComparer.OrdinalIgnoreCase).ToList();

        // Determine staleness
        var isStale = daysSinceEdit > StalenessThresholdDays
            || (daysSinceUsage is not null && daysSinceUsage > StalenessThresholdDays)
            || usageCount < LowUsageThreshold
            || coveragePercent < 50.0;

        var reason = new List<string>();
        if (daysSinceEdit > StalenessThresholdDays)
            reason.Add($"Last edited {daysSinceEdit} days ago");
        if (daysSinceUsage is not null && daysSinceUsage > StalenessThresholdDays)
            reason.Add($"Last used {daysSinceUsage} days ago");
        if (usageCount < LowUsageThreshold)
            reason.Add($"Only {usageCount} uses in last 90 days");
        if (coveragePercent < 50.0)
            reason.Add($"Rubric coverage {coveragePercent:F0}% is below threshold");

        var recommendedAction = daysSinceEdit > MajorRevisionThresholdDays ? "archive" :
            daysSinceEdit > StalenessThresholdDays ? "major_revision" :
            usageCount < LowUsageThreshold || coveragePercent < 50.0 ? "minor_refresh" :
            "no_action";

        return new ContentStalenessAssessment(
            content.Id,
            content.Title,
            daysSinceEdit,
            daysSinceUsage,
            usageCount,
            coveragePercent,
            missing,
            isStale,
            string.Join("; ", reason),
            recommendedAction);
    }

    private sealed record AttemptUsageAggregate(int UsageCountLast90Days, DateTimeOffset? LastUsage)
    {
        public static readonly AttemptUsageAggregate Empty = new(0, null);
    }

    public async Task<List<ContentStalenessAssessment>> GetStaleContentAsync(int thresholdDays, CancellationToken ct)
    {
        var all = await ComputeAllAsync(ct);
        return all.Where(a => a.DaysSinceLastEdit > thresholdDays).ToList();
    }
}

/// <summary>
/// Hosted service that runs content staleness computation periodically.
/// </summary>
public sealed class ContentStalenessWorker(IServiceScopeFactory scopeFactory, ILogger<ContentStalenessWorker> logger) : BackgroundService
{
    // Run daily at 3 AM UTC
    private static readonly TimeSpan RunTime = new(3, 0, 0);
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var now = DateTimeOffset.UtcNow;
            var nextRun = now.Date.Add(RunTime);
            if (nextRun <= now) nextRun = nextRun.AddDays(1);
            var delay = nextRun - now;

            logger.LogInformation("ContentStalenessWorker next run at {NextRun} (in {Delay:hh\\:mm\\:ss})", nextRun, delay);
            await Task.Delay(delay, stoppingToken);

            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var service = scope.ServiceProvider.GetRequiredService<IContentStalenessService>();
                var assessments = await service.ComputeAllAsync(stoppingToken);
                var staleCount = assessments.Count(a => a.IsStale);
                logger.LogInformation("Content staleness scan complete. Total={Total}, Stale={Stale}", assessments.Count, staleCount);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Content staleness scan failed");
            }

            await Task.Delay(Interval, stoppingToken);
        }
    }
}
