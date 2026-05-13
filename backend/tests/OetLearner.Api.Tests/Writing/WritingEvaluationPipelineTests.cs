using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Rulebook;
using OetLearner.Api.Services.Writing;

namespace OetLearner.Api.Tests.Writing;

/// <summary>
/// Pipeline-level tests for <see cref="WritingEvaluationPipeline"/> — the
/// real grounded AI grading path that replaced the placeholder in
/// <c>BackgroundJobProcessor.CompleteWritingEvaluationAsync</c>.
///
/// Uses SQLite in-memory (per repo memory: EF SQLite catches translation
/// regressions that <c>UseInMemoryDatabase</c> hides).
/// </summary>
public sealed class WritingEvaluationPipelineTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly LearnerDbContext _db;
    private readonly RulebookLoader _loader = new();
    private readonly WritingRuleEngine _engine;

    public WritingEvaluationPipelineTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseSqlite(_connection)
            .Options;
        _db = new LearnerDbContext(options);
        _db.Database.EnsureCreated();
        _engine = new WritingRuleEngine(_loader);
    }

    public async ValueTask DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task CompleteEvaluationAsync_HappyPath_PersistsAllSixCriteriaAndCompletes()
    {
        var (attempt, evaluation, job) = SeedAttempt(profession: "medicine", country: "UK", letterType: "discharge");

        var validJson = JsonSerializer.Serialize(new
        {
            findings = new object[]
            {
                new { ruleId = "R09.2", severity = "major", quote = "the patient", message = "Avoid 'the patient'.", fixSuggestion = "Use the surname.", criterionCode = "language" },
                new { ruleId = "ai-only-rule", severity = "minor", message = "Should not survive grounding filter.", criterionCode = "content" },
            },
            criteriaScores = new
            {
                purpose = 2,
                content = 2,
                conciseness_clarity = 5,
                genre_style = 5,
                organisation_layout = 5,
                language = 5,
            },
            estimatedScaledScore = 360,
            estimatedGrade = "B",
            passed = true,
            passRequires = new { scaledScore = 350, grade = "B", jurisdiction = "UK" },
            advisory = "Solid discharge letter; reduce minor lexical repetition.",
            strengths = new[] { "Clear purpose statement.", "Logical paragraph order." },
        });

        var pipeline = new WritingEvaluationPipeline(_db, new FakeAiGateway(validJson), _engine, NullLogger<WritingEvaluationPipeline>.Instance);

        await pipeline.CompleteEvaluationAsync(job, default);
        await _db.SaveChangesAsync();

        var reloadedAttempt = await _db.Attempts.FirstAsync(x => x.Id == attempt.Id);
        var reloadedEval = await _db.Evaluations.FirstAsync(x => x.Id == evaluation.Id);

        Assert.Equal(AttemptState.Completed, reloadedAttempt.State);
        Assert.NotNull(reloadedAttempt.CompletedAt);

        Assert.Equal(AsyncState.Completed, reloadedEval.State);
        Assert.Equal("completed", reloadedEval.StatusReasonCode);
        Assert.False(reloadedEval.Retryable);
        Assert.Null(reloadedEval.RetryAfterMs);
        Assert.Contains("-", reloadedEval.ScoreRange);
        Assert.Equal("B", reloadedEval.GradeRange);
        Assert.Equal(ConfidenceBand.High, reloadedEval.ConfidenceBand);

        // CriterionScoresJson must contain all 6 OET Writing criteria.
        using var critDoc = JsonDocument.Parse(reloadedEval.CriterionScoresJson);
        var crits = critDoc.RootElement.EnumerateArray()
            .Select(c => c.GetProperty("criterionCode").GetString())
            .ToHashSet();
        Assert.Equal(6, crits.Count);
        Assert.Contains("purpose", crits);
        Assert.Contains("content", crits);
        Assert.Contains("conciseness_clarity", crits);
        Assert.Contains("genre_style", crits);
        Assert.Contains("organisation_layout", crits);
        Assert.Contains("language", crits);

        var maxScoresByCriterion = critDoc.RootElement.EnumerateArray()
            .ToDictionary(
                c => c.GetProperty("criterionCode").GetString() ?? throw new InvalidOperationException("Missing criterion code."),
                c => c.GetProperty("max").GetInt32());
        Assert.Equal(3, maxScoresByCriterion["purpose"]);
        Assert.Equal(7, maxScoresByCriterion["content"]);
        Assert.Equal(7, maxScoresByCriterion["conciseness_clarity"]);
        Assert.Equal(7, maxScoresByCriterion["genre_style"]);
        Assert.Equal(7, maxScoresByCriterion["organisation_layout"]);
        Assert.Equal(7, maxScoresByCriterion["language"]);

        // FeedbackItemsJson must be non-empty (rule-engine finding survives merge).
        using var feedbackDoc = JsonDocument.Parse(reloadedEval.FeedbackItemsJson);
        Assert.True(feedbackDoc.RootElement.GetArrayLength() >= 1);

        // The non-allowed AI ruleId "ai-only-rule" must be filtered out by
        // the grounding invariant (it is not in the rulebook's applied list).
        var feedbackRuleIds = feedbackDoc.RootElement.EnumerateArray()
            .Select(e => e.GetProperty("ruleId").GetString())
            .ToList();
        Assert.DoesNotContain("ai-only-rule", feedbackRuleIds);
    }

    [Fact]
    public async Task CompleteEvaluationAsync_GatewayThrows_MarksFailedAndPreservesRulebookFindings()
    {
        // Use a draft that the rulebook engine reliably flags so we can
        // assert deterministic findings survive the failure path.
        var (attempt, evaluation, job) = SeedAttempt(
            profession: "medicine",
            country: "UK",
            letterType: "discharge",
            draftOverride: BuildBadDraft());

        var pipeline = new WritingEvaluationPipeline(_db, new FakeAiGateway(throwOnComplete: true), _engine, NullLogger<WritingEvaluationPipeline>.Instance);

        await pipeline.CompleteEvaluationAsync(job, default);
        await _db.SaveChangesAsync();

        var reloadedEval = await _db.Evaluations.FirstAsync(x => x.Id == evaluation.Id);
        Assert.Equal(AsyncState.Failed, reloadedEval.State);
        Assert.Equal("ai_provider_error", reloadedEval.StatusReasonCode);
        Assert.True(reloadedEval.Retryable);
        Assert.Equal(60_000, reloadedEval.RetryAfterMs);

        // Rule-engine findings must still be present in feedback items so
        // the learner sees deterministic feedback even on AI failure.
        using var feedbackDoc = JsonDocument.Parse(reloadedEval.FeedbackItemsJson);
        Assert.True(feedbackDoc.RootElement.GetArrayLength() >= 1,
            "Expected at least one rule-engine finding to survive AI failure path.");

        var violations = await _db.WritingRuleViolations
            .Where(v => v.AttemptId == attempt.Id && v.EvaluationId == evaluation.Id)
            .ToListAsync();
        Assert.NotEmpty(violations);
        Assert.All(violations, v => Assert.Equal("medicine", v.Profession));
        Assert.All(violations, v => Assert.Equal("discharge", v.LetterType));

        // Attempt must NOT be marked Completed on a failed evaluation.
        var reloadedAttempt = await _db.Attempts.FirstAsync(x => x.Id == attempt.Id);
        Assert.NotEqual(AttemptState.Completed, reloadedAttempt.State);
    }

    [Fact]
    public async Task CompleteEvaluationAsync_MissingCriterionScore_MarksFailed()
    {
        var (_, evaluation, job) = SeedAttempt(profession: "medicine", country: "UK", letterType: "routine_referral");
        var incompleteJson = JsonSerializer.Serialize(new
        {
            findings = Array.Empty<object>(),
            criteriaScores = new
            {
                purpose = 2,
                content = 5,
                conciseness_clarity = 5,
                genre_style = 5,
                organisation_layout = 5,
            },
            estimatedScaledScore = 360,
            advisory = "Incomplete scoring contract should fail."
        });

        var pipeline = new WritingEvaluationPipeline(_db, new FakeAiGateway(incompleteJson), _engine, NullLogger<WritingEvaluationPipeline>.Instance);

        await pipeline.CompleteEvaluationAsync(job, default);
        await _db.SaveChangesAsync();

        var reloadedEval = await _db.Evaluations.FirstAsync(x => x.Id == evaluation.Id);
        Assert.Equal(AsyncState.Failed, reloadedEval.State);
        Assert.Equal("ai_malformed_response", reloadedEval.StatusReasonCode);
        Assert.True(reloadedEval.Retryable);
    }

    [Fact]
    public async Task CompleteEvaluationAsync_UserInputIncludesServerCaseNotesAndTaskMetadata()
    {
        const string caseNotes = "Patient requested referral. Consent was documented. Follow-up appointment: 18 March 2026.";
        var (_, _, job) = SeedAttempt(
            profession: "medicine",
            country: "UK",
            letterType: "routine_referral",
            caseNotes: caseNotes);
        var gateway = new FakeAiGateway(BuildValidAiJson());
        var pipeline = new WritingEvaluationPipeline(_db, gateway, _engine, NullLogger<WritingEvaluationPipeline>.Instance);

        await pipeline.CompleteEvaluationAsync(job, default);

        Assert.NotNull(gateway.LastRequest);
        Assert.Contains("Official task source and case notes", gateway.LastRequest!.UserInput, StringComparison.Ordinal);
        Assert.Contains(caseNotes, gateway.LastRequest.UserInput, StringComparison.Ordinal);
        Assert.Contains("Task metadata JSON", gateway.LastRequest.UserInput, StringComparison.Ordinal);
    }

    // -------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------

    private (Attempt attempt, Evaluation evaluation, BackgroundJobItem job) SeedAttempt(
        string profession,
        string? country,
        string letterType,
        string? draftOverride = null,
        string? caseNotes = null)
    {
        var userId = $"u-{Guid.NewGuid():N}";
        var contentId = $"c-{Guid.NewGuid():N}";
        var attemptId = $"a-{Guid.NewGuid():N}";

        _db.Users.Add(new LearnerUser
        {
            Id = userId,
            DisplayName = "Test Learner",
            Email = $"{userId}@example.com",
            ActiveProfessionId = profession,
            CreatedAt = DateTimeOffset.UtcNow,
            LastActiveAt = DateTimeOffset.UtcNow,
        });

        if (!string.IsNullOrWhiteSpace(country))
        {
            _db.Set<LearnerGoal>().Add(new LearnerGoal
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                ProfessionId = profession,
                TargetCountry = country,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
        }

        if (!string.IsNullOrWhiteSpace(caseNotes))
        {
            _db.ContentItems.Add(new ContentItem
            {
                Id = contentId,
                ContentType = "writing_task",
                SubtestCode = "writing",
                ProfessionId = profession,
                Title = "Pipeline Case Notes Task",
                Difficulty = "standard",
                EstimatedDurationMinutes = 45,
                ScenarioType = letterType,
                PublishedRevisionId = "pipeline-test-revision",
                Status = ContentStatus.Published,
                CaseNotes = caseNotes,
                DetailJson = JsonSerializer.Serialize(new { letterType, taskDate = "12 March 2026" }),
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
                PublishedAt = DateTimeOffset.UtcNow,
            });
        }

        var attempt = new Attempt
        {
            Id = attemptId,
            UserId = userId,
            ContentId = contentId,
            SubtestCode = "writing",
            Context = JsonSerializer.Serialize(new { letterType }),
            Mode = "practice",
            State = AttemptState.Submitted,
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-30),
            SubmittedAt = DateTimeOffset.UtcNow,
            DraftContent = draftOverride ?? BuildSimpleDraft(),
        };
        _db.Attempts.Add(attempt);

        var evaluation = new Evaluation
        {
            Id = $"eval-{Guid.NewGuid():N}",
            AttemptId = attemptId,
            SubtestCode = "writing",
            State = AsyncState.Queued,
            ScoreRange = "pending",
            ModelExplanationSafe = "pending",
            LearnerDisclaimer = "pending",
            LastTransitionAt = DateTimeOffset.UtcNow,
        };
        _db.Evaluations.Add(evaluation);

        var job = new BackgroundJobItem
        {
            Id = $"job-{Guid.NewGuid():N}",
            Type = JobType.WritingEvaluation,
            AttemptId = attemptId,
            State = AsyncState.Processing,
            CreatedAt = DateTimeOffset.UtcNow,
            AvailableAt = DateTimeOffset.UtcNow,
            LastTransitionAt = DateTimeOffset.UtcNow,
        };
        _db.BackgroundJobs.Add(job);

        _db.SaveChanges();
        return (attempt, evaluation, job);
    }

    private static string BuildSimpleDraft() =>
        "12 March 2026\n\nDr Brown\nGeneral Practice\n\nDear Dr Brown,\n\nRe: Mr Smith\n\n" +
        "I am writing to update you on Mr Smith following his discharge today after a routine procedure.\n\n" +
        "Yours sincerely,\nDr Jones\n";

    private static string BuildBadDraft() =>
        // Multiple deliberate rulebook violations (the patient, contractions,
        // ASAP) so the deterministic engine produces findings without
        // depending on the AI.
        "Dear Dr Brown,\n\nRe: Mr Smith\n\nI'm writing about the patient. He's been quite unwell and we've decided to refer him ASAP. Yesterday he came in.\n\nYours sincerely,\nDr Jones\n";

    private static string BuildValidAiJson() => JsonSerializer.Serialize(new
    {
        findings = Array.Empty<object>(),
        criteriaScores = new
        {
            purpose = 2,
            content = 5,
            conciseness_clarity = 5,
            genre_style = 5,
            organisation_layout = 5,
            language = 5,
        },
        estimatedScaledScore = 360,
        estimatedGrade = "B",
        advisory = "Grounded scoring contract complete.",
        strengths = new[] { "Clear purpose." },
    });

    // -------------------------------------------------------------------
    // Fake gateway (test double)
    // -------------------------------------------------------------------

    private sealed class FakeAiGateway : IAiGatewayService
    {
        private readonly string? _completion;
        private readonly bool _throwOnComplete;
        public AiGatewayRequest? LastRequest { get; private set; }

        public FakeAiGateway(string completion)
        {
            _completion = completion;
            _throwOnComplete = false;
        }

        public FakeAiGateway(bool throwOnComplete)
        {
            _completion = null;
            _throwOnComplete = throwOnComplete;
        }

        public AiGroundedPrompt BuildGroundedPrompt(AiGroundingContext context)
        {
            // Return a minimal prompt that satisfies the gateway grounding
            // header invariant. We intentionally don't include "ai-only-rule"
            // in AppliedRuleIds so the pipeline's grounding filter strips it.
            return new AiGroundedPrompt
            {
                SystemPrompt = "OET AI — Rulebook-Grounded System Prompt\n(test fixture)\n",
                TaskInstruction = "Score this writing attempt.",
                Metadata = new AiGroundedPromptMetadata
                {
                    RulebookVersion = "test-1.0.0",
                    RulebookKind = context.Kind,
                    Profession = context.Profession,
                    ScoringPassMark = 350,
                    ScoringGrade = "B",
                    AppliedRulesCount = 1,
                    AppliedRuleIds = new[] { "R09.2" },
                },
            };
        }

        public Task<AiGatewayResult> CompleteAsync(AiGatewayRequest request, CancellationToken ct = default)
        {
            LastRequest = request;
            if (_throwOnComplete)
            {
                throw new InvalidOperationException("Simulated AI provider failure.");
            }

            return Task.FromResult(new AiGatewayResult
            {
                Completion = _completion ?? "{}",
                Metadata = request.Prompt!.Metadata,
                RulebookVersion = request.Prompt!.Metadata.RulebookVersion,
                AppliedRuleIds = request.Prompt!.Metadata.AppliedRuleIds,
            });
        }
    }
}
