using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;

namespace OetLearner.Api.Tests.Content;

/// <summary>
/// Keyset paging (size + 1 rows, no OFFSET) for learner content search, the ranked cursor it uses,
/// and the 60 s facet cache. Runs on the EF in-memory provider, which evaluates the same seek
/// predicate (<c>QualityScore DESC, Title ASC, Id ASC</c>) the Npgsql query is translated from.
/// </summary>
public sealed class ContentSearchKeysetTests
{
    private static LearnerDbContext NewDb()
        => new(new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static ContentItem Item(string id, string title, int quality, string subtest = "writing") => new()
    {
        Id = id,
        ContentType = $"{subtest}_task",
        SubtestCode = subtest,
        ProfessionId = "medicine",
        Title = title,
        Difficulty = "medium",
        EstimatedDurationMinutes = 45,
        CriteriaFocusJson = "[]",
        ScenarioType = "referral",
        ModeSupportJson = "[]",
        PublishedRevisionId = $"{id}-r1",
        Status = ContentStatus.Published,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
        PublishedAt = DateTimeOffset.UtcNow,
        QualityScore = quality,
    };

    // Quality / title pairs chosen so that ties on quality AND on title both occur.
    private static List<ContentItem> Catalogue() =>
    [
        Item("i01", "Alpha", 5), Item("i02", "Alpha", 5), Item("i03", "Bravo", 5), Item("i04", "Alpha", 4),
        Item("i05", "Charlie", 4), Item("i06", "Charlie", 4), Item("i07", "Delta", 4), Item("i08", "Echo", 3),
        Item("i09", "Echo", 3), Item("i10", "Foxtrot", 3), Item("i11", "Golf", 2), Item("i12", "Golf", 2),
        Item("i13", "Hotel", 2), Item("i14", "India", 1), Item("i15", "India", 1), Item("i16", "Juliet", 0),
        Item("i17", "Kilo", 0), Item("i18", "Kilo", 0), Item("i19", "Lima", 0), Item("i20", "Mike", 0),
    ];

    private static List<string> ExpectedOrder(IEnumerable<ContentItem> items)
        => items.OrderByDescending(i => i.QualityScore)
            .ThenBy(i => i.Title, StringComparer.Ordinal)
            .ThenBy(i => i.Id, StringComparer.Ordinal)
            .Select(i => i.Id)
            .ToList();

    private sealed record PageResult(List<string> Ids, bool HasMore, string? NextCursor, int? Total, int Page, int PageSize);

    private static async Task<PageResult> RunAsync(ContentSearchService service, ContentSearchQuery query)
    {
        var json = JsonSerializer.SerializeToElement(await service.SearchContentAsync(query, CancellationToken.None));
        var ids = json.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("Id").GetString()!).ToList();
        var total = json.GetProperty("total");
        var next = json.GetProperty("nextCursor");
        return new PageResult(
            ids,
            json.GetProperty("hasMore").GetBoolean(),
            next.ValueKind == JsonValueKind.Null ? null : next.GetString(),
            total.ValueKind == JsonValueKind.Null ? null : total.GetInt32(),
            json.GetProperty("page").GetInt32(),
            json.GetProperty("pageSize").GetInt32());
    }

    [Fact]
    public async Task Cursor_paging_walks_every_item_exactly_once_in_quality_title_id_order()
    {
        await using var db = NewDb();
        var catalogue = Catalogue();
        db.ContentItems.AddRange(catalogue);
        await db.SaveChangesAsync();
        var service = new ContentSearchService(db);

        var seen = new List<string>();
        string? cursor = null;
        var pages = 0;
        do
        {
            var page = await RunAsync(service, new ContentSearchQuery { PageSize = 7, Cursor = cursor });
            seen.AddRange(page.Ids);
            cursor = page.NextCursor;
            pages++;
            Assert.Equal(page.HasMore, cursor is not null);
        }
        while (cursor is not null && pages < 10);

        Assert.Equal(3, pages); // 7 + 7 + 6
        Assert.Equal(ExpectedOrder(catalogue), seen);
        Assert.Equal(seen.Count, seen.Distinct().Count());
    }

    [Fact]
    public async Task First_page_reports_hasMore_and_a_cursor_and_the_last_page_does_not()
    {
        await using var db = NewDb();
        db.ContentItems.AddRange(Catalogue());
        await db.SaveChangesAsync();
        var service = new ContentSearchService(db);

        var first = await RunAsync(service, new ContentSearchQuery { PageSize = 5 });
        Assert.Equal(5, first.Ids.Count);
        Assert.True(first.HasMore);
        Assert.NotNull(first.NextCursor);

        var everything = await RunAsync(service, new ContentSearchQuery { PageSize = 20 });
        Assert.Equal(20, everything.Ids.Count);
        Assert.False(everything.HasMore);
        Assert.Null(everything.NextCursor);
    }

