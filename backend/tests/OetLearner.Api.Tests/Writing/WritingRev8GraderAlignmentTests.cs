using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Rulebook;
using OetLearner.Api.Services.Writing;
using OetLearner.Api.Services.Writing.Configuration;
using OetLearner.Api.Services.Writing.Events;

namespace OetLearner.Api.Tests.Writing;

/// <summary>
/// Writing Rule Enforcement &amp; Model Answer Validation Addendum Rev8 (11 Sep
/// 2026) — candidate grader alignment and the candidate-facing Model Answer:
/// the grounded prompt gets the rulebook letter-type token, only a Model Answer
/// verified under the running validator is copied or shown, result pages show
/// the LIVE verified answer (never a stale snapshot), a reused grade still
/// yields a report, AI finding quotes are persisted, and the rubric input
/// never carries Model Answer text.
/// </summary>
public sealed class WritingRev8GraderAlignmentTests : IAsyncDisposable
{
    private const string OldValidatorVersion = "writing-rules.rev7.2026-09-10.1";

    private readonly SqliteConnection _connection;
    private readonly LearnerDbContext _db;

    public WritingRev8GraderAlignmentTests()
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

    // D1: LT-NM used to become "non_medical", which matches no rulebook
    // appliesTo token, so the R15 non-medical rules never reached the grader.
    [Theory]
    [InlineData("LT-NM", "non_medical_referral")]
    [InlineData("LT-UR", "urgent_referral")]
    [InlineData("LT-TR", "transfer_letter")]
    [InlineData("LT-OT", "other_letters")]
    public async Task Grounded_prompt_receives_the_rulebook_letter_type_token(string catalogueLetterType, string expectedToken)
    {
        var scenarioId = await SeedScenarioAsync(catalogueLetterType);
        var gateway = new RecordingGateway(CanonicalCompletion);
        var pipeline = PassThroughPipeline(gateway);

        var submissionId = await pipeline.CreateSubmissionAsync(Context(scenarioId, "lt-1", NormalLetter), default);
        await pipeline.EvaluateAsync(submissionId, default);

        Assert.Equal(expectedToken, gateway.LastLetterType);
    }

    // D6: the AI finding quote was parsed but never persisted.
    [Fact]
    public async Task Ai_finding_quote_is_persisted_in_per_criterion_feedback()
    {
        var scenarioId = await SeedScenarioAsync("LT-RR");
        var completion = CanonicalCompletion.Replace(
            "\"findings\": []",
            "\"findings\": [{ \"ruleId\": \"R03.4\", \"severity\": \"major\", \"quote\": \"no allergy line\", \"message\": \"State the allergy.\", \"fixSuggestion\": \"Add the allergy.\", \"criterionCode\": \"content\" }]");
        var pipeline = PassThroughPipeline(new RecordingGateway(completion));

        var submissionId = await pipeline.CreateSubmissionAsync(Context(scenarioId, "quote-1", NormalLetter), default);
        await pipeline.EvaluateAsync(submissionId, default);

        var grade = await _db.WritingGrades.AsNoTracking().SingleAsync(g => g.SubmissionId == submissionId);
        using var doc = JsonDocument.Parse(grade.PerCriterionFeedbackJson);
        var c2 = doc.RootElement.GetProperty("c2");
        Assert.Equal("no allergy line", c2.GetProperty("quote").GetString());
        Assert.Contains("no allergy line", c2.GetProperty("quotes").EnumerateArray().Select(e => e.GetString()));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(OldValidatorVersion)]
    public async Task Unverified_task_model_answer_is_not_copied_or_shown(string? validatorVersion)
    {
        var scenarioId = await SeedGradableScenarioAsync("Unverified exemplar text.", validatorVersion);
        var pipeline = FullPipeline(new RecordingGateway(CanonicalCompletion));

        var submissionId = await pipeline.CreateSubmissionAsync(Context(scenarioId, "unverified-1", NormalLetter), default);
        await pipeline.EvaluateAsync(submissionId, default);

        var report = await _db.WritingAssessmentReportsV11.AsNoTracking().SingleAsync(r => r.SubmissionId == submissionId);
        var snapshot = await _db.WritingAssessmentModelAnswers.AsNoTracking().SingleAsync(a => a.ReportId == report.Id);
        Assert.Equal(WritingAssessmentModelAnswerStatus.HeldForReview, snapshot.Status);
        Assert.Equal("model_answer_not_verified", snapshot.HoldReason);
        Assert.False(snapshot.IsCandidateVisible);
        Assert.Null(snapshot.ModelAnswerText);

        var result = await new WritingAssessmentV11ResultService(_db).GetForLearnerAsync("learner-1", submissionId, default);
        Assert.NotNull(result);
        Assert.True(result!.CandidateReportVisible);
        Assert.Null(result.ModelAnswer);
    }

