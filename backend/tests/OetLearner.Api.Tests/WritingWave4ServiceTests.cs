using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Rulebook;
using OetLearner.Api.Services.Writing;

namespace OetLearner.Api.Tests;

public class WritingWave4ServiceTests
{
    private const string UserId = "learner-writing-wave4";

    [Fact]
    public async Task GetExemplarAsync_HidesDraftsFromLearnersButAdminCanReadThem()
    {
        var db = BuildDb();
        var draftId = Guid.NewGuid();
        db.WritingExemplars.Add(Exemplar(draftId, "draft"));
        await db.SaveChangesAsync();
        var service = new WritingExemplarService(
            db,
            new FixedClock(),
            new StubEmbeddingService(draftId),
            new StubAiGateway(),
            NullLogger<WritingExemplarService>.Instance);

        var learnerRead = await service.GetExemplarAsync(UserId, draftId, CancellationToken.None);
        var adminRead = await service.AdminGetExemplarAsync("admin", draftId, CancellationToken.None);

        Assert.Null(learnerRead);
        Assert.NotNull(adminRead);

        await db.DisposeAsync();
    }

    [Fact]
    public async Task ClosestExemplarAsync_DoesNotReturnDraftEvenIfSimilarityIndexIsStale()
    {
        var db = BuildDb();
        var draftId = Guid.NewGuid();
        db.WritingExemplars.Add(Exemplar(draftId, "draft"));
        await db.SaveChangesAsync();
        var service = new WritingExemplarService(
            db,
            new FixedClock(),
            new StubEmbeddingService(draftId),
            new StubAiGateway(),
            NullLogger<WritingExemplarService>.Instance);

        var result = await service.GetClosestExemplarForScenarioAsync(UserId, Guid.NewGuid(), CancellationToken.None);

        Assert.Null(result);

        await db.DisposeAsync();
    }

    [Fact]
    public async Task AdminCreateExemplarAsync_WithPublishedStatusMakesItLearnerReadable()
    {
        var db = BuildDb();
        var embeddings = new StubEmbeddingService(Guid.Empty);
        var service = new WritingExemplarService(
            db,
            new FixedClock(),
            embeddings,
            new StubAiGateway(),
            NullLogger<WritingExemplarService>.Instance);

        var saved = await service.AdminCreateExemplarAsync("admin", new WritingExemplarUpsertRequest(
            ScenarioId: null,
            Profession: "medicine",
            LetterType: "LT-RR",
            Difficulty: 3,
            TargetBand: "A",
            LetterContent: "Dear Doctor,\n\nI am writing to refer this patient for ongoing care.",
            Annotations: Array.Empty<WritingExemplarAnnotationResponse>(),
            AuthorNote: null,
            Status: "published"), CancellationToken.None);

        var learnerRead = await service.GetExemplarAsync(UserId, saved.Id, CancellationToken.None);

        Assert.Equal("published", saved.Status);
        Assert.NotNull(learnerRead);
        Assert.Equal(1, embeddings.EmbedCalls);

        await db.DisposeAsync();
    }

    [Fact]
    public async Task AdminTestGradeExemplarAsync_PassesQualityBarWhenAiScoresAtOrAboveA()
    {
        var db = BuildDb();
        var exemplarId = Guid.NewGuid();
        db.WritingExemplars.Add(Exemplar(exemplarId, "published"));
        await db.SaveChangesAsync();
        // Stub returns a rubric scoring 3+7+7+7+7+7 = 38, which is >= 36 so
        // the publish quality bar is satisfied.
        var aiGateway = new StubAiGateway(
            completion: "{ \"c1\": 3, \"c2\": 7, \"c3\": 7, \"c4\": 7, \"c5\": 7, \"c6\": 7, \"rawTotal\": 38, \"estimatedBand\": 450, \"bandLabel\": \"A\", \"perCriterion\": {}, \"topThreePriorities\": [], \"confidenceFlag\": \"high\", \"modelUsed\": \"writing.score.v1\" }");
        var service = new WritingExemplarService(
            db,
            new FixedClock(),
            new StubEmbeddingService(exemplarId),
            aiGateway,
            NullLogger<WritingExemplarService>.Instance);

        var result = await service.AdminTestGradeExemplarAsync("admin", exemplarId, CancellationToken.None);

        Assert.NotNull(result);
        Assert.True(result.PassesQualityBar);
        Assert.Equal(38, result.Grade.RawTotal);
        Assert.Equal("A", result.Grade.BandLabel);

        await db.DisposeAsync();
    }

    [Fact]
    public async Task AdminTestGradeExemplarAsync_FailsQualityBarWhenAiScoresBelowA()
    {
        var db = BuildDb();
        var exemplarId = Guid.NewGuid();
        db.WritingExemplars.Add(Exemplar(exemplarId, "published"));
        await db.SaveChangesAsync();
        // Stub returns a rubric scoring 2+5+5+5+5+5 = 27, which is below the
        // 36 publish bar, so PassesQualityBar must be false.
        var aiGateway = new StubAiGateway(
            completion: "{ \"c1\": 2, \"c2\": 5, \"c3\": 5, \"c4\": 5, \"c5\": 5, \"c6\": 5, \"rawTotal\": 27, \"estimatedBand\": 300, \"bandLabel\": \"C+\", \"perCriterion\": {}, \"topThreePriorities\": [], \"confidenceFlag\": \"medium\", \"modelUsed\": \"writing.score.v1\" }");
        var service = new WritingExemplarService(
            db,
            new FixedClock(),
            new StubEmbeddingService(exemplarId),
            aiGateway,
            NullLogger<WritingExemplarService>.Instance);

        var result = await service.AdminTestGradeExemplarAsync("admin", exemplarId, CancellationToken.None);

        Assert.NotNull(result);
        Assert.False(result.PassesQualityBar);

        await db.DisposeAsync();
    }

