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
        var core = new ScriptedCore(_ => throw new TaskCanceledException("client timeout", new TimeoutException()));
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
        var core = new ScriptedCore(_ => "I cannot grade this letter.");
        var submissionId = await SeedQueuedSubmissionAsync();

        await Pipeline(core).EvaluateAsync(submissionId, default);

        Assert.Equal(WritingSubmissionStatuses.Graded, await StatusAsync(submissionId));
        Assert.Equal(2, core.Calls);
    }

    // ── Harness ────────────────────────────────────────────────────────────────

    /// <summary>The production gateway stack; the operation store has its own context, as in
    /// production (one scope per control-plane write).</summary>
    private WritingSubmissionEvaluationPipeline Pipeline(ScriptedCore core)
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
            subscriptionSelector: new MaxSelector(),
            assessmentPreflight: new PassThroughPreflight());
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

    /// <summary>The provider core: the FIRST call follows the script (a completion, or an
    /// exception), every later call grades.</summary>
    private sealed class ScriptedCore(Func<AiGatewayRequest, string> first) : IAiGatewayCoreExecutor
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
            var completion = Calls == 1 ? first(request) : CanonicalCompletion;
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

    private sealed class MaxSelector : IWritingSubscriptionSelector
    {
        public Task<WritingSubscriptionDecision> DecideAsync(CancellationToken ct)
            => Task.FromResult(new WritingSubscriptionDecision(
                WritingSubscriptionProviders.Claude, WritingSubscriptionProviders.ClaudeModel, "test", null, false));

        // Present only so this file also compiles against the pre-WAI-03 interface (red-first run).
        public Task RecordClaudeQuotaSignalAsync(CancellationToken ct) => Task.CompletedTask;
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
