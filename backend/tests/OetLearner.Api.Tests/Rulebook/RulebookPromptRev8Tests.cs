using System.Text.Json;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Tests.Rulebook;

/// <summary>
/// Writing Rule Enforcement Addendum Rev8 §7/§11 — the grounded prompt gives
/// the Model Answer generator, the semantic validator and the candidate grader
/// the SAME applicable rules (LT-* catalogue codes resolve letter-type-scoped
/// rules), with Model Answer strictness for GenerateContent and candidate
/// protections for every grading mode. Speaking prompts are untouched.
/// </summary>
public class RulebookPromptRev8Tests
{
    private readonly RulebookLoader _loader = new();

    private AiGroundedPrompt Build(RuleKind kind, ExamProfession profession, AiTaskMode task, string? letterType = null, string? cardType = null)
        => new AiGatewayService(_loader, new[] { (IAiModelProvider)new MockAiProvider() })
            .BuildGroundedPrompt(new AiGroundingContext
            {
                Kind = kind,
                Profession = profession,
                Task = task,
                LetterType = letterType,
                CardType = cardType,
                CandidateCountry = "UK",
            });

    [Fact]
    public void Writing_LtUr_Catalogue_Code_Selects_Urgent_Scoped_Rules()
    {
        var prompt = Build(RuleKind.Writing, ExamProfession.Dietetics, AiTaskMode.Score, letterType: "LT-UR");
        var legacy = Build(RuleKind.Writing, ExamProfession.Dietetics, AiTaskMode.Score, letterType: "urgent_referral");

        // R09.2 "Urgent closure: 'at your earliest convenience'" is scoped to urgent_referral.
        Assert.Contains("R09.2", prompt.Metadata.AppliedRuleIds);
        Assert.Contains("**R09.2**", prompt.SystemPrompt);
        // Scoping still filters: the discharge-only identifier rule stays out.
        Assert.DoesNotContain("R01.6", prompt.Metadata.AppliedRuleIds);
        Assert.Equal(legacy.Metadata.AppliedRuleIds, prompt.Metadata.AppliedRuleIds);
        Assert.StartsWith("Task: analyse the candidate's OET Writing letter (urgent referral)", prompt.TaskInstruction);
    }

    [Theory]
    [InlineData("LT-TR", "T-LEGACY,T-PACK")]
    [InlineData("transfer", "T-LEGACY,T-PACK")]
    [InlineData("LT-OT", "O-LEGACY,O-PACK")]
    [InlineData("LT-UR", "U-ONLY")]
    public void Writing_Rule_Scope_Matches_Legacy_And_Pack_Tokens(string letterType, string expectedIds)
    {
        static OetRule Scoped(string id, string token) => new()
        {
            Id = id,
            Title = id,
            Body = id,
            Severity = RuleSeverity.Major,
            AppliesTo = JsonSerializer.SerializeToElement(new[] { token }),
        };
        var book = new OetRulebook
        {
            Version = "test",
            Kind = RuleKind.Writing,
            Profession = ExamProfession.Medicine,
            Rules =
            [
                Scoped("T-LEGACY", "transfer_letter"),
                Scoped("T-PACK", "transfer"),
                Scoped("U-ONLY", "urgent_referral"),
                Scoped("O-LEGACY", "other_letters"),
                Scoped("O-PACK", "other"),
            ],
        };

        var prompt = new RulebookPromptBuilder(new SingleBookLoader(book)).Build(new AiGroundingContext
        {
            Kind = RuleKind.Writing,
            Task = AiTaskMode.Score,
            LetterType = letterType,
        });

        Assert.Equal(expectedIds.Split(','), prompt.Metadata.AppliedRuleIds);
    }

