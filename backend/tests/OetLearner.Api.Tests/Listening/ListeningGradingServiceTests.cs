using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Listening;

namespace OetLearner.Api.Tests.Listening;

/// <summary>
/// Listening V2 — mission-critical raw→scaled invariant test. The new
/// <see cref="ListeningGradingService"/> MUST route every conversion through
/// <see cref="OetScoring.OetRawToScaled"/>. This test seeds an attempt with
/// known rawScore, grades it, and asserts the persisted scaledScore equals
/// what <c>OetScoring</c> returns for that raw.
/// </summary>
public class ListeningGradingServiceTests
{
    private static LearnerDbContext NewDb() => new(
        new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    [Fact]
    public async Task GradeAsync_routes_raw_to_scaled_via_OetScoring()
    {
        await using var db = NewDb();
        var now = DateTimeOffset.UtcNow;

        var paper = new ContentPaper
        {
            Id = "paper-grading-1",
            SubtestCode = "listening",
            Title = "T",
            Slug = "t",
            Status = ContentStatus.Published,
            Difficulty = "standard",
            CreatedAt = now,
            UpdatedAt = now,
            ExtractedTextJson = "{}",
        };
        var part = new ListeningPart
        {
            Id = "p-a1",
            PaperId = paper.Id,
            PartCode = ListeningPartCode.A1,
            MaxRawScore = 1,
            CreatedAt = now,
            UpdatedAt = now,
        };
        var extract = new ListeningExtract
        {
            Id = "e-a1",
            ListeningPartId = part.Id,
            DisplayOrder = 0,
            Kind = ListeningExtractKind.Consultation,
            Title = "E",
            AccentCode = "en-GB",
            SpeakersJson = "[]",
            TranscriptSegmentsJson = "[]",
            CreatedAt = now,
            UpdatedAt = now,
        };
        var q1 = new ListeningQuestion
        {
            Id = "q-1",
            PaperId = paper.Id,
            ListeningPartId = part.Id,
            ListeningExtractId = extract.Id,
            QuestionNumber = 1,
            DisplayOrder = 1,
            Points = 1,
            QuestionType = ListeningQuestionType.ShortAnswer,
            Stem = "Dose: ____",
            CorrectAnswerJson = "\"five\"",
            AcceptedSynonymsJson = "[\"5\"]",
            CaseSensitive = false,
            CreatedAt = now,
            UpdatedAt = now,
        };
        var attempt = new ListeningAttempt
        {
            Id = "att-1",
            UserId = "u-1",
            PaperId = paper.Id,
            StartedAt = now,
            LastActivityAt = now,
            Status = ListeningAttemptStatus.InProgress,
            Mode = ListeningAttemptMode.Exam,
            MaxRawScore = 1,
            LastQuestionVersionMapJson = "{\"q-1\":1}",
        };
        var ans = new ListeningAnswer
        {
            Id = "a-1",
            ListeningAttemptId = attempt.Id,
            ListeningQuestionId = q1.Id,
            UserAnswerJson = "\"five\"",
        };

        db.ContentPapers.Add(paper);
        db.ListeningParts.Add(part);
        db.ListeningExtracts.Add(extract);
        db.ListeningQuestions.Add(q1);
        db.ListeningAttempts.Add(attempt);
        db.ListeningAnswers.Add(ans);
        await db.SaveChangesAsync();

        var grader = new ListeningGradingService(db);
        var result = await grader.GradeAsync(attempt.Id, CancellationToken.None);

        // Mission-critical: scaledScore MUST equal OetScoring.OetRawToScaled(rawScore).
        var expectedScaled = OetScoring.OetRawToScaled(result.RawScore);
        Assert.Equal(expectedScaled, result.ScaledScore);
        Assert.Equal(1, result.RawScore);
        Assert.Equal(1, result.MaxRawScore);

        var reloaded = await db.ListeningAttempts.FirstAsync(a => a.Id == "att-1");
        Assert.Equal(ListeningAttemptStatus.Submitted, reloaded.Status);
        Assert.Equal(expectedScaled, reloaded.ScaledScore);
    }

    [Fact]
    public async Task GradeAsync_zero_correct_returns_zero_scaled()
    {
        await using var db = NewDb();
        var now = DateTimeOffset.UtcNow;
        db.ContentPapers.Add(new ContentPaper
        {
            Id = "paper-0", SubtestCode = "listening", Title = "T", Slug = "t",
            Status = ContentStatus.Published, Difficulty = "standard",
            CreatedAt = now, UpdatedAt = now, ExtractedTextJson = "{}",
        });
        db.ListeningParts.Add(new ListeningPart
        {
            Id = "p-0", PaperId = "paper-0", PartCode = ListeningPartCode.A1,
            MaxRawScore = 1, CreatedAt = now, UpdatedAt = now,
        });
        db.ListeningExtracts.Add(new ListeningExtract
        {
            Id = "e-0", ListeningPartId = "p-0", DisplayOrder = 0,
            Kind = ListeningExtractKind.Consultation, Title = "E", AccentCode = "en-GB",
            SpeakersJson = "[]", TranscriptSegmentsJson = "[]",
            CreatedAt = now, UpdatedAt = now,
        });
        db.ListeningQuestions.Add(new ListeningQuestion
        {
            Id = "q-0", PaperId = "paper-0", ListeningPartId = "p-0", ListeningExtractId = "e-0",
            QuestionNumber = 1, DisplayOrder = 1, Points = 1,
            QuestionType = ListeningQuestionType.ShortAnswer, Stem = "?",
            CorrectAnswerJson = "\"yes\"", AcceptedSynonymsJson = "[]",
            CaseSensitive = false, CreatedAt = now, UpdatedAt = now,
        });
        db.ListeningAttempts.Add(new ListeningAttempt
        {
            Id = "att-0", UserId = "u", PaperId = "paper-0",
            StartedAt = now, LastActivityAt = now, MaxRawScore = 1,
            Mode = ListeningAttemptMode.Exam,
            LastQuestionVersionMapJson = "{\"q-0\":1}",
        });
        db.ListeningAnswers.Add(new ListeningAnswer
        {
            Id = "a-0", ListeningAttemptId = "att-0", ListeningQuestionId = "q-0",
            UserAnswerJson = "\"no\"",
        });
        await db.SaveChangesAsync();

        var result = await new ListeningGradingService(db).GradeAsync("att-0", CancellationToken.None);
        Assert.Equal(0, result.RawScore);
        Assert.Equal(OetScoring.OetRawToScaled(0), result.ScaledScore);
    }

    [Theory]
    [InlineData("the   aspirin", 1)]
    [InlineData("aspirin tablets", 0)]
    public async Task GradeAsync_part_a_uses_strict_accepted_variants_without_partial_credit(
        string learnerAnswer,
        int expectedRawScore)
    {
        await using var db = NewDb();
        var now = DateTimeOffset.UtcNow;
        var paper = new ContentPaper
        {
            Id = "paper-part-a-strict",
            SubtestCode = "listening",
            Title = "Part A Strict",
            Slug = "part-a-strict",
            Status = ContentStatus.Published,
            Difficulty = "standard",
            CreatedAt = now,
            UpdatedAt = now,
            ExtractedTextJson = "{}",
        };
        var part = new ListeningPart
        {
            Id = "part-a1-strict",
            PaperId = paper.Id,
            PartCode = ListeningPartCode.A1,
            MaxRawScore = 1,
            CreatedAt = now,
            UpdatedAt = now,
        };
        var extract = new ListeningExtract
        {
            Id = "extract-a1-strict",
            ListeningPartId = part.Id,
            DisplayOrder = 1,
            Kind = ListeningExtractKind.Consultation,
            Title = "Consultation",
            SpeakersJson = "[]",
            TranscriptSegmentsJson = "[]",
            CreatedAt = now,
            UpdatedAt = now,
        };
        var question = new ListeningQuestion
        {
            Id = "question-a1-strict",
            PaperId = paper.Id,
            ListeningPartId = part.Id,
            ListeningExtractId = extract.Id,
            QuestionNumber = 1,
            DisplayOrder = 1,
            Points = 1,
            QuestionType = ListeningQuestionType.ShortAnswer,
            Stem = "Prescribed ____",
            CorrectAnswerJson = "\"aspirin\"",
            AcceptedSynonymsJson = "[\"the aspirin\"]",
            CaseSensitive = false,
            CreatedAt = now,
            UpdatedAt = now,
        };
        var attempt = new ListeningAttempt
        {
            Id = $"attempt-{expectedRawScore}",
            UserId = "learner-part-a",
            PaperId = paper.Id,
            StartedAt = now,
            LastActivityAt = now,
            Status = ListeningAttemptStatus.InProgress,
            Mode = ListeningAttemptMode.Exam,
            MaxRawScore = 1,
            LastQuestionVersionMapJson = "{\"question-a1-strict\":1}",
        };
        var answer = new ListeningAnswer
        {
            Id = $"answer-{expectedRawScore}",
            ListeningAttemptId = attempt.Id,
            ListeningQuestionId = question.Id,
            UserAnswerJson = $"\"{learnerAnswer}\"",
        };

        db.ContentPapers.Add(paper);
        db.ListeningParts.Add(part);
        db.ListeningExtracts.Add(extract);
        db.ListeningQuestions.Add(question);
        db.ListeningAttempts.Add(attempt);
        db.ListeningAnswers.Add(answer);
        await db.SaveChangesAsync();

        var result = await new ListeningGradingService(db).GradeAsync(attempt.Id, CancellationToken.None);

        Assert.Equal(expectedRawScore, result.RawScore);
        Assert.Equal(OetScoring.OetRawToScaled(expectedRawScore), result.ScaledScore);
    }

    [Fact]
    public async Task GradeAsync_rejects_attempt_owned_by_different_user()
    {
        await using var db = NewDb();
        var seeded = await SeedOverrideAttemptAsync(db, submitted: false);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            new ListeningGradingService(db).GradeAsync(
                seeded.AttemptId,
                userId: "different-learner",
                CancellationToken.None));
    }

