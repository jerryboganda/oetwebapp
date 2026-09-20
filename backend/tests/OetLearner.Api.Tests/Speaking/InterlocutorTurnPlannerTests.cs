using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Configuration;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Conversation;
using OetLearner.Api.Services.Rulebook;
using OetLearner.Api.Services.Speaking;

namespace OetLearner.Api.Tests.Speaking;

public sealed class InterlocutorTurnPlannerTests
{
    [Fact]
    public async Task Planner_never_sends_a_withheld_hidden_fact_to_the_model()
    {
        const string sentinel = "SENTINEL NEVER DISCLOSE WARFARIN STOPPED";
        var gateway = new CapturingGateway(
            "{\"FactSelections\":[{\"FactId\":\"opening.response\",\"StatementVariantId\":\"default\"}],\"NonFactualResponseKind\":null}");
        var planner = CreatePlanner(gateway);
        var snapshot = Snapshot($$"""
            {
              "openingResponse": "I am worried about what is happening.",
              "patientBackground": "The patient has had chest discomfort for two days.",
              "hiddenInformation": "{{sentinel}}."
            }
            """);

        var reply = await planner.PlanAsync(Request(snapshot, "Hello, my name is Sam.", turnIndex: 1), default);

        Assert.Equal("I am worried about what is happening.", reply.Text);
        Assert.NotNull(gateway.GroundingContext);
        Assert.Equal(AiTaskMode.PlanConversationReply, gateway.GroundingContext!.Task);
        Assert.DoesNotContain(sentinel, gateway.GroundingContext.ConversationScenarioJson!, StringComparison.Ordinal);
        Assert.DoesNotContain("hidden.1", gateway.GroundingContext.ConversationScenarioJson!, StringComparison.Ordinal);
        Assert.DoesNotContain(sentinel, gateway.LastRequest!.UserInput!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Hidden_fact_enters_model_context_only_after_server_direct_relevance_condition_is_satisfied()
    {
        const string hidden = "I stopped warfarin last week.";
        var gateway = new CapturingGateway(
            "{\"FactSelections\":[{\"FactId\":\"hidden.1\",\"StatementVariantId\":\"default\"}],\"NonFactualResponseKind\":null}");
        var planner = CreatePlanner(gateway);
        var snapshot = Snapshot($$"""
            {
              "hiddenInformation": "{{hidden}}"
            }
            """);

        var reply = await planner.PlanAsync(
            Request(snapshot, "Did you stop warfarin last week?", turnIndex: 2), default);

        Assert.Equal(hidden, reply.Text);
        Assert.Contains("hidden.1", gateway.GroundingContext!.ConversationScenarioJson!, StringComparison.Ordinal);
        Assert.Contains(hidden, gateway.GroundingContext.ConversationScenarioJson!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Hidden_fact_does_not_unlock_from_one_generic_overlap_token()
    {
        const string hidden = "My father is on several medicines and had a stroke last year.";
        var gateway = new CapturingGateway(
            "{\"FactSelections\":[{\"FactId\":\"hidden.1\",\"StatementVariantId\":\"default\"}],\"NonFactualResponseKind\":null}");
        var planner = CreatePlanner(gateway);
        var snapshot = Snapshot($$"""
            {
              "hiddenInformation": "{{hidden}}"
            }
            """);

        var reply = await planner.PlanAsync(
            Request(snapshot, "What happened with your father?", turnIndex: 2), default);

        Assert.Equal("Could you explain that a little more?", reply.Text);
        Assert.DoesNotContain(hidden, gateway.GroundingContext!.ConversationScenarioJson!, StringComparison.Ordinal);
        Assert.DoesNotContain("hidden.1", gateway.GroundingContext.ConversationScenarioJson!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ineligible_fact_selection_fails_closed_to_server_authored_clarification()
    {
        const string hidden = "I stopped warfarin last week.";
        var gateway = new CapturingGateway(
            "{\"FactSelections\":[{\"FactId\":\"hidden.1\",\"StatementVariantId\":\"default\"}],\"NonFactualResponseKind\":null}");
        var planner = CreatePlanner(gateway);
        var snapshot = Snapshot($$"""
            {
              "hiddenInformation": "{{hidden}}"
            }
            """);

        var reply = await planner.PlanAsync(Request(snapshot, "Hello there.", turnIndex: 2), default);

        Assert.Equal("Could you explain that a little more?", reply.Text);
        Assert.DoesNotContain(hidden, gateway.GroundingContext!.ConversationScenarioJson!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Provider_cannot_inject_free_form_spoken_text()
    {
        const string injected = "Take this medication immediately.";
        var gateway = new CapturingGateway(
            $$"""{"FactSelections":[],"NonFactualResponseKind":"Acknowledgement","Text":"{{injected}}"}""");
        var planner = CreatePlanner(gateway);

        var reply = await planner.PlanAsync(Request(Snapshot("{}"), "Okay.", turnIndex: 2), default);

        Assert.Equal("Could you explain that a little more?", reply.Text);
        Assert.DoesNotContain(injected, reply.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Carried_fact_is_absent_until_follow_up_is_server_activated()
    {
        const string carried = "I use an inhaler every morning.";
        var gateway = new CapturingGateway(
            "{\"FactSelections\":[],\"NonFactualResponseKind\":\"Acknowledgement\"}");
        var planner = CreatePlanner(gateway);
        var snapshot = Snapshot("{}");
        snapshot.ApprovedCarryFactKeysJson = "[\"medication\"]";
        snapshot.CarriedFactsJson = $$"""{"medication":"{{carried}}"}""";

        _ = await planner.PlanAsync(Request(snapshot, "Tell me more.", turnIndex: 2), default);
        Assert.DoesNotContain("carry.medication", gateway.GroundingContext!.ConversationScenarioJson!, StringComparison.Ordinal);
        Assert.DoesNotContain(carried, gateway.GroundingContext.ConversationScenarioJson!, StringComparison.Ordinal);

        snapshot.FollowUpActivated = true;
        _ = await planner.PlanAsync(Request(snapshot, "Tell me more.", turnIndex: 2), default);
        Assert.Contains("carry.medication", gateway.GroundingContext!.ConversationScenarioJson!, StringComparison.Ordinal);
        Assert.Contains(carried, gateway.GroundingContext.ConversationScenarioJson!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Planner_sends_learner_transcript_without_raw_persona_json()
    {
        const string sentinel = "RAW PERSONA SENTINEL";
        var gateway = new CapturingGateway(
            "{\"FactSelections\":[],\"NonFactualResponseKind\":\"NeutralPause\"}");
        var planner = CreatePlanner(gateway);
        var snapshot = Snapshot($$"""{"hiddenInformation":"{{sentinel}}"}""");
        const string transcript = "[{\"role\":\"learner\",\"text\":\"Good morning\",\"interrupted\":false}]";

        _ = await planner.PlanAsync(Request(snapshot, "Good morning", 2, transcript), default);

        Assert.Equal(transcript, gateway.GroundingContext!.ConversationTranscriptJson);
        Assert.DoesNotContain(sentinel, gateway.GroundingContext.ConversationScenarioJson!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Planner_never_sends_raw_scenario_metadata_to_the_model()
    {
        const string sentinel = "SECRET COLORECTAL CANCER DIAGNOSIS";
        var gateway = new CapturingGateway(
            "{\"FactSelections\":[],\"NonFactualResponseKind\":\"NeutralPause\"}");
        var planner = CreatePlanner(gateway);
        var snapshot = Snapshot("{}");
        snapshot.InterlocutorRole = $"Patient - {sentinel}";
        snapshot.Setting = $"Oncology clinic - {sentinel}";
        snapshot.ScenarioTitle = $"Sharing a new diagnosis - {sentinel}";

        _ = await planner.PlanAsync(Request(snapshot, "Good morning", turnIndex: 2), default);

        Assert.NotNull(gateway.GroundingContext);
        var scenarioJson = gateway.GroundingContext!.ConversationScenarioJson!;
        Assert.DoesNotContain(sentinel, scenarioJson, StringComparison.Ordinal);
        Assert.DoesNotContain("authoredRole", scenarioJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("scenarioTitle", scenarioJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("setting", scenarioJson, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"roleClass\":\"patient\"", scenarioJson, StringComparison.Ordinal);
    }

    private static InterlocutorTurnPlanner CreatePlanner(CapturingGateway gateway)
        => new(gateway, new StubConversationOptionsProvider(), NullLogger<InterlocutorTurnPlanner>.Instance);

    private static InterlocutorTurnPlanRequest Request(
        SpeakingSimulationV11PersonaRuntimeSnapshot snapshot,
        string learnerText,
        int turnIndex,
        string transcriptJson = "[]")
        => new(
            snapshot,
            ExamProfession.Medicine,
            learnerText,
            transcriptJson,
            turnIndex,
            ElapsedSeconds: 15,
            RemainingSeconds: 285,
            SessionId: "session-1",
            UserId: "user-1");

    private static SpeakingSimulationV11PersonaRuntimeSnapshot Snapshot(string personaJson) => new()
    {
        Id = "persona-1",
        SpeakingSessionId = "session-1",
        RolePlayCardId = "card-1",
        CardSlot = "A",
        ProfessionId = "medicine",
        InterlocutorRole = "Patient",
        PersonaRole = "patient",
        Setting = "General practice clinic",
        ScenarioTitle = "Test role play",
        PersonaJson = personaJson,
    };

    private sealed class StubConversationOptionsProvider : IConversationOptionsProvider
    {
        private readonly ConversationOptions options = new()
        {
            ReplyModel = "test-model",
            ReplyTemperature = 0.2,
        };

        public ConversationOptions Current => options;

        public Task<ConversationOptions> GetAsync(CancellationToken ct = default)
            => Task.FromResult(options);

        public void Invalidate()
        {
        }
    }

    private sealed class CapturingGateway(string completion) : IAiGatewayService
    {
        public AiGroundingContext? GroundingContext { get; private set; }
        public AiGatewayRequest? LastRequest { get; private set; }

        public AiGroundedPrompt BuildGroundedPrompt(AiGroundingContext context)
        {
            GroundingContext = context;
            return new AiGroundedPrompt
            {
                SystemPrompt = "OET AI — Rulebook-Grounded System Prompt\n(test fixture)",
                TaskInstruction = "Return the speaking plan as JSON.",
                Metadata = new AiGroundedPromptMetadata
                {
                    RulebookVersion = "test-1.0.0",
                    RulebookKind = context.Kind,
                    Profession = context.Profession,
                    AppliedRulesCount = 1,
                    AppliedRuleIds = ["CONVERSATION_TEST"],
                },
            };
        }

        public Task<AiGatewayResult> CompleteAsync(AiGatewayRequest request, CancellationToken ct = default)
        {
            LastRequest = request;
            return Task.FromResult(new AiGatewayResult
            {
                Completion = completion,
                Metadata = request.Prompt!.Metadata,
                RulebookVersion = request.Prompt.Metadata.RulebookVersion,
                AppliedRuleIds = request.Prompt.Metadata.AppliedRuleIds,
                ResolvedProvider = "test-provider",
                ResolvedModel = request.Model,
            });
        }
    }
}
