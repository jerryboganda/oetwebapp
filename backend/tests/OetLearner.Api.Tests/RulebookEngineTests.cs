using System.Text.Json;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Tests;

public class RulebookLoaderTests
{
    private readonly RulebookLoader _loader = new();

    [Fact]
    public void Loads_Writing_Medicine_Rulebook()
    {
        var book = _loader.Load(RuleKind.Writing, ExamProfession.Medicine);
        Assert.Equal(RuleKind.Writing, book.Kind);
        Assert.Equal(ExamProfession.Medicine, book.Profession);
        Assert.Equal("1.0.0", book.Version);
        Assert.Equal(16, book.Sections.Count);
        Assert.True(book.Rules.Count >= 90);
    }

    [Fact]
    public void Loads_Speaking_Medicine_Rulebook()
    {
        var book = _loader.Load(RuleKind.Speaking, ExamProfession.Medicine);
        Assert.Equal(RuleKind.Speaking, book.Kind);
        Assert.Equal(ExamProfession.Medicine, book.Profession);
        Assert.Equal(7, book.Sections.Count);
        Assert.Equal(55, book.Rules.Count);
    }

    [Fact]
    public void Throws_For_Unregistered_Profession()
    {
        Assert.Throws<RulebookNotFoundException>(() =>
            _loader.Load(RuleKind.Writing, ExamProfession.Nursing));
    }

    [Theory]
    [InlineData("R03.4")]
    [InlineData("R07.6")]
    [InlineData("R09.2")]
    [InlineData("R13.10")]
    [InlineData("R14.6")]
    [InlineData("R14.12")]
    public void Critical_Writing_Rule_Found(string id)
    {
        var rule = _loader.FindRule(RuleKind.Writing, ExamProfession.Medicine, id);
        Assert.NotNull(rule);
        Assert.Equal(RuleSeverity.Critical, rule!.Severity);
    }

    [Theory]
    [InlineData("RULE_06")]
    [InlineData("RULE_22")]
    [InlineData("RULE_27")]
    [InlineData("RULE_32")]
    [InlineData("RULE_44")]
    public void Critical_Speaking_Rule_Found(string id)
    {
        var rule = _loader.FindRule(RuleKind.Speaking, ExamProfession.Medicine, id);
        Assert.NotNull(rule);
        Assert.Equal(RuleSeverity.Critical, rule!.Severity);
    }

    [Fact]
    public void Speaking_Has_13Stage_Consultation_StateMachine()
    {
        var book = _loader.Load(RuleKind.Speaking, ExamProfession.Medicine);
        Assert.NotNull(book.StateMachines);
        var sm = book.StateMachines!.Value;
        Assert.Equal(13, sm.GetProperty("consultationStages").GetArrayLength());
        Assert.Equal(7, sm.GetProperty("breakingBadNewsProtocol").GetArrayLength());
        Assert.Equal(3, sm.GetProperty("smokingLadder").GetArrayLength());
    }

    [Fact]
    public void Writing_Has_Letter_Skeleton_Table()
    {
        var book = _loader.Load(RuleKind.Writing, ExamProfession.Medicine);
        Assert.NotNull(book.Tables);
        var skeleton = book.Tables!.Value.GetProperty("letterSkeleton");
        Assert.True(skeleton.GetArrayLength() > 10);
    }

    [Fact]
    public void Assessment_Criteria_Is_Loaded_For_Both_Kinds()
    {
        Assert.Equal(JsonValueKind.Object, _loader.GetAssessmentCriteria(RuleKind.Writing).ValueKind);
        Assert.Equal(JsonValueKind.Object, _loader.GetAssessmentCriteria(RuleKind.Speaking).ValueKind);
    }
}

public class WritingRuleEngineTests
{
    private readonly WritingRuleEngine _engine = new(new RulebookLoader());

    private const string LetterWithBoth = @"Dr A B
Cardiology Clinic
Main Street
City

1 January 2026

Dear Dr Smith,
Re: Mr John Jones D.O.B: 01/01/1980

I am writing to refer Mr Jones, a 45-year-old teacher, for your assessment.

Mr Jones smokes 10 cigarettes per day and drinks alcohol occasionally. He presented with chest pain today. His blood pressure was 150/90 mmHg. Examination revealed mild discomfort on palpation. He was advised lifestyle changes.

Please do not hesitate to contact me.

Yours sincerely,

Doctor";

    [Fact]
    public void R03_4_Passes_With_Smoking_And_Drinking()
    {
        var findings = _engine.Lint(new WritingLintInput(LetterWithBoth, "routine_referral"));
        Assert.DoesNotContain(findings, f => f.RuleId == "R03.4");
    }