    [Fact]
    public async Task Result_pages_show_the_live_verified_answer_not_the_grading_time_snapshot()
    {
        var (scenarioId, submissionId) = await SeedCandidateReadyResultAsync("Stale snapshot exemplar.");
        _db.WritingTaskModelAnswers.Add(TaskAnswer(scenarioId, "Repaired, revalidated exemplar.", WritingRuleEngine.ValidatorVersion));
        await _db.SaveChangesAsync();

        var result = await new WritingAssessmentV11ResultService(_db).GetForLearnerAsync("learner-1", submissionId, default);
        var feedback = await new WritingResultFeedbackService(
                _db,
                new WritingResultVisibilityService(_db, TimeProvider.System, NullLogger<WritingResultVisibilityService>.Instance),
                NullLogger<WritingResultFeedbackService>.Instance)
            .GetFeedbackAsync("learner-1", submissionId, default);

        Assert.Equal("Repaired, revalidated exemplar.", result?.ModelAnswer?.ModelAnswerText);
        Assert.Equal("Repaired, revalidated exemplar.", feedback.AssessmentV11?.ModelAnswer?.ModelAnswerText);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(OldValidatorVersion)]
    public async Task Result_page_never_falls_back_to_a_ready_snapshot_when_the_task_answer_is_unverified(string? validatorVersion)
    {
        var (scenarioId, submissionId) = await SeedCandidateReadyResultAsync("Stale snapshot exemplar.");
        _db.WritingTaskModelAnswers.Add(TaskAnswer(scenarioId, "Invalidated exemplar.", validatorVersion));
        await _db.SaveChangesAsync();

        var result = await new WritingAssessmentV11ResultService(_db).GetForLearnerAsync("learner-1", submissionId, default);

        Assert.NotNull(result);
        Assert.Null(result!.ModelAnswer);
    }

    // D2: a reused grade used to create no v1.1 report, so both candidate
    // result endpoints 404'd for the reusing submission.
    [Fact]
    public async Task Reused_grade_gives_the_new_submission_its_own_report_and_model_answer()
    {
        var scenarioId = await SeedGradableScenarioAsync("Verified exemplar text.", WritingRuleEngine.ValidatorVersion);
        var gateway = new RecordingGateway(CanonicalCompletion);
        var pipeline = FullPipeline(gateway);
        var firstId = await pipeline.CreateSubmissionAsync(Context(scenarioId, "reuse-1", NormalLetter), default);
        await pipeline.EvaluateAsync(firstId, default);

        // Same learner, task and letter content under a different key: the
        // content-dedupe window would collapse a second CreateSubmissionAsync,
        // so the reusing attempt is seeded directly.
        var first = await _db.WritingSubmissions.AsNoTracking().SingleAsync(s => s.Id == firstId);
        var secondId = Guid.NewGuid();
        _db.WritingSubmissions.Add(new WritingSubmission
        {
            Id = secondId,
            UserId = first.UserId,
            ScenarioId = first.ScenarioId,
            Mode = first.Mode,
            LetterContent = first.LetterContent,
            LetterContentHash = first.LetterContentHash,
            WordCount = first.WordCount,
            Status = WritingSubmissionStatuses.Queued,
            GradingTier = first.GradingTier,
            InputSource = first.InputSource,
            StartedAt = DateTimeOffset.UtcNow,
            SubmittedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
            IdempotencyKey = "reuse-2",
        });
        await _db.SaveChangesAsync();

        var outcome = await pipeline.EvaluateAsync(secondId, default);

        Assert.True(outcome.IdempotentReuse);
        Assert.Equal(1, gateway.Calls);
        var firstReport = await _db.WritingAssessmentReportsV11.AsNoTracking()
            .Include(r => r.Errors).Include(r => r.Criteria)
            .SingleAsync(r => r.SubmissionId == firstId);
        var secondReport = await _db.WritingAssessmentReportsV11.AsNoTracking()
            .Include(r => r.Errors).Include(r => r.Criteria)
            .SingleAsync(r => r.SubmissionId == secondId);
        Assert.NotEqual(firstReport.Id, secondReport.Id);
        Assert.Equal(firstReport.EstimatedPracticeScore, secondReport.EstimatedPracticeScore);
        Assert.Equal(firstReport.Errors.Count, secondReport.Errors.Count);
        Assert.Equal(6, secondReport.Criteria.Count);
        var snapshot = await _db.WritingAssessmentModelAnswers.AsNoTracking().SingleAsync(a => a.ReportId == secondReport.Id);
        Assert.Equal(WritingAssessmentModelAnswerStatus.Ready, snapshot.Status);

        var result = await new WritingAssessmentV11ResultService(_db).GetForLearnerAsync("learner-1", secondId, default);
        Assert.NotNull(result);
        Assert.Equal("Verified exemplar text.", result!.ModelAnswer?.ModelAnswerText);
    }

