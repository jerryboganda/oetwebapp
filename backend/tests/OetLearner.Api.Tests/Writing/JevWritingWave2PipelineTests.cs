using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Ai.TypeSafe;
using OetLearner.Api.Services.Rulebook;
using OetLearner.Api.Services.Writing;
using OetLearner.Api.Services.Writing.Configuration;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests.Writing;

/// <summary>
/// Wave 2 Jev hooks driven through the REAL <see cref="WritingSubmissionEvaluationPipeline"/>: a guard
/// Block no longer skips the paid grade unless enforced, the outcome / criteria / findings cross-checks
/// only ever flag tutor review (one assignment, ConfidenceFlag <c>jev_review</c>) and never change a
/// stored score, every flag off means zero Jev calls, and a Jev outage or crash changes nothing.
/// </summary>
public sealed class JevWritingWave2PipelineTests
{
    private static readonly Guid ScenarioId = Guid.Parse("bbbbbbbb-cccc-dddd-eeee-ffffffffffff");

    private const string LetterText =
        "Dear Dr Green,\n\nRe: Mr Adam Lee, DOB 3 May 1970\n\nI am writing to refer Mr Lee for review of his chest pain.\n\nYours sincerely,\nDoctor";

    // Heuristic puts the first finding under organisation_layout ("address"); the second carries
    // its own criterionCode so Jev must never be asked about it.
    private const string TwoFindings = """
        [
          { "severity": "major", "quote": "untidy address block", "message": "Layout of the address block is untidy.", "fixSuggestion": "Put each address line on its own line." },
          { "severity": "minor", "quote": "the paragraphs", "message": "Paragraph order is confusing.", "criterionCode": "organisation_layout" }
        ]
        """;

    private const string InferredFindingMessage = "Layout of the address block is untidy.";
    private const string CodedFindingMessage = "Paragraph order is confusing.";

    // Grader criterion scores: 3 + 6 + 5 + 7 + 4 + 6 = 31.
    private static string Completion(int scaled = 380, string findings = "[]") => $$"""
        {
          "findings": {{findings}},
          "criteriaScores": { "purpose": 3, "content": 6, "conciseness_clarity": 5, "genre_style": 7, "organisation_layout": 4, "language": 6 },
          "estimatedScaledScore": {{scaled}},
          "estimatedGrade": "B"
        }
        """;

    private static readonly Dictionary<string, double> AgreeingPositions = new()
    {
        ["c1_purpose"] = 3, ["c2_content"] = 6, ["c3_conciseness"] = 5,
        ["c4_genre"] = 7, ["c5_organisation"] = 4, ["c6_language"] = 6,
    };

    // ── Guard: not enforced vs enforced ─────────────────────────────────────

    [Fact]
    public async Task GuardBlock_NotEnforced_StillGradesThroughTheGrader_AndFlagsTutorReview()
    {
        await using var db = NewDb();
        var judgments = new ScriptedJudgments();
        judgments.Handlers[AiFeatureCodes.JevWritingGuard] = BlockingGuard;
        var (pipeline, gateway) = BuildPipeline(db, judgments, Flags(o => o.WritingGuardEnabled = true), Completion());
        var id = await SeedSubmissionAsync(db);

        var outcome = await pipeline.EvaluateAsync(id, default);

        Assert.Equal(1, gateway.Calls);                  // the paid grade ran
        Assert.Equal(31, outcome.RawTotal);              // and its numbers are untouched
        Assert.Equal("B", outcome.BandLabel);
        Assert.Equal(WritingSubmissionStatuses.Graded, await StatusAsync(db, id));
        var grade = await GradeAsync(db, id);
        Assert.Equal(JevWritingPilot.TutorReviewConfidenceFlag, grade.ConfidenceFlag);
        var assignment = Assert.Single(await AssignmentsAsync(db, id));
        Assert.Equal("pending", assignment.Status);
        Assert.Equal(string.Empty, assignment.TutorId);
    }

