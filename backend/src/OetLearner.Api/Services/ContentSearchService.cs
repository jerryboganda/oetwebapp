using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services;

/// <summary>
/// Phase 9 — Full-text-like search, filtered discovery, and rule-based recommendations.
/// </summary>
public class ContentSearchService(LearnerDbContext db)
{
    /// <summary>
    /// Search content items with multiple filter dimensions.
    /// </summary>
    public async Task<object> SearchContentAsync(ContentSearchQuery query, CancellationToken ct)
    {
        var q = db.ContentItems
            .Where(c => c.Status == ContentStatus.Published && c.FreshnessConfidence != "superseded");

        if (!string.IsNullOrEmpty(query.Text))
        {
            var lower = query.Text.ToLower();
            q = q.Where(c => c.Title.ToLower().Contains(lower)
                              || c.DetailJson.ToLower().Contains(lower));
        }
        if (!string.IsNullOrEmpty(query.SubtestCode))
            q = q.Where(c => c.SubtestCode == query.SubtestCode);
        if (!string.IsNullOrEmpty(query.ProfessionId))
            q = q.Where(c => c.ProfessionId == query.ProfessionId || c.ProfessionId == null);
        if (!string.IsNullOrEmpty(query.Difficulty))
            q = q.Where(c => c.Difficulty == query.Difficulty);
        if (!string.IsNullOrEmpty(query.Language))
            q = q.Where(c => c.InstructionLanguage == query.Language);
        if (!string.IsNullOrEmpty(query.Provenance))
            q = q.Where(c => c.SourceProvenance == query.Provenance);
        if (!string.IsNullOrEmpty(query.ContentType))
            q = q.Where(c => c.ContentType == query.ContentType);

        if (query.MinQuality > 0)
            q = q.Where(c => c.QualityScore >= query.MinQuality);
        if (query.MockEligibleOnly)
            q = q.Where(c => c.IsMockEligible);
        if (query.PreviewEligibleOnly)
            q = q.Where(c => c.IsPreviewEligible);

        var total = await q.CountAsync(ct);

        var items = await q
            .OrderByDescending(c => c.QualityScore).ThenBy(c => c.Title)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize)
            .Select(c => new
            {
                c.Id, c.Title, c.SubtestCode, c.ContentType, c.ProfessionId,
                c.Difficulty, c.DifficultyRating, c.EstimatedDurationMinutes,
                c.ScenarioType, c.InstructionLanguage, c.SourceProvenance,
                c.QualityScore, c.IsPreviewEligible, c.IsMockEligible,
                c.IsDiagnosticEligible, c.CreatedAt
            })
            .ToListAsync(ct);

        return new { items, total, page = query.Page, pageSize = query.PageSize };
    }

    /// <summary>
    /// Get filter facets (counts per dimension) for the search UI.
    /// </summary>
    public async Task<object> GetSearchFacetsAsync(CancellationToken ct)
    {
        var published = db.ContentItems
            .Where(c => c.Status == ContentStatus.Published && c.FreshnessConfidence != "superseded");

        var subtestFacets = await published
            .GroupBy(c => c.SubtestCode)
            .Select(g => new { value = g.Key, count = g.Count() })
            .ToListAsync(ct);

        var difficultyFacets = await published
            .GroupBy(c => c.Difficulty)
            .Select(g => new { value = g.Key, count = g.Count() })
            .ToListAsync(ct);

        var professionFacets = await published
            .GroupBy(c => c.ProfessionId ?? "general")
            .Select(g => new { value = g.Key, count = g.Count() })
            .ToListAsync(ct);

        var languageFacets = await published
            .GroupBy(c => c.InstructionLanguage)
            .Select(g => new { value = g.Key, count = g.Count() })
            .ToListAsync(ct);

        var provenanceFacets = await published
            .GroupBy(c => c.SourceProvenance)
            .Select(g => new { value = g.Key, count = g.Count() })
            .ToListAsync(ct);

        return new
        {
            subtests = subtestFacets,
            difficulties = difficultyFacets,
            professions = professionFacets,
            languages = languageFacets,
            provenances = provenanceFacets,
            totalPublished = await published.CountAsync(ct)
        };
    }

    /// <summary>
    /// Rule-based recommendations: weakest subtest, unused criteria, difficulty progression.
    /// </summary>
    public async Task<object> GetRecommendationsAsync(string userId, int count, CancellationToken ct)
    {
        // Get user's attempt history to find gaps
        var recentAttempts = await db.Attempts
            .Where(a => a.UserId == userId)
            .OrderByDescending(a => a.StartedAt)
            .Take(50)
            .Select(a => new { a.ContentId, a.SubtestCode })
            .ToListAsync(ct);

        var attemptedContentIds = recentAttempts.Select(a => a.ContentId).ToHashSet();
        var subtestCounts = recentAttempts.GroupBy(a => a.SubtestCode)
            .ToDictionary(g => g.Key, g => g.Count());

        // Find weakest subtest (least practiced)
        var allSubtests = new[] { "writing", "speaking", "reading", "listening" };
        var weakest = allSubtests
            .OrderBy(s => subtestCounts.GetValueOrDefault(s, 0))
            .First();

        // Get content items not yet attempted, prioritizing weakest subtest
        var recommendations = await db.ContentItems
            .Where(c => c.Status == ContentStatus.Published
                        && c.FreshnessConfidence != "superseded"
                        && !attemptedContentIds.Contains(c.Id))
            .OrderByDescending(c => c.SubtestCode == weakest ? 1 : 0)
            .ThenByDescending(c => c.QualityScore)
            .ThenBy(c => c.DifficultyRating)
            .Take(count)
            .Select(c => new
            {
                c.Id, c.Title, c.SubtestCode, c.Difficulty, c.ProfessionId,
                c.ScenarioType, c.EstimatedDurationMinutes, c.QualityScore,
                reason = c.SubtestCode == weakest ? "Weakest subtest — needs more practice"
                    : "Not yet attempted"
            })
            .ToListAsync(ct);

        // Quick-access sections
        var officialSamples = await db.ContentItems
            .Where(c => c.SourceProvenance == "official_sample" && c.Status == ContentStatus.Published)
            .Take(5).Select(c => new { c.Id, c.Title, c.SubtestCode }).ToListAsync(ct);

        var recentRecalls = await db.ContentItems
            .Where(c => c.SourceProvenance == "recall" && c.Status == ContentStatus.Published)
            .OrderByDescending(c => c.CreatedAt).Take(5)
            .Select(c => new { c.Id, c.Title, c.SubtestCode }).ToListAsync(ct);

        var freeWebinars = await db.FreePreviewAssets
            .Where(a => a.PreviewType == "webinar_replay" && a.Status == ContentStatus.Published)
            .Take(5).Select(a => new { a.Id, a.Title }).ToListAsync(ct);

        return new
        {
            recommended = recommendations,
            weakestSubtest = weakest,
            quickAccess = new { officialSamples, recentRecalls, freeWebinars }
        };
    }
}

public class ContentSearchQuery
{
    public string? Text { get; set; }
    public string? SubtestCode { get; set; }
    public string? ProfessionId { get; set; }
    public string? Difficulty { get; set; }
    public string? Language { get; set; }
    public string? Provenance { get; set; }
    public string? ContentType { get; set; }
    public int MinQuality { get; set; }
    public bool MockEligibleOnly { get; set; }
    public bool PreviewEligibleOnly { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}
