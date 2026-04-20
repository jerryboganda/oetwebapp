using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;

namespace OetLearner.Api.Tests;

/// <summary>
/// MISSION CRITICAL tests for the Review module seeder (docs/REVIEW-MODULE.md).
/// Covers idempotency, severity gating, source-type coverage, and batch seeding.
/// </summary>
public class ReviewItemSeederTests
{
    private const string UserId = "user-1";

    private static (LearnerDbContext db, ReviewItemSeeder seeder) Build()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var db = new LearnerDbContext(options);
        var seeder = new ReviewItemSeeder(db, NullLogger<ReviewItemSeeder>.Instance);
        return (db, seeder);
    }

    [Fact]
    public async Task SeedGrammarError_creates_a_review_item_with_stable_source_id()
    {
        var (db, seeder) = Build();

        var result = await seeder.SeedGrammarErrorAsync(
            userId: UserId,
            examTypeCode: "oet",
            lessonId: "gl-1",
            exerciseId: "ex-5",
            title: "Past perfect",
            questionText: "Choose the correct tense.",
            correctAnswer: "had arrived",
            explanation: "Past perfect for sequence.",
            exerciseType: "fill_blank",
            ct: default);

        Assert.True(result.Created);
        var row = await db.ReviewItems.SingleAsync();
        Assert.Equal(ReviewSourceTypes.GrammarError, row.SourceType);
        Assert.Equal("gl-1:ex-5", row.SourceId);
        Assert.Equal("grammar", row.SubtestCode);
        Assert.Equal("fill_blank", row.CriterionCode);
        Assert.Equal("grammar", row.PromptKind);
        Assert.Equal("active", row.Status);
        Assert.Equal("Past perfect", row.Title);
    }

    [Fact]
    public async Task SeedGrammarError_is_idempotent_on_duplicate_calls()
    {
        var (db, seeder) = Build();

        var first = await seeder.SeedGrammarErrorAsync(UserId, "oet", "gl-1", "ex-5",
            "Title", "Q", "A", "E", "fill_blank", default);
        var second = await seeder.SeedGrammarErrorAsync(UserId, "oet", "gl-1", "ex-5",
            "Title", "Q", "A", "E", "fill_blank", default);

        Assert.True(first.Created);
        Assert.False(second.Created);
        Assert.Equal(first.Id, second.Id);
        Assert.Single(db.ReviewItems);
    }

    [Fact]
    public async Task SeedReadingMiss_and_ListeningMiss_produce_distinct_rows()
    {
        var (db, seeder) = Build();

        await seeder.SeedReadingMissAsync(UserId, "oet", "paper-1", "q-1",
            "Q1", "What is X?", "the correct answer", "Explanation", "A", default);
        await seeder.SeedListeningMissAsync(UserId, "oet", "attempt-1", "q-1",
            "Q1", "What did she say?", "twice weekly", "Patient: twice a week", default);

        var rows = await db.ReviewItems.ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, r => r.SourceType == ReviewSourceTypes.ReadingMiss && r.SourceId == "paper-1:q-1");
        Assert.Contains(rows, r => r.SourceType == ReviewSourceTypes.ListeningMiss && r.SourceId == "attempt-1:q-1");
    }

    [Theory]
    [InlineData("high", true)]
    [InlineData("medium", true)]
    [InlineData("critical", true)]
    [InlineData("low", false)]
    [InlineData("info", false)]
    [InlineData(null, false)]
    [InlineData("", false)]
    public async Task SeedWritingIssue_enforces_medium_or_high_severity(string? severity, bool shouldSeed)
    {
        var (db, seeder) = Build();

        var result = await seeder.SeedWritingIssueAsync(
            userId: UserId,
            examTypeCode: "oet",
            evaluationId: "eval-1",
            feedbackItemId: "fb-1",
            criterionCode: "conciseness",
            message: "Trim the opening.",
            severity: severity,
            suggestedFix: "Drop the filler sentence.",
            anchorSnippet: "In this case, as we discussed...",
            ct: default);

        if (shouldSeed)
        {
            Assert.NotNull(result);
            Assert.True(result!.Created);
            Assert.Single(db.ReviewItems);
        }
        else
        {
            Assert.Null(result);
            Assert.Empty(db.ReviewItems);
        }
    }

    [Fact]
    public async Task SeedSpeakingIssue_stores_rich_drill_prompt_context()
    {
        var (db, seeder) = Build();

        var result = await seeder.SeedSpeakingIssueAsync(
            userId: UserId,
            examTypeCode: "oet",
            evaluationId: "eval-2",
            feedbackItemId: "fb-2",
            criterionCode: "fluency",
            message: "Filler words weaken fluency.",
            severity: "medium",
            suggestedFix: "Start with the patient action.",
            transcriptLineId: "t2",
            drillPrompt: "Restate without filler.",
            ct: default);

        Assert.NotNull(result);
        var row = await db.ReviewItems.SingleAsync();
        Assert.Equal(ReviewSourceTypes.SpeakingIssue, row.SourceType);
        Assert.Equal("eval-2:fb-2", row.SourceId);
        Assert.Equal("speaking_issue", row.PromptKind);
        Assert.Contains("t2", row.RichContentJson!);
        Assert.Contains("Restate without filler", row.RichContentJson!);
    }

    [Fact]
    public async Task SeedPronunciationFinding_creates_phoneme_scoped_row()
    {
        var (db, seeder) = Build();

        await seeder.SeedPronunciationFindingAsync(
            userId: UserId,
            examTypeCode: "oet",
            attemptId: "att-1",
            phonemeKey: "theta",
            title: "/θ/ — 55/100",
            phoneme: "θ",
            ruleId: "P01.1",
            tip: "Place tongue between teeth.",
            score: 55.4,
            ct: default);

        var row = await db.ReviewItems.SingleAsync();
        Assert.Equal(ReviewSourceTypes.PronunciationFinding, row.SourceType);
        Assert.Equal("att-1:theta", row.SourceId);
        Assert.Equal("speaking", row.SubtestCode);
        Assert.Equal("P01.1", row.CriterionCode);
        Assert.Equal("pronunciation", row.PromptKind);
    }

    [Fact]
    public async Task SeedMockMiss_tags_with_subtest_and_section()
    {
        var (db, seeder) = Build();

        await seeder.SeedMockMissAsync(
            userId: UserId,
            examTypeCode: "oet",
            mockReportId: "mock-1",
            sectionCode: "writing",
            questionId: "weakest",
            subtestCode: "writing",
            title: "Mock weak area",
            questionText: "Trim secondary details.",
            correctAnswer: "See guide.",
            explanation: "Focus on changed follow-up actions.",
            ct: default);

        var row = await db.ReviewItems.SingleAsync();
        Assert.Equal(ReviewSourceTypes.MockMiss, row.SourceType);
        Assert.Equal("mock-1:writing:weakest", row.SourceId);
        Assert.Equal("writing", row.SubtestCode);
        Assert.Equal("mock_miss", row.PromptKind);
    }

    [Fact]
    public async Task Vocabulary_source_type_is_rejected_at_seed_time()
    {
        var (_, seeder) = Build();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            seeder.SeedAsync(UserId, new ReviewItemSeedRequest(
                ExamTypeCode: "oet",
                SourceType: ReviewSourceTypes.Vocabulary,
                SourceId: "vt-1",
                SubtestCode: "vocabulary",
                CriterionCode: null,
                Title: "should fail",
                PromptKind: "vocabulary",
                QuestionPayload: new { text = "forbidden" },
                AnswerPayload: new { text = "forbidden" },
                RichContent: null), default));
    }

    [Fact]
    public async Task Unknown_source_type_throws()
    {
        var (_, seeder) = Build();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            seeder.SeedAsync(UserId, new ReviewItemSeedRequest(
                ExamTypeCode: "oet",
                SourceType: "not_a_real_source",
                SourceId: "whatever",
                SubtestCode: "general",
                CriterionCode: null,
                Title: "should fail",
                PromptKind: null,
                QuestionPayload: new { text = "x" },
                AnswerPayload: new { text = "y" },
                RichContent: null), default));
    }

    [Fact]
    public async Task SeedBatch_seeds_many_without_duplicating()
    {
        var (db, seeder) = Build();

        var requests = Enumerable.Range(1, 5).Select(i => new ReviewItemSeedRequest(
            ExamTypeCode: "oet",
            SourceType: ReviewSourceTypes.ReadingMiss,
            SourceId: $"paper:q-{i}",
            SubtestCode: "reading",
            CriterionCode: "A",
            Title: $"Q{i}",
            PromptKind: "reading_miss",
            QuestionPayload: new { text = $"Q{i}" },
            AnswerPayload: new { text = $"A{i}" },
            RichContent: null)).ToList();

        var firstRun = await seeder.SeedBatchAsync(UserId, requests, default);
        var secondRun = await seeder.SeedBatchAsync(UserId, requests, default);

        Assert.Equal(5, firstRun.Count(r => r.Created));
        Assert.Equal(0, secondRun.Count(r => r.Created));
        Assert.Equal(5, await db.ReviewItems.CountAsync());
    }
}