    [Theory]
    [InlineData(AiTaskMode.Score)]
    [InlineData(AiTaskMode.Coach)]
    [InlineData(AiTaskMode.Correct)]
    [InlineData(AiTaskMode.GenerateFeedback)]
    public void Writing_Grading_Prompts_Carry_Candidate_Rules_With_Zero_Template_Weight(AiTaskMode task)
    {
        var prompt = Build(RuleKind.Writing, ExamProfession.Medicine, task, letterType: "LT-RR");

        Assert.Contains(WritingRev8HouseStyle.CandidateGradingRules, prompt.SystemPrompt, StringComparison.Ordinal);
        Assert.Contains("contributes ZERO to the candidate's score", prompt.SystemPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain(WritingRev8HouseStyle.ModelAnswerCanonicalRules, prompt.SystemPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain("Reply with exactly the JSON object requested in the user message", prompt.SystemPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Writing_Score_Reply_Contract_Is_Unchanged()
    {
        var prompt = Build(RuleKind.Writing, ExamProfession.Medicine, AiTaskMode.Score, letterType: "LT-RR");

        Assert.Contains(
            "  \"criteriaScores\": { \"purpose\": 0, \"content\": 0, \"conciseness_clarity\": 0, \"genre_style\": 0, \"organisation_layout\": 0, \"language\": 0 },",
            prompt.SystemPrompt, StringComparison.Ordinal);
        Assert.Contains("  \"advisory\": \"AI-generated — pending tutor review\"", prompt.SystemPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Writing_GenerateContent_Prompt_Carries_Model_Answer_House_Style_And_Defers_To_User_Json()
    {
        var prompt = Build(RuleKind.Writing, ExamProfession.Medicine, AiTaskMode.GenerateContent, letterType: "LT-UR");

        Assert.Contains(WritingRev8HouseStyle.ModelAnswerCanonicalRules, prompt.SystemPrompt, StringComparison.Ordinal);
        Assert.Contains("I am writing to", prompt.SystemPrompt, StringComparison.Ordinal);
        Assert.Contains("at your earliest convenience", prompt.SystemPrompt, StringComparison.Ordinal);
        Assert.Contains("do not hesitate to contact me", prompt.SystemPrompt, StringComparison.Ordinal);
        Assert.Contains("Reply with exactly the JSON object requested in the user message (no extra prose).", prompt.SystemPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain("selfCheckNotes", prompt.SystemPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain(WritingRev8HouseStyle.CandidateGradingRules, prompt.SystemPrompt, StringComparison.Ordinal);
        Assert.StartsWith(
            "Task: produce or validate an OET Writing Model Answer (urgent referral) strictly from the provided case notes and task, under the active rulebook and the owner house style.",
            prompt.TaskInstruction);
    }

    [Fact]
    public void Model_Answer_Generator_And_Validator_Get_Identical_Prompts()
    {
        // Generator passes the scenario's LT-* code; the semantic validator
        // passes the legacy token (WritingModelAnswerSemanticValidator).
        var generator = Build(RuleKind.Writing, ExamProfession.Dietetics, AiTaskMode.GenerateContent, letterType: "LT-UR");
        var validator = Build(RuleKind.Writing, ExamProfession.Dietetics, AiTaskMode.GenerateContent, letterType: "urgent_referral");

        Assert.Equal(generator.SystemPrompt, validator.SystemPrompt);
        Assert.Equal(generator.TaskInstruction, validator.TaskInstruction);
        Assert.Equal(generator.Metadata.AppliedRuleIds, validator.Metadata.AppliedRuleIds);
    }

    [Fact]
    public void Writing_Guardrail8_Uses_Professional_Designation_Not_Doctor()
    {
        var prompt = Build(RuleKind.Writing, ExamProfession.Pharmacy, AiTaskMode.Score, letterType: "LT-RR");

        Assert.DoesNotContain("→ Doctor", prompt.SystemPrompt, StringComparison.Ordinal);
        Assert.Contains("Yours sincerely/faithfully → professional designation only", prompt.SystemPrompt, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(AiTaskMode.Coach)]
    [InlineData(AiTaskMode.GenerateContent)]
    public void Speaking_Prompt_Does_Not_Carry_Writing_Rev8_Text(AiTaskMode task)
    {
        var prompt = Build(RuleKind.Speaking, ExamProfession.Medicine, task, cardType: "first_visit_routine");

        Assert.DoesNotContain("OWNER WRITING RULES", prompt.SystemPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain("Reply with exactly the JSON object requested in the user message", prompt.SystemPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain("professional designation only", prompt.SystemPrompt, StringComparison.Ordinal);
        Assert.Contains("8. For speaking:", prompt.SystemPrompt, StringComparison.Ordinal);
        Assert.StartsWith("Task: analyse the candidate's OET Speaking transcript (first_visit_routine)", prompt.TaskInstruction);
        if (task == AiTaskMode.GenerateContent)
        {
            Assert.Contains("{ \"content\": \"...\", \"appliedRuleIds\": [\"OW-001\"], \"selfCheckNotes\": \"...\" }", prompt.SystemPrompt, StringComparison.Ordinal);
        }
    }

    private sealed class SingleBookLoader(OetRulebook book) : IRulebookLoader
    {
        public OetRulebook Load(RuleKind kind, ExamProfession profession) => book;
        public IEnumerable<OetRulebook> All() => [book];
        public OetRule? FindRule(RuleKind kind, ExamProfession profession, string ruleId) => null;
        public JsonElement GetAssessmentCriteria(RuleKind kind) => default;
    }
}