    [Fact]
    public void R03_4_Fires_When_Smoking_Missing()
    {
        var text = LetterWithBoth.Replace("Mr Jones smokes 10 cigarettes per day and ", "");
        var findings = _engine.Lint(new WritingLintInput(text, "routine_referral"));
        Assert.Contains(findings, f => f.RuleId == "R03.4");
    }

    [Fact]
    public void R03_4_Suppressed_When_Recipient_Is_OT()
    {
        var text = LetterWithBoth
            .Replace("smokes 10 cigarettes per day and drinks alcohol occasionally. ", "");
        var findings = _engine.Lint(new WritingLintInput(
            text, "non_medical_referral", RecipientSpecialty: "Occupational Therapist"));
        Assert.DoesNotContain(findings, f => f.RuleId == "R03.4");
    }

    [Fact]
    public void R04_2_Fires_On_Blank_Between_Salutation_And_Re()
    {
        var text = "Dear Dr Smith,\n\nRe: Ms A\n\nIntro.\n\nBody.\n\nYours sincerely,\nDoctor";
        var findings = _engine.Lint(new WritingLintInput(text, "routine_referral"));
        Assert.Contains(findings, f => f.RuleId == "R04.2" || f.RuleId == "R06.3");
    }

    [Fact]
    public void R05_8_Fires_On_Date_Prefix()
    {
        var text = "Dr A\n\nDate: 1 January 2026\n\nDear Dr Smith,\nRe: Ms A\n\nIntro.\n\nBody.\n\nYours sincerely,\nDoctor";
        var findings = _engine.Lint(new WritingLintInput(text, "routine_referral"));
        Assert.Contains(findings, f => f.RuleId == "R05.8");
    }

    [Fact]
    public void R06_10_Fires_On_Minor_With_Title()
    {
        var text = "Dear Dr Smith,\nRe: Miss Sara Miller D.O.B: 01/01/2015\n\nIntro.\n\nBody.\n\nYours sincerely,\nDoctor";
        var findings = _engine.Lint(new WritingLintInput(text, "routine_referral", PatientIsMinor: true));
        Assert.Contains(findings, f => f.RuleId == "R06.10");
    }

    [Fact]
    public void R06_11_Fires_On_SirMadam_With_Sincerely()
    {
        var text = "Dear Sir/Madam,\nRe: Ms A\n\nIntro.\n\nBody.\n\nYours sincerely,\nDoctor";
        var findings = _engine.Lint(new WritingLintInput(text, "routine_referral"));
        Assert.Contains(findings, f => f.RuleId == "R06.11");
    }

    [Fact]
    public void R07_6_Fires_On_Urgent_Without_Urgent_Word()
    {
        var text = "Dear Dr Smith,\nRe: Ms A\n\nI am writing to refer Ms A for assessment.\n\nOn today's visit she presented with severe pain.\n\nAt your earliest convenience.\n\nYours sincerely,\nDoctor";
        var findings = _engine.Lint(new WritingLintInput(text, "urgent_referral"));
        Assert.Contains(findings, f => f.RuleId == "R07.6" || f.RuleId == "R13.2");
    }

    [Fact]
    public void R08_7_Flags_Next_Visit()
    {
        var text = "Dear Dr Smith,\nRe: Ms A\n\nIntro.\n\nOn the next visit, she reported improvement.\n\nYours sincerely,\nDoctor";
        var findings = _engine.Lint(new WritingLintInput(text, "routine_referral"));
        Assert.Contains(findings, f => f.RuleId == "R08.7" || f.RuleId == "R10.14");
    }

    [Fact]
    public void R08_14_Flags_The_Patient()
    {
        var text = "Dear Dr Smith,\nRe: Ms Miller\n\nIntro.\n\nThe patient presented with nausea.\n\nYours sincerely,\nDoctor";
        var findings = _engine.Lint(new WritingLintInput(text, "routine_referral"));
        Assert.Contains(findings, f => f.RuleId == "R08.14" || f.RuleId == "R12.2");
    }

    [Fact]
    public void R09_2_Fires_On_Urgent_Missing_Earliest_Convenience()
    {
        var text = "Dear Dr Smith,\nRe: Ms A\n\nI am writing to urgently refer Ms A.\n\nOn today's visit she collapsed.\n\nPlease see her soon.\n\nYours sincerely,\nDoctor";
        var findings = _engine.Lint(new WritingLintInput(text, "urgent_referral"));
        Assert.Contains(findings, f => f.RuleId == "R09.2" || f.RuleId == "R13.3");
    }

