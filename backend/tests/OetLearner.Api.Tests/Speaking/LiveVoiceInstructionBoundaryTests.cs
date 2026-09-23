using OetLearner.Api.Domain;
using OetLearner.Api.Services.Speaking;

namespace OetLearner.Api.Tests.Speaking;

/// <summary>
/// B8: the live voice system instruction carries the interlocutor boundary
/// rules ported from the v1.1 disclosure policy (PR #235).
/// </summary>
public sealed class LiveVoiceInstructionBoundaryTests
{
    private static string Build()
    {
        var card = new RolePlayCard
        {
            Id = "rpc-1",
            ContentItemId = "ci-1",
            ProfessionId = "medicine",
            ScenarioTitle = "Post-operative pain review",
            Setting = "Hospital ward",
            CandidateRole = "Doctor",
            InterlocutorRole = "Patient",
            Background = "Day two after knee surgery.",
            PatientEmotion = "anxious",
            CommunicationGoal = "Reassure and plan analgesia",
            ClinicalTopic = "pain management",
            Difficulty = "core",
            CriteriaFocusJson = "[]",
            Disclaimer = "Practice estimate only.",
            Status = ContentStatus.Published,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        var script = new InterlocutorScript
        {
            Id = "is-1",
            RolePlayCardId = "rpc-1",
            OpeningResponse = "Doctor, this pain is much worse than yesterday.",
            HiddenInformation = "Previous bad reaction to morphine",
            ResistanceLevel = ResistanceLevel.Medium,
            ClosingCue = "Accept the plan once reassured",
            EmotionalState = "Worried",
            LayLanguageTriggersJson = "[]",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        var readiness = new LiveVoiceContentReadiness(script, false, false, "authored_interlocutor_script", "not_required");
        return LiveVoiceService.BuildInstructions(card, script, readiness);
    }

    [Fact]
    public void Instructions_AreCandidateFirst()
    {
        var text = Build();
        Assert.Contains("never speak first", text, StringComparison.Ordinal);
        Assert.Contains("Wait for the candidate to open the consultation", text, StringComparison.Ordinal);
        Assert.Contains("opening response only after the candidate", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Instructions_NeverDiscloseCardTasksOrCriteria()
    {
        var text = Build();
        Assert.Contains("NEVER DISCLOSE the candidate card, the candidate tasks, the marking criteria", text, StringComparison.Ordinal);
        Assert.Contains("NEVER READ OR DISCLOSE TO THE CANDIDATE", text, StringComparison.Ordinal);
        Assert.Contains("[PRIVATE ROLEPLAYER DATA, NEVER DISCLOSE TO THE CANDIDATE]", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Instructions_StayInRole_DiscloseOnlyWhenAsked_ShortTurns()
    {
        var text = Build();
        Assert.Contains("STAY IN ROLE", text, StringComparison.Ordinal);
        Assert.Contains("DISCLOSE ONLY WHEN ASKED", text, StringComparison.Ordinal);
        Assert.Contains("at most one new piece of information per turn", text, StringComparison.Ordinal);
        Assert.Contains("Stop speaking immediately when the candidate interrupts", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Rules_PrecedeCardData()
    {
        var text = Build();
        Assert.True(text.IndexOf("CANDIDATE FIRST", StringComparison.Ordinal)
            < text.IndexOf("[CANDIDATE CARD DATA", StringComparison.Ordinal));
    }
}
