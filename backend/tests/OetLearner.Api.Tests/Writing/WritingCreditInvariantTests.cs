using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.AiManagement;
using OetLearner.Api.Services.Billing;
using OetLearner.Api.Services.Entitlements;
using OetLearner.Api.Services.FreeSamples;
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
            TaskPromptMarkdown = "Write a routine referral letter.",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        _db.WritingScenarioStructuredSentences.Add(new WritingScenarioStructuredSentence
        {
            Id = Guid.NewGuid(),
            ScenarioId = ScenarioId,
            Ordinal = 0,
            SentenceText = "Mr Lee, 54, chest pain on exertion.",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        _db.SaveChanges();
    }

    public ValueTask DisposeAsync() => _db.DisposeAsync();

    /// <summary>T-C1 — open + submit = exactly one 2-credit debit, and the grade hold is committed
    /// on that same reference with no second debit of its own.</summary>
    [Fact]
    public async Task TC1_OpenAndSubmit_ChargeExactlyOnce_AndTheHoldCommitsOnTheSameReference()
    {
        await GrantWritingCreditsAsync(6);
        var pipeline = Pipeline(new ScriptedGateway(failFirst: 0));

        var start = await OpenTaskAsync();
        Assert.True(start.Charged);
        var submissionId = await SubmitAsync(pipeline);
        await pipeline.EvaluateAsync(submissionId, default);

        var debit = Assert.Single(await LedgerRowsAsync(AiPackageCreditReason.GradingDeduct));
        Assert.Equal(-2, debit.WritingOnlyCreditsDelta);
        var hold = Assert.Single(await _db.AiCreditReservations.AsNoTracking().ToListAsync());
        Assert.Equal(debit.ReferenceId, hold.BusinessReference);
        Assert.Equal(AiCreditReservationState.Committed, hold.State);
        Assert.Equal(0, hold.Units);
        Assert.Equal(debit.ReferenceId, (await SubmissionAsync(submissionId)).CreditReference);
        Assert.Equal(4, await WritingCreditsLeftAsync());
    }

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
        // No automatic re-queue here: the learner presses Retry on a failed letter.
        var pipeline = Pipeline(new ScriptedGateway(failFirst: 1), new WritingGradeChainOptions { MaxAutoRetries = 0 });

        Assert.True((await OpenTaskAsync()).Allowed);
        var submissionId = await SubmitAsync(pipeline);

        await Assert.ThrowsAsync<ApiException>(() => pipeline.EvaluateAsync(submissionId, default));
        Assert.Equal(WritingSubmissionStatuses.Failed, await StatusAsync(submissionId));
        Assert.Equal(AiCreditReservationState.Reserved, (await HoldAsync(submissionId)).State);
        await Service(pipeline).RetryGradeAsync(UserId, submissionId, default);

        Assert.Equal(WritingSubmissionStatuses.Graded, await StatusAsync(submissionId));
        Assert.Single(await LedgerRowsAsync(AiPackageCreditReason.GradingDeduct));
        Assert.Empty(await LedgerRowsAsync(AiPackageCreditReason.RefundOnFailure));
        Assert.Equal(AiCreditReservationState.Committed, (await HoldAsync(submissionId)).State);
        Assert.Equal(2, await WritingCreditsLeftAsync());
    }

    /// <summary>T-C3 (auto-requeue): the failed run is re-queued with its hold kept, and the sweep's
    /// next run grades it — still one debit, no refund.</summary>
    [Fact]
    public async Task TC3_AutoRequeue_KeepsTheHold_AndTheNextRunGradesOnTheSameDebit()
    {
        await GrantWritingCreditsAsync(4);
        var pipeline = Pipeline(new ScriptedGateway(failFirst: 1));
        await OpenTaskAsync();
        var submissionId = await SubmitAsync(pipeline);

        await Assert.ThrowsAsync<ApiException>(() => pipeline.EvaluateAsync(submissionId, default));
        var requeued = await SubmissionAsync(submissionId);
        Assert.Equal(WritingSubmissionStatuses.Queued, requeued.Status);
        Assert.Equal(1, requeued.AutoRetryCount);
        Assert.Equal(AiCreditReservationState.Reserved, (await HoldAsync(submissionId)).State);

        await pipeline.EvaluateAsync(submissionId, default);

        Assert.Equal(WritingSubmissionStatuses.Graded, await StatusAsync(submissionId));
        Assert.Single(await LedgerRowsAsync(AiPackageCreditReason.GradingDeduct));
        Assert.Empty(await LedgerRowsAsync(AiPackageCreditReason.RefundOnFailure));
        Assert.Equal(2, await WritingCreditsLeftAsync());
    }

    /// <summary>T-C4 — refresh / resume / reopen of the paid attempt costs nothing and works at a
    /// zero balance (the start gate used to answer premium_required once the last credit was spent).</summary>
    [Fact]
    public async Task TC4_RefreshAndResume_AreFree_EvenAtZeroBalance()
    {
        await GrantWritingCreditsAsync(2);

        Assert.True((await OpenTaskAsync()).Charged);
        var refresh = await OpenTaskAsync();
        var resume = await OpenTaskAsync();

        Assert.True(refresh.Allowed);
        Assert.False(refresh.Charged);
        Assert.True(resume.Allowed);
        Assert.Single(await LedgerRowsAsync(AiPackageCreditReason.GradingDeduct));
        Assert.Equal(0, await WritingCreditsLeftAsync());
    }

    /// <summary>T-C5 — Revise &amp; Resubmit is a new letter: 2 more credits at grade time, under its
    /// own reference.</summary>
    [Fact]
    public async Task TC5_Revision_CostsTwoMoreCredits_AtGradeTime()
    {
        await GrantWritingCreditsAsync(4);
        var pipeline = Pipeline(new ScriptedGateway(failFirst: 0));
        await OpenTaskAsync();
        var originalId = await SubmitAsync(pipeline);
        await pipeline.EvaluateAsync(originalId, default);

        var revisionId = await SubmitAsync(pipeline, Letter + "\nRevised.", isRevision: true, originalId: originalId);
        await pipeline.EvaluateAsync(revisionId, default);

        var debits = await LedgerRowsAsync(AiPackageCreditReason.GradingDeduct);
        Assert.Equal(2, debits.Count);
        Assert.Contains(debits, d => d.ReferenceId == $"writing-grade:{revisionId:N}");
        Assert.Equal(WritingSubmissionStatuses.Graded, await StatusAsync(revisionId));
        Assert.Equal(0, await WritingCreditsLeftAsync());
    }

    /// <summary>T-C6 — the free sample: two free results write zero ledger rows; a third letter on
    /// the sample is not free, and with no credits it is refused.</summary>
    [Fact]
    public async Task TC6_FreeSample_TwoFreeResults_ZeroLedgerRows_ThirdIsRefused()
    {
        await EnableFreeSamplesAsync();
        var pipeline = Pipeline(new ScriptedGateway(failFirst: 0));

        var firstId = await SubmitAsync(pipeline);
        await pipeline.EvaluateAsync(firstId, default);
        var secondId = await SubmitAsync(pipeline, Letter + "\nSecond.", isRevision: true, originalId: firstId);
        await pipeline.EvaluateAsync(secondId, default);
        var thirdId = await SubmitAsync(pipeline, Letter + "\nThird.", isRevision: true, originalId: firstId);
        var refused = await Assert.ThrowsAsync<ApiException>(() => pipeline.EvaluateAsync(thirdId, default));

        Assert.Equal(WritingSubmissionStatuses.Graded, await StatusAsync(firstId));
        Assert.Equal(WritingSubmissionStatuses.Graded, await StatusAsync(secondId));
        Assert.Equal("ai_credits_insufficient", refused.ErrorCode);
        Assert.Empty(await _db.AiPackageCreditTransactions.AsNoTracking().ToListAsync());
        Assert.All(
            await _db.AiCreditReservations.AsNoTracking().ToListAsync(),
            r => Assert.Equal(("free_sample", 0), (r.BucketKind, r.Units)));
    }

    /// <summary>T-C7 — Unlimited Writing: open and grade write zero ledger rows; the hold is 0 units.</summary>
    [Fact]
    public async Task TC7_Unlimited_WritesNoLedgerRows()
    {
        await GrantUnlimitedAsync();
        var pipeline = Pipeline(new ScriptedGateway(failFirst: 0));

        Assert.Equal("unlimited", (await OpenTaskAsync()).EntitlementSource);
        var submissionId = await SubmitAsync(pipeline);
        await pipeline.EvaluateAsync(submissionId, default);

        Assert.Equal(WritingSubmissionStatuses.Graded, await StatusAsync(submissionId));
        Assert.Empty(await LedgerRowsAsync(AiPackageCreditReason.GradingDeduct));
        Assert.Equal(0, (await HoldAsync(submissionId)).Units);
    }

    /// <summary>T-C8 — a letter submitted without opening the task (API-only) pays at grade time under
    /// the START reference; the start gate run afterwards dedupes to already-paid.</summary>
    [Fact]
    public async Task TC8_GradeFirst_PaysTheStartReference_AndTheLaterStartGateDedupes()
    {
        await GrantWritingCreditsAsync(4);
        var pipeline = Pipeline(new ScriptedGateway(failFirst: 1));
        var submissionId = await SubmitAsync(pipeline);
        await Assert.ThrowsAsync<ApiException>(() => pipeline.EvaluateAsync(submissionId, default));

        var debit = Assert.Single(await LedgerRowsAsync(AiPackageCreditReason.GradingDeduct));
        Assert.Equal(await _entitlement.BuildScenarioStartReferenceIdAsync(UserId, ScenarioId, default), debit.ReferenceId);

        var start = await OpenTaskAsync();
        Assert.True(start.Allowed);
        Assert.False(start.Charged);
        Assert.Single(await LedgerRowsAsync(AiPackageCreditReason.GradingDeduct));
        Assert.Equal(2, await WritingCreditsLeftAsync());
    }

    /// <summary>T-C9 — a credit-funded grade is sent with the plan-gate bypass, and the real quota
    /// service then lets writing.grade through on a plan that does not list it (the live
    /// feature_not_in_plan refusals of 1 Oct 2026). Without the grant the same plan refuses it.</summary>
    [Fact]
    public async Task TC9_CreditFundedGrade_BypassesThePlanFeatureGate()
    {
        await GrantWritingCreditsAsync(2);
        var gateway = new ScriptedGateway(failFirst: 0);
        var pipeline = Pipeline(gateway);
        await OpenTaskAsync();
        await pipeline.EvaluateAsync(await SubmitAsync(pipeline), default);
        Assert.True(gateway.Requests[^1].FreeSampleGrant);
        Assert.False(string.IsNullOrEmpty(gateway.Requests[^1].CreditReservationId));

        _db.AiQuotaPlans.Add(new AiQuotaPlan
        {
            Id = "plan-free-test",
            Code = "free",
            Name = "Free",
            AllowedFeaturesCsv = "conversation.reply",
            MonthlyTokenCap = 50_000,
            DailyTokenCap = 50_000,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        _db.AiUserQuotaOverrides.Add(new AiUserQuotaOverride
        {
            UserId = UserId,
            ForcePlanCode = "free",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync();
        var quota = new AiQuotaService(_db, new MemoryCache(new MemoryCacheOptions()), NullLogger<AiQuotaService>.Instance, new StubResolver());

        var funded = await quota.TryReserveAsync(UserId, AiFeatureCodes.WritingGrade, AiKeySource.Platform, freeSampleGrant: true, default);
        var unfunded = await quota.TryReserveAsync(UserId, AiFeatureCodes.WritingGrade, AiKeySource.Platform, freeSampleGrant: false, default);

        Assert.True(funded.Allowed);
        Assert.False(unfunded.Allowed);
        Assert.Equal("feature_not_in_plan", unfunded.ErrorCode);
    }

    /// <summary>Legacy (before 2 Oct 2026): the failed grade's own debit was refunded and its hold
    /// Released. Its retry adopts the start debit, which still stands — no new charge, no free grade
    /// on the refunded hold, which stays Released as history.</summary>
    [Fact]
    public async Task Legacy_ReleasedHold_RetryAdoptsTheStartDebit_WithNoNewCharge()
    {
        await GrantWritingCreditsAsync(4);
        var pipeline = Pipeline(new ScriptedGateway(failFirst: 0));
        await OpenTaskAsync();
        var submissionId = await SubmitAsync(pipeline);
        var legacyReference = $"writing-grade:{submissionId:N}";
        await _ledger.DeductGradingCreditAsync(UserId, "writing", legacyReference, default);
        await _ledger.RefundAsync(UserId, legacyReference, $"{legacyReference}:release", "legacy release", default);
        _db.AiOperations.Add(new AiOperation
        {
            Id = "op-legacy",
            Module = "writing",
            FeatureCode = "writing.score.v1",
            UserId = UserId,
            IdempotencyKey = legacyReference,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        _db.AiCreditReservations.Add(new AiCreditReservation
        {
            Id = "res-legacy",
            OperationId = "op-legacy",
            UserId = UserId,
            BucketKind = "writing",
            Units = 2,
            State = AiCreditReservationState.Released,
            BusinessReference = legacyReference,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        var row = await _db.WritingSubmissions.SingleAsync(s => s.Id == submissionId);
        row.Status = WritingSubmissionStatuses.Failed;
        await _db.SaveChangesAsync();

        await Service(pipeline).RetryGradeAsync(UserId, submissionId, default);

        Assert.Equal(WritingSubmissionStatuses.Graded, await StatusAsync(submissionId));
        Assert.Equal(2, (await LedgerRowsAsync(AiPackageCreditReason.GradingDeduct)).Count);
        Assert.Equal(2, await WritingCreditsLeftAsync());
        Assert.StartsWith("writing-v2:", (await SubmissionAsync(submissionId)).CreditReference);
        Assert.Equal(AiCreditReservationState.Released,
            (await _db.AiCreditReservations.AsNoTracking().SingleAsync(r => r.Id == "res-legacy")).State);
    }

    /// <summary>A legacy Released hold whose own debit exists (a legacy revision) is re-armed in place
    /// as a no-debit hold — never handed back as Released, never charged again.</summary>
    [Fact]
    public async Task Legacy_ReleasedHoldWithItsOwnDebit_IsRearmed_WithoutASecondDebit()
    {
        await GrantWritingCreditsAsync(4);
        const string reference = "writing-grade:legacy-revision";
        await _ledger.DeductGradingCreditAsync(UserId, "writing", reference, default);
        _db.AiOperations.Add(new AiOperation
        {
            Id = "op-legacy-rev",
            Module = "writing",
            FeatureCode = "writing.score.v1",
            UserId = UserId,
            IdempotencyKey = reference,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        _db.AiCreditReservations.Add(new AiCreditReservation
        {
            Id = "res-legacy-rev",
            OperationId = "op-legacy-rev",
            UserId = UserId,
            BucketKind = "writing",
            Units = 2,
            State = AiCreditReservationState.Released,
            BusinessReference = reference,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync();

        var ticket = await _reservations.ReserveWritingAsync(UserId, "op-new", reference, default);

        Assert.Equal("res-legacy-rev", ticket.ReservationId);
        Assert.Equal(AiCreditReservationState.Reserved, ticket.State);
        Assert.Equal(0, ticket.Units);
        Assert.Single(await LedgerRowsAsync(AiPackageCreditReason.GradingDeduct));
    }

    // ── Harness ────────────────────────────────────────────────────────────────

    private WritingSubmissionEvaluationPipeline Pipeline(IAiGatewayService gateway, WritingGradeChainOptions? chain = null)
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
            creditReservations: _reservations,
            gradeChainOptions: Microsoft.Extensions.Options.Options.Create(chain ?? new WritingGradeChainOptions()));

    private WritingSubmissionService Service(IWritingSubmissionEvaluationPipeline pipeline)
        => new(_db, pipeline, NullLogger<WritingSubmissionService>.Instance, new EmptyHighlightStore());

    private Task GrantWritingCreditsAsync(int credits)
        => GrantAsync("pkg_writing_test", $$"""{"package_type":"writing","writing_only_credits":{{credits}}}""");

    private Task GrantAsync(string code, string grantJson)
        => _ledger.GrantPackageAsync(
            UserId,
            new BillingAddOn
            {
                Id = $"addon-{Guid.NewGuid():N}",
                Code = code,
                Name = code,
                Price = 1m,
                Currency = "GBP",
                Interval = "one_time",
                Status = BillingAddOnStatus.Active,
                DurationDays = 30,
                GrantEntitlementsJson = grantJson,
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

    /// <summary>Same shape as the real Mastery purchase: the unlimited signal comes from an active
    /// pkg_oet_mastery subscription item (see WritingEntitlementAuthorizeStartTests).</summary>
    private async Task GrantUnlimitedAsync()
    {
        await GrantAsync("pkg_oet_mastery", """{"package_type":"full","unlimited_grading":true,"listening_tests":null,"reading_tests":null}""");
        var now = DateTimeOffset.UtcNow;
        _db.Subscriptions.Add(new Subscription
        {
            Id = "sub-mastery",
            UserId = UserId,
            PlanId = "plan-free",
            Status = SubscriptionStatus.Active,
            StartedAt = now,
            ChangedAt = now,
            NextRenewalAt = now.AddDays(180),
            ExpiresAt = now.AddDays(180),
            PriceAmount = 0,
            Currency = "GBP",
            Interval = "one_time",
        });
        _db.SubscriptionItems.Add(new SubscriptionItem
        {
            Id = "item-mastery",
            SubscriptionId = "sub-mastery",
            ItemCode = "pkg_oet_mastery",
            ItemType = "addon",
            Status = SubscriptionItemStatus.Active,
            StartsAt = now,
            EndsAt = now.AddDays(180),
            CreatedAt = now,
            UpdatedAt = now,
        });
        await _db.SaveChangesAsync();
    }

    private async Task EnableFreeSamplesAsync()
    {
        _db.FeatureFlags.Add(new FeatureFlag
        {
            Id = "flag-free-samples",
            Name = "Free samples",
            Key = FreeSampleService.FeatureFlagKey,
            Enabled = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        _db.Users.Add(new LearnerUser
        {
            Id = UserId,
            DisplayName = "Credit Learner",
            Email = "credit-learner@example.test",
            ActiveProfessionId = "medicine",
            AccountStatus = "active",
            CreatedAt = DateTimeOffset.UtcNow,
            LastActiveAt = DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync();
    }

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

    private Task<WritingSubmission> SubmissionAsync(Guid submissionId)
        => _db.WritingSubmissions.AsNoTracking().SingleAsync(s => s.Id == submissionId);

    private async Task<string> StatusAsync(Guid submissionId) => (await SubmissionAsync(submissionId)).Status;

    private async Task<AiCreditReservation> HoldAsync(Guid submissionId)
    {
        var reference = (await SubmissionAsync(submissionId)).CreditReference;
        return await _db.AiCreditReservations.AsNoTracking().SingleAsync(r => r.BusinessReference == reference);
    }

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
