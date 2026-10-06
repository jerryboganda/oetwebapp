using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Writing;

namespace OetLearner.Api.Tests.Writing;

/// <summary>
/// WAI-06 zero-loss drafts: compare-and-set versions (a stale write can never
/// clobber newer text), the pause-while-away exam clock that can only run
/// down, the submitted → new-attempt lifecycle, and the best-effort consume
/// called by the submit paths.
/// </summary>
public sealed class WritingDraftServiceV2Tests
{
    private const string User = "draft-user";
    private static readonly Guid Scenario = Guid.Parse("5ce0a210-0000-4000-8000-000000000001");

    private readonly StepClock _clock = new();

    // ── compare-and-set ──────────────────────────────────────────────────────

    [Fact]
    public async Task CreateOnly_AnchorsTheClockWithEmptyContent_ThenRefusesASecondCreate()
    {
        await using var db = NewDb();
        var service = Service(db);

        var created = await service.SaveAsync(User, Scenario, "Practice", Save("", expected: 0, phase: "reading", reading: 300, writing: 2400), default);

        Assert.Equal((1, "active", "practice", ""), (created.Version, created.Status, created.Mode, created.Content));
        Assert.Equal(("reading", (int?)300, (int?)2400), (created.Phase, created.ReadingSecondsRemaining, created.WritingSecondsRemaining));
        Assert.Equal(_clock.Now, created.AttemptStartedAt);
        Assert.Null(created.SubmissionId);

        var again = await Assert.ThrowsAsync<ApiException>(() => service.SaveAsync(User, Scenario, "practice", Save("other", expected: 0), default));
        Assert.Equal(("draft_version_conflict", 409), (again.ErrorCode, again.StatusCode));
    }

    [Fact]
    public async Task MatchingVersion_Writes_AndAStaleVersionCanNeverClobberNewerText()
    {
        await using var db = NewDb();
        var service = Service(db);
        await service.SaveAsync(User, Scenario, "practice", Save("first", expected: 0), default);
        var second = await service.SaveAsync(User, Scenario, "practice", Save("newer text", expected: 1), default);
        Assert.Equal(2, second.Version);

        var stale = await Assert.ThrowsAsync<ApiException>(() => service.SaveAsync(User, Scenario, "practice", Save("stale", expected: 1), default));
        Assert.Equal("draft_version_conflict", stale.ErrorCode);
        var stored = await service.GetAsync(User, Scenario, "practice", default);
        Assert.Equal(("newer text", 2), (stored!.Content, stored.Version));
    }

    [Fact]
    public async Task AnExpectedVersion_WithNoRow_IsAConflict()
    {
        await using var db = NewDb();
        var ex = await Assert.ThrowsAsync<ApiException>(() => Service(db).SaveAsync(User, Scenario, "practice", Save("text", expected: 3), default));
        Assert.Equal("draft_version_conflict", ex.ErrorCode);
        Assert.Empty(db.WritingDraftsV2);
    }

    [Fact]
    public async Task TwoRacingWritesOnTheSameVersion_ExactlyOneWins()
    {
        var name = Guid.NewGuid().ToString("N");
        var root = new InMemoryDatabaseRoot();
        await using (var seed = NewDb(name, root))
        {
            await Service(seed).SaveAsync(User, Scenario, "practice", Save("v1", expected: 0), default);
        }

        await using var other = NewDb(name, root);
        await using var racer = NewDb(name, root, new RunBeforeFirstSave(() =>
            Service(other).SaveAsync(User, Scenario, "practice", Save("winner", expected: 1), default)));

        // The racer read version 1 too, but the other write committed first:
        // the database-level check (not just the in-app compare) refuses it.
        var ex = await Assert.ThrowsAsync<ApiException>(() => Service(racer).SaveAsync(User, Scenario, "practice", Save("loser", expected: 1), default));
        Assert.Equal("draft_version_conflict", ex.ErrorCode);
        await using var check = NewDb(name, root);
        var row = await check.WritingDraftsV2.SingleAsync();
        Assert.Equal(("winner", 2), (row.Content, row.Version));
    }

    [Fact]
    public async Task ALegacyWrite_IsUnconditional_AndLeavesTheClockAlone()
    {
        await using var db = NewDb();
        var service = Service(db);
        await service.SaveAsync(User, Scenario, "practice", Save("start", expected: 0, phase: "writing", reading: 0, writing: 1200), default);

        var legacy = await service.SaveAsync(User, Scenario, "practice", Save("legacy text"), default);

        Assert.Equal(("legacy text", 2), (legacy.Content, legacy.Version));
        Assert.Equal(("writing", (int?)0, (int?)1200), (legacy.Phase, legacy.ReadingSecondsRemaining, legacy.WritingSecondsRemaining));
    }

