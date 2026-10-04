using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Billing;
using OetLearner.Api.Services.FreeSamples;
using OetLearner.Api.Services.Speaking;
using static OetLearner.Api.Tests.FreeSamples.FreeSampleServiceTests;
// Both namespaces declare `AiCreditReservationService`; the Ai one holds the speaking hold.
using AiCreditReservationService = OetLearner.Api.Services.Ai.AiCreditReservationService;

namespace OetLearner.Api.Tests.FreeSamples;

/// <summary>
/// Free sample retry contract: Writing allows TWO successful AI-graded results
/// and Speaking allows ONE, both on the SAME pinned item of
/// their own profession. Only a produced result counts (start/exit, failed or
/// abandoned uses never do), a third is refused, a profession change neither
/// resets nor moves the allowance, and two racing binds for the last slot
/// resolve to exactly one winner. Speaking's free uses run on the shared
/// session engine at zero credits; Writing's second is the revision.
/// </summary>
public sealed class FreeSampleRetryPolicyTests
{
    private static LearnerDbContext NewDb(string? name = null, InMemoryDatabaseRoot? root = null, IInterceptor? interceptor = null)
    {
        var builder = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(name ?? Guid.NewGuid().ToString("N"), root ?? new InMemoryDatabaseRoot())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning));
        if (interceptor is not null) builder.AddInterceptors(interceptor);
        return new LearnerDbContext(builder.Options);
    }

    private static async Task<Guid> SeedRevisionAsync(LearnerDbContext db, string userId, Guid scenarioId, Guid originalId, string status)
    {
        var id = await SeedSubmissionAsync(db, userId, scenarioId, status);
        var row = await db.WritingSubmissions.SingleAsync(s => s.Id == id);
        row.IsRevision = true;
        row.OriginalSubmissionId = originalId;
        await db.SaveChangesAsync();
        return id;
    }

    private static async Task SetStatusAsync(LearnerDbContext db, Guid submissionId, string status)
    {
        (await db.WritingSubmissions.SingleAsync(s => s.Id == submissionId)).Status = status;
        await db.SaveChangesAsync();
    }

    // ── Writing: the two results, the revise path, the refused third ─────────

    [Fact]
    public async Task Writing_ExactlyTwoResults_TheSecondIsTheRevision_AndAThirdIsRefused()
    {
        await using var db = NewDb();
        await EnableAsync(db);
        var scenario = await SeedScenarioAsync(db, "medicine", "Alpha", difficulty: 1);
        var content = scenario.ToString("D");
        await SeedLearnerAsync(db, "u1", "medicine");
        var svc = new FreeSampleService(db);

        var fresh = Assert.Single(await svc.ListAsync("u1", "writing", default));
        Assert.Equal(FreeSampleService.StateAvailable, fresh.State);
        Assert.Equal($"/writing/practice/session/{content}", fresh.Route);
        Assert.Equal((2, 0, 2), (fresh.Limit, fresh.SuccessfulCount, fresh.Remaining));
        Assert.Null(fresh.LastResultRoute);
        Assert.Null(fresh.LastSubmissionId);

        // 1st result: the original letter.
        var original = await SeedSubmissionAsync(db, "u1", scenario, WritingSubmissionStatuses.Grading);
        Assert.True(await svc.TryClaimAsync("u1", "writing", content, FreeSampleUse.KindWritingSubmission, original.ToString("N"), default));
        await SetStatusAsync(db, original, WritingSubmissionStatuses.Graded);

        var retry = Assert.Single(await svc.ListAsync("u1", "writing", default));
        Assert.Equal(FreeSampleService.StateRetryAvailable, retry.State);
        Assert.Equal((2, 1, 1), (retry.Limit, retry.SuccessfulCount, retry.Remaining));
        Assert.Equal(original.ToString("D"), retry.LastSubmissionId);
        Assert.Equal($"/writing/submissions/{original:D}/revise", retry.Route);
        Assert.Equal($"/writing/submissions/{original:D}/results", retry.LastResultRoute);

        // 2nd result: Revise & Resubmit of the same letter is free (the grading
        // pipeline passes revisions through TryClaimAsync too).
        var revision = await SeedRevisionAsync(db, "u1", scenario, original, WritingSubmissionStatuses.Grading);
        Assert.True(await svc.TryClaimAsync("u1", "writing", content, FreeSampleUse.KindWritingSubmission, revision.ToString("N"), default));
        Assert.True(await svc.IsFreeAttemptAsync("u1", "writing", revision.ToString("N"), default));
        await SetStatusAsync(db, revision, WritingSubmissionStatuses.Graded);

        var done = Assert.Single(await svc.ListAsync("u1", "writing", default));
        Assert.Equal(FreeSampleService.StateCompleted, done.State);
        Assert.Equal((2, 2, 0), (done.Limit, done.SuccessfulCount, done.Remaining));
        Assert.Null(done.Route);
        Assert.Equal(revision.ToString("D"), done.LastSubmissionId);

        // 3rd: a further revision (or a fresh letter) is never free.
        var third = await SeedRevisionAsync(db, "u1", scenario, revision, WritingSubmissionStatuses.Grading);
        Assert.False(await svc.TryClaimAsync("u1", "writing", content, FreeSampleUse.KindWritingSubmission, third.ToString("N"), default));
        Assert.False(await svc.IsFreeAttemptAsync("u1", "writing", third.ToString("N"), default));
        Assert.False(await svc.IsOfferedAsync("u1", "writing", content, default));
        // A graded use stays free on re-entry (idempotent re-reads never flip it).
        Assert.True(await svc.IsFreeAttemptAsync("u1", "writing", revision.ToString("N"), default));
        Assert.Equal(2, await db.FreeSampleUses.CountAsync());
    }

    [Fact]
    public async Task Writing_ARetriedFailedUse_CanNeverBecomeAThirdResult()
    {
        await using var db = NewDb();
        await EnableAsync(db);
        var scenario = await SeedScenarioAsync(db, "medicine", "Alpha", difficulty: 1);
        var content = scenario.ToString("D");
        await SeedLearnerAsync(db, "u1", "medicine");
        var svc = new FreeSampleService(db);

        var failed = await SeedSubmissionAsync(db, "u1", scenario, WritingSubmissionStatuses.Grading);
        Assert.True(await svc.TryClaimAsync("u1", "writing", content, FreeSampleUse.KindWritingSubmission, failed.ToString("N"), default));
        await SetStatusAsync(db, failed, WritingSubmissionStatuses.Failed);
        foreach (var _ in Enumerable.Range(0, 2))
        {
            var ok = await SeedSubmissionAsync(db, "u1", scenario, WritingSubmissionStatuses.Grading);
            Assert.True(await svc.TryClaimAsync("u1", "writing", content, FreeSampleUse.KindWritingSubmission, ok.ToString("N"), default));
            await SetStatusAsync(db, ok, WritingSubmissionStatuses.Graded);
        }

        // RetryGradeAsync re-enters the failed submission's grading: not free any more.
        await SetStatusAsync(db, failed, WritingSubmissionStatuses.Grading);
        Assert.False(await svc.TryClaimAsync("u1", "writing", content, FreeSampleUse.KindWritingSubmission, failed.ToString("N"), default));
        Assert.False(await svc.IsFreeAttemptAsync("u1", "writing", failed.ToString("N"), default));
    }

    // ── profession change: pinned, unavailable, never reset ──────────────────

    [Fact]
    public async Task ProfessionChange_MakesTheSampleUnavailable_NeverResetsOrMovesIt()
    {
        await using var db = NewDb();
        await EnableAsync(db);
        var medicine = await SeedScenarioAsync(db, "medicine", "Alpha", difficulty: 1);
        var nursing = await SeedScenarioAsync(db, "nursing", "Nurse", difficulty: 1);
        await SeedLearnerAsync(db, "u1", "medicine");
        var svc = new FreeSampleService(db);

        var first = await SeedSubmissionAsync(db, "u1", medicine, WritingSubmissionStatuses.Grading);
        Assert.True(await svc.TryClaimAsync("u1", "writing", medicine.ToString("D"), FreeSampleUse.KindWritingSubmission, first.ToString("N"), default));
        await SetStatusAsync(db, first, WritingSubmissionStatuses.Graded);

        (await db.Users.SingleAsync(u => u.Id == "u1")).ActiveProfessionId = "nursing";
        await db.SaveChangesAsync();

        var offer = Assert.Single(await svc.ListAsync("u1", "writing", default));
        Assert.Equal(FreeSampleService.StateUnavailable, offer.State);
        Assert.Equal("medicine", offer.ProfessionId);           // still pinned to the claim
        Assert.Equal(medicine.ToString("D"), offer.ContentId);
        Assert.Null(offer.Route);
        Assert.Equal(1, offer.SuccessfulCount);
        Assert.False(await svc.IsOfferedAsync("u1", "writing", nursing.ToString("D"), default));
        Assert.False(await svc.IsOfferedAsync("u1", "writing", medicine.ToString("D"), default));
        var nursingSubmission = await SeedSubmissionAsync(db, "u1", nursing, WritingSubmissionStatuses.Grading);
        Assert.False(await svc.TryClaimAsync("u1", "writing", nursing.ToString("D"), FreeSampleUse.KindWritingSubmission, nursingSubmission.ToString("N"), default));
        Assert.Single(db.FreeSampleClaims);

        // Switching back restores the SAME allowance — no reset, no second claim.
        (await db.Users.SingleAsync(u => u.Id == "u1")).ActiveProfessionId = "Medicine";
        await db.SaveChangesAsync();
        var back = Assert.Single(await svc.ListAsync("u1", "writing", default));
        Assert.Equal(FreeSampleService.StateRetryAvailable, back.State);
        Assert.Equal(1, back.SuccessfulCount);
    }

    // ── concurrency: two binds for the last slot ─────────────────────────────

    /// <summary>Runs <paramref name="action"/> once, inside the first SaveChanges
    /// of the context it is attached to — i.e. after that context already
    /// decided the slot was free but before it committed.</summary>
    private sealed class RunBeforeFirstSave(Func<Task> action) : SaveChangesInterceptor
    {
        private Func<Task>? _action = action;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var run = Interlocked.Exchange(ref _action, null);
            if (run is not null) await run();
            return result;
        }
    }

    [Fact]
    public async Task TwoConcurrentBindsForTheLastSlot_ExactlyOneWins()
    {
        var name = Guid.NewGuid().ToString("N");
        var root = new InMemoryDatabaseRoot();
        Guid scenario, second, third;
        await using (var seed = NewDb(name, root))
        {
            await EnableAsync(seed);
            scenario = await SeedScenarioAsync(seed, "medicine", "Alpha", difficulty: 1);
            await SeedLearnerAsync(seed, "u1", "medicine");
            var first = await SeedSubmissionAsync(seed, "u1", scenario, WritingSubmissionStatuses.Grading);
            Assert.True(await new FreeSampleService(seed).TryClaimAsync(
                "u1", "writing", scenario.ToString("D"), FreeSampleUse.KindWritingSubmission, first.ToString("N"), default));
            await SetStatusAsync(seed, first, WritingSubmissionStatuses.Graded);
            second = await SeedSubmissionAsync(seed, "u1", scenario, WritingSubmissionStatuses.Grading);
            third = await SeedSubmissionAsync(seed, "u1", scenario, WritingSubmissionStatuses.Grading);
        }

        bool? otherWon = null;
        await using var other = NewDb(name, root);
        await using var racer = NewDb(name, root, new RunBeforeFirstSave(async () =>
            otherWon = await new FreeSampleService(other).TryClaimAsync(
                "u1", "writing", scenario.ToString("D"), FreeSampleUse.KindWritingSubmission, third.ToString("N"), default)));

        var racerWon = await new FreeSampleService(racer).TryClaimAsync(
            "u1", "writing", scenario.ToString("D"), FreeSampleUse.KindWritingSubmission, second.ToString("N"), default);

        Assert.True(otherWon);   // committed first
        Assert.False(racerWon);  // stale claim Version: lost, never free
        await using var check = NewDb(name, root);
        Assert.True(await check.FreeSampleUses.AnyAsync(u => u.ResourceId == third.ToString("N")));
        Assert.Equal(1, (await check.FreeSampleClaims.SingleAsync()).Version); // only the winner's bind committed
    }

    // ── Speaking on the shared session engine: zero credits, bound use ───────

    private static SpeakingSessionService BuildSessions(LearnerDbContext db)
        => new(
            db,
            creditReservations: new AiCreditReservationService(
                db, new AiPackageCreditService(db, NullLogger<AiPackageCreditService>.Instance), TimeProvider.System));

    private static CreateSpeakingSessionRequest Practice(string cardId)
        => new(cardId, SpeakingSessionModes.AiSelfPractice);

    private static async Task MarkAssessedAsync(LearnerDbContext db, string sessionId)
    {
        var session = await db.SpeakingSessions.SingleAsync(s => s.Id == sessionId);
        session.State = SpeakingSessionState.Finished;
        session.SubmittedAt = DateTimeOffset.UtcNow;
        db.SpeakingAiAssessments.Add(new SpeakingAiAssessment
        {
            Id = $"spa_{Guid.NewGuid():N}",
            SpeakingSessionId = sessionId,
            TranscriptId = "tr-1",
            Provider = "ai_gateway",
            ModelId = "test",
            ReadinessBand = "developing",
            GeneratedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Speaking_TheFreeCardRunsOnTheSessionEngine_AtZeroCredits_AndBindsAUse()
    {
        await using var db = NewDb();
        await EnableAsync(db);
        var (_, card) = await SeedCardAsync(db, "medicine", cardNumber: 1);
        var (_, otherCard) = await SeedCardAsync(db, "medicine", cardNumber: 2);
        await SeedLearnerAsync(db, "u1", "medicine"); // no credits at all
        var sessions = BuildSessions(db);

        var created = await sessions.CreateSessionAsync("u1", Practice(card), default);
        Assert.True(created.IsFreeSample);
        var use = await db.FreeSampleUses.SingleAsync();
        Assert.Equal(FreeSampleUse.KindSpeakingSession, use.ResourceKind);
        Assert.Equal(created.SessionId, use.ResourceId);

        var prep = await sessions.FinishWarmupAsync("u1", created.SessionId, default);
        Assert.Equal(SpeakingSessionStates.Prep, prep.State);
        Assert.True(prep.IsFreeSample);
        Assert.Empty(db.AiCreditReservations);             // free = 0 AI credits, nothing held
        Assert.True(await FreeSampleService.IsFreeSpeakingSessionAsync(
            db, await db.SpeakingSessions.SingleAsync(s => s.Id == created.SessionId), default)); // arms the quota grant

        // Control: any other card is an ordinary paid practice card.
        var paid = await sessions.CreateSessionAsync("u1", Practice(otherCard), default);
        Assert.False(paid.IsFreeSample);
        var ex = await Assert.ThrowsAsync<ApiException>(() => sessions.FinishWarmupAsync("u1", paid.SessionId, default));
        Assert.Equal("ai_credits_insufficient", ex.ErrorCode);
    }

    [Fact]
    public async Task Speaking_RestartingRebindsForFree_OneResultThenTheCardIsPaid()
    {
        await using var db = NewDb();
        await EnableAsync(db);
        var (_, card) = await SeedCardAsync(db, "medicine", cardNumber: 1);
        await SeedLearnerAsync(db, "u1", "medicine");
        var sessions = BuildSessions(db);
        var svc = new FreeSampleService(db);

        // Start, then exit and start again: the abandoned session is released
        // (cancelled) and nothing was spent.
        var abandoned = await sessions.CreateSessionAsync("u1", Practice(card), default);
        var first = await sessions.CreateSessionAsync("u1", Practice(card), default);
        Assert.True(first.IsFreeSample);
        Assert.Equal(first.SessionId, (await db.FreeSampleUses.SingleAsync()).ResourceId);
        Assert.Equal(SpeakingSessionState.Cancelled, (await db.SpeakingSessions.SingleAsync(s => s.Id == abandoned.SessionId)).State);
        Assert.Equal(0, (await svc.ListAsync("u1", "speaking", default)).Single().SuccessfulCount);

        // Submitted, assessment pending → in progress, and nothing new is free meanwhile.
        var submitted = await db.SpeakingSessions.SingleAsync(s => s.Id == first.SessionId);
        submitted.State = SpeakingSessionState.Finished;
        submitted.SubmittedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        var inProgress = (await svc.ListAsync("u1", "speaking", default)).Single();
        Assert.Equal(FreeSampleService.StateInProgress, inProgress.State);
        Assert.Equal($"/speaking/sessions/{first.SessionId}/results", inProgress.Route);
        Assert.False((await sessions.CreateSessionAsync("u1", Practice(card), default)).IsFreeSample);

        await MarkAssessedAsync(db, first.SessionId);
        var done = (await svc.ListAsync("u1", "speaking", default)).Single();
        Assert.Equal(FreeSampleService.StateCompleted, done.State);
        Assert.Equal(FreeSampleService.SpeakingSuccessLimit, done.Limit);
        Assert.Equal(1, done.SuccessfulCount);
        Assert.Equal(0, done.Remaining);
        Assert.Null(done.Route);
        Assert.Equal($"/speaking/sessions/{first.SessionId}/results", done.LastResultRoute);
        Assert.Equal(first.SessionId, done.LastSubmissionId);

        var paidRetry = await sessions.CreateSessionAsync("u1", Practice(card), default);
        Assert.False(paidRetry.IsFreeSample);
        var ex = await Assert.ThrowsAsync<ApiException>(() => sessions.FinishWarmupAsync("u1", paidRetry.SessionId, default));
        Assert.Equal("ai_credits_insufficient", ex.ErrorCode);
    }

    [Fact]
    public async Task Speaking_ATerminallyFailedAssessmentNeverCounts()
    {
        await using var db = NewDb();
        await EnableAsync(db);
        var (_, card) = await SeedCardAsync(db, "medicine", cardNumber: 1);
        await SeedLearnerAsync(db, "u1", "medicine");
        var sessions = BuildSessions(db);
        var svc = new FreeSampleService(db);

        var created = await sessions.CreateSessionAsync("u1", Practice(card), default);
        var row = await db.SpeakingSessions.SingleAsync(s => s.Id == created.SessionId);
        row.State = SpeakingSessionState.Finished;
        row.SubmittedAt = DateTimeOffset.UtcNow;
        db.AiOperations.Add(new AiOperation
        {
            Id = Guid.NewGuid().ToString("N"),
            Module = "speaking",
            FeatureCode = AiFeatureCodes.SpeakingGrade,
            UserId = "u1",
            ResourceType = "speaking_session",
            ResourceId = created.SessionId,
            IdempotencyKey = $"speaking.assess:{created.SessionId}",
            ResourceSlotKey = $"slot:{created.SessionId}",
            State = AiOperationState.FailedTerminal,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        var offer = (await svc.ListAsync("u1", "speaking", default)).Single();
        Assert.Equal(FreeSampleService.StateAvailable, offer.State);
        Assert.Equal(0, offer.SuccessfulCount);
        Assert.True(await svc.IsOfferedAsync("u1", "speaking", card, default));
    }
}
