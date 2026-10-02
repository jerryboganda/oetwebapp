using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Ai;
using OetLearner.Api.Services.Rulebook;
using OetLearner.Api.Services.Writing;
using OetLearner.Api.Services.Writing.Configuration;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests.Writing;

/// <summary>
/// WAI-03 — the grade chain against the REAL AI control plane (<see cref="AiExecutionCoordinator"/>
/// + <see cref="AiOperationStore"/> on SQLite + <see cref="CoordinatedAiGatewayService"/>), with only
/// the provider core scripted. These are the lockouts that made "Retry" fail identically for ever:
/// a dead operation identity was replayed into the second attempt and into every Retry, and a
/// duplicate refusal was not failed over.
/// </summary>
public sealed class WritingGradeChainControlPlaneTests : IAsyncDisposable
{
    private static readonly Guid ScenarioId = Guid.Parse("c0ffee00-0000-4000-8000-0000000000a1");

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

    public WritingGradeChainControlPlaneTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<LearnerDbContext>().UseSqlite(_connection).Options;
        _db = new LearnerDbContext(_options);
        _db.Database.EnsureCreated();
        _db.WritingScenarios.Add(new WritingScenario
        {
            Id = ScenarioId,
            Title = "Control plane task",
            Profession = "medicine",
            LetterType = "routine_referral",
            Status = "published",
            AuthorId = "admin-1",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        _db.SaveChanges();
    }

    public async ValueTask DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    /// <summary>A1 — the first Max attempt times out client-side, so the coordinator records it
    /// Indeterminate. The second attempt must run on a NEW slot and grade, not be refused as a
    /// duplicate of the dead one (which also skipped the API and Codex routes).</summary>
    [Fact]
    public async Task A1_IndeterminateFirstAttempt_SecondAttemptRunsOnAFreshSlot_AndGrades()
    {
        var core = new ScriptedCore((call, _) => call == 1 ? throw new TaskCanceledException("client timeout", new TimeoutException()) : CanonicalCompletion);
        var submissionId = await SeedQueuedSubmissionAsync();

        await Pipeline(core).EvaluateAsync(submissionId, default);

        Assert.Equal(WritingSubmissionStatuses.Graded, await StatusAsync(submissionId));
        Assert.Equal(2, core.Calls);
        Assert.Contains(await _db.AiOperations.AsNoTracking().ToListAsync(), o => o.State == AiOperationState.Indeterminate);
    }

    /// <summary>A1b — the provider answered with an unreadable contract (a Completed operation).
    /// The run must fail over inside the same grade instead of failing, and must never be locked
    /// out by the Completed twin.</summary>
    [Fact]
    public async Task A1b_UnreadableFirstAnswer_FailsOverInsideTheSameRun()
    {
        var core = new ScriptedCore((call, _) => call == 1 ? "I cannot grade this letter." : CanonicalCompletion);
        var submissionId = await SeedQueuedSubmissionAsync();

        await Pipeline(core).EvaluateAsync(submissionId, default);

        Assert.Equal(WritingSubmissionStatuses.Graded, await StatusAsync(submissionId));
        Assert.Equal(2, core.Calls);
    }

    /// <summary>A1c — live 1 Oct 2026: one letter piled up 7 FailedTerminal operations across Retries
    /// and the coordinator's bounded replay walk ran out. Each run now grades on its own epoch's
    /// slots, so a run after a fully failed one starts clean on Max and grades.</summary>
    [Fact]
    public async Task A1c_ARunAfterAFullyFailedRun_StartsOnMaxOnFreshSlots_AndGrades()
    {
        var core = new ScriptedCore((call, _) => call <= 5 ? throw new InvalidOperationException("provider down") : CanonicalCompletion);
        var pipeline = Pipeline(core);
        var submissionId = await SeedQueuedSubmissionAsync();

        await Assert.ThrowsAnyAsync<Exception>(() => pipeline.EvaluateAsync(submissionId, default));
        Assert.Equal(WritingSubmissionStatuses.Queued, await StatusAsync(submissionId));
        await pipeline.EvaluateAsync(submissionId, default);

        Assert.Equal(WritingSubmissionStatuses.Graded, await StatusAsync(submissionId));
        Assert.Equal(6, core.Calls);
        var sixth = core.Requests[5];
        Assert.Equal(WritingSubscriptionProviders.Claude, sixth.Provider);
        Assert.Equal(WritingGradeChain.ResourceVersion(2, WritingGradeHop.ClaudeMax, 0), sixth.ResourceVersion);
        Assert.Equal(5, await _db.AiOperations.AsNoTracking().CountAsync(o => o.State == AiOperationState.FailedTerminal));
    }

    /// <summary>A3 — a deploy killed the grader mid-run: its claim and a Queued operation are left
    /// behind. The stale reclaim re-queues the letter and the next run grades it without waiting on
    /// (or replaying) the ghost operation.</summary>
    [Fact]
    public async Task A3_AQueuedGhostOfAKilledRun_NeverBlocksTheNextRun()
    {
        var core = new ScriptedCore((_, _) => CanonicalCompletion);
        var submissionId = await SeedQueuedSubmissionAsync();
        var row = await _db.WritingSubmissions.SingleAsync(s => s.Id == submissionId);
        row.Status = WritingSubmissionStatuses.Grading;
        row.ClaimOwner = "killed-host:1:run";
        row.ClaimedAt = DateTimeOffset.UtcNow - WritingGradeTimings.StaleClaimLease - TimeSpan.FromMinutes(1);
        row.GradeEpoch = 1;
        _db.AiOperations.Add(new AiOperation
        {
            Id = "ghost-op",
            Module = "writing",
            FeatureCode = AiFeatureCodes.WritingGrade,
            UserId = "chain-learner",
            ResourceId = submissionId.ToString("N"),
            ResourceType = "writing_submission",
            ResourceVersion = WritingGradeChain.ResourceVersion(1, WritingGradeHop.ClaudeMax, 0),
            IdempotencyKey = "ghost-key",
            State = AiOperationState.Queued,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-30),
            UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-30),
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        Assert.Equal(1, await WritingGradeRecovery.ReclaimStaleGradingAsync(_db, DateTimeOffset.UtcNow, default));
        await Pipeline(core).EvaluateAsync(submissionId, default);

        Assert.Equal(WritingSubmissionStatuses.Graded, await StatusAsync(submissionId));
        Assert.Equal(1, core.Calls);
        Assert.Equal(AiOperationState.Queued, (await _db.AiOperations.AsNoTracking().SingleAsync(o => o.Id == "ghost-op")).State);
    }

    /// <summary>A manual Retry is a new run: new epoch, new slots, starting on Max.</summary>
    [Fact]
    public async Task Retry_IsANewRun_OnANewEpoch_StartingOnMax()
    {
        var core = new ScriptedCore((call, _) => call <= 5 ? throw new InvalidOperationException("provider down") : CanonicalCompletion);
        var pipeline = Pipeline(core, maxAutoRetries: 0);
        var submissionId = await SeedQueuedSubmissionAsync();
        await Assert.ThrowsAnyAsync<Exception>(() => pipeline.EvaluateAsync(submissionId, default));
        Assert.Equal(WritingSubmissionStatuses.Failed, await StatusAsync(submissionId));

        await new WritingSubmissionService(_db, pipeline, NullLogger<WritingSubmissionService>.Instance, new EmptyHighlightStore())
            .RetryGradeAsync("chain-learner", submissionId, default);

        Assert.Equal(WritingSubmissionStatuses.Graded, await StatusAsync(submissionId));
        Assert.Equal(WritingSubscriptionProviders.Claude, core.Requests[5].Provider);
        Assert.Equal(WritingGradeChain.ResourceVersion(2, WritingGradeHop.ClaudeMax, 0), core.Requests[5].ResourceVersion);
    }

    private sealed class EmptyHighlightStore : IWritingCaseNoteHighlightService
    {
        public Task<string> GetAsync(string userId, Guid scenarioId, CancellationToken ct) => Task.FromResult("{}");
        public Task<string> SaveAsync(string userId, Guid scenarioId, string highlightsJson, CancellationToken ct) => Task.FromResult(highlightsJson);
    }

    // ── Harness ────────────────────────────────────────────────────────────────

    /// <summary>The production gateway stack; the operation store has its own context, as in
    /// production (one scope per control-plane write).</summary>
    private WritingSubmissionEvaluationPipeline Pipeline(ScriptedCore core, int maxAutoRetries = 4)
    {
        var coordinator = new AiExecutionCoordinator(
            new AiOperationStore(new LearnerDbContext(_options)),
            core,
            logger: NullLogger<AiExecutionCoordinator>.Instance);
        var gateway = new CoordinatedAiGatewayService(core, coordinator);
        return new WritingSubmissionEvaluationPipeline(
            _db,
            gateway,
            new EmptyCanonEngine(),
            mistakeService: null!,
            events: new NoopWritingEventBus(),
            TimeProvider.System,
            TestRuntimeSettingsProvider.FromWritingOptions(new WritingV2Options()),
            NullLogger<WritingSubmissionEvaluationPipeline>.Instance,
            subscriptionSelector: new WritingSubscriptionSelector(),
            assessmentPreflight: new PassThroughPreflight(),
            gradeChainOptions: Microsoft.Extensions.Options.Options.Create(new WritingGradeChainOptions { MaxAutoRetries = maxAutoRetries }));
    }

    private async Task<Guid> SeedQueuedSubmissionAsync()
    {
        var id = Guid.NewGuid();
        _db.WritingSubmissions.Add(new WritingSubmission
        {
            Id = id,
            UserId = "chain-learner",
            ScenarioId = ScenarioId,
            Mode = "practice",
            LetterContent = "Dear Dr Green,\n\nI am writing to refer Mr Lee for review.\n\nYours sincerely,\nDoctor",
            LetterContentHash = $"hash-{id:N}",
            WordCount = 18,
            Status = WritingSubmissionStatuses.Queued,
            GradingTier = "express",
            InputSource = "typed",
            StartedAt = DateTimeOffset.UtcNow,
            SubmittedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync();
        return id;
    }

    private async Task<string> StatusAsync(Guid id)
        => (await _db.WritingSubmissions.AsNoTracking().SingleAsync(s => s.Id == id)).Status;

    /// <summary>The provider core: call N (1-based) answers what the script returns, or throws.</summary>
    private sealed class ScriptedCore(Func<int, AiGatewayRequest, string> script) : IAiGatewayCoreExecutor
    {
        public int Calls { get; private set; }
        public List<AiGatewayRequest> Requests { get; } = new();

        public AiGroundedPrompt BuildGroundedPrompt(AiGroundingContext context)
            => new()
            {
                SystemPrompt = "# OET AI — Rulebook-Grounded System Prompt\n**This call concerns WRITING**",
                TaskInstruction = "score",
            };

        public Task<AiGatewayResult> CompleteAsync(AiGatewayRequest request, CancellationToken ct = default)
        {
            Calls++;
            Requests.Add(request);
            var completion = script(Calls, request);
            return Task.FromResult(new AiGatewayResult
            {
                Completion = completion,
                ResolvedProvider = request.Provider,
                ResolvedModel = request.Model,
                UsageRecordId = $"usage-{Calls}",
                UsagePersisted = true,
            });
        }
    }

    private sealed class PassThroughPreflight : IWritingAssessmentPreflightService
    {
        public Task<WritingAssessmentPreflightResult> ValidateAsync(WritingSubmission submission, CancellationToken ct)
            => Task.FromResult(new WritingAssessmentPreflightResult(
                true, WritingAssessmentV11Status.CandidateReady,
                Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(),
                "medicine", "routine_referral", "test", "task", "Patient name: Adam Lee\nAge: 54"));
    }
}
