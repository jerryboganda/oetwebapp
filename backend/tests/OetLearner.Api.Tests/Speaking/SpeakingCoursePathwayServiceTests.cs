using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Speaking;

namespace OetLearner.Api.Tests.Speaking;

public sealed class SpeakingCoursePathwayServiceTests : IAsyncLifetime
{
    private LearnerDbContext _db = default!;
    private SpeakingCoursePathwayService _svc = default!;

    public Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase($"speaking-pathway-{Guid.NewGuid():N}")
            .Options;
        _db = new LearnerDbContext(options);
        _svc = new SpeakingCoursePathwayService(_db);
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _db.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task GetForLearnerAsync_CountsCompletedSpeakingMockSessions()
    {
        _db.SpeakingMockSessions.AddRange(
            BuildCompletedMockSession("mock-session-1", "learner-pathway"),
            BuildCompletedMockSession("mock-session-2", "learner-pathway"));
        await _db.SaveChangesAsync();

        using var document = JsonSerializer.SerializeToDocument(await _svc.GetForLearnerAsync(
            "learner-pathway",
            CancellationToken.None));

        var stage14 = FindStage(document.RootElement, 14);
        var stage16 = FindStage(document.RootElement, 16);

        Assert.Equal("completed", stage14.GetProperty("state").GetString());
        Assert.Equal("completed", stage16.GetProperty("state").GetString());
    }

    [Fact]
    public async Task GetForLearnerAsync_CountsMixedCompletedCanonicalAndLegacyMockHistory()
    {
        _db.SpeakingMockSessions.Add(BuildCompletedMockSession("mock-session-new", "learner-mixed"));
        _db.SpeakingSessions.Add(BuildFinishedLegacyMockSession(
            "legacy-session-1",
            "learner-mixed",
            mockSetId: "legacy-set-1",
            mockSessionId: null));
        await _db.SaveChangesAsync();

        using var document = JsonSerializer.SerializeToDocument(await _svc.GetForLearnerAsync(
            "learner-mixed",
            CancellationToken.None));

        var stage16 = FindStage(document.RootElement, 16);

        Assert.Equal("completed", stage16.GetProperty("state").GetString());
    }

    [Fact]
    public async Task GetForLearnerAsync_DeduplicatesLegacyRowsForCompletedSpeakingMockSession()
    {
        _db.SpeakingMockSessions.Add(BuildCompletedMockSession("mock-session-new", "learner-dedup"));
        _db.SpeakingSessions.Add(BuildFinishedLegacyMockSession(
            "mirrored-session-1",
            "learner-dedup",
            mockSetId: "set-mock-session-new",
            mockSessionId: "mock-session-new"));
        await _db.SaveChangesAsync();

        using var document = JsonSerializer.SerializeToDocument(await _svc.GetForLearnerAsync(
            "learner-dedup",
            CancellationToken.None));

        var stage14 = FindStage(document.RootElement, 14);
        var stage16 = FindStage(document.RootElement, 16);

        Assert.Equal("completed", stage14.GetProperty("state").GetString());
        Assert.NotEqual("completed", stage16.GetProperty("state").GetString());
    }

    [Fact]
    public async Task GetForLearnerAsync_RoutesRolePlayStagesToExistingChooser()
    {
        using var document = JsonSerializer.SerializeToDocument(await _svc.GetForLearnerAsync(
            "learner-route",
            CancellationToken.None));

        var rolePlayStage = FindStage(document.RootElement, 11);

        Assert.Equal("/speaking/selection", rolePlayStage.GetProperty("actionHref").GetString());
    }

    private static SpeakingMockSession BuildCompletedMockSession(string id, string userId) => new()
    {
        Id = id,
        MockSetId = $"set-{id}",
        UserId = userId,
        Attempt1Id = $"attempt-1-{id}",
        Attempt2Id = $"attempt-2-{id}",
        State = SpeakingMockSessionState.Completed,
        OrchestratorState = SpeakingMockOrchestratorStates.Aggregated,
        StartedAt = DateTimeOffset.UtcNow.AddMinutes(-20),
        CompletedAt = DateTimeOffset.UtcNow,
    };

    private static SpeakingSession BuildFinishedLegacyMockSession(
        string id,
        string userId,
        string? mockSetId,
        string? mockSessionId) => new()
        {
            Id = id,
            UserId = userId,
            RolePlayCardId = $"card-{id}",
            MockSetId = mockSetId,
            MockSessionId = mockSessionId,
            State = SpeakingSessionState.Finished,
            Mode = SpeakingSessionMode.AiExam,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-30),
            UpdatedAt = DateTimeOffset.UtcNow,
            EndedAt = DateTimeOffset.UtcNow,
        };

    private static JsonElement FindStage(JsonElement root, int orderIndex)
    {
        foreach (var stage in root.GetProperty("stages").EnumerateArray())
        {
            if (stage.GetProperty("orderIndex").GetInt32() == orderIndex)
            {
                return stage;
            }
        }

        throw new InvalidOperationException($"Stage {orderIndex} not found.");
    }
}
