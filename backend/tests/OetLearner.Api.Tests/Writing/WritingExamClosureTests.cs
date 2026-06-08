using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Writing;
using Xunit;

namespace OetLearner.Api.Tests.Writing;

/// <summary>
/// Cross-cutting tests for the OET Writing exam-closure backend services
/// (authoring validate/import/export/clone, attempt events, heuristic
/// pre-assessment, double-marking + moderation, and result-visibility gating).
/// Each test uses a uniquely-named in-memory DbContext and the real service
/// constructors. The heuristic service is exercised with the LLM flag disabled,
/// so the <see cref="StubAiGateway"/> (declared in
/// WritingHeuristicPreAssessmentServiceTests) is never actually called.
/// </summary>
public sealed class WritingExamClosureTests
{
    private static LearnerDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase($"writing-exam-closure-{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new LearnerDbContext(options);
    }

    private static ClaimsPrincipal User(string id = "admin-1")
        => new(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, id) }, "test"));

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    }

    private static WritingHeuristicPreAssessmentService NewHeuristic(bool llmEnabled = false)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Writing:PreAssessmentLlmEnabled"] = llmEnabled ? "true" : "false",
            })
            .Build();

        // StubAiGateway is defined alongside WritingHeuristicPreAssessmentServiceTests
        // in this same test namespace; reused here (it is never called while disabled).
        return new WritingHeuristicPreAssessmentService(
            new StubAiGateway(),
            configuration,
            NullLogger<WritingHeuristicPreAssessmentService>.Instance);
    }

    private static WritingTaskUpsertDto BuildValidUpsert() => new()
    {
        InternalCode = "MED-WR-T01",
        Title = "Referral: Mr Sample Patient",
        Profession = "Medicine",
        LetterType = "routine_referral",
        WriterRole = "You are the doctor on duty.",
        TodayDate = "1 June 2026",
        TaskPromptMarkdown = "Write a referral letter.",
        FixedInstructions = new List<string> { "Expand the relevant notes into complete sentences" },
        SourceProvenance = "Authored in-house for tests.",
        IntegrityAcknowledged = true,
    };

    // ── 1. Authoring publish-readiness validation ───────────────────────────

    [Fact]
    public async Task Validate_not_publish_ready_when_source_provenance_and_integrity_missing()
    {
        await using var db = NewDb();
        var svc = new WritingTaskAuthoringService(db, NullLogger<WritingTaskAuthoringService>.Instance);

        // Minimal task: no source provenance, integrity not acknowledged.
        var created = await svc.CreateAsync(new WritingTaskUpsertDto
        {
            Title = "Incomplete task",
            Profession = "Medicine",
            LetterType = "routine_referral",
            TaskPromptMarkdown = "Write something.",
        }, User(), default);

        var validation = await svc.ValidateAsync(created.Id, default);

        Assert.NotNull(validation);
        Assert.False(validation!.IsPublishReady);
        Assert.Contains(validation.Issues, i => i.Code == "source_provenance_required");
        Assert.Contains(validation.Issues, i => i.Code == "integrity_not_acknowledged");
    }

    [Fact]
    public async Task Validate_publish_ready_when_all_requirements_present()
    {
        await using var db = NewDb();
        var svc = new WritingTaskAuthoringService(db, NullLogger<WritingTaskAuthoringService>.Instance);

        var created = await svc.CreateAsync(BuildValidUpsert(), User(), default);
        var validation = await svc.ValidateAsync(created.Id, default);

        Assert.NotNull(validation);
        Assert.True(validation!.IsPublishReady);
        Assert.DoesNotContain(validation.Issues, i => i.Severity == "error");
    }

    // ── 2. Import → Export round trip (spec §18) ────────────────────────────

    [Fact]
    public async Task Import_then_Export_roundtrips_key_fields_and_creates_draft()
    {
        await using var db = NewDb();
        var svc = new WritingTaskAuthoringService(db, NullLogger<WritingTaskAuthoringService>.Instance);

        var import = new WritingTaskImportJson
        {
            TaskTitle = "Imported referral",
            InternalCode = "MED-WR-IMP1",
            Profession = "Medicine",
            TaskType = "routine_referral",
            Duration = new WritingImportDuration { ReadingTimeSeconds = 300, WritingTimeSeconds = 2400 },
            CaseNotes = new WritingImportCaseNotes
            {
                TodayDate = "2 February 2026",
                CandidateRole = "You are a GP.",
            },
            WritingTask = new WritingImportWritingTask
            {
                Instruction = "Write a referral letter to the respiratory clinic.",
                FixedInstructions = new List<string> { "Use letter format" },
                WordGuide = new WritingImportWordGuide { Min = 180, Max = 200 },
            },
            Marking = new WritingImportMarking
            {
                ExpectedPurpose = "Refer for respiratory assessment.",
                ExpectedAction = "Request clinic review.",
            },
        };

        var imported = await svc.ImportAsync(import, User(), default);

        // Import creates a draft.
        Assert.Equal("draft", imported.Status);
        Assert.Equal("MED-WR-IMP1", imported.InternalCode);
        Assert.Equal("routine_referral", imported.LetterType);
        Assert.Equal("You are a GP.", imported.WriterRole);

        var export = await svc.ExportAsync(imported.Id, default);

        Assert.NotNull(export);
        Assert.Equal("Imported referral", export!.TaskTitle);
        Assert.Equal("MED-WR-IMP1", export.InternalCode);
        Assert.Equal("Medicine", export.Profession);
        Assert.Equal("routine_referral", export.TaskType);
        Assert.Equal("Refer for respiratory assessment.", export.Marking?.ExpectedPurpose);
        Assert.Equal("Write a referral letter to the respiratory clinic.", export.WritingTask?.Instruction);
    }

    // ── 3. Clone → new draft, surviving fields copied ───────────────────────

    [Fact]
    public async Task Clone_creates_new_draft_copying_task_fields()
    {
        await using var db = NewDb();
        var svc = new WritingTaskAuthoringService(db, NullLogger<WritingTaskAuthoringService>.Instance);

        var created = await svc.CreateAsync(BuildValidUpsert(), User(), default);
        var (published, _) = await svc.PublishAsync(created.Id, default);
        Assert.NotNull(published);
        Assert.Equal("published", published!.Status);

        var clone = await svc.CloneAsync(created.Id, User("admin-2"), default);

        Assert.NotNull(clone);
        Assert.NotEqual(created.Id, clone!.Id);
        Assert.Equal("draft", clone.Status);
        Assert.Equal("routine_referral", clone.LetterType);
        Assert.Equal("Medicine", clone.Profession);
    }

    // ── 4. Attempt events: persist known, skip unknown, respect batch cap ───

    [Fact]
    public async Task AttemptEvents_persist_known_skip_unknown_and_respect_batch_cap()
    {
        await using var db = NewDb();
        var svc = new WritingAttemptEventService(db, NullLogger<WritingAttemptEventService>.Instance);

        var mixed = new List<WritingAttemptEventInput>
        {
            new("attempt_started", DateTimeOffset.UtcNow, "computer", "session-1", Guid.NewGuid(), null, null),
            new("totally_made_up", DateTimeOffset.UtcNow, "computer", "session-1", null, null, null),
            new("submit_clicked", DateTimeOffset.UtcNow, "paper", "session-1", null, null, "{\"k\":1}"),
        };

        var count = await svc.RecordAsync("user-1", mixed, default);

        Assert.Equal(2, count); // unknown type skipped, not counted.
        Assert.Equal(2, await db.WritingAttemptEvents.CountAsync());

        // Batch cap: 60 valid events should be truncated to the 50-row maximum.
        var big = Enumerable.Range(0, 60)
            .Select(_ => new WritingAttemptEventInput("response_typed", DateTimeOffset.UtcNow, "computer", "session-2", null, null, null))
            .ToList();
        var capped = await svc.RecordAsync("user-2", big, default);

        Assert.Equal(50, capped);
    }

    // ── 5. Heuristic pre-assessment of a weak letter ────────────────────────

    [Fact]
    public async Task Heuristic_flags_short_letter_and_language_issues()
    {
        var service = NewHeuristic(llmEnabled: false);
        var scenario = new WritingScenario
        {
            Id = Guid.NewGuid(),
            Title = "Referral",
            LetterType = "routine_referral",
            Profession = "Medicine",
            WordGuideMin = 180,
            WordGuideMax = 200,
        };

        // Short body, a contraction ("can't"), and no "Dear" greeting.
        var request = new WritingPreAssessmentRequest(
            Guid.NewGuid(),
            "The patient has a management plan in place but can't attend the clinic.",
            scenario);

        var result = await service.AssessAsync(request, default);

        Assert.Equal("heuristic", result.Source);
        Assert.False(result.WithinWordGuide);
        // Content checklists were removed: coverage is treated as complete and no
        // missing/irrelevant content is surfaced.
        Assert.Equal(100, result.KeyContentCoveragePercent);
        Assert.Empty(result.MissingKeyContent);
        Assert.Empty(result.DetectedIrrelevantContent);
        Assert.NotEmpty(result.LanguageNotes);

        // Bands clamped to their criterion maxima (c1 0-3; c2..c6 0-7).
        var b = result.EstimatedBands;
        Assert.InRange(b.C1Purpose, 0, 3);
        Assert.InRange(b.C2Content, 0, 7);
        Assert.InRange(b.C3Conciseness, 0, 7);
        Assert.InRange(b.C4Genre, 0, 7);
        Assert.InRange(b.C5Organisation, 0, 7);
        Assert.InRange(b.C6Language, 0, 7);
    }

    // ── 6. Double-marking + moderation lifecycle ────────────────────────────

    [Fact]
    public async Task Moderation_first_then_variance_drives_pending_or_finalized()
    {
        // First submit → pending_second.
        await using (var db = NewDb())
        {
            var svc = new WritingModerationService(db, NullLogger<WritingModerationService>.Instance);
            var first = await svc.RecordMarkerScoreAsync(
                Guid.NewGuid(), "marker-1", "first",
                new WritingCriteriaScores(3, 6, 6, 6, 6, 6),
                WritingModerationService.DefaultVarianceThreshold, default);

            Assert.Equal("pending_second", first.Status);
            Assert.Equal("marker-1", first.FirstMarkerId);
        }

        // Second submit, variance over threshold → pending_moderation.
        await using (var db = NewDb())
        {
            var svc = new WritingModerationService(db, NullLogger<WritingModerationService>.Instance);
            var submissionId = Guid.NewGuid();
            await svc.RecordMarkerScoreAsync(submissionId, "marker-1", "first",
                new WritingCriteriaScores(3, 7, 7, 7, 7, 7),
                WritingModerationService.DefaultVarianceThreshold, default);
            var second = await svc.RecordMarkerScoreAsync(submissionId, "marker-2", "second",
                new WritingCriteriaScores(1, 2, 3, 3, 3, 3),
                WritingModerationService.DefaultVarianceThreshold, default);

            Assert.Equal("pending_moderation", second.Status);
            Assert.True(second.VariancePoints > WritingModerationService.DefaultVarianceThreshold);
        }

        // Second submit, variance within threshold → finalized with averaged score.
        await using (var db = NewDb())
        {
            var svc = new WritingModerationService(db, NullLogger<WritingModerationService>.Instance);
            var submissionId = Guid.NewGuid();
            await svc.RecordMarkerScoreAsync(submissionId, "marker-1", "first",
                new WritingCriteriaScores(3, 6, 6, 6, 6, 6),
                WritingModerationService.DefaultVarianceThreshold, default);
            var second = await svc.RecordMarkerScoreAsync(submissionId, "marker-2", "second",
                new WritingCriteriaScores(3, 6, 6, 6, 4, 6),
                WritingModerationService.DefaultVarianceThreshold, default);

            Assert.Equal("finalized", second.Status);
            Assert.NotNull(second.FinalScoreJson);

            var final = System.Text.Json.JsonSerializer.Deserialize<WritingCriteriaScores>(second.FinalScoreJson!);
            Assert.NotNull(final);
            // c5 averages (6 + 4) / 2 = 5; others unchanged at their shared value.
            Assert.Equal(5, final!.C5Organisation);
            Assert.Equal(6, final.C2Content);
        }
    }

    // ── 7. Result-visibility gating of the learner feedback bundle ──────────

    [Fact]
    public async Task Feedback_hides_annotations_when_flag_false_shows_when_true()
    {
        // ----- shared fixture builder -----
        async Task<(LearnerDbContext Db, IWritingResultFeedbackService Svc, Guid SubmissionId)> BuildAsync(bool show)
        {
            var db = NewDb();
            var userId = "learner-1";
            var scenarioId = Guid.NewGuid();
            var submissionId = Guid.NewGuid();
            var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

            db.WritingScenarios.Add(new WritingScenario
            {
                Id = scenarioId,
                Title = "Referral",
                LetterType = "routine_referral",
                Profession = "Medicine",
                Status = "published",
                AuthorId = "seed",
                CreatedAt = now,
                UpdatedAt = now,
            });
            db.WritingSubmissions.Add(new WritingSubmission
            {
                Id = submissionId,
                UserId = userId,
                ScenarioId = scenarioId,
                Status = "submitted",
                LetterContent = "Dear Dr, learner letter body. Yours sincerely.",
                LetterContentHash = "test-hash",
                WordCount = 8,
                SubmittedAt = now,
                CreatedAt = now,
            });
            // A tutor span annotation that should be gated by ShowAnnotatedResponse.
            db.WritingFeedbackAnnotations.Add(new WritingFeedbackAnnotation
            {
                Id = Guid.NewGuid(),
                SubmissionId = submissionId,
                TutorId = "tutor-1",
                Criterion = "c2",
                HighlightedText = "learner letter",
                StartOffset = 9,
                EndOffset = 23,
                Severity = "medium",
                FeedbackText = "Expand this.",
                CreatedAt = now,
            });
            // Scenario-scoped visibility override controlling the flag under test.
            db.WritingResultVisibilityConfigs.Add(new WritingResultVisibilityConfig
            {
                Id = scenarioId.ToString(),
                ScenarioId = scenarioId,
                ShowSubmissionReceived = true,
                ShowAiEstimate = false,
                ShowTutorScore = true,
                ShowFullCriteria = true,
                ShowAnnotatedResponse = show,
                ShowMissingContent = show,
                ShowModelAnswer = show,
                ShowContentChecklist = show,
                AllowRewrite = true,
                UpdatedAt = now,
            });
            await db.SaveChangesAsync();

            var visibility = new WritingResultVisibilityService(db, new FixedClock(), NullLogger<WritingResultVisibilityService>.Instance);
            var svc = new WritingResultFeedbackService(
                db, visibility, NullLogger<WritingResultFeedbackService>.Instance);
            return (db, svc, submissionId);
        }

        // Flag FALSE → annotations hidden.
        var hidden = await BuildAsync(show: false);
        await using (hidden.Db)
        {
            var feedback = await hidden.Svc.GetFeedbackAsync("learner-1", hidden.SubmissionId, default);
            Assert.Empty(feedback.Annotations);
        }

        // Flag TRUE → annotations shown.
        var shown = await BuildAsync(show: true);
        await using (shown.Db)
        {
            var feedback = await shown.Svc.GetFeedbackAsync("learner-1", shown.SubmissionId, default);
            Assert.NotEmpty(feedback.Annotations);
        }
    }
}