    [Fact]
    public async Task ApplyScoreOverrideAsync_requires_a_reason()
    {
        await using var db = NewDb();
        var seeded = await SeedOverrideAttemptAsync(db, submitted: true);

        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            new ListeningGradingService(db).ApplyScoreOverrideAsync(
                seeded.AttemptId,
                seeded.WrongQuestionId,
                overrideValue: 1,
                actorId: "expert-1",
                actorName: "Expert One",
                reason: "   ",
                CancellationToken.None));

        Assert.Equal("listening_override_reason_required", ex.ErrorCode);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    public async Task ApplyScoreOverrideAsync_rejects_invalid_override_value(int invalidValue)
    {
        await using var db = NewDb();
        var seeded = await SeedOverrideAttemptAsync(db, submitted: true);

        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            new ListeningGradingService(db).ApplyScoreOverrideAsync(
                seeded.AttemptId,
                seeded.WrongQuestionId,
                overrideValue: invalidValue,
                actorId: "expert-1",
                actorName: "Expert One",
                reason: "Accepted spelling variant authored after review.",
                CancellationToken.None));

        Assert.Equal("listening_override_invalid_value", ex.ErrorCode);
    }

    [Fact]
    public async Task ApplyScoreOverrideAsync_requires_submitted_attempt()
    {
        await using var db = NewDb();
        var seeded = await SeedOverrideAttemptAsync(db, submitted: false);

        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            new ListeningGradingService(db).ApplyScoreOverrideAsync(
                seeded.AttemptId,
                seeded.WrongQuestionId,
                overrideValue: 1,
                actorId: "expert-1",
                actorName: "Expert One",
                reason: "Accepted spelling variant authored after review.",
                CancellationToken.None));

        Assert.Equal("listening_override_requires_submitted_attempt", ex.ErrorCode);
    }

    [Fact]
    public async Task ApplyScoreOverrideAsync_requires_assigned_reviewer()
    {
        await using var db = NewDb();
        var seeded = await SeedOverrideAttemptAsync(db, submitted: true);

        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            new ListeningGradingService(db).ApplyScoreOverrideAsync(
                seeded.AttemptId,
                seeded.WrongQuestionId,
                overrideValue: 1,
                actorId: "expert-2",
                actorName: "Expert Two",
                reason: "Accepted spelling variant authored after review.",
                CancellationToken.None));

        Assert.Equal("listening_override_reviewer_not_assigned", ex.ErrorCode);
    }

    [Fact]
    public async Task ApplyScoreOverrideAsync_rejects_question_outside_attempt_paper()
    {
        await using var db = NewDb();
        var seeded = await SeedOverrideAttemptAsync(db, submitted: true);

        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            new ListeningGradingService(db).ApplyScoreOverrideAsync(
                seeded.AttemptId,
                questionId: "question-from-another-paper",
                overrideValue: 1,
                actorId: "expert-1",
                actorName: "Expert One",
                reason: "Accepted spelling variant authored after review.",
                CancellationToken.None));

        Assert.Equal("listening_question_not_found", ex.ErrorCode);
    }

    [Fact]
    public async Task ApplyScoreOverrideAsync_persists_override_regrades_and_writes_audit()
    {
        await using var db = NewDb();
        var seeded = await SeedOverrideAttemptAsync(db, submitted: true);
        var originalSubmittedAt = seeded.SubmittedAt;
        const string reason = "Accepted spelling variant authored after human review.";

        var result = await new ListeningGradingService(db).ApplyScoreOverrideAsync(
            seeded.AttemptId,
            seeded.WrongQuestionId,
            overrideValue: 1,
            actorId: "expert-1",
            actorName: "Expert One",
            reason: $"  {reason}  ",
            CancellationToken.None);

        Assert.Equal(2, result.RawScore);
        Assert.Equal(2, result.MaxRawScore);
        Assert.Equal(OetScoring.OetRawToScaled(2), result.ScaledScore);
        Assert.Equal(reason, result.Reason);

        var attempt = await db.ListeningAttempts.SingleAsync(a => a.Id == seeded.AttemptId);
        Assert.Equal(originalSubmittedAt, attempt.SubmittedAt);
        Assert.Equal(2, attempt.RawScore);
        Assert.Equal(OetScoring.OetRawToScaled(2), attempt.ScaledScore);
        Assert.Contains("\"questionId\":\"q-override-wrong\"", attempt.HumanScoreOverridesJson);
        Assert.Contains(reason, attempt.HumanScoreOverridesJson);

        var answer = await db.ListeningAnswers.SingleAsync(a =>
            a.ListeningAttemptId == seeded.AttemptId && a.ListeningQuestionId == seeded.WrongQuestionId);
        Assert.True(answer.IsCorrect);
        Assert.Equal(1, answer.PointsEarned);

        var audit = await db.AuditEvents.SingleAsync(e => e.Action == "listening.score.override");
        Assert.Equal("ListeningAttempt", audit.ResourceType);
        Assert.Equal(seeded.AttemptId, audit.ResourceId);
        Assert.Contains(reason, audit.Details);
        Assert.Contains("expert-1", audit.ActorId);

        var evaluation = await db.Evaluations.SingleAsync(e => e.AttemptId == seeded.AttemptId);
        Assert.Equal("human_override_applied", evaluation.StatusReasonCode);
        Assert.Contains("\"rawScore\":2", evaluation.CriterionScoresJson);
        Assert.Contains(reason, evaluation.CriterionScoresJson);
    }

    private static async Task<SeededOverrideAttempt> SeedOverrideAttemptAsync(
        LearnerDbContext db,
        bool submitted)
    {
        var now = DateTimeOffset.UtcNow;
        var paper = new ContentPaper
        {
            Id = "paper-override",
            SubtestCode = "listening",
            Title = "Override Paper",
            Slug = "override-paper",
            Status = ContentStatus.Published,
            Difficulty = "standard",
            CreatedAt = now,
            UpdatedAt = now,
            ExtractedTextJson = "{}",
        };
        var part = new ListeningPart
        {
            Id = "part-override",
            PaperId = paper.Id,
            PartCode = ListeningPartCode.A1,
            MaxRawScore = 2,
            CreatedAt = now,
            UpdatedAt = now,
        };
        var extract = new ListeningExtract
        {
            Id = "extract-override",
            ListeningPartId = part.Id,
            DisplayOrder = 1,
            Kind = ListeningExtractKind.Consultation,
            Title = "Consultation",
            AccentCode = "en-GB",
            SpeakersJson = "[]",
            TranscriptSegmentsJson = "[]",
            CreatedAt = now,
            UpdatedAt = now,
        };
        var wrongQuestion = new ListeningQuestion
        {
            Id = "q-override-wrong",
            PaperId = paper.Id,
            ListeningPartId = part.Id,
            ListeningExtractId = extract.Id,
            QuestionNumber = 1,
            DisplayOrder = 1,
            Points = 1,
            QuestionType = ListeningQuestionType.ShortAnswer,
            Stem = "Medicine ____",
            CorrectAnswerJson = "\"aspirin\"",
            AcceptedSynonymsJson = "[]",
            CaseSensitive = false,
            CreatedAt = now,
            UpdatedAt = now,
        };
        var correctQuestion = new ListeningQuestion
        {
            Id = "q-override-correct",
            PaperId = paper.Id,
            ListeningPartId = part.Id,
            ListeningExtractId = extract.Id,
            QuestionNumber = 2,
            DisplayOrder = 2,
            Points = 1,
            QuestionType = ListeningQuestionType.ShortAnswer,
            Stem = "Dose ____",
            CorrectAnswerJson = "\"daily\"",
            AcceptedSynonymsJson = "[]",
            CaseSensitive = false,
            CreatedAt = now,
            UpdatedAt = now,
        };
        var submittedAt = submitted ? now.AddMinutes(5) : (DateTimeOffset?)null;
        var attempt = new ListeningAttempt
        {
            Id = "attempt-override",
            UserId = "learner-override",
            PaperId = paper.Id,
            StartedAt = now,
            SubmittedAt = submittedAt,
            LastActivityAt = now,
            Status = submitted ? ListeningAttemptStatus.Submitted : ListeningAttemptStatus.InProgress,
            Mode = ListeningAttemptMode.Exam,
            RawScore = submitted ? 1 : null,
            ScaledScore = submitted ? OetScoring.OetRawToScaled(1) : null,
            MaxRawScore = 2,
            LastQuestionVersionMapJson = "{\"q-override-wrong\":1,\"q-override-correct\":1}",
        };

        db.ContentPapers.Add(paper);
        db.ListeningParts.Add(part);
        db.ListeningExtracts.Add(extract);
        db.ListeningQuestions.AddRange(wrongQuestion, correctQuestion);
        db.ListeningAttempts.Add(attempt);
        db.ListeningAnswers.AddRange(
            new ListeningAnswer
            {
                Id = "answer-override-wrong",
                ListeningAttemptId = attempt.Id,
                ListeningQuestionId = wrongQuestion.Id,
                UserAnswerJson = "\"ibuprofen\"",
                IsCorrect = false,
                PointsEarned = 0,
                QuestionVersionSnapshot = 1,
                AnsweredAt = now,
            },
            new ListeningAnswer
            {
                Id = "answer-override-correct",
                ListeningAttemptId = attempt.Id,
                ListeningQuestionId = correctQuestion.Id,
                UserAnswerJson = "\"daily\"",
                IsCorrect = true,
                PointsEarned = 1,
                QuestionVersionSnapshot = 1,
                AnsweredAt = now,
            });
        db.Evaluations.Add(new Evaluation
        {
            Id = "evaluation-override",
            AttemptId = attempt.Id,
            SubtestCode = "listening",
            State = AsyncState.Completed,
            ScoreRange = "1 / 2",
            GradeRange = "Grade E",
            ConfidenceBand = ConfidenceBand.High,
            CriterionScoresJson = "[]",
            GeneratedAt = now,
            ModelExplanationSafe = "Deterministic Listening result.",
            LearnerDisclaimer = "Practice result only.",
            LastTransitionAt = now,
        });
        db.ReviewRequests.Add(new ReviewRequest
        {
            Id = "review-override",
            AttemptId = attempt.Id,
            SubtestCode = "listening",
            State = ReviewRequestState.InReview,
            TurnaroundOption = "standard",
            PaymentSource = "included",
            PriceSnapshot = 0,
            ReviewerCompensation = 0,
            CreatedAt = now,
        });
        db.ExpertReviewAssignments.Add(new ExpertReviewAssignment
        {
            Id = "assignment-override",
            ReviewRequestId = "review-override",
            AssignedReviewerId = "expert-1",
            AssignedAt = now,
            ClaimState = ExpertAssignmentState.Claimed,
        });
        await db.SaveChangesAsync();

        return new SeededOverrideAttempt(attempt.Id, wrongQuestion.Id, submittedAt);
    }

    private sealed record SeededOverrideAttempt(
        string AttemptId,
        string WrongQuestionId,
        DateTimeOffset? SubmittedAt);
}
