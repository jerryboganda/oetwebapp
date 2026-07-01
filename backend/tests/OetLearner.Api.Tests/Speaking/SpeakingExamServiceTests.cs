using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Billing;
using OetLearner.Api.Services.Speaking;

namespace OetLearner.Api.Tests.Speaking;

// Speaking module rebuild (2026-06-11 spec). Pins the two-card exam invariants:
//   * State machine: intro → prep_a → active_a → prep_b → active_b → completed,
//     with server-authoritative auto-advance and NO bridge step.
//   * Credits: an AI exam debits exactly two speaking credits (one per card at
//     reveal), idempotent on the exam+slot reference.
//   * Leakage (MISSION CRITICAL): the learner exam projection never serializes
//     the roleplayer (patient) card, the hidden card type, or any interlocutor
//     field — in any phase.
public sealed class SpeakingExamServiceTests : IAsyncLifetime
{
    private LearnerDbContext _db = default!;
    private AiPackageCreditService _credits = default!;
    private SpeakingAiAssessmentService _assessor = default!;
    private SpeakingExamService _exams = default!;

    private const string UserId = "exam-learner-1";

    public Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase($"speaking-exam-{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        _db = new LearnerDbContext(options);
        _credits = new AiPackageCreditService(_db, NullLogger<AiPackageCreditService>.Instance);
        // The assessor is only invoked from the results path; the tests here drive
        // the state machine + credits + leakage, so a minimal construction is fine.
        _assessor = null!;
        _exams = new SpeakingExamService(_db, _assessor!, NullLogger<SpeakingExamService>.Instance, _credits);
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _db.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task CreateExam_StartsInIntro_WithoutDebitingCredits()
    {
        await SeedWalletAsync(speakingCredits: 5);
        await SeedTwoPublishedCardsAsync();

        var exam = await _exams.CreateExamAsync(UserId, new CreateSpeakingExamRequest("ai", ProfessionId: "medicine"), default);

        Assert.Equal("intro", exam.State);
        Assert.Equal(0, exam.CurrentCardNumber);
        Assert.Null(exam.CurrentCard);
        // No credit consumed yet — the first debit happens at Card A reveal.
        Assert.Equal(5, (await _credits.GetSnapshotAsync(UserId, 0, default)).SpeakingOnlyCredits);
    }

    [Fact]
    public async Task FinishIntro_RevealsCardA_AndDebitsOneCredit()
    {
        await SeedWalletAsync(speakingCredits: 5);
        await SeedTwoPublishedCardsAsync();
        var exam = await _exams.CreateExamAsync(UserId, new CreateSpeakingExamRequest("ai", ProfessionId: "medicine"), default);

        var afterIntro = await _exams.FinishIntroAsync(UserId, exam.ExamId, default);

        Assert.Equal("prep_a", afterIntro.State);
        Assert.Equal(1, afterIntro.CurrentCardNumber);
        Assert.NotNull(afterIntro.CurrentCard);
        Assert.NotNull(afterIntro.CurrentSessionId);
        Assert.Equal(4, (await _credits.GetSnapshotAsync(UserId, 0, default)).SpeakingOnlyCredits);
    }

    [Fact]
    public async Task AutoAdvance_ClosesCardA_RevealsCardB_NoBridge_AndDebitsSecondCredit()
    {
        await SeedWalletAsync(speakingCredits: 5);
        await SeedTwoPublishedCardsAsync(prepSeconds: 180, discussionSeconds: 300);
        var exam = await _exams.CreateExamAsync(UserId, new CreateSpeakingExamRequest("ai", ProfessionId: "medicine"), default);
        await _exams.FinishIntroAsync(UserId, exam.ExamId, default);

        // Drive the lazy advance with a tracked exam + a clock 9 minutes later
        // (past the 8-minute card window).
        var tracked = await _db.SpeakingExamSessions.FirstAsync(e => e.Id == exam.ExamId);
        var afterA = DateTimeOffset.UtcNow.AddMinutes(9);
        var changed = await _exams.AdvanceAsync(tracked, afterA, default);
        await _db.SaveChangesAsync();

        Assert.True(changed);
        // No bridge state — we land directly on Card B's prep.
        Assert.Equal(SpeakingExamState.PrepB, tracked.State);
        Assert.NotNull(tracked.SessionBId);
        // Both cards debited → exactly 2 credits gone.
        Assert.Equal(3, (await _credits.GetSnapshotAsync(UserId, 0, default)).SpeakingOnlyCredits);
        Assert.NotNull(tracked.CreditARefId);
        Assert.NotNull(tracked.CreditBRefId);
    }

    [Fact]
    public async Task AutoAdvance_FromIntroToCompleted_DebitsExactlyTwoCredits()
    {
        await SeedWalletAsync(speakingCredits: 5);
        await SeedTwoPublishedCardsAsync(prepSeconds: 180, discussionSeconds: 300);
        var exam = await _exams.CreateExamAsync(UserId, new CreateSpeakingExamRequest("ai", ProfessionId: "medicine"), default);
        await _exams.FinishIntroAsync(UserId, exam.ExamId, default);

        // Far enough in the future to roll Card A AND Card B to completion.
        var tracked = await _db.SpeakingExamSessions.FirstAsync(e => e.Id == exam.ExamId);
        var afterBoth = DateTimeOffset.UtcNow.AddMinutes(20);
        await _exams.AdvanceAsync(tracked, afterBoth, default);
        await _db.SaveChangesAsync();

        Assert.Equal(SpeakingExamState.Completed, tracked.State);
        Assert.NotNull(tracked.CompletedAt);
        // Exactly two speaking credits consumed across the whole exam.
        Assert.Equal(3, (await _credits.GetSnapshotAsync(UserId, 0, default)).SpeakingOnlyCredits);
    }

    [Fact]
    public async Task Advance_IsIdempotent_NeverDoubleCharges()
    {
        await SeedWalletAsync(speakingCredits: 5);
        await SeedTwoPublishedCardsAsync();
        var exam = await _exams.CreateExamAsync(UserId, new CreateSpeakingExamRequest("ai", ProfessionId: "medicine"), default);
        await _exams.FinishIntroAsync(UserId, exam.ExamId, default);

        var tracked = await _db.SpeakingExamSessions.FirstAsync(e => e.Id == exam.ExamId);
        var later = DateTimeOffset.UtcNow.AddMinutes(9);
        await _exams.AdvanceAsync(tracked, later, default);
        await _db.SaveChangesAsync();
        // Re-run the same advance — must not debit again.
        await _exams.AdvanceAsync(tracked, later, default);
        await _db.SaveChangesAsync();

        Assert.Equal(3, (await _credits.GetSnapshotAsync(UserId, 0, default)).SpeakingOnlyCredits);
    }

    // ── "Full Mock Speaking Exam Access" (requirements gap audit 2026-07-01) ──
    // Card A prefers a dedicated mock-exam-allowance unit over the per-card AI
    // Speaking Credits wallet; when it does, Card B must not charge again.

    [Fact]
    public async Task FinishIntro_WithMockExamCredit_FundsWholeExam_WithoutTouchingSpeakingCredits()
    {
        await SeedWalletAsync(speakingCredits: 5, mockExams: 1);
        await SeedTwoPublishedCardsAsync();
        var exam = await _exams.CreateExamAsync(UserId, new CreateSpeakingExamRequest("ai", ProfessionId: "medicine"), default);

        var afterIntro = await _exams.FinishIntroAsync(UserId, exam.ExamId, default);
        var tracked = await _db.SpeakingExamSessions.FirstAsync(e => e.Id == exam.ExamId);

        Assert.Equal("prep_a", afterIntro.State);
        Assert.True(tracked.FundedByMockCredit);
        Assert.NotNull(tracked.CreditARefId);
        var snapshot = await _credits.GetSnapshotAsync(UserId, 0, default);
        Assert.Equal(0, snapshot.MockExamsRemaining);
        // Untouched — the mock allowance paid for the exam, not the per-card wallet.
        Assert.Equal(5, snapshot.SpeakingOnlyCredits);
    }

    [Fact]
    public async Task AutoAdvance_WithMockExamCredit_CardBIsFree_NoDoubleCharge()
    {
        await SeedWalletAsync(speakingCredits: 5, mockExams: 1);
        await SeedTwoPublishedCardsAsync(prepSeconds: 180, discussionSeconds: 300);
        var exam = await _exams.CreateExamAsync(UserId, new CreateSpeakingExamRequest("ai", ProfessionId: "medicine"), default);
        await _exams.FinishIntroAsync(UserId, exam.ExamId, default);

        var tracked = await _db.SpeakingExamSessions.FirstAsync(e => e.Id == exam.ExamId);
        var afterA = DateTimeOffset.UtcNow.AddMinutes(9);
        await _exams.AdvanceAsync(tracked, afterA, default);
        await _db.SaveChangesAsync();

        Assert.Equal(SpeakingExamState.PrepB, tracked.State);
        Assert.NotNull(tracked.SessionBId);
        Assert.NotNull(tracked.CreditBRefId);
        Assert.Equal(tracked.CreditARefId, tracked.CreditBRefId);
        var snapshot = await _credits.GetSnapshotAsync(UserId, 0, default);
        Assert.Equal(0, snapshot.MockExamsRemaining);
        Assert.Equal(5, snapshot.SpeakingOnlyCredits);
    }

    [Fact]
    public async Task CreateExam_MockExamCreditAlone_SatisfiesTheCreditGate_EvenWithZeroSpeakingCredits()
    {
        await SeedWalletAsync(speakingCredits: 0, mockExams: 1);
        await SeedTwoPublishedCardsAsync();

        // Would throw PaymentRequired under the old gate (0 < 2 speaking credits);
        // the mock-exam allowance alone must satisfy the pre-flight check.
        var exam = await _exams.CreateExamAsync(UserId, new CreateSpeakingExamRequest("ai", ProfessionId: "medicine"), default);
        Assert.Equal("intro", exam.State);
    }

    [Fact]
    public async Task CreateExam_NoMockExamCredit_AndInsufficientSpeakingCredits_StillBlocked()
    {
        await SeedWalletAsync(speakingCredits: 1, mockExams: 0);
        await SeedTwoPublishedCardsAsync();

        await Assert.ThrowsAsync<ApiException>(() =>
            _exams.CreateExamAsync(UserId, new CreateSpeakingExamRequest("ai", ProfessionId: "medicine"), default));
    }

    [Fact]
    public async Task LearnerProjection_NeverLeaksRoleplayerCard_OrCardType_InAnyPhase()
    {
        await SeedWalletAsync(speakingCredits: 5);
        await SeedTwoPublishedCardsAsync();
        var exam = await _exams.CreateExamAsync(UserId, new CreateSpeakingExamRequest("ai", ProfessionId: "medicine"), default);

        // Intro phase.
        AssertNoLeak(await _exams.GetExamForLearnerAsync(UserId, exam.ExamId, default));

        // Prep A phase (card revealed).
        var prepA = await _exams.FinishIntroAsync(UserId, exam.ExamId, default);
        AssertNoLeak(prepA);

        // Active A phase.
        var activeA = await _exams.StartCardAsync(UserId, exam.ExamId, default);
        AssertNoLeak(activeA);

        // Sanity: the candidate-side title IS present (proves projection ran).
        var json = JsonSerializer.Serialize(prepA);
        Assert.Contains("General Practice", json, StringComparison.Ordinal); // shared Setting on both cards (Card A/B picked at random)
    }

    [Fact]
    public async Task CreateExam_RefusesWhenWalletShortOfTwoCredits()
    {
        await SeedWalletAsync(speakingCredits: 1);
        await SeedTwoPublishedCardsAsync();

        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            _exams.CreateExamAsync(UserId, new CreateSpeakingExamRequest("ai", ProfessionId: "medicine"), default));
        Assert.Equal("speaking_exam_insufficient_credits", ex.ErrorCode);
    }

