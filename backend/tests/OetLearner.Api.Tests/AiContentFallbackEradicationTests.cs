using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Configuration;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Conversation;
using OetLearner.Api.Services.Pronunciation;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Tests;

public sealed class AiContentFallbackEradicationTests
{
    [Fact]
    public async Task ConversationOpening_ThrowsWhenGatewayReturnsNoReplyText()
    {
        var orchestrator = new ConversationAiOrchestrator(
            new StubAiGateway("{}"),
            new StubConversationOptionsProvider(),
            NullLogger<ConversationAiOrchestrator>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() => orchestrator.GenerateOpeningAsync(NewConversationContext(), CancellationToken.None));
    }

    [Fact]
    public async Task ConversationEvaluation_ThrowsWhenGatewayOmitsRequiredCriteria()
    {
        var orchestrator = new ConversationAiOrchestrator(
            new StubAiGateway("{\"criteria\":[]}"),
            new StubConversationOptionsProvider(),
            NullLogger<ConversationAiOrchestrator>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() => orchestrator.EvaluateAsync(NewConversationContext(), CancellationToken.None));
    }

    [Fact]
    public async Task PronunciationFeedback_ThrowsWhenGatewayResponseCannotBeParsed()
    {
        var service = new PronunciationFeedbackService(
            new StubAiGateway("not-json"),
            NullLogger<PronunciationFeedbackService>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.GenerateAsync(
            new PronunciationAssessment
            {
                Id = "pa-1",
                UserId = "learner-1",
                AccuracyScore = 62,
                FluencyScore = 63,
                CompletenessScore = 64,
                ProsodyScore = 65,
                OverallScore = 63,
                ProjectedSpeakingScaled = 320,
                ProjectedSpeakingGrade = "C+",
            },
            new PronunciationDrill
            {
                Id = "pd-1",
                Label = "th",
                TargetPhoneme = "th",
                Profession = "medicine",
                Focus = "phoneme",
                PrimaryRuleId = "P01.1",
            },
            "learner-1",
            "medicine",
            CancellationToken.None));
    }

    [Fact]
    public async Task VocabularyGloss_ThrowsUnavailableInsteadOfReturningPlaceholderDefinition()
    {
        await using var db = NewDb();
        var service = new VocabularyGlossService(
            db,
            new StubRulebookLoader(),
            new StubAiGateway("not-json"),
            NullLogger<VocabularyGlossService>.Instance);

        var ex = await Assert.ThrowsAsync<ApiException>(() => service.GlossAsync(
            "learner-1",
            new VocabularyGlossRequest("dyspnoea", null, null, "medicine"),
            CancellationToken.None));

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, ex.StatusCode);
        Assert.Equal("VOCABULARY_GLOSS_UNAVAILABLE", ex.ErrorCode);
        Assert.True(ex.Retryable);
    }

    [Fact]
    public async Task SpeakingTranscription_ThrowsWhenTranscriptMissingInsteadOfCreatingMockEvidence()
    {
        await using var db = NewDb();
        var now = DateTimeOffset.UtcNow;
        db.ContentItems.Add(new ContentItem
        {
            Id = "speaking-content-1",
            ContentType = "practice",
            SubtestCode = "speaking",
            ProfessionId = "medicine",
            Title = "Speaking roleplay",
            Difficulty = "medium",
            EstimatedDurationMinutes = 5,
            PublishedRevisionId = "rev-1",
            Status = ContentStatus.Published,
            CreatedAt = now,
            UpdatedAt = now,
        });
        db.Attempts.Add(new Attempt
        {
            Id = "speaking-attempt-1",
            UserId = "learner-1",
            ContentId = "speaking-content-1",
            SubtestCode = "speaking",
            Context = "practice",
            Mode = "practice",
            State = AttemptState.Evaluating,
            StartedAt = now,
            TranscriptJson = "[]",
        });
        await db.SaveChangesAsync();

        var pipeline = new SpeakingEvaluationPipeline(
            db,
            new StubAiGateway("{}"),
            new SpeakingRuleEngine(new StubRulebookLoader()),
            NullLogger<SpeakingEvaluationPipeline>.Instance);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            pipeline.CompleteTranscriptionAsync(new BackgroundJobItem
            {
                Id = "speaking-job-1",
                Type = JobType.SpeakingTranscription,
                State = AsyncState.Queued,
                AttemptId = "speaking-attempt-1",
                CreatedAt = now,
                AvailableAt = now,
                LastTransitionAt = now,
            }, CancellationToken.None));

        Assert.Contains("transcription evidence is missing", ex.Message);
        var persisted = await db.Attempts.SingleAsync(a => a.Id == "speaking-attempt-1");
        Assert.Equal("[]", persisted.TranscriptJson);
        Assert.DoesNotContain("mock-dev", persisted.AnalysisJson, StringComparison.OrdinalIgnoreCase);
    }

    private static ConversationAiContext NewConversationContext() => new(
        SessionId: "cs-1",
        UserId: "learner-1",
        AuthAccountId: null,
        TenantId: null,
        Profession: ExamProfession.Medicine,
        TaskTypeCode: "oet-roleplay",
        ScenarioJson: "{}",
        TranscriptJson: "[]",
        TurnIndex: 0,
        ElapsedSeconds: 0,
        RemainingSeconds: 360,
        CandidateCountry: null);

    private static LearnerDbContext NewDb() =>
        new(new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private sealed class StubConversationOptionsProvider : IConversationOptionsProvider
    {
        public Task<ConversationOptions> GetAsync(CancellationToken ct = default) => Task.FromResult(new ConversationOptions { Enabled = true });
        public void Invalidate() { }
    }

    private sealed class StubAiGateway(string completion) : IAiGatewayService
    {
        public Task<AiGatewayResult> CompleteAsync(AiGatewayRequest request, CancellationToken ct = default)
            => Task.FromResult(new AiGatewayResult
            {
                Completion = completion,
                RulebookVersion = "test-v1",
            });

        public AiGroundedPrompt BuildGroundedPrompt(AiGroundingContext context) => new()
        {
            SystemPrompt = "grounded",
            TaskInstruction = "respond strictly in JSON",
            Metadata = new AiGroundedPromptMetadata
            {
                RulebookKind = context.Kind,
                Profession = context.Profession,
                RulebookVersion = "test-v1",
                AppliedRuleIds = new[] { "C01.1", "P01.1", "V01.1" },
                AppliedRulesCount = 3,
            },
        };
    }

    private sealed class StubRulebookLoader : IRulebookLoader
    {
        private readonly OetRulebook _rulebook = new()
        {
            Version = "test-v1",
            Kind = RuleKind.Vocabulary,
            Profession = ExamProfession.Medicine,
            Rules =
            [
                new OetRule { Id = "V01.1", Title = "Clinical register", Body = "Use accurate clinical vocabulary." },
            ],
        };

        public OetRulebook Load(RuleKind kind, ExamProfession profession) => _rulebook;
        public IEnumerable<OetRulebook> All() => new[] { _rulebook };
        public OetRule? FindRule(RuleKind kind, ExamProfession profession, string ruleId)
            => _rulebook.Rules.FirstOrDefault(rule => string.Equals(rule.Id, ruleId, StringComparison.OrdinalIgnoreCase));
        public System.Text.Json.JsonElement GetAssessmentCriteria(RuleKind kind)
        {
            using var doc = System.Text.Json.JsonDocument.Parse("{}");
            return doc.RootElement.Clone();
        }
    }
}
