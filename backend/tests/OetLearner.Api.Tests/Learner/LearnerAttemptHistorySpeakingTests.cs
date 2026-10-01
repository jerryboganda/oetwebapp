using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;

namespace OetLearner.Api.Tests.Learner;

/// <summary>
/// The History "Attempt activity" list for Speaking. A graded or being-graded full mock is ONE row
/// (not the two unscored card attempts it used to be) that opens the exam results; a practice card
/// opens its own results page; both show the score once marked and say "Marking in progress" until then.
/// Credits are matched by the hold's own reference (exam:{id}:... / practice:{sessionId}).
/// </summary>
public sealed class LearnerAttemptHistorySpeakingTests : IAsyncLifetime
{
    private const string UserId = "history-speaking-learner";

    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private DbContextOptions<LearnerDbContext> _options = default!;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        // Speaking rows are seeded without their role-play cards, as SpeakingFinalizationRaceTests does.
        DisableForeignKeys();
        _options = new DbContextOptionsBuilder<LearnerDbContext>().UseSqlite(_connection).Options;
        await using var db = new LearnerDbContext(_options);
        await db.Database.EnsureCreatedAsync();
        DisableForeignKeys();
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    private void DisableForeignKeys()
    {
        using var pragma = _connection.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys=OFF;";
        pragma.ExecuteNonQuery();
    }

    private static async Task<IReadOnlyList<LearnerAttemptHistoryItem>> HistoryAsync(
        LearnerDbContext db, int limit = 100, string? subtest = null)
        => (await new LearnerAttemptHistoryService(db).GetHistoryAsync(UserId, limit, subtest, CancellationToken.None)).Items;

    // ── The mock: one row, not two ───────────────────────────────────────

    [Fact]
    public async Task GradedExam_IsOneFullSpeakingMockRow_ThatOpensItsResults_WithTheScoreAndTheFourCreditsItCost()
    {
        var started = DateTimeOffset.UtcNow.AddHours(-2);
        await using var db = new LearnerDbContext(_options);
        AddExam(db, "spx_graded", SpeakingExamState.Completed, started, combinedSnapshot: 380);
        AddDebit(db, "exam:spx_graded:cardA", 2, started.AddMinutes(1));
        AddDebit(db, "exam:spx_graded:cardB", 2, started.AddMinutes(9));
        await db.SaveChangesAsync();

        var items = await HistoryAsync(db);

        // The two card attempts are not listed on their own: the exam is the only row.
        var row = Assert.Single(items);
        Assert.Equal("spx_graded", row.AttemptId);
        Assert.Equal("speaking", row.Subtest);
        Assert.Equal("Full Speaking Mock", row.Title);
        Assert.Equal("spx_graded", row.ContentRef);
        Assert.Equal(started, row.StartedAt);
        Assert.Equal(started.AddMinutes(18), row.SubmittedAt);
        Assert.Equal("completed", row.Status);
        Assert.Equal("/speaking/exam/spx_graded/results", row.Route);
        Assert.Equal("dedicated", row.BalanceSource);
        Assert.Equal(4, row.CreditsUsed);
        Assert.Equal("380/500", row.ResultLabel);
    }

