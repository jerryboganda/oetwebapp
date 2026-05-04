using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services;

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

        var results = new List<ContentStalenessAssessment>();
        foreach (var content in publishedContent)
        {
            var assessment = await ComputeForContentAsync(content.Id, ct);
            if (assessment is not null)
                results.Add(assessment);
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

        // Days since last edit
        var lastEdit = content.UpdatedAt > content.CreatedAt ? content.UpdatedAt : content.CreatedAt;
        var daysSinceEdit = (now - lastEdit).Days;

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

        // Rubric coverage
        var rubricCriteria = !string.IsNullOrWhiteSpace(content.CriteriaFocusJson) && content.CriteriaFocusJson != "[]"
            ? System.Text.Json.JsonSerializer.Deserialize<List<string>>(content.CriteriaFocusJson) ?? new List<string>()
            : new List<string>();
        var allCriteria = await db.Criteria
            .AsNoTracking()
            .Where(c => c.SubtestCode == content.SubtestCode)
            .Select(c => c.Code)
            .ToListAsync(ct);

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
