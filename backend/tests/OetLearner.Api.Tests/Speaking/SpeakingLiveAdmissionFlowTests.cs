using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Billing;
using OetLearner.Api.Services.Speaking;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests.Speaking;

/// <summary>
/// Live AI Speaking admission control at its two real gates (owner decision 5 Oct 2026): AI exam finish-intro
/// and AI practice finish-warmup. The load-bearing promises: while the cap is full the learner is NOT started
/// (state unchanged, no clock), holds NO credit, and sees where they stand; the very call that finds a free place
/// holds the credit and starts the clock; an unfunded learner never queues; with no healthy live provider the
/// recorder fallback is never gated; Card B of an admitted exam is never gated.
/// </summary>
public sealed class SpeakingLiveAdmissionFlowTests : IAsyncLifetime
{
    private const string Learner1 = "adm-learner-1";
    private const string Learner2 = "adm-learner-2";
    // Seeded WITHOUT a credit wallet by the tests that need an account with no credits at all.
    private const string Learner3 = "adm-learner-3";

    private LearnerDbContext _db = default!;
    private MutableTimeProvider _clock = default!;
    private AiPackageCreditService _credits = default!;
    private LiveVoiceProviderProbeState _probe = default!;
    private IOptions<LiveVoiceOptions> _liveOptions = default!;
    private SpeakingLiveAdmissionService _gate = default!;
    private SpeakingExamService _exams = default!;
    private SpeakingSessionService _sessions = default!;

    public async Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase($"live-admission-flow-{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        _db = new LearnerDbContext(options);
        _clock = new MutableTimeProvider(DateTimeOffset.UtcNow);
        _credits = new AiPackageCreditService(_db, NullLogger<AiPackageCreditService>.Instance);
        _liveOptions = Options.Create(LiveVoiceTestKit.DefaultOptions());
        _probe = VerifiedProbe();
        _gate = new SpeakingLiveAdmissionService(
            _db,
            Options.Create(new SpeakingLiveAdmissionOptions { DefaultMaxConcurrent = 1, ClaimWindowSeconds = 30 }),
            _clock);
        _exams = NewExams(_probe);
        _sessions = new SpeakingSessionService(
            _db,
            aiPackageCreditService: _credits,
            liveVoiceProbe: _probe,
            liveVoiceOptions: _liveOptions,
            admission: _gate);

        SeedLearner(Learner1);
        SeedLearner(Learner2);
        await SeedWalletAsync(Learner1, 6);
        await SeedWalletAsync(Learner2, 6);
    }

    public Task DisposeAsync()
    {
        _db.Dispose();
        return Task.CompletedTask;
    }

    // ── Exam: finish-intro ───────────────────────────────────────────────────

    [Fact]
    public async Task AnExamOverTheCap_WaitsInIntro_HoldingNoCreditAndStartingNoClock()
    {
        await SeedTwoPublishedCardsAsync();
        var first = await CreateExamAsync(Learner1);
        var second = await CreateExamAsync(Learner2);

        var admitted = await _exams.FinishIntroAsync(Learner1, first, default);
        Assert.Equal("prep_a", admitted.State);
        Assert.Null(admitted.Admission);

        var waiting = await _exams.FinishIntroAsync(Learner2, second, default);

        Assert.Equal("intro", waiting.State);
        Assert.NotNull(waiting.Admission);
        Assert.Equal("waiting", waiting.Admission!.Status);
        Assert.Equal(1, waiting.Admission.Position);
        Assert.Equal(1, waiting.Admission.QueueLength);
        Assert.True(waiting.Admission.EstimatedWaitSeconds > 0);
        // No clock: still the unscored intro, no card revealed, no stage end.
        Assert.Null(waiting.Clock.StageEndsAt);
        Assert.Null(waiting.CurrentCard);
        Assert.Null(waiting.CurrentSessionId);
        var row = await _db.SpeakingExamSessions.AsNoTracking().SingleAsync(e => e.Id == second);
        Assert.Equal(SpeakingExamState.Intro, row.State);
        Assert.Null(row.IntroEndedAt);
        Assert.Null(row.PrepAStartedAt);
        Assert.Null(row.CreditARefId);
        // No credit held, no child session, no Card B.
        Assert.Equal(6, (await _credits.GetSnapshotAsync(Learner2, 0, default)).SpeakingOnlyCredits);
        Assert.False(await _db.SpeakingSessions.AnyAsync(s => s.UserId == Learner2));

        // A plain read of the waiting exam shows the same place and still starts nothing.
        var read = await _exams.GetExamForLearnerAsync(Learner2, second, default);
        Assert.Equal("intro", read.State);
        Assert.Equal(1, read.Admission!.Position);
    }

