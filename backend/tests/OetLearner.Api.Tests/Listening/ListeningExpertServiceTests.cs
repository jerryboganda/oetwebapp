using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Listening;

namespace OetLearner.Api.Tests.Listening;

public class ListeningExpertServiceTests
{
    private static LearnerDbContext NewDb() => new(
        new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static ListeningExpertService CreateService(LearnerDbContext db) =>
        new(db, NullLogger<ListeningExpertService>.Instance);

    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private static ContentPaper SeedPaper(string id = "paper-1") => new()
    {
        Id = id,
        SubtestCode = "listening",
        Title = "Listening Test Paper",
        Slug = $"listening-{id}",
        Status = ContentStatus.Published,
        Difficulty = "standard",
        CreatedAt = Now,
        UpdatedAt = Now,
        ExtractedTextJson = "{}",
    };

    private static LearnerUser SeedUser(string id = "user-1") => new()
    {
        Id = id,
        DisplayName = "Test Learner",
        Email = "learner@test.com",
        CreatedAt = Now,
        LastActiveAt = Now,
    };

    private static ListeningAttempt SeedAttempt(
        string id = "attempt-1",
        string paperId = "paper-1",
        string userId = "user-1",
        int rawScore = 30,
        int scaledScore = 350) => new()
    {
        Id = id,
        PaperId = paperId,
        UserId = userId,
        Status = ListeningAttemptStatus.Submitted,
        StartedAt = Now.AddMinutes(-40),
        SubmittedAt = Now,
        LastActivityAt = Now,
        RawScore = rawScore,
        MaxRawScore = 42,
        ScaledScore = scaledScore,
        Mode = ListeningAttemptMode.Exam,
    };

    [Fact]
    public async Task SubmitFeedbackAsync_WithRawScoreOverride_RecalculatesScaledViaOetScoring()
    {
        // Arrange
        await using var db = NewDb();
        db.Set<ContentPaper>().Add(SeedPaper());
        db.Users.Add(SeedUser());
        db.ListeningAttempts.Add(SeedAttempt());
        await db.SaveChangesAsync();

        var svc = CreateService(db);
        var req = new ListeningExpertFeedbackRequest(
            OverallFeedback: "Good attempt, adjusted score.",
            PerQuestionFeedback: null,
            RecommendedAreas: null,
            RawScoreOverride: 35,
            ScoreOverrideReason: "Expert reviewed and adjusted");

        // Act
        await svc.SubmitFeedbackAsync("expert-1", "attempt-1", req, CancellationToken.None);

        // Assert
        var attempt = await db.ListeningAttempts.FindAsync("attempt-1");
        Assert.NotNull(attempt);
        Assert.Equal(35, attempt.RawScore);
        Assert.Equal(OetScoring.OetRawToScaled(35), attempt.ScaledScore);
    }

    [Fact]
    public async Task SubmitFeedbackAsync_WithoutRawScoreOverride_DoesNotChangeExistingScores()
    {
        // Arrange
        await using var db = NewDb();
        db.Set<ContentPaper>().Add(SeedPaper());
        db.Users.Add(SeedUser());
        db.ListeningAttempts.Add(SeedAttempt(rawScore: 30, scaledScore: 350));
        await db.SaveChangesAsync();

        var svc = CreateService(db);
        var req = new ListeningExpertFeedbackRequest(
            OverallFeedback: "Feedback without override.",
            PerQuestionFeedback: null,
            RecommendedAreas: null,
            RawScoreOverride: null,
            ScoreOverrideReason: null);

        // Act
        await svc.SubmitFeedbackAsync("expert-1", "attempt-1", req, CancellationToken.None);

        // Assert
        var attempt = await db.ListeningAttempts.FindAsync("attempt-1");
        Assert.NotNull(attempt);
        Assert.Equal(30, attempt.RawScore);
        Assert.Equal(350, attempt.ScaledScore);
    }

    [Fact]
    public async Task GetAttemptsPagedAsync_ReturnsCorrectPagingMetadata()
    {
        // Arrange
        await using var db = NewDb();
        var paper = SeedPaper();
        var user = SeedUser();
        db.Set<ContentPaper>().Add(paper);
        db.Users.Add(user);

        // Seed 5 submitted attempts
        for (var i = 1; i <= 5; i++)
        {
            db.ListeningAttempts.Add(SeedAttempt(
                id: $"attempt-{i}",
                paperId: paper.Id,
                userId: user.Id,
                rawScore: 20 + i,
                scaledScore: OetScoring.OetRawToScaled(20 + i)));
        }
        await db.SaveChangesAsync();

        var svc = CreateService(db);

        // Act — page 1, size 2
        var result = await svc.GetAttemptsPagedAsync(
            "expert-1", page: 1, pageSize: 2, learnerId: null, paperId: null, CancellationToken.None);

        // Assert
        Assert.Equal(5, result.Total);
        Assert.Equal(1, result.Page);
        Assert.Equal(2, result.PageSize);
        Assert.Equal(2, result.Items.Count);
    }

    [Fact]
    public async Task GetReviewBundleAsync_PopulatesDistractorCategorySpeakerAttitudeAndOptionAnalysis()
    {
        // Arrange — a Part C MCQ with an authored speaker attitude, a tagged
        // wrong distractor, and an answer that selected that distractor.
        await using var db = NewDb();
        db.Set<ContentPaper>().Add(SeedPaper());
        db.Users.Add(SeedUser());
        db.ListeningAttempts.Add(SeedAttempt());

        var part = new ListeningPart
        {
            Id = "part-c1",
            PaperId = "paper-1",
            PartCode = ListeningPartCode.C1,
            MaxRawScore = 6,
            CreatedAt = Now,
            UpdatedAt = Now,
        };
        db.Set<ListeningPart>().Add(part);

        var question = new ListeningQuestion
        {
            Id = "q-1",
            PaperId = "paper-1",
            ListeningPartId = part.Id,
            QuestionNumber = 31,
            DisplayOrder = 0,
            Points = 1,
            QuestionType = ListeningQuestionType.MultipleChoice3,
            Stem = "What is the speaker's overall view of the new protocol?",
            CorrectAnswerJson = "\"B\"",
            SpeakerAttitude = ListeningSpeakerAttitude.Doubtful,
            TranscriptEvidenceText = "I'm not convinced it changes much.",
            CreatedAt = Now,
            UpdatedAt = Now,
        };
        db.Set<ListeningQuestion>().Add(question);

        db.Set<ListeningQuestionOption>().AddRange(
            new ListeningQuestionOption
            {
                Id = "opt-a",
                ListeningQuestionId = question.Id,
                OptionKey = "A",
                DisplayOrder = 0,
                Text = "It will transform outcomes.",
                IsCorrect = false,
                DistractorCategory = ListeningDistractorCategory.TooStrong,
                WhyWrongMarkdown = "Overstates the speaker's tentative wording.",
            },
            new ListeningQuestionOption
            {
                Id = "opt-b",
                ListeningQuestionId = question.Id,
                OptionKey = "B",
                DisplayOrder = 1,
                Text = "It makes little practical difference.",
                IsCorrect = true,
            },
            new ListeningQuestionOption
            {
                Id = "opt-c",
                ListeningQuestionId = question.Id,
                OptionKey = "C",
                DisplayOrder = 2,
                Text = "It will harm patients.",
                IsCorrect = false,
                DistractorCategory = ListeningDistractorCategory.OppositeMeaning,
                WhyWrongMarkdown = "The speaker is sceptical, not alarmed.",
            });

        db.ListeningAnswers.Add(new ListeningAnswer
        {
            Id = "ans-1",
            ListeningAttemptId = "attempt-1",
            ListeningQuestionId = question.Id,
            UserAnswerJson = "\"A\"",
            IsCorrect = false,
            PointsEarned = 0,
            SelectedDistractorCategory = ListeningDistractorCategory.TooStrong,
            AnsweredAt = Now,
        });
        await db.SaveChangesAsync();

        var svc = CreateService(db);

        // Act
        var bundle = await svc.GetReviewBundleAsync("expert-1", "attempt-1", CancellationToken.None);

        // Assert
        var item = Assert.Single(bundle.Answers);
        Assert.Equal("too_strong", item.SelectedDistractorCategory);
        Assert.Equal("doubtful", item.SpeakerAttitude);

        Assert.NotNull(item.OptionAnalysis);
        Assert.Equal(3, item.OptionAnalysis!.Count);

        // Ordered by DisplayOrder: A, B, C.
        var optA = item.OptionAnalysis[0];
        Assert.Equal("A", optA.Key);
        Assert.False(optA.IsCorrect);
        Assert.Equal("too_strong", optA.DistractorCategory);
        Assert.Equal("Overstates the speaker's tentative wording.", optA.WhyWrong);

        var optB = item.OptionAnalysis[1];
        Assert.Equal("B", optB.Key);
        Assert.True(optB.IsCorrect);
        Assert.Null(optB.DistractorCategory);
        Assert.Null(optB.WhyWrong);

        var optC = item.OptionAnalysis[2];
        Assert.Equal("C", optC.Key);
        Assert.Equal("opposite_meaning", optC.DistractorCategory);
    }

    [Fact]
    public async Task GetReviewBundleAsync_ShortAnswerItem_LeavesDistractorFieldsNull()
    {
        // Arrange — Part A short-answer item: no options, no speaker attitude,
        // no selected distractor. The new fields must all stay null.
        await using var db = NewDb();
        db.Set<ContentPaper>().Add(SeedPaper());
        db.Users.Add(SeedUser());
        db.ListeningAttempts.Add(SeedAttempt());

        var part = new ListeningPart
        {
            Id = "part-a1",
            PaperId = "paper-1",
            PartCode = ListeningPartCode.A1,
            MaxRawScore = 12,
            CreatedAt = Now,
            UpdatedAt = Now,
        };
        db.Set<ListeningPart>().Add(part);

        var question = new ListeningQuestion
        {
            Id = "q-2",
            PaperId = "paper-1",
            ListeningPartId = part.Id,
            QuestionNumber = 1,
            DisplayOrder = 0,
            Points = 1,
            QuestionType = ListeningQuestionType.ShortAnswer,
            Stem = "Record the dosage prescribed.",
            CorrectAnswerJson = "\"5mg\"",
            CreatedAt = Now,
            UpdatedAt = Now,
        };
        db.Set<ListeningQuestion>().Add(question);

        db.ListeningAnswers.Add(new ListeningAnswer
        {
            Id = "ans-2",
            ListeningAttemptId = "attempt-1",
            ListeningQuestionId = question.Id,
            UserAnswerJson = "\"5 mg\"",
            IsCorrect = true,
            PointsEarned = 1,
            AnsweredAt = Now,
        });
        await db.SaveChangesAsync();

        var svc = CreateService(db);

        // Act
        var bundle = await svc.GetReviewBundleAsync("expert-1", "attempt-1", CancellationToken.None);

        // Assert
        var item = Assert.Single(bundle.Answers);
        Assert.Null(item.SelectedDistractorCategory);
        Assert.Null(item.SpeakerAttitude);
        Assert.Null(item.OptionAnalysis);
    }

    [Fact]
    public async Task SubmitFeedbackAsync_WithRawScoreOverride_Zero_ScaledIsZero()
    {
        // Arrange — edge case: expert overrides to 0
        await using var db = NewDb();
        db.Set<ContentPaper>().Add(SeedPaper());
        db.Users.Add(SeedUser());
        db.ListeningAttempts.Add(SeedAttempt());
        await db.SaveChangesAsync();

        var svc = CreateService(db);
        var req = new ListeningExpertFeedbackRequest(
            OverallFeedback: "Override to zero.",
            PerQuestionFeedback: null,
            RecommendedAreas: null,
            RawScoreOverride: 0,
            ScoreOverrideReason: "Complete re-evaluation");

        // Act
        await svc.SubmitFeedbackAsync("expert-1", "attempt-1", req, CancellationToken.None);

        // Assert
        var attempt = await db.ListeningAttempts.FindAsync("attempt-1");
        Assert.NotNull(attempt);
        Assert.Equal(0, attempt.RawScore);
        Assert.Equal(OetScoring.OetRawToScaled(0), attempt.ScaledScore);
        Assert.Equal(0, attempt.ScaledScore);
    }
}
