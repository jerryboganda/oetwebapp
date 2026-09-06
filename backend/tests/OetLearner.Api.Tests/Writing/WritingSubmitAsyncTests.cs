using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Ai;
using OetLearner.Api.Services.AiManagement;
using OetLearner.Api.Services.Rulebook;
using OetLearner.Api.Services.Writing;
using OetLearner.Api.Services.Writing.Configuration;
using OetLearner.Api.Services.Writing.Events;

namespace OetLearner.Api.Tests.Writing;

/// <summary>
/// Interface-level tests for the SubmitGrading seam: one logical submit
/// action resolves to one submission behind SubmitAsync. See #189.
/// </summary>
public sealed class WritingSubmitAsyncTests : IAsyncDisposable
{
    private static readonly Guid ScenarioId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    private static readonly Guid ScenarioNursingId = Guid.Parse("bbbbbbbb-cccc-dddd-eeee-ffffffffffff");
    private static readonly Guid ScenarioReadyId = Guid.Parse("cccccccc-dddd-eeee-ffff-000000000000");
    private static readonly Guid ScenarioLtId = Guid.Parse("dddddddd-eeee-ffff-0000-111111111111");

    private readonly SqliteConnection _connection;
    private readonly LearnerDbContext _db;

    public WritingSubmitAsyncTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _db = new LearnerDbContext(new DbContextOptionsBuilder<LearnerDbContext>().UseSqlite(_connection).Options);
        _db.Database.EnsureCreated();
        _db.WritingScenarios.Add(new WritingScenario
        {
            Id = ScenarioId,
            Title = "Harness task",
            Profession = "medicine",
            LetterType = "routine_referral",
            Status = "published",
            AuthorId = "admin-1",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        // Nursing scenario WITH content but no approved pack: release-blocked.
        _db.WritingScenarios.Add(new WritingScenario
        {
            Id = ScenarioNursingId,
            Title = "Harness nursing task",
            Profession = "nursing",
            LetterType = "routine_referral",
            Status = "published",
            AuthorId = "admin-1",
            TaskPromptMarkdown = "Write a routine referral letter.",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        _db.WritingScenarioStructuredSentences.Add(new WritingScenarioStructuredSentence
        {
            Id = Guid.NewGuid(),
            ScenarioId = ScenarioNursingId,
            Ordinal = 0,
            SentenceText = "Patient is a 54-year-old man with chest pain.",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        // Medicine scenario WITH content + approved pack: gradable.
        _db.WritingScenarios.Add(new WritingScenario
        {
            Id = ScenarioReadyId,
            Title = "Harness ready task",
            Profession = "medicine",
            LetterType = "routine_referral",
            Status = "published",
            AuthorId = "admin-1",
            TaskPromptMarkdown = "Write a routine referral letter.",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        _db.WritingScenarioStructuredSentences.Add(new WritingScenarioStructuredSentence
        {
            Id = Guid.NewGuid(),
            ScenarioId = ScenarioReadyId,
            Ordinal = 0,
            SentenceText = "Patient is a 54-year-old man with chest pain.",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        _db.WritingAssessmentPackVersions.Add(new WritingAssessmentPackVersion
        {
            Id = Guid.NewGuid(),
            Profession = "medicine",
            LetterType = "routine_referral",
            VersionKey = "v1-test",
            Status = WritingAssessmentReleaseStatus.Approved,
            CandidateFacing = true,
            RulesJson = "{}",
            ApprovedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        // LT-catalogue scenario: LT-RR must resolve the routine_referral pack.
        _db.WritingScenarios.Add(new WritingScenario
        {
            Id = ScenarioLtId,
            Title = "Harness LT task",
            Profession = "medicine",
            LetterType = "LT-RR",
            Status = "published",
            AuthorId = "admin-1",
            TaskPromptMarkdown = "Write a routine referral letter.",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        _db.WritingScenarioStructuredSentences.Add(new WritingScenarioStructuredSentence
        {
            Id = Guid.NewGuid(),
            ScenarioId = ScenarioLtId,
            Ordinal = 0,
            SentenceText = "Patient is a 54-year-old man with chest pain.",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        _db.SaveChanges();
    }

    public async ValueTask DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task SubmitAsync_SameKeyTwice_ResolvesToSameSubmission_OneRow()
    {
        var pipeline = BuildPipeline();

        var first = await pipeline.SubmitAsync(SampleAttempt("seam-key-1", LetterA), default);
        var second = await pipeline.SubmitAsync(SampleAttempt("seam-key-1", LetterA), default);

        Assert.True(first.IsNew);
        Assert.False(second.IsNew);
        Assert.Equal(first.SubmissionId, second.SubmissionId);
        Assert.Equal(1, await _db.WritingSubmissions.CountAsync());
    }

    [Fact]
    public async Task SubmitAsync_SameContentDifferentKeys_ReusesRow()
    {
        var pipeline = BuildPipeline();

        var first = await pipeline.SubmitAsync(SampleAttempt("seam-key-a", LetterA), default);
        var second = await pipeline.SubmitAsync(SampleAttempt("seam-key-b", LetterA), default);

        Assert.Equal(first.SubmissionId, second.SubmissionId);
        Assert.Equal(1, await _db.WritingSubmissions.CountAsync());
    }

    [Fact]
    public async Task SubmitAsync_NewContentAfterTerminalSubmit_ThrowsLocked()
    {
        var pipeline = BuildPipeline();
        _db.WritingSubmissions.Add(new WritingSubmission
        {
            Id = Guid.NewGuid(),
            UserId = "learner-1",
            ScenarioId = ScenarioId,
            Mode = "practice",
            LetterContent = LetterA,
            LetterContentHash = "seedhash",
            WordCount = 10,
            TimeSpentSeconds = 40,
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-9),
            SubmittedAt = DateTimeOffset.UtcNow.AddMinutes(-8),
            IsRevision = false,
            OriginalSubmissionId = null,
            Status = "submitted",
            GradingTier = "express",
            InputSource = "typed",
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-8),
            IdempotencyKey = "seed-key",
        });
        await _db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ApiException>(
            () => pipeline.SubmitAsync(SampleAttempt("fresh-key", LetterB), default));

        Assert.Equal("writing_submission_locked", ex.Code);
    }

    [Fact]
    public async Task SubmitAsync_SameKeyTwice_EvaluatesToSingleGrade_OneProviderCall()
    {
        var gateway = new CountingGateway(CanonicalCompletion);
        var credits = new CountingReservations();
        var pipeline = BuildGradingPipeline(gateway, credits);

        var first = await pipeline.SubmitAsync(SampleAttempt("grade-key-1", LetterA), default);
        var second = await pipeline.SubmitAsync(SampleAttempt("grade-key-1", LetterA), default);

        var firstOutcome = await pipeline.EvaluateAsync(first.SubmissionId, default);
        var secondOutcome = await pipeline.EvaluateAsync(second.SubmissionId, default);

        Assert.Equal(firstOutcome.GradeId, secondOutcome.GradeId);
        Assert.Equal(1, gateway.Calls);
        Assert.Equal(1, await _db.WritingGrades.CountAsync());
    }

    [Fact]
    public async Task SubmitAsync_CheckTerminalLockFalse_AfterTerminal_CreatesNew()
    {
        var pipeline = BuildPipeline();
        await pipeline.SubmitAsync(SampleAttempt("mock-key-1", LetterA), default);
        MarkTerminal("learner-1", ScenarioId);

        // Mock surface: own session lifecycle, never the practice terminal lock.
        var mock = await pipeline.SubmitAsync(
            SampleAttempt("mock-key-2", LetterB, checkTerminalLock: false), default);

        Assert.True(mock.IsNew);
        Assert.Equal(2, await _db.WritingSubmissions.CountAsync());
    }

    [Fact]
    public async Task SubmitAsync_Revision_AfterTerminal_CreatesNew()
    {
        var pipeline = BuildPipeline();
        var original = await pipeline.SubmitAsync(SampleAttempt("rev-key-1", LetterA), default);
        MarkTerminal("learner-1", ScenarioId);

        var revision = await pipeline.SubmitAsync(new WritingSubmitAttempt(
            UserId: "learner-1",
            ScenarioId: ScenarioId,
            Mode: "practice",
            GradingTier: "express",
            InputSource: "typed",
            LetterContent: LetterB,
            TimeSpentSeconds: 40,
            StartedAt: DateTimeOffset.UtcNow.AddMinutes(-5),
            IsRevision: true,
            OriginalSubmissionId: original.SubmissionId,
            IdempotencyKey: null), default);

        Assert.True(revision.IsNew);
        Assert.Equal(2, await _db.WritingSubmissions.CountAsync());
    }

    [Fact]
    public async Task SeamMatrix_MissingTaskInput_BlocksWithMissingInput()
    {
        var pipeline = BuildRealPreflightPipeline(new CountingGateway(CanonicalCompletion), new CountingReservations());
        var submit = await pipeline.SubmitAsync(SampleAttempt("matrix-key-1", LetterA), default);

        var ex = await Assert.ThrowsAsync<ApiException>(
            () => pipeline.EvaluateAsync(submit.SubmissionId, default));

        Assert.Equal("writing_assessment_missing_input", ex.Code);
        Assert.Equal("failed", (await _db.WritingSubmissions.FindAsync(submit.SubmissionId))?.Status);
    }

    [Fact]
    public async Task SeamMatrix_ProfessionWithoutPack_BlocksWithReleaseBlocked()
    {
        var pipeline = BuildRealPreflightPipeline(new CountingGateway(CanonicalCompletion), new CountingReservations());
        var submit = await pipeline.SubmitAsync(
            SampleAttempt("matrix-key-2", LetterA, scenarioId: ScenarioNursingId), default);

        var ex = await Assert.ThrowsAsync<ApiException>(
            () => pipeline.EvaluateAsync(submit.SubmissionId, default));

        Assert.Equal("writing_assessment_release_blocked", ex.Code);
    }

    [Fact]
    public async Task SeamMatrix_ReadyTask_GradesWithOneProviderCall()
    {
        var gateway = new CountingGateway(CanonicalCompletion);
        var pipeline = BuildRealPreflightPipeline(gateway, new CountingReservations());
        var submit = await pipeline.SubmitAsync(
            SampleAttempt("matrix-key-3", LetterA, scenarioId: ScenarioReadyId), default);

        var outcome = await pipeline.EvaluateAsync(submit.SubmissionId, default);

        Assert.False(outcome.IdempotentReuse);
        Assert.Equal(1, gateway.Calls);
    }

    [Fact]
    public async Task SubmitAsync_PracticeAfterTerminalMock_CreatesNew()
    {
        var pipeline = BuildPipeline();
        _db.WritingSubmissions.Add(new WritingSubmission
        {
            Id = Guid.NewGuid(),
            UserId = "learner-1",
            ScenarioId = ScenarioId,
            Mode = "mock",
            LetterContent = LetterA,
            LetterContentHash = "mockhash",
            WordCount = 10,
            TimeSpentSeconds = 40,
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-9),
            SubmittedAt = DateTimeOffset.UtcNow.AddMinutes(-8),
            IsRevision = false,
            OriginalSubmissionId = null,
            Status = "graded",
            GradingTier = "express",
            InputSource = "typed",
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-8),
            IdempotencyKey = "mock-seed-key",
        });
        await _db.SaveChangesAsync();

        // A graded mock must not lock the learner out of practising the scenario.
        var practice = await pipeline.SubmitAsync(SampleAttempt("practice-after-mock", LetterB), default);

        Assert.True(practice.IsNew);
    }

    [Fact]
    public async Task SubmitAsync_MockShape_NullKey_DedupesByDerivedKey()
    {
        var pipeline = BuildPipeline();
        var attempt = new WritingSubmitAttempt(
            UserId: "learner-1",
            ScenarioId: ScenarioId,
            Mode: "mock",
            GradingTier: "express",
            InputSource: "typed",
            LetterContent: LetterA,
            TimeSpentSeconds: 40,
            StartedAt: DateTimeOffset.UtcNow.AddMinutes(-5),
            IsRevision: false,
            OriginalSubmissionId: null,
            IdempotencyKey: null,
            CheckTerminalLock: false);

        var first = await pipeline.SubmitAsync(attempt, default);
        var second = await pipeline.SubmitAsync(attempt, default);

        Assert.True(first.IsNew);
        Assert.False(second.IsNew);
        Assert.Equal(first.SubmissionId, second.SubmissionId);
    }

    [Fact]
    public async Task SeamMatrix_BlankLetter_GradesZero_NoProviderCall()
    {
        var gateway = new CountingGateway(CanonicalCompletion);
        var pipeline = BuildRealPreflightPipeline(gateway, new CountingReservations());
        var submit = await pipeline.SubmitAsync(
            SampleAttempt("matrix-blank-1", string.Empty, scenarioId: ScenarioReadyId), default);

        var outcome = await pipeline.EvaluateAsync(submit.SubmissionId, default);

        Assert.Equal(0, gateway.Calls);
        Assert.Equal(submit.SubmissionId, outcome.SubmissionId);
    }

    [Fact]
    public async Task SeamMatrix_LtCodeTask_ResolvesPackAndGrades()
    {
        var gateway = new CountingGateway(CanonicalCompletion);
        var preflight = new CountingPreflight(new WritingAssessmentPreflightService(_db));
        var pipeline = BuildRealPreflightPipeline(gateway, new CountingReservations(), preflight);
        var submit = await pipeline.SubmitAsync(
            SampleAttempt("matrix-lt-1", LetterA, scenarioId: ScenarioLtId), default);

        var outcome = await pipeline.EvaluateAsync(submit.SubmissionId, default);

        Assert.False(outcome.IdempotentReuse);
        Assert.Equal(1, gateway.Calls);
        Assert.Equal(1, preflight.Calls);
    }

    [Fact]
    public async Task SeamReserve_QuotaDenied_SurfacesInsufficient_WithFailedStatus()
    {
        var credits = new CountingReservations();
        var pipeline = BuildRealPreflightPipeline(new QuotaDeniedGateway(), credits);
        var submit = await pipeline.SubmitAsync(
            SampleAttempt("matrix-quota-1", LetterA, scenarioId: ScenarioReadyId), default);

        var ex = await Assert.ThrowsAsync<ApiException>(
            () => pipeline.EvaluateAsync(submit.SubmissionId, default));

        Assert.Equal("ai_credits_insufficient", ex.Code);
        Assert.Equal(402, ex.StatusCode);
        Assert.Equal("failed", (await _db.WritingSubmissions.FindAsync(submit.SubmissionId))?.Status);
        Assert.Equal(0, await _db.WritingGrades.CountAsync());
        Assert.Equal(1, credits.ReserveCalls);
        Assert.Equal(1, credits.ReleaseCalls);
    }

    [Fact]
    public async Task SeamReserve_RubricFailure_ReleasesReservation_AndFailsRetryable()
    {
        var credits = new CountingReservations();
        var pipeline = BuildRealPreflightPipeline(new ThrowingGateway(), credits);
        var submit = await pipeline.SubmitAsync(
            SampleAttempt("matrix-rubric-1", LetterA, scenarioId: ScenarioReadyId), default);

        var ex = await Assert.ThrowsAsync<ApiException>(
            () => pipeline.EvaluateAsync(submit.SubmissionId, default));

        Assert.Equal("writing_rubric_failed", ex.Code);
        Assert.True(ex.Retryable);
        Assert.Equal(503, ex.StatusCode);
        // Stuck-proofing invariant: a failure with no persisted grade MUST
        // land in failed (never wedge in grading), otherwise neither re-POST
        // (409 already-in-progress) nor retry-grade could ever resume it.
        Assert.Equal("failed", (await _db.WritingSubmissions.FindAsync(submit.SubmissionId))?.Status);
        Assert.Equal(1, credits.ReserveCalls);
        Assert.Equal(1, credits.ReleaseCalls);
        Assert.Equal(0, await _db.WritingGrades.CountAsync());
    }

    [Fact]
    public async Task FailedAttempt_ResumesToGraded_OnRetryGrade_WithoutDuplicateDebit()
    {
        var gateway = new FlakyGateway(CanonicalCompletion);
        var credits = new CountingReservations();
        var pipeline = BuildRealPreflightPipeline(gateway, credits);
        var submit = await pipeline.SubmitAsync(
            SampleAttempt("matrix-resume-1", LetterA, scenarioId: ScenarioReadyId), default);

        // First attempt: provider blows up mid-grade. No grade, failed row.
        var ex = await Assert.ThrowsAsync<ApiException>(
            () => pipeline.EvaluateAsync(submit.SubmissionId, default));
        Assert.Equal("writing_rubric_failed", ex.Code);
        Assert.Equal("failed", (await _db.WritingSubmissions.FindAsync(submit.SubmissionId))?.Status);

        // Resume grades the SAME attempt to completion: exactly one grade,
        // and the rubric request carries a sized token budget so finding-rich
        // letters cannot truncate into an unreadable contract.
        var outcome = await pipeline.EvaluateAsync(submit.SubmissionId, default);
        Assert.False(outcome.IdempotentReuse);
        Assert.Equal("graded", (await _db.WritingSubmissions.FindAsync(submit.SubmissionId))?.Status);
        Assert.Equal(1, await _db.WritingGrades.CountAsync(g => g.SubmissionId == submit.SubmissionId));
        Assert.Equal(2, gateway.Calls);
        Assert.NotNull(gateway.LastMaxTokens);
        Assert.True(gateway.LastMaxTokens >= 4000);
    }

    [Fact]
    public async Task SeamReserve_ReserveDenied_SurfacesInsufficient_NoRelease()
    {
        var credits = new DenyingReservations();
        var pipeline = BuildRealPreflightPipeline(new CountingGateway(CanonicalCompletion), credits);
        var submit = await pipeline.SubmitAsync(
            SampleAttempt("matrix-deny-1", LetterA, scenarioId: ScenarioReadyId), default);

        var ex = await Assert.ThrowsAsync<ApiException>(
            () => pipeline.EvaluateAsync(submit.SubmissionId, default));

        // Real AiCreditReservationService denies the same way: 402 with no
        // reservation opened, so there is nothing to release and no
        // failed-row write on this path.
        Assert.Equal("ai_credits_insufficient", ex.Code);
        Assert.Equal(402, ex.StatusCode);
        Assert.Equal(1, credits.ReserveCalls);
        Assert.Equal(0, credits.ReleaseCalls);
        Assert.Equal(0, await _db.WritingGrades.CountAsync());
    }

    private void MarkTerminal(string userId, Guid scenarioId)
    {
        foreach (var row in _db.WritingSubmissions.Where(
            s => s.UserId == userId && s.ScenarioId == scenarioId && !s.IsRevision))
        {
            row.Status = "submitted";
        }
        _db.SaveChanges();
    }

    private WritingSubmissionEvaluationPipeline BuildRealPreflightPipeline(
        IAiGatewayService gateway, IAiCreditReservationService credits,
        IWritingAssessmentPreflightService? preflight = null)
        => new(
            _db,
            gateway,
            new EmptyCanonEngine(),
            mistakeService: null!,
            events: new NoopWritingEventBus(),
            TimeProvider.System,
            TestRuntimeSettingsProvider.FromWritingOptions(new WritingV2Options()),
            NullLogger<WritingSubmissionEvaluationPipeline>.Instance,
            assessmentPreflight: preflight ?? new WritingAssessmentPreflightService(_db),
            creditReservations: credits);

    private sealed class CountingPreflight(IWritingAssessmentPreflightService inner) : IWritingAssessmentPreflightService
    {
        public int Calls { get; private set; }

        public Task<WritingAssessmentPreflightResult> ValidateAsync(WritingSubmission submission, CancellationToken ct)
        {
            Calls++;
            return inner.ValidateAsync(submission, ct);
        }
    }

    private WritingSubmissionEvaluationPipeline BuildPipeline()
        => new(
            _db,
            aiGateway: null!,
            canonEngine: null!,
            mistakeService: null!,
            events: new NoopWritingEventBus(),
            TimeProvider.System,
            settingsProvider: null!,
            NullLogger<WritingSubmissionEvaluationPipeline>.Instance);

    private static WritingSubmitAttempt SampleAttempt(string key, string letter)
        => SampleAttempt(key, letter, ScenarioId);

    private static WritingSubmitAttempt SampleAttempt(
        string key, string letter, Guid? scenarioId = null, bool checkTerminalLock = true)
        => new(
            UserId: "learner-1",
            ScenarioId: scenarioId ?? ScenarioId,
            Mode: "practice",
            GradingTier: "express",
            InputSource: "typed",
            LetterContent: letter,
            TimeSpentSeconds: 40,
            StartedAt: DateTimeOffset.UtcNow.AddMinutes(-5),
            IsRevision: false,
            OriginalSubmissionId: null,
            IdempotencyKey: key,
            CheckTerminalLock: checkTerminalLock);

    private const string LetterA =
        "Dear Dr Smith,\nRe: Mr Jones\n\nI am writing to refer Mr Jones.\n\nYours sincerely,\nDoctor";

    private const string LetterB =
        "Dear Dr Smith,\nRe: Mrs Jones\n\nI am writing to refer Mrs Jones urgently.\n\nYours sincerely,\nDoctor";

    private WritingSubmissionEvaluationPipeline BuildGradingPipeline(
        IAiGatewayService gateway, CountingReservations credits)
        => new(
            _db,
            gateway,
            new EmptyCanonEngine(),
            mistakeService: null!,
            events: new NoopWritingEventBus(),
            TimeProvider.System,
            TestRuntimeSettingsProvider.FromWritingOptions(new WritingV2Options()),
            NullLogger<WritingSubmissionEvaluationPipeline>.Instance,
            assessmentPreflight: new PassThroughPreflight(),
            creditReservations: credits);

    private const string CanonicalCompletion = """
        {
          "findings": [],
          "criteriaScores": { "purpose": 3, "content": 6, "conciseness_clarity": 5, "genre_style": 7, "organisation_layout": 4, "language": 6 },
          "estimatedScaledScore": 380,
          "estimatedGrade": "B"
        }
        """;

    /// <summary>Throws once (transient provider failure), then serves the canonical contract.</summary>
    private sealed class FlakyGateway(string completion) : IAiGatewayService
    {
        public int Calls { get; private set; }
        public int? LastMaxTokens { get; private set; }

        public AiGroundedPrompt BuildGroundedPrompt(AiGroundingContext context)
            => new()
            {
                SystemPrompt = "# OET AI — Rulebook-Grounded System Prompt\n**This call concerns WRITING**",
                TaskInstruction = "score",
            };

        public Task<AiGatewayResult> CompleteAsync(AiGatewayRequest request, CancellationToken ct = default)
        {
            Calls++;
            LastMaxTokens = request.MaxTokens;
            if (Calls == 1) throw new InvalidOperationException("transient provider failure");
            return Task.FromResult(new AiGatewayResult
            {
                Completion = completion,
                ResolvedModel = "claude-sonnet-5",
            });
        }
    }

    private sealed class CountingGateway(string completion) : IAiGatewayService
    {
        public int Calls { get; private set; }

        public AiGroundedPrompt BuildGroundedPrompt(AiGroundingContext context)
            => new()
            {
                SystemPrompt = "# OET AI — Rulebook-Grounded System Prompt\n**This call concerns WRITING**",
                TaskInstruction = "score",
            };

        public Task<AiGatewayResult> CompleteAsync(AiGatewayRequest request, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(new AiGatewayResult
            {
                Completion = completion,
                ResolvedModel = "claude-sonnet-5",
            });
        }
    }

    private sealed class CountingReservations : IAiCreditReservationService
    {
        public int ReserveCalls { get; private set; }
        public int CommitCalls { get; private set; }
        public int ReleaseCalls { get; private set; }

        public Task<AiCreditReservationTicket> ReserveWritingAsync(
            string userId, string operationId, string businessReference, CancellationToken ct)
        {
            ReserveCalls++;
            return Task.FromResult(new AiCreditReservationTicket(
                "res-1", operationId, "writing", 1, AiCreditReservationState.Reserved, false));
        }

        public Task<AiCreditReservationTicket> ReserveSpeakingAsync(
            string userId, string operationId, string businessReference, CancellationToken ct)
            => ReserveWritingAsync(userId, operationId, businessReference, ct);

        public Task CommitAsync(string reservationId, CancellationToken ct)
        {
            CommitCalls++;
            return Task.CompletedTask;
        }

        public Task CommitByBusinessReferenceAsync(string businessReference, CancellationToken ct)
            => CommitAsync("res-1", ct);

        public Task ReleaseAsync(string reservationId, CancellationToken ct)
        {
            ReleaseCalls++;
            return Task.CompletedTask;
        }
    }

    private sealed class DenyingReservations : IAiCreditReservationService
    {
        public int ReserveCalls { get; private set; }
        public int ReleaseCalls { get; private set; }

        public Task<AiCreditReservationTicket> ReserveWritingAsync(
            string userId, string operationId, string businessReference, CancellationToken ct)
        {
            ReserveCalls++;
            throw ApiException.PaymentRequired(
                "ai_credits_insufficient",
                "You have no AI grading credits remaining. Purchase an AI Credits package to continue.");
        }

        public Task<AiCreditReservationTicket> ReserveSpeakingAsync(
            string userId, string operationId, string businessReference, CancellationToken ct)
            => ReserveWritingAsync(userId, operationId, businessReference, ct);

        public Task CommitAsync(string reservationId, CancellationToken ct)
            => Task.CompletedTask;

        public Task CommitByBusinessReferenceAsync(string businessReference, CancellationToken ct)
            => Task.CompletedTask;

        public Task ReleaseAsync(string reservationId, CancellationToken ct)
        {
            ReleaseCalls++;
            return Task.CompletedTask;
        }
    }

    private sealed class QuotaDeniedGateway : IAiGatewayService
    {
        public AiGroundedPrompt BuildGroundedPrompt(AiGroundingContext context)
            => new() { SystemPrompt = "score", TaskInstruction = "score" };

        public Task<AiGatewayResult> CompleteAsync(AiGatewayRequest request, CancellationToken ct = default)
            => throw new AiQuotaDeniedException("ai_quota_exhausted", "denied");
    }

    private sealed class ThrowingGateway : IAiGatewayService
    {
        public AiGroundedPrompt BuildGroundedPrompt(AiGroundingContext context)
            => new() { SystemPrompt = "score", TaskInstruction = "score" };

        public Task<AiGatewayResult> CompleteAsync(AiGatewayRequest request, CancellationToken ct = default)
            => throw new InvalidOperationException("provider boom");
    }

    private sealed class EmptyCanonEngine : IWritingCanonEngine
    {
        public Task<WritingCanonDetectionResult> DetectViolationsAsync(WritingCanonDetectionRequest request, CancellationToken ct)
            => Task.FromResult(new WritingCanonDetectionResult(request.SubmissionId, Array.Empty<WritingCanonViolation>()));

        public Task<WritingCanonRuleTestResponse?> TestRuleAsync(string adminUserId, string ruleId, WritingCanonRuleTestRequest request, CancellationToken ct)
            => throw new NotImplementedException();
    }

    private sealed class PassThroughPreflight : IWritingAssessmentPreflightService
    {
        public Task<WritingAssessmentPreflightResult> ValidateAsync(WritingSubmission submission, CancellationToken ct)
            => Task.FromResult(new WritingAssessmentPreflightResult(
                true, WritingAssessmentV11Status.CandidateReady,
                Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(),
                "medicine", "routine_referral", "test", "task", "Patient name: John Jones\nAge: 54"));
    }

    private sealed class NoopWritingEventBus : IWritingEventBus
    {
        public Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct = default)
            where TEvent : WritingEvent
            => Task.CompletedTask;
    }
}