    [Fact]
    public async Task TheCallThatFindsAFreePlace_HoldsTheCreditAndStartsTheClockAtomically()
    {
        await SeedTwoPublishedCardsAsync();
        var first = await CreateExamAsync(Learner1);
        var second = await CreateExamAsync(Learner2);
        await _exams.FinishIntroAsync(Learner1, first, default);
        Assert.NotNull((await _exams.FinishIntroAsync(Learner2, second, default)).Admission);

        // Learner 1 abandons the exam; once the claim window is over the place is free.
        await _exams.CancelAsync(Learner1, first, default);
        _clock.Advance(TimeSpan.FromSeconds(31));
        var started = await _exams.FinishIntroAsync(Learner2, second, default);

        Assert.Equal("prep_a", started.State);
        Assert.Null(started.Admission);
        Assert.NotNull(started.CurrentSessionId);
        Assert.NotNull(started.Clock.StageEndsAt);
        var row = await _db.SpeakingExamSessions.AsNoTracking().SingleAsync(e => e.Id == second);
        Assert.NotNull(row.PrepAStartedAt);
        Assert.NotNull(row.CreditARefId);
        // Exactly one hold of 2 credits, taken by this call.
        Assert.Equal(4, (await _credits.GetSnapshotAsync(Learner2, 0, default)).SpeakingOnlyCredits);
    }

    [Fact]
    public async Task AnAdmittedExamKeepsItsPlaceThroughBothCards_AndTheNextLearnerKeepsWaiting()
    {
        await SeedTwoPublishedCardsAsync();
        var first = await CreateExamAsync(Learner1);
        var second = await CreateExamAsync(Learner2);
        await _exams.FinishIntroAsync(Learner1, first, default);

        // Card A closes and Card B is revealed: no second admission, the exam still holds its one place.
        var tracked = await _db.SpeakingExamSessions.FirstAsync(e => e.Id == first);
        await _exams.AdvanceAsync(tracked, DateTimeOffset.UtcNow.AddMinutes(9), default);
        await _db.SaveChangesAsync();
        Assert.Equal(SpeakingExamState.PrepB, tracked.State);
        Assert.NotNull(tracked.CreditBRefId);
        Assert.Equal(1, await _db.SpeakingLiveAdmissions.CountAsync(a => a.SubjectKind == SpeakingLiveAdmissionKinds.Exam
            && a.State == SpeakingLiveAdmissionState.Admitted));

        _clock.Advance(TimeSpan.FromSeconds(31));
        Assert.NotNull((await _exams.FinishIntroAsync(Learner2, second, default)).Admission);
    }

    [Fact]
    public async Task AnUnfundedLearner_FailsFastAndNeverQueues()
    {
        await SeedTwoPublishedCardsAsync();
        var first = await CreateExamAsync(Learner1);
        var second = await CreateExamAsync(Learner2);
        await _exams.FinishIntroAsync(Learner1, first, default);

        // The balance dropped after the exam was created.
        (await _db.AiPackageCreditAccounts.SingleAsync(a => a.UserId == Learner2)).SpeakingOnlyCredits = 1;
        await _db.SaveChangesAsync();

        var refused = await Assert.ThrowsAsync<ApiException>(() => _exams.FinishIntroAsync(Learner2, second, default));

        Assert.Equal(402, refused.StatusCode);
        Assert.False(await _db.SpeakingLiveAdmissions.AnyAsync(a => a.SubjectId == second));
    }