    [Fact]
    public async Task ALegacyWrite_CreatesTheRowAtVersionOne()
    {
        await using var db = NewDb();
        var created = await Service(db).SaveAsync(User, Scenario, "mock", Save("paper page text"), default);
        Assert.Equal((1, "active", (string?)null), (created.Version, created.Status, created.Phase));
    }

    // ── the exam clock only runs down ────────────────────────────────────────

    [Fact]
    public async Task Timers_KeepTheMinimum_AndThePhaseNeverGoesBack()
    {
        await using var db = NewDb();
        var service = Service(db);
        await service.SaveAsync(User, Scenario, "practice", Save("", expected: 0, phase: "reading", reading: 300, writing: 2400), default);
        var writing = await service.SaveAsync(User, Scenario, "practice", Save("a", expected: 1, phase: "writing", reading: 0, writing: 1500), default);
        Assert.Equal(("writing", (int?)0, (int?)1500), (writing.Phase, writing.ReadingSecondsRemaining, writing.WritingSecondsRemaining));

        // A save from a stale tab still on the reading screen with a fresh clock.
        var stale = await service.SaveAsync(User, Scenario, "practice", Save("ab", expected: 2, phase: "reading", reading: 300, writing: 2400), default);
        Assert.Equal(("writing", (int?)0, (int?)1500), (stale.Phase, stale.ReadingSecondsRemaining, stale.WritingSecondsRemaining));

        var nulls = await service.SaveAsync(User, Scenario, "practice", Save("abc", expected: 3, phase: "bogus"), default);
        Assert.Equal(("writing", (int?)0, (int?)1500), (nulls.Phase, nulls.ReadingSecondsRemaining, nulls.WritingSecondsRemaining));
    }

    [Fact]
    public async Task ALegacyRowWithoutAClock_TakesTheFirstClockSent()
    {
        await using var db = NewDb();
        var service = Service(db);
        await service.SaveAsync(User, Scenario, "practice", Save("old letter"), default);

        var timed = await service.SaveAsync(User, Scenario, "practice", Save("old letter", expected: 1, phase: "writing", reading: 0, writing: 2400), default);
        Assert.Equal(("writing", (int?)0, (int?)2400), (timed.Phase, timed.ReadingSecondsRemaining, timed.WritingSecondsRemaining));
    }

    // ── submitted → new attempt ──────────────────────────────────────────────

    [Fact]
    public async Task ASubmittedRow_IgnoresLegacyWrites_RefusesStaleVersions_AndStartsANewAttemptOnAMatch()
    {
        await using var db = NewDb();
        var service = Service(db);
        await service.SaveAsync(User, Scenario, "practice", Save("submitted letter", expected: 0, phase: "writing", reading: 0, writing: 100), default);
        var submissionId = await SeedSubmissionAsync(db, WritingSubmissionStatuses.Graded);
        await WritingDraftServiceV2.ConsumeAsync(db, User, Scenario, "practice", submissionId, NullLogger.Instance, default);

        var consumed = await service.GetAsync(User, Scenario, "practice", default);
        Assert.Equal(("submitted", 2, (Guid?)submissionId, "graded"), (consumed!.Status, consumed.Version, consumed.SubmissionId, consumed.SubmissionStatus));

        var ignored = await service.SaveAsync(User, Scenario, "practice", Save("late autosave"), default);
        Assert.Equal(("submitted letter", "submitted", 2), (ignored.Content, ignored.Status, ignored.Version));
        await Assert.ThrowsAsync<ApiException>(() => service.SaveAsync(User, Scenario, "practice", Save("x", expected: 1), default));

        var startedAt = _clock.Now = _clock.Now.AddHours(1);
        var again = await service.SaveAsync(User, Scenario, "practice", Save("", expected: 2, phase: "reading", reading: 300, writing: 2400), default);
        Assert.Equal(("active", 3, (Guid?)null, (string?)null), (again.Status, again.Version, again.SubmissionId, again.SubmissionStatus));
        Assert.Equal(("reading", (int?)300, (int?)2400, ""), (again.Phase, again.ReadingSecondsRemaining, again.WritingSecondsRemaining, again.Content));
        Assert.Equal(startedAt, again.AttemptStartedAt);
        Assert.Equal(startedAt, await WritingDraftServiceV2.GetAttemptStartedAtAsync(db, User, Scenario, "PRACTICE", default));
    }

    // ── consume + attempt start ──────────────────────────────────────────────

