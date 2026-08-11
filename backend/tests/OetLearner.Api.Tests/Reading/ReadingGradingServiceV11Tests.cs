using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Reading;

namespace OetLearner.Api.Tests.Reading;

public sealed class ReadingGradingServiceV11Tests
{
    [Theory]
    [InlineData("acetylsalicylic acid", true)]
    [InlineData("salicylic acid", false)]
    public async Task Part_a_short_answer_uses_only_explicit_authored_variants(
        string learnerAnswer,
        bool expectedCorrect)
    {
        await using var db = NewDb();
        var now = DateTimeOffset.UtcNow;

        var paper = new ContentPaper
        {
            Id = "reading-paper-v11-variants",
            SubtestCode = "reading",
            Title = "Reading Part A variants",
            Slug = "reading-part-a-variants",
            Status = ContentStatus.Published,
            Difficulty = "standard",
            CreatedAt = now,
            UpdatedAt = now,
            ExtractedTextJson = "{}",
        };
        var part = new ReadingPart
        {
            Id = "reading-part-a-v11",
            PaperId = paper.Id,
            PartCode = ReadingPartCode.A,
            TimeLimitMinutes = 15,
            MaxRawScore = 1,
            CreatedAt = now,
            UpdatedAt = now,
        };
        var question = new ReadingQuestion
        {
            Id = "reading-question-a-v11",
            ReadingPartId = part.Id,
            DisplayOrder = 1,
            Points = 1,
            QuestionType = ReadingQuestionType.ShortAnswer,
            Stem = "Medication ____",
            CorrectAnswerJson = "\"aspirin\"",
            AcceptedSynonymsJson = "[\"acetylsalicylic acid\"]",
            CaseSensitive = false,
            ReviewState = ReadingReviewState.Published,
            CreatedAt = now,
            UpdatedAt = now,
        };
        var attempt = new ReadingAttempt
        {
            Id = "reading-attempt-v11",
            UserId = "learner-v11",
            PaperId = paper.Id,
            StartedAt = now,
            LastActivityAt = now,
            Status = ReadingAttemptStatus.InProgress,
            Mode = ReadingAttemptMode.Exam,
            MaxRawScore = 1,
            PolicySnapshotJson = "{}",
        };
        var answer = new ReadingAnswer
        {
            Id = "reading-answer-v11",
            ReadingAttemptId = attempt.Id,
            ReadingQuestionId = question.Id,
            UserAnswerJson = System.Text.Json.JsonSerializer.Serialize(learnerAnswer),
            AnsweredAt = now,
            CreatedAt = now,
            UpdatedAt = now,
        };

        db.ContentPapers.Add(paper);
        db.ReadingParts.Add(part);
        db.ReadingQuestions.Add(question);
        db.ReadingAttempts.Add(attempt);
        db.ReadingAnswers.Add(answer);
        db.ReadingPolicies.Add(new ReadingPolicy
        {
            Id = "global",
            ShortAnswerAcceptSynonyms = true,
            PartACaseInsensitive = true,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();

        var policy = new ReadingPolicyService(db, new MemoryCache(new MemoryCacheOptions()));
        var grader = new ReadingGradingService(
            db,
            policy,
            NullLogger<ReadingGradingService>.Instance);

        var result = await grader.GradeAttemptAsync(attempt.Id, CancellationToken.None);

        Assert.Equal(expectedCorrect ? 1 : 0, result.RawScore);
        Assert.Equal(expectedCorrect, Assert.Single(result.Answers).IsCorrect);
        Assert.Null(result.ScaledScore);
    }

    private static LearnerDbContext NewDb() => new(
        new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