    [Fact]
    public async Task WithNoHealthyLiveProvider_TheRecorderFallbackIsNeverGated()
    {
        await SeedTwoPublishedCardsAsync();
        var exams = NewExams(new LiveVoiceProviderProbeState(_clock));
        var first = await CreateExamAsync(Learner1, exams);
        var second = await CreateExamAsync(Learner2, exams);

        var one = await exams.FinishIntroAsync(Learner1, first, default);
        var two = await exams.FinishIntroAsync(Learner2, second, default);

        Assert.Equal("prep_a", one.State);
        Assert.Equal("prep_a", two.State);
        Assert.False(one.LiveVoiceAvailable);
        Assert.Null(two.Admission);
        Assert.Empty(await _db.SpeakingLiveAdmissions.ToListAsync());
    }

    [Fact]
    public async Task WithoutTheAdmissionService_FinishIntroBehavesAsBefore()
    {
        await SeedTwoPublishedCardsAsync();
        var plain = new SpeakingExamService(_db, null!, NullLogger<SpeakingExamService>.Instance, _credits);
        var first = await CreateExamAsync(Learner1, plain);
        var second = await CreateExamAsync(Learner2, plain);

        Assert.Equal("prep_a", (await plain.FinishIntroAsync(Learner1, first, default)).State);
        Assert.Equal("prep_a", (await plain.FinishIntroAsync(Learner2, second, default)).State);
    }

    [Fact]
    public async Task AnExamAccountWithNoCreditWalletAtAll_FailsFastAndNeverQueues_WhereHoldsGoThroughReservations()
    {
        await SeedTwoPublishedCardsAsync();
        var first = await CreateExamAsync(Learner1);
        await _exams.FinishIntroAsync(Learner1, first, default);
        // A brand-new account has no package wallet, so its Card A hold could only be refused (402) at the end of the line.
        SeedLearner(Learner3);
        var exams = NewExams(_probe, new OetLearner.Api.Services.Ai.AiCreditReservationService(_db, _credits, _clock));
        var third = await CreateExamAsync(Learner3, exams);

        var refused = await Assert.ThrowsAsync<ApiException>(() => exams.FinishIntroAsync(Learner3, third, default));

        Assert.Equal(402, refused.StatusCode);
        Assert.Equal("ai_credits_insufficient", refused.ErrorCode);
        Assert.False(await _db.SpeakingLiveAdmissions.AnyAsync(a => a.SubjectId == third));
    }

    [Fact]
    public async Task AFailedCardAHold_GivesTheExamPlaceBackAtOnce_SoTheNextLearnerStartsWithoutWaitingForTheClaimWindow()
    {
        await SeedTwoPublishedCardsAsync();
        var refusing = NewExams(_probe, new RefusingReservations());
        var first = await CreateExamAsync(Learner1, refusing);
        var second = await CreateExamAsync(Learner2);

        // The place is taken, then the Card A hold is refused: the exam stays in Intro and the place is freed now.
        var refused = await Assert.ThrowsAsync<ApiException>(() => refusing.FinishIntroAsync(Learner1, first, default));
        Assert.Equal(402, refused.StatusCode);
        Assert.Equal(SpeakingExamState.Intro, (await _db.SpeakingExamSessions.AsNoTracking().SingleAsync(e => e.Id == first)).State);
        Assert.Equal(
            SpeakingLiveAdmissionState.Released,
            (await _db.SpeakingLiveAdmissions.AsNoTracking().SingleAsync(a => a.SubjectId == first)).State);

        // No clock advance: the 30 s claim window is nowhere near over, and the cap is one.
        var started = await _exams.FinishIntroAsync(Learner2, second, default);
        Assert.Equal("prep_a", started.State);
        Assert.Null(started.Admission);
    }

    [Fact]
    public async Task CancellingAnAdmittedExam_FreesItsPlaceAtOnce()
    {
        await SeedTwoPublishedCardsAsync();
        var first = await CreateExamAsync(Learner1);
        var second = await CreateExamAsync(Learner2);
        await _exams.FinishIntroAsync(Learner1, first, default);
        Assert.NotNull((await _exams.FinishIntroAsync(Learner2, second, default)).Admission);

        await _exams.CancelAsync(Learner1, first, default);

        // The claim window of the cancelled exam is NOT over (no clock advance): its place is free anyway.
        var started = await _exams.FinishIntroAsync(Learner2, second, default);
        Assert.Equal("prep_a", started.State);
    }