    [Fact]
    public async Task GuardBlock_Enforced_SkipsThePaidGrade_ExactlyAsBefore()
    {
        await using var db = NewDb();
        var judgments = new ScriptedJudgments();
        judgments.Handlers[AiFeatureCodes.JevWritingGuard] = BlockingGuard;
        var (pipeline, gateway) = BuildPipeline(
            db, judgments, Flags(o => { o.WritingGuardEnabled = true; o.WritingGuardEnforced = true; }), Completion());
        var id = await SeedSubmissionAsync(db);

        var ex = await Assert.ThrowsAsync<ApiException>(() => pipeline.EvaluateAsync(id, default));

        Assert.Equal("writing_submission_flagged", ex.ErrorCode);
        Assert.Equal(0, gateway.Calls);                  // no paid call
        Assert.Empty(await db.WritingGrades.AsNoTracking().ToListAsync());
        Assert.Equal(WritingSubmissionStatuses.Failed, await StatusAsync(db, id));
        Assert.Single(await AssignmentsAsync(db, id));
    }

    [Fact]
    public async Task GuardBlock_NeverStacksASecondAssignment_WhenOneAlreadyExists()
    {
        await using var db = NewDb();
        var judgments = new ScriptedJudgments();
        judgments.Handlers[AiFeatureCodes.JevWritingGuard] = BlockingGuard;
        var (pipeline, _) = BuildPipeline(db, judgments, Flags(o => o.WritingGuardEnabled = true), Completion());
        var id = await SeedSubmissionAsync(db);
        var existingId = Guid.NewGuid();
        db.WritingTutorReviewAssignments.Add(new WritingTutorReviewAssignment
        {
            Id = existingId,
            SubmissionId = id,
            TutorId = string.Empty,
            ClaimedAt = DateTimeOffset.UtcNow,
            DueAt = DateTimeOffset.UtcNow.AddHours(24),
            Status = "pending",
        });
        await db.SaveChangesAsync();

        await pipeline.EvaluateAsync(id, default);

        var assignment = Assert.Single(await AssignmentsAsync(db, id));
        Assert.Equal(existingId, assignment.Id);
    }

    [Fact]
    public async Task AutoRetryRun_SeesTheGuardBlockAgain_OnItsOwnJevOperation_AndStillFlags()
    {
        await using var db = NewDb();
        var judgments = new ScriptedJudgments();
        judgments.Handlers[AiFeatureCodes.JevWritingGuard] = BlockingGuard;
        var (pipeline, gateway) = BuildPipeline(db, judgments, Flags(o => o.WritingGuardEnabled = true), Completion());
        gateway.FailFirstCalls = 1;
        var id = await SeedSubmissionAsync(db);

        // Run 1: the guard flags, the paid grade then fails and the letter is re-queued.
        await Assert.ThrowsAsync<ApiException>(() => pipeline.EvaluateAsync(id, default));
        Assert.Equal(WritingSubmissionStatuses.Queued, await StatusAsync(db, id));
        Assert.Empty(await AssignmentsAsync(db, id)); // a failed run stages nothing

        // Run 2 (the automatic re-queue) grades.
        var outcome = await pipeline.EvaluateAsync(id, default);

        Assert.Equal(31, outcome.RawTotal);
        // Each run is keyed by its own epoch, so run 2's guard call is never a Duplicate of run 1's
        // (the control plane would answer a same-key repeat with Unavailable and the flag would vanish).
        var guardVersions = judgments.Calls
            .Where(c => c.Feature == AiFeatureCodes.JevWritingGuard)
            .Select(c => c.Version)
            .ToList();
        Assert.Equal(2, guardVersions.Count);
        Assert.All(guardVersions, v => Assert.NotNull(v));
        Assert.Equal(2, guardVersions.Distinct().Count());
        Assert.Equal(JevWritingPilot.TutorReviewConfidenceFlag, (await GradeAsync(db, id)).ConfidenceFlag);
        Assert.Single(await AssignmentsAsync(db, id));
    }

    [Fact]
    public async Task SeveralReasonsInOneRun_StageExactlyOneAssignment()
    {
        await using var db = NewDb();
        var judgments = new ScriptedJudgments();
        judgments.Handlers[AiFeatureCodes.JevWritingGuard] = BlockingGuard;
        judgments.Handlers[AiFeatureCodes.JevWritingVerify] = r => Answer(r, _ => Choice("contradicted", 0.9));
        judgments.Handlers[AiFeatureCodes.JevWritingOutcome] = r => Answer(r, _ => Noul(0.05));
        judgments.Handlers[AiFeatureCodes.JevWritingFindings] = r => Answer(r, q => q.Kind == JevQuestionKind.Choice ? Choice("language", 0.9) : Noul(0.9));
        var (pipeline, _) = BuildPipeline(
            db, judgments, Flags(o =>
            {
                o.WritingGuardEnabled = true;
                o.WritingVerifyEnabled = true;
                o.WritingOutcomeEnabled = true;
                o.WritingFindingsEnabled = true;
            }), Completion(findings: TwoFindings));
        var id = await SeedSubmissionAsync(db);

        await pipeline.EvaluateAsync(id, default);

        Assert.Single(await AssignmentsAsync(db, id));
        Assert.Equal(JevWritingPilot.TutorReviewConfidenceFlag, (await GradeAsync(db, id)).ConfidenceFlag);
    }

