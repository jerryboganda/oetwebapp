using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Conversation;
using OetLearner.Api.Services.Speaking;
using OetLearner.Api.Services.Writing;

namespace OetLearner.Api.Tests;

/// <summary>
/// Owner profession lock (23 Sep 2026): Writing + Speaking are locked to the
/// learner's registered profession on every learner surface, a learner with
/// no profession fails closed, and the server 404s BEFORE any card / case
/// note payload. Also pins that Emotion / Goal / Topic never reach learners.
/// Learner of profession A = nursing; content of profession B = medicine.
/// </summary>
public sealed class ProfessionLockTests
{
    private const string Nurse = "lock-nurse";
    private const string Doctor = "lock-doctor";
    private const string NoProfession = "lock-none";

    // ── Shared guard ─────────────────────────────────────────────────────

    [Theory]
    [InlineData("occupational_therapy", "Occupational-Therapy", true)]
    [InlineData(" Medicine ", "medicine", true)]
    [InlineData("nursing", "medicine", false)]
    [InlineData(null, "medicine", false)]
    [InlineData("", "medicine", false)]
    [InlineData("medicine", null, false)]
    public void Guard_Matches_NormalisesAndFailsClosed(string? learner, string? content, bool expected)
        => Assert.Equal(expected, LearnerProfessionGuard.Matches(learner, content));

    [Fact]
    public void Guard_RolePlayCard_UniversalOnlyWithAContentItem_AndNeverWithoutAProfession()
    {
        Assert.True(LearnerProfessionGuard.CanAccessRolePlayCard("nursing", "medicine", hasContentItem: true, contentItemProfessionId: null));
        Assert.False(LearnerProfessionGuard.CanAccessRolePlayCard("nursing", "medicine", hasContentItem: false, contentItemProfessionId: null));
        Assert.True(LearnerProfessionGuard.CanAccessRolePlayCard("nursing", "nursing", hasContentItem: false, contentItemProfessionId: null));
        Assert.False(LearnerProfessionGuard.CanAccessRolePlayCard(null, "nursing", hasContentItem: true, contentItemProfessionId: null));
    }

    // ── Speaking sessions ────────────────────────────────────────────────

    [Theory]
    [InlineData(Doctor)]
    [InlineData(NoProfession)]
    public async Task SpeakingSession_Create_OtherProfessionOrNoProfession_Is404_AndCreatesNothing(string userId)
    {
        await using var db = NewDb();
        var cardId = await SeedCardAsync(db, "nursing");
        var svc = new SpeakingSessionService(db);

        var ex = await Assert.ThrowsAsync<ApiException>(() => svc.CreateSessionAsync(
            userId, new CreateSpeakingSessionRequest(cardId, "ai_self_practice"), CancellationToken.None));

        Assert.Equal("role_play_card_not_found", ex.ErrorCode);
        Assert.Equal(404, ex.StatusCode);
        Assert.False(await db.SpeakingSessions.AnyAsync());
        Assert.False(await db.Attempts.AnyAsync());
    }

