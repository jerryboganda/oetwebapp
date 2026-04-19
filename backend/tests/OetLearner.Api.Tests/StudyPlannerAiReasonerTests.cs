using OetLearner.Api.Domain;
using OetLearner.Api.Services.Rulebook;
using OetLearner.Api.Services.StudyPlanner;

namespace OetLearner.Api.Tests;

public class StudyPlannerAiReasonerTests
{
    /// <summary>
    /// In-memory gateway that the reasoner can drive without hitting a real provider.
    /// Returns whatever <see cref="NextCompletion"/> is set to (mimicking the AI response
    /// shape the reasoner parses).
    /// </summary>
    private sealed class FakeGateway : IAiGatewayService
    {
        public string NextCompletion { get; set; } = "";
        public bool Refuse { get; set; }
        public AiGatewayRequest? LastRequest { get; private set; }

        public AiGroundedPrompt BuildGroundedPrompt(AiGroundingContext ctx) => new()
        {
            SystemPrompt = "# OET AI — Rulebook-Grounded System Prompt\n(seed)",
            TaskInstruction = "Produce JSON.",
            Metadata = new AiGroundedPromptMetadata { RulebookVersion = "1.0.0" },
        };

        public Task<AiGatewayResult> CompleteAsync(AiGatewayRequest request, CancellationToken ct = default)
        {
            LastRequest = request;
            if (Refuse) throw new PromptNotGroundedException("test refusal");
            return Task.FromResult(new AiGatewayResult
            {
                Completion = NextCompletion,
                RulebookVersion = "1.0.0",
                Metadata = new AiGroundedPromptMetadata { RulebookVersion = "1.0.0" },
            });
        }
    }

    private static IReadOnlyList<StudyPlanItem> SampleItems() => new[]
    {
        new StudyPlanItem { Id = "spi-1", StudyPlanId = "p", Title = "Writing Drill", SubtestCode = "writing", ItemType = "practice", DurationMinutes = 45, Rationale = "r", Section = "today", Status = StudyPlanItemStatus.NotStarted, DueDate = DateOnly.FromDateTime(DateTime.UtcNow) },
            new StudyPlanItem { Id = "spi-2", StudyPlanId = "p", Title = "Speaking Role Play", SubtestCode = "speaking", ItemType = "roleplay", DurationMinutes = 20, Rationale = "r", Section = "today", Status = StudyPlanItemStatus.NotStarted, DueDate = DateOnly.FromDateTime(DateTime.UtcNow) },
    };

    private static LearnerPlanContext SampleContext() =>
        new("u-1", "medicine", "oet", "UK", 8, 10, 350, 350, 350, 350, new[] { "writing" });

    [Fact]
    public async Task Returns_addenda_when_ai_emits_valid_json()
    {
        var gw = new FakeGateway
        {
            NextCompletion = """
{ "addenda": { "spi-1": "Because your Writing is the weakest subtest, prioritise this today.", "spi-2": "Builds the SBAR fluency you'll need for a UK test centre." } }
""",
        };
        var reasoner = new StudyPlannerAiReasoner(gw);
        var addenda = await reasoner.ProduceAddendaAsync(SampleContext(), SampleItems(), default);

        Assert.Equal(2, addenda.Count);
        Assert.StartsWith("Because your Writing", addenda["spi-1"]);
        Assert.Equal(AiFeatureCodes.StudyPlanReasoning, gw.LastRequest!.FeatureCode);
    }

    [Fact]
    public async Task Returns_addenda_when_ai_prefixes_prose()
    {
        // Some models wrap the JSON in narration. The parser must still pull
        // the object out cleanly.
        var gw = new FakeGateway
        {
            NextCompletion = "Here's the plan:\n{ \"addenda\": { \"spi-1\": \"Stick with it.\" } }\nEnd of response.",
        };
        var reasoner = new StudyPlannerAiReasoner(gw);
        var addenda = await reasoner.ProduceAddendaAsync(SampleContext(), SampleItems(), default);

        Assert.Single(addenda);
        Assert.Equal("Stick with it.", addenda["spi-1"]);
    }

    [Fact]
    public async Task Returns_empty_when_ai_emits_invalid_json()
    {
        var gw = new FakeGateway { NextCompletion = "I cannot help with this." };
        var reasoner = new StudyPlannerAiReasoner(gw);
        var addenda = await reasoner.ProduceAddendaAsync(SampleContext(), SampleItems(), default);
        Assert.Empty(addenda);
    }

    [Fact]
    public async Task Returns_empty_when_gateway_refuses_ungrounded_prompt()
    {
        var gw = new FakeGateway { Refuse = true };
        var reasoner = new StudyPlannerAiReasoner(gw);
        var addenda = await reasoner.ProduceAddendaAsync(SampleContext(), SampleItems(), default);
        // The reasoner is fail-soft: the generator's deterministic output remains
        // intact. It must NEVER propagate PromptNotGroundedException.
        Assert.Empty(addenda);
    }

    [Fact]
    public async Task Truncates_excessively_long_addenda()
    {
        var longText = new string('x', 1000);
        var gw = new FakeGateway
        {
            NextCompletion = "{ \"addenda\": { \"spi-1\": \"" + longText + "\" } }",
        };
        var reasoner = new StudyPlannerAiReasoner(gw);
        var addenda = await reasoner.ProduceAddendaAsync(SampleContext(), SampleItems(), default);
        Assert.Single(addenda);
        Assert.True(addenda["spi-1"].Length <= 300, $"expected truncation, got length {addenda["spi-1"].Length}");
        Assert.EndsWith("…", addenda["spi-1"]);
    }

    [Fact]
    public async Task Ignores_non_string_addenda_values()
    {
        var gw = new FakeGateway
        {
            NextCompletion = "{ \"addenda\": { \"spi-1\": \"ok\", \"spi-2\": 42 } }",
        };
        var reasoner = new StudyPlannerAiReasoner(gw);
        var addenda = await reasoner.ProduceAddendaAsync(SampleContext(), SampleItems(), default);
        Assert.Single(addenda);
        Assert.Equal("ok", addenda["spi-1"]);
    }

    [Fact]
    public async Task No_items_returns_empty_without_calling_gateway()
    {
        var gw = new FakeGateway { NextCompletion = "{ \"addenda\": { } }" };
        var reasoner = new StudyPlannerAiReasoner(gw);
        var addenda = await reasoner.ProduceAddendaAsync(SampleContext(), Array.Empty<StudyPlanItem>(), default);
        Assert.Empty(addenda);
        Assert.Null(gw.LastRequest);
    }

    [Fact]
    public async Task Stamps_feature_code_for_usage_accounting()
    {
        var gw = new FakeGateway { NextCompletion = "{ \"addenda\": { } }" };
        var reasoner = new StudyPlannerAiReasoner(gw);
        await reasoner.ProduceAddendaAsync(SampleContext(), SampleItems(), default);
        Assert.NotNull(gw.LastRequest);
        Assert.Equal(AiFeatureCodes.StudyPlanReasoning, gw.LastRequest!.FeatureCode);
        Assert.Equal("u-1", gw.LastRequest.UserId);
    }
}