    [Fact]
    public async Task ExamResultLabel_FollowsTheExamResultsRules_AndNeverShowsARawState()
    {
        var started = DateTimeOffset.UtcNow.AddHours(-10);
        await using var db = new LearnerDbContext(_options);
        // The persisted combined snapshot wins.
        AddExam(db, "spx_snapshot", SpeakingExamState.Completed, started, combinedSnapshot: 412);
        // No snapshot yet, both cards graded: (361 + 400) / 2 = 380.5, rounded the way the results page rounds it.
        AddExam(db, "spx_average", SpeakingExamState.Completed, started.AddMinutes(30));
        AddAssessment(db, "sps_spx_average_a", 361);
        AddAssessment(db, "sps_spx_average_b", 400);
        // Graded by the v1.1 assessor instead of the classic one: counts the same way.
        AddExam(db, "spx_v11", SpeakingExamState.Completed, started.AddMinutes(45));
        AddV11CardScore(db, "sps_spx_v11_a", 350);
        AddV11CardScore(db, "sps_spx_v11_b", 400);
        // Finished, but only one card is graded, or none yet.
        AddExam(db, "spx_half", SpeakingExamState.Completed, started.AddMinutes(60));
        AddAssessment(db, "sps_spx_half_a", 360);
        AddExam(db, "spx_waiting", SpeakingExamState.Completed, started.AddMinutes(90));
        // Still running, or never finished: no result to show or to wait for.
        AddExam(db, "spx_running", SpeakingExamState.ActiveA, started.AddMinutes(120));
        AddExam(db, "spx_expired", SpeakingExamState.Expired, started.AddMinutes(150));
        AddExam(db, "spx_cancelled", SpeakingExamState.Cancelled, started.AddMinutes(180));
        // A tutor-marked exam never gets an AI number.
        AddExam(db, "spx_tutor", SpeakingExamState.Completed, started.AddMinutes(210), mode: SpeakingExamMode.LiveTutor);
        await db.SaveChangesAsync();

        var rows = (await HistoryAsync(db)).ToDictionary(item => item.AttemptId);

        Assert.Equal("412/500", rows["spx_snapshot"].ResultLabel);
        Assert.Equal("380/500", rows["spx_average"].ResultLabel);
        Assert.Equal("375/500", rows["spx_v11"].ResultLabel);
        Assert.Equal("Marking in progress", rows["spx_half"].ResultLabel);
        Assert.Equal("Marking in progress", rows["spx_waiting"].ResultLabel);
        Assert.Null(rows["spx_running"].ResultLabel);
        Assert.Null(rows["spx_expired"].ResultLabel);
        Assert.Null(rows["spx_cancelled"].ResultLabel);
        Assert.Null(rows["spx_tutor"].ResultLabel);
    }

    [Theory]
    [InlineData(SpeakingExamState.Intro, "in_progress", false)]
    [InlineData(SpeakingExamState.PrepA, "in_progress", false)]
    [InlineData(SpeakingExamState.ActiveA, "in_progress", false)]
    [InlineData(SpeakingExamState.PrepB, "in_progress", false)]
    [InlineData(SpeakingExamState.ActiveB, "in_progress", false)]
    [InlineData(SpeakingExamState.Completed, "completed", true)]
    [InlineData(SpeakingExamState.Cancelled, "completed", true)]
    [InlineData(SpeakingExamState.Expired, "completed", true)]
    public async Task ExamRow_StatusAndRoute_FollowTheExamState(SpeakingExamState state, string status, bool opensResults)
    {
        await using var db = new LearnerDbContext(_options);
        AddExam(db, "spx_state", state, DateTimeOffset.UtcNow.AddHours(-1));
        await db.SaveChangesAsync();

        var row = Assert.Single(await HistoryAsync(db));

        Assert.Equal(status, row.Status);
        // A running exam resumes on its own page; a finished, cancelled or expired one opens its results.
        Assert.Equal(opensResults ? "/speaking/exam/spx_state/results" : "/speaking/exam/spx_state", row.Route);
        Assert.Equal(opensResults, row.SubmittedAt is not null);
    }

    [Fact]
    public async Task MockFundedExam_ReportsTheMockAllowance_AsItsOneCredit()
    {
        var started = DateTimeOffset.UtcNow.AddHours(-1);
        await using var db = new LearnerDbContext(_options);
        AddExam(db, "spx_mock", SpeakingExamState.Completed, started);
        db.AiPackageCreditTransactions.Add(new AiPackageCreditTransaction
        {
            Id = $"tx-{Guid.NewGuid():N}",
            UserId = UserId,
            AccountId = "acct-1",
            PackageType = "mock",
            MockExamsDelta = -1,
            Reason = AiPackageCreditReason.MockDeduct,
            ReferenceId = "exam:spx_mock:mock",
            CreatedAt = started,
        });
        await db.SaveChangesAsync();

        var row = Assert.Single(await HistoryAsync(db));

        Assert.Equal("mock", row.BalanceSource);
        Assert.Equal(1, row.CreditsUsed);
    }