    [Fact]
    public async Task CreateExamForBooking_MakesLiveTutorExam_NoCredits_AndIsIdempotent()
    {
        await SeedTwoPublishedCardsAsync();
        var now = DateTimeOffset.UtcNow;
        var booking = new PrivateSpeakingBooking
        {
            Id = "psb-test-1",
            LearnerUserId = UserId,
            TutorProfileId = "tutor-1",
            Status = PrivateSpeakingBookingStatus.Confirmed,
            SessionStartUtc = now.AddDays(1),
            DurationMinutes = 30,
            ProfessionTrack = "Medicine",
            CreatedAt = now,
            UpdatedAt = now,
        };
        _db.PrivateSpeakingBookings.Add(booking);
        await _db.SaveChangesAsync();

        var exam = await _exams.CreateExamForBookingAsync(UserId, booking.Id, default);

        Assert.Equal("live_tutor", exam.Mode);
        Assert.Equal("intro", exam.State);
        var savedBooking = await _db.PrivateSpeakingBookings.FirstAsync(b => b.Id == booking.Id);
        Assert.Equal("exam", savedBooking.SessionFormat);
        Assert.Equal(exam.ExamId, savedBooking.ExamSessionId);

        // Idempotent — a second call returns the same exam, not a new one.
        var again = await _exams.CreateExamForBookingAsync(UserId, booking.Id, default);
        Assert.Equal(exam.ExamId, again.ExamId);
        Assert.Equal(1, await _db.SpeakingExamSessions.CountAsync());
    }