    [Fact]
    public async Task LeavingTheExamQueue_ReleasesTheWaitingPlaceAtOnce_AndNeverCancelsTheExam()
    {
        await SeedTwoPublishedCardsAsync();
        var first = await CreateExamAsync(Learner1);
        var second = await CreateExamAsync(Learner2);
        await _exams.FinishIntroAsync(Learner1, first, default);
        Assert.NotNull((await _exams.FinishIntroAsync(Learner2, second, default)).Admission);

        await _exams.LeaveAdmissionQueueAsync(Learner2, second, default);

        Assert.Equal(
            SpeakingLiveAdmissionState.Released,
            (await _db.SpeakingLiveAdmissions.AsNoTracking().SingleAsync(a => a.SubjectId == second)).State);
        var read = await _exams.GetExamForLearnerAsync(Learner2, second, default);
        Assert.Equal("intro", read.State);
        Assert.Null(read.Admission);
        // Someone else's exam is not found, and an admitted place is not touched by a leave.
        await Assert.ThrowsAsync<ApiException>(() => _exams.LeaveAdmissionQueueAsync(Learner1, second, default));
        await _exams.LeaveAdmissionQueueAsync(Learner1, first, default);
        Assert.Equal(
            SpeakingLiveAdmissionState.Admitted,
            (await _db.SpeakingLiveAdmissions.AsNoTracking().SingleAsync(a => a.SubjectId == first)).State);
    }

    // ── Practice: finish-warmup ──────────────────────────────────────────────

    [Fact]
    public async Task APracticeCardOverTheCap_StaysInWarmup_WithoutAHoldOrAClock()
    {
        var cardId = await SeedPracticeCardAsync();
        var first = await SeedWarmUpSessionAsync(Learner1, cardId);
        var second = await SeedWarmUpSessionAsync(Learner2, cardId);

        var admitted = await _sessions.FinishWarmupAsync(Learner1, first, default);
        Assert.Equal(SpeakingSessionStates.Prep, admitted.State);
        Assert.Null(admitted.Admission);

        var waiting = await _sessions.FinishWarmupAsync(Learner2, second, default);

        Assert.Equal(SpeakingSessionStates.WarmUp, waiting.State);
        Assert.NotNull(waiting.Admission);
        Assert.Equal("waiting", waiting.Admission!.Status);
        Assert.Equal(1, waiting.Admission.Position);
        Assert.Null(waiting.WarmupEndedAt);
        Assert.Null(waiting.PrepStartedAt);
        var row = await _db.SpeakingSessions.AsNoTracking().SingleAsync(s => s.Id == second);
        Assert.Equal(SpeakingSessionState.WarmUp, row.State);
        Assert.Null(row.PrepStartedAt);
        Assert.Equal(6, (await _credits.GetSnapshotAsync(Learner2, 0, default)).SpeakingOnlyCredits);

        // A plain read of the waiting card shows the same place.
        var read = await _sessions.GetSessionForLearnerAsync(Learner2, second, default);
        Assert.Equal(1, read.Admission!.Position);
        // And the admitted card never carries one.
        Assert.Null((await _sessions.GetSessionForLearnerAsync(Learner1, first, default)).Admission);
    }