    [Fact]
    public async Task Consume_IsIdempotent_AndTouchesOnlyTheCallersDraft()
    {
        await using var db = NewDb();
        var service = Service(db);
        await service.SaveAsync(User, Scenario, "practice", Save("mine", expected: 0), default);
        await service.SaveAsync("someone-else", Scenario, "practice", Save("theirs", expected: 0), default);
        var first = Guid.NewGuid();

        await WritingDraftServiceV2.ConsumeAsync(db, User, Scenario, "practice", first, NullLogger.Instance, default);
        await WritingDraftServiceV2.ConsumeAsync(db, User, Scenario, "practice", Guid.NewGuid(), NullLogger.Instance, default);
        await WritingDraftServiceV2.ConsumeAsync(db, User, Guid.NewGuid(), "practice", first, NullLogger.Instance, default); // no row

        var mine = await db.WritingDraftsV2.AsNoTracking().SingleAsync(d => d.UserId == User);
        Assert.Equal(("submitted", (Guid?)first, 2), (mine.Status, mine.SubmissionId, mine.Version));
        var theirs = await db.WritingDraftsV2.AsNoTracking().SingleAsync(d => d.UserId == "someone-else");
        Assert.Equal(("active", (Guid?)null), (theirs.Status, theirs.SubmissionId));
    }

    [Fact]
    public async Task Consume_NeverThrows_AndLeavesNothingTrackedForTheCallersNextSave()
    {
        var name = Guid.NewGuid().ToString("N");
        var root = new InMemoryDatabaseRoot();
        await using (var seed = NewDb(name, root))
        {
            await Service(seed).SaveAsync(User, Scenario, "practice", Save("text", expected: 0), default);
        }

        await using var db = NewDb(name, root, new RunBeforeFirstSave(() => throw new InvalidOperationException("db down")));
        await WritingDraftServiceV2.ConsumeAsync(db, User, Scenario, "practice", Guid.NewGuid(), NullLogger.Instance, default);

        Assert.Empty(db.ChangeTracker.Entries<WritingDraftV2>());
        db.FeatureFlags.Add(new FeatureFlag { Id = "ff-1", Key = "k", Name = "n", CreatedAt = _clock.Now, UpdatedAt = _clock.Now });
        await db.SaveChangesAsync(); // the submit's own later save still succeeds
        Assert.Equal("active", (await db.WritingDraftsV2.AsNoTracking().SingleAsync()).Status);
    }

    [Fact]
    public async Task Consume_WhenAnAutosaveLandsFirst_ConsumesTheWinnersRow()
    {
        var name = Guid.NewGuid().ToString("N");
        var root = new InMemoryDatabaseRoot();
        await using (var seed = NewDb(name, root))
        {
            await Service(seed).SaveAsync(User, Scenario, "practice", Save("v1", expected: 0), default);
        }

        await using var autosave = NewDb(name, root);
        await using var db = NewDb(name, root, new RunBeforeFirstSave(() =>
            Service(autosave).SaveAsync(User, Scenario, "practice", Save("final words", expected: 1), default)));
        var submissionId = Guid.NewGuid();
        await WritingDraftServiceV2.ConsumeAsync(db, User, Scenario, "practice", submissionId, NullLogger.Instance, default);

        await using var check = NewDb(name, root);
        var row = await check.WritingDraftsV2.SingleAsync();
        Assert.Equal(("final words", "submitted", (Guid?)submissionId, 3), (row.Content, row.Status, row.SubmissionId, row.Version));
    }

    [Fact]
    public async Task AttemptStartedAt_IsNullWithoutARowAndForLegacyRows()
    {
        await using var db = NewDb();
        Assert.Null(await WritingDraftServiceV2.GetAttemptStartedAtAsync(db, User, Scenario, "practice", default));
        db.WritingDraftsV2.Add(new WritingDraftV2
        {
            Id = Guid.NewGuid(), UserId = User, ScenarioId = Scenario, Mode = "practice", Content = "legacy",
            LastSavedAt = _clock.Now, CreatedAt = _clock.Now,
        });
        await db.SaveChangesAsync();
        Assert.Null(await WritingDraftServiceV2.GetAttemptStartedAtAsync(db, User, Scenario, "practice", default));
    }

