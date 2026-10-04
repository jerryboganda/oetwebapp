using System.Text.Json;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Rulebook;
using OetLearner.Api.Services.Rulebooks;

namespace OetLearner.Api.Tests.Rulebook;

public class AiGatewayAndPromptTests
{
    private readonly RulebookLoader _loader = new();

    private IAiGatewayService BuildGateway()
        => new AiGatewayService(_loader, new[] { (IAiModelProvider)new MockAiProvider() });

    [Fact]
    public void Prompt_Contains_Rulebook_Header_And_Critical_Rules()
    {
        var gateway = BuildGateway();
        var prompt = gateway.BuildGroundedPrompt(new AiGroundingContext
        {
            Kind = RuleKind.Writing,
            Profession = ExamProfession.Medicine,
            Task = AiTaskMode.Score,
            LetterType = "routine_referral",
            CandidateCountry = "UK",
        });
        Assert.Contains("OET AI — Rulebook-Grounded System Prompt", prompt.SystemPrompt);
        Assert.Contains("CRITICAL rules", prompt.SystemPrompt);
        Assert.Contains("OW-001", prompt.SystemPrompt);
        Assert.Equal(350, prompt.Metadata.ScoringPassMark);
        Assert.Equal("B", prompt.Metadata.ScoringGrade);
    }

    [Fact]
    public void Prompt_For_USA_Writing_Uses_300_Grade_CPlus()
    {
        var gateway = BuildGateway();
        var prompt = gateway.BuildGroundedPrompt(new AiGroundingContext
        {
            Kind = RuleKind.Writing,
            Profession = ExamProfession.Medicine,
            Task = AiTaskMode.Score,
            CandidateCountry = "USA",
            LetterType = "routine_referral",
        });
        Assert.Equal(300, prompt.Metadata.ScoringPassMark);
        Assert.Equal("C+", prompt.Metadata.ScoringGrade);
    }

    [Fact]
    public void Prompt_For_Speaking_Is_350_Regardless_Of_Country()
    {
        var gateway = BuildGateway();
        var prompt = gateway.BuildGroundedPrompt(new AiGroundingContext
        {
            Kind = RuleKind.Speaking,
            Profession = ExamProfession.Medicine,
            Task = AiTaskMode.Coach,
            CandidateCountry = "USA",
            CardType = "first_visit_routine",
        });
        Assert.Equal(350, prompt.Metadata.ScoringPassMark);
        Assert.Equal("B", prompt.Metadata.ScoringGrade);
        Assert.Contains("SPEAKING: Grade B at 350/500, universal", prompt.SystemPrompt);
    }

    [Fact]
    public void Prompt_For_BBN_Card_Includes_BBN_Rules()
    {
        var gateway = BuildGateway();
        var prompt = gateway.BuildGroundedPrompt(new AiGroundingContext
        {
            Kind = RuleKind.Speaking,
            Profession = ExamProfession.Medicine,
            Task = AiTaskMode.Coach,
            CardType = "breaking_bad_news",
        });
        Assert.Contains("RULE_41", prompt.SystemPrompt);
        Assert.Contains("RULE_44", prompt.SystemPrompt);
    }

    [Fact]
    public void Prompt_For_Routine_First_Visit_Excludes_BBN_Rules()
    {
        var gateway = BuildGateway();
        var prompt = gateway.BuildGroundedPrompt(new AiGroundingContext
        {
            Kind = RuleKind.Speaking,
            Profession = ExamProfession.Medicine,
            Task = AiTaskMode.Coach,
            CardType = "first_visit_routine",
        });
        Assert.DoesNotContain("RULE_44 (critical)", prompt.SystemPrompt);
    }

    [Fact]
    public async Task Gateway_Rejects_Empty_SystemPrompt()
    {
        var gateway = BuildGateway();
        var prompt = new AiGroundedPrompt { SystemPrompt = "", TaskInstruction = "do stuff" };
        await Assert.ThrowsAsync<PromptNotGroundedException>(() =>
            gateway.CompleteAsync(new AiGatewayRequest { Prompt = prompt }));
    }

    [Fact]
    public async Task Gateway_Rejects_Ungrounded_SystemPrompt()
    {
        var gateway = BuildGateway();
        var prompt = new AiGroundedPrompt
        {
            SystemPrompt = "You are a friendly chatbot, answer however you like.",
            TaskInstruction = "Hi",
        };
        await Assert.ThrowsAsync<PromptNotGroundedException>(() =>
            gateway.CompleteAsync(new AiGatewayRequest { Prompt = prompt }));
    }