    // ── Outcome cross-check ─────────────────────────────────────────────────

    [Theory]
    [InlineData(380, 0.10, true)]   // grader passed, Jev confident it did not
    [InlineData(380, 0.50, false)]
    [InlineData(380, 0.90, false)]  // agrees
    [InlineData(300, 0.90, true)]   // grader failed, Jev confident it passed
    [InlineData(300, 0.50, false)]
    [InlineData(300, 0.10, false)]  // agrees
    public async Task Outcome_FlagsOnlyOnAConfidentDisagreement_AndNeverChangesTheStoredGrade(int scaled, double jevPassProbability, bool expectedFlag)
    {
        await using var db = NewDb();
        var judgments = new ScriptedJudgments();
        judgments.Handlers[AiFeatureCodes.JevWritingOutcome] = r => Answer(r, _ => Noul(jevPassProbability));
        var (pipeline, gateway) = BuildPipeline(db, judgments, Flags(o => o.WritingOutcomeEnabled = true), Completion(scaled));
        var id = await SeedSubmissionAsync(db);

        var outcome = await pipeline.EvaluateAsync(id, default);

        Assert.Equal(1, gateway.Calls);
        Assert.Equal(1, judgments.Calls.Count);
        Assert.Equal(AiFeatureCodes.JevWritingOutcome, judgments.Calls[0].Feature);
        Assert.Equal(31, outcome.RawTotal);
        Assert.Equal(OetScoring.OetGradeLetterFromScaled(scaled), outcome.BandLabel);
        var grade = await GradeAsync(db, id);
        Assert.Equal(expectedFlag ? JevWritingPilot.TutorReviewConfidenceFlag : "high", grade.ConfidenceFlag);
        Assert.Equal(expectedFlag ? 1 : 0, (await AssignmentsAsync(db, id)).Count);
        Assert.Equal(31, grade.RawTotal);
        Assert.Equal(OetScoring.OetGradeLetterFromScaled(scaled), grade.BandLabel);
    }

    // ── Criteria divergence ─────────────────────────────────────────────────

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task CriteriaDivergence_FlagsTutorReview_NeverChangesAScore(bool diverge, bool expectedFlag)
    {
        await using var db = NewDb();
        var judgments = new ScriptedJudgments();
        judgments.Handlers[AiFeatureCodes.JevWritingCriteria] = r => Answer(r, q =>
        {
            var position = AgreeingPositions[q.Id];
            if (diverge && q.Id is "c2_content" or "c6_language") position = 1.5;
            return Score(position, 0.9);
        });
        var (pipeline, _) = BuildPipeline(db, judgments, Flags(o => o.WritingCriteriaEnabled = true), Completion());
        var id = await SeedSubmissionAsync(db);

        var outcome = await pipeline.EvaluateAsync(id, default);

        Assert.Equal(31, outcome.RawTotal);
        var grade = await GradeAsync(db, id);
        Assert.Equal((short)6, grade.C2Content);
        Assert.Equal((short)6, grade.C6Language);
        Assert.Equal(expectedFlag ? JevWritingPilot.TutorReviewConfidenceFlag : "high", grade.ConfidenceFlag);
        Assert.Equal(expectedFlag ? 1 : 0, (await AssignmentsAsync(db, id)).Count);
        // The advisory radar is still merged as display-only data.
        var c2 = JsonDocument.Parse(grade.PerCriterionFeedbackJson).RootElement.GetProperty("c2");
        Assert.Equal(diverge ? 1.5 : 6.0, c2.GetProperty(JevWritingPilot.AdvisoryFieldName).GetDouble());
    }

    // ── Verify (existing behaviour, now routed through the single staging point) ──

