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
}
