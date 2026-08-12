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

        var variantAudit = await db.AuditEvents.SingleOrDefaultAsync(e =>
            e.Action == "reading.marking.accepted_variant_used");
        if (expectedCorrect)
        {
            Assert.NotNull(variantAudit);
            Assert.Contains("acetylsalicylic acid", variantAudit!.Details);
            Assert.Contains(attempt.Id, variantAudit.Details);
        }
        else
        {
            Assert.Null(variantAudit);
        }
    }

    [Fact]
    public async Task Multiple_selection_for_single_answer_mcq_is_held_for_admin_review()
    {
        await using var db = NewDb();
        var now = DateTimeOffset.UtcNow;
        var paper = new ContentPaper
        {
            Id = "reading-paper-v11-multi",
            SubtestCode = "reading",
            Title = "Reading multiple-selection hold",
            Slug = "reading-multiple-selection-hold",
            Status = ContentStatus.Published,
            Difficulty = "standard",
            CreatedAt = now,
            UpdatedAt = now,
            ExtractedTextJson = "{}",
        };
        var part = new ReadingPart
        {
            Id = "reading-part-b-v11-multi",
            PaperId = paper.Id,
            PartCode = ReadingPartCode.B,
            TimeLimitMinutes = 45,
            MaxRawScore = 1,
            CreatedAt = now,
            UpdatedAt = now,
        };
        var question = new ReadingQuestion
        {
            Id = "reading-question-b-v11-multi",
            ReadingPartId = part.Id,
            DisplayOrder = 21,
            Points = 1,
            QuestionType = ReadingQuestionType.MultipleChoice3,
            Stem = "Choose one answer",
            OptionsJson = "[{\"key\":\"A\"},{\"key\":\"B\"},{\"key\":\"C\"}]",
            CorrectAnswerJson = "\"A\"",
            ReviewState = ReadingReviewState.Published,
            CreatedAt = now,
            UpdatedAt = now,
        };
        var attempt = new ReadingAttempt
        {
            Id = "reading-attempt-v11-multi",
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
            Id = "reading-answer-v11-multi",
            ReadingAttemptId = attempt.Id,
            ReadingQuestionId = question.Id,
            UserAnswerJson = "[\"A\",\"B\"]",
            AnsweredAt = now,
            CreatedAt = now,
            UpdatedAt = now,
        };

        db.ContentPapers.Add(paper);
        db.ReadingParts.Add(part);
        db.ReadingQuestions.Add(question);
        db.ReadingAttempts.Add(attempt);
        db.ReadingAnswers.Add(answer);
        db.ReadingPolicies.Add(new ReadingPolicy { Id = "global", UpdatedAt = now });
        await db.SaveChangesAsync();

        var grader = new ReadingGradingService(
            db,
            new ReadingPolicyService(db, new MemoryCache(new MemoryCacheOptions())),
            NullLogger<ReadingGradingService>.Instance);

        var result = await grader.GradeAttemptAsync(attempt.Id, CancellationToken.None);
        var saved = await db.ReadingAttempts.SingleAsync(a => a.Id == attempt.Id);
        var savedAnswer = await db.ReadingAnswers.SingleAsync(a => a.Id == answer.Id);

        Assert.True(saved.RequiresAdminReview);
        Assert.Equal("multiple_selections_for_single_answer_mcq", saved.AdminReviewReason);
        Assert.Null(savedAnswer.IsCorrect);
        Assert.Equal(0, savedAnswer.PointsEarned);
        Assert.Null(savedAnswer.SelectedDistractorCategory);
        Assert.Equal("multiple_selection_review_required", savedAnswer.MissReason);
        Assert.Null(saved.ScaledScore);
        Assert.Null(saved.ScoreConversionTableId);
        Assert.Equal(0, result.IncorrectCount);
        Assert.Equal(1, result.InvalidCount);
        Assert.Single(result.Answers);
        Assert.True(result.Answers[0].IsInvalid);
        Assert.Empty(await db.ReadingErrorBankEntries
            .Where(entry => entry.ReadingQuestionId == question.Id)
            .ToListAsync());

        var tutor = new ReadingTutorService(
            db,
            grader,
            NullLogger<ReadingTutorService>.Instance);
        var privileged = await tutor.GetPrivilegedReviewAsync(attempt.Id, CancellationToken.None);
        Assert.NotNull(privileged);
        Assert.True(privileged!.RequiresAdminReview);
        Assert.Equal("multiple_selections_for_single_answer_mcq", privileged.AdminReviewReason);
        Assert.Equal(1, privileged.InvalidCount);
        Assert.Equal(1, privileged.Sections.Single().InvalidCount);
        Assert.Equal(0, privileged.Sections.Single().IncorrectCount);
        Assert.True(privileged.Questions.Single().IsInvalid);
        Assert.Null(privileged.GradedScaledScore);
        Assert.Equal("multiple_selection_review_required", result.ScoreConversionErrorCode);
        Assert.Contains(
            await db.AuditEvents.ToListAsync(),
            e => e.Action == "reading.mcq.multiple_selection_review_required");
    }

    [Fact]
    public async Task Unknown_question_type_is_held_for_admin_review()
    {
        await using var db = NewDb();
        var now = DateTimeOffset.UtcNow;
        var paper = new ContentPaper
        {
            Id = "reading-paper-v11-integrity",
            SubtestCode = "reading",
            Title = "Reading question integrity hold",
            Slug = "reading-question-integrity-hold",
            Status = ContentStatus.Published,
            Difficulty = "standard",
            CreatedAt = now,
            UpdatedAt = now,
            ExtractedTextJson = "{}",
        };
        var part = new ReadingPart
        {
            Id = "reading-part-v11-integrity",
            PaperId = paper.Id,
            PartCode = ReadingPartCode.B,
            TimeLimitMinutes = 45,
            MaxRawScore = 1,
            CreatedAt = now,
            UpdatedAt = now,
        };
        var question = new ReadingQuestion
        {
            Id = "reading-question-v11-integrity",
            ReadingPartId = part.Id,
            DisplayOrder = 21,
            Points = 1,
            QuestionType = (ReadingQuestionType)999,
            Stem = "Corrupt question type",
            CorrectAnswerJson = "\"A\"",
            ReviewState = ReadingReviewState.Published,
            CreatedAt = now,
            UpdatedAt = now,
        };
        var attempt = new ReadingAttempt
        {
            Id = "reading-attempt-v11-integrity",
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
            Id = "reading-answer-v11-integrity",
            ReadingAttemptId = attempt.Id,
            ReadingQuestionId = question.Id,
            UserAnswerJson = "\"A\"",
            AnsweredAt = now,
            CreatedAt = now,
            UpdatedAt = now,
        };

        db.ContentPapers.Add(paper);
        db.ReadingParts.Add(part);
        db.ReadingQuestions.Add(question);
        db.ReadingAttempts.Add(attempt);
        db.ReadingAnswers.Add(answer);
        db.ReadingPolicies.Add(new ReadingPolicy { Id = "global", UpdatedAt = now });
        await db.SaveChangesAsync();

        var grader = new ReadingGradingService(
            db,
            new ReadingPolicyService(db, new MemoryCache(new MemoryCacheOptions())),
            NullLogger<ReadingGradingService>.Instance);

        var result = await grader.GradeAttemptAsync(attempt.Id, CancellationToken.None);
        var saved = await db.ReadingAttempts.SingleAsync(a => a.Id == attempt.Id);
        var savedAnswer = await db.ReadingAnswers.SingleAsync(a => a.Id == answer.Id);

        Assert.True(saved.RequiresAdminReview);
        Assert.Equal(ReadingGradingService.QuestionIntegrityReviewReason, saved.AdminReviewReason);
        Assert.Null(savedAnswer.IsCorrect);
        Assert.Equal(0, savedAnswer.PointsEarned);
        Assert.Equal(ReadingGradingService.QuestionIntegrityReviewReason, savedAnswer.MissReason);
        Assert.Equal(0, result.RawScore);
        Assert.Equal(0, result.IncorrectCount);
        Assert.Equal(1, result.InvalidCount);
        Assert.Null(result.ScaledScore);
        Assert.Equal(ReadingGradingService.QuestionIntegrityReviewReason, result.ScoreConversionErrorCode);
        Assert.Empty(await db.ReadingErrorBankEntries
            .Where(entry => entry.ReadingQuestionId == question.Id)
            .ToListAsync());
        Assert.Contains(
            await db.AuditEvents.ToListAsync(),
            e => e.Action == "reading.question.integrity_review_required");
    }

    private static LearnerDbContext NewDb() => new(
        new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
