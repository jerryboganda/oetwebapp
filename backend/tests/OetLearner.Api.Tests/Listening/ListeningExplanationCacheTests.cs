using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Ai;
using OetLearner.Api.Services.Listening;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Tests.Listening;

public sealed class ListeningExplanationCacheTests
{
    [Fact]
    public async Task First_explanation_generates_subsequent_are_cache_hits()
    {
        await using var db = NewDb();
        await SeedAsync(db);

        var gateway = new CountingGateway();
        var cache = new MemoryResultCache();
        var service = new ListeningExplanationService(
            db,
            gateway,
            NullLogger<ListeningExplanationService>.Instance,
            explanationCache: null,
            resultCache: cache);

        var first = await service.GetSubmittedAttemptExplanationAsync(
            "learner-1", "listening-explanation-attempt-1", "listening-explanation-q1", "en", default);
        var second = await service.GetSubmittedAttemptExplanationAsync(
            "learner-1", "listening-explanation-attempt-1", "listening-explanation-q1", "en", default);

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
            Id = "listening-paper-1",
            SubtestCode = "listening",
            Title = "Listening explanation cache paper",
            Slug = "listening-explanation-cache-paper",
            Status = ContentStatus.Published,
            PublishedRevisionId = "listening-revision-1",
        });
        db.ListeningQuestions.Add(new ListeningQuestion
        {
            Id = "listening-explanation-q1",
            PaperId = "listening-paper-1",
            ListeningPartId = "listening-part-b1",
            QuestionNumber = 1,
            DisplayOrder = 1,
            QuestionType = ListeningQuestionType.MultipleChoice3,
            Stem = "Which option is supported?",
            CorrectAnswerJson = "\"A\"",
            TranscriptEvidenceText = "The speaker explicitly supports option A.",
            Version = 1,
        });
        db.ListeningAttempts.Add(new ListeningAttempt
        {
            Id = "listening-explanation-attempt-1",
            UserId = "learner-1",
            PaperId = "listening-paper-1",
            StartedAt = now.AddMinutes(-10),
            LastActivityAt = now,
            SubmittedAt = now,
            Status = ListeningAttemptStatus.Submitted,
            MaxRawScore = 1,
            PaperRevisionId = "listening-revision-1",
            LastQuestionVersionMapJson = "{\"listening-explanation-q1\":1}",
        });
        db.ListeningAnswers.Add(new ListeningAnswer
        {
            Id = "listening-explanation-answer-1",
            ListeningAttemptId = "listening-explanation-attempt-1",
            ListeningQuestionId = "listening-explanation-q1",
            UserAnswerJson = "\"B\"",
            QuestionVersionSnapshot = 1,
            AnsweredAt = now,
        });
        db.AssessmentRationales.Add(new AssessmentRationale
        {
            Id = "rationale-listening-explanation-q1",
            Assessment = "listening",
            QuestionRevisionId = "listening-explanation-q1",
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
                Completion = """{"whyCorrect":"Option A is stated.","whyWrong":"B is not supported.","trapName":"Near miss","avoidTip":"Stay with the transcript."}""",
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