    [Fact]
    public async Task AWaitingPracticeCardStartsOnceTheRunningOneEnds_NotBefore()
    {
        var cardId = await SeedPracticeCardAsync();
        var first = await SeedWarmUpSessionAsync(Learner1, cardId);
        var second = await SeedWarmUpSessionAsync(Learner2, cardId);
        await _sessions.FinishWarmupAsync(Learner1, first, default);
        Assert.NotNull((await _sessions.FinishWarmupAsync(Learner2, second, default)).Admission);

        // Learner 1 is still preparing, long after the claim window: the place stays taken.
        _clock.Advance(TimeSpan.FromSeconds(31));
        Assert.NotNull((await _sessions.FinishWarmupAsync(Learner2, second, default)).Admission);

        // The role-play ends (any terminal path); the next call from learner 2 admits and holds atomically.
        var running = await _db.SpeakingSessions.SingleAsync(s => s.Id == first);
        running.State = SpeakingSessionState.Finished;
        await _db.SaveChangesAsync();
        var started = await _sessions.FinishWarmupAsync(Learner2, second, default);

        Assert.Equal(SpeakingSessionStates.Prep, started.State);
        Assert.Null(started.Admission);
        Assert.NotNull(started.PrepStartedAt);
        Assert.Equal(4, (await _credits.GetSnapshotAsync(Learner2, 0, default)).SpeakingOnlyCredits);
    }

    [Fact]
    public async Task APracticeCardWithNoHealthyLiveProvider_IsNeverGated()
    {
        var cardId = await SeedPracticeCardAsync();
        var first = await SeedWarmUpSessionAsync(Learner1, cardId);
        var second = await SeedWarmUpSessionAsync(Learner2, cardId);
        var sessions = new SpeakingSessionService(
            _db,
            aiPackageCreditService: _credits,
            liveVoiceProbe: new LiveVoiceProviderProbeState(_clock),
            liveVoiceOptions: _liveOptions,
            admission: _gate);

        Assert.Equal(SpeakingSessionStates.Prep, (await sessions.FinishWarmupAsync(Learner1, first, default)).State);
        Assert.Equal(SpeakingSessionStates.Prep, (await sessions.FinishWarmupAsync(Learner2, second, default)).State);
        Assert.Empty(await _db.SpeakingLiveAdmissions.ToListAsync());
    }

    [Fact]
    public async Task AnUnfundedPracticeLearner_FailsFastAndNeverQueues()
    {
        var cardId = await SeedPracticeCardAsync();
        var first = await SeedWarmUpSessionAsync(Learner1, cardId);
        await _sessions.FinishWarmupAsync(Learner1, first, default);
        // A single stranded credit cannot pay for a 2-credit card.
        (await _db.AiPackageCreditAccounts.SingleAsync(a => a.UserId == Learner2)).SpeakingOnlyCredits = 1;
        await _db.SaveChangesAsync();
        var second = await SeedWarmUpSessionAsync(Learner2, cardId);

        // The cap is full, yet the learner is refused at once instead of queueing for a place it cannot pay for.
        var refused = await Assert.ThrowsAsync<ApiException>(() => _sessions.FinishWarmupAsync(Learner2, second, default));

        Assert.Equal(402, refused.StatusCode);
        Assert.False(await _db.SpeakingLiveAdmissions.AnyAsync(a => a.SubjectId == second));
        Assert.Equal(SpeakingSessionState.WarmUp, (await _db.SpeakingSessions.AsNoTracking().SingleAsync(s => s.Id == second)).State);
    }

    [Fact]
    public async Task AnUnfundedPracticeLearner_IsRefusedBeforeAFreeSlot_WhereHoldsGoThroughReservations()
    {
        var cardId = await SeedPracticeCardAsync();
        SeedLearner(Learner3);
        var sessions = NewSessions(new OetLearner.Api.Services.Ai.AiCreditReservationService(_db, _credits, _clock));
        var session = await SeedWarmUpSessionAsync(Learner3, cardId);

        // A free place exists (cap one, nobody running) but the account has no credits at all: 402, no row, no hold.
        var refused = await Assert.ThrowsAsync<ApiException>(() => sessions.FinishWarmupAsync(Learner3, session, default));

        Assert.Equal(402, refused.StatusCode);
        Assert.Equal("ai_credits_insufficient", refused.ErrorCode);
        Assert.Empty(await _db.SpeakingLiveAdmissions.ToListAsync());
        Assert.Empty(await _db.AiCreditReservations.ToListAsync());
    }

