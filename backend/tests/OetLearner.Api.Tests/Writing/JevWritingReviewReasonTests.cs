using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Ai.TypeSafe;
using OetLearner.Api.Services.Writing;
using OetLearner.Api.Services.Writing.Configuration;

namespace OetLearner.Api.Tests.Writing;

/// <summary>
/// ITEM 3: the Jev reason a Writing grade was flagged for tutor review is persisted on the tutor
/// assignment (fixed vocabulary, comma-separated, column-width capped) and surfaced through the
/// tutor queue; rows created before the column existed (null) still map.
/// </summary>
public sealed class JevWritingReviewReasonTests
{
    private static readonly Guid ScenarioId = Guid.Parse("cccccccc-dddd-eeee-ffff-000000000001");

    [Fact]
    public void Merge_FirstReasonStaysFirst_AndDuplicatesAreDropped()
    {
        var merged = WritingSubmissionEvaluationPipeline.MergeJevReviewReasons(
            WritingJevReviewReasons.OutcomeFlip,
            [WritingJevReviewReasons.VerifyFlag, WritingJevReviewReasons.OutcomeFlip, WritingJevReviewReasons.VerifyFlag]);

        Assert.Equal("outcome_flip,verify_flag", merged);
    }

    [Fact]
    public void Merge_IgnoresFreeText_AndReturnsNullWhenNothingIsLeft()
    {
        Assert.Null(WritingSubmissionEvaluationPipeline.MergeJevReviewReasons(null, ["the learner cheated", ""]));
        Assert.Equal(
            "guard_block",
            WritingSubmissionEvaluationPipeline.MergeJevReviewReasons(null, ["not-a-code", WritingJevReviewReasons.GuardBlock]));
    }

    [Fact]
    public void Merge_NeverExceedsTheColumnWidth_AndNeverTruncatesACode()
    {
        // All five codes would need 81 characters; start from a 55-character list that leaves no room for the longest one (25 + separator).
        var merged = WritingSubmissionEvaluationPipeline.MergeJevReviewReasons(
            $"{WritingJevReviewReasons.GuardBlock},{WritingJevReviewReasons.CriteriaDivergence},{WritingJevReviewReasons.OutcomeFlip},{WritingJevReviewReasons.VerifyFlag}",
            [WritingJevReviewReasons.FindingValidAlternative]);

        Assert.NotNull(merged);
        Assert.True(merged!.Length <= 64);
        Assert.DoesNotContain(WritingJevReviewReasons.FindingValidAlternative, merged, StringComparison.Ordinal);
        Assert.All(merged.Split(','), code => Assert.Contains(code, new[]
        {
            WritingJevReviewReasons.GuardBlock,
            WritingJevReviewReasons.CriteriaDivergence,
            WritingJevReviewReasons.OutcomeFlip,
            WritingJevReviewReasons.VerifyFlag,
        }));
    }

    [Fact]
    public async Task TutorQueue_ExposesTheReason_AndRowsWithoutOneStillMap()
    {
        await using var db = NewDb();
        var now = DateTimeOffset.UtcNow;
        var flagged = await SeedAssignmentAsync(db, "outcome_flip,verify_flag", now.AddMinutes(-10));
        var legacy = await SeedAssignmentAsync(db, null, now.AddMinutes(-5));
        var service = new WritingTutorReviewService(
            db,
            TimeProvider.System,
            TestRuntimeSettingsProvider.FromWritingOptions(new WritingV2Options()),
            new WritingModerationService(db, NullLogger<WritingModerationService>.Instance),
            NullLogger<WritingTutorReviewService>.Instance);

        var queue = await service.GetTutorQueueAsync("tutor-1", null, CancellationToken.None);

        Assert.Equal(2, queue.Items.Count);
        Assert.Equal("outcome_flip,verify_flag", queue.Items.Single(i => i.SubmissionId == flagged).ReviewReason);
        Assert.Null(queue.Items.Single(i => i.SubmissionId == legacy).ReviewReason);
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
            Title = "Jev review reason task",
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

    private static async Task<Guid> SeedAssignmentAsync(LearnerDbContext db, string? reviewReason, DateTimeOffset claimedAt)
    {
        var submissionId = Guid.NewGuid();
        db.WritingSubmissions.Add(new WritingSubmission
        {
            Id = submissionId,
            UserId = "jev-learner",
            ScenarioId = ScenarioId,
            Mode = "practice",
            LetterContent = "Dear Dr Green, please review Mr Lee.",
            LetterContentHash = $"hash-{submissionId:N}",
            WordCount = 7,
            Status = WritingSubmissionStatuses.Graded,
            GradingTier = "express",
            InputSource = "typed",
            StartedAt = DateTimeOffset.UtcNow,
            SubmittedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        db.WritingTutorReviewAssignments.Add(new WritingTutorReviewAssignment
        {
            Id = Guid.NewGuid(),
            SubmissionId = submissionId,
            TutorId = string.Empty,
            ClaimedAt = claimedAt,
            DueAt = claimedAt.AddHours(24),
            Status = "pending",
            ReviewReason = reviewReason,
        });
        await db.SaveChangesAsync();
        return submissionId;
    }
}
