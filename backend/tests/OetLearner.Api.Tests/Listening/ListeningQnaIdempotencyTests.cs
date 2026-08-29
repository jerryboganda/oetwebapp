using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Listening;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Tests.Listening;

public sealed class ListeningQnaIdempotencyTests
{
    [Fact]
    public async Task Duplicate_client_turn_returns_cached_reply_without_new_spend()
    {
        await using var db = NewDb();
        await SeedAsync(db);

        var gateway = new CountingGateway();
        var service = new ListeningQuestionQnaService(db, new RulebookLoader(), gateway);
        var request = new ListeningQuestionQnaRequest(
            "Why was my answer wrong?",
            new List<ChatMessageDto>(),
            "turn-1");

        var first = await service.AskAsync("learner-1", "listening-qna-attempt-1", "listening-qna-q1", request, default);
        var second = await service.AskAsync("learner-1", "listening-qna-attempt-1", "listening-qna-q1", request, default);

        Assert.Equal(1, gateway.Calls);
        Assert.False(first.Cached);
        Assert.True(second.Cached);
        Assert.Equal(first.Reply, second.Reply);
        Assert.Equal(1, await db.ListeningQnaTurns.CountAsync());
    }

    private static LearnerDbContext NewDb() => new(
        new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static async Task SeedAsync(LearnerDbContext db)
    {
        var now = DateTimeOffset.UtcNow;
        db.ContentPapers.Add(new ContentPaper
        {
            Id = "listening-qna-paper",
            SubtestCode = "listening",
            Title = "Listening QnA paper",
            Slug = "listening-qna-paper",
            Status = ContentStatus.Published,
            PublishedRevisionId = "listening-qna-revision-1",
        });
        db.ListeningQuestions.Add(new ListeningQuestion
        {
            Id = "listening-qna-q1",
            PaperId = "listening-qna-paper",
            ListeningPartId = "listening-part-b1",
            QuestionNumber = 1,
            DisplayOrder = 1,
            QuestionType = ListeningQuestionType.MultipleChoice3,
            Stem = "Which option is supported?",
            CorrectAnswerJson = "\"A\"",
            TranscriptEvidenceText = "The speaker explicitly supports option A.",
            Version = 1,
        });
        db.ListeningAttempts.Add(new ListeningAttempt
        {
            Id = "listening-qna-attempt-1",
            UserId = "learner-1",
            PaperId = "listening-qna-paper",
            StartedAt = now.AddMinutes(-10),
            LastActivityAt = now,
            SubmittedAt = now,
            Status = ListeningAttemptStatus.Submitted,
            MaxRawScore = 1,
            PaperRevisionId = "listening-qna-revision-1",
            LastQuestionVersionMapJson = "{\"listening-qna-q1\":1}",
        });
        db.ListeningAnswers.Add(new ListeningAnswer
        {
            Id = "listening-qna-answer-1",
            ListeningAttemptId = "listening-qna-attempt-1",
            ListeningQuestionId = "listening-qna-q1",
            UserAnswerJson = "\"B\"",
            QuestionVersionSnapshot = 1,
            AnsweredAt = now,
        });
        db.AssessmentRationales.Add(new AssessmentRationale
        {
            Id = "rationale-listening-qna-q1",
            Assessment = "listening",
            QuestionRevisionId = "listening-qna-q1",
            SourceSentence = "The source sentence supports the authored answer.",
            RationaleText = "The authored rationale explains the answer.",
            EvidenceCount = 1,
            Status = AssessmentGovernanceStatus.Effective,
            CreatedByUserId = "author-1",
            ApprovedByUserId = "reviewer-1",
            CreatedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();
    }

    private sealed class CountingGateway : IAiGatewayService
    {
        public int Calls { get; private set; }

        public Task<AiGatewayResult> CompleteAsync(AiGatewayRequest request, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(new AiGatewayResult
            {
                Completion = """{"reply":"The transcript supports option A.","advisoryOnly":true}""",
                UsageRecordId = "usage-qna-1",
                UsagePersisted = true,
            });
        }

        public AiGroundedPrompt BuildGroundedPrompt(AiGroundingContext context)
            => new()
            {
                SystemPrompt = "grounded test prompt",
                TaskInstruction = "answer",
                Metadata = new AiGroundedPromptMetadata
                {
                    RulebookKind = context.Kind,
                    Profession = context.Profession,
                    RulebookVersion = "test",
                },
            };
    }
}