    [Fact]
    public async Task AFundedPracticeLearner_IsStillAdmitted_WhereHoldsGoThroughReservations()
    {
        // The pre-check mirrors the hold, so a learner the hold would accept is never refused by it.
        var cardId = await SeedPracticeCardAsync();
        var session = await SeedWarmUpSessionAsync(Learner1, cardId);
        var sessions = NewSessions(new OetLearner.Api.Services.Ai.AiCreditReservationService(_db, _credits, _clock));

        var started = await sessions.FinishWarmupAsync(Learner1, session, default);

        Assert.Equal(SpeakingSessionStates.Prep, started.State);
        Assert.Null(started.Admission);
        Assert.Equal(4, (await _credits.GetSnapshotAsync(Learner1, 0, default)).SpeakingOnlyCredits);
    }

    [Fact]
    public async Task AFailedCreditHold_GivesThePracticePlaceBackAtOnce()
    {
        var cardId = await SeedPracticeCardAsync();
        var first = await SeedWarmUpSessionAsync(Learner1, cardId);
        var second = await SeedWarmUpSessionAsync(Learner2, cardId);
        var refusing = NewSessions(new RefusingReservations());

        // The place is taken, then the hold is refused: the card stays in warm-up and the place is freed now.
        var refused = await Assert.ThrowsAsync<ApiException>(() => refusing.FinishWarmupAsync(Learner1, first, default));
        Assert.Equal(402, refused.StatusCode);
        Assert.Equal(SpeakingSessionState.WarmUp, (await _db.SpeakingSessions.AsNoTracking().SingleAsync(s => s.Id == first)).State);
        Assert.Equal(
            SpeakingLiveAdmissionState.Released,
            (await _db.SpeakingLiveAdmissions.AsNoTracking().SingleAsync(a => a.SubjectId == first)).State);

        // No clock advance: the claim window is not over, and the cap is one. The next learner starts at once.
        var started = await _sessions.FinishWarmupAsync(Learner2, second, default);
        Assert.Equal(SpeakingSessionStates.Prep, started.State);
        Assert.Null(started.Admission);
    }

    [Fact]
    public async Task ALearnerHoldsOneLivePlace_ASecondPracticeCardIsRefusedWithAClearMessageAndStartsNothing()
    {
        var cardId = await SeedPracticeCardAsync();
        var first = await SeedWarmUpSessionAsync(Learner1, cardId);
        var second = await SeedWarmUpSessionAsync(Learner1, cardId);
        Assert.Equal(SpeakingSessionStates.Prep, (await _sessions.FinishWarmupAsync(Learner1, first, default)).State);

        var refused = await Assert.ThrowsAsync<ApiException>(() => _sessions.FinishWarmupAsync(Learner1, second, default));

        Assert.Equal(409, refused.StatusCode);
        Assert.Equal("speaking_live_session_active", refused.ErrorCode);
        Assert.Equal(SpeakingSessionState.WarmUp, (await _db.SpeakingSessions.AsNoTracking().SingleAsync(s => s.Id == second)).State);
        Assert.False(await _db.SpeakingLiveAdmissions.AnyAsync(a => a.SubjectId == second));
        // Only the first card's 2 credits are held.
        Assert.Equal(4, (await _credits.GetSnapshotAsync(Learner1, 0, default)).SpeakingOnlyCredits);
    }

    [Fact]
    public async Task LeavingThePracticeQueue_ReleasesTheWaitingPlaceAtOnce_AndTheCardStaysInWarmup()
    {
        var cardId = await SeedPracticeCardAsync();
        var first = await SeedWarmUpSessionAsync(Learner1, cardId);
        var second = await SeedWarmUpSessionAsync(Learner2, cardId);
        await _sessions.FinishWarmupAsync(Learner1, first, default);
        Assert.NotNull((await _sessions.FinishWarmupAsync(Learner2, second, default)).Admission);

        await _sessions.LeaveAdmissionQueueAsync(Learner2, second, default);

        Assert.Equal(
            SpeakingLiveAdmissionState.Released,
            (await _db.SpeakingLiveAdmissions.AsNoTracking().SingleAsync(a => a.SubjectId == second)).State);
        var read = await _sessions.GetSessionForLearnerAsync(Learner2, second, default);
        Assert.Equal(SpeakingSessionStates.WarmUp, read.State);
        Assert.Null(read.Admission);
        await Assert.ThrowsAsync<ApiException>(() => _sessions.LeaveAdmissionQueueAsync(Learner1, second, default));
    }

