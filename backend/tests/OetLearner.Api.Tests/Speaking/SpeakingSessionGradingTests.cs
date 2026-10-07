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
using OetLearner.Api.Tests.Infrastructure;

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

    private void SeedTranscript(string sessionId, string? segmentsJson = null)
    {
        _db.SpeakingTranscripts.Add(new SpeakingTranscript
        {
            Id = Guid.NewGuid().ToString("N"),
            SpeakingSessionId = sessionId,
            Provider = "live-roleplay",
            Language = "en",
            SegmentsJson = segmentsJson ?? FakeTranscriptionProvider.SegmentsJson,
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

    /// <summary>A completed exam whose two card holds are Reserved and aged <paramref name="createdAt"/>;
    /// the sessions in <paramref name="gradedSessionIds"/> carry a complete v1.1 card report.</summary>
    private void SeedExamHolds(string examId, DateTimeOffset createdAt, params string[] gradedSessionIds)
    {
        _db.SpeakingExamSessions.Add(new SpeakingExamSession
        {
            Id = examId,
            UserId = UserId,
            CardAId = "rpc-grading",
            CardBId = "rpc-grading",
            SessionAId = $"sps_{examId}_a",
            SessionBId = $"sps_{examId}_b",
            CreditARefId = $"exam:{examId}:cardA",
            CreditBRefId = $"exam:{examId}:cardB",
            State = SpeakingExamState.Completed,
            CreatedAt = createdAt,
            UpdatedAt = createdAt,
        });
        SeedHold($"exam:{examId}:cardA", createdAt);
        SeedHold($"exam:{examId}:cardB", createdAt);
        foreach (var sessionId in gradedSessionIds)
        {
            _db.SpeakingSimulationV11Assessments.Add(new SpeakingSimulationV11Assessment
            {
                Id = $"v11-{sessionId}",
                SpeakingSessionId = sessionId,
                AssessmentKind = "card",
                Status = SpeakingSimulationV11AssessmentStatus.Complete,
                EstimatedPracticeScore = 350,
            });
        }
    }

    /// <summary>A completed exam: two Finished card sessions with a saved transcript each, holds Reserved.</summary>
    private void SeedFinishedExam(string examId)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var slot in new[] { "a", "b" })
        {
            _db.SpeakingSessions.Add(new SpeakingSession
            {
                Id = $"sps_{examId}_{slot}",
                UserId = UserId,
                RolePlayCardId = "rpc-grading",
                ExamSessionId = examId,
                ExamSlot = slot,
                Mode = SpeakingSessionMode.AiExam,
                State = SpeakingSessionState.Finished,
                EndedAt = now,
                CreatedAt = now,
                UpdatedAt = now,
            });
        }
        SeedExamHolds(examId, now);
        _db.SaveChanges();
        SeedTranscript($"sps_{examId}_a");
        SeedTranscript($"sps_{examId}_b");
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

    [Fact]
    public async Task AssessNow_HoldsItsLeaseForTheWorkerLeaseDuration()
    {
        // Production 26 Sep 2026: a full 5-minute role-play took ~11.5 min to
        // grade; the 10-minute direct-run lease expired mid-grade and the worker
        // took the operation over and marked it FailedTerminal.
        var gateway = new SwitchableAiGateway();
        var canonical = BuildCanonical(gateway, reservations: null);
        var sessions = new SpeakingSessionService(_db, compliance: BuildCompliance());
        var created = await sessions.CreateSessionAsync(UserId, new CreateSpeakingSessionRequest("rpc-grading", "ai_self_practice"), default);
        var sessionId = created.SessionId;
        await sessions.FinishWarmupAsync(UserId, sessionId, default);
        await sessions.StartRolePlayAsync(UserId, sessionId, default);
        await sessions.EndSessionAsync(UserId, sessionId, default);
        SeedTranscript(sessionId);

        DateTimeOffset? leaseDuringGrading = null;
        gateway.OnComplete = () => leaseDuringGrading = _db.AiOperations.AsNoTracking()
            .Single(o => o.ResourceId == sessionId && o.FeatureCode == AiFeatureCodes.SpeakingGrade).LeaseExpiresAt;
        var started = DateTimeOffset.UtcNow;

        await canonical.AssessNowAsync(sessionId, default);

        Assert.NotNull(leaseDuringGrading);
        Assert.True(leaseDuringGrading >= started.Add(AiOperationWorker.LeaseDuration).AddMinutes(-1));
        Assert.Equal(1, await _db.SpeakingAiAssessments.CountAsync(a => a.SpeakingSessionId == sessionId));
    }

    [Fact]
    public async Task AssessNow_CallerGoneMidGrade_HandsTheGradeBackToTheWorker()
    {
        // Production 26 Sep 2026: the exam results page's 30 s request timeout
        // cancelled Card B's grade, which then sat leased (stuck) for 30 minutes.
        var gateway = new SwitchableAiGateway();
        var canonical = BuildCanonical(gateway, reservations: null);
        var sessions = new SpeakingSessionService(_db, compliance: BuildCompliance());
        var created = await sessions.CreateSessionAsync(UserId, new CreateSpeakingSessionRequest("rpc-grading", "ai_self_practice"), default);
        var sessionId = created.SessionId;
        await sessions.FinishWarmupAsync(UserId, sessionId, default);
        await sessions.StartRolePlayAsync(UserId, sessionId, default);
        await sessions.EndSessionAsync(UserId, sessionId, default);
        SeedTranscript(sessionId);

        using var aborted = new CancellationTokenSource();
        gateway.OnComplete = aborted.Cancel;

        await Assert.ThrowsAnyAsync<Exception>(() => canonical.AssessNowAsync(sessionId, aborted.Token));

        var op = await _db.AiOperations.AsNoTracking()
            .SingleAsync(o => o.ResourceId == sessionId && o.FeatureCode == AiFeatureCodes.SpeakingGrade);
        Assert.Equal(AiOperationState.RetryScheduled, op.State);
        Assert.Null(op.LeaseOwner);
        Assert.NotNull(op.NextAttemptAt);
    }

    [Fact]
    public async Task PaidPracticeCard_IsCreditFunded_SoTheAiPlanGateDoesNotRefuseGrading()
    {
        // Production 25 Sep 2026: a learner on the default "free" AI plan paid
        // 2 credits for a card, then grading was refused ("Your plan does not
        // include this AI feature"). The paid hold now authorises grading.
        var credits = new StubPackageCredits();
        var reservations = new OetLearner.Api.Services.Ai.AiCreditReservationService(_db, credits, TimeProvider.System);
        var sessions = new SpeakingSessionService(_db, creditReservations: reservations);
        var created = await sessions.CreateSessionAsync(UserId, new CreateSpeakingSessionRequest("rpc-grading", "ai_self_practice"), default);
        var session = await _db.SpeakingSessions.AsNoTracking().SingleAsync(s => s.Id == created.SessionId);
        Assert.False(await SpeakingCreditSettlement.IsCreditFundedAsync(_db, session, default));

        await sessions.FinishWarmupAsync(UserId, created.SessionId, default);
        Assert.True(await SpeakingCreditSettlement.IsCreditFundedAsync(_db, session, default));
    }

    // ── Full AI mock: exam holds are settled once, and exam cards are only created by their exam ──

    [Fact]
    public async Task CreateSession_AiExamMode_IsRejected_BecauseExamCardsAreCreatedByTheirExam()
    {
        // POST /v1/speaking/sessions accepted mode "ai_exam": the card skipped the exam, so it also
        // skipped the credit hold the exam takes, yet ran a billed live-voice conversation.
        var sessions = new SpeakingSessionService(_db);

        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            sessions.CreateSessionAsync(UserId, new CreateSpeakingSessionRequest("rpc-grading", "ai_exam"), default));

        Assert.Equal("speaking_session_exam_managed", ex.ErrorCode);
        Assert.Equal(409, ex.StatusCode);
        Assert.False(await _db.SpeakingSessions.AnyAsync());
        Assert.False(await _db.Attempts.AnyAsync());
    }

    [Fact]
    public async Task StaleExamHolds_AreCommittedOnlyWhenBothCardsAreGraded_AndSettlingAgainChangesNothing()
    {
        var credits = new StubPackageCredits();
        var reservations = new OetLearner.Api.Services.Ai.AiCreditReservationService(_db, credits, TimeProvider.System);
        var now = DateTimeOffset.UtcNow;
        SeedExamHolds("spx_half", now.AddHours(-25), "sps_spx_half_a");
        SeedExamHolds("spx_both", now.AddHours(-25), "sps_spx_both_a", "sps_spx_both_b");
        await _db.SaveChangesAsync();

        var settled = await SpeakingCreditSettlement.SettleStaleHoldsAsync(_db, reservations, now, default);
        var settledAgain = await SpeakingCreditSettlement.SettleStaleHoldsAsync(_db, reservations, now, default);

        Assert.Equal(4, settled);
        Assert.Equal(0, settledAgain);
        var byReference = await _db.AiCreditReservations.AsNoTracking().ToDictionaryAsync(r => r.BusinessReference);
        // Only Card A of the first exam was graded: that exam has no result, so both its holds are refunded.
        Assert.Equal(AiCreditReservationState.Released, byReference["exam:spx_half:cardA"].State);
        Assert.Equal(AiCreditReservationState.Released, byReference["exam:spx_half:cardB"].State);
        Assert.Equal(AiCreditReservationState.Committed, byReference["exam:spx_both:cardA"].State);
        Assert.Equal(AiCreditReservationState.Committed, byReference["exam:spx_both:cardB"].State);
        Assert.Equal(2, credits.RefundCalls);
        Assert.Equal(0, credits.DeductCalls);
    }

    [Fact]
    public async Task ExamCards_GradingFailsThenSucceeds_EachCardIsGradedOnce_AndBothHoldsCommitOnlyOnce()
    {
        var credits = new StubPackageCredits();
        var reservations = new OetLearner.Api.Services.Ai.AiCreditReservationService(_db, credits, TimeProvider.System);
        var gateway = new SwitchableAiGateway();
        var canonical = BuildCanonical(gateway, reservations);
        var exams = new SpeakingExamService(
            _db,
            new SpeakingAiAssessmentService(_db, gateway, NullLogger<SpeakingAiAssessmentService>.Instance),
            NullLogger<SpeakingExamService>.Instance,
            creditReservations: reservations,
            canonical: canonical);
        const string examId = "spx_retry";
        var sessionA = $"sps_{examId}_a";
        var sessionB = $"sps_{examId}_b";
        SeedFinishedExam(examId);

        // Card B's grade fails first: nothing is committed, and nothing is charged again.
        gateway.Fail = true;
        await Assert.ThrowsAsync<ApiException>(() => canonical.AssessNowAsync(sessionB, default));
        Assert.All(await _db.AiCreditReservations.AsNoTracking().ToListAsync(),
            r => Assert.Equal(AiCreditReservationState.Reserved, r.State));

        // Card A grades alone: the exam still has no result, so its holds stay held.
        gateway.Fail = false;
        await canonical.AssessNowAsync(sessionA, default);
        Assert.All(await _db.AiCreditReservations.AsNoTracking().ToListAsync(),
            r => Assert.Equal(AiCreditReservationState.Reserved, r.State));

        // Card B's retry succeeds (twice: the second is a no-op), and the results page polls meanwhile.
        await canonical.AssessNowAsync(sessionB, default);
        await canonical.AssessNowAsync(sessionB, default);
        var cardScore = (await _db.SpeakingAiAssessments.AsNoTracking().FirstAsync(a => a.SpeakingSessionId == sessionA)).EstimatedScaledScore;

        // Both cards are graded: the overall result waits for the ONE combined judgement (never an average), which the
        // second card's grade queued for the worker.
        var waiting = await exams.GetResultsAsync(UserId, examId, default);
        Assert.Equal("pending", waiting.OverallStatus);
        Assert.Equal(SpeakingExamCombinedStates.Pending, waiting.CombinedState);
        Assert.Null(waiting.CombinedScaledScore);
        var examOperation = await _db.AiOperations.AsNoTracking()
            .SingleAsync(o => o.ResourceType == SpeakingCanonicalAssessmentService.ExamResourceType);
        await canonical.ExecuteQueuedAsync(examOperation.Id, default);

        for (var read = 0; read < 3; read++)
        {
            var results = await exams.GetResultsAsync(UserId, examId, default);
            Assert.Equal("scored", results.OverallStatus);
            Assert.Equal(SpeakingExamCombinedStates.Ready, results.CombinedState);
            Assert.Equal(OetScoring.OetReportedScaledScore(cardScore), results.CombinedScaledScore);
        }
        Assert.Equal(cardScore, (await _db.SpeakingExamSessions.AsNoTracking()
            .SingleAsync(exam => exam.Id == examId)).CombinedScaledSnapshot);

        var holds = await _db.AiCreditReservations.AsNoTracking().ToListAsync();
        Assert.Equal(2, holds.Count);
        Assert.All(holds, r => Assert.Equal(AiCreditReservationState.Committed, r.State));
        Assert.Equal(1, await _db.SpeakingAiAssessments.CountAsync(a => a.SpeakingSessionId == sessionA));
        Assert.Equal(1, await _db.SpeakingAiAssessments.CountAsync(a => a.SpeakingSessionId == sessionB));
        // The holds are the only credit movement: grading and retries never touch the ledger.
        Assert.Equal(0, credits.DeductCalls);
        Assert.Equal(0, credits.RefundCalls);
    }

    // ── Classic assessor: reply contract, provenance and the grading chain ──

    private async Task<string> SeedFinishedSessionWithTranscriptAsync(string? segmentsJson = null)
    {
        var sessions = new SpeakingSessionService(_db, compliance: BuildCompliance());
        var created = await sessions.CreateSessionAsync(UserId, new CreateSpeakingSessionRequest("rpc-grading", "ai_self_practice"), default);
        await sessions.FinishWarmupAsync(UserId, created.SessionId, default);
        await sessions.StartRolePlayAsync(UserId, created.SessionId, default);
        await sessions.EndSessionAsync(UserId, created.SessionId, default);
        SeedTranscript(created.SessionId, segmentsJson);
        return created.SessionId;
    }

    private SpeakingAiAssessmentService BuildAssessor(
        IAiGatewayService gateway,
        SpeakingGradingOptions? gradingOptions = null,
        ISpeakingAudioEvidenceService? audioEvidence = null)
        => new(
            _db,
            gateway,
            NullLogger<SpeakingAiAssessmentService>.Instance,
            gradingOptions: gradingOptions is null ? null : Options.Create(gradingOptions),
            audioEvidence: audioEvidence);

    private static object Crit(int score) => new { score, rationale = "ok", evidenceQuotes = Array.Empty<string>() };

    private static Dictionary<string, object> FullCriteria() => new()
    {
        ["intelligibility"] = Crit(5),
        ["fluency"] = Crit(5),
        ["appropriateness"] = Crit(5),
        ["grammarExpression"] = Crit(5),
        ["relationshipBuilding"] = Crit(3),
        ["patientPerspective"] = Crit(2),
        ["structure"] = Crit(2),
        ["informationGathering"] = Crit(2),
        ["informationGiving"] = Crit(2),
    };

    private static string ReplyWith(Dictionary<string, object> criterionScores) => JsonSerializer.Serialize(new
    {
        criterionScores,
        readinessBand = "exam_ready",
        overallSummary = "Strong, organised communication.",
        confidenceBand = "high",
    });

    [Fact]
    public async Task Assessor_ReplyMissingACriterion_IsUnparseable_AndNothingIsPersisted()
    {
        var sessionId = await SeedFinishedSessionWithTranscriptAsync();
        var criteria = FullCriteria();
        criteria.Remove("informationGiving");
        var gateway = new SwitchableAiGateway { Completion = ReplyWith(criteria) };

        var ex = await Assert.ThrowsAsync<ApiException>(() => BuildAssessor(gateway).RunAssessmentAsync(sessionId, default));

        // Previously the absent criterion scored 0 and the grade was stored as if it were real.
        Assert.Equal("speaking_ai_unparseable", ex.ErrorCode);
        Assert.Equal(0, await _db.SpeakingAiAssessments.CountAsync(a => a.SpeakingSessionId == sessionId));
    }

    [Fact]
    public async Task Assessor_ReplyWithANonNumericScore_IsUnparseable()
    {
        var sessionId = await SeedFinishedSessionWithTranscriptAsync();
        var criteria = FullCriteria();
        criteria["fluency"] = new { score = "n/a", rationale = "ok", evidenceQuotes = Array.Empty<string>() };
        var gateway = new SwitchableAiGateway { Completion = ReplyWith(criteria) };

        var ex = await Assert.ThrowsAsync<ApiException>(() => BuildAssessor(gateway).RunAssessmentAsync(sessionId, default));

        Assert.Equal("speaking_ai_unparseable", ex.ErrorCode);
    }

    [Fact]
    public async Task Assessor_ReplyUsingTheSystemPromptSpellings_IsAcceptedAndStoredUnderTheCanonicalCodes()
    {
        // The grounded system prompt names these two criteria grammar / providingStructure;
        // the JSON template says grammarExpression / structure. A model may follow either.
        var sessionId = await SeedFinishedSessionWithTranscriptAsync();
        var criteria = FullCriteria();
        criteria.Remove("grammarExpression");
        criteria.Remove("structure");
        criteria["grammar"] = Crit(4);
        criteria["providingStructure"] = Crit(1);
        var gateway = new SwitchableAiGateway { Completion = ReplyWith(criteria) };

        var projection = await BuildAssessor(gateway).RunAssessmentAsync(sessionId, default);

        var row = await _db.SpeakingAiAssessments.AsNoTracking().SingleAsync(a => a.SpeakingSessionId == sessionId);
        Assert.Equal(4, row.GrammarExpression);
        Assert.Equal(1, row.Structure);
        Assert.Contains("grammarExpression", row.PerCriterionRationalesJson);
        Assert.Contains("structure", row.PerCriterionRationalesJson);
        Assert.DoesNotContain("providingStructure", row.PerCriterionRationalesJson);
        Assert.Equal(4, projection.CriterionScores["grammarExpression"].Score);
        Assert.Equal(1, projection.CriterionScores["structure"].Score);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Assessor_WhenBothSpellingsArePresent_TheCanonicalOneWinsInEitherOrder(bool aliasFirst)
    {
        var sessionId = await SeedFinishedSessionWithTranscriptAsync();
        var criteria = new Dictionary<string, object>();
        if (aliasFirst) criteria["grammar"] = Crit(1);
        foreach (var (code, value) in FullCriteria()) criteria[code] = value;
        if (!aliasFirst) criteria["grammar"] = Crit(1);
        var gateway = new SwitchableAiGateway { Completion = ReplyWith(criteria) };

        await BuildAssessor(gateway).RunAssessmentAsync(sessionId, default);

        var row = await _db.SpeakingAiAssessments.AsNoTracking().SingleAsync(a => a.SpeakingSessionId == sessionId);
        Assert.Equal(5, row.GrammarExpression);
    }

    [Fact]
    public async Task Assessor_BareNumberScores_AreAccepted()
    {
        var sessionId = await SeedFinishedSessionWithTranscriptAsync();
        var criteria = FullCriteria();
        criteria["fluency"] = 4;
        var gateway = new SwitchableAiGateway { Completion = ReplyWith(criteria) };

        await BuildAssessor(gateway).RunAssessmentAsync(sessionId, default);

        var row = await _db.SpeakingAiAssessments.AsNoTracking().SingleAsync(a => a.SpeakingSessionId == sessionId);
        Assert.Equal(4, row.Fluency);
    }

    [Fact]
    public async Task Assessor_AcceptsTheMockProvidersSpeakingReply()
    {
        // Dev and smoke stacks grade through MockAiProvider: its Speaking reply must satisfy the
        // stricter nine-criterion contract, or those flows would start failing with 409.
        var sessionId = await SeedFinishedSessionWithTranscriptAsync();
        var mockReply = await new MockAiProvider().CompleteAsync(
            new AiProviderRequest { SystemPrompt = "**This call concerns SPEAKING** — universal 350/500 pass mark." },
            default);
        var gateway = new SwitchableAiGateway { Completion = mockReply.Text };

        await BuildAssessor(gateway).RunAssessmentAsync(sessionId, default);

        Assert.Equal(1, await _db.SpeakingAiAssessments.CountAsync(a => a.SpeakingSessionId == sessionId));
    }

    [Fact]
    public async Task Assessor_StoresTheProviderAndModelThatActuallyRan()
    {
        var sessionId = await SeedFinishedSessionWithTranscriptAsync();
        var gateway = new SwitchableAiGateway { ResolvedProvider = "writing-claude-sub", ResolvedModel = "claude-opus-5-5" };

        var projection = await BuildAssessor(gateway).RunAssessmentAsync(sessionId, default);

        var row = await _db.SpeakingAiAssessments.AsNoTracking().SingleAsync(a => a.SpeakingSessionId == sessionId);
        Assert.Equal("writing-claude-sub", row.Provider);
        Assert.Equal("claude-opus-5-5", row.ModelId);
        Assert.Equal("writing-claude-sub", projection.Provider);
        Assert.Equal("claude-opus-5-5", projection.ModelId);
    }

    [Fact]
    public async Task Assessor_ProvenanceIsTruncatedToTheColumnLimits()
    {
        var sessionId = await SeedFinishedSessionWithTranscriptAsync();
        var gateway = new SwitchableAiGateway
        {
            ResolvedProvider = new string('p', 40),
            ResolvedModel = new string('m', 120),
        };

        await BuildAssessor(gateway).RunAssessmentAsync(sessionId, default);

        var row = await _db.SpeakingAiAssessments.AsNoTracking().SingleAsync(a => a.SpeakingSessionId == sessionId);
        Assert.Equal(32, row.Provider.Length);
        Assert.Equal(96, row.ModelId.Length);
    }

    [Fact]
    public async Task Assessor_ProvenanceFallsBackToTheLegacyConstantsWhenTheGatewayReportsNothing()
    {
        var sessionId = await SeedFinishedSessionWithTranscriptAsync();

        await BuildAssessor(new SwitchableAiGateway()).RunAssessmentAsync(sessionId, default);

        var row = await _db.SpeakingAiAssessments.AsNoTracking().SingleAsync(a => a.SpeakingSessionId == sessionId);
        Assert.Equal("ai_gateway", row.Provider);
        Assert.Equal("gateway-default", row.ModelId);
    }

    [Fact]
    public async Task Assessor_DoesNotGradeTheOpeningConnectionCheck_ButTheStoredTranscriptKeepsIt()
    {
        // Owner spec 4 Oct 2026 section 7.2: "Hi, can you hear me" / "Yeah, I hear you. Go ahead." is not performance.
        const string segments =
            """[{"speaker":"candidate","startMs":1000,"endMs":2000,"text":"Hi, can you hear me?"},{"speaker":"patient","startMs":3000,"endMs":4500,"text":"Yeah, I hear you. Go ahead."},{"speaker":"candidate","startMs":6000,"endMs":9000,"text":"Hello, I am the doctor looking after you today."}]""";
        var sessionId = await SeedFinishedSessionWithTranscriptAsync(segments);
        var gateway = new SwitchableAiGateway();

        await BuildAssessor(gateway).RunAssessmentAsync(sessionId, default);

        var call = Assert.Single(gateway.Requests);
        Assert.DoesNotContain("can you hear me", call.UserInput, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Go ahead", call.UserInput, StringComparison.Ordinal);
        Assert.Contains("Hello, I am the doctor looking after you today.", call.UserInput, StringComparison.Ordinal);
        // The stored transcript is untouched: technical logs keep the connection check.
        var stored = await _db.SpeakingTranscripts.AsNoTracking().SingleAsync(t => t.SpeakingSessionId == sessionId);
        Assert.Contains("can you hear me", stored.SegmentsJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Assessor_StoresTheGraderVersion_AndReportsOneNumberGradeBandAndAProvisionalLabel()
    {
        var sessionId = await SeedFinishedSessionWithTranscriptAsync();
        var gateway = new SwitchableAiGateway { ResolvedProvider = "writing-claude-sub", ResolvedModel = "claude-opus-5-5" };

        var projection = await BuildAssessor(gateway).RunAssessmentAsync(sessionId, default);

        var row = await _db.SpeakingAiAssessments.AsNoTracking().SingleAsync(a => a.SpeakingSessionId == sessionId);
        Assert.Equal("speaking.score.v4", row.PromptTemplateId);
        Assert.Equal("speaking.score.v4|speaking-map.v0-heuristic|audio-none", row.GraderVersion);
        // 5+5+5+5 and 3+2+2+2+2 = 31/39: reported 400, Grade B, Exam-ready — one number for all three.
        Assert.Equal(400, row.EstimatedScaledScore);
        Assert.Equal(400, projection.EstimatedScaledScore);
        Assert.Equal("B", projection.Grade);
        Assert.Equal("exam_ready", projection.ReadinessBand);
        // No grader version has passed calibration yet, so the score is provisional.
        Assert.Equal("provisional", projection.ScoreLabel);
    }

    [Fact]
    public async Task LegacyStoredScore_IsReReportedInTenPointSteps_WithTheBandAndGradeDerivedFromIt()
    {
        var sessionId = await SeedFinishedSessionWithTranscriptAsync();
        _db.SpeakingAiAssessments.Add(new SpeakingAiAssessment
        {
            Id = "spa_legacy",
            SpeakingSessionId = sessionId,
            TranscriptId = "tx-legacy",
            Provider = "ai_gateway",
            ModelId = "gateway-default",
            EstimatedScaledScore = 345, // stored unrounded by the old grader
            ReadinessBand = "borderline", // computed on the unrounded number
            GeneratedAt = DateTimeOffset.UtcNow,
            IsAdvisory = true,
        });
        await _db.SaveChangesAsync();

        var projection = await BuildAssessor(new SwitchableAiGateway()).GetLatestAsync(sessionId, default);

        Assert.NotNull(projection);
        // Shown as 350 and therefore Grade B and Exam-ready — never "350 / Borderline".
        Assert.Equal(350, projection!.EstimatedScaledScore);
        Assert.Equal("B", projection.Grade);
        Assert.Equal("exam_ready", projection.ReadinessBand);
        Assert.Equal("provisional", projection.ScoreLabel);
    }

    [Fact]
    public async Task Assessor_WithAPinnedProvider_GradesOnThePinnedLevelInOneCall()
    {
        var sessionId = await SeedFinishedSessionWithTranscriptAsync();
        var gateway = new SwitchableAiGateway { ResolvedProvider = "writing-claude-sub", ResolvedModel = "claude-opus-5-5" };
        var options = new SpeakingGradingOptions { PinnedProviderCode = "writing-claude-sub", PinnedModel = "claude-opus-5-5" };

        await BuildAssessor(gateway, options).RunAssessmentAsync(sessionId, default);

        var call = Assert.Single(gateway.Requests);
        Assert.Equal("writing-claude-sub", call.Provider);
        Assert.Equal("claude-opus-5-5", call.Model);
        Assert.Equal(AiFeatureCodes.SpeakingGrade, call.FeatureCode);
    }

    [Fact]
    public async Task Assessor_WhenThePinnedLevelFails_GradesOnTheDefaultRoute_AndStoresThatProvider()
    {
        var sessionId = await SeedFinishedSessionWithTranscriptAsync();
        var gateway = new SwitchableAiGateway
        {
            ResolvedProvider = "anthropic",
            ResolvedModel = "claude-sonnet-5",
            FailWhen = request => string.IsNullOrEmpty(request.Provider)
                ? null
                : new AiProviderHttpException("Anthropic", 502, "Bad Gateway"),
        };
        var options = new SpeakingGradingOptions { PinnedProviderCode = "writing-claude-sub", PinnedModel = "claude-opus-5-5" };

        await BuildAssessor(gateway, options).RunAssessmentAsync(sessionId, default);

        Assert.Equal(2, gateway.Requests.Count);
        Assert.Equal("writing-claude-sub", gateway.Requests[0].Provider);
        Assert.Equal(string.Empty, gateway.Requests[1].Provider);
        var row = await _db.SpeakingAiAssessments.AsNoTracking().SingleAsync(a => a.SpeakingSessionId == sessionId);
        Assert.Equal("anthropic", row.Provider);
        Assert.Equal("claude-sonnet-5", row.ModelId);
    }

    [Fact]
    public async Task Assessor_WhenBothLevelsFail_StillSurfacesTheGenericRetryableError()
    {
        var sessionId = await SeedFinishedSessionWithTranscriptAsync();
        var gateway = new SwitchableAiGateway { Fail = true };
        var options = new SpeakingGradingOptions { PinnedProviderCode = "writing-claude-sub", PinnedModel = "claude-opus-5-5" };

        var ex = await Assert.ThrowsAsync<ApiException>(() => BuildAssessor(gateway, options).RunAssessmentAsync(sessionId, default));

        Assert.Equal("speaking_ai_unavailable", ex.ErrorCode);
        Assert.Equal(2, gateway.Requests.Count);
        Assert.Equal(0, await _db.SpeakingAiAssessments.CountAsync(a => a.SpeakingSessionId == sessionId));
    }

    // ── Acoustic evidence: the audio judge's Intelligibility replaces the transcript estimate ──

    private sealed class FakeAudioEvidence(SpeakingAudioEvidence result, bool enabled = true) : ISpeakingAudioEvidenceService
    {
        public List<SpeakingAudioAssessRequest> Requests { get; } = new();

        public Task<bool> IsEnabledAsync(CancellationToken ct) => Task.FromResult(enabled);

        public Task<SpeakingAudioEvidence> AssessAsync(SpeakingAudioAssessRequest request, CancellationToken ct)
        {
            Requests.Add(request);
            return Task.FromResult(result);
        }

        public Task<SpeakingAudioProbeResult> ProbeAsync(Stream audio, string mimeType, string spokenPhrase, CancellationToken ct)
            => throw new NotSupportedException("The probe is not part of grading.");
    }

    private static SpeakingAudioEvidence JudgedAudio(int score = 3, string confidence = "high") => new()
    {
        Status = SpeakingAudioEvidence.StatusAudio,
        IntelligibilityScore = score,
        IntelligibilityRationale = "Several vowels were hard to tell apart.",
        AudioQuality = "good",
        Confidence = confidence,
        Observations = [new SpeakingAudioObservation(1, 12, "The word asthma was stressed on the wrong syllable.", "AS-ma")],
        Fluency = new SpeakingFluencyEvidence(118, 1, 2, 3, 0, ["One long pause before the explanation."]),
        Model = "gpt-audio-1.5",
        ClipCount = 4,
        DurationMs = 61_000,
    };

    [Fact]
    public async Task Assessor_WithTheAudioStageSwitchedOff_NeverCallsIt_AndLabelsTheTranscriptEstimate()
    {
        var sessionId = await SeedFinishedSessionWithTranscriptAsync();
        var disabled = new FakeAudioEvidence(JudgedAudio(), enabled: false);

        var projection = await BuildAssessor(new SwitchableAiGateway(), audioEvidence: disabled).RunAssessmentAsync(sessionId, default);

        Assert.Empty(disabled.Requests);
        var row = await _db.SpeakingAiAssessments.AsNoTracking().SingleAsync(a => a.SpeakingSessionId == sessionId);
        Assert.Equal(SpeakingAiAssessmentService.GraderVersion, row.GraderVersion);
        Assert.DoesNotContain("_acoustic", row.PerCriterionRationalesJson);
        Assert.Equal(5, row.Intelligibility);
        Assert.Equal("transcript_only", projection.IntelligibilityEvidence!.Source);
        Assert.Null(projection.IntelligibilityEvidence.Reason);
        Assert.Equal("low", projection.IntelligibilityEvidence.Confidence);
    }

    [Fact]
    public async Task Assessor_AudioJudged_ReplacesTheTextOnlyIntelligibility_AndStoresTheEvidence()
    {
        var sessionId = await SeedFinishedSessionWithTranscriptAsync();
        var audio = new FakeAudioEvidence(JudgedAudio(score: 3));
        var gateway = new SwitchableAiGateway();
        var audioCallsBeforeTheGrade = -1;
        gateway.OnComplete = () => audioCallsBeforeTheGrade = audio.Requests.Count;

        var projection = await BuildAssessor(gateway, audioEvidence: audio).RunAssessmentAsync(sessionId, default);

        // The audio judge ran first, for this session and learner; the grader then read its evidence.
        Assert.Equal(1, audioCallsBeforeTheGrade);
        var request = Assert.Single(audio.Requests);
        Assert.Equal(sessionId, request.SessionId);
        Assert.Equal(UserId, request.UserId);
        var graderInput = Assert.Single(gateway.Requests).UserInput!;
        Assert.Contains("ACOUSTIC EVIDENCE", graderInput);
        Assert.Contains("3/6", graderInput);
        Assert.DoesNotContain("No audio evidence could be used", graderInput);

        // The grader said 5 from the words; the sound said 3, and the sound is final.
        var row = await _db.SpeakingAiAssessments.AsNoTracking().SingleAsync(a => a.SpeakingSessionId == sessionId);
        Assert.Equal(3, row.Intelligibility);
        Assert.Equal(SpeakingAiAssessmentService.GraderVersionWithAudio("gpt-audio-1.5"), row.GraderVersion);
        Assert.Equal(3, projection.CriterionScores["intelligibility"].Score);
        Assert.Equal("Several vowels were hard to tell apart.", projection.CriterionScores["intelligibility"].Rationale);
        var evidence = projection.IntelligibilityEvidence!;
        Assert.Equal("audio", evidence.Source);
        Assert.Equal("high", evidence.Confidence);
        var observation = Assert.Single(evidence.Observations);
        Assert.Equal(12, observation.ApproxSecond);
        Assert.Contains("wrong syllable", observation.Issue);

        // Stored beside the rationales and read back unchanged by a later GET.
        Assert.Contains("\"_acoustic\"", row.PerCriterionRationalesJson);
        var reread = await BuildAssessor(new SwitchableAiGateway()).GetLatestAsync(sessionId, default);
        Assert.NotNull(reread);
        Assert.Equal("audio", reread!.IntelligibilityEvidence!.Source);
        Assert.Equal(3, reread.CriterionScores["intelligibility"].Score);
    }

    [Fact]
    public async Task Assessor_AudioUnavailable_StillGrades_ButSaysSo_WithLowConfidence()
    {
        var sessionId = await SeedFinishedSessionWithTranscriptAsync();
        var audio = new FakeAudioEvidence(SpeakingAudioEvidence.Unavailable("no_audio"));
        var gateway = new SwitchableAiGateway();

        var projection = await BuildAssessor(gateway, audioEvidence: audio).RunAssessmentAsync(sessionId, default);

        var graderInput = Assert.Single(gateway.Requests).UserInput!;
        Assert.Contains("No audio evidence could be used", graderInput);
        Assert.Contains("no audio recording was kept", graderInput);
        Assert.DoesNotContain("ACOUSTIC EVIDENCE", graderInput);

        var row = await _db.SpeakingAiAssessments.AsNoTracking().SingleAsync(a => a.SpeakingSessionId == sessionId);
        Assert.Equal(5, row.Intelligibility); // the grader's own estimate from the transcript
        Assert.Equal("low", row.ConfidenceBand); // although the grader itself said high
        Assert.Equal(SpeakingAiAssessmentService.GraderVersion, row.GraderVersion); // no audio stage in this grade
        var evidence = projection.IntelligibilityEvidence!;
        Assert.Equal("transcript_only", evidence.Source);
        Assert.Equal("no_audio", evidence.Reason);
        Assert.Equal("no audio recording was kept for this attempt", evidence.ReasonText);
        Assert.Equal("low", evidence.Confidence);
        Assert.Equal("low", projection.ConfidenceBand);
    }

    [Fact]
    public async Task Assessor_AudioJudgedWithLowConfidence_KeepsTheJudgement_ButTheGradeIsLowConfidence()
    {
        var sessionId = await SeedFinishedSessionWithTranscriptAsync();
        var audio = new FakeAudioEvidence(JudgedAudio(score: 4, confidence: "low"));

        var projection = await BuildAssessor(new SwitchableAiGateway(), audioEvidence: audio).RunAssessmentAsync(sessionId, default);

        var row = await _db.SpeakingAiAssessments.AsNoTracking().SingleAsync(a => a.SpeakingSessionId == sessionId);
        Assert.Equal(4, row.Intelligibility);
        Assert.Equal("low", projection.ConfidenceBand);
        Assert.Equal("audio", projection.IntelligibilityEvidence!.Source);
        Assert.Equal("low", projection.IntelligibilityEvidence.Confidence);
    }

    private sealed class SwitchableAiGateway : IAiGatewayService
    {
        public bool Fail { get; set; }
        public Action? OnComplete { get; set; }

        /// <summary>Raw model reply; null means the valid nine-criterion reply.</summary>
        public string? Completion { get; set; }

        /// <summary>What the "gateway" reports as the provider/model that served the call.</summary>
        public string ResolvedProvider { get; set; } = string.Empty;
        public string ResolvedModel { get; set; } = string.Empty;

        /// <summary>Throws the returned exception for a request (used to fail only the pinned level).</summary>
        public Func<AiGatewayRequest, Exception?>? FailWhen { get; set; }

        public List<AiGatewayRequest> Requests { get; } = new();

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
            Requests.Add(request);
            if (Fail) throw new InvalidOperationException("provider unreachable");
            if (FailWhen?.Invoke(request) is { } failure) throw failure;
            OnComplete?.Invoke();
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(new AiGatewayResult
            {
                Completion = Completion ?? ValidAssessmentJson(),
                Metadata = request.Prompt!.Metadata,
                RulebookVersion = request.Prompt!.Metadata.RulebookVersion,
                AppliedRuleIds = request.Prompt!.Metadata.AppliedRuleIds,
                ResolvedProvider = ResolvedProvider,
                ResolvedModel = ResolvedModel,
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
}
