using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Speaking;
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
            // Nothing has been handed in yet: the pages fall back to neutral wording.
            Assert.Equal(JsonValueKind.Null, root.GetProperty("inputKind").ValueKind);
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

    // inputKind tells the results pages what the learner handed in, so a live conversation (a saved
    // transcript, no audio at all) is never described as a recording.
    [Theory]
    [InlineData("realtime_transcript", "live_voice")]
    [InlineData("recording", "recording")]
    [InlineData("recording_and_realtime_transcript", "recording")]
    [InlineData("archived_recording", "recording")]
    [InlineData("warmup_recording_only", null)]
    [InlineData("warmup_recording_and_realtime_transcript", "live_voice")]
    [InlineData("recorder_transcript_only", null)]
    [InlineData("superseded_realtime_transcript", null)]
    [InlineData("nothing", null)]
    public async Task Results_InputKind_ReflectsWhatTheLearnerHandedIn(string scenario, string? expected)
    {
        var ownerId = $"results-kind-{Guid.NewGuid():N}";
        var (sessionId, _) = await SeedFinishedSessionAsync(ownerId);
        var realtime = $"{LiveVoiceService.TranscriptProviderPrefix}openai";
        switch (scenario)
        {
            case "realtime_transcript":
                await SeedTranscriptAsync(sessionId, realtime);
                break;
            case "recording":
                await SeedRecordingAsync(sessionId);
                break;
            case "recording_and_realtime_transcript":
                await SeedTranscriptAsync(sessionId, realtime);
                await SeedRecordingAsync(sessionId);
                break;
            case "archived_recording":
                // A retention-swept recording is still what the learner handed in.
                await SeedRecordingAsync(sessionId, isArchived: true);
                break;
            case "warmup_recording_only":
                await SeedRecordingAsync(sessionId, isWarmup: true);
                break;
            case "warmup_recording_and_realtime_transcript":
                // The unscored warm-up is not the role-play: the live conversation still decides.
                await SeedRecordingAsync(sessionId, isWarmup: true);
                await SeedTranscriptAsync(sessionId, realtime);
                break;
            case "recorder_transcript_only":
                // A Whisper transcript with no recording row (still queued, or already purged) says nothing.
                await SeedTranscriptAsync(sessionId, "openai-whisper");
                break;
            case "superseded_realtime_transcript":
                await SeedTranscriptAsync(sessionId, realtime, isLatest: false);
                break;
        }
        using var owner = await CreateLearnerClientAsync(ownerId);

        var response = await owner.GetAsync($"/v1/speaking/sessions/{sessionId}/results");

        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var kind = json.RootElement.GetProperty("inputKind");
        Assert.Equal(expected, kind.ValueKind == JsonValueKind.Null ? null : kind.GetString());
    }

    [Fact]
    public async Task Results_InputKind_IsNeverRevealedToANonOwner()
    {
        var ownerId = $"results-kind-owner-{Guid.NewGuid():N}";
        var (sessionId, _) = await SeedFinishedSessionAsync(ownerId);
        await SeedTranscriptAsync(sessionId, $"{LiveVoiceService.TranscriptProviderPrefix}gemini");
        using var other = await CreateLearnerClientAsync($"results-kind-other-{Guid.NewGuid():N}");

        var response = await other.GetAsync($"/v1/speaking/sessions/{sessionId}/results");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task SeedTranscriptAsync(string sessionId, string provider, bool isLatest = true)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        db.SpeakingTranscripts.Add(new SpeakingTranscript
        {
            Id = $"spt_{Guid.NewGuid():N}",
            SpeakingSessionId = sessionId,
            Provider = provider,
            SegmentsJson = "[]",
            IsLatest = isLatest,
            GeneratedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    private async Task SeedRecordingAsync(string sessionId, bool isWarmup = false, bool isArchived = false)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        db.SpeakingRecordings.Add(new SpeakingRecording
        {
            Id = isWarmup ? $"srec_warmup_{Guid.NewGuid():N}" : SpeakingSessionRecordingService.RecordingIdFor(sessionId),
            SpeakingSessionId = sessionId,
            MediaAssetId = $"smed_{Guid.NewGuid():N}",
            Sha256 = string.Empty,
            IsWarmup = isWarmup,
            IsArchived = isArchived,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
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
