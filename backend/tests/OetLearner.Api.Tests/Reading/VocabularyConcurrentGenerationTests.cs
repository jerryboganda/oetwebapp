using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Reading;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Tests.Reading;

public sealed class VocabularyConcurrentGenerationTests
{
    [Fact]
    public async Task Concurrent_same_word_requests_share_one_generation()
    {
        await using var db = NewDb();
        var gateway = new SlowCountingGateway();
        var service = new ReadingVocabularyService(
            db,
            new StubRulebookLoader(),
            gateway,
            NullLogger<ReadingVocabularyService>.Instance);

        var first = service.EnsureWordExistsAsync("Anaemia", default);
        var second = service.EnsureWordExistsAsync("anaemia", default);
        var results = await Task.WhenAll(first, second);

        Assert.Equal(1, gateway.Calls);
        Assert.Equal(results[0].Id, results[1].Id);
        Assert.Equal(1, await db.VocabularyWords.CountAsync());
        Assert.Equal("anaemia", results[0].NormalizedWord);
        Assert.DoesNotContain("(AI unavailable).", results[0].DefinitionEn, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Gateway_failure_does_not_persist_a_stub_card()
    {
        await using var db = NewDb();
        var service = new ReadingVocabularyService(
            db,
            new StubRulebookLoader(),
            new ThrowingGateway(),
            NullLogger<ReadingVocabularyService>.Instance);

        await Assert.ThrowsAsync<VocabularyGenerationUnavailableException>(() =>
            service.EnsureWordExistsAsync("tachycardia", default));

        Assert.Equal(0, await db.VocabularyWords.CountAsync());
    }

    private static LearnerDbContext NewDb() => new(
        new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private sealed class StubRulebookLoader : IRulebookLoader
    {
        public OetRulebook Load(RuleKind kind, ExamProfession profession)
            => new() { Version = "test", Kind = kind };

        public IEnumerable<OetRulebook> All() => Array.Empty<OetRulebook>();

        public OetRule? FindRule(RuleKind kind, ExamProfession profession, string ruleId) => null;

        public JsonElement GetAssessmentCriteria(RuleKind kind) => default;
    }

    private sealed class SlowCountingGateway : IAiGatewayService
    {
        public int Calls { get; private set; }

        public async Task<AiGatewayResult> CompleteAsync(AiGatewayRequest request, CancellationToken ct = default)
        {
            Calls++;
            await Task.Delay(80, ct);
            return new AiGatewayResult
            {
                Completion = """{"partOfSpeech":"noun","definitionEn":"A reduced red-cell mass.","definitionAr":"","pronunciationIpa":"/əˈniːmiə/","exampleEn":"The patient presented with anaemia.","exampleAr":"","healthcareContext":"haematology","professionRelevance":["Medicine"],"difficulty":4}""",
            };
        }

        public AiGroundedPrompt BuildGroundedPrompt(AiGroundingContext context)
            => new()
            {
                SystemPrompt = "grounded test prompt",
                TaskInstruction = "gloss",
                Metadata = new AiGroundedPromptMetadata
                {
                    RulebookKind = context.Kind,
                    Profession = context.Profession,
                    RulebookVersion = "test",
                },
            };
    }

    private sealed class ThrowingGateway : IAiGatewayService
    {
        public Task<AiGatewayResult> CompleteAsync(AiGatewayRequest request, CancellationToken ct = default)
            => throw new InvalidOperationException("AI gateway failure for vocabulary unavailable test.");

        public AiGroundedPrompt BuildGroundedPrompt(AiGroundingContext context)
            => new()
            {
                SystemPrompt = "grounded test prompt",
                TaskInstruction = "gloss",
                Metadata = new AiGroundedPromptMetadata
                {
                    RulebookKind = context.Kind,
                    Profession = context.Profession,
                    RulebookVersion = "test",
                },
            };
    }
}
