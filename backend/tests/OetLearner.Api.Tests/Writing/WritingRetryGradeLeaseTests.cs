using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Ai;
using OetLearner.Api.Services.Rulebook;
using OetLearner.Api.Services.Writing;
using OetLearner.Api.Services.Writing.Configuration;
using OetLearner.Api.Services.Writing.Events;

namespace OetLearner.Api.Tests.Writing;

/// <summary>
/// Claim-lease recovery: a submission wedged in <c>grading</c> with an expired
/// claim resumes the SAME attempt via retry-grade instead of refusing forever,
/// while a fresh claim still gets the controlled in-progress response.
/// </summary>
public sealed class WritingRetryGradeLeaseTests
{
    private const string CanonicalCompletion = """
        {
          "findings": [],
          "criteriaScores": { "purpose": 3, "content": 6, "conciseness_clarity": 5, "genre_style": 7, "organisation_layout": 4, "language": 6 },
          "estimatedScaledScore": 380,
          "estimatedGrade": "B"
        }
        """;

    [Fact]
    public async Task RetryGrade_RecoversStuckGrading_WithExpiredClaim()
    {
        await using var db = NewDb();
        var submissionId = Guid.NewGuid();
        db.WritingSubmissions.Add(new WritingSubmission
        {
            Id = submissionId,
            UserId = "learner-1",
            ScenarioId = ScenarioId,
            Mode = "practice",
            LetterContent = "Dear Dr Smith, I am writing to refer Mr Jones for assessment and ongoing management.",
            LetterContentHash = "hash-stuck-1",
            WordCount = 20,
            Status = WritingSubmissionStatuses.Grading,
            ClaimedAt = DateTimeOffset.UtcNow - TimeSpan.FromHours(1),
            ClaimOwner = "dead-worker",
            GradingTier = "express",
            InputSource = "typed",
            StartedAt = DateTimeOffset.UtcNow,
            SubmittedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        var service = BuildService(db);

        var outcome = await service.RetryGradeAsync("learner-1", submissionId, default);

        Assert.Equal(submissionId, outcome.SubmissionId);
        Assert.False(outcome.IdempotentReuse);
        Assert.Equal("graded", (await db.WritingSubmissions.AsNoTracking().FirstAsync(s => s.Id == submissionId)).Status);
        Assert.Equal(1, await db.WritingGrades.CountAsync(g => g.SubmissionId == submissionId));
    }

    [Fact]
    public async Task RetryGrade_RejectsStuckGrading_WithFreshClaim()
    {
        await using var db = NewDb();
        var submissionId = Guid.NewGuid();
        db.WritingSubmissions.Add(new WritingSubmission
        {
            Id = submissionId,
            UserId = "learner-1",
            ScenarioId = ScenarioId,
            Mode = "practice",
            LetterContent = "Dear Dr Smith, I am writing to refer Mr Jones for assessment and ongoing management.",
            LetterContentHash = "hash-fresh-1",
            WordCount = 20,
            Status = WritingSubmissionStatuses.Grading,
            ClaimedAt = DateTimeOffset.UtcNow - TimeSpan.FromMinutes(1),
            ClaimOwner = "live-worker",
            GradingTier = "express",
            InputSource = "typed",
            StartedAt = DateTimeOffset.UtcNow,
            SubmittedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        var service = BuildService(db);

        var ex = await Assert.ThrowsAsync<OetLearner.Api.Services.ApiException>(
            () => service.RetryGradeAsync("learner-1", submissionId, default));
        Assert.Equal("writing_rubric_already_in_progress", ex.ErrorCode);
        Assert.Equal(0, await db.WritingGrades.CountAsync());
    }

    private static WritingSubmissionService BuildService(LearnerDbContext db)
    {
        var pipeline = new WritingSubmissionEvaluationPipeline(
            db,
            new CountingGateway(),
            new EmptyCanonEngine(),
            mistakeService: null!,
            events: new NoopWritingEventBus(),
            TimeProvider.System,
            TestRuntimeSettingsProvider.FromWritingOptions(new WritingV2Options()),
            NullLogger<WritingSubmissionEvaluationPipeline>.Instance,
            assessmentPreflight: new PassThroughPreflight(),
            creditReservations: new CountingReservations());
        return new WritingSubmissionService(
            db,
            pipeline,
            NullLogger<WritingSubmissionService>.Instance,
            new EmptyHighlightStore());
    }

    private static LearnerDbContext NewDb()
    {
        var db = new LearnerDbContext(
            new DbContextOptionsBuilder<LearnerDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                .Options);
        // Grading resolves profession/letter-type from the owning scenario
        // (fail-closed when absent).
        db.WritingScenarios.Add(new WritingScenario
        {
            Id = ScenarioId,
            Title = "Harness task",
            Profession = "medicine",
            LetterType = "routine_referral",
            Status = "published",
            AuthorId = "admin-1",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        db.SaveChanges();
        return db;
    }

    private static readonly Guid ScenarioId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

    private sealed class CountingGateway : IAiGatewayService
    {
        public AiGroundedPrompt BuildGroundedPrompt(AiGroundingContext context)
            => new()
            {
                SystemPrompt = "# OET AI — Rulebook-Grounded System Prompt\n**This call concerns WRITING**",
                TaskInstruction = "score",
            };

        public Task<AiGatewayResult> CompleteAsync(AiGatewayRequest request, CancellationToken ct = default)
            => Task.FromResult(new AiGatewayResult { Completion = CanonicalCompletion, ResolvedModel = "claude-sonnet-5" });
    }

    private sealed class CountingReservations : IAiCreditReservationService
    {
        public Task<AiCreditReservationTicket> ReserveWritingAsync(
            string userId, string operationId, string businessReference, CancellationToken ct)
            => Task.FromResult(new AiCreditReservationTicket(
                "res-1", operationId, "writing", 1, AiCreditReservationState.Reserved, false));

        public Task<AiCreditReservationTicket> ReserveSpeakingAsync(
            string userId, string operationId, string businessReference, CancellationToken ct)
            => ReserveWritingAsync(userId, operationId, businessReference, ct);

        public Task CommitAsync(string reservationId, CancellationToken ct) => Task.CompletedTask;
        public Task CommitByBusinessReferenceAsync(string businessReference, CancellationToken ct) => Task.CompletedTask;
        public Task ReleaseAsync(string reservationId, CancellationToken ct) => Task.CompletedTask;
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

    private sealed class PassThroughPreflight : IWritingAssessmentPreflightService
    {
        public Task<WritingAssessmentPreflightResult> ValidateAsync(WritingSubmission submission, CancellationToken ct)
            => Task.FromResult(new WritingAssessmentPreflightResult(
                true, WritingAssessmentV11Status.CandidateReady,
                Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(),
                "medicine", "routine_referral", "test", "task", "Patient name: John Jones\nAge: 54"));
    }

    private sealed class EmptyHighlightStore : IWritingCaseNoteHighlightService
    {
        public Task<string> GetAsync(string userId, Guid scenarioId, CancellationToken ct) => Task.FromResult("{}");
        public Task<string> SaveAsync(string userId, Guid scenarioId, string highlightsJson, CancellationToken ct) => Task.FromResult(highlightsJson);
    }
}