    [Fact]
    public async Task CreateSubmission_ConsumesThePracticeDraft_AndRevisionDraftsAreNoLongerWritten()
    {
        await using var db = NewDb();
        db.Users.Add(new LearnerUser
        {
            Id = User, DisplayName = User, Email = $"{User}@example.test", ActiveProfessionId = "medicine",
            AccountStatus = "active", CreatedAt = _clock.Now, LastActiveAt = _clock.Now,
        });
        db.WritingScenarios.Add(new WritingScenario
        {
            Id = Scenario, Title = "Task", Profession = "medicine", LetterType = "LT-RR", Status = "published",
            AuthorId = "admin-1", CreatedAt = _clock.Now, UpdatedAt = _clock.Now,
        });
        await db.SaveChangesAsync();
        var drafts = Service(db);
        await drafts.SaveAsync(User, Scenario, "practice", Save("letter", expected: 0), default);
        // Revise & Resubmit is retired: a stale client can no longer create or grow a revision draft.
        var retired = await Assert.ThrowsAsync<ApiException>(
            () => drafts.SaveAsync(User, Scenario, "revision", Save("revised letter", expected: 0), default));
        Assert.Equal(("writing_revise_retired", 409), (retired.ErrorCode, retired.StatusCode));
        Assert.False(await db.WritingDraftsV2.AnyAsync(d => d.Mode == "revision"));
        var submissions = new WritingSubmissionService(db, new SeamStub(db), NullLogger<WritingSubmissionService>.Instance, new NoHighlights());

        var created = await submissions.CreateSubmissionAsync(User, new WritingSubmissionCreateRequest(
            Scenario, "practice", "letter", 1, 60, "editor", null), default);
        Assert.Equal(("submitted", (Guid?)created.Id), await DraftStateAsync(db, "practice"));
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private WritingDraftServiceV2 Service(LearnerDbContext db) => new(db, _clock);

    private static WritingDraftV2SaveRequest Save(
        string content, int? expected = null, string? phase = null, int? reading = null, int? writing = null)
        => new(content, content.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length, 60, expected, phase, reading, writing);

    private static async Task<(string, Guid?)> DraftStateAsync(LearnerDbContext db, string mode)
    {
        var row = await db.WritingDraftsV2.AsNoTracking().SingleAsync(d => d.UserId == User && d.Mode == mode);
        return (row.Status, row.SubmissionId);
    }

    private async Task<Guid> SeedSubmissionAsync(LearnerDbContext db, string status)
    {
        var id = Guid.NewGuid();
        db.WritingSubmissions.Add(new WritingSubmission
        {
            Id = id, UserId = User, ScenarioId = Scenario, Mode = "practice", LetterContent = "submitted letter",
            // Submitted past the 15-minute release window, so a graded seed reads as a released result.
            LetterContentHash = id.ToString("N"), Status = status, StartedAt = _clock.Now, SubmittedAt = _clock.Now.AddHours(-1), CreatedAt = _clock.Now,
        });
        await db.SaveChangesAsync();
        return id;
    }

    private static LearnerDbContext NewDb(string? name = null, InMemoryDatabaseRoot? root = null, IInterceptor? interceptor = null)
    {
        var builder = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(name ?? Guid.NewGuid().ToString("N"), root ?? new InMemoryDatabaseRoot())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning));
        if (interceptor is not null) builder.AddInterceptors(interceptor);
        return new LearnerDbContext(builder.Options);
    }

    private sealed class StepClock : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 10, 2, 9, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    /// <summary>Runs <paramref name="action"/> once, inside the first SaveChanges
    /// of the context it is attached to — after that context read the row but
    /// before it committed.</summary>
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

    /// <summary>The submit seam reduced to "persist one row" (no grading: IsNew false).</summary>
    private sealed class SeamStub(LearnerDbContext db) : IWritingSubmissionEvaluationPipeline
    {
        public Task<Guid> CreateSubmissionAsync(WritingSubmissionGradeContext context, CancellationToken ct) => throw new NotSupportedException();
        public Task<WritingSubmissionGradeOutcome> EvaluateAsync(Guid submissionId, CancellationToken ct) => throw new NotSupportedException();

        public async Task<WritingSubmitOutcome> SubmitAsync(WritingSubmitAttempt attempt, CancellationToken ct)
        {
            var id = Guid.NewGuid();
            db.WritingSubmissions.Add(new WritingSubmission
            {
                Id = id, UserId = attempt.UserId, ScenarioId = attempt.ScenarioId, Mode = attempt.Mode,
                LetterContent = attempt.LetterContent ?? "", LetterContentHash = id.ToString("N"),
                IsRevision = attempt.IsRevision, OriginalSubmissionId = attempt.OriginalSubmissionId,
                Status = WritingSubmissionStatuses.Graded, StartedAt = attempt.StartedAt,
                SubmittedAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync(ct);
            return new WritingSubmitOutcome(id, IsNew: false);
        }
    }

    private sealed class NoHighlights : IWritingCaseNoteHighlightService
    {
        public Task<string> GetAsync(string userId, Guid scenarioId, CancellationToken ct) => Task.FromResult("{}");
        public Task<string> SaveAsync(string userId, Guid scenarioId, string highlightsJson, CancellationToken ct) => Task.FromResult(highlightsJson);
    }
}