    [Fact]
    public void R11_1_Flags_Latin_Abbreviation_bd()
    {
        var text = "Dear Dr Smith,\nRe: Ms A\n\nIntro.\n\nShe was prescribed amoxicillin 500 mg bd.\n\nYours sincerely,\nDoctor";
        var findings = _engine.Lint(new WritingLintInput(text, "routine_referral"));
        Assert.Contains(findings, f => f.RuleId == "R11.1");
    }

    [Fact]
    public void R12_1_Flags_Contractions()
    {
        var text = "Dear Dr Smith,\nRe: Ms A\n\nIntro.\n\nShe doesn't take any regular medication.\n\nYours sincerely,\nDoctor";
        var findings = _engine.Lint(new WritingLintInput(text, "routine_referral"));
        Assert.Contains(findings, f => f.RuleId == "R12.1");
    }

    [Fact]
    public void R13_10_Flags_ASAP()
    {
        var text = "Dear Dr Smith,\nRe: Ms A\n\nIntro.\n\nPlease see her ASAP.\n\nYours sincerely,\nDoctor";
        var findings = _engine.Lint(new WritingLintInput(text, "routine_referral"));
        Assert.Contains(findings, f => f.RuleId == "R13.10");
    }

    [Fact]
    public void R14_6_Flags_Was_Presented_In_Discharge()
    {
        var text = "Dear Dr Smith,\nRe: Ms A\n\nI am writing to update you regarding Ms A.\n\nShe was presented with chest pain on admission.\n\nYours sincerely,\nDoctor";
        var findings = _engine.Lint(new WritingLintInput(text, "discharge"));
        Assert.Contains(findings, f => f.RuleId == "R14.6");
    }

    [Fact]
    public void R14_12_Flags_Treated_From()
    {
        var text = "Dear Dr Smith,\nRe: Ms A\n\nIntro.\n\nShe was treated from pneumonia.\n\nYours sincerely,\nDoctor";
        var findings = _engine.Lint(new WritingLintInput(text, "routine_referral"));
        Assert.Contains(findings, f => f.RuleId == "R14.12");
    }

    [Fact]
    public void R15_2_Flags_Jargon_In_Non_Medical_Referral()
    {
        var text = "Dear Sir/Madam,\nRe: Ms A\n\nMs A has hypertension and diabetes.\n\nYours faithfully,\nDoctor";
        var findings = _engine.Lint(new WritingLintInput(
            text, "non_medical_referral", RecipientSpecialty: "Occupational Therapist"));
        Assert.Contains(findings, f => f.RuleId == "R15.2");
    }

    [Fact]
    public void Findings_Are_Ordered_Critical_Then_Major_Then_Minor()
    {
        var text = "Dear Dr Smith,\nRe: Ms A\n\nThe patient has Hypertension.\n\nShe takes amoxicillin bd.\n\nYours sincerely,\nDoctor";
        var findings = _engine.Lint(new WritingLintInput(text, "routine_referral"));
        for (int i = 1; i < findings.Count; i++)
        {
            Assert.True(Rank(findings[i - 1].Severity) <= Rank(findings[i].Severity));
        }
        static int Rank(RuleSeverity s) => s switch
        {
            RuleSeverity.Critical => 0, RuleSeverity.Major => 1, RuleSeverity.Minor => 2, _ => 3,
        };
    }
}

public class SpeakingRuleEngineTests
{
    private readonly SpeakingRuleEngine _engine = new(new RulebookLoader());

    private static SpeakingAuditInput Input(
        IEnumerable<SpeakingTurn> turns,
        string cardType = "first_visit_routine",
        int? silenceMs = null)
        => new(turns.ToList(), cardType, ExamProfession.Medicine, silenceMs);

    [Fact]
    public void Jargon_Detector_Flags_CT_Scan()
    {
        var findings = _engine.Audit(Input(new[]
        {
            new SpeakingTurn("candidate", "We will arrange a CT scan of your chest next week."),
        }));
        Assert.Contains(findings, f => f.RuleId is "RULE_06" or "RULE_07");
    }

    [Fact]
    public void Jargon_Detector_Passes_Imaging_Scan()
    {
        var findings = _engine.Audit(Input(new[]
        {
            new SpeakingTurn("candidate", "I'd like to arrange an imaging scan of your chest."),
        }));
        Assert.DoesNotContain(findings, f => f.RuleId == "RULE_07");
    }

    [Fact]
    public void Monologue_Detector_Flags_Long_Candidate_Turn()
    {
        var longText = string.Join(' ', Enumerable.Range(0, 160).Select(i => "word" + i));
        var findings = _engine.Audit(Input(new[] { new SpeakingTurn("candidate", longText) }));
        Assert.Contains(findings, f => f.RuleId == "RULE_22");
    }