    [Fact]
    public async Task SpeakingSession_Create_OwnProfession_Succeeds_AndCardHasNoEmotionGoalTopic()
    {
        await using var db = NewDb();
        var cardId = await SeedCardAsync(db, "nursing");
        var svc = new SpeakingSessionService(db);

        var created = await svc.CreateSessionAsync(
            Nurse, new CreateSpeakingSessionRequest(cardId, "ai_self_practice"), CancellationToken.None);
        var detail = await svc.GetSessionForLearnerAsync(Nurse, created.SessionId, CancellationToken.None);

        foreach (var card in new[] { created.Card, detail.Card })
        {
            var json = JsonSerializer.Serialize(card);
            Assert.DoesNotContain("patientEmotion", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("communicationGoal", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("clinicalTopic", json, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Lock scenario", json, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task SpeakingSession_ResumedOnAnotherProfessionsCard_Is404OnReadAndTransitions()
    {
        // A session created before the lock (doctor on a nursing card) must
        // not be readable or advanced any more.
        await using var db = NewDb();
        var cardId = await SeedCardAsync(db, "nursing");
        var now = DateTimeOffset.UtcNow;
        db.SpeakingSessions.Add(new SpeakingSession
        {
            Id = "sps_prelock",
            UserId = Doctor,
            RolePlayCardId = cardId,
            Mode = SpeakingSessionMode.AiSelfPractice,
            State = SpeakingSessionState.WarmUp,
            CreatedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();
        var svc = new SpeakingSessionService(db);

        var read = await Assert.ThrowsAsync<ApiException>(() =>
            svc.GetSessionForLearnerAsync(Doctor, "sps_prelock", CancellationToken.None));
        Assert.Equal("speaking_session_not_found", read.ErrorCode);

        var transition = await Assert.ThrowsAsync<ApiException>(() =>
            svc.StartWarmupAsync(Doctor, "sps_prelock", CancellationToken.None));
        Assert.Equal("speaking_session_not_found", transition.ErrorCode);
        Assert.Null((await db.SpeakingSessions.AsNoTracking().SingleAsync()).WarmupStartedAt);
    }

    [Fact]
    public async Task SpeakingAttemptAndEvaluationReads_PreLockAttemptOnAnotherProfession_Are404()
    {
        await using var db = NewDb();
        var now = DateTimeOffset.UtcNow;
        db.ContentItems.Add(new ContentItem
        {
            Id = "ci-att-med",
            ContentType = "speaking_task",
            SubtestCode = "speaking",
            ProfessionId = "medicine",
            Title = "Medicine task",
            Difficulty = "core",
            Status = ContentStatus.Published,
            PublishedRevisionId = "ci-att-med-r1",
        });
        db.Attempts.Add(new Attempt
        {
            Id = "att-prelock",
            UserId = Nurse,
            ContentId = "ci-att-med",
            SubtestCode = "speaking",
            Context = "practice",
            Mode = "practice",
            State = AttemptState.Completed,
            StartedAt = now,
        });
        db.Evaluations.Add(new Evaluation
        {
            Id = "ev-prelock",
            AttemptId = "att-prelock",
            SubtestCode = "speaking",
            State = AsyncState.Completed,
            ScoreRange = "n/a",
            ModelExplanationSafe = "n/a",
            LearnerDisclaimer = "n/a",
        });
        await db.SaveChangesAsync();
        var svc = new LearnerService(db, null!, null!, null!, null!, null!, null!, null!);

        foreach (var read in new Func<Task<object>>[]
        {
            () => svc.GetSpeakingAttemptAsync(Nurse, "att-prelock", CancellationToken.None),
            () => svc.GetSpeakingEvaluationSummaryAsync(Nurse, "ev-prelock", CancellationToken.None),
            () => svc.GetSpeakingReviewAsync(Nurse, "ev-prelock", CancellationToken.None),
        })
        {
            var ex = await Assert.ThrowsAsync<ApiException>(read);
            Assert.Equal("content_not_found", ex.ErrorCode);
        }
    }

    // ── Conversation ─────────────────────────────────────────────────────

    [Fact]
    public async Task Conversation_Create_OtherProfessionsContent_Is404()
    {
        await using var db = NewDb();
        await SeedSpeakingContentAsync(db, "ci-conv-med", "medicine");
        var svc = NewConversationService(db);

        var ex = await Assert.ThrowsAsync<ApiException>(() => svc.CreateSessionAsync(
            Nurse, new ConversationCreateSessionRequest("ci-conv-med", null, "oet-roleplay"), CancellationToken.None));

        Assert.Equal("CONVERSATION_CONTENT_NOT_FOUND", ex.ErrorCode);
        Assert.False(await db.ConversationSessions.AnyAsync());
    }

    [Fact]
    public async Task Conversation_Create_NoProfession_FailsClosed()
    {
        await using var db = NewDb();
        var svc = NewConversationService(db);

        var ex = await Assert.ThrowsAsync<ApiException>(() => svc.CreateSessionAsync(
            NoProfession, new ConversationCreateSessionRequest(null, null, "oet-roleplay", Profession: "medicine"), CancellationToken.None));

        Assert.Equal("PROFESSION_REQUIRED", ex.ErrorCode);
        Assert.False(await db.ConversationSessions.AnyAsync());
    }

    [Fact]
    public async Task Conversation_Create_ProfessionComesFromTheAccount_NotTheClient()
    {
        await using var db = NewDb();
        var svc = NewConversationService(db);

        await svc.CreateSessionAsync(
            Nurse, new ConversationCreateSessionRequest(null, null, "oet-roleplay", Profession: "medicine"), CancellationToken.None);

        Assert.Equal("nursing", (await db.ConversationSessions.SingleAsync()).Profession);
    }

    [Fact]
    public async Task Conversation_Create_OwnContent_EchoHidesEmotionGoalAndInterlocutor_ButStoredScenarioKeepsThem()
    {
        await using var db = NewDb();
        await SeedSpeakingContentAsync(db, "ci-conv-nurse", "nursing");
        var svc = NewConversationService(db);

        var echo = await svc.CreateSessionAsync(
            Nurse, new ConversationCreateSessionRequest("ci-conv-nurse", null, "oet-roleplay"), CancellationToken.None);

        using var echoDoc = JsonDocument.Parse(JsonSerializer.Serialize(echo));
        var learnerScenario = echoDoc.RootElement.GetProperty("scenarioJson").GetString()!;
        Assert.DoesNotContain("\"emotion\"", learnerScenario, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("expectedOutcomes", learnerScenario, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("hiddenPatientProfile", learnerScenario, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("cuePrompts", learnerScenario, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Secret hidden profile", learnerScenario, StringComparison.Ordinal);
        Assert.Contains("Ward", learnerScenario, StringComparison.Ordinal);

        // Internal AI scenario is untouched (the AI patient still needs it).
        var stored = (await db.ConversationSessions.SingleAsync()).ScenarioJson;
        Assert.Contains("\"emotion\"", stored, StringComparison.Ordinal);
        Assert.Contains("Secret hidden profile", stored, StringComparison.Ordinal);
    }

    [Fact]
    public void Conversation_LearnerScenario_StripsTemplatePatientVoiceEmotion()
    {
        var learner = ConversationService.ToLearnerScenarioJson(
            """{"title":"T","patientVoice":{"Emotion":"angry","gender":"female"},"communicationGoal":"g","clinicalTopic":"t"}""");

        using var doc = JsonDocument.Parse(learner);
        Assert.False(doc.RootElement.TryGetProperty("communicationGoal", out _));
        Assert.False(doc.RootElement.TryGetProperty("clinicalTopic", out _));
        var voice = doc.RootElement.GetProperty("patientVoice");
        Assert.False(voice.TryGetProperty("Emotion", out _));
        Assert.Equal("female", voice.GetProperty("gender").GetString());
        Assert.Equal("{}", ConversationService.ToLearnerScenarioJson("not json"));
    }

    // ── Writing mocks ────────────────────────────────────────────────────

    [Fact]
    public async Task WritingMock_List_OnlyOwnProfession_AndNoProfessionSeesNone()
    {
        await using var db = NewDb();
        var medicineMock = await SeedWritingMockAsync(db, "medicine");
        var nursingMock = await SeedWritingMockAsync(db, "nursing");
        var svc = NewWritingMockService(db);

        var nurse = await svc.ListAsync(Nurse, CancellationToken.None);
        Assert.Contains(nurse, m => m.Id == nursingMock);
        Assert.DoesNotContain(nurse, m => m.Id == medicineMock);

        Assert.Empty(await svc.ListAsync(NoProfession, CancellationToken.None));
    }

    [Theory]
    [InlineData(Nurse)]
    [InlineData(NoProfession)]
    public async Task WritingMock_Start_OtherProfessionOrNoProfession_Is404(string userId)
    {
        await using var db = NewDb();
        var medicineMock = await SeedWritingMockAsync(db, "medicine");
        var svc = NewWritingMockService(db);

        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            svc.StartSessionAsync(userId, medicineMock, isPractice: false, CancellationToken.None));

        Assert.Equal("writing_mock_not_found", ex.ErrorCode);
        Assert.False(await db.WritingMockSessions.AnyAsync());
    }

    [Fact]
    public async Task WritingMock_Submit_PreLockSessionOnAnotherProfession_Is404_BeforeGrading()
    {
        // The service is built with no grading pipeline: reaching grading
        // would throw a NullReferenceException, not the 404 asserted here.
        await using var db = NewDb();
        var medicineMock = await SeedWritingMockAsync(db, "medicine");
        var now = DateTimeOffset.UtcNow;
        var sessionId = Guid.NewGuid();
        db.WritingMockSessions.Add(new WritingMockSession
        {
            Id = sessionId,
            UserId = Nurse,
            MockId = medicineMock,
            StartedAt = now.AddMinutes(-10),
            ReadingPhaseEndedAt = now.AddMinutes(-5),
            Status = "writing",
            CreatedAt = now.AddMinutes(-10),
        });
        await db.SaveChangesAsync();
        var svc = NewWritingMockService(db);

        var ex = await Assert.ThrowsAsync<ApiException>(() => svc.SubmitMockAsync(
            Nurse, sessionId, new WritingMockSubmitRequest("Dear Dr Green, letter.", 4, 60), CancellationToken.None));

        Assert.Equal("writing_mock_not_found", ex.ErrorCode);
        Assert.Equal("writing", (await db.WritingMockSessions.AsNoTracking().SingleAsync()).Status);
        Assert.False(await db.WritingSubmissions.AnyAsync());
    }

    // ── Fixtures ─────────────────────────────────────────────────────────

    private static LearnerDbContext NewDb()
    {
        var db = new LearnerDbContext(new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase($"profession-lock-{Guid.NewGuid():N}")
            .Options);
        var now = DateTimeOffset.UtcNow;
        foreach (var (id, profession) in new (string Id, string? Profession)[] { (Nurse, "nursing"), (Doctor, "medicine"), (NoProfession, null) })
        {
            db.Users.Add(new LearnerUser
            {
                Id = id,
                DisplayName = id,
                Email = $"{id}@example.test",
                ActiveProfessionId = profession,
                AccountStatus = "active",
                CreatedAt = now,
                LastActiveAt = now,
            });
        }
        db.SaveChanges();
        return db;
    }

    private static async Task<string> SeedCardAsync(LearnerDbContext db, string profession)
    {
        var contentItemId = $"ci-{Guid.NewGuid():N}";
        db.ContentItems.Add(new ContentItem
        {
            Id = contentItemId,
            ContentType = "speaking_role_play",
            ProfessionId = profession,
            SubtestCode = "speaking",
            Title = "Lock scenario",
            Difficulty = "core",
            Status = ContentStatus.Published,
            PublishedRevisionId = $"rev-{Guid.NewGuid():N}",
        });
        var cardId = $"card-{Guid.NewGuid():N}";
        db.RolePlayCards.Add(new RolePlayCard
        {
            Id = cardId,
            ContentItemId = contentItemId,
            ProfessionId = profession,
            ScenarioTitle = "Lock scenario",
            Setting = "Ward",
            CandidateRole = "Nurse",
            PatientEmotion = "furious",
            CommunicationGoal = "Persuade",
            ClinicalTopic = "wound care",
            Difficulty = "core",
            CriteriaFocusJson = "[]",
            Status = ContentStatus.Published,
            PrepTimeSeconds = 180,
            RolePlayTimeSeconds = 300,
        });
        await db.SaveChangesAsync();
        return cardId;
    }

    private static async Task SeedSpeakingContentAsync(LearnerDbContext db, string id, string profession)
    {
        var now = DateTimeOffset.UtcNow;
        db.ContentItems.Add(new ContentItem
        {
            Id = id,
            ContentType = "speaking_roleplay",
            SubtestCode = "speaking",
            ProfessionId = profession,
            Title = "Conversation lock scenario",
            Difficulty = "core",
            Status = ContentStatus.Published,
            PublishedRevisionId = $"{id}-r1",
            CreatedAt = now,
            UpdatedAt = now,
            PublishedAt = now,
            DetailJson = JsonSerializer.Serialize(new
            {
                setting = "Ward",
                patientEmotion = "anxious",
                communicationGoal = "Reassure the patient",
                interlocutorCard = new
                {
                    patientProfile = "Secret hidden profile",
                    cuePrompts = new[] { "Secret cue" },
                },
            }),
            ModelAnswerJson = "{}",
        });
        await db.SaveChangesAsync();
    }

    private static async Task<Guid> SeedWritingMockAsync(LearnerDbContext db, string profession)
    {
        var scenarioId = Guid.NewGuid();
        var mockId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        db.WritingScenarios.Add(new WritingScenario
        {
            Id = scenarioId,
            Title = $"{profession} scenario",
            LetterType = "LT-RR",
            Profession = profession,
            Difficulty = 3,
            IsDiagnostic = false,
            Status = "published",
            AuthorId = "admin",
            PublishedAt = now,
            CreatedAt = now,
        });
        db.WritingMocks.Add(new WritingMock
        {
            Id = mockId,
            ScenarioId = scenarioId,
            Title = $"{profession} mock",
            Difficulty = 3,
            Status = "published",
            CreatedAt = now,
        });
        await db.SaveChangesAsync();
        return mockId;
    }

    private static ConversationService NewConversationService(LearnerDbContext db)
        => new(
            db,
            Options.Create(new ConversationOptions()),
            new FixedConversationOptions(new ConversationOptions()),
            new AllowConversation());

    private static WritingMockService NewWritingMockService(LearnerDbContext db)
        => new(
            db,
            TimeProvider.System,
            events: null!,
            pipeline: null!,
            tutorReview: null!,
            NullLogger<WritingMockService>.Instance);

    private sealed class FixedConversationOptions(ConversationOptions options) : IConversationOptionsProvider
    {
        public Task<ConversationOptions> GetAsync(CancellationToken ct = default) => Task.FromResult(options);
        public void Invalidate() { }
    }

    private sealed class AllowConversation : IConversationEntitlementService
    {
        public Task<ConversationEntitlement> CheckAsync(string? userId, CancellationToken ct)
            => Task.FromResult(new ConversationEntitlement(true, "premium", 10, 10, 7, null, string.Empty));
    }
}