    // Rev8 §7 / OWN-W-038: the grader receives the owner rules, and the
    // verified Model Answer (copied for display) never enters the rubric input.
    [Fact]
    public async Task Rubric_input_carries_owner_rules_but_never_model_answer_text()
    {
        const string canary = "CANARY-REV8-MODEL-ANSWER-QUOKKA-17";
        var scenarioId = await SeedGradableScenarioAsync(
            $"Dear Dr Green, {canary} kindly review Mrs Smith. Yours sincerely, Doctor", WritingRuleEngine.ValidatorVersion);
        var gateway = new RecordingGateway(CanonicalCompletion);
        var pipeline = FullPipeline(gateway);

        var submissionId = await pipeline.CreateSubmissionAsync(Context(scenarioId, "canary-1", NormalLetter), default);
        await pipeline.EvaluateAsync(submissionId, default);

        var report = await _db.WritingAssessmentReportsV11.AsNoTracking().SingleAsync(r => r.SubmissionId == submissionId);
        var snapshot = await _db.WritingAssessmentModelAnswers.AsNoTracking().SingleAsync(a => a.ReportId == report.Id);
        Assert.Contains(canary, snapshot.ModelAnswerText);
        Assert.NotNull(gateway.LastUserInput);
        Assert.DoesNotContain(canary, gateway.LastUserInput);
        Assert.Contains(WritingRev8HouseStyle.CandidateGradingRules, gateway.LastUserInput);
    }

    // -----------------------------------------------------------------
    // Harness
    // -----------------------------------------------------------------

