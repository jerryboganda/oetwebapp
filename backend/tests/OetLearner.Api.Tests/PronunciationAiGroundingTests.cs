using OetLearner.Api.Domain;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Tests;

/// <summary>
/// MISSION-CRITICAL contract tests for pronunciation AI grounding.
///
/// Every pronunciation AI call must:
///   1. Build its system prompt via <see cref="IAiGatewayService.BuildGroundedPrompt"/>.
///   2. Load the pronunciation rulebook for the selected profession.
///   3. Include the canonical OET Speaking 350-anchor pass context.
///   4. Emit the correct reply-format contract for the task mode.
///   5. Refuse ungrounded prompts with <see cref="PromptNotGroundedException"/>.
/// </summary>
public class PronunciationAiGroundingTests
{
    private readonly IAiGatewayService _gateway;

    public PronunciationAiGroundingTests()
    {
        var loader = new RulebookLoader();
        var providers = new IAiModelProvider[] { new MockAiProvider() };
        _gateway = new AiGatewayService(loader, providers);
    }

    [Theory]
    [InlineData(AiTaskMode.ScorePronunciationAttempt)]
    [InlineData(AiTaskMode.GeneratePronunciationDrill)]
    [InlineData(AiTaskMode.GeneratePronunciationFeedback)]
    public void BuildGroundedPrompt_For_Pronunciation_Embeds_Rulebook_Header_And_Task(AiTaskMode task)
    {
        var prompt = _gateway.BuildGroundedPrompt(new AiGroundingContext
        {
            Kind = RuleKind.Pronunciation,
            Profession = ExamProfession.Medicine,
            Task = task,
        });

        Assert.Contains("OET AI — Rulebook-Grounded System Prompt", prompt.SystemPrompt);
        Assert.Contains("PRONUNCIATION", prompt.SystemPrompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("MEDICINE", prompt.SystemPrompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(task.ToString(), prompt.SystemPrompt);
        Assert.Equal(RuleKind.Pronunciation, prompt.Metadata.RulebookKind);
        Assert.Equal(ExamProfession.Medicine, prompt.Metadata.Profession);
        Assert.False(string.IsNullOrWhiteSpace(prompt.Metadata.RulebookVersion));
    }

    [Fact]
    public void BuildGroundedPrompt_Includes_Score_Reply_Contract_For_Score_Task()
    {
        var prompt = _gateway.BuildGroundedPrompt(new AiGroundingContext
        {
            Kind = RuleKind.Pronunciation,
            Profession = ExamProfession.Nursing,
            Task = AiTaskMode.ScorePronunciationAttempt,
        });

        Assert.Contains("\"accuracyScore\"", prompt.SystemPrompt);
        Assert.Contains("\"fluencyScore\"", prompt.SystemPrompt);
        Assert.Contains("\"completenessScore\"", prompt.SystemPrompt);
        Assert.Contains("\"prosodyScore\"", prompt.SystemPrompt);
        Assert.Contains("\"overallScore\"", prompt.SystemPrompt);
        Assert.Contains("\"problematicPhonemes\"", prompt.SystemPrompt);
        Assert.Contains("\"projectedSpeakingBand\"", prompt.SystemPrompt);
    }

    [Fact]
    public void BuildGroundedPrompt_Includes_Drill_Reply_Contract_For_Generate_Task()
    {
        var prompt = _gateway.BuildGroundedPrompt(new AiGroundingContext
        {
            Kind = RuleKind.Pronunciation,
            Profession = ExamProfession.Medicine,
            Task = AiTaskMode.GeneratePronunciationDrill,
        });

        Assert.Contains("\"targetPhoneme\"", prompt.SystemPrompt);
        Assert.Contains("\"exampleWords\"", prompt.SystemPrompt);
        Assert.Contains("\"minimalPairs\"", prompt.SystemPrompt);
        Assert.Contains("\"sentences\"", prompt.SystemPrompt);
        Assert.Contains("\"tipsHtml\"", prompt.SystemPrompt);
        Assert.Contains("\"appliedRuleIds\"", prompt.SystemPrompt);
    }

    [Fact]
    public void BuildGroundedPrompt_Includes_Feedback_Reply_Contract_For_Feedback_Task()
    {
        var prompt = _gateway.BuildGroundedPrompt(new AiGroundingContext
        {
            Kind = RuleKind.Pronunciation,
            Profession = ExamProfession.Pharmacy,
            Task = AiTaskMode.GeneratePronunciationFeedback,
        });

        Assert.Contains("\"summary\"", prompt.SystemPrompt);
        Assert.Contains("\"strengths\"", prompt.SystemPrompt);
        Assert.Contains("\"improvements\"", prompt.SystemPrompt);
        Assert.Contains("\"nextDrillTargetPhoneme\"", prompt.SystemPrompt);
    }

    [Fact]
    public void BuildGroundedPrompt_Embeds_Canonical_Pronunciation_Scoring_Anchor()
    {
        var prompt = _gateway.BuildGroundedPrompt(new AiGroundingContext
        {
            Kind = RuleKind.Pronunciation,
            Profession = ExamProfession.Medicine,
            Task = AiTaskMode.ScorePronunciationAttempt,
        });

        Assert.Contains("PRONUNCIATION", prompt.SystemPrompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("350/500", prompt.SystemPrompt);
        Assert.Contains("Speaking", prompt.SystemPrompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildGroundedPrompt_Does_Not_Require_Country_For_Pronunciation()
    {
        var prompt = _gateway.BuildGroundedPrompt(new AiGroundingContext
        {
            Kind = RuleKind.Pronunciation,
            Profession = ExamProfession.Dentistry,
            Task = AiTaskMode.ScorePronunciationAttempt,
        });

        // Speaking is universal — pass mark is always 350.
        Assert.Equal(350, prompt.Metadata.ScoringPassMark);
        Assert.Equal("B", prompt.Metadata.ScoringGrade);
    }

    [Fact]
    public async Task CompleteAsync_Refuses_Null_Prompt()
    {
        await Assert.ThrowsAsync<PromptNotGroundedException>(() =>
            _gateway.CompleteAsync(new AiGatewayRequest
            {
                Prompt = null,
                UserInput = "anything",
                FeatureCode = AiFeatureCodes.PronunciationScore,
            }));
    }

    [Fact]
    public async Task CompleteAsync_Refuses_Prompt_Without_Grounding_Header()
    {
        await Assert.ThrowsAsync<PromptNotGroundedException>(() =>
            _gateway.CompleteAsync(new AiGatewayRequest
            {
                Prompt = new AiGroundedPrompt
                {
                    SystemPrompt = "This is a free-form system prompt without the rulebook header.",
                    TaskInstruction = "do it",
                },
                UserInput = "anything",
                FeatureCode = AiFeatureCodes.PronunciationScore,
            }));
    }

    [Fact]
    public async Task CompleteAsync_Succeeds_With_Grounded_Pronunciation_Prompt()
    {
        var prompt = _gateway.BuildGroundedPrompt(new AiGroundingContext
        {
            Kind = RuleKind.Pronunciation,
            Profession = ExamProfession.Medicine,
            Task = AiTaskMode.ScorePronunciationAttempt,
        });

        var result = await _gateway.CompleteAsync(new AiGatewayRequest
        {
            Prompt = prompt,
            UserInput = "stub input",
            FeatureCode = AiFeatureCodes.PronunciationScore,
            Model = "mock",
            Provider = "mock",
        });

        Assert.NotNull(result);
        Assert.Equal(RuleKind.Pronunciation, result.Metadata.RulebookKind);
    }

    [Fact]
    public void AiFeatureCodes_Include_Pronunciation_Codes()
    {
        Assert.Equal("pronunciation.score", AiFeatureCodes.PronunciationScore);
        Assert.Equal("pronunciation.feedback", AiFeatureCodes.PronunciationFeedback);
        Assert.Equal("pronunciation.tip", AiFeatureCodes.PronunciationTip);
        Assert.Equal("admin.pronunciation_draft", AiFeatureCodes.AdminPronunciationDraft);
    }
}
