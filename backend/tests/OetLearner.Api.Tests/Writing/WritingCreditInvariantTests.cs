using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Billing;
using OetLearner.Api.Services.Entitlements;
using OetLearner.Api.Services.Rulebook;
using OetLearner.Api.Services.Writing;
using OetLearner.Api.Services.Writing.Configuration;
using OetLearner.Api.Tests.Infrastructure;
// Both namespaces declare `AiCreditReservationService`; the Ai one is the Writing grade hold.
using AiCreditReservationService = OetLearner.Api.Services.Ai.AiCreditReservationService;

namespace OetLearner.Api.Tests.Writing;

/// <summary>
/// WAI-01 money path (owner decision 2 Oct 2026): a Writing letter is charged ONCE, when the task
/// is opened; a failed grade keeps that credit on the letter; Retry costs nothing; nothing is
/// refunded automatically. Everything here runs on the REAL ledger
/// (<see cref="AiPackageCreditService"/>), the real grade hold (<see cref="AiCreditReservationService"/>)
/// and the real start gate (<see cref="WritingEntitlementService"/>), so a second debit or a refund
/// shows up as a ledger row, not as a stub counter.
/// </summary>
public sealed class WritingCreditInvariantTests : IAsyncDisposable
{
    private const string UserId = "credit-learner";
    private static readonly Guid ScenarioId = Guid.Parse("c0ffee00-0000-4000-8000-000000000001");

    private const string Letter =
        "Dear Dr Green,\n\nI am writing to refer Mr Lee, aged 54, for review of his chest pain.\n\nYours sincerely,\nDoctor";

    private const string CanonicalCompletion = """
        {
          "findings": [],
          "criteriaScores": { "purpose": 3, "content": 6, "conciseness_clarity": 5, "genre_style": 7, "organisation_layout": 4, "language": 6 },
          "estimatedScaledScore": 380,
          "estimatedGrade": "B"
        }
        """;

    private readonly LearnerDbContext _db;
    private readonly AiPackageCreditService _ledger;
    private readonly WritingEntitlementService _entitlement;
    private readonly AiCreditReservationService _reservations;

