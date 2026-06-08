using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Content;
using OetLearner.Api.Services.Listening;

namespace OetLearner.Api.Tests.Listening;

/// <summary>
/// Surfaces the Part A note-completion body (<c>notesBody</c>) on the learner
/// session DTO through both runtime stores. Covers: the RELATIONAL path (a
/// paper with a relational A1 <see cref="ListeningExtract.NotesBodyMarkdown"/>
/// surfaces <c>notesBody</c> on the session's A1 extract; Part B has null); the
/// JSON path (a paper whose <c>listeningExtracts[].notesBody</c> is set surfaces
/// the same); a backfill carrying the body from JSON into the relational row;
/// and a body-only-edit re-backfill regression with learner attempts present.
/// Mirrors the harness in <see cref="ListeningRelationalRuntimeTests"/>.
/// </summary>
public class ListeningSessionNotesBodyTests
{
    private const string A1Body =
        "Patient: Mr Jones\n## History\n- Presenting complaint: ____\n- Onset: ____";

    private sealed class AllowAllContentEntitlementService : IContentEntitlementService
    {
        public Task<ContentEntitlementResult> AllowAccessAsync(string? userId, ContentPaper paper, CancellationToken ct)
            => Task.FromResult(new ContentEntitlementResult(true, "test", "premium", null));

        public Task RequireAccessAsync(string? userId, ContentPaper paper, CancellationToken ct)
            => Task.CompletedTask;

        public bool IsAdmin(System.Security.Claims.ClaimsPrincipal? principal) => false;
    }

