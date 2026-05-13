using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Listening;

namespace OetLearner.Api.Tests;

/// <summary>
/// Gap B6 — per-question + per-extract PATCH coverage for
/// <see cref="ListeningAuthoringService"/>. Verifies partial-update
/// semantics (null fields are preserved) and the BeforeJson/AfterJson
/// audit trail.
/// </summary>
public class ListeningAuthoringServiceTests
{
    private static (LearnerDbContext db, ListeningAuthoringService svc) Build()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var db = new LearnerDbContext(options);
        return (db, new ListeningAuthoringService(db));
    }

    private static async Task<ContentPaper> SeedPaperAsync(LearnerDbContext db)
    {
        var now = DateTimeOffset.UtcNow;
        var paper = new ContentPaper
        {
            Id = Guid.NewGuid().ToString("N"),
            SubtestCode = "listening",
            Title = "Listening Patch Paper",
            Slug = $"listening-{Guid.NewGuid():N}",
            Status = ContentStatus.Draft,
            ExtractedTextJson = JsonSerializer.Serialize(new
            {
                listeningQuestions = new[]
                {
                    new
                    {
                        id = "lq-1",
                        number = 1,
                        partCode = "A1",
                        type = "short_answer",
                        text = "Original stem for question 1",
                        correctAnswer = "alpha",
                        explanation = "original explanation",
                    },
                },
                listeningExtracts = new[]
                {
                    new
                    {
                        partCode = "A1",
                        displayOrder = 0,
                        kind = "consultation",
                        title = "Original A1 title",
                        accentCode = "en-GB",
                        speakers = Array.Empty<object>(),
                        audioStartMs = 0,
                        audioEndMs = 30_000,
                    },
                },
            }),
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.ContentPapers.Add(paper);
        await db.SaveChangesAsync();
        return paper;
    }

    [Fact]
    public async Task PatchQuestion_UpdatesOnlyProvidedFields_AndAuditsBeforeAfter()
    {
        var (db, svc) = Build();
        var paper = await SeedPaperAsync(db);

        var patch = new ListeningQuestionPatch(
            Stem: "Patched stem",
            CorrectAnswer: "beta");

        var result = await svc.PatchQuestionAsync(
            paper.Id, "lq-1", patch, adminId: "admin-42", default);

        var updated = result.Questions.Single();
        Assert.Equal("Patched stem", updated.Stem);
        Assert.Equal("beta", updated.CorrectAnswer);
        // Untouched by patch — must survive.
        Assert.Equal("original explanation", updated.Explanation);
        Assert.Equal("A1", updated.PartCode);
        Assert.Equal(1, updated.Number);

        var audit = await db.AuditEvents.SingleAsync(
            a => a.Action == "listening.question.patch");
        Assert.Equal("ListeningQuestion", audit.ResourceType);
        Assert.Equal("lq-1", audit.ResourceId);
        Assert.Equal("admin-42", audit.ActorId);
        Assert.NotNull(audit.Details);
        Assert.Contains("beforeJson", audit.Details);
        Assert.Contains("afterJson", audit.Details);
        Assert.Contains("Original stem for question 1", audit.Details);
    }

    [Fact]
    public async Task PatchQuestion_AllowsOutOfScopeDistractorCategory()
    {
        var (db, svc) = Build();
        var paper = await SeedPaperAsync(db);

        var patch = new ListeningQuestionPatch(
            Type: "multiple_choice_3",
            Options: new[] { "A", "B", "C" },
            CorrectAnswer: "A",
            OptionDistractorCategory: new string?[] { null, "out_of_scope", "reused_keyword" });

        var result = await svc.PatchQuestionAsync(
            paper.Id, "lq-1", patch, adminId: "admin-42", default);

        var updated = result.Questions.Single();
        Assert.Equal(new string?[] { null, "out_of_scope", "reused_keyword" }, updated.OptionDistractorCategory);
    }

    [Fact]
    public async Task PatchQuestion_404_WhenMissing()
    {
        var (db, svc) = Build();
        var paper = await SeedPaperAsync(db);

        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            svc.PatchQuestionAsync(
                paper.Id, "lq-does-not-exist",
                new ListeningQuestionPatch(Stem: "ignored"),
                adminId: "admin", default));

        Assert.Equal("listening_question_not_found", ex.ErrorCode);
        Assert.Equal(StatusCodes.Status404NotFound, ex.StatusCode);
    }

    [Fact]
    public async Task PatchExtract_UpdatesAudioWindow_AndAudits()
    {
        var (db, svc) = Build();
        var paper = await SeedPaperAsync(db);

        var patch = new ListeningExtractPatch(
            AudioStartMs: 1_500,
            AudioEndMs: 60_000);

        var result = await svc.PatchExtractAsync(
            paper.Id, "A1", patch, adminId: "admin-7", default);

        var updated = result.Single();
        Assert.Equal(1_500, updated.AudioStartMs);
        Assert.Equal(60_000, updated.AudioEndMs);
        // Untouched by patch — must survive.
        Assert.Equal("Original A1 title", updated.Title);
        Assert.Equal("en-GB", updated.AccentCode);
        Assert.Equal("consultation", updated.Kind);

        var audit = await db.AuditEvents.SingleAsync(
            a => a.Action == "listening.extract.patch");
        Assert.Equal("ListeningExtract", audit.ResourceType);
        Assert.Equal($"{paper.Id}:A1", audit.ResourceId);
        Assert.Equal("admin-7", audit.ActorId);
        Assert.Contains("beforeJson", audit.Details!);
        Assert.Contains("afterJson", audit.Details!);
    }

    [Fact]
    public async Task PatchQuestion_ConflictsBeforeMutating_WhenRelationalAttemptsExist()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var db = new LearnerDbContext(options);
        var backfill = new RecordingBackfillService();
        var svc = new ListeningAuthoringService(db, backfill);
        var paper = await SeedPaperAsync(db);
        var originalJson = paper.ExtractedTextJson;

        var part = new ListeningPart
        {
            Id = "part-a1",
            PaperId = paper.Id,
            PartCode = ListeningPartCode.A1,
            MaxRawScore = 12,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.ListeningParts.Add(part);
        db.ListeningQuestions.Add(new ListeningQuestion
        {
            Id = "rel-q-1",
            PaperId = paper.Id,
            ListeningPartId = part.Id,
            QuestionNumber = 1,
            DisplayOrder = 1,
            QuestionType = ListeningQuestionType.ShortAnswer,
            Stem = "Original stem for question 1",
            CorrectAnswerJson = "\"alpha\"",
            Points = 1,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        db.ListeningAttempts.Add(new ListeningAttempt
        {
            Id = "attempt-1",
            PaperId = paper.Id,
            UserId = "learner-1",
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            LastActivityAt = DateTimeOffset.UtcNow,
            MaxRawScore = 42,
        });
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            svc.PatchQuestionAsync(
                paper.Id,
                "lq-1",
                new ListeningQuestionPatch(Stem: "Should not persist"),
                "admin",
                default));

        Assert.Equal("listening_relational_resync_blocked", ex.ErrorCode);
        Assert.Equal(StatusCodes.Status409Conflict, ex.StatusCode);
        Assert.False(backfill.WasCalled);
        Assert.Equal(originalJson, (await db.ContentPapers.AsNoTracking().SingleAsync(p => p.Id == paper.Id)).ExtractedTextJson);
        Assert.Empty(await db.AuditEvents.Where(a => a.Action == "listening.question.patch").ToListAsync());
    }

    [Fact]
    public async Task ReplaceExtracts_ConflictsBeforeMutating_WhenRelationalAttemptsExist()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var db = new LearnerDbContext(options);
        var backfill = new RecordingBackfillService();
        var svc = new ListeningAuthoringService(db, backfill);
        var paper = await SeedPaperAsync(db);
        var originalJson = paper.ExtractedTextJson;

        var part = new ListeningPart
        {
            Id = "part-a1",
            PaperId = paper.Id,
            PartCode = ListeningPartCode.A1,
            MaxRawScore = 12,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.ListeningParts.Add(part);
        db.ListeningExtracts.Add(new ListeningExtract
        {
            Id = "extract-a1",
            ListeningPartId = part.Id,
            DisplayOrder = 0,
            Kind = ListeningExtractKind.Consultation,
            Title = "Original A1 title",
            AudioStartMs = 0,
            AudioEndMs = 30_000,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        db.ListeningAttempts.Add(new ListeningAttempt
        {
            Id = "attempt-1",
            PaperId = paper.Id,
            UserId = "learner-1",
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            LastActivityAt = DateTimeOffset.UtcNow,
            MaxRawScore = 42,
        });
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            svc.ReplaceExtractsAsync(
                paper.Id,
                [new ListeningAuthoredExtract(
                    PartCode: "A1",
                    DisplayOrder: 0,
                    Kind: "consultation",
                    Title: "Should not persist",
                    AccentCode: "en-AU",
                    Speakers: [],
                    AudioStartMs: 10,
                    AudioEndMs: 20)],
                "admin",
                default));

        Assert.Equal("listening_relational_resync_blocked", ex.ErrorCode);
        Assert.Equal(StatusCodes.Status409Conflict, ex.StatusCode);
        Assert.False(backfill.WasCalled);
        Assert.Equal(originalJson, (await db.ContentPapers.AsNoTracking().SingleAsync(p => p.Id == paper.Id)).ExtractedTextJson);
        Assert.Empty(await db.AuditEvents.Where(a => a.Action == "ListeningExtractsUpdated").ToListAsync());
    }

    private sealed class RecordingBackfillService : IListeningBackfillService
    {
        public bool WasCalled { get; private set; }

        public Task<ListeningBackfillReport> BackfillPaperAsync(string paperId, string adminId, CancellationToken ct)
        {
            WasCalled = true;
            return Task.FromResult(new ListeningBackfillReport(paperId, true, 1, 1, 1, 0, null));
        }

        public Task<IReadOnlyList<ListeningBackfillReport>> BackfillAllAsync(string adminId, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<ListeningBackfillReport>>([]);
    }
}
