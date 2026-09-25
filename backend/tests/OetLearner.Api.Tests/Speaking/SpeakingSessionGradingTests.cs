using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Ai;
using OetLearner.Api.Services.Billing;
using OetLearner.Api.Services.Content;
using OetLearner.Api.Services.Rulebook;
using OetLearner.Api.Services.Speaking;

namespace OetLearner.Api.Tests.Speaking;

/// <summary>
/// Shared Speaking session engine, owner requirements 8, 9 and 11:
///   * a practice card holds 2 AI credits at reveal and is charged exactly
///     once, only when the card is GRADED; a failed grading is retryable at
///     no extra charge; an ungraded hold is refunded;
///   * the recorder fallback (no live-voice provider) stores the upload once
///     (409 on a repeat), transcribes it and auto-runs the assessment;
///   * session consent also records the account-level consents.
/// </summary>
public sealed class SpeakingSessionGradingTests : IAsyncLifetime
{
    private const string UserId = "grading-learner";
    private LearnerDbContext _db = default!;
    private string _storageRoot = default!;

    public Task InitializeAsync()
    {
        _db = new LearnerDbContext(new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase($"speaking-grading-{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options);
        _storageRoot = Path.Combine(Path.GetTempPath(), $"oet-speaking-grading-{Guid.NewGuid():N}");
        _db.Users.Add(new LearnerUser
        {
            Id = UserId,
            DisplayName = "Grading Learner",
            Email = $"{UserId}@example.test",
            ActiveProfessionId = "medicine",
            AccountStatus = "active",
            CreatedAt = DateTimeOffset.UtcNow,
            LastActiveAt = DateTimeOffset.UtcNow,
        });
        _db.RolePlayCards.Add(new RolePlayCard
        {
            Id = "rpc-grading",
            ContentItemId = "ci-grading",
            ProfessionId = "medicine",
            ScenarioTitle = "Chest pain follow-up",
            Setting = "General practice",
            CandidateRole = "Doctor",
            InterlocutorRole = "Patient",
            Background = "Follow-up after an ECG.",
            Task1 = "Explain the result",
            PrepTimeSeconds = 180,
            RolePlayTimeSeconds = 300,
            Status = ContentStatus.Published,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        _db.SaveChanges();
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _db.Dispose();
        if (Directory.Exists(_storageRoot)) Directory.Delete(_storageRoot, recursive: true);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task PracticeCard_ChargedOnceOnlyWhenGraded_FailedGradingIsRetryableAtNoExtraCharge()
    {
        var credits = new StubPackageCredits();
        var reservations = new OetLearner.Api.Services.Ai.AiCreditReservationService(_db, credits, TimeProvider.System);
        var gateway = new SwitchableAiGateway();
        var canonical = BuildCanonical(gateway, reservations);
        var sessions = new SpeakingSessionService(_db, creditReservations: reservations);

        var created = await sessions.CreateSessionAsync(UserId, new CreateSpeakingSessionRequest("rpc-grading", "ai_self_practice"), default);
        var sessionId = created.SessionId;
        await sessions.FinishWarmupAsync(UserId, sessionId, default);
        await sessions.StartRolePlayAsync(UserId, sessionId, default);

        // Held at reveal, NOT committed when the role-play starts.
        var hold = await _db.AiCreditReservations.AsNoTracking().SingleAsync();
        Assert.Equal(SpeakingCreditSettlement.PracticeReference(sessionId), hold.BusinessReference);
        Assert.Equal(AiCreditReservationState.Reserved, hold.State);
        Assert.Equal(1, credits.DeductCalls);

        await sessions.EndSessionAsync(UserId, sessionId, default);
        SeedTranscript(sessionId);

        // Grading fails: nothing is charged, the result is failed + retryable.
        gateway.Fail = true;
        await Assert.ThrowsAsync<ApiException>(() => canonical.AssessNowAsync(sessionId, default));
        Assert.Equal(AiCreditReservationState.Reserved, (await _db.AiCreditReservations.AsNoTracking().SingleAsync()).State);
        var failed = await canonical.GetStateAsync(sessionId, default);
        Assert.Equal(SpeakingAssessmentState.Failed, failed.AssessmentState);
        Assert.True(failed.Retryable);
        Assert.NotNull(failed.FailureReason);

        // Retry succeeds: the hold is committed exactly once.
        gateway.Fail = false;
        await canonical.AssessNowAsync(sessionId, default);
        await canonical.AssessNowAsync(sessionId, default);

        Assert.Equal(AiCreditReservationState.Committed, (await _db.AiCreditReservations.AsNoTracking().SingleAsync()).State);
        Assert.Equal(1, credits.DeductCalls);
        Assert.Equal(0, credits.RefundCalls);
        Assert.Equal(1, await _db.SpeakingAiAssessments.CountAsync(a => a.SpeakingSessionId == sessionId));
        Assert.Equal(SpeakingAssessmentState.Completed, (await canonical.GetStateAsync(sessionId, default)).AssessmentState);
    }

    [Fact]
    public async Task StaleHolds_AreRefundedWhenUngraded_AndCommittedWhenGraded()
    {
        var credits = new StubPackageCredits();
        var reservations = new OetLearner.Api.Services.Ai.AiCreditReservationService(_db, credits, TimeProvider.System);
        var now = DateTimeOffset.UtcNow;
        SeedHold("practice:sps_abandoned", now.AddHours(-25));
        SeedHold("practice:sps_graded", now.AddHours(-25));
        SeedHold("practice:sps_recent", now.AddHours(-1));
        _db.SpeakingSimulationV11Assessments.Add(new SpeakingSimulationV11Assessment
        {
            Id = "v11-graded",
            SpeakingSessionId = "sps_graded",
            AssessmentKind = "card",
            Status = SpeakingSimulationV11AssessmentStatus.Complete,
            EstimatedPracticeScore = 350,
        });
        await _db.SaveChangesAsync();

        var settled = await SpeakingCreditSettlement.SettleStaleHoldsAsync(_db, reservations, now, default);

        Assert.Equal(2, settled);
        var byReference = await _db.AiCreditReservations.AsNoTracking().ToDictionaryAsync(r => r.BusinessReference);
        Assert.Equal(AiCreditReservationState.Released, byReference["practice:sps_abandoned"].State);
        Assert.Equal(AiCreditReservationState.Committed, byReference["practice:sps_graded"].State);
        Assert.Equal(AiCreditReservationState.Reserved, byReference["practice:sps_recent"].State);
        Assert.Equal(1, credits.RefundCalls);
    }

    [Fact]
    public async Task SessionConsent_RecordsAccountConsents_AndIsReportedOnTheSession()
    {
        var sessions = new SpeakingSessionService(_db, compliance: BuildCompliance());
        var created = await sessions.CreateSessionAsync(UserId, new CreateSpeakingSessionRequest("rpc-grading", "ai_self_practice"), default);
        Assert.False(created.ConsentAccepted);
        Assert.False(created.LiveVoiceAvailable);

        var detail = await sessions.MarkConsentAsync(UserId, created.SessionId, "recording.v1", default);
        await sessions.MarkConsentAsync(UserId, created.SessionId, "recording.v1", default);

        Assert.True(detail.ConsentAccepted);
        var consentTypes = await _db.SpeakingComplianceConsents.AsNoTracking()
            .Where(c => c.UserId == UserId)
            .Select(c => c.ConsentType)
            .ToListAsync();
        // Recorded once each, even when consent is posted twice.
        Assert.Equal(
            new[] { SpeakingComplianceConsentTypes.AiProcessing, SpeakingComplianceConsentTypes.Recording, SpeakingComplianceConsentTypes.Retention },
            consentTypes.OrderBy(x => x, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public async Task RecorderFallback_StoresOnce_TranscribesAndAutoAssesses()
    {
        var gateway = new SwitchableAiGateway();
        var canonical = BuildCanonical(gateway, reservations: null);
        var pipeline = new SpeakingTranscriptionPipeline(
            _db, new FakeTranscriptionProvider(), NullLogger<SpeakingTranscriptionPipeline>.Instance, canonical);
        var recordings = BuildRecordings(pipeline);
        var sessions = new SpeakingSessionService(_db, compliance: BuildCompliance());

        var created = await sessions.CreateSessionAsync(UserId, new CreateSpeakingSessionRequest("rpc-grading", "ai_self_practice"), default);
        var sessionId = created.SessionId;
        await sessions.MarkConsentAsync(UserId, sessionId, "recording.v1", default);
        await sessions.FinishWarmupAsync(UserId, sessionId, default);
        await sessions.StartRolePlayAsync(UserId, sessionId, default);

        var audio = new byte[] { 0x1A, 0x45, 0xDF, 0xA3, 0x01, 0x02, 0x03, 0x04 };
        var first = await recordings.ReceiveAsync(UserId, sessionId, new MemoryStream(audio), "audio/webm;codecs=opus", audio.Length, 290, default);
        var second = await recordings.ReceiveAsync(UserId, sessionId, new MemoryStream(audio), "audio/webm", audio.Length, 290, default);

        Assert.True(first);
        Assert.False(second); // endpoint answers 409 recording_already_received
        var recording = await _db.SpeakingRecordings.AsNoTracking().SingleAsync(r => r.SpeakingSessionId == sessionId);
        Assert.Equal(SpeakingSessionRecordingService.RecordingIdFor(sessionId), recording.Id);
        Assert.NotNull(recording.RetentionExpiresAt);

        await sessions.EndSessionAsync(UserId, sessionId, default);
        await sessions.SubmitForMarkingAsync(UserId, sessionId, default);

        // ai-assess while the transcript is pending → 202 processing.
        Assert.True(await recordings.DeferAssessmentUntilTranscribedAsync(sessionId, canonical, default));
        Assert.Equal(SpeakingAssessmentState.Processing, (await canonical.GetStateAsync(sessionId, default)).AssessmentState);

        // The transcription job writes the transcript and grades automatically.
        Assert.True(await pipeline.ProcessNextAsync(default));

        var transcript = await _db.SpeakingTranscripts.AsNoTracking().SingleAsync(t => t.SpeakingSessionId == sessionId && t.IsLatest);
        Assert.Equal("openai-whisper", transcript.Provider);
        Assert.Equal(1, await _db.SpeakingAiAssessments.CountAsync(a => a.SpeakingSessionId == sessionId));
        Assert.Equal(SpeakingAssessmentState.Completed, (await canonical.GetStateAsync(sessionId, default)).AssessmentState);
        Assert.False(await recordings.DeferAssessmentUntilTranscribedAsync(sessionId, canonical, default));
    }

    [Fact]
    public async Task RecorderFallback_RejectsOtherLearners_AndOtherProfessions()
    {
        var pipeline = new SpeakingTranscriptionPipeline(
            _db, new FakeTranscriptionProvider(), NullLogger<SpeakingTranscriptionPipeline>.Instance);
        var recordings = BuildRecordings(pipeline);
        var sessions = new SpeakingSessionService(_db);
        var created = await sessions.CreateSessionAsync(UserId, new CreateSpeakingSessionRequest("rpc-grading", "ai_self_practice"), default);
        var sessionId = created.SessionId;
        await sessions.MarkConsentAsync(UserId, sessionId, "recording.v1", default);
        await sessions.FinishWarmupAsync(UserId, sessionId, default);
        await sessions.StartRolePlayAsync(UserId, sessionId, default);

        var intruder = await Assert.ThrowsAsync<ApiException>(() => recordings.ReceiveAsync(
            "someone-else", sessionId, new MemoryStream(new byte[] { 1, 2, 3 }), "audio/webm", 3, null, default));
        Assert.Equal("speaking_session_not_found", intruder.ErrorCode);

        var user = await _db.Users.SingleAsync(u => u.Id == UserId);
        user.ActiveProfessionId = "nursing";
        await _db.SaveChangesAsync();
        var otherProfession = await Assert.ThrowsAsync<ApiException>(() => recordings.ReceiveAsync(
            UserId, sessionId, new MemoryStream(new byte[] { 1, 2, 3 }), "audio/webm", 3, null, default));
        Assert.Equal("speaking_session_not_found", otherProfession.ErrorCode);
        Assert.False(await _db.SpeakingRecordings.AnyAsync());
    }

    // ── Builders / seeds ────────────────────────────────────────────────

    private SpeakingCanonicalAssessmentService BuildCanonical(
        IAiGatewayService gateway,
        OetLearner.Api.Services.Ai.IAiCreditReservationService? reservations)
        => new(
            _db,
            new SpeakingAiAssessmentService(_db, gateway, NullLogger<SpeakingAiAssessmentService>.Instance),
            v11: null!,
            TimeProvider.System,
            NullLogger<SpeakingCanonicalAssessmentService>.Instance,
            reservations);

    private SpeakingComplianceService BuildCompliance()
        => new(
            _db,
            storage: null!,
            Options.Create(new SpeakingComplianceOptions()),
            NullLogger<SpeakingComplianceService>.Instance,
            TimeProvider.System);

    private SpeakingSessionRecordingService BuildRecordings(SpeakingTranscriptionPipeline pipeline)
    {
        var storageOptions = Options.Create(new StorageOptions { LocalRootPath = _storageRoot });
        var storage = new LocalFileStorage(new TestHostEnvironment(_storageRoot), storageOptions);
        return new SpeakingSessionRecordingService(
            _db,
            storage,
            pipeline,
            Options.Create(new SpeakingComplianceOptions()),
            NullLogger<SpeakingSessionRecordingService>.Instance,
            storageOptions);
    }

    private void SeedTranscript(string sessionId)
    {
        _db.SpeakingTranscripts.Add(new SpeakingTranscript
        {
            Id = Guid.NewGuid().ToString("N"),
            SpeakingSessionId = sessionId,
            Provider = "live-roleplay",
            Language = "en",
            SegmentsJson = FakeTranscriptionProvider.SegmentsJson,
            IsLatest = true,
            WordCount = 11,
            MeanConfidence = 0.9,
            GeneratedAt = DateTimeOffset.UtcNow,
        });
        _db.SaveChanges();
    }

    private void SeedHold(string businessReference, DateTimeOffset createdAt)
    {
        var operationId = Guid.NewGuid().ToString("N");
        _db.AiOperations.Add(new AiOperation
        {
            Id = operationId,
            Module = "speaking",
            FeatureCode = AiFeatureCodes.SpeakingGrade,
            UserId = UserId,
            IdempotencyKey = businessReference,
            CreatedAt = createdAt,
            UpdatedAt = createdAt,
        });
        _db.AiCreditReservations.Add(new AiCreditReservation
        {
            Id = Guid.NewGuid().ToString("N"),
            OperationId = operationId,
            UserId = UserId,
            BucketKind = "speaking",
            Units = 2,
            State = AiCreditReservationState.Reserved,
            BusinessReference = businessReference,
            CreatedAt = createdAt,
            UpdatedAt = createdAt,
        });
    }

    private static string ValidAssessmentJson() => JsonSerializer.Serialize(new
    {
        criterionScores = new
        {
            intelligibility = new { score = 5, rationale = "Clear.", evidenceQuotes = Array.Empty<string>() },
            fluency = new { score = 5, rationale = "Smooth.", evidenceQuotes = Array.Empty<string>() },
            appropriateness = new { score = 5, rationale = "Warm.", evidenceQuotes = Array.Empty<string>() },
            grammarExpression = new { score = 5, rationale = "Accurate.", evidenceQuotes = Array.Empty<string>() },
            relationshipBuilding = new { score = 3, rationale = "Empathetic.", evidenceQuotes = Array.Empty<string>() },
            patientPerspective = new { score = 2, rationale = "Checked concerns.", evidenceQuotes = Array.Empty<string>() },
            structure = new { score = 2, rationale = "Signposted.", evidenceQuotes = Array.Empty<string>() },
            informationGathering = new { score = 2, rationale = "Open questions.", evidenceQuotes = Array.Empty<string>() },
            informationGiving = new { score = 2, rationale = "Checked understanding.", evidenceQuotes = Array.Empty<string>() },
        },
        readinessBand = "exam_ready",
        overallSummary = "Strong, organised communication.",
        confidenceBand = "high",
        strengths = new[] { "Clear opening" },
        improvements = Array.Empty<string>(),
        recommendedDrillKinds = Array.Empty<string>(),
    });

    // ── Fakes ───────────────────────────────────────────────────────────

    [Fact]
    public async Task AssessNow_DoesNotGradeASessionTheWorkerAlreadyHolds()
    {
        // Production 25 Sep 2026: /submit handed grading to the worker and the
        // page's /ai-assess graded the same session concurrently (500).
        var gateway = new SwitchableAiGateway();
        var canonical = BuildCanonical(gateway, reservations: null);
        var sessions = new SpeakingSessionService(_db, compliance: BuildCompliance());
        var created = await sessions.CreateSessionAsync(UserId, new CreateSpeakingSessionRequest("rpc-grading", "ai_self_practice"), default);
        var sessionId = created.SessionId;
        await sessions.FinishWarmupAsync(UserId, sessionId, default);
        await sessions.StartRolePlayAsync(UserId, sessionId, default);
        await sessions.EndSessionAsync(UserId, sessionId, default);
        SeedTranscript(sessionId);

        var ticket = await canonical.EnqueueAsync(sessionId, default);
        var op = await _db.AiOperations.SingleAsync(o => o.Id == ticket.OperationId);
        op.State = AiOperationState.Leased;
        op.LeaseOwner = "worker-1";
        op.LeaseExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5);
        await _db.SaveChangesAsync();

        await canonical.AssessNowAsync(sessionId, default);
        Assert.Equal(0, await _db.SpeakingAiAssessments.CountAsync(a => a.SpeakingSessionId == sessionId));

        // The worker that owns that lease does grade it (it must not be refused
        // by its own lease).
        await canonical.ExecuteQueuedAsync(ticket.OperationId, default);
        Assert.Equal(1, await _db.SpeakingAiAssessments.CountAsync(a => a.SpeakingSessionId == sessionId));

        // Once graded, later /ai-assess calls leave it alone.
        await canonical.AssessNowAsync(sessionId, default);
        await canonical.AssessNowAsync(sessionId, default);
        Assert.Equal(1, await _db.SpeakingAiAssessments.CountAsync(a => a.SpeakingSessionId == sessionId));
    }

    [Fact]
    public async Task AssessNow_TakesOverAnExpiredWorkerLease()
    {
        var gateway = new SwitchableAiGateway();
        var canonical = BuildCanonical(gateway, reservations: null);
        var sessions = new SpeakingSessionService(_db, compliance: BuildCompliance());
        var created = await sessions.CreateSessionAsync(UserId, new CreateSpeakingSessionRequest("rpc-grading", "ai_self_practice"), default);
        var sessionId = created.SessionId;
        await sessions.FinishWarmupAsync(UserId, sessionId, default);
        await sessions.StartRolePlayAsync(UserId, sessionId, default);
        await sessions.EndSessionAsync(UserId, sessionId, default);
        SeedTranscript(sessionId);

        var ticket = await canonical.EnqueueAsync(sessionId, default);
        var op = await _db.AiOperations.SingleAsync(o => o.Id == ticket.OperationId);
        op.State = AiOperationState.Leased;
        op.LeaseOwner = "crashed-worker";
        op.LeaseExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        await _db.SaveChangesAsync();

        await canonical.AssessNowAsync(sessionId, default);
        Assert.Equal(1, await _db.SpeakingAiAssessments.CountAsync(a => a.SpeakingSessionId == sessionId));
    }

    private sealed class SwitchableAiGateway : IAiGatewayService
    {
        public bool Fail { get; set; }

        public AiGroundedPrompt BuildGroundedPrompt(AiGroundingContext context) => new()
        {
            SystemPrompt = "OET AI — Rulebook-Grounded System Prompt\n(test fixture)\n",
            TaskInstruction = "Score this speaking attempt.",
            Metadata = new AiGroundedPromptMetadata
            {
                RulebookVersion = "test-1.0.0",
                RulebookKind = context.Kind,
                Profession = context.Profession,
                ScoringPassMark = 350,
                ScoringGrade = "B",
                AppliedRulesCount = 1,
                AppliedRuleIds = new[] { "RULE_01" },
            },
        };

        public Task<AiGatewayResult> CompleteAsync(AiGatewayRequest request, CancellationToken ct = default)
        {
            if (Fail) throw new InvalidOperationException("provider unreachable");
            return Task.FromResult(new AiGatewayResult
            {
                Completion = ValidAssessmentJson(),
                Metadata = request.Prompt!.Metadata,
                RulebookVersion = request.Prompt!.Metadata.RulebookVersion,
                AppliedRuleIds = request.Prompt!.Metadata.AppliedRuleIds,
            });
        }
    }

    private sealed class FakeTranscriptionProvider : ISpeakingTranscriptionProvider
    {
        public const string SegmentsJson =
            """[{"speaker":"candidate","startMs":0,"endMs":4000,"text":"Hello, I am the doctor looking after you today.","confidence":0.94,"words":[]}]""";

        public string ProviderCode => "openai-whisper";

        public Task<SpeakingTranscriptionProviderResult> TranscribeAsync(string mediaAssetUrl, string language, CancellationToken ct)
            => Task.FromResult(new SpeakingTranscriptionProviderResult
            {
                Provider = "openai-whisper",
                Language = "en",
                SegmentsJson = SegmentsJson,
                WordCount = 10,
                MeanConfidence = 0.94,
            });
    }

    private sealed class StubPackageCredits : IAiPackageCreditService
    {
        public int DeductCalls { get; private set; }
        public int RefundCalls { get; private set; }

        public Task<AiPackageCreditSnapshot> GetSnapshotAsync(string userId, int transactionLimit, CancellationToken ct)
            => Task.FromResult(new AiPackageCreditSnapshot(
                userId, FlexibleCredits: 2, WritingOnlyCredits: 0, SpeakingOnlyCredits: 4,
                ListeningTestsRemaining: 0, ReadingTestsRemaining: 0, MockExamsRemaining: 0,
                ExpiresAt: DateTimeOffset.UtcNow.AddDays(30), ExpiredBecausePassed: false, PassedAt: null,
                Transactions: Array.Empty<AiPackageCreditTransactionDto>(),
                SpeakingUnlimited: false, SharedCredits: 0));

        public Task<AiPackageDebitResult> DeductGradingCreditAsync(string userId, string subtest, string referenceId, CancellationToken ct)
        {
            DeductCalls++;
            return Task.FromResult(new AiPackageDebitResult(true, null, null, referenceId, Bypassed: false, CreditsUsed: 2));
        }

        public Task<AiPackageDebitResult> DeductGradingCreditAsync(string userId, string subtest, string referenceId, int quantity, CancellationToken ct)
            => DeductGradingCreditAsync(userId, subtest, referenceId, ct);

        public Task<bool> RefundAsync(string userId, string originalReferenceId, string refundReferenceId, string description, CancellationToken ct)
        {
            RefundCalls++;
            return Task.FromResult(true);
        }

        public Task<AiPackageCreditSnapshot> GrantPackageAsync(string userId, BillingAddOn addOn, int quantity, string stripeSessionId, string? quoteId, CancellationToken ct, string? sourceReferenceId = null, DateTimeOffset? validFrom = null)
            => throw new NotImplementedException();
        public Task<bool> GrantCourseGiftCreditsAsync(string userId, string planCode, string planName, int credits, string referenceId, DateTimeOffset? expiresAt, CancellationToken ct, string? sourceReferenceId = null, DateTimeOffset? validFrom = null)
            => throw new NotImplementedException();
        public Task<AiPackageDebitResult> CheckGradingCreditAsync(string userId, string subtest, CancellationToken ct)
            => CheckGradingCreditAsync(userId, subtest, 1, ct);
        public Task<AiPackageDebitResult> CheckGradingCreditAsync(string userId, string subtest, int quantity, CancellationToken ct)
            => Task.FromResult(new AiPackageDebitResult(true, null, null, null));
        public Task<AiPackageDebitResult> DeductObjectivePracticeAsync(string userId, string subtest, string referenceId, CancellationToken ct)
            => throw new NotImplementedException();
        public Task<AiPackageDebitResult> DeductMockAsync(string userId, string referenceId, CancellationToken ct)
            => throw new NotImplementedException();
        public Task<AiPackageCreditSnapshot> AdjustAsync(string userId, AiPackageCreditAdjustmentRequest request, string adminId, CancellationToken ct)
            => throw new NotImplementedException();
        public Task<AiPackageCreditSnapshot> RecordExamOutcomeAsync(string userId, LearnerExamOutcomeRequest request, string adminId, string adminName, CancellationToken ct)
            => throw new NotImplementedException();
        public Task<int> ReverseGrantsAsync(string userId, string sourceReferenceId, CancellationToken ct)
            => throw new NotImplementedException();
        public Task RecalculateObjectiveAllowancesAsync(string userId, CancellationToken ct)
            => throw new NotImplementedException();
        public Task ParkSubscriptionLotsAsync(string userId, string subscriptionId, CancellationToken ct)
            => Task.CompletedTask;
        public Task UnparkSubscriptionLotsAsync(string userId, string subscriptionId, CancellationToken ct)
            => Task.CompletedTask;
        public Task UpdateGrantExpiryAsync(string userId, string subscriptionId, DateTimeOffset? expiresAt, CancellationToken ct)
            => throw new NotImplementedException();
        public Task UpdateGrantWindowAsync(string userId, string subscriptionId, DateTimeOffset? validFrom, DateTimeOffset? expiresAt, CancellationToken ct)
            => throw new NotImplementedException();
        public Task<bool> HasObjectivePracticeAllowanceAsync(string userId, string subtest, CancellationToken ct)
            => throw new NotImplementedException();
    }

    private sealed class TestHostEnvironment(string contentRootPath)
        : Microsoft.AspNetCore.Hosting.IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "OetLearner.Api.Tests";
        public Microsoft.Extensions.FileProviders.IFileProvider WebRootFileProvider { get; set; }
            = new Microsoft.Extensions.FileProviders.NullFileProvider();
        public string WebRootPath { get; set; } = string.Empty;
        public string EnvironmentName { get; set; } = "Development";
        public string ContentRootPath { get; set; } = contentRootPath;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; }
            = new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
