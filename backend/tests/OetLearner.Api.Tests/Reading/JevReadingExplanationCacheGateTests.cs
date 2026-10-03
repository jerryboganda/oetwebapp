using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Ai;
using OetLearner.Api.Services.Ai.TypeSafe;
using OetLearner.Api.Services.Reading;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Tests.Reading;

/// <summary>
/// Wave 3 — the Reading explanation consumes <see cref="AiGatewayResult.JevAdvisory"/>. A real
/// <c>review_required</c> judgment keeps the explanation out of the 30-day result cache and the
/// cross-learner explanation cache while the learner is still served; the learner's own re-view inside
/// the AI replay window is answered from a short-lived held copy, because the coordinator refuses an
/// identical gateway call there. A null (flags off), <c>ok</c> or <c>unavailable</c> advisory caches
/// exactly as before. Also pins the trapName vocabulary to <see cref="ReadingDistractorCategory"/> and
/// the v2 cache-key versioning.
/// </summary>
public sealed class JevReadingExplanationCacheGateTests
{
    private const string AttemptId = "jev-reading-gate-attempt-1";
    private const string QuestionId = "jev-reading-gate-q1";
    private const string RationaleId = "rationale-jev-reading-gate-q1";
    private const string ExplanationText = "Option A is stated in the passage.";

