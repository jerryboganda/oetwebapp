using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests.Content;

/// <summary>
/// HTTP contract of the paging changes: <c>GET /v1/papers</c> keeps its bare-array body and signals
/// the continuation in <c>X-Has-More</c> / <c>X-Next-Cursor</c>; <c>GET /v1/search</c> keeps
/// <c>items / total / page / pageSize</c> and adds <c>hasMore</c> / <c>nextCursor</c>.
/// </summary>
public sealed class ContentPapersPagingEndpointTests(TestWebApplicationFactory factory)
    : IClassFixture<TestWebApplicationFactory>
{
    private HttpClient CreateLearnerClient()
    {
        var learnerId = $"learner-{Guid.NewGuid():N}";
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Debug-UserId", learnerId);
        client.DefaultRequestHeaders.Add("X-Debug-Role", "learner");
        client.DefaultRequestHeaders.Add("X-Debug-Email", $"{learnerId}@example.test");
        client.DefaultRequestHeaders.Add("X-Debug-Name", learnerId);
        client.DefaultRequestHeaders.Add("X-Debug-Profession", "medicine");
        return client;
    }

    // priority DESC, title ASC, id ASC => p1 p2 p3 | p4 p5 | p6 p7
    private static readonly (string Suffix, string Title, int Priority)[] PaperRows =
    [
        ("p1", "Alpha", 5), ("p2", "Alpha", 5), ("p3", "Bravo", 5),
        ("p4", "Charlie", 3), ("p5", "Delta", 3),
        ("p6", "Echo", 1), ("p7", "Foxtrot", 1),
    ];

    private async Task<string> SeedPapersAsync()
    {
        var subtest = $"paging{Guid.NewGuid():N}";
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        await db.Database.EnsureCreatedAsync();
        var now = DateTimeOffset.UtcNow;
        foreach (var (suffix, title, priority) in PaperRows)
        {
            db.ContentPapers.Add(new ContentPaper
            {
                Id = $"{subtest}-{suffix}",
                SubtestCode = subtest,
                Title = title,
                Slug = $"{subtest}-{suffix}",
                AppliesToAllProfessions = true,
                Difficulty = "standard",
                EstimatedDurationMinutes = 30,
                Status = ContentStatus.Published,
                SourceProvenance = "Test",
                TagsCsv = "access:free",
                Priority = priority,
                CreatedAt = now,
                UpdatedAt = now,
                PublishedAt = now,
            });
        }

        await db.SaveChangesAsync();
        return subtest;
    }

    private static string Url(string subtest, int pageSize, string? cursor = null, int? page = null)
        => $"/v1/papers?subtest={subtest}&pageSize={pageSize}"
           + (page is null ? string.Empty : $"&page={page}")
           + (cursor is null ? string.Empty : $"&cursor={Uri.EscapeDataString(cursor)}");

    private static List<string> Ids(string body)
    {
        using var document = JsonDocument.Parse(body);
        return document.RootElement.EnumerateArray().Select(row => row.GetProperty("id").GetString()!).ToList();
    }

    [Fact]
    public async Task Papers_list_pages_by_cursor_through_the_response_headers_and_keeps_the_array_body()
    {
        var subtest = await SeedPapersAsync();
        using var client = CreateLearnerClient();

        var seen = new List<string>();
        string? cursor = null;
        var requests = 0;
        while (requests < 5)
        {
            requests++;
            using var response = await client.GetAsync(Url(subtest, pageSize: 3, cursor));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            seen.AddRange(Ids(await response.Content.ReadAsStringAsync()));

            var hasMore = response.Headers.GetValues("X-Has-More").Single() == "true";
            if (!hasMore)
            {
                Assert.False(response.Headers.Contains("X-Next-Cursor"));
                break;
            }

            cursor = response.Headers.GetValues("X-Next-Cursor").Single();
        }

        Assert.Equal(3, requests); // 3 + 3 + 1
        Assert.Equal(PaperRows.Select(row => $"{subtest}-{row.Suffix}").ToList(), seen);
    }

    [Fact]
    public async Task Papers_list_exposes_the_paging_headers_to_a_cross_origin_browser()
    {
        var subtest = await SeedPapersAsync();
        using var client = CreateLearnerClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, Url(subtest, pageSize: 3));
        // One of the Development CORS origins (Program.cs corsOrigins).
        request.Headers.TryAddWithoutValidation("Origin", "http://localhost:3000");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var exposed = string.Join(",", response.Headers.GetValues("Access-Control-Expose-Headers"));
        Assert.Contains("X-Has-More", exposed, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("X-Next-Cursor", exposed, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Papers_list_without_a_cursor_still_serves_the_first_page_and_the_legacy_page_parameter()
    {
        var subtest = await SeedPapersAsync();
        using var client = CreateLearnerClient();

        using var first = await client.GetAsync(Url(subtest, pageSize: 3));
        using var second = await client.GetAsync(Url(subtest, pageSize: 3, page: 2));

        Assert.Equal(new[] { $"{subtest}-p1", $"{subtest}-p2", $"{subtest}-p3" }, Ids(await first.Content.ReadAsStringAsync()));
        Assert.Equal(new[] { $"{subtest}-p4", $"{subtest}-p5", $"{subtest}-p6" }, Ids(await second.Content.ReadAsStringAsync()));
        Assert.Equal("true", second.Headers.GetValues("X-Has-More").Single());
    }

    [Fact]
    public async Task Papers_list_rejects_an_invalid_cursor_with_a_400()
    {
        var subtest = await SeedPapersAsync();
        using var client = CreateLearnerClient();

        using var response = await client.GetAsync(Url(subtest, pageSize: 3, cursor: "not-a-cursor"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("papers_cursor_invalid", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Papers_list_with_an_absurd_page_number_returns_an_empty_page_not_a_500()
    {
        var subtest = await SeedPapersAsync();
        using var client = CreateLearnerClient();

        using var response = await client.GetAsync(Url(subtest, pageSize: 100, page: int.MaxValue));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(Ids(await response.Content.ReadAsStringAsync()));
        Assert.Equal("false", response.Headers.GetValues("X-Has-More").Single());
    }

    [Fact]
    public async Task Search_returns_the_keyset_fields_and_keeps_the_existing_ones()
    {
        var subtest = $"searchpaging{Guid.NewGuid():N}";
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            await db.Database.EnsureCreatedAsync();
            var now = DateTimeOffset.UtcNow;
            for (var i = 1; i <= 3; i++)
            {
                db.ContentItems.Add(new ContentItem
                {
                    Id = $"{subtest}-{i}",
                    ContentType = "writing_task",
                    SubtestCode = subtest,
                    ProfessionId = "medicine",
                    Title = $"Search paging {i}",
                    Difficulty = "medium",
                    EstimatedDurationMinutes = 45,
                    CriteriaFocusJson = "[]",
                    ModeSupportJson = "[]",
                    PublishedRevisionId = $"{subtest}-{i}-r1",
                    Status = ContentStatus.Published,
                    QualityScore = 3,
                    CreatedAt = now,
                    UpdatedAt = now,
                    PublishedAt = now,
                });
            }

            await db.SaveChangesAsync();
        }

        using var client = CreateLearnerClient();
        using var firstResponse = await client.GetAsync($"/v1/search?subtest={subtest}&pageSize=2&includeTotal=true");
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        using var first = JsonDocument.Parse(await firstResponse.Content.ReadAsStringAsync());
        Assert.Equal(2, first.RootElement.GetProperty("items").GetArrayLength());
        Assert.Equal(3, first.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(1, first.RootElement.GetProperty("page").GetInt32());
        Assert.Equal(2, first.RootElement.GetProperty("pageSize").GetInt32());
        Assert.True(first.RootElement.GetProperty("hasMore").GetBoolean());
        var cursor = first.RootElement.GetProperty("nextCursor").GetString();
        Assert.False(string.IsNullOrEmpty(cursor));

        using var secondResponse = await client.GetAsync(
            $"/v1/search?subtest={subtest}&pageSize=2&cursor={Uri.EscapeDataString(cursor!)}");
        using var second = JsonDocument.Parse(await secondResponse.Content.ReadAsStringAsync());
        Assert.Equal(1, second.RootElement.GetProperty("items").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, second.RootElement.GetProperty("total").ValueKind);
        Assert.False(second.RootElement.GetProperty("hasMore").GetBoolean());
        Assert.Equal(JsonValueKind.Null, second.RootElement.GetProperty("nextCursor").ValueKind);

        using var badResponse = await client.GetAsync($"/v1/search?subtest={subtest}&cursor=bogus");
        Assert.Equal(HttpStatusCode.BadRequest, badResponse.StatusCode);
        Assert.Contains("search_cursor_invalid", await badResponse.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }
}
