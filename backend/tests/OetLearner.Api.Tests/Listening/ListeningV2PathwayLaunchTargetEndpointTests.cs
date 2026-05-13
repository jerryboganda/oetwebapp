using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Listening;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests.Listening;

public class ListeningV2PathwayLaunchTargetEndpointTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public ListeningV2PathwayLaunchTargetEndpointTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public void BuildActionHref_returns_exact_stage_scoped_player_routes_for_actionable_stages()
    {
        var expectedModes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["diagnostic"] = "diagnostic",
            ["foundation_partA"] = "practice",
            ["foundation_partB"] = "practice",
            ["foundation_partC"] = "practice",
            ["drill_partA"] = "practice",
            ["drill_partB"] = "practice",
            ["drill_partC"] = "practice",
            ["minitest_partA"] = "practice",
            ["minitest_partBC"] = "practice",
            ["fullpaper_paper"] = "paper",
            ["fullpaper_cbt"] = "exam",
            ["exam_simulation"] = "home",
        };

        foreach (var stage in ListeningPathwayProgressService.PathwayStages)
        {
            var href = ListeningPathwayLaunchTargets.BuildActionHref(
                stage,
                ListeningPathwayStageStatus.Unlocked,
                "paper 1");

            Assert.Equal($"/listening/player/paper%201?mode={expectedModes[stage]}&pathwayStage={stage}", href);
        }
    }

    [Fact]
    public void BuildActionHref_does_not_launch_locked_completed_or_unanchored_stages()
    {
        Assert.Null(ListeningPathwayLaunchTargets.BuildActionHref(
            "foundation_partA",
            ListeningPathwayStageStatus.Locked,
            "paper-1"));
        Assert.Null(ListeningPathwayLaunchTargets.BuildActionHref(
            "foundation_partA",
            ListeningPathwayStageStatus.Completed,
            "paper-1"));
        Assert.Null(ListeningPathwayLaunchTargets.BuildActionHref(
            "foundation_partA",
            ListeningPathwayStageStatus.Unlocked,
            null));
    }

    [Fact]
    public async Task Pathway_endpoint_returns_backend_authored_stage_launch_targets()
    {
        await ClearPathwayLaunchPapersAsync();

        var userId = $"listener-{Guid.NewGuid():N}";
        var paperId = $"paper-{Guid.NewGuid():N}";
        await _factory.EnsureLearnerProfileAsync(userId, $"{userId}@example.test", userId);
        await SeedObjectiveReadyPaperAsync(userId, paperId, includeQuestion: true);
        await SeedSubmittedDiagnosticAttemptAsync(userId, paperId);

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Debug-UserId", userId);
        client.DefaultRequestHeaders.Add("X-Debug-Role", "learner");

        var response = await client.GetAsync("/v1/listening/v2/me/pathway");
        response.EnsureSuccessStatusCode();
        var rows = await response.Content.ReadFromJsonAsync<JsonElement[]>(JsonSupport.Options);

        Assert.NotNull(rows);
        var diagnostic = rows.Single(row => row.GetProperty("stage").GetString() == "diagnostic");
        Assert.Equal("Completed", diagnostic.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, diagnostic.GetProperty("actionHref").ValueKind);

        var foundation = rows.Single(row => row.GetProperty("stage").GetString() == "foundation_partA");
        Assert.Equal("Unlocked", foundation.GetProperty("status").GetString());
        Assert.Equal(
            $"/listening/player/{paperId}?mode=practice&pathwayStage=foundation_partA",
            foundation.GetProperty("actionHref").GetString());
    }

    [Fact]
    public async Task Pathway_endpoint_omits_player_launches_when_no_objective_ready_paper_exists()
    {
        await ClearPathwayLaunchPapersAsync();

        var userId = $"listener-{Guid.NewGuid():N}";
        var paperId = $"paper-{Guid.NewGuid():N}";
        await _factory.EnsureLearnerProfileAsync(userId, $"{userId}@example.test", userId);
        await SeedObjectiveReadyPaperAsync(userId, paperId, includeQuestion: false);

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Debug-UserId", userId);
        client.DefaultRequestHeaders.Add("X-Debug-Role", "learner");

        var response = await client.GetAsync("/v1/listening/v2/me/pathway");
        response.EnsureSuccessStatusCode();
        var rows = await response.Content.ReadFromJsonAsync<JsonElement[]>(JsonSupport.Options);

        Assert.NotNull(rows);
        var diagnostic = rows.Single(row => row.GetProperty("stage").GetString() == "diagnostic");
        Assert.Equal("Unlocked", diagnostic.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, diagnostic.GetProperty("actionHref").ValueKind);
    }

    [Fact]
    public async Task Pathway_endpoint_omits_player_launches_when_objective_ready_paper_is_not_allowed()
    {
        await ClearPathwayLaunchPapersAsync();

        var userId = $"listener-{Guid.NewGuid():N}";
        var paperId = $"paper-{Guid.NewGuid():N}";
        await _factory.EnsureLearnerProfileAsync(userId, $"{userId}@example.test", userId);
        await RemoveUserSubscriptionsAsync(userId);
        await SeedObjectiveReadyPaperAsync(userId, paperId, includeQuestion: true, isFree: false);

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Debug-UserId", userId);
        client.DefaultRequestHeaders.Add("X-Debug-Role", "learner");

        var response = await client.GetAsync("/v1/listening/v2/me/pathway");
        response.EnsureSuccessStatusCode();
        var rows = await response.Content.ReadFromJsonAsync<JsonElement[]>(JsonSupport.Options);

        Assert.NotNull(rows);
        var diagnostic = rows.Single(row => row.GetProperty("stage").GetString() == "diagnostic");
        Assert.Equal("Unlocked", diagnostic.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, diagnostic.GetProperty("actionHref").ValueKind);
    }

    private async Task ClearPathwayLaunchPapersAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var paperIds = db.ContentPapers
            .Where(paper => paper.Slug.StartsWith("pathway-launch-"))
            .Select(paper => paper.Id)
            .ToArray();

        db.ListeningAttempts.RemoveRange(db.ListeningAttempts.Where(attempt => paperIds.Contains(attempt.PaperId)));
        db.ListeningQuestions.RemoveRange(db.ListeningQuestions.Where(question => paperIds.Contains(question.PaperId)));
        db.ContentPapers.RemoveRange(db.ContentPapers.Where(paper => paperIds.Contains(paper.Id)));
        await db.SaveChangesAsync();
    }

    private async Task RemoveUserSubscriptionsAsync(string userId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        db.Subscriptions.RemoveRange(db.Subscriptions.Where(subscription => subscription.UserId == userId));
        await db.SaveChangesAsync();
    }

    private async Task SeedObjectiveReadyPaperAsync(
        string userId,
        string paperId,
        bool includeQuestion,
        bool isFree = true)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var now = DateTimeOffset.UtcNow;

        db.ContentPapers.Add(new ContentPaper
        {
            Id = paperId,
            SubtestCode = "listening",
            Title = "Pathway launch paper",
            Slug = $"pathway-launch-{paperId}",
            AppliesToAllProfessions = true,
            Status = ContentStatus.Published,
            TagsCsv = isFree ? "access:free" : "access:premium",
            SourceProvenance = "Test seed",
            CreatedAt = now,
            UpdatedAt = now,
            PublishedAt = now,
        });

        if (includeQuestion)
        {
            db.ListeningQuestions.Add(new ListeningQuestion
            {
                Id = $"q-{Guid.NewGuid():N}",
                PaperId = paperId,
                ListeningPartId = "part-a",
                QuestionNumber = 1,
                DisplayOrder = 1,
                QuestionType = ListeningQuestionType.ShortAnswer,
                Stem = "Patient name?",
                CorrectAnswerJson = "\"Smith\"",
                CreatedAt = now,
                UpdatedAt = now,
            });
        }

        await db.SaveChangesAsync();
    }

    private async Task SeedSubmittedDiagnosticAttemptAsync(string userId, string paperId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var now = DateTimeOffset.UtcNow;

        db.ListeningAttempts.Add(new ListeningAttempt
        {
            Id = $"lat-{Guid.NewGuid():N}",
            UserId = userId,
            PaperId = paperId,
            StartedAt = now.AddMinutes(-20),
            LastActivityAt = now.AddMinutes(-1),
            SubmittedAt = now,
            Status = ListeningAttemptStatus.Submitted,
            Mode = ListeningAttemptMode.Diagnostic,
            MaxRawScore = 42,
            RawScore = 0,
            ScaledScore = 0,
            ScopeJson = "{\"mode\":\"diagnostic\",\"sourceKind\":\"content_paper\",\"pathwayStage\":\"diagnostic\"}",
        });

        await db.SaveChangesAsync();
    }
}
