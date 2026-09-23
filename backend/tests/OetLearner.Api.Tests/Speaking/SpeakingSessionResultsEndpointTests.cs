using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests.Speaking;

/// <summary>
/// GET /v1/speaking/sessions/{id}/results — the results page polls it: "no
/// assessment yet" is assessmentState=processing (never a 404), a graded
/// session is completed, and only a session the caller does not own is 404.
/// </summary>
public sealed class SpeakingSessionResultsEndpointTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public SpeakingSessionResultsEndpointTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Results_ReportProcessingThenCompleted_AndAreOwnerOnly()
    {
        var ownerId = $"results-owner-{Guid.NewGuid():N}";
        var (sessionId, cardId) = await SeedFinishedSessionAsync(ownerId);
        using var owner = await CreateLearnerClientAsync(ownerId);

        var pending = await owner.GetAsync($"/v1/speaking/sessions/{sessionId}/results");
        Assert.Equal(HttpStatusCode.OK, pending.StatusCode);
        using (var json = JsonDocument.Parse(await pending.Content.ReadAsStringAsync()))
        {
            var root = json.RootElement;
            Assert.Equal(sessionId, root.GetProperty("sessionId").GetString());
            Assert.Equal("processing", root.GetProperty("assessmentState").GetString());
            Assert.False(root.GetProperty("retryable").GetBoolean());
            Assert.Equal(JsonValueKind.Null, root.GetProperty("failureReason").ValueKind);
            Assert.False(root.GetProperty("isFreeSample").GetBoolean());
            Assert.Equal(cardId, root.GetProperty("cardId").GetString());
        }

        await SeedAssessmentAsync(sessionId);
        var completed = await owner.GetAsync($"/v1/speaking/sessions/{sessionId}/results");
        completed.EnsureSuccessStatusCode();
        using (var json = JsonDocument.Parse(await completed.Content.ReadAsStringAsync()))
        {
            Assert.Equal("completed", json.RootElement.GetProperty("assessmentState").GetString());
        }

        using var other = await CreateLearnerClientAsync($"results-other-{Guid.NewGuid():N}");
        Assert.Equal(HttpStatusCode.NotFound,
            (await other.GetAsync($"/v1/speaking/sessions/{sessionId}/results")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await owner.GetAsync($"/v1/speaking/sessions/sps_missing_{Guid.NewGuid():N}/results")).StatusCode);
    }

    private async Task<(string SessionId, string CardId)> SeedFinishedSessionAsync(string ownerId)
    {
        await _factory.EnsureLearnerProfileAsync(ownerId, $"{ownerId}@example.test", ownerId, "medicine");
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var now = DateTimeOffset.UtcNow;
        var cardId = $"rpc-results-{Guid.NewGuid():N}";
        var sessionId = $"sps_{Guid.NewGuid():N}";
        db.RolePlayCards.Add(new RolePlayCard
        {
            Id = cardId,
            ContentItemId = $"ci-{cardId}",
            ProfessionId = "medicine",
            ScenarioTitle = "Results polling card",
            Setting = "General practice",
            CandidateRole = "Doctor",
            Status = ContentStatus.Published,
            CreatedAt = now,
            UpdatedAt = now,
        });
        db.SpeakingSessions.Add(new SpeakingSession
        {
            Id = sessionId,
            UserId = ownerId,
            RolePlayCardId = cardId,
            Mode = SpeakingSessionMode.AiSelfPractice,
            State = SpeakingSessionState.Finished,
            PrepStartedAt = now.AddMinutes(-10),
            RolePlayStartedAt = now.AddMinutes(-7),
            EndedAt = now.AddMinutes(-1),
            ConsentVersion = "recording.v1",
            CreatedAt = now.AddMinutes(-12),
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();
        return (sessionId, cardId);
    }

    private async Task SeedAssessmentAsync(string sessionId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        db.SpeakingAiAssessments.Add(new SpeakingAiAssessment
        {
            Id = $"sai-{Guid.NewGuid():N}",
            SpeakingSessionId = sessionId,
            TranscriptId = $"tr-{Guid.NewGuid():N}",
            Provider = "test",
            ModelId = "test-model",
            Intelligibility = 4,
            Fluency = 4,
            Appropriateness = 4,
            GrammarExpression = 4,
            RelationshipBuilding = 2,
            PatientPerspective = 2,
            Structure = 2,
            InformationGathering = 2,
            InformationGiving = 2,
            EstimatedScaledScore = 360,
            ReadinessBand = "exam_ready",
            OverallSummary = "Seeded AI assessment.",
            ConfidenceBand = "high",
            GeneratedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    private async Task<HttpClient> CreateLearnerClientAsync(string userId)
    {
        await _factory.EnsureLearnerProfileAsync(userId, $"{userId}@example.test", userId, "medicine");
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Debug-UserId", userId);
        client.DefaultRequestHeaders.Add("X-Debug-Email", $"{userId}@example.test");
        client.DefaultRequestHeaders.Add("X-Debug-Name", userId);
        client.DefaultRequestHeaders.Add("X-Debug-Role", ApplicationUserRoles.Learner);
        return client;
    }
}
