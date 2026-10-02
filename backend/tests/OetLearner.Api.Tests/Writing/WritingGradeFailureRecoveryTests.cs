using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Rulebook;
using OetLearner.Api.Services.Writing;
using OetLearner.Api.Services.Writing.Configuration;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests.Writing;

/// <summary>
/// WAI-03 — what a failed grading run leaves behind: a candidate-safe reason, an automatic
/// re-queue with back-off while retries remain, then a real <c>failed</c> row the learner can
/// Retry (unless Retry can never help). The failure write is fenced on the run's own claim.
/// </summary>
public sealed class WritingGradeFailureRecoveryTests : IAsyncDisposable
{
    private static readonly Guid ScenarioId = Guid.Parse("c0ffee00-0000-4000-8000-0000000000f1");
    private static readonly DateTimeOffset T0 = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);

    private const string CanonicalCompletion = """
        {
          "findings": [],
          "criteriaScores": { "purpose": 3, "content": 6, "conciseness_clarity": 5, "genre_style": 7, "organisation_layout": 4, "language": 6 },
          "estimatedScaledScore": 380,
          "estimatedGrade": "B"
        }
        """;

    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<LearnerDbContext> _options;
    private readonly LearnerDbContext _db;
    private readonly MutableTimeProvider _clock = new(T0);

    public WritingGradeFailureRecoveryTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<LearnerDbContext>().UseSqlite(_connection).Options;
        _db = new LearnerDbContext(_options);
        _db.Database.EnsureCreated();
        _db.WritingScenarios.Add(new WritingScenario
        {
            Id = ScenarioId,
            Title = "Recovery task",
            Profession = "medicine",
            LetterType = "LT-RR",
            TaskPromptMarkdown = "Write to Dr Green requesting a review.",
            Status = "published",
            AuthorId = "admin-1",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        _db.WritingScenarioStructuredSentences.Add(new WritingScenarioStructuredSentence
        {
            Id = Guid.NewGuid(),
            ScenarioId = ScenarioId,
            Ordinal = 1,
            SentenceText = "Asthma; allergy status negative.",
            RelevanceLabel = "relevant",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        _db.WritingAssessmentPackVersions.Add(new WritingAssessmentPackVersion
        {
            Id = Guid.NewGuid(),
            Profession = "medicine",
            LetterType = "routine_referral",
            VersionKey = "medicine-core-v11",
            Status = WritingAssessmentReleaseStatus.Approved,
            CandidateFacing = true,
        });
        _db.SaveChanges();
    }

    public async ValueTask DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Theory]
    [InlineData("ai_credits_insufficient", WritingGradeFailureCodes.CreditsInsufficient, true, false)]
    [InlineData("no_ai_package_credits", WritingGradeFailureCodes.CreditsInsufficient, true, false)]
    [InlineData("ai_platform_budget_exhausted", WritingGradeFailureCodes.ServicePaused, true, true)]
    [InlineData("writing_grading_paused", WritingGradeFailureCodes.ServicePaused, true, true)]
    [InlineData("writing_assessment_missing_input", WritingGradeFailureCodes.TaskNotReady, false, false)]
    [InlineData("writing_assessment_release_blocked", WritingGradeFailureCodes.TaskNotReady, false, false)]
    [InlineData("writing_assessment_requires_review", WritingGradeFailureCodes.ManualReview, false, false)]
    [InlineData("writing_submission_flagged", WritingGradeFailureCodes.ManualReview, false, false)]
    [InlineData("writing_submission_too_long", WritingGradeFailureCodes.LetterInvalid, false, false)]
    [InlineData("writing_rubric_failed", WritingGradeFailureCodes.GradingDelayed, true, true)]
    [InlineData("writing_canon_failed", WritingGradeFailureCodes.GradingDelayed, true, true)]
    [InlineData("writing_rubric_already_in_progress", WritingGradeFailureCodes.GradingDelayed, true, true)]
    public void Classify_MapsEveryOutcomeToACandidateSafeCode(string errorCode, string code, bool retryable, bool autoRetry)
    {
        var failure = WritingGradeRecovery.Classify(ApiException.Conflict(errorCode, "x"));

        Assert.Equal(new WritingGradeRecovery.Failure(code, retryable, autoRetry), failure);
        Assert.Equal(WritingGradeFailureCodes.GradingDelayed, WritingGradeRecovery.Classify(new InvalidOperationException("db down")).Code);
    }

    [Fact]
    public async Task RetryableFailures_RequeueWithBackoff_ThenEndFailedAndRetryable()
    {
        var pipeline = Pipeline(new SwitchGateway { Fail = true });
        var id = await SeedQueuedAsync();

        int[] backoff = [2, 5, 15, 30];
        for (var run = 1; run <= 4; run++)
        {
            await Assert.ThrowsAsync<ApiException>(() => pipeline.EvaluateAsync(id, default));
            var row = await RowAsync(id);
            Assert.Equal(WritingSubmissionStatuses.Queued, row.Status);
            Assert.Equal(run, row.AutoRetryCount);
            Assert.Equal(run, row.GradeEpoch);
            Assert.Equal(T0.AddMinutes(backoff[run - 1]), row.NextAutoRetryAt);
            Assert.Equal(WritingGradeFailureCodes.GradingDelayed, row.FailureCode);
            Assert.Null(row.ClaimOwner);
        }

        await Assert.ThrowsAsync<ApiException>(() => pipeline.EvaluateAsync(id, default));
        var spent = await RowAsync(id);
        Assert.Equal(WritingSubmissionStatuses.Failed, spent.Status);
        Assert.Equal(4, spent.AutoRetryCount);
        Assert.True(spent.FailureRetryable);
        Assert.Null(spent.NextAutoRetryAt);
        Assert.True(WritingV2ResponseMapper.ToSubmissionResponse(spent, T0).CanRetry);
    }

    [Fact]
    public async Task ManualRetry_ResetsTheCounters_AndGradesTheSameLetter()
    {
        var gateway = new SwitchGateway { Fail = true };
        var pipeline = Pipeline(gateway, new WritingGradeChainOptions { MaxAutoRetries = 0 });
        var id = await SeedQueuedAsync();
        await Assert.ThrowsAsync<ApiException>(() => pipeline.EvaluateAsync(id, default));
        Assert.Equal(WritingSubmissionStatuses.Failed, (await RowAsync(id)).Status);

        gateway.Fail = false;
        var response = await Service(pipeline).RetryGradeAsync("learner-1", id, default);

        var row = await RowAsync(id);
        Assert.Equal(WritingSubmissionStatuses.Graded, row.Status);
        Assert.Equal(0, row.AutoRetryCount);
        Assert.Null(row.FailureCode);
        Assert.Equal(2, row.GradeEpoch);
        Assert.Equal(1, await _db.WritingGrades.CountAsync(g => g.SubmissionId == id));
        Assert.Equal("graded", response.Status);
    }

    [Fact]
    public async Task NonRetryableFailure_RefusesRetry()
    {
        var id = await SeedQueuedAsync(status: WritingSubmissionStatuses.Failed, failureRetryable: false, failureCode: WritingGradeFailureCodes.TaskNotReady);

        var ex = await Assert.ThrowsAsync<ApiException>(
            () => Service(Pipeline(new SwitchGateway())).RetryGradeAsync("learner-1", id, default));

        Assert.Equal("writing_grade_retry_not_eligible", ex.ErrorCode);
        Assert.Equal(WritingSubmissionStatuses.Failed, (await RowAsync(id)).Status);
    }

    [Fact]
    public async Task ABlockedPreflight_FailsNonRetryable_AndTheDtoOffersNoRetry()
    {
        var unready = Guid.NewGuid();
        _db.WritingScenarios.Add(new WritingScenario
        {
            Id = unready,
            Title = "No case notes",
            Profession = "medicine",
            LetterType = "LT-RR",
            Status = "published",
            AuthorId = "admin-1",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync();
        var id = await SeedQueuedAsync(scenarioId: unready);

        await Assert.ThrowsAsync<ApiException>(() => Pipeline(new SwitchGateway()).EvaluateAsync(id, default));

        var row = await RowAsync(id);
        Assert.Equal(WritingSubmissionStatuses.Failed, row.Status);
        Assert.Equal(WritingGradeFailureCodes.TaskNotReady, row.FailureCode);
        Assert.False(row.FailureRetryable);
        Assert.False(WritingV2ResponseMapper.ToSubmissionResponse(row, T0).CanRetry);
    }

    [Fact]
    public async Task ALostClaim_IsFenced_TheStaleGraderCannotOverwriteTheNewOwner()
    {
        var id = await SeedQueuedAsync();
        var gateway = new SwitchGateway
        {
            Fail = true,
            // While this run is at the provider, its claim is reclaimed and another worker takes it.
            OnCall = () =>
            {
                using var other = new LearnerDbContext(_options);
                var row = other.WritingSubmissions.Single(s => s.Id == id);
                row.ClaimOwner = "other-worker";
                other.SaveChanges();
            },
        };

        await Assert.ThrowsAsync<ApiException>(() => Pipeline(gateway).EvaluateAsync(id, default));

        var after = await RowAsync(id);
        Assert.Equal(WritingSubmissionStatuses.Grading, after.Status);
        Assert.Equal("other-worker", after.ClaimOwner);
        Assert.Null(after.FailureCode);
    }

    [Fact]
    public async Task A9_ABlockedPlaceholderReport_IsReplacedByTheRealReport()
    {
        var id = await SeedQueuedAsync();
        _db.WritingAssessmentReportsV11.Add(new WritingAssessmentReportV11
        {
            Id = Guid.NewGuid(),
            SubmissionId = id,
            Status = WritingAssessmentV11Status.BlockedMissingInput,
        });
        await _db.SaveChangesAsync();

        await Pipeline(new SwitchGateway(), withReport: true).EvaluateAsync(id, default);

        Assert.Equal(WritingSubmissionStatuses.Graded, (await RowAsync(id)).Status);
        var report = await _db.WritingAssessmentReportsV11.AsNoTracking().SingleAsync(r => r.SubmissionId == id);
        Assert.NotEqual(WritingAssessmentV11Status.BlockedMissingInput, report.Status);
    }

    [Fact]
    public async Task WorkerShutdown_RequeuesAtOnce_WithoutSpendingAnAutoRetry()
    {
        var id = await SeedQueuedAsync();
        using var shutdown = new CancellationTokenSource();
        var gateway = new SwitchGateway { OnCall = shutdown.Cancel, HonourCancellation = true };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Pipeline(gateway).EvaluateAsync(id, shutdown.Token));

        var row = await RowAsync(id);
        Assert.Equal(WritingSubmissionStatuses.Queued, row.Status);
        Assert.Equal(0, row.AutoRetryCount);
        Assert.Equal(T0, row.NextAutoRetryAt);
        Assert.Null(row.ClaimOwner);
    }

    [Fact]
    public void Dto_ExposesOnlyCandidateSafeRecoveryState()
    {
        WritingSubmission Row(string status, Action<WritingSubmission>? tweak = null)
        {
            var row = new WritingSubmission
            {
                Id = Guid.NewGuid(),
                UserId = "learner-1",
                LetterContent = "x",
                LetterContentHash = "h",
                Status = status,
                SubmittedAt = T0.AddMinutes(-5),
                GradeEpoch = 3,
                FailureCode = WritingGradeFailureCodes.GradingDelayed,
            };
            tweak?.Invoke(row);
            return row;
        }

        var failedLegacy = WritingV2ResponseMapper.ToSubmissionResponse(Row("failed", r => r.FailureCode = null), T0);
        Assert.True(failedLegacy.CanRetry);
        var failedFinal = WritingV2ResponseMapper.ToSubmissionResponse(Row("failed", r => r.FailureRetryable = false), T0);
        Assert.False(failedFinal.CanRetry);
        Assert.Equal(WritingGradeFailureCodes.GradingDelayed, failedFinal.FailureCode);

        var autoRetry = WritingV2ResponseMapper.ToSubmissionResponse(
            Row("queued", r => { r.AutoRetryCount = 1; r.NextAutoRetryAt = T0.AddMinutes(2); }), T0);
        Assert.True(autoRetry.AutoRetrying);
        Assert.False(autoRetry.CanRetry);
        Assert.Equal(WritingGradeFailureCodes.GradingDelayed, autoRetry.FailureCode);
        Assert.Equal(3, autoRetry.AttemptCount);

        var staleQueue = WritingV2ResponseMapper.ToSubmissionResponse(
            Row("queued", r => r.NextAutoRetryAt = T0 - WritingGradeTimings.StaleClaimLease), T0);
        Assert.True(staleQueue.CanRetry);
        Assert.False(staleQueue.AutoRetrying);
        Assert.Null(staleQueue.FailureCode);

        Assert.True(WritingV2ResponseMapper.ToSubmissionResponse(
            Row("grading", r => r.ClaimedAt = T0 - WritingGradeTimings.StaleClaimLease), T0).CanRetry);
        Assert.False(WritingV2ResponseMapper.ToSubmissionResponse(
            Row("grading", r => r.ClaimedAt = T0.AddMinutes(-1)), T0).CanRetry);

        var graded = WritingV2ResponseMapper.ToSubmissionResponse(Row("graded"), T0);
        Assert.False(graded.CanRetry);
        Assert.Null(graded.FailureCode);
    }

    // ── Harness ────────────────────────────────────────────────────────────────

    private WritingSubmissionEvaluationPipeline Pipeline(
        IAiGatewayService gateway, WritingGradeChainOptions? chain = null, bool withReport = false)
        => new(
            _db,
            gateway,
            new EmptyCanonEngine(),
            mistakeService: null!,
            events: new NoopWritingEventBus(),
            _clock,
            TestRuntimeSettingsProvider.FromWritingOptions(new WritingV2Options()),
            NullLogger<WritingSubmissionEvaluationPipeline>.Instance,
            assessmentPreflight: new WritingAssessmentPreflightService(_db),
            assessmentRuleEngine: withReport ? new WritingAssessmentV11RuleEngine(new WritingRuleEngine(new RulebookLoader())) : null,
            calibrationReleaseService: withReport ? new WritingCalibrationReleaseService(_db) : null,
            gradeChainOptions: Microsoft.Extensions.Options.Options.Create(chain ?? new WritingGradeChainOptions()));

    private WritingSubmissionService Service(IWritingSubmissionEvaluationPipeline pipeline)
        => new(_db, pipeline, NullLogger<WritingSubmissionService>.Instance, new EmptyHighlightStore());

    private async Task<Guid> SeedQueuedAsync(
        string status = WritingSubmissionStatuses.Queued,
        bool? failureRetryable = null,
        string? failureCode = null,
        Guid? scenarioId = null)
    {
        var id = Guid.NewGuid();
        _db.WritingSubmissions.Add(new WritingSubmission
        {
            Id = id,
            UserId = "learner-1",
            ScenarioId = scenarioId ?? ScenarioId,
            Mode = "practice",
            LetterContent = "Dear Dr Green,\n\nI am writing to request a review of Mrs Smith, aged 54, who has asthma.\n\nYours sincerely,\nDoctor",
            LetterContentHash = $"hash-{id:N}",
            WordCount = 24,
            Status = status,
            FailureRetryable = failureRetryable,
            FailureCode = failureCode,
            GradingTier = "express",
            InputSource = "typed",
            StartedAt = T0,
            SubmittedAt = T0,
            CreatedAt = T0,
        });
        await _db.SaveChangesAsync();
        return id;
    }

    private Task<WritingSubmission> RowAsync(Guid id)
        => _db.WritingSubmissions.AsNoTracking().SingleAsync(s => s.Id == id);

    /// <summary>Grades, or fails like a dropped provider while <see cref="Fail"/> is set.</summary>
    private sealed class SwitchGateway : IAiGatewayService
    {
        public bool Fail { get; set; }
        public bool HonourCancellation { get; init; }
        public Action? OnCall { get; init; }

        public AiGroundedPrompt BuildGroundedPrompt(AiGroundingContext context)
            => new()
            {
                SystemPrompt = "# OET AI — Rulebook-Grounded System Prompt\n**This call concerns WRITING**",
                TaskInstruction = "score",
            };

        public Task<AiGatewayResult> CompleteAsync(AiGatewayRequest request, CancellationToken ct = default)
        {
            OnCall?.Invoke();
            if (HonourCancellation) ct.ThrowIfCancellationRequested();
            if (Fail) throw new InvalidOperationException("transient provider failure");
            return Task.FromResult(new AiGatewayResult { Completion = CanonicalCompletion, ResolvedModel = "claude-sonnet-5" });
        }
    }

    private sealed class EmptyHighlightStore : IWritingCaseNoteHighlightService
    {
        public Task<string> GetAsync(string userId, Guid scenarioId, CancellationToken ct) => Task.FromResult("{}");
        public Task<string> SaveAsync(string userId, Guid scenarioId, string highlightsJson, CancellationToken ct) => Task.FromResult(highlightsJson);
    }
}