    // ── Practice cards: their own results page ───────────────────────────

    [Fact]
    public async Task PracticeCard_OpensItsOwnResults_ShowsItsScoreOrTheMarkingNotice_AndMatchesItsOwnHold()
    {
        var started = DateTimeOffset.UtcNow.AddHours(-6);
        await using var db = new LearnerDbContext(_options);
        AddPractice(db, "sps_p_graded", SpeakingSessionState.Finished, AttemptState.Submitted, started);
        AddAssessment(db, "sps_p_graded", 350);
        AddDebit(db, "practice:sps_p_graded", 2, started);
        AddPractice(db, "sps_p_waiting", SpeakingSessionState.Finished, AttemptState.Submitted, started.AddMinutes(60));
        AddPractice(db, "sps_p_running", SpeakingSessionState.Active, AttemptState.InProgress, started.AddMinutes(120));
        AddPractice(db, "sps_p_tutor", SpeakingSessionState.Finished, AttemptState.Submitted, started.AddMinutes(180),
            mode: SpeakingSessionMode.LiveTutor);
        await db.SaveChangesAsync();

        var rows = (await HistoryAsync(db)).ToDictionary(item => item.AttemptId);

        var graded = rows["att_sps_p_graded"];
        Assert.Equal("completed", graded.Status);
        Assert.Equal("/speaking/sessions/sps_p_graded/results", graded.Route);
        Assert.Equal("350/500", graded.ResultLabel);
        Assert.Equal(2, graded.CreditsUsed);
        Assert.Equal("dedicated", graded.BalanceSource);

        var waiting = rows["att_sps_p_waiting"];
        Assert.Equal("/speaking/sessions/sps_p_waiting/results", waiting.Route);
        Assert.Equal("Marking in progress", waiting.ResultLabel);
        Assert.Equal(0, waiting.CreditsUsed);
        Assert.Null(waiting.BalanceSource);

        var running = rows["att_sps_p_running"];
        Assert.Equal("in_progress", running.Status);
        Assert.Equal("/speaking/sessions/sps_p_running", running.Route);
        Assert.Null(running.ResultLabel);

        // A human-marked session is not "in progress" for an AI result that never comes.
        var tutor = rows["att_sps_p_tutor"];
        Assert.Equal("/speaking/sessions/sps_p_tutor/results", tutor.Route);
        Assert.Null(tutor.ResultLabel);
    }

    [Fact]
    public async Task LegacySpeakingAttemptWithoutASession_IsUnchanged()
    {
        var started = DateTimeOffset.UtcNow.AddHours(-3);
        await using var db = new LearnerDbContext(_options);
        db.Attempts.Add(new Attempt
        {
            Id = "att_legacy",
            UserId = UserId,
            ContentId = "legacy-card",
            SubtestCode = "speaking",
            Context = "practice",
            Mode = "exam",
            State = AttemptState.Completed,
            StartedAt = started,
            SubmittedAt = started.AddMinutes(8),
        });
        // The old recorder flow named the content id in its ledger reference, and still matches that way.
        AddDebit(db, "grading:legacy-card:1", 2, started);
        await db.SaveChangesAsync();

        var row = Assert.Single(await HistoryAsync(db));

        Assert.Equal("att_legacy", row.AttemptId);
        Assert.Equal("legacy-card", row.Title);
        Assert.Equal("completed", row.Status);
        Assert.Equal("/speaking", row.Route);
        Assert.Equal("dedicated", row.BalanceSource);
        Assert.Equal(2, row.CreditsUsed);
        Assert.Null(row.ResultLabel);
    }

