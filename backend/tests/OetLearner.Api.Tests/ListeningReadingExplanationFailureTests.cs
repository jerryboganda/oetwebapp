using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Listening;
using OetLearner.Api.Services.Reading;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Tests;

public sealed class ListeningReadingExplanationFailureTests
{
    [Fact]
    public async Task ReadingExplanation_gateway_failure_returns_unavailable_without_inventing_rationale()
    {
        await using var db = CreateDb();
        var now = DateTimeOffset.UtcNow;
        db.ReadingParts.Add(new ReadingPart
        {
            Id = "reading-part-a",
            PaperId = "reading-paper-1",
            PartCode = ReadingPartCode.A,
            TimeLimitMinutes = 15,
            MaxRawScore = 1,
        });
        db.ReadingQuestions.Add(new ReadingQuestion
        {
            Id = "reading-explanation-q1",
            ReadingPartId = "reading-part-a",
            QuestionType = ReadingQuestionType.MultipleChoice3,
            Stem = "Which option is supported?",
            OptionsJson = "[\"A\",\"B\",\"C\"]",
            CorrectAnswerJson = "\"A\"",
        });
        db.ReadingAttempts.Add(new ReadingAttempt
        {
            Id = "reading-explanation-attempt-1",
            UserId = "learner-1",
            PaperId = "reading-paper-1",
            StartedAt = now.AddMinutes(-10),
            LastActivityAt = now,
            SubmittedAt = now,
            Status = ReadingAttemptStatus.Submitted,
            MaxRawScore = 1,
        });
        db.ReadingAnswers.Add(new ReadingAnswer
        {
            Id = "reading-explanation-answer-1",
            ReadingAttemptId = "reading-explanation-attempt-1",
            ReadingQuestionId = "reading-explanation-q1",
            UserAnswerJson = "\"B\"",
            AnsweredAt = now,
            CreatedAt = now,
            UpdatedAt = now,
        });
        db.AssessmentRationales.Add(EffectiveRationale("reading", "reading-explanation-q1"));
        await db.SaveChangesAsync();

        var service = new ReadingExplanationService(
            db,
            new RulebookLoader(),
            new ThrowingGateway(),
            NullLogger<ReadingExplanationService>.Instance);

        var exception = await Assert.ThrowsAsync<ReadingGroundedExplanationUnavailableException>(() =>
            service.GetSubmittedAttemptExplanationAsync(
                "learner-1", "reading-explanation-attempt-1", "reading-explanation-q1", "en", default));

        Assert.Contains("gateway failed", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ListeningExplanation_gateway_failure_returns_unavailable_without_inventing_rationale()
    {
        await using var db = CreateDb();
        db.ListeningQuestions.Add(new ListeningQuestion
        {
            Id = "listening-explanation-q1",
            PaperId = "listening-paper-1",
            ListeningPartId = "listening-part-b1",
            QuestionNumber = 1,
            DisplayOrder = 1,
            QuestionType = ListeningQuestionType.MultipleChoice3,
            Stem = "Which option is supported?",
            CorrectAnswerJson = "\"A\"",
            TranscriptEvidenceText = "The speaker explicitly supports option A.",
        });
        db.ListeningAttempts.Add(new ListeningAttempt
        {
            Id = "listening-explanation-attempt-1",
            UserId = "learner-1",
            PaperId = "listening-paper-1",
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
            LastActivityAt = DateTimeOffset.UtcNow,
            SubmittedAt = DateTimeOffset.UtcNow,
            Status = ListeningAttemptStatus.Submitted,
            MaxRawScore = 1,
        });
        db.ListeningAnswers.Add(new ListeningAnswer
        {
            Id = "listening-explanation-answer-1",
            ListeningAttemptId = "listening-explanation-attempt-1",
            ListeningQuestionId = "listening-explanation-q1",
            UserAnswerJson = "\"B\"",
            AnsweredAt = DateTimeOffset.UtcNow,
        });
        db.AssessmentRationales.Add(EffectiveRationale("listening", "listening-explanation-q1"));
        await db.SaveChangesAsync();

        var service = new ListeningExplanationService(
            db,
            new ThrowingGateway(),
            NullLogger<ListeningExplanationService>.Instance);

        var exception = await Assert.ThrowsAsync<ListeningGroundedExplanationUnavailableException>(() =>
            service.GetSubmittedAttemptExplanationAsync(
                "learner-1",
                "listening-explanation-attempt-1",
                "listening-explanation-q1",
                "en",
                default));

        Assert.Contains("gateway failed", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static LearnerDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase($"explanation-failure-{Guid.NewGuid():N}")
            .Options;
        return new LearnerDbContext(options);
    }

    private static AssessmentRationale EffectiveRationale(string assessment, string questionId)
        => new()
        {
            Id = $"rationale-{assessment}-{questionId}",
            Assessment = assessment,
            QuestionRevisionId = questionId,
            SourceSentence = "The source sentence supports the authored answer.",
            RationaleText = "The authored rationale explains the answer.",
            EvidenceCount = 1,
            Status = AssessmentGovernanceStatus.Effective,
            CreatedByUserId = "author-1",
            ApprovedByUserId = "reviewer-1",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

    private sealed class ThrowingGateway : IAiGatewayService
    {
        public Task<AiGatewayResult> CompleteAsync(AiGatewayRequest request, CancellationToken ct = default)
            => throw new InvalidOperationException("AI gateway failure for unavailable explanation test.");

        public AiGroundedPrompt BuildGroundedPrompt(AiGroundingContext context)
            => new()
            {
                SystemPrompt = "grounded test prompt",
                TaskInstruction = "fallback test",
                Metadata = new AiGroundedPromptMetadata
                {
                    RulebookKind = context.Kind,
                    Profession = context.Profession,
                    RulebookVersion = "test",
                },
            };
    }
}
