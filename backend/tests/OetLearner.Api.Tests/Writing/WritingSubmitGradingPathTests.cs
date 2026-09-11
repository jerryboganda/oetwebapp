using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Ai;
using OetLearner.Api.Services.Rulebook;
using OetLearner.Api.Services.Writing;
using OetLearner.Api.Services.Writing.Configuration;
using OetLearner.Api.Services.Writing.Events;

namespace OetLearner.Api.Tests.Writing;

/// <summary>
/// Submit-for-grading path invariants: canonical-input grading, no live
/// Model Answer generation, no runtime document extraction, response-length
/// acceptance, single grading job, and candidate-safe errors.
/// </summary>
public sealed class WritingSubmitGradingPathTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly LearnerDbContext _db;

    public WritingSubmitGradingPathTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _db = new LearnerDbContext(new DbContextOptionsBuilder<LearnerDbContext>().UseSqlite(_connection).Options);
        _db.Database.EnsureCreated();
    }

    public async ValueTask DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task Normal_submit_reuses_pregenerated_exemplar_with_single_provider_call()
    {
        var scenarioId = await SeedGradableScenarioAsync(withPregeneratedAnswer: true);
        var gateway = new QueueGateway([CanonicalCompletion, "{}"]);
        var pipeline = BuildPipeline(gateway, new CountingReservations());

        var submissionId = await pipeline.CreateSubmissionAsync(SampleContext(scenarioId, "reuse-1", NormalLetter), default);
        var outcome = await pipeline.EvaluateAsync(submissionId, default);

        Assert.False(outcome.IdempotentReuse);
        Assert.Equal(1, gateway.Calls);
        var report = await _db.WritingAssessmentReportsV11.AsNoTracking().SingleAsync(r => r.SubmissionId == submissionId);
        var answer = await _db.WritingAssessmentModelAnswers.AsNoTracking().SingleAsync(a => a.ReportId == report.Id);
        Assert.Equal(WritingAssessmentModelAnswerStatus.Ready, answer.Status);
        Assert.Equal("Pregenerated exemplar text.", answer.ModelAnswerText);
        // The saved Model Answer is display-only: the grade derives from the
        // AI rubric over canonical inputs, not from exemplar comparison.
        var grade = await _db.WritingGrades.AsNoTracking().SingleAsync(g => g.SubmissionId == submissionId);
        Assert.Equal(31, grade.RawTotal);
    }

    [Fact]
    public async Task Normal_submit_without_pregenerated_answer_grades_without_generating()
    {
        var scenarioId = await SeedGradableScenarioAsync(withPregeneratedAnswer: false);
        // A second queued completion would serve a live Model Answer
        // generation call: the assertion that exactly ONE provider call
        // happens proves normal Submit never generates a Model Answer.
        var gateway = new QueueGateway([CanonicalCompletion, "{}"]);
        var pipeline = BuildPipeline(gateway, new CountingReservations());

        var submissionId = await pipeline.CreateSubmissionAsync(SampleContext(scenarioId, "nogen-1", NormalLetter), default);
        var outcome = await pipeline.EvaluateAsync(submissionId, default);

        Assert.Equal(1, gateway.Calls);
        Assert.True(await _db.WritingGrades.AnyAsync(g => g.SubmissionId == submissionId));
        var report = await _db.WritingAssessmentReportsV11.AsNoTracking().SingleAsync(r => r.SubmissionId == submissionId);
        var answer = await _db.WritingAssessmentModelAnswers.AsNoTracking().SingleAsync(a => a.ReportId == report.Id);
        Assert.Equal(WritingAssessmentModelAnswerStatus.HeldForReview, answer.Status);
        Assert.Equal("model_answer_not_pregenerated", answer.HoldReason);
        Assert.False(answer.IsCandidateVisible);
    }

    [Fact]
    public async Task Short_one_line_submit_is_accepted_and_graded()
    {
        var scenarioId = await SeedGradableScenarioAsync(withPregeneratedAnswer: false);
        var gateway = new QueueGateway([CanonicalCompletion, "{}"]);
        var pipeline = BuildPipeline(gateway, new CountingReservations());

        var submissionId = await pipeline.CreateSubmissionAsync(
            SampleContext(scenarioId, "short-1", "Dear Dr Green, please review. Yours sincerely, Doctor"), default);
        var outcome = await pipeline.EvaluateAsync(submissionId, default);

        Assert.Equal(1, gateway.Calls);
        Assert.True(await _db.WritingGrades.AnyAsync(g => g.SubmissionId == submissionId));
        Assert.False(outcome.IdempotentReuse);
    }

    [Fact]
    public async Task Empty_submit_gets_deterministic_zero_with_no_provider_call_and_no_credit_hold()
    {
        var scenarioId = await SeedGradableScenarioAsync(withPregeneratedAnswer: false);
        var gateway = new QueueGateway([CanonicalCompletion, "{}"]);
        var credits = new CountingReservations();
        var pipeline = BuildPipeline(gateway, credits);

        var submissionId = await pipeline.CreateSubmissionAsync(SampleContext(scenarioId, "empty-1", "   "), default);
        var outcome = await pipeline.EvaluateAsync(submissionId, default);

        var grade = await _db.WritingGrades.AsNoTracking().SingleAsync(g => g.SubmissionId == submissionId);
        Assert.Equal(0, grade.RawTotal);
        Assert.Equal("deterministic-empty-v1", grade.ModelUsed);
        Assert.Equal(0, gateway.Calls);
        Assert.Equal(0, credits.ReserveCalls);
        Assert.False(outcome.IdempotentReuse);
    }

    [Fact]
    public async Task Duplicate_evaluate_resolves_single_grading_job_and_single_credit_hold()
    {
        var scenarioId = await SeedGradableScenarioAsync(withPregeneratedAnswer: false);
        var gateway = new QueueGateway([CanonicalCompletion, "{}"]);
        var credits = new CountingReservations();
        var pipeline = BuildPipeline(gateway, credits);

        var submissionId = await pipeline.CreateSubmissionAsync(SampleContext(scenarioId, "dup-1", NormalLetter), default);
        var first = await pipeline.EvaluateAsync(submissionId, default);
        var second = await pipeline.EvaluateAsync(submissionId, default);

        Assert.Equal(first.GradeId, second.GradeId);
        Assert.True(second.IdempotentReuse);
        Assert.Equal(1, gateway.Calls);
        Assert.Equal(1, credits.ReserveCalls);
        Assert.Equal(1, credits.CommitCalls);
        Assert.Equal(0, credits.ReleaseCalls);
        Assert.Equal(1, await _db.WritingGrades.CountAsync(g => g.SubmissionId == submissionId));
        Assert.Equal(1, await _db.WritingAssessmentReportsV11.CountAsync(r => r.SubmissionId == submissionId));
    }

    [Fact]
    public async Task Release_blocked_submit_returns_candidate_safe_error_without_internal_codes()
    {
        // Scenario with canonical inputs but no released pack: the learner
        // must see controlled copy, never the internal configuration code.
        var scenarioId = Guid.NewGuid();
        _db.WritingScenarios.Add(new WritingScenario
        {
            Id = scenarioId,
            Title = "Unreleased task",
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
            ScenarioId = scenarioId,
            Ordinal = 1,
            SentenceText = "Asthma; allergy status negative.",
            RelevanceLabel = "relevant",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync();

        var pipeline = BuildPipeline(
            new QueueGateway([CanonicalCompletion, "{}"]), new CountingReservations());
        var submissionId = await pipeline.CreateSubmissionAsync(SampleContext(scenarioId, "blocked-1", NormalLetter), default);

        var ex = await Assert.ThrowsAsync<ApiException>(() => pipeline.EvaluateAsync(submissionId, default));
        Assert.Equal("writing_assessment_release_blocked", ex.ErrorCode);
        Assert.DoesNotContain("profession_pack_not_approved", ex.Message);
        Assert.DoesNotContain("letter_type_pack_not_approved", ex.Message);
    }

    [Fact]
    public async Task Prose_wrapped_contract_grades_without_fabrication()
    {
        // The model sometimes wraps the contract in analysis prose. The
        // parser must find the complete contract inside it — never fail a
        // valid grading, never fabricate one.
        var scenarioId = await SeedGradableScenarioAsync(withPregeneratedAnswer: false);
        var wrapped = "Here is my assessment of the letter:\n"
            + CanonicalCompletion
            + "\nI hope this detailed analysis helps the candidate improve.";
        var gateway = new QueueGateway([wrapped, "{}"]);
        var pipeline = BuildPipeline(gateway, new CountingReservations());

        var submissionId = await pipeline.CreateSubmissionAsync(SampleContext(scenarioId, "wrapped-1", NormalLetter), default);
        var outcome = await pipeline.EvaluateAsync(submissionId, default);

        Assert.False(outcome.IdempotentReuse);
        var grade = await _db.WritingGrades.AsNoTracking().SingleAsync(g => g.SubmissionId == submissionId);
        Assert.Equal(31, grade.RawTotal);
        Assert.Equal("B", grade.BandLabel);
    }

    [Fact]
    public void ExtractJsonObjectSpans_FindsBalancedSpan_AroundProse()
    {
        var spans = WritingSubmissionEvaluationPipeline.ExtractJsonObjectSpans(
            "noise {\"a\":1} middle {\"b\":{\"c\":2}} tail");

        Assert.Equal(2, spans.Count);
        Assert.Contains("{\"b\":{\"c\":2}}", spans);
    }

    [Fact]
    public void ExtractJsonObjectSpans_IgnoresBraces_InsideStrings_And_Truncation()
    {
        var spans = WritingSubmissionEvaluationPipeline.ExtractJsonObjectSpans(
            "{\"quote\":\"a } b { c\"} trailing {\"truncated\":");

        Assert.Single(spans);
        Assert.Equal("{\"quote\":\"a } b { c\"}", spans[0]);
    }

    [Fact]
    public void DescribeCompletion_Summarizes_Without_Dumping_Body()
    {
        var summary = WritingSubmissionEvaluationPipeline.DescribeCompletion(new string('x', 500));

        Assert.StartsWith("len=500 ", summary);
        Assert.True(summary.Length < 600);
    }

    [Fact]
    public void ControlChars_Inside_Finding_Quotes_Still_Parse()
    {
        var contract = CanonicalCompletion.Replace(
            "\"findings\": []",
            "\"findings\": [{ \"ruleId\": \"R03.4\", \"severity\": \"critical\", \"quote\": \"line one\nline two\ttabbed\", \"message\": \"m\", \"fixSuggestion\": \"f\", \"criterionCode\": \"content\" }]");
        var spans = WritingSubmissionEvaluationPipeline.ExtractJsonObjectSpans(contract);

        Assert.Single(spans);
        var escaped = WritingSubmissionEvaluationPipeline.EscapeControlCharsInStrings(spans[0]);
        Assert.Contains("\\n", escaped);
        Assert.Contains("\\t", escaped);
    }

    [Fact]
    public async Task Multiline_Finding_Quotes_Grade_Without_Fabrication()
    {
        // Finding quotes with literal newlines/tabs must not sink the whole
        // grading: the parser escapes them and scores the complete contract.
        var scenarioId = await SeedGradableScenarioAsync(withPregeneratedAnswer: false);
        var multiline = CanonicalCompletion.Replace(
            "\"findings\": []",
            "\"findings\": [{ \"ruleId\": \"R03.4\", \"severity\": \"critical\", \"quote\": \"first line\nsecond line\", \"message\": \"m\", \"fixSuggestion\": \"f\", \"criterionCode\": \"content\" }]");
        var gateway = new QueueGateway([multiline, "{}"]);
        var pipeline = BuildPipeline(gateway, new CountingReservations());

        var submissionId = await pipeline.CreateSubmissionAsync(SampleContext(scenarioId, "multiline-1", NormalLetter), default);
        var outcome = await pipeline.EvaluateAsync(submissionId, default);

        Assert.False(outcome.IdempotentReuse);
        var grade = await _db.WritingGrades.AsNoTracking().SingleAsync(g => g.SubmissionId == submissionId);
        Assert.Equal(31, grade.RawTotal);
    }

    [Fact]
    public async Task Normal_submit_with_no_release_gate_configured_is_candidate_ready_by_default()
    {
        // 2026-09-09 owner decision (Dr Ahmed Hesham, FINAL OWNER DECISION —
        // WRITING AI SCORE RELEASE): Writing AI grading is fully automated;
        // production has zero WritingAssessmentReleaseGates rows and none
        // should be required for a successful grading to reach the
        // candidate. See WritingCalibrationReleaseService.ResolveAsync.
        var scenarioId = await SeedGradableScenarioAsync(withPregeneratedAnswer: true);
        var gateway = new QueueGateway([CanonicalCompletion, "{}"]);
        var pipeline = BuildPipeline(gateway, new CountingReservations());

        var submissionId = await pipeline.CreateSubmissionAsync(
            SampleContext(scenarioId, "release-default-1", NormalLetter), default);
        await pipeline.EvaluateAsync(submissionId, default);

        var report = await _db.WritingAssessmentReportsV11.AsNoTracking().SingleAsync(r => r.SubmissionId == submissionId);
        Assert.Equal(WritingAssessmentV11Status.CandidateReady, report.Status);
        Assert.True(report.CandidateNumericScoreEnabled);
        Assert.True(report.CandidateReportVisible);
        Assert.Equal(380, report.EstimatedPracticeScore);
    }

    [Fact]
    public async Task Normal_submit_stays_restricted_when_admin_explicitly_blocks_the_model()
    {
        // The release-gate table remains a real admin kill switch: an
        // explicit Blocked row for this exact model+calibration-set pairing
        // still suppresses candidate release, independent of calibration.
        var scenarioId = await SeedGradableScenarioAsync(withPregeneratedAnswer: true);
        _db.WritingAssessmentReleaseGates.Add(new WritingAssessmentReleaseGate
        {
            Id = Guid.NewGuid(),
            ModelVersion = "claude-sonnet-5",
            CalibrationSetVersion = "unreleased",
            Status = WritingAssessmentReleaseStatus.Blocked,
        });
        await _db.SaveChangesAsync();
        var gateway = new QueueGateway([CanonicalCompletion, "{}"]);
        var pipeline = BuildPipeline(gateway, new CountingReservations());

        var submissionId = await pipeline.CreateSubmissionAsync(
            SampleContext(scenarioId, "release-blocked-1", NormalLetter), default);
        await pipeline.EvaluateAsync(submissionId, default);

        var report = await _db.WritingAssessmentReportsV11.AsNoTracking().SingleAsync(r => r.SubmissionId == submissionId);
        Assert.Equal(WritingAssessmentV11Status.RestrictedCalibration, report.Status);
        Assert.False(report.CandidateNumericScoreEnabled);
        Assert.False(report.CandidateReportVisible);
    }

    private async Task<Guid> SeedGradableScenarioAsync(bool withPregeneratedAnswer)
    {
        var scenarioId = Guid.NewGuid();
        _db.WritingScenarios.Add(new WritingScenario
        {
            Id = scenarioId,
            Title = "Gradable task",
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
            ScenarioId = scenarioId,
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
        if (withPregeneratedAnswer)
        {
            _db.WritingTaskModelAnswers.Add(new WritingTaskModelAnswer
            {
                Id = Guid.NewGuid(),
                ScenarioId = scenarioId,
                Status = WritingAssessmentModelAnswerStatus.Ready,
                IsCandidateVisible = true,
                ModelAnswerText = "Pregenerated exemplar text.",
                ValidatorVersion = WritingRuleEngine.ValidatorVersion,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
        }

        await _db.SaveChangesAsync();
        return scenarioId;
    }

    private const string NormalLetter =
        "Dear Dr Green,\n\nI am writing to request a review of Mrs Smith, aged 54, who has asthma with a negative allergy status. " +
        "She remains symptomatic despite treatment and would benefit from your assessment. Thank you.\n\nYours sincerely,\nDoctor";

    private const string CanonicalCompletion = """
        {
          "findings": [],
          "criteriaScores": { "purpose": 3, "content": 6, "conciseness_clarity": 5, "genre_style": 7, "organisation_layout": 4, "language": 6 },
          "estimatedScaledScore": 380,
          "estimatedGrade": "B"
        }
        """;

    private static WritingSubmissionGradeContext SampleContext(Guid scenarioId, string key, string letter)
        => new(
            UserId: "learner-1",
            ScenarioId: scenarioId,
            Mode: "practice",
            GradingTier: "express",
            InputSource: "typed",
            LetterContent: letter,
            TimeSpentSeconds: 120,
            StartedAt: DateTimeOffset.UtcNow.AddMinutes(-5),
            IsRevision: false,
            OriginalSubmissionId: null,
            IdempotencyKey: key);

    private WritingSubmissionEvaluationPipeline BuildPipeline(
        QueueGateway gateway, CountingReservations credits)
        => new(
            _db,
            gateway,
            new EmptyCanonEngine(),
            mistakeService: null!,
            events: new NoopWritingEventBus(),
            TimeProvider.System,
            TestRuntimeSettingsProvider.FromWritingOptions(new WritingV2Options()),
            NullLogger<WritingSubmissionEvaluationPipeline>.Instance,
            assessmentPreflight: new WritingAssessmentPreflightService(_db),
            assessmentRuleEngine: new WritingAssessmentV11RuleEngine(new WritingRuleEngine(new RulebookLoader())),
            calibrationReleaseService: new WritingCalibrationReleaseService(_db),
            // No Model Answer generator is wired by design: if the Submit
            // path ever regains a live exemplar-generation call, the second
            // queued completion is consumed and the single-call assertions
            // below fail loudly.
            creditReservations: credits);

    /// <summary>
    /// Serves one queued completion per provider call and counts calls, so
    /// tests prove exactly how many physical AI calls a Submit performs.
    /// </summary>
    private sealed class QueueGateway(IReadOnlyList<string> completions) : IAiGatewayService
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
            var completion = Calls <= completions.Count ? completions[Calls - 1] : "{}";
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
                "res-1", operationId, "writing", 1, AiCreditReservationState.Reserved, ReserveCalls > 1));
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

    private sealed class EmptyCanonEngine : IWritingCanonEngine
    {
        public Task<WritingCanonDetectionResult> DetectViolationsAsync(WritingCanonDetectionRequest request, CancellationToken ct)
            => Task.FromResult(new WritingCanonDetectionResult(request.SubmissionId, Array.Empty<WritingCanonViolation>()));

        public Task<WritingCanonRuleTestResponse?> TestRuleAsync(string adminUserId, string ruleId, WritingCanonRuleTestRequest request, CancellationToken ct)
            => throw new NotImplementedException();
    }

    private sealed class NoopWritingEventBus : IWritingEventBus
    {
        public Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct = default)
            where TEvent : WritingEvent
            => Task.CompletedTask;
    }
}