    [Fact]
    public async Task VerifyFlag_StillFlagsTutorReview_WithOneAssignment()
    {
        await using var db = NewDb();
        var judgments = new ScriptedJudgments();
        judgments.Handlers[AiFeatureCodes.JevWritingVerify] = r => Answer(r, _ => Choice("contradicted", 0.9));
        var (pipeline, _) = BuildPipeline(db, judgments, Flags(o => o.WritingVerifyEnabled = true), Completion(findings: TwoFindings));
        var id = await SeedSubmissionAsync(db);

        await pipeline.EvaluateAsync(id, default);

        Assert.Equal(JevWritingPilot.TutorReviewConfidenceFlag, (await GradeAsync(db, id)).ConfidenceFlag);
        Assert.Single(await AssignmentsAsync(db, id));
    }

    // ── Finding classification ──────────────────────────────────────────────

    [Fact]
    public async Task FindingClassification_ConfidentJev_MovesOnlyTheHeuristicFinding()
    {
        await using var db = NewDb();
        var judgments = new ScriptedJudgments();
        judgments.Handlers[AiFeatureCodes.JevWritingFindings] =
            r => Answer(r, q => q.Kind == JevQuestionKind.Choice ? Choice("language", 0.9) : Noul(0.1));
        var (pipeline, _) = BuildPipeline(db, judgments, Flags(o => o.WritingFindingsEnabled = true), Completion(findings: TwoFindings));
        var id = await SeedSubmissionAsync(db);

        var outcome = await pipeline.EvaluateAsync(id, default);

        var grade = await GradeAsync(db, id);
        Assert.Contains(InferredFindingMessage, Feedback(grade, "c6"));   // Jev: language
        Assert.DoesNotContain(InferredFindingMessage, Feedback(grade, "c5"));
        Assert.Contains(CodedFindingMessage, Feedback(grade, "c5"));       // grader-assigned: untouched
        // Jev was only asked about the finding without a criterionCode.
        var request = Assert.Single(judgments.Calls).Request;
        Assert.Contains(request.Questions, q => q.Id == "crit_0");
        Assert.DoesNotContain(request.Questions, q => q.Id == "crit_1");
        Assert.Equal(31, outcome.RawTotal);
        Assert.Equal("high", grade.ConfidenceFlag);
        Assert.Empty(await AssignmentsAsync(db, id));
    }

    [Fact]
    public async Task FindingClassification_LowConfidence_KeepsTheKeywordHeuristic()
    {
        await using var db = NewDb();
        var judgments = new ScriptedJudgments();
        judgments.Handlers[AiFeatureCodes.JevWritingFindings] =
            r => Answer(r, q => q.Kind == JevQuestionKind.Choice ? Choice("language", 0.30) : Noul(0.1));
        var (pipeline, _) = BuildPipeline(db, judgments, Flags(o => o.WritingFindingsEnabled = true), Completion(findings: TwoFindings));
        var id = await SeedSubmissionAsync(db);

        await pipeline.EvaluateAsync(id, default);

        var grade = await GradeAsync(db, id);
        Assert.Contains(InferredFindingMessage, Feedback(grade, "c5"));    // heuristic: "address" -> organisation_layout
        Assert.DoesNotContain(InferredFindingMessage, Feedback(grade, "c6"));
    }

    [Fact]
    public async Task ValidAlternativeFinding_FlagsTutorReview_ButNeverRemovesOrDowngradesTheFinding()
    {
        await using var db = NewDb();
        var judgments = new ScriptedJudgments();
        judgments.Handlers[AiFeatureCodes.JevWritingFindings] =
            r => Answer(r, q => q.Kind == JevQuestionKind.Choice ? Choice("organisation_layout", 0.9) : Noul(0.9));
        var (pipeline, _) = BuildPipeline(db, judgments, Flags(o => o.WritingFindingsEnabled = true), Completion(findings: TwoFindings));
        var id = await SeedSubmissionAsync(db);

        var outcome = await pipeline.EvaluateAsync(id, default);

        var grade = await GradeAsync(db, id);
        Assert.Equal(JevWritingPilot.TutorReviewConfidenceFlag, grade.ConfidenceFlag);
        Assert.Single(await AssignmentsAsync(db, id));
        Assert.Contains(InferredFindingMessage, Feedback(grade, "c5"));
        Assert.Contains(CodedFindingMessage, Feedback(grade, "c5"));
        Assert.Equal(31, outcome.RawTotal);
    }