    [Fact]
    public async Task A_cursor_page_is_stable_when_better_ranked_rows_are_added_after_it_was_issued()
    {
        await using var db = NewDb();
        var catalogue = Catalogue();
        db.ContentItems.AddRange(catalogue);
        await db.SaveChangesAsync();
        var service = new ContentSearchService(db);
        var expected = ExpectedOrder(catalogue);

        var first = await RunAsync(service, new ContentSearchQuery { PageSize = 5 });
        Assert.Equal(expected.Take(5), first.Ids);

        // New top-ranked content lands between the two page requests. With OFFSET paging this
        // shifted page 2 and repeated the last row of page 1; the keyset cursor does not move.
        db.ContentItems.Add(Item("i99", "Aardvark", 5));
        await db.SaveChangesAsync();

        var second = await RunAsync(service, new ContentSearchQuery { PageSize = 5, Cursor = first.NextCursor });
        Assert.Equal(expected.Skip(5).Take(5), second.Ids);
    }

    [Fact]
    public async Task Total_is_null_by_default_and_counted_only_when_requested()
    {
        await using var db = NewDb();
        db.ContentItems.AddRange(Catalogue());
        await db.SaveChangesAsync();
        var service = new ContentSearchService(db);

        var without = await RunAsync(service, new ContentSearchQuery { PageSize = 5 });
        var with = await RunAsync(service, new ContentSearchQuery { PageSize = 5, IncludeTotal = true });

        Assert.Null(without.Total);
        Assert.Equal(20, with.Total);
    }

    [Fact]
    public async Task Total_ignores_the_cursor_and_counts_every_match()
    {
        await using var db = NewDb();
        db.ContentItems.AddRange(Catalogue());
        await db.SaveChangesAsync();
        var service = new ContentSearchService(db);
        var first = await RunAsync(service, new ContentSearchQuery { PageSize = 5 });

        var second = await RunAsync(service, new ContentSearchQuery { PageSize = 5, Cursor = first.NextCursor, IncludeTotal = true });

        Assert.Equal(20, second.Total);
    }

    [Fact]
    public async Task The_legacy_page_parameter_still_pages_when_no_cursor_is_sent()
    {
        await using var db = NewDb();
        var catalogue = Catalogue();
        db.ContentItems.AddRange(catalogue);
        await db.SaveChangesAsync();
        var service = new ContentSearchService(db);
        var expected = ExpectedOrder(catalogue);

        var second = await RunAsync(service, new ContentSearchQuery { PageSize = 6, Page = 2 });
        var fourth = await RunAsync(service, new ContentSearchQuery { PageSize = 6, Page = 4 });

        Assert.Equal(expected.Skip(6).Take(6), second.Ids);
        Assert.Equal(2, second.Page);
        Assert.True(second.HasMore);
        Assert.Equal(expected.Skip(18), fourth.Ids);
        Assert.False(fourth.HasMore);
    }

    [Fact]
    public async Task An_absurd_page_number_returns_an_empty_page_instead_of_overflowing()
    {
        await using var db = NewDb();
        db.ContentItems.AddRange(Catalogue());
        await db.SaveChangesAsync();
        var service = new ContentSearchService(db);

        var result = await RunAsync(service, new ContentSearchQuery { PageSize = 100, Page = int.MaxValue });

        Assert.Empty(result.Ids);
        Assert.False(result.HasMore);
    }

    [Fact]
    public async Task A_cursor_wins_over_the_page_parameter()
    {
        await using var db = NewDb();
        var catalogue = Catalogue();
        db.ContentItems.AddRange(catalogue);
        await db.SaveChangesAsync();
        var service = new ContentSearchService(db);
        var expected = ExpectedOrder(catalogue);
        var first = await RunAsync(service, new ContentSearchQuery { PageSize = 5 });

        var second = await RunAsync(service, new ContentSearchQuery { PageSize = 5, Page = 9, Cursor = first.NextCursor });

        Assert.Equal(expected.Skip(5).Take(5), second.Ids);
    }

    [Fact]
    public async Task Text_filter_keeps_substring_semantics_across_cursor_pages()
    {
        await using var db = NewDb();
        db.ContentItems.AddRange(
            Item("t1", "Cardiology referral letter", 5),
            Item("t2", "Cardiology discharge letter", 5),
            Item("t3", "Referral to cardiology", 4),
            Item("t4", "Renal discharge summary", 4),
            Item("t5", "CARDIOLOGY follow-up", 3));
        await db.SaveChangesAsync();
        var service = new ContentSearchService(db);

        var first = await RunAsync(service, new ContentSearchQuery { Text = "cardiology", PageSize = 2 });
        var second = await RunAsync(service, new ContentSearchQuery { Text = "cardiology", PageSize = 2, Cursor = first.NextCursor });

        Assert.Equal(new[] { "t2", "t1" }, first.Ids); // quality 5, "...discharge..." before "...referral..."
        Assert.True(first.HasMore);
        Assert.Equal(new[] { "t3", "t5" }, second.Ids);
        Assert.False(second.HasMore);
    }