    // ── Paging and filtering with a collapsed mock ───────────────────────

    [Fact]
    public async Task CollapsedExamCards_NeverEatThePageSize()
    {
        var now = DateTimeOffset.UtcNow;
        await using var db = new LearnerDbContext(_options);
        AddExam(db, "spx_old", SpeakingExamState.Completed, now.AddHours(-120));
        AddExam(db, "spx_mid", SpeakingExamState.Completed, now.AddHours(-60));
        AddExam(db, "spx_new", SpeakingExamState.Completed, now.AddHours(-10));
        AddWriting(db, "writing-1", now.AddHours(-200));
        AddWriting(db, "writing-2", now.AddHours(-300));
        await db.SaveChangesAsync();

        var items = await HistoryAsync(db, limit: 4);

        // Six card attempts and two letters are stored, but the three mocks are three rows: the page of
        // four still fills with the four newest things, none of them a duplicate.
        Assert.Equal(
            new[] { "spx_new", "spx_mid", "spx_old", "writing-1" },
            items.Select(item => item.AttemptId).ToArray());
    }

    [Fact]
    public async Task SubtestFilter_Speaking_ListsTheMockAndThePractice_AndWritingNeverIncludesTheMock()
    {
        var now = DateTimeOffset.UtcNow;
        await using var db = new LearnerDbContext(_options);
        AddExam(db, "spx_filter", SpeakingExamState.Completed, now.AddHours(-3));
        AddPractice(db, "sps_filter", SpeakingSessionState.Finished, AttemptState.Submitted, now.AddHours(-2));
        AddWriting(db, "writing-filter", now.AddHours(-1));
        await db.SaveChangesAsync();

        var speaking = await HistoryAsync(db, subtest: " Speaking ");
        var writing = await HistoryAsync(db, subtest: "writing");

        Assert.Equal(new[] { "att_sps_filter", "spx_filter" }, speaking.Select(item => item.AttemptId).ToArray());
        Assert.All(speaking, item => Assert.Equal("speaking", item.Subtest));
        Assert.Equal(new[] { "writing-filter" }, writing.Select(item => item.AttemptId).ToArray());
    }

    [Fact]
    public async Task ReleasedHolds_AreNettedOff_SoACancelledOrAbandonedAttemptNeverClaimsTheCreditsItGotBack()
    {
        var now = DateTimeOffset.UtcNow;
        await using var db = new LearnerDbContext(_options);
        // A mock whose two holds were both released (cancelled, or never graded and swept): nothing was spent in the end.
        AddExam(db, "spx_refunded", SpeakingExamState.Cancelled, now.AddHours(-30));
        AddDebit(db, "exam:spx_refunded:cardA", 2, now.AddHours(-30));
        AddDebit(db, "exam:spx_refunded:cardB", 2, now.AddHours(-29));
        AddRefund(db, "exam:spx_refunded:cardA:release", 2, now.AddHours(-6));
        AddRefund(db, "exam:spx_refunded:cardB:release", 2, now.AddHours(-6));
        // Only Card A's hold came back: Card B still counts.
        AddExam(db, "spx_half_refunded", SpeakingExamState.Cancelled, now.AddHours(-20));
        AddDebit(db, "exam:spx_half_refunded:cardA", 2, now.AddHours(-20));
        AddDebit(db, "exam:spx_half_refunded:cardB", 2, now.AddHours(-19));
        AddRefund(db, "exam:spx_half_refunded:cardA:release", 2, now.AddHours(-5));
        // A practice card whose hold was released.
        AddPractice(db, "sps_refunded", SpeakingSessionState.Finished, AttemptState.Submitted, now.AddHours(-26));
        AddDebit(db, "practice:sps_refunded", 2, now.AddHours(-26));
        AddRefund(db, "practice:sps_refunded:release", 2, now.AddHours(-2));
        await db.SaveChangesAsync();

        var rows = (await HistoryAsync(db)).ToDictionary(item => item.AttemptId);

        Assert.Equal(0, rows["spx_refunded"].CreditsUsed);
        Assert.Null(rows["spx_refunded"].BalanceSource);
        Assert.Equal(2, rows["spx_half_refunded"].CreditsUsed);
        Assert.Equal("dedicated", rows["spx_half_refunded"].BalanceSource);
        Assert.Equal(0, rows["att_sps_refunded"].CreditsUsed);
        Assert.Null(rows["att_sps_refunded"].BalanceSource);
    }

