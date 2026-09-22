using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Ai;
using OetLearner.Api.Services.Writing;
using OetLearner.Api.Services.Writing.Configuration;
using OetLearner.Api.Services.Writing.Events;

namespace OetLearner.Api.Tests.Writing;

/// <summary>
/// CRITICAL SECURITY FIX (22 Sep 2026 handoff, item 2) — before this fix,
/// WritingSubmissionService.CreateSubmissionAsync had no profession check at
/// all: any learner could submit (and be AI-graded and charged for) any OTHER
/// profession's Writing task by id.
/// </summary>
public sealed class WritingSubmissionProfessionIsolationTests
{
    private static readonly Guid MedicineScenarioId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

    [Fact]
    public async Task CreateSubmission_ForAnotherProfessionsScenario_Is404()
    {
        await using var db = NewDb();
        db.Users.Add(new LearnerUser
        {
            Id = "nursing-learner",
            DisplayName = "Nursing Learner",
            Email = "nursing-learner@example.test",
            ActiveProfessionId = "nursing",
            AccountStatus = "active",
            CreatedAt = DateTimeOffset.UtcNow,
            LastActiveAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
        var service = BuildService(db);

        var ex = await Assert.ThrowsAsync<ApiException>(() => service.CreateSubmissionAsync(
            "nursing-learner", SampleRequest(MedicineScenarioId, "cross-profession-1"), default));

        Assert.Equal("writing_scenario_not_found", ex.ErrorCode);
        Assert.Empty(db.WritingSubmissions);
    }

    [Fact]
    public async Task CreateSubmission_WithNoAccountProfession_Is404()
    {
        await using var db = NewDb();
        db.Users.Add(new LearnerUser
        {
            Id = "no-profession-learner",
            DisplayName = "No Profession Learner",
            Email = "no-profession-learner@example.test",
            ActiveProfessionId = null,
            AccountStatus = "active",
            CreatedAt = DateTimeOffset.UtcNow,
            LastActiveAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
        var service = BuildService(db);

        var ex = await Assert.ThrowsAsync<ApiException>(() => service.CreateSubmissionAsync(
            "no-profession-learner", SampleRequest(MedicineScenarioId, "no-profession-1"), default));

        Assert.Equal("writing_scenario_not_found", ex.ErrorCode);
    }

    [Fact]
    public async Task CreateSubmission_ForTheLearnersOwnProfession_Succeeds()
    {
        await using var db = NewDb();
        db.Users.Add(new LearnerUser
        {
            Id = "medicine-learner",
            DisplayName = "Medicine Learner",
            Email = "medicine-learner@example.test",
            ActiveProfessionId = "medicine",
            AccountStatus = "active",
            CreatedAt = DateTimeOffset.UtcNow,
            LastActiveAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
        var service = BuildService(db);

        var response = await service.CreateSubmissionAsync(
            "medicine-learner", SampleRequest(MedicineScenarioId, "same-profession-1"), default);

        Assert.NotEqual(Guid.Empty, response.Id);
        Assert.Single(db.WritingSubmissions);
    }

    private static WritingSubmissionCreateRequest SampleRequest(Guid scenarioId, string key) => new(
        ScenarioId: scenarioId,
        Mode: "practice",
        LetterContent: "Dear Dr Smith, I am writing to refer Mr Jones for assessment and ongoing management.",
        WordCount: 15,
        TimeSpentSeconds: 120,
        InputSource: "typed",
        SimulationMode: null,
        CaseNoteHighlightsJson: null,
        IdempotencyKey: key);

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
        db.WritingScenarios.Add(new WritingScenario
        {
            Id = MedicineScenarioId,
            Title = "Harness task",
            Profession = "medicine",
            LetterType = "routine_referral",
            TaskPromptMarkdown = "Write to Dr Green requesting a review.",
            Status = "published",
            AuthorId = "admin-1",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        db.WritingScenarioStructuredSentences.Add(new WritingScenarioStructuredSentence
        {
            Id = Guid.NewGuid(),
            ScenarioId = MedicineScenarioId,
            Ordinal = 1,
            SentenceText = "Asthma; allergy status negative.",
            RelevanceLabel = "relevant",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        db.SaveChanges();
        return db;
    }

    private const string CanonicalCompletion = """
        {
          "findings": [],
          "criteriaScores": { "purpose": 3, "content": 6, "conciseness_clarity": 5, "genre_style": 7, "organisation_layout": 4, "language": 6 },
          "estimatedScaledScore": 380,
          "estimatedGrade": "B"
        }
        """;

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
