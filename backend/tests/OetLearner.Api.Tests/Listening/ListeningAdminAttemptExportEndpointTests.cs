using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests.Listening;

public class ListeningAdminAttemptExportEndpointTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public ListeningAdminAttemptExportEndpointTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Admin_attempt_export_returns_relational_attempt_json()
    {
        var attemptId = $"attempt-{Guid.NewGuid():N}";
        await SeedRelationalAttemptAsync(attemptId);

        using var client = CreateAdminClient();
        var response = await client.GetAsync($"/v1/admin/listening/attempts/{attemptId}/export");
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;

        Assert.Equal("listening-v2", root.GetProperty("source").GetString());
        Assert.Equal(attemptId, root.GetProperty("attemptId").GetString());
        Assert.Equal("learner-export", root.GetProperty("userId").GetString());
        Assert.Equal("paper-export", root.GetProperty("paperId").GetString());
        Assert.Equal(12, root.GetProperty("rawScore").GetInt32());
        Assert.Equal(360, root.GetProperty("scaledScore").GetInt32());
        Assert.Contains("onePlay", root.GetProperty("policySnapshotJson").GetString(), StringComparison.Ordinal);
        Assert.Contains("expert-1", root.GetProperty("humanScoreOverridesJson").GetString(), StringComparison.Ordinal);

        var answer = Assert.Single(root.GetProperty("answers").EnumerateArray());
        Assert.Equal("question-export", answer.GetProperty("questionId").GetString());
        Assert.Equal("\"patient.email@example.test\"", answer.GetProperty("userAnswerJson").GetString());

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var audit = await db.AuditEvents.AsNoTracking()
            .SingleAsync(e => e.Action == "ListeningAttemptExported" && e.ResourceId == attemptId);
        Assert.Equal("admin-export", audit.ActorId);
        Assert.Equal("ListeningAttempt", audit.ResourceType);
        Assert.Contains("listening-v2", audit.Details, StringComparison.Ordinal);
        Assert.DoesNotContain("patient.email@example.test", audit.Details, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Admin_attempt_export_returns_legacy_attempt_json()
    {
        var attemptId = $"legacy-{Guid.NewGuid():N}";
        await SeedLegacyAttemptAsync(attemptId);

        using var client = CreateAdminClient();
        var response = await client.GetAsync($"/v1/admin/listening/attempts/{attemptId}/export");
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;

        Assert.Equal("legacy", root.GetProperty("source").GetString());
        Assert.Equal(attemptId, root.GetProperty("attemptId").GetString());
        Assert.Equal("legacy-learner", root.GetProperty("userId").GetString());
        Assert.Equal("legacy-paper", root.GetProperty("paperId").GetString());
        Assert.Equal(310, root.GetProperty("scaledScore").GetInt32());
        Assert.Contains("legacy wrong", root.GetProperty("answersJson").GetString(), StringComparison.Ordinal);

        var evaluation = Assert.Single(root.GetProperty("evaluations").EnumerateArray());
        Assert.Contains("scaledScore", evaluation.GetProperty("criterionScoresJson").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Admin_attempt_export_filters_evaluations_to_listening()
    {
        var attemptId = $"attempt-{Guid.NewGuid():N}";
        await SeedRelationalAttemptAsync(attemptId);
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            var now = DateTimeOffset.UtcNow;
            db.Evaluations.AddRange(
                new Evaluation
                {
                    Id = $"eval-listening-{Guid.NewGuid():N}",
                    AttemptId = attemptId,
                    SubtestCode = "listening",
                    State = AsyncState.Completed,
                    ScoreRange = "360",
                    ConfidenceBand = ConfidenceBand.High,
                    CriterionScoresJson = "[{\"scaledScore\":360}]",
                    GeneratedAt = now,
                    ModelExplanationSafe = "listening",
                    LearnerDisclaimer = "practice",
                    LastTransitionAt = now,
                },
                new Evaluation
                {
                    Id = $"eval-reading-{Guid.NewGuid():N}",
                    AttemptId = attemptId,
                    SubtestCode = "reading",
                    State = AsyncState.Completed,
                    ScoreRange = "500",
                    ConfidenceBand = ConfidenceBand.High,
                    CriterionScoresJson = "[{\"scaledScore\":500}]",
                    GeneratedAt = now.AddSeconds(1),
                    ModelExplanationSafe = "reading",
                    LearnerDisclaimer = "practice",
                    LastTransitionAt = now.AddSeconds(1),
                });
            await db.SaveChangesAsync();
        }

        using var client = CreateAdminClient();
        var response = await client.GetAsync($"/v1/admin/listening/attempts/{attemptId}/export");
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var evaluation = Assert.Single(document.RootElement.GetProperty("evaluations").EnumerateArray());

        Assert.Equal("listening", evaluation.GetProperty("subtestCode").GetString());
        Assert.Contains("360", evaluation.GetProperty("scoreRange").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Learner_cannot_export_admin_attempt_json()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Debug-UserId", "learner-export-denied");
        client.DefaultRequestHeaders.Add("X-Debug-Role", "learner");

        var response = await client.GetAsync("/v1/admin/listening/attempts/anything/export");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Admin_without_content_read_permission_cannot_export_attempt_json()
    {
        using var client = CreateAdminClient(AdminPermissions.ContentWrite);

        var response = await client.GetAsync("/v1/admin/listening/attempts/anything/export");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Missing_attempt_export_returns_not_found()
    {
        using var client = CreateAdminClient();

        var response = await client.GetAsync($"/v1/admin/listening/attempts/missing-{Guid.NewGuid():N}/export");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private HttpClient CreateAdminClient(string adminPermissions = AdminPermissions.ContentRead)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Debug-UserId", "admin-export");
        client.DefaultRequestHeaders.Add("X-Debug-Role", "admin");
        client.DefaultRequestHeaders.Add("X-Debug-AdminPermissions", adminPermissions);
        return client;
    }

    private async Task SeedRelationalAttemptAsync(string attemptId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        await db.Database.EnsureCreatedAsync();
        var now = DateTimeOffset.UtcNow;

        db.ListeningAttempts.Add(new ListeningAttempt
        {
            Id = attemptId,
            UserId = "learner-export",
            PaperId = "paper-export",
            StartedAt = now.AddMinutes(-45),
            LastActivityAt = now,
            SubmittedAt = now,
            Status = ListeningAttemptStatus.Submitted,
            Mode = ListeningAttemptMode.Exam,
            RawScore = 12,
            ScaledScore = 360,
            MaxRawScore = 42,
            PolicySnapshotJson = "{\"onePlay\":true}",
            HumanScoreOverridesJson = "[{\"questionId\":\"question-export\",\"by\":\"expert-1\"}]",
            LastQuestionVersionMapJson = "{\"question-export\":2}",
        });
        db.ListeningAnswers.Add(new ListeningAnswer
        {
            Id = $"answer-{Guid.NewGuid():N}",
            ListeningAttemptId = attemptId,
            ListeningQuestionId = "question-export",
            UserAnswerJson = "\"patient.email@example.test\"",
            IsCorrect = false,
            PointsEarned = 0,
            QuestionVersionSnapshot = 2,
            AnsweredAt = now,
        });

        await db.SaveChangesAsync();
    }

    private async Task SeedLegacyAttemptAsync(string attemptId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        await db.Database.EnsureCreatedAsync();
        var now = DateTimeOffset.UtcNow;

        db.Attempts.Add(new Attempt
        {
            Id = attemptId,
            UserId = "legacy-learner",
            ContentId = "legacy-paper",
            SubtestCode = "listening",
            Context = "exam",
            Mode = "exam",
            State = AttemptState.Submitted,
            StartedAt = now.AddMinutes(-45),
            SubmittedAt = now,
            CompletedAt = now,
            ElapsedSeconds = 2700,
            AnswersJson = "{\"q1\":\"legacy wrong\"}",
            DraftContent = "legacy draft",
            TranscriptJson = "[]",
            AnalysisJson = "{\"legacy\":true}",
        });
        db.Evaluations.Add(new Evaluation
        {
            Id = $"eval-{Guid.NewGuid():N}",
            AttemptId = attemptId,
            SubtestCode = "listening",
            State = AsyncState.Completed,
            ScoreRange = "310",
            ConfidenceBand = ConfidenceBand.High,
            CriterionScoresJson = "[{\"scaledScore\":310}]",
            GeneratedAt = now,
            ModelExplanationSafe = "test",
            LearnerDisclaimer = "test",
            LastTransitionAt = now,
        });

        await db.SaveChangesAsync();
    }
}