    private static (LearnerDbContext db, ListeningLearnerService svc) Build()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var db = new LearnerDbContext(options);
        return (db, new ListeningLearnerService(db, new AllowAllContentEntitlementService()));
    }

    private static LearnerUser NewLearner()
    {
        var now = DateTimeOffset.UtcNow;
        return new LearnerUser
        {
            Id = "learner-1",
            AuthAccountId = "auth-1",
            DisplayName = "Learner One",
            Email = "learner@example.test",
            Role = ApplicationUserRoles.Learner,
            CreatedAt = now,
            LastActiveAt = now,
            AccountStatus = "active",
        };
    }

    /// <summary>Seed a relational paper with an A1 extract (carrying a notesBody)
    /// and a B1 extract (no body). Returns the paper id.</summary>
    private static async Task<(string userId, string paperId)> SeedRelationalPaperAsync(LearnerDbContext db)
    {
        var now = DateTimeOffset.UtcNow;
        var paper = new ContentPaper
        {
            Id = "paper-notes-1",
            SubtestCode = "listening",
            Title = "Relational NotesBody Paper",
            Slug = "relational-notesbody-paper",
            Status = ContentStatus.Published,
            Difficulty = "standard",
            AppliesToAllProfessions = true,
            EstimatedDurationMinutes = 45,
            CreatedAt = now,
            UpdatedAt = now,
            PublishedAt = now,
            ExtractedTextJson = "{}",
        };
        var partA = new ListeningPart
        {
            Id = "part-a1",
            PaperId = paper.Id,
            PartCode = ListeningPartCode.A1,
            MaxRawScore = 1,
            CreatedAt = now,
            UpdatedAt = now,
        };
        var partB = new ListeningPart
        {
            Id = "part-b1",
            PaperId = paper.Id,
            PartCode = ListeningPartCode.B1,
            MaxRawScore = 1,
            CreatedAt = now,
            UpdatedAt = now,
        };
        var extractA = new ListeningExtract
        {
            Id = "extract-a1",
            ListeningPartId = partA.Id,
            DisplayOrder = 0,
            Kind = ListeningExtractKind.Consultation,
            Title = "Consultation 1",
            AccentCode = "en-GB",
            SpeakersJson = "[]",
            TranscriptSegmentsJson = "[]",
            NotesBodyMarkdown = A1Body,
            CreatedAt = now,
            UpdatedAt = now,
        };
        var extractB = new ListeningExtract
        {
            Id = "extract-b1",
            ListeningPartId = partB.Id,
            DisplayOrder = 2,
            Kind = ListeningExtractKind.Workplace,
            Title = "Workplace extract 1",
            AccentCode = "en-AU",
            SpeakersJson = "[]",
            TranscriptSegmentsJson = "[]",
            NotesBodyMarkdown = null,
            CreatedAt = now,
            UpdatedAt = now,
        };
        var qA = new ListeningQuestion
        {
            Id = "q-a1",
            PaperId = paper.Id,
            ListeningPartId = partA.Id,
            ListeningExtractId = extractA.Id,
            QuestionNumber = 1,
            DisplayOrder = 1,
            Points = 1,
            QuestionType = ListeningQuestionType.ShortAnswer,
            Stem = "Presenting complaint: ____",
            CorrectAnswerJson = "\"headache\"",
            AcceptedSynonymsJson = "[]",
            CaseSensitive = false,
            CreatedAt = now,
            UpdatedAt = now,
        };
        var qB = new ListeningQuestion
        {
            Id = "q-b1",
            PaperId = paper.Id,
            ListeningPartId = partB.Id,
            ListeningExtractId = extractB.Id,
            QuestionNumber = 25,
            DisplayOrder = 25,
            Points = 1,
            QuestionType = ListeningQuestionType.MultipleChoice3,
            Stem = "What is the main point?",
            CorrectAnswerJson = "\"B\"",
            AcceptedSynonymsJson = null,
            CaseSensitive = false,
            CreatedAt = now,
            UpdatedAt = now,
        };

        db.Users.Add(NewLearner());
        db.ContentPapers.Add(paper);
        db.ListeningParts.AddRange(partA, partB);
        db.ListeningExtracts.AddRange(extractA, extractB);
        db.ListeningQuestions.AddRange(qA, qB);
        db.ListeningPolicies.Add(new ListeningPolicy { Id = "global", FullPaperTimerMinutes = 45, GracePeriodSeconds = 10 });
        await db.SaveChangesAsync();
        return ("learner-1", paper.Id);
    }

    private static async Task<(string userId, string paperId)> SeedJsonPaperAsync(LearnerDbContext db)
    {
        var now = DateTimeOffset.UtcNow;
        var paper = new ContentPaper
        {
            Id = "json-notes-paper-1",
            SubtestCode = "listening",
            Title = "JSON NotesBody Paper",
            Slug = "json-notesbody-paper",
            Status = ContentStatus.Published,
            Difficulty = "standard",
            AppliesToAllProfessions = true,
            EstimatedDurationMinutes = 45,
            CreatedAt = now,
            UpdatedAt = now,
            PublishedAt = now,
            ExtractedTextJson = JsonSerializer.Serialize(new
            {
                listeningQuestions = new[]
                {
                    new
                    {
                        id = "json-q1",
                        number = 1,
                        partCode = "A1",
                        type = "short_answer",
                        text = "Presenting complaint: ____",
                        correctAnswer = "headache",
                    },
                },
                listeningExtracts = new[]
                {
                    new
                    {
                        partCode = "A1",
                        kind = "consultation",
                        title = "Consultation 1",
                        accentCode = "en-GB",
                        notesBody = A1Body,
                    },
                },
            }),
        };
        db.Users.Add(NewLearner());
        db.ContentPapers.Add(paper);
        db.ListeningPolicies.Add(new ListeningPolicy { Id = "global", FullPaperTimerMinutes = 45, GracePeriodSeconds = 10 });
        await db.SaveChangesAsync();
        return ("learner-1", paper.Id);
    }

    private static JsonElement A1ExtractElement(JsonElement sessionRoot)
        => sessionRoot.GetProperty("paper").GetProperty("extracts").EnumerateArray()
            .Single(e => e.GetProperty("partCode").GetString() == "A1");

    // ── Tests ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task RelationalSession_SurfacesA1NotesBody_AndNullForPartB()
    {
        var (db, svc) = Build();
        var (userId, paperId) = await SeedRelationalPaperAsync(db);

        var session = await svc.GetSessionAsync(userId, paperId, "paper", attemptId: null, default);
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(session));
        var extracts = doc.RootElement.GetProperty("paper").GetProperty("extracts");

        var a1 = extracts.EnumerateArray().Single(e => e.GetProperty("partCode").GetString() == "A1");
        Assert.Equal(A1Body, a1.GetProperty("notesBody").GetString());

        var b1 = extracts.EnumerateArray().Single(e => e.GetProperty("partCode").GetString() == "B1");
        Assert.Equal(JsonValueKind.Null, b1.GetProperty("notesBody").ValueKind);
    }

    [Fact]
    public async Task JsonSession_SurfacesA1NotesBody()
    {
        var (db, svc) = Build();
        var (userId, paperId) = await SeedJsonPaperAsync(db);

        var session = await svc.GetSessionAsync(userId, paperId, "paper", attemptId: null, default);
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(session));
        var a1 = A1ExtractElement(doc.RootElement);
        Assert.Equal(A1Body, a1.GetProperty("notesBody").GetString());
    }

    [Fact]
    public async Task Backfill_CarriesNotesBodyFromJsonIntoRelationalExtract()
    {
        var (db, _) = Build();
        var now = DateTimeOffset.UtcNow;
        var paper = new ContentPaper
        {
            Id = Guid.NewGuid().ToString("N"),
            SubtestCode = "listening",
            Title = "Backfill NotesBody",
            Slug = $"listening-{Guid.NewGuid():N}",
            Status = ContentStatus.Draft,
            CreatedAt = now,
            UpdatedAt = now,
            ExtractedTextJson = JsonSerializer.Serialize(new
            {
                listeningQuestions = new[]
                {
                    new { id = "a-1", number = 1, partCode = "A1", type = "short_answer", text = "Q: ____", correctAnswer = "x" },
                },
                listeningExtracts = new[]
                {
                    new { partCode = "A1", kind = "consultation", title = "Consultation 1", accentCode = "en-GB", notesBody = A1Body },
                },
            }),
        };
        db.ContentPapers.Add(paper);
        await db.SaveChangesAsync();

        var report = await new ListeningBackfillService(db).BackfillPaperAsync(paper.Id, "admin-1", default);
        Assert.True(report.Success);

        var a1Part = await db.ListeningParts.AsNoTracking()
            .SingleAsync(p => p.PaperId == paper.Id && p.PartCode == ListeningPartCode.A1);
        var a1Extract = await db.ListeningExtracts.AsNoTracking()
            .SingleAsync(e => e.ListeningPartId == a1Part.Id);
        Assert.Equal(A1Body, a1Extract.NotesBodyMarkdown);
    }

    [Fact]
    public async Task Backfill_BodyOnlyChange_WithLearnerAttempts_Succeeds()
    {
        var (db, _) = Build();
        var now = DateTimeOffset.UtcNow;

        string JsonWithBody(string body) => JsonSerializer.Serialize(new
        {
            listeningQuestions = new[]
            {
                new { id = "a-1", number = 1, partCode = "A1", type = "short_answer", text = "Q: ____", correctAnswer = "x" },
            },
            listeningExtracts = new[]
            {
                new { partCode = "A1", kind = "consultation", title = "Consultation 1", accentCode = "en-GB", notesBody = body },
            },
        });

        var paper = new ContentPaper
        {
            Id = Guid.NewGuid().ToString("N"),
            SubtestCode = "listening",
            Title = "Backfill Body-Only",
            Slug = $"listening-{Guid.NewGuid():N}",
            Status = ContentStatus.Draft,
            CreatedAt = now,
            UpdatedAt = now,
            ExtractedTextJson = JsonWithBody("Original body ____"),
        };
        db.ContentPapers.Add(paper);
        await db.SaveChangesAsync();

        var backfill = new ListeningBackfillService(db);
        var first = await backfill.BackfillPaperAsync(paper.Id, "admin-1", default);
        Assert.True(first.Success);

        // A learner attempt now exists — answer-key changes would be blocked.
        db.ListeningAttempts.Add(new ListeningAttempt
        {
            Id = "attempt-body-only",
            UserId = "learner-1",
            PaperId = paper.Id,
            StartedAt = now,
            LastActivityAt = now,
            Status = ListeningAttemptStatus.InProgress,
            Mode = ListeningAttemptMode.Home,
            MaxRawScore = 42,
            PolicySnapshotJson = "{}",
        });
        // Change ONLY the notesBody (cosmetic) — the answer key is untouched.
        paper.ExtractedTextJson = JsonWithBody("Edited body with more ____ detail");
        await db.SaveChangesAsync();

        var second = await backfill.BackfillPaperAsync(paper.Id, "admin-1", default);

        Assert.True(second.Success);
        var a1Part = await db.ListeningParts.AsNoTracking()
            .SingleAsync(p => p.PaperId == paper.Id && p.PartCode == ListeningPartCode.A1);
        var a1Extract = await db.ListeningExtracts.AsNoTracking()
            .SingleAsync(e => e.ListeningPartId == a1Part.Id);
        Assert.Equal("Edited body with more ____ detail", a1Extract.NotesBodyMarkdown);
    }
}
