using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OetWithDrHesham.Api.Data;
using OetWithDrHesham.Api.Domain;
using OetWithDrHesham.Api.Tests.Infrastructure;

namespace OetWithDrHesham.Api.Tests;

public class SpeakingDualAssessmentSecurityTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public SpeakingDualAssessmentSecurityTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task LearnerDualAssessment_RequiresSessionOwnership_AndHidesTutorDrafts()
    {
        var ownerId = $"dual-owner-{Guid.NewGuid():N}";
        var otherId = $"dual-other-{Guid.NewGuid():N}";
        var sessionId = await SeedFinishedSessionWithAssessmentsAsync(ownerId, includeTutorDraft: true);

        using var ownerClient = await CreateLearnerClientAsync(ownerId);
        var ownerResponse = await ownerClient.GetAsync($"/v1/speaking/sessions/{sessionId}/assessments");
        ownerResponse.EnsureSuccessStatusCode();

        using (var json = JsonDocument.Parse(await ownerResponse.Content.ReadAsStringAsync()))
        {
            Assert.Equal(sessionId, json.RootElement.GetProperty("sessionId").GetString());
            Assert.Equal(JsonValueKind.Object, json.RootElement.GetProperty("ai").ValueKind);
            Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("tutor").ValueKind);
        }

        using var otherClient = await CreateLearnerClientAsync(otherId);
        var otherResponse = await otherClient.GetAsync($"/v1/speaking/sessions/{sessionId}/assessments");
        Assert.Equal(HttpStatusCode.NotFound, otherResponse.StatusCode);
    }

    [Fact]
    public async Task ExpertDualAssessment_RequiresClaimOrAssignment()
    {
        var ownerId = $"dual-expert-owner-{Guid.NewGuid():N}";
        var sessionId = await SeedFinishedSessionWithAssessmentsAsync(ownerId, includeTutorDraft: false);

        using var expertClient = CreateExpertClient($"unclaimed-expert-{Guid.NewGuid():N}");
        var response = await expertClient.GetAsync($"/v1/expert/speaking/sessions/{sessionId}/assessments");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task LiveRoomObserverToken_RequiresAdminObserverAccess()
    {
        var ownerId = $"live-owner-{Guid.NewGuid():N}";
        var roomId = await SeedLiveTutorRoomAsync(ownerId, tutorId: "assigned-tutor-1");

        using var ownerClient = await CreateLearnerClientAsync(ownerId);
        var ownerObserverResponse = await ownerClient.PostAsJsonAsync(
            $"/v1/speaking/live-rooms/{roomId}/tokens",
            new { role = "observer" });

        Assert.Equal(HttpStatusCode.NotFound, ownerObserverResponse.StatusCode);

        using var otherClient = await CreateLearnerClientAsync($"live-other-{Guid.NewGuid():N}");
        var otherObserverResponse = await otherClient.PostAsJsonAsync(
            $"/v1/speaking/live-rooms/{roomId}/tokens",
            new { role = "observer" });

        Assert.Equal(HttpStatusCode.NotFound, otherObserverResponse.StatusCode);
    }

    private async Task<string> SeedFinishedSessionWithAssessmentsAsync(string ownerId, bool includeTutorDraft)
    {
        await _factory.EnsureLearnerProfileAsync(ownerId, $"{ownerId}@example.test", ownerId);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var now = DateTimeOffset.UtcNow;
        var sessionId = $"ss-dual-{Guid.NewGuid():N}";

        db.SpeakingSessions.Add(new SpeakingSession
        {
            Id = sessionId,
            UserId = ownerId,
            RolePlayCardId = "st-001",
            Mode = SpeakingSessionMode.AiExam,
            State = SpeakingSessionState.Finished,
            PrepStartedAt = now.AddMinutes(-10),
            RolePlayStartedAt = now.AddMinutes(-7),
            EndedAt = now.AddMinutes(-1),
            ElapsedSeconds = 300,
            ConsentVersion = "recording.v1",
            CreatedAt = now.AddMinutes(-12),
            UpdatedAt = now,
        });

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
            OverallSummary = "Seeded AI assessment for access-control tests.",
            ConfidenceBand = "high",
            GeneratedAt = now,
        });

        if (includeTutorDraft)
        {
            db.SpeakingTutorAssessments.Add(new SpeakingTutorAssessment
            {
                Id = $"sta-{Guid.NewGuid():N}",
                SpeakingSessionId = sessionId,
                TutorId = "expert-001",
                Intelligibility = 1,
                Fluency = 1,
                Appropriateness = 1,
                GrammarExpression = 1,
                RelationshipBuilding = 1,
                PatientPerspective = 1,
                Structure = 1,
                InformationGathering = 1,
                InformationGiving = 1,
                EstimatedScaledScore = 250,
                ReadinessBand = "draft",
                OverallFeedbackMarkdown = "Draft feedback must not be visible to learners.",
                StrengthsJson = "[]",
                ImprovementsJson = "[]",
                RecommendedDrillsJson = "[]",
                IsFinal = false,
                CreatedAt = now,
                UpdatedAt = now,
            });
        }

        await db.SaveChangesAsync();
        return sessionId;
    }

    private async Task<string> SeedLiveTutorRoomAsync(string ownerId, string tutorId)
    {
        await _factory.EnsureLearnerProfileAsync(ownerId, $"{ownerId}@example.test", ownerId);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var now = DateTimeOffset.UtcNow;
        var sessionId = $"ss-live-{Guid.NewGuid():N}";
        var roomId = $"lvr-{Guid.NewGuid():N}";

        db.SpeakingSessions.Add(new SpeakingSession
        {
            Id = sessionId,
            UserId = ownerId,
            RolePlayCardId = "st-001",
            Mode = SpeakingSessionMode.LiveTutor,
            State = SpeakingSessionState.Active,
            InterlocutorActorId = tutorId,
            LiveRoomId = roomId,
            PrepStartedAt = now.AddMinutes(-12),
            RolePlayStartedAt = now.AddMinutes(-8),
            ConsentVersion = "recording.v1",
            CreatedAt = now.AddMinutes(-15),
            UpdatedAt = now,
        });
        db.SpeakingLiveRooms.Add(new SpeakingLiveRoom
        {
            Id = roomId,
            SpeakingSessionId = sessionId,
            Provider = "livekit",
            RoomName = $"room-{Guid.NewGuid():N}",
            LearnerIdentity = $"learner:{ownerId}",
            TutorIdentity = $"tutor:{tutorId}",
            ScheduledStartUtc = now.AddMinutes(-10),
            ActualStartUtc = now.AddMinutes(-8),
            State = SpeakingLiveRoomState.Active,
            RecordingConsentVersion = "recording.v1",
            CreatedAt = now.AddMinutes(-10),
            UpdatedAt = now,
        });

        await db.SaveChangesAsync();
        return roomId;
    }

    private async Task<HttpClient> CreateLearnerClientAsync(string userId)
    {
        await _factory.EnsureLearnerProfileAsync(userId, $"{userId}@example.test", userId);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Debug-UserId", userId);
        client.DefaultRequestHeaders.Add("X-Debug-Email", $"{userId}@example.test");
        client.DefaultRequestHeaders.Add("X-Debug-Name", userId);
        client.DefaultRequestHeaders.Add("X-Debug-Role", ApplicationUserRoles.Learner);
        return client;
    }

    private HttpClient CreateExpertClient(string expertId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Debug-UserId", expertId);
        client.DefaultRequestHeaders.Add("X-Debug-Email", $"{expertId}@example.test");
        client.DefaultRequestHeaders.Add("X-Debug-Name", expertId);
        client.DefaultRequestHeaders.Add("X-Debug-Role", ApplicationUserRoles.Expert);
        return client;
    }
}