    [Fact]
    public async Task Gateway_Accepts_Grounded_Prompt_Via_Builder()
    {
        var gateway = BuildGateway();
        var prompt = gateway.BuildGroundedPrompt(new AiGroundingContext
        {
            Kind = RuleKind.Writing,
            Profession = ExamProfession.Medicine,
            Task = AiTaskMode.Score,
            LetterType = "routine_referral",
        });
        var result = await gateway.CompleteAsync(new AiGatewayRequest { Prompt = prompt });
        Assert.False(string.IsNullOrWhiteSpace(result.Completion));
        Assert.Equal("2.5.0-cross-model-audit", result.RulebookVersion);
        Assert.NotEmpty(result.AppliedRuleIds);
    }

    [Fact]
    public async Task Gateway_Refuses_Writing_Prompt_Missing_LetterType()
    {
        var gateway = BuildGateway();
        var ex = Assert.Throws<PromptNotGroundedException>(() =>
            gateway.BuildGroundedPrompt(new AiGroundingContext
            {
                Kind = RuleKind.Writing,
                Profession = ExamProfession.Medicine,
                Task = AiTaskMode.Score,
                // LetterType intentionally omitted
            }));
        Assert.Contains("LetterType", ex.Message);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Gateway_Refuses_Writing_Prompt_With_Whitespace_LetterType()
    {
        var gateway = BuildGateway();
        var ex = Assert.Throws<PromptNotGroundedException>(() =>
            gateway.BuildGroundedPrompt(new AiGroundingContext
            {
                Kind = RuleKind.Writing,
                Profession = ExamProfession.Medicine,
                Task = AiTaskMode.Score,
                LetterType = "   ",
            }));
        Assert.Contains("LetterType", ex.Message);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Gateway_Refuses_Speaking_Prompt_Missing_CardType()
    {
        var gateway = BuildGateway();
        var ex = Assert.Throws<PromptNotGroundedException>(() =>
            gateway.BuildGroundedPrompt(new AiGroundingContext
            {
                Kind = RuleKind.Speaking,
                Profession = ExamProfession.Medicine,
                Task = AiTaskMode.Score,
                // CardType intentionally omitted
            }));
        Assert.Contains("CardType", ex.Message);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Gateway_Refuses_Speaking_Prompt_With_Whitespace_CardType()
    {
        var gateway = BuildGateway();
        var ex = Assert.Throws<PromptNotGroundedException>(() =>
            gateway.BuildGroundedPrompt(new AiGroundingContext
            {
                Kind = RuleKind.Speaking,
                Profession = ExamProfession.Medicine,
                Task = AiTaskMode.Score,
                CardType = "\t",
            }));
        Assert.Contains("CardType", ex.Message);
        await Task.CompletedTask;
    }

    // ── Speaking scoring prompt (owner spec 4 Oct 2026) ────────────────────────────────────────

    private string SpeakingScoreSystemPrompt(string cardType = "role_play")
        => BuildGateway().BuildGroundedPrompt(new AiGroundingContext
        {
            Kind = RuleKind.Speaking,
            Profession = ExamProfession.Medicine,
            Task = AiTaskMode.Score,
            CardType = cardType,
        }).SystemPrompt;

    [Fact]
    public void SpeakingScorePrompt_CarriesTheOfficialBandDescriptors_UnderTheGradersCriterionCodes()
    {
        var system = SpeakingScoreSystemPrompt();

        Assert.Contains("Official OET band descriptors", system);
        // Linguistic bands, straight from rulebooks/speaking/common/assessment-criteria.json.
        Assert.Contains("Minimal strain for the listener", system);
        Assert.Contains("Completely fluent speech at normal speed", system);
        // The JSON's "resources" dimension is the grader's `grammarExpression`; clusters A-E map to the five clinical codes.
        Assert.Contains("`grammarExpression` — Resources of Grammar and Expression", system);
        Assert.Contains("`relationshipBuilding` — Relationship building", system);
        Assert.Contains("`structure` — Providing structure", system);
        Assert.Contains("`informationGiving` — Information giving", system);
        Assert.Contains("A1: Initiating the interaction appropriately", system);
        Assert.Contains("3 = Adept use", system);
        Assert.Contains("0 = Ineffective use", system);
        // The old Writing-style spellings are gone.
        Assert.DoesNotContain("`providingStructure`", system);
        Assert.DoesNotContain("`grammar` —", system);
    }

    [Fact]
    public void SpeakingScorePrompt_TreatsRulesAsGuidance_NeverAsDeductions()
    {
        var system = SpeakingScoreSystemPrompt();

        Assert.DoesNotContain("auto-mark-deductions", system);
        Assert.Contains("Key rules (interpretation guidance — not deductions)", system);
        Assert.Contains("A rule is NEVER a separate deduction", system);
        Assert.Contains("Count each event against at most ONE criterion", system);
        Assert.Contains("Never lower two criteria for the same behaviour", system);
    }

    [Fact]
    public void SpeakingScorePrompt_DoesNotAssessMedicalKnowledge_AndIgnoresTheConnectionCheck()
    {
        var system = SpeakingScoreSystemPrompt();

        Assert.Contains("Medical or clinical knowledge accuracy is NOT assessed in OET Speaking", system);
        Assert.Contains("Information giving", system);
        Assert.Contains("is not part of the performance", system);
        Assert.Contains("can you hear me", system);
    }

    [Fact]
    public void SpeakingScorePrompt_NeverAsksForAScoreOrRuleIds_AndHasNoWritingReplyEnvelope()
    {
        var system = SpeakingScoreSystemPrompt();

        Assert.Contains("Reply with exactly the JSON object requested in the user message", system);
        Assert.Contains("Do not add a score, grade or pass/fail verdict of your own", system);
        Assert.Contains("Never write rule IDs", system);
        // The Writing-shaped envelope contradicted the Speaking schema.
        Assert.DoesNotContain("estimatedScaledScore", system);
        Assert.DoesNotContain("criteriaScores", system);
        Assert.DoesNotContain("Cite rule IDs explicitly in every feedback finding", system);
    }

    [Fact]
    public void SpeakingScorePrompt_BreakingBadNewsCardsGetTheirScopedRules_AsGuidance()
    {
        var generic = SpeakingScoreSystemPrompt("role_play");
        var bbn = SpeakingScoreSystemPrompt("breaking_bad_news");

        // RULE_41 is scoped to breaking_bad_news; the generic token never reached it.
        Assert.DoesNotContain("**RULE_41**", generic);
        Assert.Contains("**RULE_41**", bbn);
        Assert.Contains("interpretation guidance", bbn);
        Assert.DoesNotContain("auto-mark-deductions", bbn);
    }

    [Fact]
    public void SpeakingCoachPrompt_KeepsItsOwnReplyShape_ButRulesStillAreNotDeductions()
    {
        var prompt = BuildGateway().BuildGroundedPrompt(new AiGroundingContext
        {
            Kind = RuleKind.Speaking,
            Profession = ExamProfession.Medicine,
            Task = AiTaskMode.Coach,
            CardType = "first_visit_routine",
        });

        Assert.Contains("nextBestAction", prompt.SystemPrompt);
        Assert.DoesNotContain("auto-mark-deductions", prompt.SystemPrompt);
        // The scoring principles and descriptor block belong to the score task only.
        Assert.DoesNotContain("How to score (mandatory)", prompt.SystemPrompt);
    }

    [Fact]
    public void WritingPrompt_IsUntouched_ByTheSpeakingChanges()
    {
        var prompt = BuildGateway().BuildGroundedPrompt(new AiGroundingContext
        {
            Kind = RuleKind.Writing,
            Profession = ExamProfession.Medicine,
            Task = AiTaskMode.Score,
            LetterType = "routine_referral",
            CandidateCountry = "UK",
        });

        Assert.Contains("CRITICAL rules (violations are auto-mark-deductions; flag them first)", prompt.SystemPrompt);
        Assert.Contains("\"criteriaScores\"", prompt.SystemPrompt);
        Assert.DoesNotContain("How to score (mandatory)", prompt.SystemPrompt);
        Assert.DoesNotContain("Official OET band descriptors", prompt.SystemPrompt);
    }

    [Theory]
    [InlineData("Breaking Bad News", "[]", "breaking_bad_news")]
    [InlineData("Other Cards", "[\"Angry\",\"Breaking Bad News\"]", "breaking_bad_news")]
    [InlineData("Second Visit / Follow-up", "[]", "follow_up")]
    [InlineData("Already Known Patient", "[]", "already_known_patient")]
    [InlineData("First Visit", "[]", "role_play")]
    [InlineData("Examination Card", "[\"Reluctant\"]", "role_play")]
    [InlineData("Other Cards", "not json", "role_play")]
    [InlineData("Other Cards", "", "role_play")]
    public void SpeakingGrader_MapsACardToItsRulebookToken(string primaryCategory, string secondaryTagsJson, string expectedToken)
    {
        var card = new OetLearner.Api.Domain.RolePlayCard
        {
            PrimaryCategory = primaryCategory,
            SecondaryTagsJson = secondaryTagsJson,
        };

        Assert.Equal(expectedToken, OetLearner.Api.Services.Speaking.SpeakingAiAssessmentService.RulebookCardToken(card));
    }
}