    // ── The sweeper's housekeeping pass ──────────────────────────────────────

    [Fact]
    public async Task TheSweeper_ExpiresAbandonedWaiters_AndIsANoOpWithoutTheAdmissionService()
    {
        var name = $"live-admission-sweep-{Guid.NewGuid():N}";
        var opts = new SpeakingLiveAdmissionOptions { DefaultMaxConcurrent = 1, WaiterHeartbeatSeconds = 60 };
        var gated = BuildSweeperServices(name, opts, withAdmission: true);
        var ungated = BuildSweeperServices(name, opts, withAdmission: false);

        await using (var scope = gated.CreateAsyncScope())
        {
            var gate = scope.ServiceProvider.GetRequiredService<SpeakingLiveAdmissionService>();
            await gate.AdmitOrQueueAsync("u-a", SpeakingLiveAdmissionKinds.Exam, "exam-a", true, default);
            await gate.AdmitOrQueueAsync("u-b", SpeakingLiveAdmissionKinds.Exam, "exam-b", true, default);
        }

        _clock.Advance(TimeSpan.FromSeconds(61));
        Assert.Equal(0, await NewWorker(ungated).SweepAdmissionsAsync(default));
        Assert.Equal(1, await NewWorker(gated).SweepAdmissionsAsync(default));

        await using var read = gated.CreateAsyncScope();
        var db = read.ServiceProvider.GetRequiredService<LearnerDbContext>();
        Assert.Equal(SpeakingLiveAdmissionState.Expired,
            (await db.SpeakingLiveAdmissions.SingleAsync(a => a.SubjectId == "exam-b")).State);
    }

    // ── Fixtures ─────────────────────────────────────────────────────────────

    private LiveVoiceProviderProbeState VerifiedProbe()
    {
        var probe = new LiveVoiceProviderProbeState(_clock);
        probe.Set(LiveVoiceProviders.OpenAi, true, null);
        probe.Set(LiveVoiceProviders.Gemini, true, null);
        return probe;
    }

    private SpeakingExamService NewExams(
        LiveVoiceProviderProbeState probe,
        OetLearner.Api.Services.Ai.IAiCreditReservationService? reservations = null)
        => new(
            _db,
            null!,
            NullLogger<SpeakingExamService>.Instance,
            _credits,
            creditReservations: reservations,
            liveVoiceProbe: probe,
            liveVoiceOptions: _liveOptions,
            admission: _gate);

    private SpeakingSessionService NewSessions(OetLearner.Api.Services.Ai.IAiCreditReservationService? reservations)
        => new(
            _db,
            aiPackageCreditService: _credits,
            creditReservations: reservations,
            liveVoiceProbe: _probe,
            liveVoiceOptions: _liveOptions,
            admission: _gate);

    /// <summary>A credit hold that is always refused (402), to prove a place taken for a start that then fails is
    /// given back at once. Only the speaking hold is ever reached.</summary>
    private sealed class RefusingReservations : OetLearner.Api.Services.Ai.IAiCreditReservationService
    {
        public Task<OetLearner.Api.Services.Ai.AiCreditReservationTicket> ReserveWritingAsync(
            string userId, string operationId, string businessReference, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<OetLearner.Api.Services.Ai.AiCreditReservationTicket> ReserveSpeakingAsync(
            string userId, string operationId, string businessReference, CancellationToken ct)
            => throw ApiException.PaymentRequired("ai_credits_insufficient", "The hold was refused.");

        public Task CommitAsync(string reservationId, CancellationToken ct) => Task.CompletedTask;

        public Task CommitByBusinessReferenceAsync(string businessReference, CancellationToken ct) => Task.CompletedTask;

        public Task ReleaseAsync(string reservationId, CancellationToken ct) => Task.CompletedTask;
    }

    private async Task<string> CreateExamAsync(string userId, SpeakingExamService? exams = null)
        => (await (exams ?? _exams).CreateExamAsync(
            userId, new CreateSpeakingExamRequest("ai", ProfessionId: "medicine"), default)).ExamId;