    // ── Persisted review reason (ITEM 3) ────────────────────────────────────

    [Fact]
    public async Task ReviewReason_IsStoredOnTheNewAssignment()
    {
        await using var db = NewDb();
        var judgments = new ScriptedJudgments();
        judgments.Handlers[AiFeatureCodes.JevWritingOutcome] = r => Answer(r, _ => Noul(0.10));
        var (pipeline, _) = BuildPipeline(db, judgments, Flags(o => o.WritingOutcomeEnabled = true), Completion(380));
        var id = await SeedSubmissionAsync(db);

        await pipeline.EvaluateAsync(id, default);

        var assignment = Assert.Single(await AssignmentsAsync(db, id));
        Assert.Equal(WritingJevReviewReasons.OutcomeFlip, assignment.ReviewReason);
    }

    [Fact]
    public async Task ReviewReason_SeveralReasonsInOneRun_AreAllStored_FirstReasonFirst_WithoutDuplicates()
    {
        await using var db = NewDb();
        var judgments = new ScriptedJudgments();
        judgments.Handlers[AiFeatureCodes.JevWritingGuard] = BlockingGuard;
        judgments.Handlers[AiFeatureCodes.JevWritingVerify] = r => Answer(r, _ => Choice("contradicted", 0.9));
        judgments.Handlers[AiFeatureCodes.JevWritingOutcome] = r => Answer(r, _ => Noul(0.05));
        var (pipeline, _) = BuildPipeline(
            db, judgments, Flags(o =>
            {
                o.WritingGuardEnabled = true;
                o.WritingVerifyEnabled = true;
                o.WritingOutcomeEnabled = true;
            }), Completion(findings: TwoFindings));
        var id = await SeedSubmissionAsync(db);

        await pipeline.EvaluateAsync(id, default);

        var assignment = Assert.Single(await AssignmentsAsync(db, id));
        var codes = assignment.ReviewReason!.Split(',');
        Assert.Equal(WritingJevReviewReasons.GuardBlock, codes[0]);
        Assert.Contains(WritingJevReviewReasons.VerifyFlag, codes);
        Assert.Contains(WritingJevReviewReasons.OutcomeFlip, codes);
        Assert.Equal(codes.Length, codes.Distinct().Count());
        Assert.True(assignment.ReviewReason.Length <= 64);
    }

    [Theory]
    [InlineData(false, "criteria_divergence", "criteria_divergence,outcome_flip")]   // tracked existing row: first reason kept, new one appended
    [InlineData(true, "criteria_divergence", "criteria_divergence,outcome_flip")]    // existing row only in the database
    [InlineData(true, "outcome_flip", "outcome_flip")]                               // same reason again: no duplicate
    [InlineData(true, null, "outcome_flip")]                                          // old row without a reason gains one
    public async Task ReviewReason_ExistingAssignment_KeepsTheFirstReason_AndAppendsOnlyNewDistinctOnes(
        bool detachExisting, string? existingReason, string expectedReason)
    {
        await using var db = NewDb();
        var judgments = new ScriptedJudgments();
        judgments.Handlers[AiFeatureCodes.JevWritingOutcome] = r => Answer(r, _ => Noul(0.10));
        var (pipeline, _) = BuildPipeline(db, judgments, Flags(o => o.WritingOutcomeEnabled = true), Completion(380));
        var id = await SeedSubmissionAsync(db);
        var existingId = Guid.NewGuid();
        db.WritingTutorReviewAssignments.Add(new WritingTutorReviewAssignment
        {
            Id = existingId,
            SubmissionId = id,
            TutorId = string.Empty,
            ClaimedAt = DateTimeOffset.UtcNow,
            DueAt = DateTimeOffset.UtcNow.AddHours(24),
            Status = "pending",
            ReviewReason = existingReason,
        });
        await db.SaveChangesAsync();
        if (detachExisting) db.ChangeTracker.Clear();

        await pipeline.EvaluateAsync(id, default);

        var assignment = Assert.Single(await AssignmentsAsync(db, id));
        Assert.Equal(existingId, assignment.Id);
        Assert.Equal(expectedReason, assignment.ReviewReason);
    }

