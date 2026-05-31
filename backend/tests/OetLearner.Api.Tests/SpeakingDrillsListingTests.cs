using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests;

// Wave 6 of docs/SPEAKING-MODULE-PLAN.md - speaking drills bank.
// Verifies the seeded drill ContentItem rows surface through
// /v1/speaking/drills, that filters work, and that the kinds list is
// stable.
[Collection("AuthFlows")]
public class SpeakingDrillsListingTests : IClassFixture<FirstPartyAuthTestWebApplicationFactory>
{
    private readonly FirstPartyAuthTestWebApplicationFactory _factory;

    public SpeakingDrillsListingTests(FirstPartyAuthTestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Drills_List_ReturnsSeededRows_AndExposesKinds()
    {
        using var client = _factory.CreateAuthenticatedClient(SeedData.LearnerEmail, SeedData.LocalSeedPassword, expectedRole: "learner");

        var resp = await client.GetAsync("/v1/speaking/drills");
        resp.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var kinds = doc.RootElement.GetProperty("kinds").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Contains("phrasing", kinds);
        Assert.Contains("intonation", kinds);
        Assert.Contains("pronunciation", kinds);
        Assert.Contains("vocabulary", kinds);
        Assert.Contains("chunking", kinds);
        Assert.Contains("empathy", kinds);

        var items = doc.RootElement.GetProperty("items").EnumerateArray().ToList();
        Assert.True(items.Count >= 6, $"expected at least 6 seeded drills, got {items.Count}");
        Assert.Contains(items, i => i.GetProperty("id").GetString() == "sd-phrasing-001");
    }

    [Fact]
    public async Task Drills_List_FiltersByKind()
    {
        using var client = _factory.CreateAuthenticatedClient(SeedData.LearnerEmail, SeedData.LocalSeedPassword, expectedRole: "learner");

        var resp = await client.GetAsync("/v1/speaking/drills?kind=intonation");
        resp.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var items = doc.RootElement.GetProperty("items").EnumerateArray().ToList();
        Assert.NotEmpty(items);
        Assert.All(items, i => Assert.Equal("intonation", i.GetProperty("kind").GetString()));
    }

    [Fact]
    public async Task Drills_List_FiltersByCriterionFocus()
    {
        using var client = _factory.CreateAuthenticatedClient(SeedData.LearnerEmail, SeedData.LocalSeedPassword, expectedRole: "learner");

        // 'fluency' is in the chunking drill's CriteriaFocusJson.
        var resp = await client.GetAsync("/v1/speaking/drills?criterion=fluency");
        resp.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var items = doc.RootElement.GetProperty("items").EnumerateArray().ToList();
        Assert.NotEmpty(items);
        Assert.All(items, i =>
        {
            var focus = i.GetProperty("criteriaFocus").EnumerateArray().Select(e => e.GetString()).ToList();
            Assert.Contains("fluency", focus, StringComparer.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public async Task Drills_List_RejectsUnknownKind_Silently()
    {
        using var client = _factory.CreateAuthenticatedClient(SeedData.LearnerEmail, SeedData.LocalSeedPassword, expectedRole: "learner");

        // Bogus kind should be ignored and the full list returned.
        var resp = await client.GetAsync("/v1/speaking/drills?kind=does-not-exist");
        resp.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var items = doc.RootElement.GetProperty("items").EnumerateArray().ToList();
        Assert.True(items.Count >= 6);
    }

    [Fact]
    public async Task Drills_List_MarksCanonicalSpeakingDrillAttemptsCompleted()
    {
        string contentItemId;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            var learner = await db.Users.SingleAsync(u => u.Email == SeedData.LearnerEmail);
            var drill = await db.SpeakingDrillItems
                .OrderBy(d => d.Id)
                .FirstAsync(d => d.ContentItem != null && d.ContentItem.Status == ContentStatus.Published);
            contentItemId = drill.ContentItemId;

            db.SpeakingDrillAttempts.Add(new SpeakingDrillAttempt
            {
                Id = $"sda-listing-{Guid.NewGuid():N}",
                UserId = learner.Id,
                DrillItemId = drill.Id,
                StartedAt = DateTimeOffset.UtcNow.AddMinutes(-2),
                CompletedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
                Score = 4,
            });
            await db.SaveChangesAsync();
        }

        using var client = _factory.CreateAuthenticatedClient(SeedData.LearnerEmail, SeedData.LocalSeedPassword, expectedRole: "learner");
        var resp = await client.GetAsync("/v1/speaking/drills");
        resp.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var completedItem = doc.RootElement.GetProperty("items")
            .EnumerateArray()
            .Single(i => i.GetProperty("id").GetString() == contentItemId);
        Assert.True(completedItem.GetProperty("completed").GetBoolean());
    }

    [Fact]
    public async Task Drills_List_RequiresAuth()
    {
        using var anon = _factory.CreateClient();
        var resp = await anon.GetAsync("/v1/speaking/drills");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }
}