    [Fact]
    public async Task ListMyMistakesAsync_IncludesRuleViolationAnalyticsWhenNoPersistedStatExists()
    {
        var db = BuildDb();
        var clock = new FixedClock();
        var mistakeId = Guid.NewGuid();
        db.WritingCommonMistakes.Add(new WritingCommonMistake
        {
            Id = mistakeId,
            Category = "content",
            Summary = "Missing key clinical content",
            ExampleWrong = "The discharge diagnosis is omitted.",
            ExampleRight = "The discharge diagnosis is stated clearly.",
            CanonRuleId = "missing_key_content",
            RelatedSubSkill = "W3",
            CreatedAt = clock.GetUtcNow().AddDays(-4),
        });
        db.WritingRuleViolations.AddRange(
            RuleViolation("rv-1", "missing_key_content", clock.GetUtcNow().AddDays(-2)),
            RuleViolation("rv-2", "missing_key_content", clock.GetUtcNow().AddDays(-1)));
        await db.SaveChangesAsync();
        var service = new WritingMistakeService(db, clock);

        var result = await service.ListMyMistakesAsync(UserId, CancellationToken.None);

        var row = Assert.Single(result.Items);
        Assert.Equal(mistakeId, row.Id);
        Assert.Equal(2, row.Stat.OccurrenceCount);

        await db.DisposeAsync();
    }

    private static LearnerDbContext BuildDb()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new LearnerDbContext(options);
    }

    private static WritingExemplar Exemplar(Guid id, string status)
    {
        var now = new DateTimeOffset(2026, 5, 27, 8, 0, 0, TimeSpan.Zero);
        return new WritingExemplar
        {
            Id = id,
            ScenarioId = Guid.NewGuid(),
            Profession = "medicine",
            LetterType = "LT-RR",
            TargetBand = "A",
            LetterContent = "Dear Doctor,\n\nI am writing to refer this patient for ongoing care.",
            Status = status,
            AuthorId = "admin",
            CreatedAt = now,
            PublishedAt = status == "published" ? now : null,
        };
    }

    private static WritingRuleViolation RuleViolation(string id, string ruleId, DateTimeOffset generatedAt)
        => new()
        {
            Id = id,
            AttemptId = $"attempt-{id}",
            EvaluationId = $"evaluation-{id}",
            UserId = UserId,
            Profession = "medicine",
            LetterType = "LT-RR",
            RuleId = ruleId,
            Severity = "major",
            Source = "rulebook",
            Message = "Missing key content.",
            GeneratedAt = generatedAt,
        };

    private sealed class StubEmbeddingService(Guid exemplarId) : IWritingExemplarEmbeddingService
    {
        public int EmbedCalls { get; private set; }

        public Task<float[]> EmbedExemplarAsync(string userId, Guid exemplarId, CancellationToken ct)
        {
            EmbedCalls++;
            return Task.FromResult(Array.Empty<float>());
        }

        public Task<float[]> EmbedScenarioAsync(string userId, Guid scenarioId, CancellationToken ct)
            => Task.FromResult(Array.Empty<float>());

        public Task<IReadOnlyList<WritingExemplarSimilarity>> FindClosestAsync(string userId, Guid scenarioId, int take = 5, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<WritingExemplarSimilarity>>([
                new WritingExemplarSimilarity(exemplarId, 0.9, "LT-RR", "medicine"),
            ]);

        // pgvector backfill — no-op in tests (the JSON path is the source of
        // truth here and the SQLite/in-memory providers do not map the
        // Embedding column).
        public Task<(int Exemplars, int Scenarios)> BackfillFromJsonAsync(CancellationToken ct)
            => Task.FromResult<(int Exemplars, int Scenarios)>((0, 0));
    }

    private sealed class FixedClock : TimeProvider
    {
        private readonly DateTimeOffset now = new(2026, 5, 27, 8, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class StubAiGateway : IAiGatewayService
    {
        private readonly string _completion;

        public StubAiGateway(string? completion = null)
        {
            _completion = completion ?? "{ \"c1\": 0, \"c2\": 0, \"c3\": 0, \"c4\": 0, \"c5\": 0, \"c6\": 0, \"rawTotal\": 0, \"estimatedBand\": 0, \"bandLabel\": \"E\", \"perCriterion\": {}, \"topThreePriorities\": [], \"confidenceFlag\": \"low\", \"modelUsed\": \"writing.score.v1\" }";
        }

        public AiGroundedPrompt BuildGroundedPrompt(AiGroundingContext context)
            => new()
            {
                SystemPrompt = "OET AI — Rulebook-Grounded System Prompt\n(test fixture)\n",
                TaskInstruction = "Score this writing attempt.",
                Metadata = new AiGroundedPromptMetadata
                {
                    RulebookVersion = "test-1.0.0",
                    RulebookKind = context.Kind,
                    Profession = context.Profession,
                    ScoringPassMark = 350,
                    ScoringGrade = "B",
                    AppliedRulesCount = 0,
                    AppliedRuleIds = Array.Empty<string>(),
                },
            };

        public Task<AiGatewayResult> CompleteAsync(AiGatewayRequest request, CancellationToken ct = default)
            => Task.FromResult(new AiGatewayResult { Completion = _completion });
    }
}