    [Theory]
    [InlineData("contradicted")]
    [InlineData("insufficient")]
    [InlineData("off_task")]
    [InlineData("unsafe")]
    public async Task Advisory_requiring_review_skips_the_shared_caches_and_keeps_only_a_short_lived_learner_copy(string kind)
    {
        var advisory = Advisory(kind)!;
        await using var rig = await Rig.CreateAsync(advisory);

        var first = await rig.ExplainAsync();
        // The scripted gateway refuses a repeat call like the real coordinator inside the replay window,
        // so this re-view only succeeds if it is answered without one.
        var second = await rig.ExplainAsync();

        Assert.Equal(ExplanationText, first.WhyCorrect);
        Assert.False(first.Cached);
        Assert.True(second.Cached);
        Assert.Equal(first.WhyCorrect, second.WhyCorrect);
        Assert.Equal(1, rig.Gateway.Calls);
        Assert.Equal(0, rig.ExplanationCache.Stores);

        // Only the learner's own held copy is stored: a different key and prompt variant from the normal
        // 30-day entry (AiResultCaches.CacheKey is unique and never purged), and never longer than the window.
        var stored = Assert.Single(rig.ResultCache.StoreCalls);
        var normalKey = rig.ResultCache.BuildCacheKey(
            AiFeatureCodes.ReadingExplanation, "reading", AttemptId, QuestionId, "B", "en",
            "reading.explanation.v2", RationaleId);
        Assert.NotEqual(normalKey, stored.Key);
        Assert.Equal("reading.explanation.v2.jev-held", stored.PromptVersion);
        Assert.Equal<TimeSpan?>(AiOperationReplayPolicy.DefaultReplayWindow, stored.Ttl);
        Assert.Null(await rig.ResultCache.TryGetAsync(normalKey, default));

        var warning = Assert.Single(rig.Logger.Entries, e => e.Level == LogLevel.Warning);
        Assert.Contains(QuestionId, warning.Message, StringComparison.Ordinal);
        Assert.Contains(advisory.EvidenceRelation!, warning.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(ExplanationText, warning.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("none")]
    [InlineData("ok")]
    [InlineData("unavailable")]
    public async Task Missing_clean_or_unavailable_advisory_caches_as_before(string kind)
    {
        await using var rig = await Rig.CreateAsync(Advisory(kind));

        var first = await rig.ExplainAsync();
        var second = await rig.ExplainAsync();

        Assert.False(first.Cached);
        Assert.True(second.Cached);
        Assert.Equal(first.WhyCorrect, second.WhyCorrect);
        Assert.Equal(1, rig.Gateway.Calls);
        Assert.Equal(1, rig.ResultCache.Stores);
        Assert.Equal(1, rig.ExplanationCache.Stores);
        Assert.DoesNotContain(rig.Logger.Entries, e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task Prompt_trap_names_are_the_canonical_distractor_categories()
    {
        await using var rig = await Rig.CreateAsync(null);

        await rig.ExplainAsync();

        var line = rig.Gateway.LastRequest!.UserInput!
            .Split('\n')
            .Single(l => l.Contains("\"trapName\"", StringComparison.Ordinal));
        const string marker = "one of: ";
        var start = line.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
        var names = line[start..line.IndexOf('"', start)].Split('|');

        Assert.Equal(Enum.GetNames<ReadingDistractorCategory>(), names);
        Assert.DoesNotContain("TooGeneral", names);
    }

    [Fact]
    public async Task Cache_keys_carry_the_v2_prompt_template()
    {
        await using var rig = await Rig.CreateAsync(null);

        await rig.ExplainAsync();

        Assert.Equal("reading.explanation.v2", rig.Gateway.LastRequest!.PromptTemplateId);
        Assert.Contains("reading.explanation.v2", rig.ResultCache.PromptVersions);
        Assert.All(rig.ResultCache.PromptVersions, v => Assert.StartsWith("reading.explanation.v2", v, StringComparison.Ordinal));
        Assert.StartsWith("reading.explanation.v2", rig.ExplanationCache.LastExtraEvidence, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Explanations_cached_under_the_v1_vocabulary_are_not_served()
    {
        await using var rig = await Rig.CreateAsync(null);
        var stale = JsonSerializer.Serialize(new ExplanationDto("stale", "stale", "TooGeneral", "stale", "en"));
        rig.ResultCache.Seed(
            rig.ResultCache.BuildCacheKey(
                AiFeatureCodes.ReadingExplanation, "reading", AttemptId, QuestionId, "B", "en",
                "reading.explanation.v1", RationaleId),
            stale);
        rig.ExplanationCache.Seed(
            rig.ExplanationCache.BuildCacheKey(
                "reading", QuestionId, null, "B", "en",
                "The authored rationale explains the answer.",
                "The source sentence supports the authored answer.",
                null),
            stale);

        var result = await rig.ExplainAsync();

        Assert.Equal(1, rig.Gateway.Calls);
        Assert.Equal(ExplanationText, result.WhyCorrect);
        Assert.False(result.Cached);
    }

    private static JevResponseAdvisory? Advisory(string kind) => kind switch
    {
        "none" => null,
        "ok" => new("ok", "jev-test", false, "supported", 0.95, 0.97, 0.01, null),
        "unavailable" => new("unavailable", null, true, null, null, null, null, "jev_unavailable"),
        "contradicted" => new("review_required", "jev-test", true, "contradicted", 0.91, 0.95, 0.02, null),
        "insufficient" => new("review_required", "jev-test", true, "insufficient_evidence", 0.88, 0.95, 0.02, null),
        "off_task" => new("review_required", "jev-test", true, "supported", 0.95, 0.10, 0.02, null),
        "unsafe" => new("review_required", "jev-test", true, "supported", 0.95, 0.95, 0.90, null),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    private sealed class Rig : IAsyncDisposable
    {
        private readonly LearnerDbContext db;

        private Rig(LearnerDbContext db, JevResponseAdvisory? advisory)
        {
            this.db = db;
            Gateway = new ScriptedGateway(advisory);
            ResultCache = new MemoryResultCache();
            ExplanationCache = new MemoryExplanationCache();
            Logger = new CapturingLogger();
            Service = new ReadingExplanationService(
                db, new RulebookLoader(), Gateway, Logger, ExplanationCache, ResultCache);
        }

        public ScriptedGateway Gateway { get; }
        public MemoryResultCache ResultCache { get; }
        public MemoryExplanationCache ExplanationCache { get; }
        public CapturingLogger Logger { get; }
        public ReadingExplanationService Service { get; }

        public static async Task<Rig> CreateAsync(JevResponseAdvisory? advisory)
        {
            var db = new LearnerDbContext(
                new DbContextOptionsBuilder<LearnerDbContext>()
                    .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                    .Options);
            await SeedAsync(db);
            return new Rig(db, advisory);
        }

        public Task<ExplanationDto> ExplainAsync()
            => Service.GetSubmittedAttemptExplanationAsync("learner-1", AttemptId, QuestionId, "en", default);

        public ValueTask DisposeAsync() => db.DisposeAsync();
    }

    private static async Task SeedAsync(LearnerDbContext db)
    {
        var now = DateTimeOffset.UtcNow;
        db.ContentPapers.Add(new ContentPaper
        {
            Id = "jev-reading-gate-paper-1",
            SubtestCode = "reading",
            Title = "Reading explanation Jev gate paper",
            Slug = "reading-explanation-jev-gate-paper",
            Status = ContentStatus.Published,
            PublishedRevisionId = "jev-reading-gate-revision-1",
        });
        db.ReadingParts.Add(new ReadingPart
        {
            Id = "jev-reading-gate-part-a",
            PaperId = "jev-reading-gate-paper-1",
            PartCode = ReadingPartCode.A,
            TimeLimitMinutes = 15,
            MaxRawScore = 1,
        });
        db.ReadingQuestions.Add(new ReadingQuestion
        {
            Id = QuestionId,
            ReadingPartId = "jev-reading-gate-part-a",
            QuestionType = ReadingQuestionType.MultipleChoice3,
            Stem = "Which option is supported?",
            OptionsJson = "[\"A\",\"B\",\"C\"]",
            CorrectAnswerJson = "\"A\"",
        });
        db.ReadingAttempts.Add(new ReadingAttempt
        {
            Id = AttemptId,
            UserId = "learner-1",
            PaperId = "jev-reading-gate-paper-1",
            StartedAt = now.AddMinutes(-10),
            LastActivityAt = now,
            SubmittedAt = now,
            Status = ReadingAttemptStatus.Submitted,
            MaxRawScore = 1,
            PaperRevisionId = "jev-reading-gate-revision-1",
        });
        db.ReadingAnswers.Add(new ReadingAnswer
        {
            Id = "jev-reading-gate-answer-1",
            ReadingAttemptId = AttemptId,
            ReadingQuestionId = QuestionId,
            UserAnswerJson = "\"B\"",
            AnsweredAt = now,
            CreatedAt = now,
            UpdatedAt = now,
        });
        db.AssessmentRationales.Add(new AssessmentRationale
        {
            Id = RationaleId,
            Assessment = "reading",
            QuestionRevisionId = QuestionId,
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

    private sealed class ScriptedGateway(JevResponseAdvisory? advisory) : IAiGatewayService
    {
        public int Calls { get; private set; }
        public AiGatewayRequest? LastRequest { get; private set; }

        public Task<AiGatewayResult> CompleteAsync(AiGatewayRequest request, CancellationToken ct = default)
        {
            Calls++;
            LastRequest = request;
            // CoordinatedAiGatewayService inside AiOperationReplayPolicy.DefaultReplayWindow: the first call
            // completed, so an identical request (same learner, same prompt, no ResourceId) is a duplicate
            // whose result cannot be reconstructed, never a second provider call.
            if (Calls > 1)
                return Task.FromException<AiGatewayResult>(
                    new AiOperationDuplicateResultUnavailableException("op-1", AiOperationState.Completed, null));
            return Task.FromResult(new AiGatewayResult
            {
                Completion = $$"""{"whyCorrect":"{{ExplanationText}}","whyWrong":"B is not supported.","trapName":"NotInText","avoidTip":"Stay with the passage."}""",
                JevAdvisory = advisory,
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
        public List<ResultStore> StoreCalls { get; } = [];
        public List<string?> PromptVersions { get; } = [];
        public int Stores => StoreCalls.Count;

        public void Seed(string key, string payload) => _items[key] = payload;

        public string BuildCacheKey(
            string featureCode,
            string module,
            string? attemptId,
            string? questionRevisionId,
            string? storedAnswerHash,
            string? language,
            string? promptVersion,
            string? rulebookVersion)
        {
            PromptVersions.Add(promptVersion);
            return string.Join('|', featureCode, module, attemptId, questionRevisionId, storedAnswerHash, language, promptVersion, rulebookVersion);
        }

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
            StoreCalls.Add(new ResultStore(cacheKey, promptVersion, ttl));
            _items[cacheKey] = payloadJson;
            return Task.CompletedTask;
        }
    }

    private sealed record ResultStore(string Key, string? PromptVersion, TimeSpan? Ttl);

    private sealed class MemoryExplanationCache : IAiExplanationCacheService
    {
        private readonly Dictionary<string, string> _items = new(StringComparer.Ordinal);
        public int Stores { get; private set; }
        public string? LastExtraEvidence { get; private set; }

        public void Seed(string key, string payload) => _items[key] = payload;

        public string BuildCacheKey(
            string module,
            string questionId,
            int? questionVersion,
            string normalizedSelectedAnswer,
            string language,
            string approvedRationale,
            string sourceSentence,
            string? extraEvidence)
        {
            LastExtraEvidence = extraEvidence;
            return string.Join('|', module, questionId, questionVersion, normalizedSelectedAnswer, language, approvedRationale, sourceSentence, extraEvidence);
        }

        public Task<string?> TryGetAsync(string cacheKey, CancellationToken ct)
            => Task.FromResult(_items.TryGetValue(cacheKey, out var payload) ? payload : null);

        public Task StoreAsync(string module, string questionId, string language, string cacheKey, string explanationJson, CancellationToken ct)
        {
            Stores++;
            _items[cacheKey] = explanationJson;
            return Task.CompletedTask;
        }
    }

    private sealed class CapturingLogger : ILogger<ReadingExplanationService>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));
    }
}
