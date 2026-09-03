using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Rulebook;
using OetLearner.Api.Services.Writing;

namespace OetLearner.Api.Tests.Writing;

/// <summary>
/// Preparation-time Model Answer backfill
/// (<see cref="WritingTaskModelAnswerService.GenerateMissingAsync"/>): missing
/// answers are generated once, Ready answers are never regenerated blindly,
/// and repeat runs are idempotent with zero additional provider calls.
/// </summary>
public sealed class WritingModelAnswerBatchTests
{
    private static LearnerDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase($"writing-model-answer-batch-{Guid.NewGuid()}")
            .Options;
        return new LearnerDbContext(options);
    }

    private static async Task<Guid> SeedPublishedTaskAsync(LearnerDbContext db, string taskPrompt)
    {
        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        db.WritingScenarios.Add(new WritingScenario
        {
            Id = id,
            Title = "Batch task",
            Profession = "medicine",
            LetterType = "LT-RR",
            TaskPromptMarkdown = taskPrompt,
            Status = "published",
            AuthorId = "admin-1",
            CreatedAt = now,
            UpdatedAt = now,
        });
        db.WritingScenarioStructuredSentences.Add(new WritingScenarioStructuredSentence
        {
            Id = Guid.NewGuid(),
            ScenarioId = id,
            Ordinal = 1,
            SentenceText = "John Jones is a 54 year old man with severe asthma attending City Clinic for respiratory review and ongoing management.",
            RelevanceLabel = "relevant",
            CreatedAt = now,
        });
        await db.SaveChangesAsync();
        return id;
    }

    /// <summary>190-word exemplar; every sentence shares facts with the seed case notes.</summary>
    private static string ExemplarText()
    {
        const string sentence = "Dear Doctor, I am writing about John Jones and his severe asthma for respiratory review at City Clinic today.";
        return string.Join(" ", Enumerable.Repeat(sentence, 10));
    }

    private sealed class ExemplarGateway : IAiGatewayService
    {
        public int Calls { get; private set; }

        public AiGroundedPrompt BuildGroundedPrompt(AiGroundingContext context)
            => new()
            {
                SystemPrompt = "# OET AI — Rulebook-Grounded System Prompt\n**This call concerns WRITING**",
                TaskInstruction = "generate",
            };

        public Task<AiGatewayResult> CompleteAsync(AiGatewayRequest request, CancellationToken ct = default)
        {
            Calls++;
            var json = System.Text.Json.JsonSerializer.Serialize(new
            {
                modelAnswerText = ExemplarText(),
                whyThisWorks = new[] { "Grounded exemplar." },
                groundedFactReferences = new[] { "case-note-line:1" },
            });
            return Task.FromResult(new AiGatewayResult
            {
                Completion = json,
                ResolvedModel = "claude-sonnet-5",
            });
        }
    }

    [Fact]
    public async Task GenerateMissing_GeneratesOnceThenSkipsWithoutNewProviderCalls()
    {
        await using var db = NewDb();
        var gateway = new ExemplarGateway();
        var svc = new WritingTaskModelAnswerService(
            db, gateway, TimeProvider.System, NullLogger<WritingTaskModelAnswerService>.Instance);

        var scenarioId = await SeedPublishedTaskAsync(db, "Write a routine referral for John Jones to City Clinic.");

        var first = await svc.GenerateMissingAsync("admin-1", limit: 5, includeStale: false, CancellationToken.None);
        Assert.Equal(1, first.Generated);
        Assert.Equal(0, first.Skipped);
        Assert.Equal("generated", first.Items.Single().Outcome);
        Assert.Equal(1, gateway.Calls);

        var row = await db.WritingTaskModelAnswers.AsNoTracking().SingleAsync(a => a.ScenarioId == scenarioId);
        Assert.Equal(WritingAssessmentModelAnswerStatus.Ready, row.Status);
        Assert.False(row.IsCandidateVisible);

        // Awaiting approval: must NOT regenerate (would discard the pending
        // review and burn another provider call).
        var second = await svc.GenerateMissingAsync("admin-1", limit: 5, includeStale: false, CancellationToken.None);
        Assert.Equal(0, second.Generated);
        Assert.Equal(1, second.Skipped);
        Assert.Equal("awaiting_approval", second.Items.Single().HoldReason);
        Assert.Equal(1, gateway.Calls);

        // Approved + fresh: reused forever.
        await svc.ApproveAsync(scenarioId, "admin-1", CancellationToken.None);
        var third = await svc.GenerateMissingAsync("admin-1", limit: 5, includeStale: false, CancellationToken.None);
        Assert.Equal(0, third.Generated);
        Assert.Equal("already_ready", third.Items.Single().HoldReason);
        Assert.Equal(1, gateway.Calls);
    }

    [Fact]
    public async Task GenerateMissing_RegeneratesStaleOnlyOnExplicitRequest()
    {
        await using var db = NewDb();
        var gateway = new ExemplarGateway();
        var svc = new WritingTaskModelAnswerService(
            db, gateway, TimeProvider.System, NullLogger<WritingTaskModelAnswerService>.Instance);

        var scenarioId = await SeedPublishedTaskAsync(db, "Write a routine referral for John Jones to City Clinic.");
        await svc.GenerateMissingAsync("admin-1", limit: 5, includeStale: false, CancellationToken.None);
        await svc.ApproveAsync(scenarioId, "admin-1", CancellationToken.None);
        Assert.Equal(1, gateway.Calls);

        // Drift the source content: the approved answer is now stale.
        var scenario = await db.WritingScenarios.FirstAsync(s => s.Id == scenarioId);
        scenario.TaskPromptMarkdown = "Write a routine referral for John Jones to City Clinic urgently.";
        await db.SaveChangesAsync();

        var withoutStale = await svc.GenerateMissingAsync("admin-1", limit: 5, includeStale: false, CancellationToken.None);
        Assert.Equal(0, withoutStale.Generated);
        Assert.Equal("stale_refresh_not_requested", withoutStale.Items.Single().HoldReason);
        Assert.Equal(1, gateway.Calls);

        var withStale = await svc.GenerateMissingAsync("admin-1", limit: 5, includeStale: true, CancellationToken.None);
        Assert.Equal(1, withStale.Generated);
        Assert.Equal(2, gateway.Calls);
    }
}
