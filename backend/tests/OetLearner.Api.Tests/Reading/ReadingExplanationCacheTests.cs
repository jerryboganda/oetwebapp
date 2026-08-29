using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Ai;
using OetLearner.Api.Services.Reading;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Tests.Reading;

public sealed class ReadingExplanationCacheTests
{
    [Fact]
    public async Task First_explanation_generates_subsequent_are_cache_hits()
    {
        await using var db = NewDb();
        await SeedAsync(db);

        var gateway = new CountingGateway();
        var cache = new MemoryResultCache();
        var service = new ReadingExplanationService(
            db,
            new RulebookLoader(),
            gateway,
            NullLogger<ReadingExplanationService>.Instance,
            explanationCache: null,
            resultCache: cache);

        var first = await service.GetSubmittedAttemptExplanationAsync(
            "learner-1", "reading-explanation-cache-attempt-1", "reading-explanation-cache-q1", "en", default);
        var second = await service.GetSubmittedAttemptExplanationAsync(
            "learner-1", "reading-explanation-cache-attempt-1", "reading-explanation-cache-q1", "en", default);

        Assert.Equal(1, gateway.Calls);
        Assert.Equal(1, cache.Stores);
        Assert.False(first.Cached);
        Assert.True(second.Cached);
        Assert.Equal(first.WhyCorrect, second.WhyCorrect);
    }

    private static LearnerDbContext NewDb() => new(
        new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static async Task SeedAsync(LearnerDbContext db)
    {
        var now = DateTimeOffset.UtcNow;
        db.ContentPapers.Add(new ContentPaper
        {
            Id = "reading-cache-paper-1",
            SubtestCode = "reading",
            Title = "Reading explanation cache paper",
            Slug = "reading-explanation-cache-paper",
            Status = ContentStatus.Published,
            PublishedRevisionId = "reading-cache-revision-1",
        });
        db.ReadingParts.Add(new ReadingPart
        {
            Id = "reading-cache-part-a",
            PaperId = "reading-cache-paper-1",
            PartCode = ReadingPartCode.A,
            TimeLimitMinutes = 15,
            MaxRawScore = 1,
        });
        db.ReadingQuestions.Add(new ReadingQuestion
        {
            Id = "reading-explanation-cache-q1",
            ReadingPartId = "reading-cache-part-a",
            QuestionType = ReadingQuestionType.MultipleChoice3,
            Stem = "Which option is supported?",
            OptionsJson = "[\"A\",\"B\",\"C\"]",
            CorrectAnswerJson = "\"A\"",
        });
        db.ReadingAttempts.Add(new ReadingAttempt
        {
            Id = "reading-explanation-cache-attempt-1",
            UserId = "learner-1",
            PaperId = "reading-cache-paper-1",
            StartedAt = now.AddMinutes(-10),
            LastActivityAt = now,
            SubmittedAt = now,
            Status = ReadingAttemptStatus.Submitted,
            MaxRawScore = 1,
            PaperRevisionId = "reading-cache-revision-1",
        });
        db.ReadingAnswers.Add(new ReadingAnswer
        {
            Id = "reading-explanation-cache-answer-1",
            ReadingAttemptId = "reading-explanation-cache-attempt-1",
            ReadingQuestionId = "reading-explanation-cache-q1",
            UserAnswerJson = "\"B\"",
            AnsweredAt = now,
            CreatedAt = now,
            UpdatedAt = now,
        });
        db.AssessmentRationales.Add(new AssessmentRationale
        {
            Id = "rationale-reading-explanation-cache-q1",
            Assessment = "reading",
            QuestionRevisionId = "reading-explanation-cache-q1",
            SourceSentence = "The source sentence supports the authored answer.",
            RationaleText = "The authored rationale explains the answer.",
            EvidenceCount = 1,
            Status = AssessmentGovernanceStatus.Effective,
            CreatedByUserId = "author-1",
            ApprovedByUserId = "reviewer-1",
            CreatedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();
    }

    private sealed class CountingGateway : IAiGatewayService
    {
        public int Calls { get; private set; }

        public Task<AiGatewayResult> CompleteAsync(AiGatewayRequest request, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(new AiGatewayResult
            {
                Completion = """{"whyCorrect":"Option A is stated.","whyWrong":"B is not supported.","trapName":"Near miss","avoidTip":"Stay with the passage."}""",
            });
        }

        public AiGroundedPrompt BuildGroundedPrompt(AiGroundingContext context)
            => new()
            {
                SystemPrompt = "grounded test prompt",
                TaskInstruction = "explain",
                Metadata = new AiGroundedPromptMetadata
                {
                    RulebookKind = context.Kind,
                    Profession = context.Profession,
                    RulebookVersion = "test",
                },
            };
    }

    private sealed class MemoryResultCache : IAiResultCacheService
    {
        private readonly Dictionary<string, string> _items = new(StringComparer.Ordinal);
        public int Stores { get; private set; }

        public string BuildCacheKey(
            string featureCode,
            string module,
            string? attemptId,
            string? questionRevisionId,
            string? storedAnswerHash,
            string? language,
            string? promptVersion,
            string? rulebookVersion)
            => string.Join('|', featureCode, module, attemptId, questionRevisionId, storedAnswerHash, language, promptVersion, rulebookVersion);

        public Task<string?> TryGetAsync(string cacheKey, CancellationToken ct)
            => Task.FromResult(_items.TryGetValue(cacheKey, out var payload) ? payload : null);

        public Task StoreAsync(
            string cacheKey,
            string featureCode,
            string module,
            string payloadJson,
            string? promptVersion,
            string? rulebookVersion,
            string? resourceVersion,
            TimeSpan? ttl,
            CancellationToken ct)
        {
            Stores++;
            _items[cacheKey] = payloadJson;
            return Task.CompletedTask;
        }
    }
}