    [Fact]
    public void Weight_Sensitivity_Flags_Direct_Question()
    {
        var findings = _engine.Audit(Input(new[]
        {
            new SpeakingTurn("candidate", "What is your weight?"),
        }));
        Assert.Contains(findings, f => f.RuleId == "RULE_23");
    }

    [Fact]
    public void Smoking_Ladder_Flags_Reduction_Before_Cessation()
    {
        var findings = _engine.Audit(Input(new[]
        {
            new SpeakingTurn("candidate", "I would suggest you try to reduce how many cigarettes you smoke."),
            new SpeakingTurn("patient", "I'm not sure."),
            new SpeakingTurn("candidate", "Ideally we want you to quit smoking completely."),
        }));
        Assert.Contains(findings, f => f.RuleId == "RULE_27");
    }

    [Fact]
    public void Smoking_Ladder_Passes_Cessation_First()
    {
        var findings = _engine.Audit(Input(new[]
        {
            new SpeakingTurn("candidate", "I strongly recommend you quit smoking completely."),
            new SpeakingTurn("patient", "I don't think I can."),
            new SpeakingTurn("candidate", "If complete cessation is hard, we could discuss reducing your intake."),
        }));
        Assert.DoesNotContain(findings, f => f.RuleId == "RULE_27");
    }

    [Fact]
    public void Overdiagnosis_Flags_You_Have_Hypertension()
    {
        var findings = _engine.Audit(Input(new[]
        {
            new SpeakingTurn("candidate", "Based on this reading, you have hypertension."),
        }));
        Assert.Contains(findings, f => f.RuleId == "RULE_32");
    }

    [Fact]
    public void Stage_Coverage_Flags_Missing_Empathy_Recap_Closure()
    {
        var findings = _engine.Audit(Input(new[]
        {
            new SpeakingTurn("candidate", "Hello, I am Dr X. How can I help?"),
            new SpeakingTurn("patient", "I have a headache."),
            new SpeakingTurn("candidate", "Can you tell me more about the pain?"),
            new SpeakingTurn("patient", "It started last week."),
        }));
        Assert.Contains(findings, f => f.RuleId == "RULE_15");
        Assert.Contains(findings, f => f.RuleId == "RULE_20");
        Assert.Contains(findings, f => f.RuleId == "RULE_21");
    }

    [Fact]
    public void BBN_Protocol_Flags_Missing_Steps()
    {
        var findings = _engine.Audit(Input(new[]
        {
            new SpeakingTurn("candidate", "Your results show cancer."),
            new SpeakingTurn("patient", "…"),
        }, cardType: "breaking_bad_news", silenceMs: 500));
        Assert.Contains(findings, f => f.RuleId == "RULE_41");
        Assert.Contains(findings, f => f.RuleId == "RULE_42");
        Assert.Contains(findings, f => f.RuleId == "RULE_44");
    }

    [Fact]
    public void BBN_Protocol_Passes_Fully_Sequenced_Transcript()
    {
        var findings = _engine.Audit(Input(new[]
        {
            new SpeakingTurn("candidate", "Before we discuss your results, is there anyone you'd like to have here with you?"),
            new SpeakingTurn("patient", "My partner is outside."),
            new SpeakingTurn("candidate", "I am afraid the results are not quite what we had hoped for."),
            new SpeakingTurn("candidate", "I am very sorry to tell you — the results are showing signs of cancer."),
            new SpeakingTurn("patient", "…"),
            new SpeakingTurn("candidate", "I know this is a lot to take in. Please take all the time you need."),
            new SpeakingTurn("candidate", "I want you to know that we caught this at an early stage, and there are effective treatment options available."),
            new SpeakingTurn("candidate", "I am here for you, and my number is available whenever you need anything."),
        }, cardType: "breaking_bad_news", silenceMs: 4000));
        Assert.DoesNotContain(findings, f => f.RuleId.StartsWith("RULE_4"));
    }

    [Fact]
    public void BBN_Rules_Do_Not_Fire_On_NonBBN_Card()
    {
        var findings = _engine.Audit(Input(new[] { new SpeakingTurn("candidate", "Hello.") }, cardType: "first_visit_routine"));
        Assert.DoesNotContain(findings, f => f.RuleId.StartsWith("RULE_4"));
    }
}

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
        Assert.Contains("R03.4", prompt.SystemPrompt);
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
        });
        var result = await gateway.CompleteAsync(new AiGatewayRequest { Prompt = prompt });
        Assert.False(string.IsNullOrWhiteSpace(result.Completion));
        Assert.Equal("1.0.0", result.RulebookVersion);
        Assert.NotEmpty(result.AppliedRuleIds);
    }
}
