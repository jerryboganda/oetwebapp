using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Content;
using OetLearner.Api.Services.Pronunciation;
using OetLearner.Api.Services.Rulebook;
using OetLearner.Api.Services.Speaking;

namespace OetLearner.Api.Tests.Speaking;

public sealed class SpeakingSimulationV11NumericScoringLockTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task New_card_assessment_is_locked_even_when_owner_gates_are_approved(bool approveAll)
    {
        await using var fixture = await Fixture.CreateAsync(approveAll);
        var gate = await fixture.Gate.EvaluateAsync("medicine", default);
        Assert.True(gate.IsReleased);

        await AssertLockedAsync(() => fixture.Service.RunAssessmentAsync("session-a", default));

        Assert.Equal(0, fixture.Gateway.Calls);
        Assert.Empty(await fixture.Db.SpeakingSimulationV11Assessments.ToListAsync());
        Assert.Empty(await fixture.Db.SpeakingSimulationV11CriterionScores.ToListAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cached_card_is_locked_on_creation_and_retrieval(bool approveAll)
    {
        await using var fixture = await Fixture.CreateAsync(approveAll);
        fixture.AddScoredCard("a");
        await fixture.Db.SaveChangesAsync();

        await AssertLockedAsync(() => fixture.Service.RunAssessmentAsync("session-a", default));
        await AssertLockedAsync(() => fixture.Service.GetLatestAsync("session-a", default));

        Assert.Equal(0, fixture.Gateway.Calls);
        Assert.Equal(1, await fixture.Db.SpeakingSimulationV11Assessments.CountAsync());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-json")]
    [InlineData("{\"criteria\":[{\"rawScore\":70,\"weightedScore\":7,\"scoreBand\":\"B\"}]}")]
    public async Task Completed_card_without_top_level_numbers_is_still_locked(string? reportJson)
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Db.SpeakingSimulationV11Assessments.Add(new SpeakingSimulationV11Assessment
        {
            Id = "cached-card", SpeakingSessionId = "session-a", RolePlayCardId = "card-a",
            AssessmentKind = "card", Status = SpeakingSimulationV11AssessmentStatus.Complete,
            ReportJson = reportJson,
        });
        await fixture.Db.SaveChangesAsync();

        await AssertLockedAsync(() => fixture.Service.RunAssessmentAsync("session-a", default));
        await AssertLockedAsync(() => fixture.Service.GetLatestAsync("session-a", default));
        Assert.Equal(0, fixture.Gateway.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Persisted_combined_nested_scores_are_locked_on_creation_and_retrieval(bool approveAll)
    {
        await using var fixture = await Fixture.CreateAsync(approveAll);
        fixture.Db.SpeakingSimulationV11Assessments.Add(new SpeakingSimulationV11Assessment
        {
            Id = "cached-combined", ExamSessionId = "exam-1", AssessmentKind = "combined",
            Status = SpeakingSimulationV11AssessmentStatus.Complete,
            // The row and report roots have no scores. Numeric evidence exists only inside the report.
            ReportJson = JsonSerializer.Serialize(ScoredReport("cached-combined", "combined") with
            {
                EstimatedPracticeScore = null, ScoreRangeLow = null, ScoreRangeHigh = null,
                ConfidenceScore = null,
            }),
        });
        await fixture.Db.SaveChangesAsync();

        await AssertLockedAsync(() => fixture.Service.GetLatestCombinedAsync("exam-1", default));
        await AssertLockedAsync(() => fixture.Service.RunCombinedAssessmentAsync("exam-1", default));
        Assert.Equal(0, fixture.Gateway.Calls);
        Assert.Equal(1, await fixture.Db.SpeakingSimulationV11Assessments.CountAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task New_combined_assessment_is_locked_before_aggregation_or_storage(bool approveAll)
    {
        await using var fixture = await Fixture.CreateAsync(approveAll);
        var gate = await fixture.Gate.EvaluateAsync("medicine", default);
        Assert.True(gate.IsReleased);
        fixture.AddScoredCard("a");
        fixture.AddScoredCard("b");
        await fixture.Db.SaveChangesAsync();
        var originalCriteriaCount = await fixture.Db.SpeakingSimulationV11CriterionScores.CountAsync();

        await AssertLockedAsync(() => fixture.Service.RunCombinedAssessmentAsync("exam-1", default));

        Assert.Equal(0, fixture.Gateway.Calls);
        Assert.Equal(2, await fixture.Db.SpeakingSimulationV11Assessments.CountAsync());
        Assert.False(await fixture.Db.SpeakingSimulationV11Assessments
            .AnyAsync(x => x.AssessmentKind == "combined"));
        Assert.Equal(originalCriteriaCount, await fixture.Db.SpeakingSimulationV11CriterionScores.CountAsync());
    }

    [Theory]
    [InlineData("unsupported_profession")]
    [InlineData("human_examiner_required")]
    [InlineData("v11_persona_not_captured")]
    public async Task Technical_paths_return_and_persist_a_safe_no_score_envelope(string code)
    {
        await using var fixture = await Fixture.CreateAsync(
            capturePersona: code != "v11_persona_not_captured");
        if (code == "unsupported_profession")
            (await fixture.Db.RolePlayCards.SingleAsync(x => x.Id == "card-a")).ProfessionId = "unsupported";
        if (code == "human_examiner_required")
            (await fixture.Db.SpeakingSessions.SingleAsync(x => x.Id == "session-a")).Mode = SpeakingSessionMode.LiveTutor;
        await fixture.Db.SaveChangesAsync();

        var response = await fixture.Service.RunAssessmentAsync("session-a", default);

        AssertNoScore(response, code);
        var stored = await fixture.Db.SpeakingSimulationV11Assessments.AsNoTracking().SingleAsync();
        Assert.Equal(response.AssessmentId, stored.Id);
        Assert.NotNull(stored.ReportJson);
        using var json = JsonDocument.Parse(stored.ReportJson!);
        Assert.False(string.IsNullOrWhiteSpace(json.RootElement.GetProperty("message").GetString()));
        var retrieved = await fixture.Service.GetLatestAsync("session-a", default);
        Assert.NotNull(retrieved);
        AssertNoScore(retrieved!, code);
        Assert.Equal(0, fixture.Gateway.Calls);
    }

    [Theory]
    [InlineData(false, "not-json")]
    [InlineData(true, "not-json")]
    [InlineData(false, "{\"message\":\"Review required\",\"criteria\":[{\"rawScore\":90,\"scoreBand\":\"A\"}]}")]
    [InlineData(true, "{\"message\":\"Review required\",\"criteria\":[{\"rawScore\":90,\"scoreBand\":\"A\"}]}")]
    public async Task Technical_retrieval_never_parses_or_returns_untrusted_report_json(bool combined, string reportJson)
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Db.SpeakingSimulationV11Assessments.Add(new SpeakingSimulationV11Assessment
        {
            Id = "technical-report", SpeakingSessionId = combined ? null : "session-a",
            ExamSessionId = "exam-1", AssessmentKind = combined ? "combined" : "card",
            Status = SpeakingSimulationV11AssessmentStatus.TechnicalReview,
            TechnicalReviewCode = "technical_only", ReportJson = reportJson,
        });
        await fixture.Db.SaveChangesAsync();

        var response = combined
            ? await fixture.Service.GetLatestCombinedAsync("exam-1", default)
            : await fixture.Service.GetLatestAsync("session-a", default);

        Assert.NotNull(response);
        AssertNoScore(response!, "technical_only");
        Assert.Equal(0, fixture.Gateway.Calls);
    }

    [Theory]
    [InlineData("estimate")]
    [InlineData("low")]
    [InlineData("high")]
    [InlineData("confidence")]
    public async Task A_technical_status_cannot_expose_stored_numeric_columns(string field)
    {
        await using var fixture = await Fixture.CreateAsync();
        var row = new SpeakingSimulationV11Assessment
        {
            Id = "inconsistent-technical", ExamSessionId = "exam-1", AssessmentKind = "combined",
            Status = SpeakingSimulationV11AssessmentStatus.TechnicalReview,
            TechnicalReviewCode = "technical_only", ReportJson = "{\"message\":\"Review required\"}",
        };
        switch (field)
        {
            case "estimate": row.EstimatedPracticeScore = 350; break;
            case "low": row.ScoreRangeLow = 325; break;
            case "high": row.ScoreRangeHigh = 375; break;
            case "confidence": row.ConfidenceScore = .9m; break;
        }
        fixture.Db.SpeakingSimulationV11Assessments.Add(row);
        await fixture.Db.SaveChangesAsync();

        await AssertLockedAsync(() => fixture.Service.GetLatestCombinedAsync("exam-1", default));
    }

    [Theory]
    [InlineData("", 400, "SPEAKING_SESSION_ID_REQUIRED")]
    [InlineData("missing", 404, "speaking_session_not_found")]
    [InlineData("session-a", 409, "speaking_session_not_finished")]
    public async Task Existing_session_errors_precede_score_availability(string sessionId, int status, string code)
    {
        await using var fixture = await Fixture.CreateAsync();
        (await fixture.Db.SpeakingSessions.SingleAsync(x => x.Id == "session-a")).State = SpeakingSessionState.Active;
        await fixture.Db.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<ApiException>(() => fixture.Service.RunAssessmentAsync(sessionId, default));

        Assert.Equal(status, error.StatusCode);
        Assert.Equal(code, error.ErrorCode);
        Assert.Equal(0, fixture.Gateway.Calls);
    }

    [Fact]
    public async Task Missing_card_error_precedes_score_availability()
    {
        await using var fixture = await Fixture.CreateAsync();
        (await fixture.Db.SpeakingSessions.SingleAsync(x => x.Id == "session-a")).RolePlayCardId = "missing-card";
        await fixture.Db.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<ApiException>(() => fixture.Service.RunAssessmentAsync("session-a", default));

        Assert.Equal(404, error.StatusCode);
        Assert.Equal("role_play_card_not_found", error.ErrorCode);
    }

    [Theory]
    [InlineData("", 400, "SPEAKING_EXAM_ID_REQUIRED")]
    [InlineData("missing", 404, "speaking_exam_not_found")]
    public async Task Existing_exam_errors_precede_score_availability(string examId, int status, string code)
    {
        await using var fixture = await Fixture.CreateAsync();

        var error = await Assert.ThrowsAsync<ApiException>(() => fixture.Service.RunCombinedAssessmentAsync(examId, default));

        Assert.Equal(status, error.StatusCode);
        Assert.Equal(code, error.ErrorCode);
    }

    [Theory]
    [InlineData("speaking_exam_not_completed")]
    [InlineData("human_examiner_required")]
    [InlineData("two_cards_required")]
    [InlineData("card_assessment_invalid")]
    [InlineData("profession_mismatch")]
    public async Task Combined_prerequisite_failures_remain_unscored(string code)
    {
        await using var fixture = await Fixture.CreateAsync();
        var exam = await fixture.Db.SpeakingExamSessions.SingleAsync();
        switch (code)
        {
            case "speaking_exam_not_completed": exam.State = SpeakingExamState.ActiveA; break;
            case "human_examiner_required": exam.Mode = SpeakingExamMode.LiveTutor; break;
            case "two_cards_required": exam.SessionBId = null; break;
            case "profession_mismatch":
                fixture.AddScoredCard("a");
                fixture.AddScoredCard("b");
                exam.ProfessionId = "nursing";
                break;
        }
        await fixture.Db.SaveChangesAsync();

        var response = await fixture.Service.RunCombinedAssessmentAsync("exam-1", default);

        AssertNoScore(response, code);
        Assert.Equal(0, fixture.Gateway.Calls);
        Assert.False(await fixture.Db.SpeakingSimulationV11Assessments.AnyAsync(x => x.AssessmentKind == "combined"));
    }

    [Fact]
    public async Task Absent_reports_preserve_null_retrieval()
    {
        await using var fixture = await Fixture.CreateAsync();

        Assert.Null(await fixture.Service.GetLatestAsync("session-a", default));
        Assert.Null(await fixture.Service.GetLatestCombinedAsync("exam-1", default));
    }

    private static async Task AssertLockedAsync(Func<Task> action)
    {
        var error = await Assert.ThrowsAsync<ApiException>(action);
        Assert.Equal(409, error.StatusCode);
        Assert.Equal("numeric_scoring_validation_required", error.ErrorCode);
    }

    private static void AssertNoScore(SpeakingSimulationV11AssessmentResponse response, string code)
    {
        Assert.Equal(nameof(SpeakingSimulationV11AssessmentStatus.TechnicalReview), response.Status);
        Assert.Equal(code, response.TechnicalReviewCode);
        Assert.Null(response.EstimatedPracticeScore);
        Assert.Null(response.ScoreRangeLow);
        Assert.Null(response.ScoreRangeHigh);
        Assert.Null(response.ConfidenceScore);
        Assert.Null(response.Report);
    }

    private static SpeakingSimulationV11AssessmentReport ScoredReport(string id, string slot)
        => new(
            AssessmentId: id, AssessmentKind: slot == "combined" ? "combined" : "card", CardSlot: slot,
            SpecVersion: SpeakingSimulationV11Contracts.SpecVersion,
            RubricVersion: SpeakingSimulationV11Contracts.RubricVersion,
            CalibrationVersion: SpeakingSimulationV11Contracts.CalibrationVersion,
            GraphDisclaimer: SpeakingSimulationV11Contracts.GraphDisclaimer,
            EstimatedPracticeScore: 350, ScoreRangeLow: 325, ScoreRangeHigh: 375,
            ConfidenceLabel: "high", ConfidenceScore: .9m, OverallSummary: "Stored practice feedback.",
            Criteria: SpeakingSimulationV11Contracts.RubricCriteria.Criteria.Select(c =>
                new SpeakingSimulationV11CriterionResult(c.CriterionCode, c.Label, c.Weight,
                    70m, 70m * c.Weight / 100m, "B", "Stored rationale.", [])).ToArray(),
            CardBreakdowns: [], Strengths: [], Weaknesses: [], TaskMap: [], Timeline: [],
            LanguageAnalysis: new Dictionary<string, object?>(), TimeManagement: new Dictionary<string, object?>(),
            TopFive: [], BetterAlternatives: [], Tips: [], PracticePlan: [],
            SourceTranscriptId: null, SourceRecordingId: null, CardVersion: "card-v1",
            GeneratedAt: DateTimeOffset.UtcNow);

    // Same EF InMemory pattern as SpeakingSimulationV11ReleaseGateTests; no provider or database server is started.
    private sealed class Fixture : IAsyncDisposable
    {
        public LearnerDbContext Db { get; }
        public ForbiddenGateway Gateway { get; } = new();
        public SpeakingSimulationV11ReleaseGate Gate { get; }
        public SpeakingSimulationV11AssessmentService Service { get; }
        private readonly IFileStorage storage = new OetLearner.Api.Tests.InMemoryFileStorage();
        private readonly IPronunciationPhonemeProvider phonemeProvider = new ForbiddenPhonemeProvider();

        private Fixture(LearnerDbContext db)
        {
            Db = db;
            Gate = new(db);
            // These downstream services must never be reached while numeric scoring is locked.
            // They are fully constructed so the test exercises the production composition boundary.
            Service = new(db, Gateway, Gate,
                new SpeakingSimulationV11EvidenceCaptureService(db, storage,
                    NullLogger<SpeakingSimulationV11EvidenceCaptureService>.Instance),
                new SpeakingSimulationV11AudioAssessmentService(db, storage, phonemeProvider,
                    NullLogger<SpeakingSimulationV11AudioAssessmentService>.Instance),
                new SpeakingSimulationV11TurnTelemetryService(db, Gate,
                    NullLogger<SpeakingSimulationV11TurnTelemetryService>.Instance),
                NullLogger<SpeakingSimulationV11AssessmentService>.Instance);
        }

        public static async Task<Fixture> CreateAsync(bool approveAll = false, bool capturePersona = true)
        {
            var options = new DbContextOptionsBuilder<LearnerDbContext>()
                .UseInMemoryDatabase($"speaking-numeric-lock-{Guid.NewGuid():N}").Options;
            var fixture = new Fixture(new LearnerDbContext(options));
            var db = fixture.Db;
            foreach (var slot in new[] { "a", "b" })
            {
                db.RolePlayCards.Add(new RolePlayCard
                {
                    Id = "card-" + slot, ContentItemId = "content-" + slot, ProfessionId = "medicine",
                    ScenarioTitle = "Practice discussion", Setting = "Clinic", CandidateRole = "Doctor",
                });
                db.SpeakingSessions.Add(new SpeakingSession
                {
                    Id = "session-" + slot, UserId = "learner-1", RolePlayCardId = "card-" + slot,
                    ExamSessionId = "exam-1", ExamSlot = slot, Mode = SpeakingSessionMode.AiExam,
                    State = SpeakingSessionState.Finished,
                });
                if (capturePersona)
                    db.SpeakingSimulationV11PersonaRuntimeSnapshots.Add(new SpeakingSimulationV11PersonaRuntimeSnapshot
                    {
                        Id = "persona-" + slot, SpeakingSessionId = "session-" + slot,
                        RolePlayCardId = "card-" + slot, ExamSessionId = "exam-1", CardSlot = slot,
                        ProfessionId = "medicine", SpecVersion = SpeakingSimulationV11Contracts.SpecVersion,
                        CardVersion = "card-v1", MemoryScopeKey = "scope-" + slot,
                    });
            }
            db.SpeakingExamSessions.Add(new SpeakingExamSession
            {
                Id = "exam-1", UserId = "learner-1", ProfessionId = "medicine",
                CardAId = "card-a", CardBId = "card-b", SessionAId = "session-a", SessionBId = "session-b",
                Mode = SpeakingExamMode.Ai, State = SpeakingExamState.Completed,
            });
            db.SpeakingSimulationV11SpecReleases.Add(new SpeakingSimulationV11SpecRelease
            {
                Id = "spec-v1-1", SpecVersion = SpeakingSimulationV11Contracts.SpecVersion,
                ReleaseVersion = "2026-08-11", Status = SpeakingSimulationV11ReleaseStatus.Approved,
            });
            db.SpeakingSimulationV11RubricReleases.Add(new SpeakingSimulationV11RubricRelease
            {
                Id = "rubric-v1-1", RubricVersion = SpeakingSimulationV11Contracts.RubricVersion,
                CalibrationVersion = SpeakingSimulationV11Contracts.CalibrationVersion,
                Status = SpeakingSimulationV11ReleaseStatus.Approved,
                CriteriaJson = JsonSerializer.Serialize(SpeakingSimulationV11Contracts.RubricCriteria.Criteria),
            });
            if (approveAll)
                db.SpeakingSimulationV11OwnerApprovals.AddRange(
                    Approval("calibration_approval"), Approval("concurrency_budget", value: 8m),
                    Approval("cost_ceiling", value: 25m), Approval("latency_sla_ms", value: 1500m),
                    Approval("retention_days", value: 30m), Approval("stt_cost_per_minute", value: .01m),
                    Approval("tts_cost_per_1000_characters", value: .03m),
                    Approval("silence_prompt_threshold_ms", value: 12000m), Approval("retention_approval"),
                    Approval("silence_prompt_approval"), Approval("graph_approval"),
                    Approval("profession_pack_approval", scope: "medicine"),
                    Approval("audio_assessment_approval", evidence: "{\"provider\":\"azure-phoneme\"}"));
            await db.SaveChangesAsync();
            return fixture;
        }

        public void AddScoredCard(string slot)
        {
            var report = ScoredReport("assessment-" + slot, slot);
            Db.SpeakingSimulationV11Assessments.Add(new SpeakingSimulationV11Assessment
            {
                Id = report.AssessmentId, SpeakingSessionId = "session-" + slot,
                RolePlayCardId = "card-" + slot, ExamSessionId = "exam-1", ProfessionId = "medicine",
                AssessmentKind = "card", CardSlot = slot, Status = SpeakingSimulationV11AssessmentStatus.Complete,
                EstimatedPracticeScore = report.EstimatedPracticeScore, ScoreRangeLow = report.ScoreRangeLow,
                ScoreRangeHigh = report.ScoreRangeHigh, ConfidenceScore = report.ConfidenceScore,
                ConfidenceLabel = report.ConfidenceLabel, ReportJson = JsonSerializer.Serialize(report),
            });
            foreach (var criterion in report.Criteria)
                Db.SpeakingSimulationV11CriterionScores.Add(new SpeakingSimulationV11CriterionScore
                {
                    Id = "criterion-" + slot + "-" + criterion.CriterionCode, AssessmentId = report.AssessmentId,
                    CriterionCode = criterion.CriterionCode, Weight = criterion.Weight,
                    RawScore = criterion.RawScore, WeightedScore = criterion.WeightedScore,
                    ScoreBand = criterion.ScoreBand, Rationale = criterion.Rationale,
                });
        }

        private static SpeakingSimulationV11OwnerApproval Approval(
            string key, string scope = "global", decimal? value = null, string? evidence = null)
            => new()
            {
                Id = key + "-" + scope, ApprovalKey = key, ScopeKey = scope,
                SpecVersion = SpeakingSimulationV11Contracts.SpecVersion,
                RubricVersion = SpeakingSimulationV11Contracts.RubricVersion,
                Status = SpeakingSimulationV11ApprovalStatus.Approved, NumericValue = value,
                EvidenceJson = evidence ?? "{}", ApprovedByUserId = "test-owner", ApprovedAt = DateTimeOffset.UtcNow,
            };

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class ForbiddenGateway : IAiGatewayService
    {
        public int Calls { get; private set; }

        public AiGroundedPrompt BuildGroundedPrompt(AiGroundingContext context)
        {
            Calls++;
            throw new InvalidOperationException("Numeric lock must precede prompt construction.");
        }

        public Task<AiGatewayResult> CompleteAsync(AiGatewayRequest request, CancellationToken ct = default)
        {
            Calls++;
            throw new InvalidOperationException("Numeric lock must precede provider invocation.");
        }
    }

    private sealed class ForbiddenPhonemeProvider : IPronunciationPhonemeProvider
    {
        public string Name => "forbidden-test-provider";

        public bool IsConfigured => true;

        public Task<bool> IsConfiguredAsync(CancellationToken ct = default)
            => Task.FromResult(true);

        public Task<AsrResult> AnalyzePhonemesAsync(AsrRequest request, CancellationToken ct)
            => throw new InvalidOperationException("Numeric lock must precede phoneme analysis.");
    }
}