    [Fact]
    public async Task ReviewReason_StaysNull_WhenNothingIsFlagged()
    {
        await using var db = NewDb();
        var judgments = new ScriptedJudgments();
        judgments.Handlers[AiFeatureCodes.JevWritingOutcome] = r => Answer(r, _ => Noul(0.90)); // agrees
        var (pipeline, _) = BuildPipeline(db, judgments, Flags(o => o.WritingOutcomeEnabled = true), Completion(380));
        var id = await SeedSubmissionAsync(db);

        await pipeline.EvaluateAsync(id, default);

        Assert.Empty(await AssignmentsAsync(db, id));
    }

    // ── Flags off / Jev outage / Jev crash ──────────────────────────────────

    [Fact]
    public async Task AllJevFlagsOff_MakeZeroJevCalls_AndFlagNothing()
    {
        await using var db = NewDb();
        var judgments = new ScriptedJudgments();
        var (pipeline, gateway) = BuildPipeline(db, judgments, Flags(), Completion(findings: TwoFindings));
        var id = await SeedSubmissionAsync(db);

        var outcome = await pipeline.EvaluateAsync(id, default);

        Assert.Empty(judgments.Calls);
        Assert.Equal(1, gateway.Calls);
        Assert.Equal(31, outcome.RawTotal);
        var grade = await GradeAsync(db, id);
        Assert.Equal("high", grade.ConfidenceFlag);
        Assert.Empty(await AssignmentsAsync(db, id));
        Assert.DoesNotContain(JevWritingPilot.AdvisoryFieldName, grade.PerCriterionFeedbackJson);
        Assert.Contains(InferredFindingMessage, Feedback(grade, "c5"));
    }

    [Fact]
    public async Task JevUnavailable_ChangesNothing()
    {
        await using var db = NewDb();
        var judgments = new ScriptedJudgments(); // every feature unscripted: Unavailable
        var (pipeline, gateway) = BuildPipeline(db, judgments, AllFlagsOn(), Completion(findings: TwoFindings));
        var id = await SeedSubmissionAsync(db);

        var outcome = await pipeline.EvaluateAsync(id, default);

        Assert.Equal(5, judgments.Calls.Count); // guard, verify, findings, criteria, outcome: all attempted, all fail-soft
        Assert.Equal(1, gateway.Calls);
        Assert.Equal(31, outcome.RawTotal);
        Assert.Equal(WritingSubmissionStatuses.Graded, await StatusAsync(db, id));
        var grade = await GradeAsync(db, id);
        Assert.Equal("high", grade.ConfidenceFlag);
        Assert.Empty(await AssignmentsAsync(db, id));
        Assert.DoesNotContain(JevWritingPilot.AdvisoryFieldName, grade.PerCriterionFeedbackJson);
        Assert.Contains(InferredFindingMessage, Feedback(grade, "c5"));
    }

    [Fact]
    public async Task EveryNewJevStep_IsFailSoftOnItsOwn()
    {
        await using var db = NewDb();
        var gateway = new CountingGateway(Completion(findings: TwoFindings));
        var pipeline = new WritingSubmissionEvaluationPipeline(
            db,
            gateway,
            new EmptyCanonEngine(),
            mistakeService: null!,
            events: new NoopWritingEventBus(),
            TimeProvider.System,
            TestRuntimeSettingsProvider.FromWritingOptions(new WritingV2Options()),
            NullLogger<WritingSubmissionEvaluationPipeline>.Instance,
            assessmentPreflight: new PassThroughPreflight(),
            writingPilot: new ThrowingPilot(),
            typeSafeOptions: Microsoft.Extensions.Options.Options.Create(AllFlagsOn()));
        var id = await SeedSubmissionAsync(db);

        var outcome = await pipeline.EvaluateAsync(id, default);

        Assert.Equal(31, outcome.RawTotal);
        Assert.Equal(WritingSubmissionStatuses.Graded, await StatusAsync(db, id));
        Assert.Equal("high", (await GradeAsync(db, id)).ConfidenceFlag);
        Assert.Empty(await AssignmentsAsync(db, id));
    }

    // ── Harness ─────────────────────────────────────────────────────────────

    private static TypeSafeOptions Flags(Action<TypeSafeOptions>? configure = null)
    {
        var opts = new TypeSafeOptions { Enabled = true, ApiKey = "apikey_test" }; // every Jev flag defaults to off
        configure?.Invoke(opts);
        return opts;
    }

