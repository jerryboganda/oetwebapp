using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Writing;

namespace OetLearner.Api.Tests;

public class WritingDualAssessmentServiceTests
{
    private const string OwnerUser = "learner-1";
    private const string EvaluationId = "eval-1";
    private const string AttemptId = "att-1";

    private static (LearnerDbContext db, WritingDualAssessmentService svc) Build()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var db = new LearnerDbContext(options);
        return (db, new WritingDualAssessmentService(db));
    }

    private static async Task SeedAttemptAndEvaluationAsync(
        LearnerDbContext db,
        string criterionScoresJson = "[{\"criterionCode\":\"purpose\",\"score\":2,\"maxScore\":3},{\"criterionCode\":\"content\",\"score\":5,\"maxScore\":7}]",
        string userId = OwnerUser)
    {
        db.Attempts.Add(new Attempt
        {
            Id = AttemptId,
            UserId = userId,
            ContentId = "paper-1",
            SubtestCode = "writing",
            State = AttemptState.Submitted,
            StartedAt = DateTimeOffset.UtcNow,
            SubmittedAt = DateTimeOffset.UtcNow,
            Context = "{}",
            Mode = "exam",
        });
        db.Evaluations.Add(new Evaluation
        {
            Id = EvaluationId,
            AttemptId = AttemptId,
            SubtestCode = "writing",
            State = AsyncState.Completed,
            ScoreRange = "350",
            GradeRange = "B",
            CriterionScoresJson = criterionScoresJson,
            ConfidenceBand = ConfidenceBand.High,
            LearnerDisclaimer = "Practice estimate only.",
            ModelExplanationSafe = "Model output.",
            CreatedAt = DateTimeOffset.UtcNow,
            GeneratedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    private static async Task SeedSubmittedDraftAsync(
        LearnerDbContext db,
        string rubricEntriesJson = "{\"purpose\":3,\"content\":6,\"conciseness\":5,\"genre\":6,\"organization\":5,\"language\":6}",
        string reviewerId = "expert-1",
        string tutorDisplayName = "Dr Smith")
    {
        var requestId = "rr-1";
        db.ReviewRequests.Add(new ReviewRequest
        {
            Id = requestId,
            AttemptId = AttemptId,
            SubtestCode = "writing",
            State = ReviewRequestState.Submitted,
            TurnaroundOption = "standard",
            PaymentSource = "wallet",
            PriceSnapshot = 25m,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        db.ExpertUsers.Add(new ExpertUser
        {
            Id = reviewerId,
            AuthAccountId = "auth-expert-1",
            DisplayName = tutorDisplayName,
            Email = "expert@example.com",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        db.ExpertReviewDrafts.Add(new ExpertReviewDraft
        {
            Id = "erd-1",
            ReviewRequestId = requestId,
            ReviewerId = reviewerId,
            State = "submitted",
            RubricEntriesJson = rubricEntriesJson,
            FinalCommentDraft = "Strong purpose; tighten conciseness next round.",
            DraftSavedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Returns_ai_only_when_no_expert_review()
    {
        var (db, svc) = Build();
        await SeedAttemptAndEvaluationAsync(db);

        var result = await svc.GetAsync(OwnerUser, EvaluationId, default);

        Assert.NotNull(result);
        Assert.NotNull(result!.Ai);
        Assert.Null(result.Tutor);
        Assert.Null(result.Divergence);
        Assert.Equal(2, result.Ai.CriterionScores["purpose"].Score);
        Assert.Equal(3, result.Ai.CriterionScores["purpose"].MaxScore);
        Assert.True(result.Ai.IsAdvisory);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Returns_both_with_divergence_when_tutor_submitted()
    {
        var (db, svc) = Build();
        await SeedAttemptAndEvaluationAsync(db);
        await SeedSubmittedDraftAsync(db);

        var result = await svc.GetAsync(OwnerUser, EvaluationId, default);

        Assert.NotNull(result);
        Assert.NotNull(result!.Tutor);
        Assert.Equal("Dr Smith", result.Tutor!.TutorName);
        Assert.Equal(3, result.Tutor.CriterionScores["purpose"].Score);
        Assert.NotNull(result.Divergence);
        // AI purpose=2, tutor=3 → delta +1.
        Assert.Equal(1, result.Divergence!.PerCriterion["purpose"]);
        Assert.Contains(result.Divergence.AgreementBand, new[] { "close", "moderate", "wide" });
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Hides_in_progress_expert_drafts()
    {
        var (db, svc) = Build();
        await SeedAttemptAndEvaluationAsync(db);
        // Insert a draft that has NOT been submitted yet.
        db.ReviewRequests.Add(new ReviewRequest
        {
            Id = "rr-1",
            AttemptId = AttemptId,
            SubtestCode = "writing",
            State = ReviewRequestState.Submitted,
            TurnaroundOption = "standard",
            PaymentSource = "wallet",
            PriceSnapshot = 25m,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        db.ExpertUsers.Add(new ExpertUser
        {
            Id = "expert-1",
            AuthAccountId = "auth-x",
            DisplayName = "X",
            Email = "x@x",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        db.ExpertReviewDrafts.Add(new ExpertReviewDraft
        {
            Id = "erd-1",
            ReviewRequestId = "rr-1",
            ReviewerId = "expert-1",
            State = "editing",
            RubricEntriesJson = "{\"purpose\":3}",
            DraftSavedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        var result = await svc.GetAsync(OwnerUser, EvaluationId, default);
        Assert.NotNull(result);
        Assert.Null(result!.Tutor);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Forbids_access_to_another_users_evaluation()
    {
        var (db, svc) = Build();
        await SeedAttemptAndEvaluationAsync(db, userId: "other-user");

        var result = await svc.GetAsync(OwnerUser, EvaluationId, default);
        Assert.Null(result);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Returns_null_when_evaluation_does_not_exist()
    {
        var (db, svc) = Build();
        var result = await svc.GetAsync(OwnerUser, "not-a-real-eval", default);
        Assert.Null(result);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Fills_missing_canonical_criteria_with_zero()
    {
        var (db, svc) = Build();
        // Only purpose is in the AI JSON; the other 5 must still appear in the response.
        await SeedAttemptAndEvaluationAsync(db,
            criterionScoresJson: "[{\"criterionCode\":\"purpose\",\"score\":2,\"maxScore\":3}]");

        var result = await svc.GetAsync(OwnerUser, EvaluationId, default);
        Assert.NotNull(result);
        Assert.Equal(6, result!.Ai.CriterionScores.Count);
        Assert.Equal(0, result.Ai.CriterionScores["content"].Score);
        Assert.Equal(7, result.Ai.CriterionScores["content"].MaxScore);
        Assert.Equal(3, result.Ai.CriterionScores["purpose"].MaxScore);
        await db.DisposeAsync();
    }
}
