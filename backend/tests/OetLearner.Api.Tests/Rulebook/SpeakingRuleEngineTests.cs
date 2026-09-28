using System.Text.Json;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Rulebook;
using OetLearner.Api.Services.Rulebooks;

namespace OetLearner.Api.Tests.Rulebook;

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