    private async Task<Guid> SeedScenarioAsync(string letterType)
    {
        var scenarioId = Guid.NewGuid();
        _db.WritingScenarios.Add(new WritingScenario
        {
            Id = scenarioId,
            Title = "Rev8 task",
            Profession = "medicine",
            LetterType = letterType,
            TaskPromptMarkdown = "Write to Dr Green requesting a review.",
            Status = "published",
            AuthorId = "admin-1",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync();
        return scenarioId;
    }

    /// <summary>Canonical inputs + a released pack, so the real preflight scores it.</summary>
    private async Task<Guid> SeedGradableScenarioAsync(string modelAnswerText, string? validatorVersion)
    {
        var scenarioId = await SeedScenarioAsync("LT-RR");
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
        _db.WritingTaskModelAnswers.Add(TaskAnswer(scenarioId, modelAnswerText, validatorVersion));
        await _db.SaveChangesAsync();
        return scenarioId;
    }

    private static WritingTaskModelAnswer TaskAnswer(Guid scenarioId, string text, string? validatorVersion) => new()
    {
        Id = Guid.NewGuid(),
        ScenarioId = scenarioId,
        Status = WritingAssessmentModelAnswerStatus.Ready,
        IsCandidateVisible = true,
        ModelAnswerText = text,
        ValidatorVersion = validatorVersion,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
    };

    /// <summary>A released result whose grading-time snapshot is Ready + visible.</summary>
    private async Task<(Guid ScenarioId, Guid SubmissionId)> SeedCandidateReadyResultAsync(string snapshotText)
    {
        var scenarioId = await SeedScenarioAsync("LT-RR");
        var submissionId = Guid.NewGuid();
        var reportId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        _db.WritingSubmissions.Add(new WritingSubmission
        {
            Id = submissionId,
            UserId = "learner-1",
            ScenarioId = scenarioId,
            Mode = "practice",
            LetterContent = NormalLetter,
            LetterContentHash = $"hash-{submissionId:N}",
            WordCount = 50,
            Status = WritingSubmissionStatuses.Graded,
            GradingTier = "express",
            InputSource = "typed",
            StartedAt = now,
            SubmittedAt = now,
            CreatedAt = now,
        });
        _db.WritingAssessmentReportsV11.Add(new WritingAssessmentReportV11
        {
            Id = reportId,
            SubmissionId = submissionId,
            Status = WritingAssessmentV11Status.CandidateReady,
            Profession = "medicine",
            LetterType = "routine_referral",
            EstimatedPracticeScore = 380,
            CandidateNumericScoreEnabled = true,
            CandidateReportVisible = true,
            CreatedAt = now,
            UpdatedAt = now,
        });
        _db.WritingAssessmentModelAnswers.Add(new WritingAssessmentModelAnswer
        {
            Id = Guid.NewGuid(),
            ReportId = reportId,
            Status = WritingAssessmentModelAnswerStatus.Ready,
            IsCandidateVisible = true,
            ModelAnswerText = snapshotText,
            CreatedAt = now,
            UpdatedAt = now,
        });
        await _db.SaveChangesAsync();
        return (scenarioId, submissionId);
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

    private static WritingSubmissionGradeContext Context(Guid scenarioId, string key, string letter)
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

    /// <summary>Real preflight, deterministic rule engine and release service.</summary>
    private WritingSubmissionEvaluationPipeline FullPipeline(RecordingGateway gateway)
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
            calibrationReleaseService: new WritingCalibrationReleaseService(_db));

    /// <summary>Scores any task (no pack/case-note setup); no v1.1 report.</summary>
    private WritingSubmissionEvaluationPipeline PassThroughPipeline(RecordingGateway gateway)
        => new(
            _db,
            gateway,
            new EmptyCanonEngine(),
            mistakeService: null!,
            events: new NoopWritingEventBus(),
            TimeProvider.System,
            TestRuntimeSettingsProvider.FromWritingOptions(new WritingV2Options()),
            NullLogger<WritingSubmissionEvaluationPipeline>.Instance,
            assessmentPreflight: new PassThroughPreflight());

    /// <summary>Records the grounding letter type and rubric input; counts provider calls.</summary>
    private sealed class RecordingGateway(string completion) : IAiGatewayService
    {
        public int Calls { get; private set; }
        public string? LastLetterType { get; private set; }
        public string? LastUserInput { get; private set; }

        public AiGroundedPrompt BuildGroundedPrompt(AiGroundingContext context)
        {
            LastLetterType = context.LetterType;
            return new AiGroundedPrompt
            {
                SystemPrompt = "# OET AI — Rulebook-Grounded System Prompt\n**This call concerns WRITING**",
                TaskInstruction = "score",
            };
        }

        public Task<AiGatewayResult> CompleteAsync(AiGatewayRequest request, CancellationToken ct = default)
        {
            Calls++;
            LastUserInput = request.UserInput;
            return Task.FromResult(new AiGatewayResult { Completion = completion, ResolvedModel = "claude-sonnet-5" });
        }
    }

    private sealed class PassThroughPreflight : IWritingAssessmentPreflightService
    {
        public Task<WritingAssessmentPreflightResult> ValidateAsync(WritingSubmission submission, CancellationToken ct)
            => Task.FromResult(new WritingAssessmentPreflightResult(
                CanScore: true,
                Status: WritingAssessmentV11Status.CandidateReady,
                MissingInputCodes: Array.Empty<string>(),
                ReleaseBlockCodes: Array.Empty<string>(),
                AppliedRulePacks: Array.Empty<string>(),
                Profession: "medicine",
                LetterType: "routine_referral",
                RulePackVersion: "test",
                TaskSnapshot: "Refer the patient.",
                CaseNotesSnapshot: "Patient name: Jane Smith\nAge: 54"));
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
