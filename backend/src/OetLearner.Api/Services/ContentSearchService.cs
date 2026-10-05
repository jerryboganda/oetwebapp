using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services;

/// <summary>
/// Phase 9 — Full-text-like search, filtered discovery, and rule-based recommendations.
/// </summary>
/// <remarks>
/// The cache is optional (null in unit tests that construct the service without it); when
/// present it holds the facet counts for <see cref="FacetsCacheTtl"/>.
/// </remarks>
public class ContentSearchService(LearnerDbContext db, IMemoryCache? cache = null)
{
    private const int MaxPageSize = 100;
    private const int MaxRecommendationCount = 100;

    /// <summary>The six facet aggregates scan every published item; the counts barely move.</summary>
    public static readonly TimeSpan FacetsCacheTtl = TimeSpan.FromSeconds(60);
    internal const string FacetsCacheKey = "content-search:facets:v1";

    // One recompute at a time when the facet entry expires, so a busy minute does not turn
    // into a thundering herd of 6-query scans. ponytail: process-wide, not per key (one key).
    private static readonly SemaphoreSlim FacetsGate = new(1, 1);

    /// <summary>
    /// Search content items with multiple filter dimensions.
    /// </summary>
    /// <remarks>
    /// Paging is keyset (size + 1 rows, no OFFSET): pass the previous response's
    /// <c>nextCursor</c> as <see cref="ContentSearchQuery.Cursor"/> to continue. The legacy
    /// <c>page</c> parameter still works when no cursor is sent (it is an OFFSET, so it is
    /// only cheap for the first pages). <c>total</c> is only counted when
    /// <see cref="ContentSearchQuery.IncludeTotal"/> is set (otherwise null): the COUNT
    /// re-ran the whole leading-wildcard ILIKE scan on every request.
    /// </remarks>
    public async Task<object> SearchContentAsync(ContentSearchQuery query, CancellationToken ct)
    {
        var page = Math.Max(query.Page, 1);
        var pageSize = Math.Clamp(query.PageSize, 1, MaxPageSize);

        var hasCursor = !string.IsNullOrWhiteSpace(query.Cursor);
        CursorPagination.RankedCursor cursor = default;
        if (hasCursor && !CursorPagination.TryDecodeRanked(query.Cursor, out cursor))
        {
            throw ApiException.Validation(
                "search_cursor_invalid",
                "The search cursor is not valid.",
                [new ApiFieldError("cursor", "invalid", "The search cursor is not valid.")]);
        }

        var q = db.ContentItems
            .AsNoTracking()
            .Where(c => c.Status == ContentStatus.Published && c.FreshnessConfidence != "superseded");

        if (!string.IsNullOrEmpty(query.Text))
        {
            var text = query.Text;
            var pattern = ToContainsPattern(text);
            if (db.Database.IsNpgsql())
            {
                q = q.Where(c => EF.Functions.ILike(c.Title, pattern, @"\")
                                 || EF.Functions.ILike(c.DetailJson, pattern, @"\"));
            }
            else if (db.Database.IsInMemory())
            {
                q = q.Where(c => c.Title.Contains(text, StringComparison.OrdinalIgnoreCase)
                                 || c.DetailJson.Contains(text, StringComparison.OrdinalIgnoreCase));
            }
            else
            {
                q = q.Where(c => EF.Functions.Like(c.Title, pattern, @"\")
                                 || EF.Functions.Like(c.DetailJson, pattern, @"\"));
            }
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

        // Opt-in: the COUNT re-ran the whole filtered scan on every page request.
        int? total = null;
        if (query.IncludeTotal)
        {
            total = await q.CountAsync(ct);
        }

        // Keyset seek for `QualityScore DESC, Title ASC, Id ASC` (Id makes the order total so a
        // page boundary can never repeat or skip a row that shares a title).
        var seek = q;
        if (hasCursor)
        {
            var cursorRank = cursor.Rank;
            var cursorTitle = cursor.Title;
            var cursorId = cursor.Id;
            seek = seek.Where(c => c.QualityScore < cursorRank
                || (c.QualityScore == cursorRank && c.Title.CompareTo(cursorTitle) > 0)
                || (c.QualityScore == cursorRank && c.Title == cursorTitle && c.Id.CompareTo(cursorId) > 0));
        }

        IQueryable<ContentItem> ordered = seek
            .OrderByDescending(c => c.QualityScore).ThenBy(c => c.Title).ThenBy(c => c.Id);
        if (!hasCursor && page > 1)
        {
            // Legacy page=N (no cursor): an OFFSET, clamped so (page - 1) * pageSize cannot overflow.
            ordered = ordered.Skip((int)Math.Min((long)(page - 1) * pageSize, int.MaxValue));
        }

        // size + 1 rows: the extra row only proves another page exists.
        var rows = await ordered
            .Take(pageSize + 1)
            .Select(c => new
            {
                c.Id, c.Title, c.SubtestCode, c.ContentType, c.ProfessionId,
                c.Difficulty, c.DifficultyRating, c.EstimatedDurationMinutes,
                c.ScenarioType, c.InstructionLanguage, c.SourceProvenance,
                c.QualityScore, c.IsPreviewEligible, c.IsMockEligible,
                c.IsDiagnosticEligible, c.CreatedAt
            })
            .ToListAsync(ct);

        var hasMore = rows.Count > pageSize;
        var items = hasMore ? rows.Take(pageSize).ToList() : rows;
        string? nextCursor = null;
        if (hasMore)
        {
            var last = items[items.Count - 1];
            nextCursor = CursorPagination.EncodeRanked(last.QualityScore, last.Title, last.Id);
        }

        return new { items, total, page, pageSize, hasMore, nextCursor };
    }

    /// <summary>
    /// Get filter facets (counts per dimension) for the search UI.
    /// </summary>
    public async Task<object> GetSearchFacetsAsync(CancellationToken ct)
    {
        if (cache is null)
        {
            return await ComputeSearchFacetsAsync(ct);
        }

        if (cache.TryGetValue(FacetsCacheKey, out object? cached) && cached is not null)
        {
            return cached;
        }

        await FacetsGate.WaitAsync(ct);
        try
        {
            // Another request may have refilled the entry while this one waited.
            if (cache.TryGetValue(FacetsCacheKey, out cached) && cached is not null)
            {
                return cached;
            }

            var facets = await ComputeSearchFacetsAsync(ct);
            cache.Set(FacetsCacheKey, facets, FacetsCacheTtl);
            return facets;
        }
        finally
        {
            FacetsGate.Release();
        }
    }

    private async Task<object> ComputeSearchFacetsAsync(CancellationToken ct)
    {
        var published = db.ContentItems
            .AsNoTracking()
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
        count = Math.Clamp(count, 1, MaxRecommendationCount);

        // Get user's attempt history to find gaps
        var recentAttempts = await db.Attempts
            .AsNoTracking()
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
            .AsNoTracking()
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
            .AsNoTracking()
            .Where(c => c.SourceProvenance == "official_sample" && c.Status == ContentStatus.Published)
            .Take(5).Select(c => new { c.Id, c.Title, c.SubtestCode }).ToListAsync(ct);

        var recentRecalls = await db.ContentItems
            .AsNoTracking()
            .Where(c => c.SourceProvenance == "recall" && c.Status == ContentStatus.Published)
            .OrderByDescending(c => c.CreatedAt).Take(5)
            .Select(c => new { c.Id, c.Title, c.SubtestCode }).ToListAsync(ct);

        var freeWebinars = await db.FreePreviewAssets
            .AsNoTracking()
            .Where(a => a.PreviewType == "webinar_replay" && a.Status == ContentStatus.Published)
            .Take(5).Select(a => new { a.Id, a.Title }).ToListAsync(ct);

        return new
        {
            recommended = recommendations,
            weakestSubtest = weakest,
            quickAccess = new { officialSamples, recentRecalls, freeWebinars }
        };
    }

    private static string ToContainsPattern(string value)
    {
        var escaped = value
            .Replace(@"\", @"\\", StringComparison.Ordinal)
            .Replace("%", @"\%", StringComparison.Ordinal)
            .Replace("_", @"\_", StringComparison.Ordinal);
        return $"%{escaped}%";
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

    /// <summary>Opaque keyset cursor from a previous response's <c>nextCursor</c>; wins over <see cref="Page"/>.</summary>
    public string? Cursor { get; set; }

    /// <summary>Also return <c>total</c> (an extra COUNT over the same filters). Off by default.</summary>
    public bool IncludeTotal { get; set; }
}