    // ── No AI for MOCK Speaking (2026-06-29 owner rule) ──────────────────────

    [Fact]
    public async Task CreateExam_WithMockSetId_InAiMode_IsRejected()
    {
        // A mock-set Speaking exam must be human-marked via a live-tutor booking.
        // AI mode is forbidden — the guard fires before card resolution / wallet,
        // so no seeding is needed.
        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            _exams.CreateExamAsync(UserId,
                new CreateSpeakingExamRequest("ai", MockSetId: "sms-anything", ProfessionId: "medicine"), default));
        Assert.Equal("SPEAKING_MOCK_REQUIRES_LIVE_TUTOR", ex.ErrorCode);
    }

    [Fact]
    public async Task CreateExam_WithMockSetId_InLiveTutorMode_Succeeds_AsHumanMarked()
    {
        var setId = await SeedPublishedMockSetAsync();

        var exam = await _exams.CreateExamAsync(UserId,
            new CreateSpeakingExamRequest("live_tutor", MockSetId: setId, BookingId: "psb-mock-1"), default);

        Assert.Equal("live_tutor", exam.Mode);
        Assert.Equal("intro", exam.State);
        var saved = await _db.SpeakingExamSessions.FirstAsync(e => e.Id == exam.ExamId);
        Assert.Equal(setId, saved.MockSetId);
    }

    [Fact]
    public async Task FinishIntro_OnMockSetExam_PropagatesMockSetId_AndLiveTutorMode_ToChildSession()
    {
        var setId = await SeedPublishedMockSetAsync();
        var exam = await _exams.CreateExamAsync(UserId,
            new CreateSpeakingExamRequest("live_tutor", MockSetId: setId, BookingId: "psb-mock-2"), default);

        var afterIntro = await _exams.FinishIntroAsync(UserId, exam.ExamId, default);

        Assert.Equal("prep_a", afterIntro.State);
        Assert.NotNull(afterIntro.CurrentSessionId);
        var childA = await _db.SpeakingSessions.FirstAsync(s => s.Id == afterIntro.CurrentSessionId);
        // The child self-identifies as a mock so the assessor (which loads only the
        // child) can skip AI, and inherits the human-marked LiveTutor mode.
        Assert.Equal(setId, childA.MockSetId);
        Assert.Equal(SpeakingSessionMode.LiveTutor, childA.Mode);
    }

    private static void AssertNoLeak(SpeakingExamDetail detail)
    {
        var json = JsonSerializer.Serialize(detail);
        Assert.DoesNotContain("patientBackground", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("patientTask", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("HiddenInformation", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("openingResponse", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("closingCue", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("cardType", json, StringComparison.OrdinalIgnoreCase);
        // The patient card's secret marker must never appear.
        Assert.DoesNotContain("SECRET-PATIENT", json, StringComparison.Ordinal);
    }

    // ── Fixtures ─────────────────────────────────────────────────────────

    private async Task SeedWalletAsync(int speakingCredits, int mockExams = 0)
    {
        var now = DateTimeOffset.UtcNow;
        _db.AiPackageCreditAccounts.Add(new AiPackageCreditAccount
        {
            Id = $"aipkg-{Guid.NewGuid():N}",
            UserId = UserId,
            SpeakingOnlyCredits = speakingCredits,
            MockExamsRemaining = mockExams,
            ExpiresAt = now.AddDays(90),
            CreatedAt = now,
            UpdatedAt = now,
        });
        await _db.SaveChangesAsync();
    }

    private async Task SeedTwoPublishedCardsAsync(int prepSeconds = 180, int discussionSeconds = 300)
    {
        var cardType = new SpeakingCardType
        {
            Id = $"sct-{Guid.NewGuid():N}",
            Name = "Examination Card",
            Description = "Hidden marking guidance",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        _db.SpeakingCardTypes.Add(cardType);

        await SeedCardAsync("A", cardType.Id, prepSeconds, discussionSeconds);
        await SeedCardAsync("B", cardType.Id, prepSeconds, discussionSeconds);
        await _db.SaveChangesAsync();
    }

    /// <summary>Seeds two published medicine cards and a published Mock Set that
    /// pairs them (by ContentItemId, the key ResolveCardsAsync matches on).
    /// Returns the mock-set id.</summary>
    private async Task<string> SeedPublishedMockSetAsync()
    {
        await SeedTwoPublishedCardsAsync();
        var contentIds = await _db.RolePlayCards.AsNoTracking()
            .Where(c => c.ProfessionId == "medicine")
            .Select(c => c.ContentItemId)
            .ToListAsync();

        var now = DateTimeOffset.UtcNow;
        var set = new SpeakingMockSet
        {
            Id = $"sms-{Guid.NewGuid():N}",
            ProfessionId = "medicine",
            Title = "Mock Set 1",
            RolePlay1ContentId = contentIds[0],
            RolePlay2ContentId = contentIds[1],
            Status = SpeakingMockSetStatus.Published,
            Difficulty = "exam",
            CreatedAt = now,
            UpdatedAt = now,
            PublishedAt = now,
        };
        _db.SpeakingMockSets.Add(set);
        await _db.SaveChangesAsync();
        return set.Id;
    }

    private async Task SeedCardAsync(string slot, string cardTypeId, int prepSeconds, int discussionSeconds)
    {
        var now = DateTimeOffset.UtcNow;
        var contentItemId = $"ci-{Guid.NewGuid():N}";
        _db.ContentItems.Add(new ContentItem
        {
            Id = contentItemId,
            ContentType = "speaking_roleplay",
            SubtestCode = "speaking",
            ProfessionId = "medicine",
            Title = $"Card {slot}",
            Difficulty = "exam",
            Status = ContentStatus.Published,
            PublishedRevisionId = $"{contentItemId}-r1",
            CreatedAt = now,
            UpdatedAt = now,
            PublishedAt = now,
            DetailJson = "{}",
            ModelAnswerJson = "{}",
        });

        var cardId = $"rpc-{Guid.NewGuid():N}";
        _db.RolePlayCards.Add(new RolePlayCard
        {
            Id = cardId,
            ContentItemId = contentItemId,
            ProfessionId = "medicine",
            ScenarioTitle = $"Scenario {slot}",
            Setting = "General Practice",
            CandidateRole = "Doctor",
            InterlocutorRole = "Patient",
            Background = $"Candidate {slot} background",
            Task1 = "Take a history",
            Task2 = "Explain the diagnosis",
            Task3 = "Advise on next steps",
            PrepTimeSeconds = prepSeconds,
            RolePlayTimeSeconds = discussionSeconds,
            PatientEmotion = "worried",
            CommunicationGoal = "Reassure",
            ClinicalTopic = "general",
            Difficulty = "exam",
            CriteriaFocusJson = "[]",
            Disclaimer = "Practice estimate only.",
            Status = ContentStatus.Published,
            CardTypeId = cardTypeId,
            DisplayCardNumber = slot == "A" ? 1 : 2,
            CreatedAt = now,
            UpdatedAt = now,
            PublishedAt = now,
        });

        _db.InterlocutorScripts.Add(new InterlocutorScript
        {
            Id = $"is-{Guid.NewGuid():N}",
            RolePlayCardId = cardId,
            OpeningResponse = "Doctor, my knee hurts.",
            HiddenInformation = "SECRET-PATIENT hidden detail",
            PatientBackground = "SECRET-PATIENT background paragraph",
            PatientTask1 = "SECRET-PATIENT explain symptoms",
            ResistanceLevel = ResistanceLevel.Low,
            ClosingCue = "Accept advice",
            EmotionalState = "anxious",
            LayLanguageTriggersJson = "[]",
            CreatedAt = now,
            UpdatedAt = now,
        });
    }
}