    public WritingCreditInvariantTests()
    {
        _db = new LearnerDbContext(new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);
        _ledger = new AiPackageCreditService(_db, NullLogger<AiPackageCreditService>.Instance);
        _entitlement = new WritingEntitlementService(_db, new StubResolver(), new StubOptions(), _ledger);
        _reservations = new AiCreditReservationService(_db, _ledger, TimeProvider.System);
        _db.WritingScenarios.Add(new WritingScenario
        {
            Id = ScenarioId,
            Title = "Credit invariant task",
            Profession = "medicine",
            LetterType = "routine_referral",
            Status = "published",
            AuthorId = "admin-1",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        _db.SaveChanges();
    }

    public ValueTask DisposeAsync() => _db.DisposeAsync();

    /// <summary>T-C2 — the live symptom: a learner with exactly one letter of credit pays it when
    /// the task opens (balance 0), and the submit must still be graded, not refused with 402.</summary>
    [Fact]
    public async Task TC2_ExactlyOneLetterOfCredit_OpenSpendsIt_AndTheSubmitStillGrades()
    {
        await GrantWritingCreditsAsync(2);
        var pipeline = Pipeline(new ScriptedGateway(failFirst: 0));

        Assert.True((await OpenTaskAsync()).Allowed);
        Assert.Equal(0, await WritingCreditsLeftAsync());

        var submissionId = await SubmitAsync(pipeline);
        await pipeline.EvaluateAsync(submissionId, default);

        Assert.Equal(WritingSubmissionStatuses.Graded, await StatusAsync(submissionId));
        var debit = Assert.Single(await LedgerRowsAsync(AiPackageCreditReason.GradingDeduct));
        Assert.StartsWith("writing-v2:", debit.ReferenceId);
        Assert.Equal(0, await WritingCreditsLeftAsync());
    }

    /// <summary>T-C3 — fail, then Retry, then success: still exactly one 2-credit debit (the one taken
    /// at task open) and no refund row; the retry never re-charges and never grades for free on a
    /// refunded hold.</summary>
    [Fact]
    public async Task TC3_FailThenRetryThenSuccess_KeepsOneDebit_AndNoRefund()
    {
        // Four credits so the unfixed grade-time reservation could be funded too: the assertion is
        // about the NUMBER of ledger rows, not about running out.
        await GrantWritingCreditsAsync(4);
        var pipeline = Pipeline(new ScriptedGateway(failFirst: 1));

        Assert.True((await OpenTaskAsync()).Allowed);
        var submissionId = await SubmitAsync(pipeline);

        await Assert.ThrowsAsync<ApiException>(() => pipeline.EvaluateAsync(submissionId, default));
        await Service(pipeline).RetryGradeAsync(UserId, submissionId, default);

        Assert.Equal(WritingSubmissionStatuses.Graded, await StatusAsync(submissionId));
        Assert.Single(await LedgerRowsAsync(AiPackageCreditReason.GradingDeduct));
        Assert.Empty(await LedgerRowsAsync(AiPackageCreditReason.RefundOnFailure));
        Assert.Equal(2, await WritingCreditsLeftAsync());
    }

    // ── Harness ────────────────────────────────────────────────────────────────

    private WritingSubmissionEvaluationPipeline Pipeline(IAiGatewayService gateway)
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
            creditReservations: _reservations);

    private WritingSubmissionService Service(IWritingSubmissionEvaluationPipeline pipeline)
        => new(_db, pipeline, NullLogger<WritingSubmissionService>.Instance, new EmptyHighlightStore());

    private Task GrantWritingCreditsAsync(int credits)
        => _ledger.GrantPackageAsync(
            UserId,
            new BillingAddOn
            {
                Id = $"addon-{Guid.NewGuid():N}",
                Code = "pkg_writing_test",
                Name = "Writing test credits",
                Price = 1m,
                Currency = "GBP",
                Interval = "one_time",
                Status = BillingAddOnStatus.Active,
                DurationDays = 30,
                GrantEntitlementsJson = $$"""{"package_type":"writing","writing_only_credits":{{credits}}}""",
                AddonKind = "ai_package",
                AppliesToAllPlans = true,
                IsStackable = true,
                QuantityStep = 1,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            },
            1,
            $"cs-{Guid.NewGuid():N}",
            null,
            default);

    /// <summary>The "Practice this" start gate exactly as the eligibility endpoint runs it.</summary>
    private async Task<WritingStartAuthorization> OpenTaskAsync()
    {
        var reference = await _entitlement.BuildScenarioStartReferenceIdAsync(UserId, ScenarioId, default);
        return await _entitlement.AuthorizeStartAsync(UserId, reference, ScenarioId.ToString("D"), default);
    }

    private async Task<Guid> SubmitAsync(
        WritingSubmissionEvaluationPipeline pipeline, string letter = Letter, bool isRevision = false, Guid? originalId = null)
        => (await pipeline.SubmitAsync(new WritingSubmitAttempt(
            UserId: UserId,
            ScenarioId: ScenarioId,
            Mode: "practice",
            GradingTier: "express",
            InputSource: "typed",
            LetterContent: letter,
            TimeSpentSeconds: 600,
            StartedAt: DateTimeOffset.UtcNow.AddMinutes(-10),
            IsRevision: isRevision,
            OriginalSubmissionId: originalId,
            IdempotencyKey: Guid.NewGuid().ToString("N")), default)).SubmissionId;

    private async Task<string> StatusAsync(Guid submissionId)
        => (await _db.WritingSubmissions.AsNoTracking().SingleAsync(s => s.Id == submissionId)).Status;

    private async Task<int> WritingCreditsLeftAsync()
        => (await _ledger.GetSnapshotAsync(UserId, 0, default)).WritingOnlyCredits;

    private Task<List<AiPackageCreditTransaction>> LedgerRowsAsync(AiPackageCreditReason reason)
        => _db.AiPackageCreditTransactions.AsNoTracking()
            .Where(t => t.UserId == UserId && t.Reason == reason)
            .ToListAsync();

    /// <summary>The first <paramref name="failFirst"/> calls fail like a dropped provider; the rest grade.</summary>
    private sealed class ScriptedGateway(int failFirst) : IAiGatewayService
    {
        public List<AiGatewayRequest> Requests { get; } = new();

        public AiGroundedPrompt BuildGroundedPrompt(AiGroundingContext context)
            => new()
            {
                SystemPrompt = "# OET AI — Rulebook-Grounded System Prompt\n**This call concerns WRITING**",
                TaskInstruction = "score",
            };

        public Task<AiGatewayResult> CompleteAsync(AiGatewayRequest request, CancellationToken ct = default)
        {
            Requests.Add(request);
            if (Requests.Count <= failFirst) throw new InvalidOperationException("transient provider failure");
            return Task.FromResult(new AiGatewayResult { Completion = CanonicalCompletion, ResolvedModel = "claude-sonnet-5" });
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

    private sealed class EmptyHighlightStore : IWritingCaseNoteHighlightService
    {
        public Task<string> GetAsync(string userId, Guid scenarioId, CancellationToken ct) => Task.FromResult("{}");
        public Task<string> SaveAsync(string userId, Guid scenarioId, string highlightsJson, CancellationToken ct) => Task.FromResult(highlightsJson);
    }

    private sealed class StubResolver : IEffectiveEntitlementResolver
    {
        public Task<EffectiveEntitlementSnapshot> ResolveAsync(string? userId, CancellationToken ct)
            => Task.FromResult(new EffectiveEntitlementSnapshot(
                userId,
                false,
                false,
                "free",
                null, null, null, null, null, null, null,
                Array.Empty<string>(),
                false,
                Array.Empty<string>()));
    }

    private sealed class StubOptions : IWritingOptionsProvider
    {
        public Task<WritingOptions> GetAsync(CancellationToken ct)
            => Task.FromResult(new WritingOptions { FreeTierEnabled = false, FreeTierLimit = 1, FreeTierWindowDays = 7 });

        public Task<WritingOptions> UpdateAsync(WritingOptions update, string? adminId, CancellationToken ct)
            => Task.FromResult(update);
    }
}