    private ServiceProvider BuildSweeperServices(string databaseName, SpeakingLiveAdmissionOptions options, bool withAdmission)
    {
        var services = new ServiceCollection();
        services.AddDbContext<LearnerDbContext>(o => o
            .UseInMemoryDatabase(databaseName)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
        if (withAdmission)
        {
            services.AddScoped(sp => new SpeakingLiveAdmissionService(
                sp.GetRequiredService<LearnerDbContext>(), Options.Create(options), _clock));
        }
        return services.BuildServiceProvider(validateScopes: true);
    }

    private static SpeakingExamAutoAdvanceWorker NewWorker(ServiceProvider provider)
        => new(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<SpeakingExamAutoAdvanceWorker>.Instance);

    private void SeedLearner(string userId)
    {
        _db.Users.Add(new LearnerUser
        {
            Id = userId,
            DisplayName = userId,
            Email = $"{userId}@example.test",
            ActiveProfessionId = "medicine",
            AccountStatus = "active",
            CreatedAt = DateTimeOffset.UtcNow,
            LastActiveAt = DateTimeOffset.UtcNow,
        });
        _db.SaveChanges();
    }

    private async Task SeedWalletAsync(string userId, int speakingCredits)
    {
        var now = DateTimeOffset.UtcNow;
        _db.AiPackageCreditAccounts.Add(new AiPackageCreditAccount
        {
            Id = $"aipkg-{Guid.NewGuid():N}",
            UserId = userId,
            SpeakingOnlyCredits = speakingCredits,
            ExpiresAt = now.AddDays(90),
            CreatedAt = now,
            UpdatedAt = now,
        });
        await _db.SaveChangesAsync();
    }

    private async Task SeedTwoPublishedCardsAsync()
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
        SeedExamCard("A", cardType.Id);
        SeedExamCard("B", cardType.Id);
        await _db.SaveChangesAsync();
    }

    private void SeedExamCard(string slot, string cardTypeId)
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
            PrepTimeSeconds = 180,
            RolePlayTimeSeconds = 300,
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
            NeedsOwnerInput = false,
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

    private async Task<string> SeedPracticeCardAsync()
    {
        var contentItemId = $"ci-{Guid.NewGuid():N}";
        _db.ContentItems.Add(new ContentItem
        {
            Id = contentItemId,
            ContentType = "speaking_role_play",
            ProfessionId = "medicine",
            SubtestCode = "speaking",
            Title = "Admission gate practice card",
            Difficulty = "core",
            Status = ContentStatus.Published,
            PublishedRevisionId = $"rev-{Guid.NewGuid():N}",
        });

        var cardId = $"card-{Guid.NewGuid():N}";
        _db.RolePlayCards.Add(new RolePlayCard
        {
            Id = cardId,
            ContentItemId = contentItemId,
            ScenarioTitle = "Admission gate practice card",
            Setting = "Ward",
            CandidateRole = "Doctor",
            PatientEmotion = "neutral",
            CommunicationGoal = "Inform",
            ClinicalTopic = "general",
            Difficulty = "core",
            CriteriaFocusJson = "[]",
            Status = ContentStatus.Published,
            PrepTimeSeconds = 180,
            RolePlayTimeSeconds = 300,
        });
        await _db.SaveChangesAsync();
        return cardId;
    }

    private async Task<string> SeedWarmUpSessionAsync(string userId, string cardId)
    {
        var sessionId = $"sps_{Guid.NewGuid():N}";
        var now = DateTimeOffset.UtcNow;
        _db.SpeakingSessions.Add(new SpeakingSession
        {
            Id = sessionId,
            UserId = userId,
            RolePlayCardId = cardId,
            Mode = SpeakingSessionMode.AiSelfPractice,
            State = SpeakingSessionState.WarmUp,
            WarmupStartedAt = now.AddMinutes(-1),
            CreatedAt = now,
            UpdatedAt = now,
        });
        await _db.SaveChangesAsync();
        return sessionId;
    }
}
