using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Speaking;

namespace OetLearner.Api.Tests.Speaking;

public sealed class SpeakingSimulationV11PersonaServiceTests
{
    [Fact]
    public void Patient_role_alone_never_activates_second_visit_memory()
    {
        Assert.False(
            SpeakingSimulationV11PersonaService.ContainsExplicitSecondVisitIndicator(
                "I am your patient and I need advice.",
                "returning for review"));
    }

    [Fact]
    public void Explicit_follow_up_signals_are_required()
    {
        Assert.False(
            SpeakingSimulationV11PersonaService.ContainsExplicitSecondVisitIndicator(
                "I saw you last time.",
                "returning for review"));

        Assert.True(
            SpeakingSimulationV11PersonaService.ContainsExplicitSecondVisitIndicator(
                "I am returning for a review.",
                "returning for review"));
    }

    [Fact]
    public void Actor_safety_boundary_replaces_coaching_or_medical_advice()
    {
        Assert.Equal(
            SpeakingSimulationV11PersonaService.NeutralSilencePrompt,
            SpeakingSimulationV11PersonaService.SanitizeActorReply("Excellent question. Your score will be high."));
        Assert.Equal(
            SpeakingSimulationV11PersonaService.NeutralSilencePrompt,
            SpeakingSimulationV11PersonaService.SanitizeActorReply("You should stop taking this medicine immediately."));
        Assert.Equal(
            "I feel worried about the pain.",
            SpeakingSimulationV11PersonaService.SanitizeActorReply(" I feel worried about the pain. "));
    }

    [Fact]
    public async Task Card_b_is_independent_without_explicit_authoring_flag()
    {
        await using var db = CreateDb();
        var cardA = CreateCard("card-a", "A");
        var cardB = CreateCard("card-b", "B");
        db.RolePlayCards.AddRange(cardA, cardB);
        db.InterlocutorScripts.Add(
            CreateScript("script-a", cardA.Id, hidden: "A-only hidden fact"));
        db.InterlocutorScripts.Add(
            CreateScript("script-b", cardB.Id, hidden: "B-only hidden fact"));
        await db.SaveChangesAsync();

        var service = new SpeakingSimulationV11PersonaService(db);
        var exam = new SpeakingExamSession
        {
            Id = "exam-1",
            UserId = "user-1",
            CardAId = cardA.Id,
            CardBId = cardB.Id,
        };
        var a = await service.CaptureAtRevealAsync(
            exam,
            CreateSession("session-a", cardA.Id, exam.Id, "a"),
            cardA,
            DateTimeOffset.UtcNow,
            CancellationToken.None);
        var b = await service.CaptureAtRevealAsync(
            exam,
            CreateSession("session-b", cardB.Id, exam.Id, "b"),
            cardB,
            DateTimeOffset.UtcNow,
            CancellationToken.None);

        Assert.False(b.FollowUpEligible);
        Assert.Equal("[]", b.ApprovedCarryFactKeysJson);
        Assert.Equal("{}", b.CarriedFactsJson);
        Assert.NotEqual(a.MemoryScopeKey, b.MemoryScopeKey);
        Assert.DoesNotContain("A-only hidden fact", b.PersonaJson);
    }

    [Fact]
    public async Task Explicit_follow_up_carries_only_the_approved_current_fact_key()
    {
        await using var db = CreateDb();
        var cardA = CreateCard("card-a", "A");
        var cardB = CreateCard("card-b", "B");
        db.RolePlayCards.AddRange(cardA, cardB);
        db.InterlocutorScripts.Add(
            CreateScript("script-a", cardA.Id, hidden: "A-only hidden fact"));
        var bScript = CreateScript("script-b", cardB.Id, hidden: "B-only hidden fact");
        bScript.AllowsSecondVisit = true;
        bScript.SecondVisitIndicator = "returning for review";
        bScript.SecondVisitCarryFactsJson = "[\"hiddenInformation\"]";
        db.InterlocutorScripts.Add(bScript);
        await db.SaveChangesAsync();

        var service = new SpeakingSimulationV11PersonaService(db);
        var exam = new SpeakingExamSession
        {
            Id = "exam-2",
            UserId = "user-2",
            CardAId = cardA.Id,
            CardBId = cardB.Id,
        };
        await service.CaptureAtRevealAsync(
            exam,
            CreateSession("session-a", cardA.Id, exam.Id, "a"),
            cardA,
            DateTimeOffset.UtcNow,
            CancellationToken.None);
        var b = await service.CaptureAtRevealAsync(
            exam,
            CreateSession("session-b", cardB.Id, exam.Id, "b"),
            cardB,
            DateTimeOffset.UtcNow,
            CancellationToken.None);

        var notActivated = await service.ObserveCandidateTurnAsync(
            b,
            "You are my patient.",
            CancellationToken.None);
        Assert.False(notActivated.FollowUpActivated);
        Assert.Equal("{}", notActivated.CarriedFactsJson);

        var activated = await service.ObserveCandidateTurnAsync(
            b,
            "I understand you are returning for a review.",
            CancellationToken.None);

        Assert.True(activated.FollowUpActivated);
        Assert.Contains("A-only hidden fact", activated.CarriedFactsJson);
        Assert.DoesNotContain("B-only hidden fact", activated.CarriedFactsJson);
        Assert.DoesNotContain("transcript", activated.CarriedFactsJson, StringComparison.OrdinalIgnoreCase);
    }

    private static LearnerDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase($"speaking-persona-v11-{Guid.NewGuid():N}")
            .Options;
        return new LearnerDbContext(options);
    }

    private static RolePlayCard CreateCard(string id, string suffix) => new()
    {
        Id = id,
        ContentItemId = $"content-{suffix}",
        ProfessionId = "medicine",
        ScenarioTitle = $"Scenario {suffix}",
        Setting = "Clinic",
        CandidateRole = "Doctor",
        InterlocutorRole = "Patient",
        PatientEmotion = "worried",
        CommunicationGoal = "Inform",
        ClinicalTopic = "general",
        UpdatedAt = DateTimeOffset.UtcNow,
    };

    private static InterlocutorScript CreateScript(
        string id,
        string cardId,
        string hidden) => new()
    {
        Id = id,
        RolePlayCardId = cardId,
        OpeningResponse = "I am worried.",
        HiddenInformation = hidden,
        EmotionalState = "worried",
        ClosingCue = "I accept the explanation.",
        LayLanguageTriggersJson = "[]",
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
    };

    private static SpeakingSession CreateSession(
        string id,
        string cardId,
        string examId,
        string slot) => new()
    {
        Id = id,
        UserId = "user-1",
        RolePlayCardId = cardId,
        ExamSessionId = examId,
        ExamSlot = slot,
        Mode = SpeakingSessionMode.AiExam,
        State = SpeakingSessionState.Prep,
        PrepStartedAt = DateTimeOffset.UtcNow,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
    };
}