    private static TypeSafeOptions AllFlagsOn() => Flags(o =>
    {
        o.WritingGuardEnabled = true;
        o.WritingVerifyEnabled = true;
        o.WritingCriteriaEnabled = true;
        o.WritingOutcomeEnabled = true;
        o.WritingFindingsEnabled = true;
    });

    private static (WritingSubmissionEvaluationPipeline Pipeline, CountingGateway Gateway) BuildPipeline(
        LearnerDbContext db, ScriptedJudgments judgments, TypeSafeOptions opts, string completion)
    {
        var gateway = new CountingGateway(completion);
        var pilot = new JevWritingPilot(
            judgments, Microsoft.Extensions.Options.Options.Create(opts), NullLogger<JevWritingPilot>.Instance);
        var pipeline = new WritingSubmissionEvaluationPipeline(
            db,
            gateway,
            new EmptyCanonEngine(),
            mistakeService: null!,
            events: new NoopWritingEventBus(),
            TimeProvider.System,
            TestRuntimeSettingsProvider.FromWritingOptions(new WritingV2Options()),
            NullLogger<WritingSubmissionEvaluationPipeline>.Instance,
            assessmentPreflight: new PassThroughPreflight(),
            writingPilot: pilot,
            typeSafeOptions: Microsoft.Extensions.Options.Options.Create(opts));
        return (pipeline, gateway);
    }

