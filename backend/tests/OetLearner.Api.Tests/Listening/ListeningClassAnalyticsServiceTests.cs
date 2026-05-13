using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Listening;

namespace OetLearner.Api.Tests.Listening;

public class ListeningClassAnalyticsServiceTests
{
    private static LearnerDbContext NewDb() => new(
        new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    [Fact]
    public async Task GetClassAnalyticsAsync_filters_to_owned_class_members()
    {
        await using var db = NewDb();
        var now = DateTimeOffset.UtcNow;
        SeedClass(db, ownerUserId: "teacher-1", classId: "class-1", "learner-1", "learner-2");
        db.ListeningAttempts.AddRange(
            NewAttempt("attempt-1", "learner-1", scaledScore: 350, now),
            NewAttempt("attempt-2", "learner-2", scaledScore: 300, now),
            NewAttempt("attempt-outsider", "learner-outsider", scaledScore: 500, now));
        await db.SaveChangesAsync();

        var result = await new ListeningAnalyticsService(db).GetClassAnalyticsAsync(
            ownerUserId: "teacher-1",
            classId: "class-1",
            days: 30,
            CancellationToken.None);

        Assert.Equal("class-1", result.ClassId);
        Assert.Equal("Listening Class", result.ClassName);
        Assert.Equal(2, result.MemberCount);
        Assert.Equal(2, result.Analytics.CompletedAttempts);
        Assert.Equal(325, result.Analytics.AverageScaledScore);
        Assert.Equal(50, result.Analytics.PercentLikelyPassing);
    }

    [Fact]
    public async Task GetClassAnalyticsAsync_translates_member_join_filters_with_sqlite()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var capture = new SqlCaptureInterceptor();
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(capture)
            .Options;
        await using var db = new LearnerDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var now = DateTimeOffset.UtcNow;
        var outOfWindow = now.AddDays(-45);
        SeedClass(db, ownerUserId: "teacher-1", classId: "class-1", "learner-legacy", "learner-relational");
        db.ContentPapers.AddRange(
            NewContentPaper("paper-legacy", "Legacy listening paper", now),
            NewContentPaper("paper-relational", "Relational listening paper", now),
            NewContentPaper("paper-outsider", "Outsider listening paper", now));
        db.Attempts.AddRange(
            NewLegacyAttempt("legacy-member", "learner-legacy", "paper-legacy", now),
            NewLegacyAttempt("legacy-old-member", "learner-legacy", "paper-legacy", outOfWindow),
            NewLegacyAttempt("legacy-outsider", "learner-outsider", "paper-outsider", now));
        db.Evaluations.AddRange(
            NewEvaluation("eval-legacy-member", "legacy-member", 360, now),
            NewEvaluation("eval-legacy-old-member", "legacy-old-member", 500, outOfWindow),
            NewEvaluation("eval-legacy-outsider", "legacy-outsider", 500, now));
        db.ListeningAttempts.AddRange(
            NewAttempt("relational-member", "learner-relational", scaledScore: 340, now, paperId: "paper-relational"),
            NewAttempt("relational-old-member", "learner-relational", scaledScore: 500, outOfWindow, paperId: "paper-relational"),
            NewAttempt("relational-outsider", "learner-outsider", scaledScore: 500, now, paperId: "paper-outsider"));
        await db.SaveChangesAsync();

        var result = await new ListeningAnalyticsService(db).GetClassAnalyticsAsync(
            ownerUserId: "teacher-1",
            classId: "class-1",
            days: 30,
            CancellationToken.None);

        Assert.Equal(2, result.MemberCount);
        Assert.Equal(2, result.Analytics.CompletedAttempts);
        Assert.Equal(350, result.Analytics.AverageScaledScore);
        Assert.Equal(50, result.Analytics.PercentLikelyPassing);
        Assert.Contains(capture.Commands, sql =>
            sql.Contains("JOIN", StringComparison.OrdinalIgnoreCase)
            && sql.Contains("TeacherClassMembers", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GetClassAnalyticsAsync_hides_class_existence_from_non_owner()
    {
        await using var db = NewDb();
        SeedClass(db, ownerUserId: "teacher-1", classId: "class-1", "learner-1");
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            new ListeningAnalyticsService(db).GetClassAnalyticsAsync(
                ownerUserId: "teacher-2",
                classId: "class-1",
                days: 30,
                CancellationToken.None));
    }

    [Fact]
    public async Task GetClassAnalyticsAsync_returns_empty_payload_for_empty_class()
    {
        await using var db = NewDb();
        SeedClass(db, ownerUserId: "teacher-1", classId: "class-empty");
        await db.SaveChangesAsync();

        var result = await new ListeningAnalyticsService(db).GetClassAnalyticsAsync(
            ownerUserId: "teacher-1",
            classId: "class-empty",
            days: 30,
            CancellationToken.None);

        Assert.Equal("class-empty", result.ClassId);
        Assert.Equal(0, result.MemberCount);
        Assert.Equal(0, result.Analytics.CompletedAttempts);
        Assert.Null(result.Analytics.AverageScaledScore);
        Assert.Empty(result.Analytics.ClassPartAverages);
    }

    [Fact]
    public async Task GetClassAnalyticsAsync_hides_raw_common_misspellings_from_teacher_payload()
    {
        await using var db = NewDb();
        var now = DateTimeOffset.UtcNow;
        SeedClass(db, ownerUserId: "teacher-1", classId: "class-1", "learner-1");
        SeedPartAContent(db, now);
        db.ListeningAttempts.Add(NewAttempt("attempt-1", "learner-1", scaledScore: 200, now, paperId: "paper-a"));
        db.ListeningAnswers.Add(new ListeningAnswer
        {
            Id = "answer-1",
            ListeningAttemptId = "attempt-1",
            ListeningQuestionId = "question-a",
            UserAnswerJson = "\"fiv\"",
            IsCorrect = false,
            PointsEarned = 0,
            AnsweredAt = now,
        });
        await db.SaveChangesAsync();

        var teacherResult = await new ListeningAnalyticsService(db).GetClassAnalyticsAsync(
            ownerUserId: "teacher-1",
            classId: "class-1",
            days: 30,
            CancellationToken.None);
        var adminResult = await new ListeningAnalyticsService(db).GetAdminAnalyticsAsync(30, CancellationToken.None);

        Assert.DoesNotContain(
            teacherResult.Analytics.GetType().GetProperties(),
            property => property.Name == "CommonMisspellings");
        Assert.NotEmpty(adminResult.CommonMisspellings);
    }

    [Fact]
    public async Task GetClassAnalyticsAsync_returns_distractor_counts_without_raw_wrong_answers()
    {
        await using var db = NewDb();
        var now = DateTimeOffset.UtcNow;
        SeedClass(db, ownerUserId: "teacher-1", classId: "class-1", "learner-1");
        SeedPartBContent(db, now);
        db.ListeningAttempts.Add(NewAttempt("attempt-1", "learner-1", scaledScore: 200, now, paperId: "paper-b"));
        db.ListeningAnswers.Add(new ListeningAnswer
        {
            Id = "answer-b",
            ListeningAttemptId = "attempt-1",
            ListeningQuestionId = "question-b",
            UserAnswerJson = "\"patient.email@example.test\"",
            IsCorrect = false,
            PointsEarned = 0,
            AnsweredAt = now,
        });
        await db.SaveChangesAsync();

        var teacherResult = await new ListeningAnalyticsService(db).GetClassAnalyticsAsync(
            ownerUserId: "teacher-1",
            classId: "class-1",
            days: 30,
            CancellationToken.None);
        var adminResult = await new ListeningAnalyticsService(db).GetAdminAnalyticsAsync(30, CancellationToken.None);

        var teacherHeat = Assert.Single(teacherResult.Analytics.DistractorHeat);
        Assert.Equal(1, teacherHeat.WrongAnswerCount);
        Assert.DoesNotContain(
            teacherHeat.GetType().GetProperties(),
            property => property.Name == "WrongAnswerHistogram");

        var adminHeat = Assert.Single(adminResult.DistractorHeat);
        Assert.True(adminHeat.WrongAnswerHistogram.ContainsKey("patient.email@example.test"));
    }

    private static void SeedClass(
        LearnerDbContext db,
        string ownerUserId,
        string classId,
        params string[] memberUserIds)
    {
        var now = DateTimeOffset.UtcNow;
        db.TeacherClasses.Add(new TeacherClass
        {
            Id = classId,
            OwnerUserId = ownerUserId,
            Name = "Listening Class",
            Description = "Test roster",
            CreatedAt = now,
            UpdatedAt = now,
        });

        db.TeacherClassMembers.AddRange(memberUserIds.Select(memberUserId => new TeacherClassMember
        {
            Id = Guid.NewGuid().ToString("N"),
            TeacherClassId = classId,
            UserId = memberUserId,
            AddedAt = now,
        }));
    }

    private static void SeedPartAContent(LearnerDbContext db, DateTimeOffset now)
    {
        db.ContentPapers.Add(new ContentPaper
        {
            Id = "paper-a",
            SubtestCode = "listening",
            Title = "Part A Paper",
            Slug = "part-a-paper",
            Status = ContentStatus.Published,
            Difficulty = "standard",
            CreatedAt = now,
            UpdatedAt = now,
            ExtractedTextJson = "{}",
        });
        db.ListeningParts.Add(new ListeningPart
        {
            Id = "part-a",
            PaperId = "paper-a",
            PartCode = ListeningPartCode.A1,
            MaxRawScore = 1,
            CreatedAt = now,
            UpdatedAt = now,
        });
        db.ListeningExtracts.Add(new ListeningExtract
        {
            Id = "extract-a",
            ListeningPartId = "part-a",
            DisplayOrder = 1,
            Kind = ListeningExtractKind.Consultation,
            Title = "Consultation",
            SpeakersJson = "[]",
            TranscriptSegmentsJson = "[]",
            CreatedAt = now,
            UpdatedAt = now,
        });
        db.ListeningQuestions.Add(new ListeningQuestion
        {
            Id = "question-a",
            PaperId = "paper-a",
            ListeningPartId = "part-a",
            ListeningExtractId = "extract-a",
            QuestionNumber = 1,
            DisplayOrder = 1,
            Points = 1,
            QuestionType = ListeningQuestionType.ShortAnswer,
            Stem = "Medicine ____",
            CorrectAnswerJson = "\"five\"",
            AcceptedSynonymsJson = "[]",
            CaseSensitive = false,
            CreatedAt = now,
            UpdatedAt = now,
        });
    }

    private static void SeedPartBContent(LearnerDbContext db, DateTimeOffset now)
    {
        db.ContentPapers.Add(new ContentPaper
        {
            Id = "paper-b",
            SubtestCode = "listening",
            Title = "Part B Paper",
            Slug = "part-b-paper",
            Status = ContentStatus.Published,
            Difficulty = "standard",
            CreatedAt = now,
            UpdatedAt = now,
            ExtractedTextJson = "{}",
        });
        db.ListeningParts.Add(new ListeningPart
        {
            Id = "part-b",
            PaperId = "paper-b",
            PartCode = ListeningPartCode.B,
            MaxRawScore = 1,
            CreatedAt = now,
            UpdatedAt = now,
        });
        db.ListeningExtracts.Add(new ListeningExtract
        {
            Id = "extract-b",
            ListeningPartId = "part-b",
            DisplayOrder = 1,
            Kind = ListeningExtractKind.Workplace,
            Title = "Workplace extract",
            SpeakersJson = "[]",
            TranscriptSegmentsJson = "[]",
            CreatedAt = now,
            UpdatedAt = now,
        });
        db.ListeningQuestions.Add(new ListeningQuestion
        {
            Id = "question-b",
            PaperId = "paper-b",
            ListeningPartId = "part-b",
            ListeningExtractId = "extract-b",
            QuestionNumber = 8,
            DisplayOrder = 1,
            Points = 1,
            QuestionType = ListeningQuestionType.MultipleChoice3,
            Stem = "What is the speaker's main concern?",
            CorrectAnswerJson = "\"B\"",
            AcceptedSynonymsJson = "[]",
            CaseSensitive = false,
            CreatedAt = now,
            UpdatedAt = now,
        });
        db.ListeningQuestionOptions.AddRange(
            new ListeningQuestionOption
            {
                Id = "option-b-a",
                ListeningQuestionId = "question-b",
                OptionKey = "A",
                DisplayOrder = 1,
                Text = "The appointment time",
                IsCorrect = false,
            },
            new ListeningQuestionOption
            {
                Id = "option-b-b",
                ListeningQuestionId = "question-b",
                OptionKey = "B",
                DisplayOrder = 2,
                Text = "The medication instruction",
                IsCorrect = true,
            },
            new ListeningQuestionOption
            {
                Id = "option-b-c",
                ListeningQuestionId = "question-b",
                OptionKey = "C",
                DisplayOrder = 3,
                Text = "The discharge date",
                IsCorrect = false,
            });
    }

    private static ContentPaper NewContentPaper(string id, string title, DateTimeOffset now)
        => new()
        {
            Id = id,
            SubtestCode = "listening",
            Title = title,
            Slug = id,
            Status = ContentStatus.Published,
            Difficulty = "standard",
            CreatedAt = now,
            UpdatedAt = now,
            ExtractedTextJson = "{}",
        };

    private static Attempt NewLegacyAttempt(
        string id,
        string userId,
        string contentId,
        DateTimeOffset now)
        => new()
        {
            Id = id,
            UserId = userId,
            ContentId = contentId,
            SubtestCode = "listening",
            Context = "exam",
            Mode = "exam",
            State = AttemptState.Submitted,
            StartedAt = now.AddMinutes(-45),
            SubmittedAt = now,
            CompletedAt = now,
            ElapsedSeconds = 2700,
            AnswersJson = "{}",
        };

    private static Evaluation NewEvaluation(
        string id,
        string attemptId,
        int scaledScore,
        DateTimeOffset now)
        => new()
        {
            Id = id,
            AttemptId = attemptId,
            SubtestCode = "listening",
            State = AsyncState.Completed,
            ScoreRange = scaledScore.ToString(System.Globalization.CultureInfo.InvariantCulture),
            GradeRange = null,
            ConfidenceBand = ConfidenceBand.High,
            CriterionScoresJson = $"[{{\"scaledScore\":{scaledScore}}}]",
            GeneratedAt = now,
            ModelExplanationSafe = "test",
            LearnerDisclaimer = "test",
            LastTransitionAt = now,
        };

    private sealed class SqlCaptureInterceptor : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result)
        {
            Commands.Add(command.CommandText);
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }

    private static ListeningAttempt NewAttempt(
        string id,
        string userId,
        int scaledScore,
        DateTimeOffset now,
        string? paperId = null)
        => new()
        {
            Id = id,
            UserId = userId,
            PaperId = paperId ?? $"paper-{id}",
            StartedAt = now.AddMinutes(-45),
            LastActivityAt = now,
            SubmittedAt = now,
            Status = ListeningAttemptStatus.Submitted,
            Mode = ListeningAttemptMode.Exam,
            RawScore = null,
            ScaledScore = scaledScore,
            MaxRawScore = OetLearner.Api.Services.OetScoring.ListeningReadingRawMax,
        };
}