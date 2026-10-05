using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Speaking;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests.Speaking;

/// <summary>
/// Live AI Speaking admission control (owner decision 5 Oct 2026): the DB-backed FIFO gate in front of the credit
/// hold and the clock. These tests drive the gate directly with a mutable clock and pin the rules the owner asked
/// for: a cap, a strict FIFO line (a newcomer never overtakes an earlier waiter), atomic admission, abandoned
/// waiters expiring, a kill switch, no eviction of running sessions, a fail-open gate and DB-derived counts.
/// Every call uses a NEW DbContext over the same in-memory store, like separate requests on separate API slots.
/// </summary>
public sealed class SpeakingLiveAdmissionServiceTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);

    private readonly string _dbName = $"live-admission-{Guid.NewGuid():N}";
    private readonly MutableTimeProvider _clock = new(T0);
    private readonly List<LearnerDbContext> _contexts = [];

    public void Dispose()
    {
        foreach (var db in _contexts)
        {
            db.Dispose();
        }
    }

    private LearnerDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(_dbName)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var db = new LearnerDbContext(options);
        _contexts.Add(db);
        return db;
    }

    // Heartbeat 600 s so the clock can move minutes without abandoning waiters; claim window 30 s (the minimum).
    private static SpeakingLiveAdmissionOptions Opts(int cap = 1) => new()
    {
        DefaultMaxConcurrent = cap,
        WaiterHeartbeatSeconds = 600,
        ClaimWindowSeconds = 30,
        AverageSessionSeconds = 600,
    };

    private SpeakingLiveAdmissionService Svc(SpeakingLiveAdmissionOptions? options = null)
        => new(NewDb(), Options.Create(options ?? Opts()), _clock);

    private static Task<SpeakingLiveAdmissionResult> AdmitAsync(
        SpeakingLiveAdmissionService svc, string user, string kind, string subject, bool live = true)
        => svc.AdmitOrQueueAsync(user, kind, subject, live, CancellationToken.None);

    private static Task<SpeakingLiveAdmissionResult> ExamAsync(SpeakingLiveAdmissionService svc, string n)
        => AdmitAsync(svc, $"user-{n}", SpeakingLiveAdmissionKinds.Exam, $"exam-{n}");

    private static Task<SpeakingLiveAdmissionResult> PracticeAsync(SpeakingLiveAdmissionService svc, string n)
        => AdmitAsync(svc, $"user-{n}", SpeakingLiveAdmissionKinds.Practice, $"practice-{n}");

    private async Task SeedExamAsync(string id, SpeakingExamState state)
    {
        var db = NewDb();
        db.SpeakingExamSessions.Add(new SpeakingExamSession
        {
            Id = id,
            UserId = $"owner-of-{id}",
            CardAId = "card-a",
            CardBId = "card-b",
            State = state,
        });
        await db.SaveChangesAsync();
    }

    private async Task SetExamStateAsync(string id, SpeakingExamState state)
    {
        var db = NewDb();
        (await db.SpeakingExamSessions.SingleAsync(e => e.Id == id)).State = state;
        await db.SaveChangesAsync();
    }

    private async Task SeedPracticeAsync(string id, SpeakingSessionState state)
    {
        var db = NewDb();
        db.SpeakingSessions.Add(new SpeakingSession
        {
            Id = id,
            UserId = $"owner-of-{id}",
            RolePlayCardId = "card",
            Mode = SpeakingSessionMode.AiSelfPractice,
            State = state,
        });
        await db.SaveChangesAsync();
    }

    private async Task SetPracticeStateAsync(string id, SpeakingSessionState state)
    {
        var db = NewDb();
        (await db.SpeakingSessions.SingleAsync(s => s.Id == id)).State = state;
        await db.SaveChangesAsync();
    }

    private async Task<SpeakingLiveAdmission> RowAsync(string kind, string subject)
        => await NewDb().SpeakingLiveAdmissions.AsNoTracking()
            .SingleAsync(a => a.Id == SpeakingLiveAdmissionService.RowId(kind, subject));

    [Fact]
    public async Task UnderTheCap_EveryLearnerIsAdmittedAtOnce()
    {
        var svc = Svc(Opts(cap: 2));

        var first = await ExamAsync(svc, "a");
        var second = await PracticeAsync(Svc(Opts(cap: 2)), "b");

        Assert.Equal(SpeakingLiveAdmissionOutcome.Admitted, first.Outcome);
        Assert.Equal(SpeakingLiveAdmissionOutcome.Admitted, second.Outcome);
        Assert.False(first.MustWait);
        Assert.Equal(SpeakingLiveAdmissionState.Admitted, (await RowAsync(SpeakingLiveAdmissionKinds.Exam, "exam-a")).State);
        Assert.Equal(SpeakingLiveAdmissionState.Admitted, (await RowAsync(SpeakingLiveAdmissionKinds.Practice, "practice-b")).State);
    }

    [Fact]
    public async Task OverTheCap_TheLineIsFifo_AndAWaitersPlaceIsKeptWhileItPolls()
    {
        Assert.Equal(SpeakingLiveAdmissionOutcome.Admitted, (await ExamAsync(Svc(), "a")).Outcome);

        var b = await ExamAsync(Svc(), "b");
        var c = await ExamAsync(Svc(), "c");

        Assert.True(b.MustWait);
        Assert.Equal("waiting", b.Waiting!.Status);
        Assert.Equal(1, b.Waiting.Position);
        Assert.Equal(1, b.Waiting.QueueLength);
        Assert.True(c.MustWait);
        Assert.Equal(2, c.Waiting!.Position);
        Assert.Equal(2, c.Waiting.QueueLength);

        // B polls again: same ticket, same place, the line is now two long.
        _clock.Advance(TimeSpan.FromSeconds(5));
        var bAgain = await ExamAsync(Svc(), "b");
        Assert.True(bAgain.MustWait);
        Assert.Equal(1, bAgain.Waiting!.Position);
        Assert.Equal(2, bAgain.Waiting.QueueLength);
        Assert.Equal(2, await NewDb().SpeakingLiveAdmissions.CountAsync(a => a.State == SpeakingLiveAdmissionState.Waiting));
    }

    [Fact]
    public async Task AFreedPlace_GoesToTheEarliestWaiter_NeverToANewcomer()
    {
        await SeedExamAsync("exam-a", SpeakingExamState.ActiveA);
        Assert.Equal(SpeakingLiveAdmissionOutcome.Admitted, (await ExamAsync(Svc(), "a")).Outcome);
        Assert.True((await ExamAsync(Svc(), "b")).MustWait);
        Assert.True((await ExamAsync(Svc(), "c")).MustWait);

        // A's exam ends; once the claim window is over its place is free.
        await SetExamStateAsync("exam-a", SpeakingExamState.Completed);
        _clock.Advance(TimeSpan.FromSeconds(31));

        // A newcomer finds a free place but two earlier waiters: it queues behind them.
        var d = await ExamAsync(Svc(), "d");
        Assert.True(d.MustWait);
        Assert.Equal(3, d.Waiting!.Position);

        // C is second in line: the one free place is not hers yet.
        var cAgain = await ExamAsync(Svc(), "c");
        Assert.True(cAgain.MustWait);
        Assert.Equal(2, cAgain.Waiting!.Position);

        // B, first in line, takes it atomically.
        Assert.Equal(SpeakingLiveAdmissionOutcome.Admitted, (await ExamAsync(Svc(), "b")).Outcome);

        // And now C is first in line, waiting for the next place.
        var cLast = await ExamAsync(Svc(), "c");
        Assert.True(cLast.MustWait);
        Assert.Equal(1, cLast.Waiting!.Position);
    }

    [Fact]
    public async Task APlaceFreesTheMomentItsSubjectEnds_NotAtTheSafetyTtl()
    {
        await SeedPracticeAsync("practice-a", SpeakingSessionState.Active);
        Assert.Equal(SpeakingLiveAdmissionOutcome.Admitted, (await PracticeAsync(Svc(), "a")).Outcome);
        Assert.True((await PracticeAsync(Svc(), "b")).MustWait);

        // Still running past the claim window: the place stays taken, nobody is evicted.
        _clock.Advance(TimeSpan.FromSeconds(31));
        Assert.True((await PracticeAsync(Svc(), "b")).MustWait);

        // The role-play finishes (any terminal path): the very next decision sees the free place.
        await SetPracticeStateAsync("practice-a", SpeakingSessionState.Finished);
        Assert.Equal(SpeakingLiveAdmissionOutcome.Admitted, (await PracticeAsync(Svc(), "b")).Outcome);
    }

    [Fact]
    public async Task AnAdmissionWhoseStartNeverHappened_FreesItsPlaceAfterTheClaimWindow()
    {
        // Admitted, but the credit hold then failed: the subject never left warm-up. No compensation step exists;
        // the claim window is what frees the place.
        await SeedPracticeAsync("practice-a", SpeakingSessionState.WarmUp);
        Assert.Equal(SpeakingLiveAdmissionOutcome.Admitted, (await PracticeAsync(Svc(), "a")).Outcome);

        var b = await PracticeAsync(Svc(), "b");
        Assert.True(b.MustWait);

        _clock.Advance(TimeSpan.FromSeconds(31));
        Assert.Equal(SpeakingLiveAdmissionOutcome.Admitted, (await PracticeAsync(Svc(), "b")).Outcome);
    }

    [Fact]
    public async Task AnAdmittedSubject_IsAdmittedAgainWithoutASecondPlace()
    {
        var first = await ExamAsync(Svc(Opts(cap: 2)), "a");
        var retry = await ExamAsync(Svc(Opts(cap: 2)), "a");
        var other = await ExamAsync(Svc(Opts(cap: 2)), "b");
        var third = await ExamAsync(Svc(Opts(cap: 2)), "c");

        Assert.Equal(SpeakingLiveAdmissionOutcome.Admitted, first.Outcome);
        Assert.Equal(SpeakingLiveAdmissionOutcome.Admitted, retry.Outcome);
        Assert.Equal(SpeakingLiveAdmissionOutcome.Admitted, other.Outcome);
        Assert.True(third.MustWait);
        Assert.Equal(1, await NewDb().SpeakingLiveAdmissions.CountAsync(a => a.SubjectId == "exam-a"));
    }

    [Fact]
    public async Task AnAbandonedWaiter_ExpiresAndStopsHoldingAPlaceInTheLine()
    {
        var options = Opts();
        options.WaiterHeartbeatSeconds = 60;
        await SeedPracticeAsync("practice-a", SpeakingSessionState.Active);
        Assert.Equal(SpeakingLiveAdmissionOutcome.Admitted, (await PracticeAsync(Svc(options), "a")).Outcome);
        Assert.Equal(1, (await PracticeAsync(Svc(options), "b")).Waiting!.Position);
        _clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(2, (await PracticeAsync(Svc(options), "c")).Waiting!.Position);

        // C keeps polling; B closed the tab.
        _clock.Advance(TimeSpan.FromSeconds(50));
        Assert.Equal(2, (await PracticeAsync(Svc(options), "c")).Waiting!.Position);
        _clock.Advance(TimeSpan.FromSeconds(15));
        var cAfterBLeft = await PracticeAsync(Svc(options), "c");

        Assert.True(cAfterBLeft.MustWait);
        Assert.Equal(1, cAfterBLeft.Waiting!.Position);
        Assert.Equal(1, cAfterBLeft.Waiting.QueueLength);
        var abandoned = await RowAsync(SpeakingLiveAdmissionKinds.Practice, "practice-b");
        Assert.Equal(SpeakingLiveAdmissionState.Expired, abandoned.State);

        // B comes back: a fresh ticket at the back of the line, never its old place.
        var bBack = await PracticeAsync(Svc(options), "b");
        Assert.True(bBack.MustWait);
        Assert.Equal(2, bBack.Waiting!.Position);
    }

    [Fact]
    public async Task APlaceKeptPastTheMaximumWait_GoesToTheBackOfTheLine()
    {
        var options = Opts();
        options.MaxWaitSeconds = 60;
        await SeedPracticeAsync("practice-a", SpeakingSessionState.Active);
        Assert.Equal(SpeakingLiveAdmissionOutcome.Admitted, (await PracticeAsync(Svc(options), "a")).Outcome);
        Assert.True((await PracticeAsync(Svc(options), "b")).MustWait);
        _clock.Advance(TimeSpan.FromSeconds(30));
        Assert.True((await PracticeAsync(Svc(options), "c")).MustWait);

        // B has been polling the whole time but has now waited longer than the maximum.
        _clock.Advance(TimeSpan.FromSeconds(31));
        var bAgain = await PracticeAsync(Svc(options), "b");

        Assert.True(bAgain.MustWait);
        Assert.Equal(2, bAgain.Waiting!.Position);
    }

    [Fact]
    public async Task TheKillSwitch_LetsEveryoneThrough_AndReleasesWaiters()
    {
        Assert.Equal(SpeakingLiveAdmissionOutcome.Admitted, (await ExamAsync(Svc(), "a")).Outcome);
        Assert.True((await ExamAsync(Svc(), "b")).MustWait);

        var counts = await Svc().UpdateSettingsAsync(enabled: false, maxConcurrent: null, "admin-1", CancellationToken.None);
        Assert.False(counts.Enabled);
        Assert.Equal("admin", counts.Source);

        var b = await ExamAsync(Svc(), "b");
        var newcomer = await ExamAsync(Svc(), "c");

        Assert.Equal(SpeakingLiveAdmissionOutcome.Bypassed, b.Outcome);
        Assert.Equal(SpeakingLiveAdmissionBypassReasons.Disabled, b.BypassReason);
        Assert.Equal(SpeakingLiveAdmissionOutcome.Bypassed, newcomer.Outcome);
        Assert.Equal(SpeakingLiveAdmissionState.Released, (await RowAsync(SpeakingLiveAdmissionKinds.Exam, "exam-b")).State);
        Assert.False(await NewDb().SpeakingLiveAdmissions.AnyAsync(a => a.SubjectId == "exam-c"));

        // Back on: the gate counts again from the database.
        await Svc().UpdateSettingsAsync(enabled: true, maxConcurrent: null, "admin-1", CancellationToken.None);
        Assert.True((await ExamAsync(Svc(), "d")).MustWait);
    }

    [Fact]
    public async Task TheEnvironmentKillSwitch_BypassesWithoutTouchingTheDatabase()
    {
        var options = Opts();
        options.Enabled = false;

        var result = await ExamAsync(Svc(options), "a");

        Assert.Equal(SpeakingLiveAdmissionOutcome.Bypassed, result.Outcome);
        Assert.Equal(SpeakingLiveAdmissionBypassReasons.Disabled, result.BypassReason);
        Assert.Empty(await NewDb().SpeakingLiveAdmissions.ToListAsync());
    }

    [Fact]
    public async Task NoHealthyLiveProvider_UsesTheRecorderFallback_AndNeverQueues()
    {
        Assert.Equal(SpeakingLiveAdmissionOutcome.Admitted, (await ExamAsync(Svc(), "a")).Outcome);

        var fallback = await AdmitAsync(Svc(), "user-b", SpeakingLiveAdmissionKinds.Exam, "exam-b", live: false);

        Assert.Equal(SpeakingLiveAdmissionOutcome.Bypassed, fallback.Outcome);
        Assert.Equal(SpeakingLiveAdmissionBypassReasons.LiveVoiceUnavailable, fallback.BypassReason);
        Assert.False(await NewDb().SpeakingLiveAdmissions.AnyAsync(a => a.SubjectId == "exam-b"));
    }

    [Fact]
    public async Task RaisingTheCap_AdmitsTheNextWaiter_AndLoweringItNeverEvictsARunningSession()
    {
        await SeedExamAsync("exam-a", SpeakingExamState.ActiveA);
        await SeedExamAsync("exam-b", SpeakingExamState.ActiveA);
        Assert.Equal(SpeakingLiveAdmissionOutcome.Admitted, (await ExamAsync(Svc(), "a")).Outcome);
        Assert.True((await ExamAsync(Svc(), "b")).MustWait);

        await Svc().UpdateSettingsAsync(null, maxConcurrent: 2, "admin-1", CancellationToken.None);
        Assert.Equal(SpeakingLiveAdmissionOutcome.Admitted, (await ExamAsync(Svc(), "b")).Outcome);

        // Lowering the cap below what is running changes nothing for the two sessions already admitted.
        _clock.Advance(TimeSpan.FromMinutes(5));
        var counts = await Svc().UpdateSettingsAsync(null, maxConcurrent: 1, "admin-1", CancellationToken.None);
        Assert.Equal(2, counts.Admitted);
        Assert.Equal(0, counts.Free);
        Assert.Equal(SpeakingLiveAdmissionState.Admitted, (await RowAsync(SpeakingLiveAdmissionKinds.Exam, "exam-a")).State);
        Assert.Equal(SpeakingLiveAdmissionState.Admitted, (await RowAsync(SpeakingLiveAdmissionKinds.Exam, "exam-b")).State);
        Assert.Equal(SpeakingLiveAdmissionOutcome.Admitted, (await ExamAsync(Svc(), "a")).Outcome);

        // But nobody new gets in until the count drains below the new cap.
        Assert.True((await ExamAsync(Svc(), "c")).MustWait);
    }

    [Fact]
    public async Task AFullLine_RefusesANewWaiterWithARetryable503()
    {
        var options = Opts();
        options.MaxQueueLength = 1;
        Assert.Equal(SpeakingLiveAdmissionOutcome.Admitted, (await ExamAsync(Svc(options), "a")).Outcome);
        Assert.True((await ExamAsync(Svc(options), "b")).MustWait);

        var refused = await Assert.ThrowsAsync<ApiException>(() => ExamAsync(Svc(options), "c"));

        Assert.Equal(503, refused.StatusCode);
        Assert.Equal("speaking_live_queue_full", refused.ErrorCode);
        Assert.True(refused.Retryable);
        // The learner already in the line is not affected, and the refused one left no row.
        Assert.True((await ExamAsync(Svc(options), "b")).MustWait);
        Assert.False(await NewDb().SpeakingLiveAdmissions.AnyAsync(a => a.SubjectId == "exam-c"));
    }

    [Fact]
    public async Task TheWaitEstimate_ScalesWithPlaceAndCap()
    {
        var options = Opts(cap: 2);
        await ExamAsync(Svc(options), "a");
        await ExamAsync(Svc(options), "b");

        var first = (await ExamAsync(Svc(options), "c")).Waiting!;
        var second = (await ExamAsync(Svc(options), "d")).Waiting!;
        var third = (await ExamAsync(Svc(options), "e")).Waiting!;

        // 600 s average session over 2 places: one place opens every 300 s.
        Assert.Equal(300, first.EstimatedWaitSeconds);
        Assert.Equal(600, second.EstimatedWaitSeconds);
        Assert.Equal(900, third.EstimatedWaitSeconds);
        Assert.Equal(4, first.PollAfterSeconds);
    }

    [Fact]
    public async Task TheWaitingView_IsReadOnly_AndNullWhenNotWaiting()
    {
        await ExamAsync(Svc(), "a");
        await ExamAsync(Svc(), "b");
        var before = await RowAsync(SpeakingLiveAdmissionKinds.Exam, "exam-b");

        _clock.Advance(TimeSpan.FromSeconds(20));
        var view = await Svc().GetWaitingViewAsync(SpeakingLiveAdmissionKinds.Exam, "exam-b", CancellationToken.None);

        Assert.NotNull(view);
        Assert.Equal(1, view!.Position);
        Assert.Equal(1, view.QueueLength);
        // A read never refreshes the heartbeat: only the page's retry keeps a place.
        Assert.Equal(before.LastSeenAt, (await RowAsync(SpeakingLiveAdmissionKinds.Exam, "exam-b")).LastSeenAt);
        Assert.Null(await Svc().GetWaitingViewAsync(SpeakingLiveAdmissionKinds.Exam, "exam-a", CancellationToken.None));
        Assert.Null(await Svc().GetWaitingViewAsync(SpeakingLiveAdmissionKinds.Exam, "never-queued", CancellationToken.None));

        // A waiter that stopped polling is not shown as waiting any more.
        _clock.Advance(TimeSpan.FromSeconds(601));
        Assert.Null(await Svc().GetWaitingViewAsync(SpeakingLiveAdmissionKinds.Exam, "exam-b", CancellationToken.None));
    }

    [Fact]
    public async Task TheCounts_ComeFromTheDatabase_AndReportTheCapAdmittedWaitingAndTheOldestWait()
    {
        await SeedExamAsync("exam-a", SpeakingExamState.ActiveA);
        await ExamAsync(Svc(), "a");
        await ExamAsync(Svc(), "b");
        _clock.Advance(TimeSpan.FromSeconds(30));
        await ExamAsync(Svc(), "c");

        // A different slot (a different DbContext) sees the same numbers.
        var counts = await Svc().GetCountsAsync(CancellationToken.None);

        Assert.True(counts.Enabled);
        Assert.Equal(1, counts.MaxConcurrent);
        Assert.Equal("default", counts.Source);
        Assert.Equal(1, counts.Admitted);
        Assert.Equal(2, counts.Waiting);
        Assert.Equal(0, counts.Free);
        Assert.Equal(T0, counts.OldestWaitingSince);
        Assert.Equal(30, counts.OldestWaitSeconds);
    }

    [Fact]
    public async Task TheSettings_AreValidated_AndTheRowOverridesTheConfiguredDefault()
    {
        var tooLow = await Assert.ThrowsAsync<ApiException>(() =>
            Svc().UpdateSettingsAsync(null, 0, "admin-1", CancellationToken.None));
        var tooHigh = await Assert.ThrowsAsync<ApiException>(() =>
            Svc().UpdateSettingsAsync(null, 10_001, "admin-1", CancellationToken.None));
        Assert.Equal("speaking_live_admission_cap_invalid", tooLow.ErrorCode);
        Assert.Equal(400, tooHigh.StatusCode);
        Assert.Empty(await NewDb().SpeakingLiveAdmissionSettings.ToListAsync());

        var counts = await Svc(Opts(cap: 5)).UpdateSettingsAsync(null, 7, "admin-1", CancellationToken.None);

        Assert.Equal(7, counts.MaxConcurrent);
        Assert.Equal("admin", counts.Source);
        Assert.True(counts.Enabled);
        var row = await NewDb().SpeakingLiveAdmissionSettings.SingleAsync();
        Assert.Equal("global", row.Id);
        Assert.Equal("admin-1", row.UpdatedById);
    }

    [Fact]
    public async Task ADefaultOfOneHundred_IsTheOwnerTargetWhenNothingIsConfigured()
    {
        var counts = await new SpeakingLiveAdmissionService(
            NewDb(), Options.Create(new SpeakingLiveAdmissionOptions()), _clock).GetCountsAsync(CancellationToken.None);

        Assert.Equal(100, counts.MaxConcurrent);
        Assert.True(counts.Enabled);
    }

    [Fact]
    public async Task TheGate_FailsOpen_WhenItsOwnStorageThrows_AndLeavesNothingForTheCallersNextSave()
    {
        var db = NewFaultingDb();
        var svc = new SpeakingLiveAdmissionService(db, Options.Create(Opts()), _clock);

        db.FailAdmissionWrites = true;
        var result = await svc.AdmitOrQueueAsync("user-a", SpeakingLiveAdmissionKinds.Exam, "exam-a", true, CancellationToken.None);

        Assert.Equal(SpeakingLiveAdmissionOutcome.Bypassed, result.Outcome);
        Assert.Equal(SpeakingLiveAdmissionBypassReasons.AdmissionUnavailable, result.BypassReason);
        // The half-written row was detached: the caller's next SaveChanges (the credit hold, the exam
        // transition) must not retry it and fail.
        Assert.Empty(db.ChangeTracker.Entries<SpeakingLiveAdmission>());
        db.FailAdmissionWrites = false;
        await db.SaveChangesAsync();
        Assert.Empty(await NewDb().SpeakingLiveAdmissions.ToListAsync());
    }

    [Fact]
    public async Task TheSweep_ExpiresAbandonedWaiters_AndPurgesOldEndedRows()
    {
        var options = Opts();
        options.WaiterHeartbeatSeconds = 60;
        options.RetentionDays = 7;
        await ExamAsync(Svc(options), "a");
        await ExamAsync(Svc(options), "b");

        _clock.Advance(TimeSpan.FromSeconds(61));
        var changed = await Svc(options).SweepAsync(CancellationToken.None);
        Assert.Equal(1, changed);
        Assert.Equal(SpeakingLiveAdmissionState.Expired, (await RowAsync(SpeakingLiveAdmissionKinds.Exam, "exam-b")).State);

        _clock.Advance(TimeSpan.FromDays(8));
        var purged = await Svc(options).SweepAsync(CancellationToken.None);

        Assert.True(purged >= 1);
        Assert.False(await NewDb().SpeakingLiveAdmissions.AnyAsync(a => a.SubjectId == "exam-b"));
    }

    [Fact]
    public async Task AnUnknownKind_IsRejected()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            AdmitAsync(Svc(), "user-a", "something-else", "subject-a"));
    }

    private FaultingAdmissionDb NewFaultingDb()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(_dbName)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var db = new FaultingAdmissionDb(options);
        _contexts.Add(db);
        return db;
    }

    /// <summary>Fails any save that adds an admission row, to prove the gate fails open and detaches what it added.</summary>
    private sealed class FaultingAdmissionDb(DbContextOptions<LearnerDbContext> options) : LearnerDbContext(options)
    {
        public bool FailAdmissionWrites { get; set; }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            if (FailAdmissionWrites && ChangeTracker.Entries<SpeakingLiveAdmission>().Any(e => e.State == EntityState.Added))
            {
                throw new DbUpdateException("Simulated admission write failure.");
            }
            return base.SaveChangesAsync(cancellationToken);
        }
    }
}