    [Fact]
    public async Task Filters_are_applied_before_paging()
    {
        await using var db = NewDb();
        db.ContentItems.AddRange(
            Item("w1", "Writing one", 5, "writing"),
            Item("s1", "Speaking one", 5, "speaking"),
            Item("w2", "Writing two", 4, "writing"),
            Item("s2", "Speaking two", 4, "speaking"),
            Item("w3", "Writing three", 3, "writing"));
        await db.SaveChangesAsync();
        var service = new ContentSearchService(db);

        var first = await RunAsync(service, new ContentSearchQuery { SubtestCode = "writing", PageSize = 2 });
        var second = await RunAsync(service, new ContentSearchQuery { SubtestCode = "writing", PageSize = 2, Cursor = first.NextCursor });

        Assert.Equal(new[] { "w1", "w2" }, first.Ids);
        Assert.Equal(new[] { "w3" }, second.Ids);
        Assert.False(second.HasMore);
    }

    [Fact]
    public async Task An_invalid_cursor_is_a_validation_error_not_a_silent_first_page()
    {
        await using var db = NewDb();
        db.ContentItems.AddRange(Catalogue());
        await db.SaveChangesAsync();
        var service = new ContentSearchService(db);

        foreach (var bad in new[] { "not-a-cursor", "AAAA", CursorPagination.Encode(DateTimeOffset.UtcNow, "i01") })
        {
            var error = await Assert.ThrowsAsync<ApiException>(() =>
                service.SearchContentAsync(new ContentSearchQuery { Cursor = bad }, CancellationToken.None));
            Assert.Equal("search_cursor_invalid", error.ErrorCode);
            Assert.Equal(400, error.StatusCode);
        }
    }

    [Fact]
    public async Task Facets_are_served_from_the_cache_until_it_expires()
    {
        await using var db = NewDb();
        db.ContentItems.AddRange(Catalogue());
        await db.SaveChangesAsync();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = new ContentSearchService(db, cache);

        var first = JsonSerializer.SerializeToElement(await service.GetSearchFacetsAsync(CancellationToken.None));
        db.ContentItems.Add(Item("late", "Late arrival", 5));
        await db.SaveChangesAsync();
        var second = JsonSerializer.SerializeToElement(await service.GetSearchFacetsAsync(CancellationToken.None));

        Assert.Equal(20, first.GetProperty("totalPublished").GetInt32());
        Assert.Equal(20, second.GetProperty("totalPublished").GetInt32()); // cached

        cache.Remove(ContentSearchService.FacetsCacheKey);
        var third = JsonSerializer.SerializeToElement(await service.GetSearchFacetsAsync(CancellationToken.None));
        Assert.Equal(21, third.GetProperty("totalPublished").GetInt32());
    }

    [Fact]
    public async Task Facets_are_computed_every_time_without_a_cache()
    {
        await using var db = NewDb();
        db.ContentItems.AddRange(Catalogue());
        await db.SaveChangesAsync();
        var service = new ContentSearchService(db);

        var first = JsonSerializer.SerializeToElement(await service.GetSearchFacetsAsync(CancellationToken.None));
        db.ContentItems.Add(Item("late", "Late arrival", 5));
        await db.SaveChangesAsync();
        var second = JsonSerializer.SerializeToElement(await service.GetSearchFacetsAsync(CancellationToken.None));

        Assert.Equal(20, first.GetProperty("totalPublished").GetInt32());
        Assert.Equal(21, second.GetProperty("totalPublished").GetInt32());
    }

    [Fact]
    public async Task Concurrent_facet_requests_all_receive_the_same_cached_result()
    {
        var dbName = Guid.NewGuid().ToString("N");
        var options = new DbContextOptionsBuilder<LearnerDbContext>().UseInMemoryDatabase(dbName).Options;
        await using (var seed = new LearnerDbContext(options))
        {
            seed.ContentItems.AddRange(Catalogue());
            await seed.SaveChangesAsync();
        }

        using var cache = new MemoryCache(new MemoryCacheOptions());
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(async () =>
        {
            await using var db = new LearnerDbContext(options);
            return await new ContentSearchService(db, cache).GetSearchFacetsAsync(CancellationToken.None);
        })));

        Assert.All(results, result => Assert.Same(results[0], result));
    }

    [Fact]
    public void Ranked_cursor_round_trips_and_is_not_interchangeable_with_the_timestamp_cursor()
    {
        const string title = "Title with \"quotes\" and ünïcode / slashes";
        var encoded = CursorPagination.EncodeRanked(4, title, "id-7");

        Assert.True(CursorPagination.TryDecodeRanked(encoded, out var ranked));
        Assert.Equal(4, ranked.Rank);
        Assert.Equal(title, ranked.Title);
        Assert.Equal("id-7", ranked.Id);
        Assert.False(CursorPagination.TryDecode(encoded, out _));

        var timestamp = CursorPagination.Encode(DateTimeOffset.UtcNow, "att-1");
        Assert.False(CursorPagination.TryDecodeRanked(timestamp, out _));
        Assert.False(CursorPagination.TryDecodeRanked(null, out _));
        Assert.False(CursorPagination.TryDecodeRanked("   ", out _));
        Assert.False(CursorPagination.TryDecodeRanked("%%%", out _));
    }
}
