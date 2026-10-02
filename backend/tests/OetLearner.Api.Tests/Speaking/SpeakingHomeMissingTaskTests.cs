using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests.Speaking;

// 2 Oct 2026: in the CI stack GET /v1/speaking/home was a 500 ("Sequence contains no elements" from
// GetSpeakingEvaluationSummaryAsync) because the seeded evaluation pointed at a task row the stack did not have. An evaluation
// can outlive its task, so that must be a 404 on the summary and a hub that simply leaves the latest evaluation out.
public class SpeakingHomeMissingTaskTests : IClassFixture<TestWebApplicationFactory>
{
    private const string UserId = "mock-user-001";
    private const string EvaluationId = "se-task-gone";

    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public SpeakingHomeMissingTaskTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.EnsureLearnerProfileAsync(UserId, "mock-user-001@example.test", UserId).GetAwaiter().GetResult();
        _client = factory.CreateClient();
        SeedLatestEvaluationWithoutItsTaskAsync().GetAwaiter().GetResult();
    }

    [Fact]
    public async Task SpeakingHome_StillLoads_WhenTheLatestEvaluationsTaskIsGone()
    {
        var response = await _client.GetAsync("/v1/speaking/home");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("latestEvaluation").ValueKind);
        Assert.True(json.RootElement.TryGetProperty("featuredTasks", out _));
        Assert.True(json.RootElement.TryGetProperty("pastAttempts", out _));
    }

    [Fact]
    public async Task SpeakingEvaluationSummary_IsA404WithACode_WhenTheTaskIsGone()
    {
        var response = await _client.GetAsync($"/v1/speaking/evaluations/{EvaluationId}/summary");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("speaking_task_not_found", json.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task SpeakingEvaluationSummary_StillWorks_WhenTheTaskExists()
    {
        var response = await _client.GetAsync("/v1/speaking/evaluations/se-task-present/summary");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("ci-task-present", json.RootElement.GetProperty("taskId").GetString());
    }

    // The newest evaluation of the learner, on an attempt whose ContentItem does not exist.
    private async Task SeedLatestEvaluationWithoutItsTaskAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        if (db.Evaluations.Any(e => e.Id == EvaluationId)) return;
        var now = DateTimeOffset.UtcNow;
        db.ContentItems.Add(new ContentItem
        {
            Id = "ci-task-present",
            ContentType = "speaking_roleplay",
            SubtestCode = "speaking",
            Title = "Task that still exists",
            Difficulty = "core",
            PublishedRevisionId = "ci-task-present-r1",
            Status = ContentStatus.Published,
            CreatedAt = now,
            UpdatedAt = now,
        });
        db.Attempts.Add(NewAttempt("sa-task-present", "ci-task-present", now));
        db.Evaluations.Add(NewEvaluation("se-task-present", "sa-task-present", now.AddDays(-1)));
        db.Attempts.Add(NewAttempt("sa-task-gone", "ci-task-gone", now));
        db.Evaluations.Add(NewEvaluation(EvaluationId, "sa-task-gone", now.AddDays(1)));
        await db.SaveChangesAsync();
    }

    private static Attempt NewAttempt(string id, string contentId, DateTimeOffset now) => new()
    {
        Id = id,
        UserId = UserId,
        ContentId = contentId,
        SubtestCode = "speaking",
        Context = "practice",
        Mode = "ai",
        State = AttemptState.Completed,
        StartedAt = now.AddMinutes(-30),
        SubmittedAt = now.AddMinutes(-10),
        CompletedAt = now.AddMinutes(-5),
        ElapsedSeconds = 600,
        DeviceType = "desktop",
    };

    private static Evaluation NewEvaluation(string id, string attemptId, DateTimeOffset generatedAt) => new()
    {
        Id = id,
        AttemptId = attemptId,
        SubtestCode = "speaking",
        State = AsyncState.Completed,
        ScoreRange = "330-360",
        ConfidenceBand = ConfidenceBand.Medium,
        StrengthsJson = "[]",
        IssuesJson = "[]",
        CriterionScoresJson = "[]",
        FeedbackItemsJson = "[]",
        GeneratedAt = generatedAt,
        ModelExplanationSafe = "Test evaluation.",
        LearnerDisclaimer = "Test evaluation.",
        StatusReasonCode = "completed",
        StatusMessage = "Completed.",
        LastTransitionAt = generatedAt,
    };
}