    [Fact]
    public async Task AnExamThatHasNotBegunACard_AndAnotherLearnersExam_AreNotListed()
    {
        var now = DateTimeOffset.UtcNow;
        await using var db = new LearnerDbContext(_options);
        // Created and abandoned before any consent: no card attempt exists yet, so there is nothing to list.
        AddExam(db, "spx_untouched", SpeakingExamState.Intro, now.AddHours(-2), withCards: false);
        AddExam(db, "spx_someone_else", SpeakingExamState.Completed, now.AddHours(-1), userId: "someone-else");
        AddExam(db, "spx_mine", SpeakingExamState.Completed, now.AddHours(-3));
        await db.SaveChangesAsync();

        var items = await HistoryAsync(db);

        Assert.Equal(new[] { "spx_mine" }, items.Select(item => item.AttemptId).ToArray());
    }

    // ── Seeds ────────────────────────────────────────────────────────────

    /// <summary>An exam and (unless <paramref name="withCards"/> is false) its two card attempts and sessions,
    /// exactly as SpeakingExamService leaves them: Mode ai_exam, ids sps_{exam}_a / sps_{exam}_b.</summary>
    private static void AddExam(
        LearnerDbContext db,
        string examId,
        SpeakingExamState state,
        DateTimeOffset startedAt,
        double? combinedSnapshot = null,
        SpeakingExamMode mode = SpeakingExamMode.Ai,
        bool withCards = true,
        string userId = UserId)
    {
        db.SpeakingExamSessions.Add(new SpeakingExamSession
        {
            Id = examId,
            UserId = userId,
            Mode = mode,
            State = state,
            CardAId = "card-a",
            CardBId = "card-b",
            SessionAId = withCards ? $"sps_{examId}_a" : null,
            SessionBId = withCards ? $"sps_{examId}_b" : null,
            IntroStartedAt = startedAt,
            CompletedAt = SpeakingExamStates.IsTerminal(state) ? startedAt.AddMinutes(18) : null,
            CombinedScaledSnapshot = combinedSnapshot,
            CreatedAt = startedAt,
            UpdatedAt = startedAt,
        });
        if (!withCards)
        {
            return;
        }

        foreach (var slot in new[] { "a", "b" })
        {
            var cardStartedAt = startedAt.AddMinutes(slot == "a" ? 1 : 9);
            db.Attempts.Add(new Attempt
            {
                Id = $"att_{examId}_{slot}",
                UserId = userId,
                ContentId = $"content-{slot}",
                SubtestCode = "speaking",
                Context = "practice",
                Mode = "ai_exam",
                State = AttemptState.Submitted,
                StartedAt = cardStartedAt,
                SubmittedAt = cardStartedAt.AddMinutes(5),
            });
            db.SpeakingSessions.Add(new SpeakingSession
            {
                Id = $"sps_{examId}_{slot}",
                UserId = userId,
                RolePlayCardId = $"card-{slot}",
                ExamSessionId = examId,
                ExamSlot = slot,
                Mode = SpeakingSessionMode.AiExam,
                State = SpeakingSessionState.Finished,
                AttemptId = $"att_{examId}_{slot}",
                CreatedAt = cardStartedAt,
                UpdatedAt = cardStartedAt,
            });
        }
    }