    private static LearnerDbContext NewDb()
    {
        var db = new LearnerDbContext(
            new DbContextOptionsBuilder<LearnerDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                .Options);
        db.WritingScenarios.Add(new WritingScenario
        {
            Id = ScenarioId,
            Title = "Jev wave 2 task",
            Profession = "medicine",
            LetterType = "routine_referral",
            TaskPromptMarkdown = "Write to Dr Green requesting a review.",
            Status = "published",
            AuthorId = "admin-1",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        db.SaveChanges();
        return db;
    }

    private static async Task<Guid> SeedSubmissionAsync(LearnerDbContext db)
    {
        var id = Guid.NewGuid();
        db.WritingSubmissions.Add(new WritingSubmission
        {
            Id = id,
            UserId = "jev-learner",
            ScenarioId = ScenarioId,
            Mode = "practice",
            LetterContent = LetterText,
            LetterContentHash = $"hash-{id:N}",
            WordCount = 24,
            Status = WritingSubmissionStatuses.Queued,
            GradingTier = "express",
            InputSource = "typed",
            StartedAt = DateTimeOffset.UtcNow,
            SubmittedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
        return id;
    }

    private static Task<WritingGrade> GradeAsync(LearnerDbContext db, Guid submissionId) =>
        db.WritingGrades.AsNoTracking().SingleAsync(g => g.SubmissionId == submissionId);

    private static Task<List<WritingTutorReviewAssignment>> AssignmentsAsync(LearnerDbContext db, Guid submissionId) =>
        db.WritingTutorReviewAssignments.AsNoTracking().Where(a => a.SubmissionId == submissionId).ToListAsync();

    private static async Task<string> StatusAsync(LearnerDbContext db, Guid submissionId) =>
        (await db.WritingSubmissions.AsNoTracking().SingleAsync(s => s.Id == submissionId)).Status;

    private static string Feedback(WritingGrade grade, string criterion) =>
        JsonDocument.Parse(grade.PerCriterionFeedbackJson).RootElement
            .GetProperty(criterion).GetProperty("feedback").GetString() ?? string.Empty;

    private static JevJudgmentResult BlockingGuard(JevJudgmentRequest request) =>
        Answer(request, q => Noul(q.Id == "jev_injection" ? 0.99 : 0.01));

    private static JevJudgmentResult Answer(JevJudgmentRequest request, Func<JevQuestion, JevAnswer> answerFor) =>
        new(JevCallStatus.Ok, "jev-1.13.0", request.Questions.ToDictionary(q => q.Id, answerFor), 500, 10, null);

    private static JevAnswer Noul(double p) => new(JevQuestionKind.Noul, new JevNoulAnswer(p), null, null);

    private static JevAnswer Choice(string choice, double confidence) =>
        new(JevQuestionKind.Choice, null, new JevChoiceAnswer(choice, new Dictionary<string, double>(), confidence), null);

    private static JevAnswer Score(double position, double confidence) =>
        new(JevQuestionKind.Score, null, null, new JevScoreAnswer(position, new Dictionary<string, double>(), confidence));

    /// <summary>Answers per feature code; an unscripted feature is Unavailable, like a real outage.</summary>
    private sealed class ScriptedJudgments : ITypeSafeJudgmentService
    {
        public Dictionary<string, Func<JevJudgmentRequest, JevJudgmentResult>> Handlers { get; } = new();
        public List<(string Feature, JevJudgmentRequest Request, int? Version)> Calls { get; } = new();

        public Task<JevJudgmentResult> AskAsync(JevJudgmentRequest request, JevCallMetadata call, CancellationToken ct)
        {
            Calls.Add((call.FeatureCode, request, call.ResourceVersion));
            return Task.FromResult(
                Handlers.TryGetValue(call.FeatureCode, out var handler) ? handler(request) : JevJudgmentResult.Unavailable("unscripted"));
        }
    }

    /// <summary>Every new hook throws, as a buggy or crashing judgment layer would.</summary>
    private sealed class ThrowingPilot : IJevWritingPilot
    {
        public Task<WritingGuardResult> GuardSubmissionAsync(string letterText, string task, string? userId, CancellationToken ct, int? resourceVersion = null) =>
            Task.FromResult(WritingGuardResult.Neutral(JevCallStatus.Disabled, "test"));

        public Task<WritingRouteResult> RouteWritingRequestAsync(string task, string? letterText, string? userId, CancellationToken ct) =>
            Task.FromResult(WritingRouteResult.Neutral("test"));

        public Task<WritingVerifyResult> VerifyFindingsAsync(string letterText, IReadOnlyList<WritingFindingInput> findings, string? userId, CancellationToken ct, int? resourceVersion = null) =>
            Task.FromResult(WritingVerifyResult.Neutral("test"));

        public Task<WritingCriteriaResult> ScoreCriteriaAsync(string letterText, string letterType, string? userId, CancellationToken ct, int? resourceVersion = null) =>
            Task.FromResult(new WritingCriteriaResult(
                new Dictionary<string, double> { ["c1"] = 1 }, JevCallStatus.Ok, null, new Dictionary<string, double> { ["c1"] = 0.9 }));

        public string MergeAdvisoryIntoPerCriterionJson(string perCriterionJson, IReadOnlyDictionary<string, double> advisory) => perCriterionJson;

        public WritingCriteriaDivergence AssessCriteriaDivergence(IReadOnlyDictionary<string, int> graderScores, WritingCriteriaResult advisory) =>
            throw new InvalidOperationException("divergence boom");

        public Task<WritingOutcomeResult> CheckOutcomeAsync(string taskText, string caseNotes, string letterText, bool graderPassed, string? userId, CancellationToken ct, int? resourceVersion = null) =>
            throw new InvalidOperationException("outcome boom");

        public Task<WritingFindingsResult> ClassifyFindingsAsync(string letterText, IReadOnlyList<WritingFindingInput> findings, string? userId, CancellationToken ct, int? resourceVersion = null) =>
            throw new InvalidOperationException("findings boom");
    }

    private sealed class CountingGateway(string completion) : IAiGatewayService
    {
        public int Calls { get; private set; }

        /// <summary>The first N provider calls fail, as a provider outage would.</summary>
        public int FailFirstCalls { get; set; }

        public AiGroundedPrompt BuildGroundedPrompt(AiGroundingContext context)
            => new()
            {
                SystemPrompt = "# OET AI — Rulebook-Grounded System Prompt\n**This call concerns WRITING**",
                TaskInstruction = "score",
            };

        public Task<AiGatewayResult> CompleteAsync(AiGatewayRequest request, CancellationToken ct = default)
        {
            Calls++;
            if (Calls <= FailFirstCalls) throw new InvalidOperationException("provider down");
            return Task.FromResult(new AiGatewayResult { Completion = completion, ResolvedModel = "claude-sonnet-5" });
        }
    }

    private sealed class PassThroughPreflight : IWritingAssessmentPreflightService
    {
        public Task<WritingAssessmentPreflightResult> ValidateAsync(WritingSubmission submission, CancellationToken ct)
            => Task.FromResult(new WritingAssessmentPreflightResult(
                true, WritingAssessmentV11Status.CandidateReady,
                Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(),
                "medicine", "routine_referral", "test", "Write to Dr Green requesting a review.", "Patient name: Adam Lee\nAge: 54"));
    }
}
