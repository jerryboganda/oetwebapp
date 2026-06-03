using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Rulebook;
using OetLearner.Api.Services.Speaking;
using OetLearner.Api.Tests;

namespace OetLearner.Api.Tests.Speaking;

public sealed class SpeakingDrillServiceTests : IAsyncLifetime
{
    private LearnerDbContext _db = default!;
    private SpeakingDrillService _svc = default!;

    public Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase($"speaking-drills-{Guid.NewGuid():N}")
            .Options;
        _db = new LearnerDbContext(options);
        _svc = new SpeakingDrillService(
            _db,
            new ThrowingAiGateway(),
            new InMemoryFileStorage());
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _db.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task StartAttempt_AcceptsLegacyContentId_AndCreatesCanonicalDrillItem()
    {
        await SeedLegacyContentDrillAsync("legacy-drill-001", scenarioType: "phrasing", criteriaJson: "[\"fluency\"]");

        var attempt = await _svc.StartAttemptAsync(
            "learner-legacy-drill",
            "legacy-drill-001",
            sourceCode: null,
            CancellationToken.None);

        Assert.Equal("sdi-legacy-legacy-drill-001", attempt.DrillId);
        Assert.Equal(attempt.DrillId, await _db.SpeakingDrillItems.Select(d => d.Id).SingleAsync());
        Assert.Equal(attempt.DrillId, await _db.SpeakingDrillAttempts.Select(a => a.DrillItemId).SingleAsync());
    }

    [Fact]
    public async Task ScoreAttempt_RequiresUploadedAudio()
    {
        await SeedCanonicalDrillAsync("canonical-drill-001", "content-drill-001");
        var attempt = await _svc.StartAttemptAsync(
            "learner-drill-audio-required",
            "canonical-drill-001",
            sourceCode: null,
            CancellationToken.None);

        var ex = await Assert.ThrowsAsync<ApiException>(() => _svc.ScoreAttemptAsync(
            "learner-drill-audio-required",
            attempt.AttemptId,
            CancellationToken.None));

        Assert.Equal("speaking_drill_recording_required", ex.ErrorCode);
    }

    private async Task SeedCanonicalDrillAsync(string drillId, string contentId)
    {
        await SeedLegacyContentDrillAsync(contentId, scenarioType: "empathy", criteriaJson: "[\"relationshipBuilding\"]");
        _db.SpeakingDrillItems.Add(new SpeakingDrillItem
        {
            Id = drillId,
            ContentItemId = contentId,
            DrillKind = SpeakingDrillKind.Empathy,
            TargetCriteriaJson = "[\"relationshipBuilding\"]",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync();
    }

    private async Task SeedLegacyContentDrillAsync(string contentId, string scenarioType, string criteriaJson)
    {
        _db.ContentItems.Add(new ContentItem
        {
            Id = contentId,
            ContentType = "speaking_drill",
            SubtestCode = "speaking",
            ScenarioType = scenarioType,
            ProfessionId = "nursing",
            Title = "Focused speaking drill",
            Difficulty = "core",
            Status = ContentStatus.Published,
            PublishedRevisionId = $"{contentId}-r1",
            CriteriaFocusJson = criteriaJson,
            DetailJson = "{\"instructionText\":\"Record a concise response.\"}",
        });
        await _db.SaveChangesAsync();
    }

    private sealed class ThrowingAiGateway : IAiGatewayService
    {
        public Task<AiGatewayResult> CompleteAsync(AiGatewayRequest request, CancellationToken ct = default)
            => throw new NotSupportedException("AI gateway should not be called by deterministic drill tests.");

        public AiGroundedPrompt BuildGroundedPrompt(AiGroundingContext context)
            => throw new NotSupportedException("AI gateway should not be called by deterministic drill tests.");
    }
}