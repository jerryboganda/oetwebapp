using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Reading;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Tests.Reading;

public sealed class ReadingPassageQnaIdempotencyTests
{
    [Fact]
    public async Task Duplicate_client_turn_returns_cached_reply_without_new_spend()
    {
        await using var db = NewDb();
        await SeedAsync(db);

        var gateway = new CountingGateway();
        var service = new ReadingPassageQnaService(db, new RulebookLoader(), gateway);
        var request = new PassageQnaRequest(
            "reading-qna-attempt-1",
            "reading-qna-passage-1",
            "What should patients follow?",
            new List<ChatMessageDto>(),
            "turn-1");

        var first = await service.AskAsync("learner-1", request, default);
        var second = await service.AskAsync("learner-1", request, default);

        Assert.Equal(1, gateway.Calls);
        Assert.False(first.Cached);
        Assert.True(second.Cached);
        Assert.Equal(first.Reply, second.Reply);
        Assert.Equal(1, await db.ReadingQnaTurns.CountAsync());
        Assert.Equal(AiFeatureCodes.ReadingPassageQna, gateway.LastFeatureCode);
    }

    [Fact]
    public async Task Session_turn_cap_blocks_further_spend()
    {
        await using var db = NewDb();
        await SeedAsync(db);
        for (var i = 0; i < 50; i++)
        {
            db.ReadingQnaTurns.Add(new ReadingQnaTurn
            {
                Id = Guid.NewGuid().ToString("N"),
                SessionId = "reading-qna-attempt-1:reading-qna-passage-1",
                ClientTurnId = $"seed-{i}",
                UserId = "learner-1",
                AttemptId = "reading-qna-attempt-1",
                PassageId = "reading-qna-passage-1",
                Message = "prior",
                Reply = "prior reply",
                CreatedAt = DateTimeOffset.UtcNow,
            });
        }
        await db.SaveChangesAsync();

        var gateway = new CountingGateway();
        var service = new ReadingPassageQnaService(db, new RulebookLoader(), gateway);

        await Assert.ThrowsAsync<ReadingPassageQnaSessionLimitException>(() =>
            service.AskAsync("learner-1", new PassageQnaRequest(
                "reading-qna-attempt-1",
                "reading-qna-passage-1",
                "One more question?",
                new List<ChatMessageDto>(),
                "turn-over-cap"), default));

        Assert.Equal(0, gateway.Calls);
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
            Id = "reading-qna-paper",
            SubtestCode = "reading",
            Title = "Reading QnA paper",
            Slug = "reading-qna-paper",
            Status = ContentStatus.Published,
            PublishedRevisionId = "reading-qna-revision-1",
        });
        db.ReadingParts.Add(new ReadingPart
        {
            Id = "reading-qna-part-a",
            PaperId = "reading-qna-paper",
            PartCode = ReadingPartCode.A,
            TimeLimitMinutes = 15,
            MaxRawScore = 1,
        });
        db.ReadingTexts.Add(new ReadingText
        {
            Id = "reading-qna-passage-1",
            ReadingPartId = "reading-qna-part-a",
            DisplayOrder = 1,
            Title = "Discharge plan",
            BodyHtml = "<p>Patients should follow the discharge plan.</p>",
            WordCount = 7,
            CreatedAt = now,
            UpdatedAt = now,
        });
        db.ReadingQuestions.Add(new ReadingQuestion
        {
            Id = "reading-qna-q1",
            ReadingPartId = "reading-qna-part-a",
            ReadingTextId = "reading-qna-passage-1",
            QuestionType = ReadingQuestionType.MultipleChoice3,
            Stem = "Which option is supported?",
            OptionsJson = "[\"A\",\"B\",\"C\"]",
            CorrectAnswerJson = "\"A\"",
        });
        db.ReadingAttempts.Add(new ReadingAttempt
        {
            Id = "reading-qna-attempt-1",
            UserId = "learner-1",
            PaperId = "reading-qna-paper",
            Mode = ReadingAttemptMode.Exam,
            StartedAt = now.AddMinutes(-10),
            LastActivityAt = now,
            SubmittedAt = now,
            Status = ReadingAttemptStatus.Submitted,
            MaxRawScore = 1,
            PaperRevisionId = "reading-qna-revision-1",
        });
        await db.SaveChangesAsync();
    }

    private sealed class CountingGateway : IAiGatewayService
    {
        public int Calls { get; private set; }
        public string? LastFeatureCode { get; private set; }

        public Task<AiGatewayResult> CompleteAsync(AiGatewayRequest request, CancellationToken ct = default)
        {
            Calls++;
            LastFeatureCode = request.FeatureCode;
            return Task.FromResult(new AiGatewayResult
            {
                Completion = """{"reply":"Follow the discharge plan.","advisoryOnly":true}""",
                UsageRecordId = "usage-reading-qna-1",
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
