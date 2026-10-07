using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Rulebook;
using OetLearner.Api.Services.Speaking;

namespace OetLearner.Api.Tests.Speaking;

/// <summary>
/// The harness (owner spec 4 Oct 2026): grades the expert-marked performances with the CURRENT grader as durable operations,
/// keeps only numbers, never competes with a learner's grade, and reports against the expert's marks.
/// </summary>
public sealed class SpeakingGraderCalibrationRunTests : IAsyncLifetime
{
    private const string AdminId = "admin-1";
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);

    private LearnerDbContext _db = default!;

    public Task InitializeAsync()
    {
        _db = new LearnerDbContext(new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase($"speaking-calibration-run-{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options);
        _db.RolePlayCards.Add(new RolePlayCard
        {
            Id = "rpc-cal",
            ContentItemId = "ci-cal",
            ProfessionId = "medicine",
            ScenarioTitle = "Asthma review",
            Setting = "General practice",
            CandidateRole = "Doctor",
            InterlocutorRole = "Patient",
            Background = "Review of poorly controlled asthma.",
            Task1 = "Explain the peak-flow result",
            PrepTimeSeconds = 180,
            RolePlayTimeSeconds = 300,
            Status = ContentStatus.Published,
            CreatedAt = Now,
            UpdatedAt = Now,
        });
        // learner-1 accepted the calibration wording (v3) before these performances were recorded.
        _db.SpeakingComplianceConsents.Add(new SpeakingComplianceConsent
        {
            Id = "consent-1", UserId = "learner-1", ConsentType = SpeakingComplianceConsentTypes.Recording,
            ConsentVersion = "recording.v3", AcceptedAt = Now.AddDays(-1),
        });
        AddMarkedPerformance("one", 4000);
        AddMarkedPerformance("two", 3200);
        _db.SaveChanges();
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _db.Dispose();
        return Task.CompletedTask;
    }

    // ── Creating a run ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateRun_FreezesTheMarkedPerformances_WithRepeatsOfEach_AndOnlyOneRunAtATime()
    {
        var service = Service(new ReplyGateway());

        var view = await service.CreateRunAsync(AdminId, "Admin", new SpeakingGraderCalibrationRunCreateRequest(3, true), default);

        Assert.Equal("running", view.Status);
        Assert.Equal(3, view.Repeats);
        Assert.True(view.UseAudio);
        Assert.Equal(6, view.Progress.Total); // two marked performances x three repeats
        Assert.Equal(6, view.Progress.Pending);
        Assert.Equal(6, await _db.SpeakingGraderCalibrationGrades.CountAsync(g => g.RunId == view.Id));
        Assert.Equal(2, await _db.SpeakingGraderCalibrationGrades.Where(g => g.RunId == view.Id).Select(g => g.SampleId).Distinct().CountAsync());

        var second = await Assert.ThrowsAsync<ApiException>(() =>
            service.CreateRunAsync(AdminId, "Admin", new SpeakingGraderCalibrationRunCreateRequest(null, null), default));
        Assert.Equal("speaking_calibration_run_active", second.ErrorCode);
    }

    [Fact]
    public async Task CreateRun_NeedsAtLeastTwoRepeats_AndOnlyCountsMarkedPerformances()
    {
        var service = Service(new ReplyGateway());
        _db.SpeakingGraderCalibrationSamples.Add(Sample("unmarked", labelled: false));
        await _db.SaveChangesAsync();

        var view = await service.CreateRunAsync(AdminId, "Admin", new SpeakingGraderCalibrationRunCreateRequest(1, null), default);

        Assert.Equal(2, view.Repeats); // one repeat cannot show repeatability
        Assert.True(view.UseAudio); // default: the audio judge runs
        Assert.Equal(4, view.Progress.Total); // the unmarked performance is not graded
        Assert.DoesNotContain(await _db.SpeakingGraderCalibrationGrades.Select(g => g.SampleId).ToListAsync(), id => id == "unmarked");
    }

    [Fact]
    public async Task CreateRun_WithNothingMarked_SaysWhy()
    {
        foreach (var sample in await _db.SpeakingGraderCalibrationSamples.ToListAsync()) sample.Status = SpeakingGraderCalibrationSampleStatus.Pending;
        await _db.SaveChangesAsync();

        var failure = await Assert.ThrowsAsync<ApiException>(() =>
            Service(new ReplyGateway()).CreateRunAsync(AdminId, "Admin", new SpeakingGraderCalibrationRunCreateRequest(null, null), default));

        Assert.Equal("speaking_calibration_nothing_to_grade", failure.ErrorCode);
    }

    // ── Queueing grades ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Next_QueuesOneGradeAtATime_AsADurableOperation_AndSaysBusyWhileItRuns()
    {
        var service = Service(new ReplyGateway());
        var run = await service.CreateRunAsync(AdminId, "Admin", new SpeakingGraderCalibrationRunCreateRequest(2, false), default);

        var first = await service.NextAsync(run.Id, AdminId, default);

        Assert.Equal("queued", first.State);
        Assert.NotNull(first.GradeId);
        Assert.Equal(1, first.Progress.Queued);
        var operation = await _db.AiOperations.AsNoTracking().SingleAsync(o => o.ResourceType == SpeakingGraderCalibrationService.GradeResourceType);
        Assert.Equal(first.GradeId, operation.ResourceId);
        Assert.Equal(AiFeatureCodes.SpeakingGrade, operation.FeatureCode);
        Assert.Equal(AiOperationState.Queued, operation.State);

        var second = await service.NextAsync(run.Id, AdminId, default);
        Assert.Equal("busy", second.State);
        Assert.Equal(1, await _db.AiOperations.CountAsync(o => o.ResourceType == SpeakingGraderCalibrationService.GradeResourceType));
    }

    [Fact]
    public async Task Next_YieldsToALearnersGrade_AndStartsWhenNoLearnerIsWaiting()
    {
        var service = Service(new ReplyGateway());
        var run = await service.CreateRunAsync(AdminId, "Admin", new SpeakingGraderCalibrationRunCreateRequest(2, false), default);
        var learner = new AiOperation
        {
            Id = "op-learner",
            Module = "speaking",
            FeatureCode = AiFeatureCodes.SpeakingGrade,
            UserId = "learner-1",
            ResourceType = "speaking_session",
            ResourceId = "sps_learner",
            IdempotencyKey = "speaking.assess:sps_learner",
            State = AiOperationState.Queued,
            CreatedAt = Now,
            UpdatedAt = Now,
        };
        _db.AiOperations.Add(learner);
        await _db.SaveChangesAsync();

        var yielded = await service.NextAsync(run.Id, AdminId, default);

        Assert.Equal("yield", yielded.State);
        Assert.Null(yielded.GradeId);
        Assert.False(await _db.AiOperations.AnyAsync(o => o.ResourceType == SpeakingGraderCalibrationService.GradeResourceType));

        // A learner's Writing grade holds it back too; once nobody is waiting it goes ahead.
        learner.FeatureCode = AiFeatureCodes.WritingGrade;
        await _db.SaveChangesAsync();
        Assert.Equal("yield", (await service.NextAsync(run.Id, AdminId, default)).State);
        learner.State = AiOperationState.Completed;
        await _db.SaveChangesAsync();
        Assert.Equal("queued", (await service.NextAsync(run.Id, AdminId, default)).State);
    }

    // ── Running a grade ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteGrade_GradesWithTheCurrentGrader_KeepsOnlyNumbers_AndLeavesLearnerTablesAlone()
    {
        var gateway = new ReplyGateway();
        var service = Service(gateway);
        var run = await service.CreateRunAsync(AdminId, "Admin", new SpeakingGraderCalibrationRunCreateRequest(2, false), default);
        var next = await service.NextAsync(run.Id, AdminId, default);

        await service.ExecuteGradeAsync(next.GradeId!, default);

        var grade = await _db.SpeakingGraderCalibrationGrades.AsNoTracking().SingleAsync(g => g.Id == next.GradeId);
        Assert.Equal(SpeakingGraderCalibrationGradeStatus.Done, grade.Status);
        Assert.Equal(1, grade.Attempts);
        Assert.Equal(31, grade.RawTotal);
        Assert.Equal(OetScoring.SpeakingRawToReported[31], grade.ReportedScaled);
        Assert.Equal("transcript_only", grade.IntelligibilitySource);
        Assert.Equal(SpeakingAiAssessmentService.GraderVersion, grade.GraderVersion);
        Assert.Equal("claude-opus-5-5", grade.ModelId);
        var scores = JsonSerializer.Deserialize<Dictionary<string, int>>(grade.ScoresJson!)!;
        Assert.Equal(9, scores.Count);
        Assert.Equal(5, scores["intelligibility"]);
        Assert.Equal(2, scores["informationGiving"]);

        // The same grade a learner gets, for no learner: no plan gate, no grant, nothing written for the learner.
        // The primary grade is followed by its bounded secondary review (best effort; it cannot fail the grade).
        Assert.Equal(2, gateway.Requests.Count);
        var call = gateway.Requests[0];
        Assert.Equal(AiFeatureCodes.SpeakingGrade, call.FeatureCode);
        Assert.Null(call.UserId);
        Assert.False(call.FreeSampleGrant);
        Assert.Equal(SpeakingAiAssessmentService.PromptTemplateId, call.PromptTemplateId);
        Assert.Contains("the pinned transcript sentence", call.UserInput);
        Assert.Equal(AiFeatureCodes.SpeakingGradeReview, gateway.Requests[1].FeatureCode);
        Assert.Equal(0, await _db.SpeakingAiAssessments.CountAsync());
        Assert.Equal(0, await _db.AiCreditReservations.CountAsync());
        Assert.Equal(AiOperationState.Completed, (await _db.AiOperations.AsNoTracking().SingleAsync(o => o.Id == grade.OperationId)).State);
    }

    [Fact]
    public async Task ExecuteGrade_GradesTheTranscriptPinnedAtPromotion_NotALaterOne()
    {
        var gateway = new ReplyGateway();
        var service = Service(gateway);
        // A later re-transcription must never change what the expert and the AI are compared on.
        _db.SpeakingTranscripts.Add(Transcript("later", "sps_one", "A later, different transcript.", isLatest: true));
        var oldTranscript = await _db.SpeakingTranscripts.SingleAsync(t => t.Id == "tx-one");
        oldTranscript.IsLatest = false;
        await _db.SaveChangesAsync();
        var run = await service.CreateRunAsync(AdminId, "Admin", new SpeakingGraderCalibrationRunCreateRequest(2, false), default);
        var next = await service.NextAsync(run.Id, AdminId, default);
        Assert.Equal("spgc_one", (await _db.SpeakingGraderCalibrationGrades.SingleAsync(g => g.Id == next.GradeId)).SampleId);

        await service.ExecuteGradeAsync(next.GradeId!, default);

        // The primary grade reads the pinned transcript (the secondary review of it sees the same text appended).
        Assert.Equal(2, gateway.Requests.Count);
        var request = gateway.Requests[0];
        Assert.Contains("the pinned transcript sentence", request.UserInput);
        Assert.DoesNotContain("A later, different transcript.", request.UserInput);
    }

    [Fact]
    public async Task ExecuteGrade_WhenTheGraderFails_MarksItFailed_AndNextTriesAgainUpToThreeTimes()
    {
        var gateway = new ReplyGateway { Fail = true };
        var service = Service(gateway);
        var run = await service.CreateRunAsync(AdminId, "Admin", new SpeakingGraderCalibrationRunCreateRequest(2, false), default);

        var attemptedGrades = new HashSet<string>();
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            var next = await service.NextAsync(run.Id, AdminId, default);
            Assert.Equal("queued", next.State);
            attemptedGrades.Add(next.GradeId!);
            await service.ExecuteGradeAsync(next.GradeId!, default);
        }

        // Each queue call picks the next pending grade, so three failed attempts touched three grades (or the same one
        // retried): none may have exceeded its attempts, and every failure carries a reason code, never learner text.
        var failed = await _db.SpeakingGraderCalibrationGrades.AsNoTracking()
            .Where(g => g.RunId == run.Id && g.Status == SpeakingGraderCalibrationGradeStatus.Failed).ToListAsync();
        Assert.NotEmpty(failed);
        Assert.All(failed, g => Assert.Equal("speaking_ai_unavailable", g.Error));
        Assert.All(await _db.SpeakingGraderCalibrationGrades.AsNoTracking().ToListAsync(), g => Assert.True(g.Attempts <= 3));
        Assert.All(await _db.AiOperations.AsNoTracking().Where(o => o.ResourceType == SpeakingGraderCalibrationService.GradeResourceType).ToListAsync(),
            o => Assert.Equal(AiOperationState.FailedTerminal, o.State));

        // Keep failing until every grade has used its attempts: the run then reports "done", never loops forever.
        string state;
        var guard = 0;
        do
        {
            var next = await service.NextAsync(run.Id, AdminId, default);
            state = next.State;
            if (next.GradeId is not null) await service.ExecuteGradeAsync(next.GradeId, default);
        }
        while (state == "queued" && ++guard < 50);

        Assert.Equal("done", state);
        var progress = (await service.GetRunAsync(run.Id, default)).Progress;
        Assert.Equal(progress.Total, progress.Failed);
        Assert.All(await _db.SpeakingGraderCalibrationGrades.AsNoTracking().ToListAsync(), g => Assert.Equal(3, g.Attempts));
    }

    // ── Finishing ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Finalize_FreezesTheReport_TheGraderVersion_AndWritesAnAuditEvent()
    {
        var service = Service(new ReplyGateway());
        var run = await service.CreateRunAsync(AdminId, "Admin", new SpeakingGraderCalibrationRunCreateRequest(2, false), default);
        var guard = 0;
        while (guard++ < 20)
        {
            var next = await service.NextAsync(run.Id, AdminId, default);
            if (next.State is "done" or "complete") break;
            if (next.GradeId is not null) await service.ExecuteGradeAsync(next.GradeId, default);
        }

        var live = await service.GetRunAsync(run.Id, default);
        Assert.Equal(4, live.Progress.Done);
        Assert.NotNull(live.Report); // the report so far is available at any time
        Assert.Equal(string.Empty, live.GraderVersion); // not known until it is frozen

        var final = await service.FinalizeAsync(run.Id, AdminId, "Admin", default);

        Assert.Equal("complete", final.Status);
        Assert.Equal(SpeakingAiAssessmentService.GraderVersion, final.GraderVersion);
        Assert.NotNull(final.FinalizedAt);
        var stored = await service.GetRunAsync(run.Id, default);
        Assert.Equal(2, stored.Report!.Performances);
        Assert.Equal(4, stored.Report.Observations);
        Assert.Equal(2, stored.Report.Repeats);
        // Two marked performances can never satisfy the coverage a calibration needs: it says so, in plain words.
        Assert.False(stored.Report.Verdict.Passed);
        Assert.Contains(stored.Report.Verdict.Failures, f => f.Contains("at least 30 expert-marked performances (has 2)"));
        var audit = await _db.AuditEvents.AsNoTracking().SingleAsync(a => a.Action == "SpeakingGraderCalibrationRunFinalized");
        Assert.Equal(run.Id, audit.ResourceId);
        Assert.DoesNotContain("pinned transcript", audit.Details); // numbers only

        // After finalising, next never starts more work, and finalising again changes nothing.
        Assert.Equal("complete", (await service.NextAsync(run.Id, AdminId, default)).State);
        Assert.Equal("complete", (await service.FinalizeAsync(run.Id, AdminId, "Admin", default)).Status);
        Assert.Equal(1, await _db.AuditEvents.CountAsync(a => a.Action == "SpeakingGraderCalibrationRunFinalized"));
    }

    [Fact]
    public async Task APerformanceMarkedAfterTheRunBegan_IsNotPartOfThatRun()
    {
        var service = Service(new ReplyGateway());
        var run = await service.CreateRunAsync(AdminId, "Admin", new SpeakingGraderCalibrationRunCreateRequest(2, false), default);
        _db.SpeakingGraderCalibrationSamples.Add(Sample("late", labelled: true));
        await _db.SaveChangesAsync();

        var view = await service.GetRunAsync(run.Id, default);

        Assert.Equal(4, view.Progress.Total);
        Assert.False(await _db.SpeakingGraderCalibrationGrades.AnyAsync(g => g.SampleId == "late"));
    }

    // ── Full Mock scope + owner pilot (owner request 7 Oct 2026) ─────────────────────────

    [Fact]
    public async Task AMockScopePilotRun_GradesWholeTestsWithTheCombinedGrader_AndItsReportCannotPass()
    {
        AddMarkedMock();
        await _db.SaveChangesAsync();
        var gateway = new ReplyGateway();
        var service = Service(gateway);
        var run = await service.CreateRunAsync(AdminId, "Admin", new SpeakingGraderCalibrationRunCreateRequest(2, false, "mock", Pilot: true), default);
        Assert.True(run.Pilot);
        Assert.Equal("mock", run.Scope);
        Assert.Equal(2, run.Progress.Total); // one whole test, graded twice

        var guard = 0;
        while (guard++ < 10)
        {
            var next = await service.NextAsync(run.Id, AdminId, default);
            if (next.State is "done" or "complete") break;
            if (next.GradeId is not null) await service.ExecuteGradeAsync(next.GradeId, default);
        }

        // Both grades went through the combined prompt: one judgement over both cards, never two card grades.
        // (Each primary grade is followed by its bounded secondary review — the review of a review-free prompt.)
        var primaries = gateway.Requests.Where(r => r.FeatureCode == AiFeatureCodes.SpeakingGrade).ToList();
        Assert.Equal(2, primaries.Count);
        Assert.All(gateway.Requests.Where(r => r.FeatureCode != AiFeatureCodes.SpeakingGrade),
            r => Assert.Equal(AiFeatureCodes.SpeakingGradeReview, r.FeatureCode));
        Assert.All(primaries, request =>
        {
            Assert.Equal(SpeakingAiAssessmentService.CombinedPromptTemplateId, request.PromptTemplateId);
            Assert.Contains("ONE COMPLETE OET SPEAKING TEST", request.UserInput);
            Assert.Contains("the Card A sentence", request.UserInput);
            Assert.Contains("the Card B sentence", request.UserInput);
            Assert.Null(request.UserId);
        });
        Assert.Equal(0, await _db.SpeakingAiAssessments.CountAsync());

        await service.FinalizeAsync(run.Id, AdminId, "Admin", default);
        var report = (await service.GetRunAsync(run.Id, default)).Report!;
        Assert.Equal(1, report.Performances);
        Assert.Equal(2, report.Observations);
        Assert.Equal(2, report.Repeats);
        // A pilot is an informational comparison: no coverage failure is raised, every observation is advisory, and the
        // verdict can never pass — the Speaking score stays Provisional.
        Assert.Equal("pilot", report.Verdict.Mode);
        Assert.False(report.Verdict.Passed);
        Assert.Empty(report.Verdict.Failures);
        Assert.Contains(report.Verdict.Advisory ?? [], a => a.Contains("OWNER PILOT"));
    }

    [Fact]
    public async Task AMockScopeRun_WithNothingMarked_SaysWhy()
    {
        var failure = await Assert.ThrowsAsync<ApiException>(() =>
            Service(new ReplyGateway()).CreateRunAsync(AdminId, "Admin", new SpeakingGraderCalibrationRunCreateRequest(null, null, "mock"), default));

        Assert.Equal("speaking_calibration_nothing_to_grade", failure.ErrorCode);
        Assert.Contains("Full Mock", failure.Message);
    }

    private void AddMarkedMock()
    {
        _db.SpeakingExamSessions.Add(new SpeakingExamSession
        {
            Id = "exam-1",
            UserId = "learner-1",
            ProfessionId = "medicine",
            Mode = SpeakingExamMode.Ai,
            State = SpeakingExamState.Completed,
            CardAId = "rpc-cal",
            CardBId = "rpc-cal",
            SessionAId = "sps_mock_a",
            SessionBId = "sps_mock_b",
            CreatedAt = Now,
            UpdatedAt = Now,
        });
        _db.SpeakingSessions.Add(new SpeakingSession
        {
            Id = "sps_mock_a",
            UserId = "learner-1",
            RolePlayCardId = "rpc-cal",
            Mode = SpeakingSessionMode.AiExam,
            State = SpeakingSessionState.Finished,
            EndedAt = Now,
            CreatedAt = Now,
            UpdatedAt = Now,
        });
        _db.SpeakingTranscripts.Add(Transcript("tx_mock_a", "sps_mock_a", "the Card A sentence", isLatest: true));
        _db.SpeakingSessions.Add(new SpeakingSession
        {
            Id = "sps_mock_b",
            UserId = "learner-1",
            RolePlayCardId = "rpc-cal",
            Mode = SpeakingSessionMode.AiExam,
            State = SpeakingSessionState.Finished,
            EndedAt = Now,
            CreatedAt = Now,
            UpdatedAt = Now,
        });
        _db.SpeakingTranscripts.Add(Transcript("tx_mock_b", "sps_mock_b", "the Card B sentence", isLatest: true));
        _db.SpeakingGraderCalibrationMockSamples.Add(new SpeakingGraderCalibrationMockSample
        {
            Id = "spgcm_mock",
            SpeakingExamId = "exam-1",
            SessionAId = "sps_mock_a",
            SessionBId = "sps_mock_b",
            TranscriptAId = "tx_mock_a",
            TranscriptBId = "tx_mock_b",
            CardAId = "rpc-cal",
            CardBId = "rpc-cal",
            ProfessionId = "medicine",
            HasAudio = false,
            Status = SpeakingGraderCalibrationSampleStatus.Labelled,
            ExpertScoresJson = JsonSerializer.Serialize(new Dictionary<string, int>
            {
                ["intelligibility"] = 5, ["fluency"] = 5, ["appropriateness"] = 5, ["grammarExpression"] = 5,
                ["relationshipBuilding"] = 3, ["patientPerspective"] = 2, ["structure"] = 2, ["informationGathering"] = 2, ["informationGiving"] = 2,
            }),
            ExpertOverallScaled = 400,
            PromotedById = AdminId,
            PromotedAt = Now,
            LabelledAt = Now,
            UpdatedAt = Now,
        });
    }

    // ── The workers' hand-off and the guards ───────────────────────────────────────────────

    [Fact]
    public void TheWorkersResumePath_RunsACalibrationGrade_BeforeTheLearnerGradeBranch()
    {
        var source = File.ReadAllText(FindSource("Services/Ai/AiLeasedOperationHandler.cs"));

        var calibration = source.IndexOf("SpeakingGraderCalibrationService.GradeResourceType", StringComparison.Ordinal);
        var learner = source.IndexOf("ISpeakingCanonicalAssessmentService", StringComparison.Ordinal);
        Assert.True(calibration > 0 && learner > 0 && calibration < learner,
            "a calibration grade shares the speaking.grade feature code, so it must be recognised before the learner branch");
    }

    [Fact]
    public void TheHarness_NeverWritesALearnersResultOrACreditOrAnAssessmentRow()
    {
        var runs = File.ReadAllText(FindSource("Services/Speaking/SpeakingGraderCalibrationService.Runs.cs"));
        var calibration = File.ReadAllText(FindSource("Services/Speaking/SpeakingAiAssessmentService.Calibration.cs"));

        foreach (var source in new[] { runs, calibration })
        {
            Assert.DoesNotContain("SpeakingAiAssessments.Add", source);
            Assert.DoesNotContain("AiCreditReservation", source);
            Assert.DoesNotContain("SpeakingCreditSettlement", source);
            Assert.DoesNotContain("JevSpeakingAdvisor", source);
        }

        Assert.Contains("UserId: null", calibration);
    }

    // ── Builders ───────────────────────────────────────────────────────────────────────────

    private SpeakingGraderCalibrationService Service(IAiGatewayService gateway)
        => new(_db, new FixedClock(Now), new SpeakingAiAssessmentService(_db, gateway, NullLogger<SpeakingAiAssessmentService>.Instance));

    private void AddMarkedPerformance(string key, int startMs)
    {
        _db.SpeakingSessions.Add(new SpeakingSession
        {
            Id = $"sps_{key}",
            UserId = "learner-1",
            RolePlayCardId = "rpc-cal",
            Mode = SpeakingSessionMode.AiSelfPractice,
            State = SpeakingSessionState.Finished,
            EndedAt = Now,
            CreatedAt = Now,
            UpdatedAt = Now,
        });
        _db.SpeakingTranscripts.Add(Transcript($"tx-{key}", $"sps_{key}", "the pinned transcript sentence", isLatest: true, endMs: startMs));
        _db.SpeakingGraderCalibrationSamples.Add(Sample(key, labelled: true));
    }

    private static SpeakingGraderCalibrationSample Sample(string key, bool labelled)
    {
        var id = key is "one" or "two" ? $"spgc_{key}" : key;
        return new SpeakingGraderCalibrationSample
        {
            Id = id,
            SpeakingSessionId = $"sps_{key}",
            TranscriptId = $"tx-{key}",
            RolePlayCardId = "rpc-cal",
            ProfessionId = "medicine",
            HasAudio = false,
            Status = labelled ? SpeakingGraderCalibrationSampleStatus.Labelled : SpeakingGraderCalibrationSampleStatus.Pending,
            ExpertScoresJson = labelled
                ? JsonSerializer.Serialize(new Dictionary<string, int>
                {
                    ["intelligibility"] = 5, ["fluency"] = 5, ["appropriateness"] = 5, ["grammarExpression"] = 5,
                    ["relationshipBuilding"] = 3, ["patientPerspective"] = 2, ["structure"] = 2, ["informationGathering"] = 2, ["informationGiving"] = 2,
                })
                : null,
            ExpertOverallScaled = labelled ? 400 : null,
            PromotedById = AdminId,
            PromotedAt = Now,
            LabelledAt = labelled ? Now : null,
            UpdatedAt = Now,
        };
    }

    private static SpeakingTranscript Transcript(string id, string sessionId, string text, bool isLatest, int endMs = 4000)
        => new()
        {
            Id = id,
            SpeakingSessionId = sessionId,
            Provider = "live-roleplay",
            Language = "en",
            SegmentsJson = JsonSerializer.Serialize(new[] { new { speaker = "candidate", startMs = 0, endMs, text } }),
            IsLatest = isLatest,
            WordCount = 4,
            MeanConfidence = 0.9,
            GeneratedAt = Now,
        };

    private static string FindSource(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "backend", "src", "OetLearner.Api", relative);
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }

        throw new FileNotFoundException($"Could not locate {relative} from {AppContext.BaseDirectory}.");
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    /// <summary>Answers every grade with the same valid nine-criterion reply (raw 31/39) as the grader's pinned Max route would.</summary>
    private sealed class ReplyGateway : IAiGatewayService
    {
        public bool Fail { get; set; }
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
            return Task.FromResult(new AiGatewayResult
            {
                Completion = Reply,
                Metadata = request.Prompt!.Metadata,
                RulebookVersion = request.Prompt.Metadata.RulebookVersion,
                AppliedRuleIds = request.Prompt.Metadata.AppliedRuleIds,
                ResolvedProvider = "writing-claude-sub",
                ResolvedModel = "claude-opus-5-5",
            });
        }

        private static string Crit(int score) => $$"""{"score":{{score}},"rationale":"ok","evidenceQuotes":[]}""";

        private static readonly string Reply = $$"""
            {
              "criterionScores": {
                "intelligibility": {{Crit(5)}},
                "fluency": {{Crit(5)}},
                "appropriateness": {{Crit(5)}},
                "grammarExpression": {{Crit(5)}},
                "relationshipBuilding": {{Crit(3)}},
                "patientPerspective": {{Crit(2)}},
                "structure": {{Crit(2)}},
                "informationGathering": {{Crit(2)}},
                "informationGiving": {{Crit(2)}}
              },
              "overallSummary": "Strong, organised communication.",
              "confidenceBand": "high"
            }
            """;
    }
}