    /// <summary>A standalone card: attempt "att_{sessionId}" and its session.</summary>
    private static void AddPractice(
        LearnerDbContext db,
        string sessionId,
        SpeakingSessionState sessionState,
        AttemptState attemptState,
        DateTimeOffset startedAt,
        SpeakingSessionMode mode = SpeakingSessionMode.AiSelfPractice)
    {
        var submitted = attemptState != AttemptState.InProgress;
        db.Attempts.Add(new Attempt
        {
            Id = $"att_{sessionId}",
            UserId = UserId,
            ContentId = $"content-{sessionId}",
            SubtestCode = "speaking",
            Context = "practice",
            Mode = SpeakingSessionModes.ToCode(mode),
            State = attemptState,
            StartedAt = startedAt,
            SubmittedAt = submitted ? startedAt.AddMinutes(6) : null,
        });
        db.SpeakingSessions.Add(new SpeakingSession
        {
            Id = sessionId,
            UserId = UserId,
            RolePlayCardId = "card-p",
            Mode = mode,
            State = sessionState,
            AttemptId = $"att_{sessionId}",
            CreatedAt = startedAt,
            UpdatedAt = startedAt,
        });
    }

    private static void AddWriting(LearnerDbContext db, string attemptId, DateTimeOffset startedAt)
        => db.Attempts.Add(new Attempt
        {
            Id = attemptId,
            UserId = UserId,
            ContentId = "content-writing",
            SubtestCode = "writing",
            Context = "practice",
            Mode = "exam",
            State = AttemptState.Completed,
            StartedAt = startedAt,
        });

    /// <summary>A Speaking-credits debit (a card hold) of <paramref name="credits"/>.</summary>
    private static void AddDebit(LearnerDbContext db, string reference, int credits, DateTimeOffset at)
        => db.AiPackageCreditTransactions.Add(new AiPackageCreditTransaction
        {
            Id = $"tx-{Guid.NewGuid():N}",
            UserId = UserId,
            AccountId = "acct-1",
            PackageType = "speaking",
            SpeakingOnlyCreditsDelta = -credits,
            Reason = AiPackageCreditReason.GradingDeduct,
            ReferenceId = reference,
            CreatedAt = at,
        });

    /// <summary>The ledger row a released hold leaves: a positive Speaking delta under "{reference}:release".</summary>
    private static void AddRefund(LearnerDbContext db, string releaseReference, int credits, DateTimeOffset at)
        => db.AiPackageCreditTransactions.Add(new AiPackageCreditTransaction
        {
            Id = $"tx-{Guid.NewGuid():N}",
            UserId = UserId,
            AccountId = "acct-1",
            PackageType = "speaking",
            SpeakingOnlyCreditsDelta = credits,
            Reason = AiPackageCreditReason.RefundOnFailure,
            ReferenceId = releaseReference,
            CreatedAt = at,
        });

    private static void AddAssessment(LearnerDbContext db, string sessionId, int score)
        => db.SpeakingAiAssessments.Add(new SpeakingAiAssessment
        {
            Id = $"spa_{sessionId}",
            SpeakingSessionId = sessionId,
            TranscriptId = "transcript-1",
            Provider = "ai_gateway",
            ModelId = "gateway-default",
            EstimatedScaledScore = score,
            ReadinessBand = "developing",
            GeneratedAt = DateTimeOffset.UtcNow,
        });

    private static void AddV11CardScore(LearnerDbContext db, string sessionId, int score)
        => db.SpeakingSimulationV11Assessments.Add(new SpeakingSimulationV11Assessment
        {
            Id = $"v11_{sessionId}",
            SpeakingSessionId = sessionId,
            AssessmentKind = "card",
            Status = SpeakingSimulationV11AssessmentStatus.Complete,
            EstimatedPracticeScore = score,
        });
}
