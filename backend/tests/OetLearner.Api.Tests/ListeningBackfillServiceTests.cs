using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Listening;

namespace OetLearner.Api.Tests;

/// <summary>
/// Unit tests for <see cref="ListeningBackfillService"/> — projects the
/// JSON-blob authored shape under
/// <c>ContentPaper.ExtractedTextJson["listeningQuestions"]</c> into the
/// relational <c>ListeningPart</c> / <c>Extract</c> / <c>Question</c> /
/// <c>Option</c> tables.
/// </summary>
public class ListeningBackfillServiceTests
{
    private static (LearnerDbContext db, ListeningBackfillService svc) Build()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var db = new LearnerDbContext(options);
        return (db, new ListeningBackfillService(db));
    }

    private static async Task<ContentPaper> AddPaperAsync(LearnerDbContext db, string extractedTextJson)
    {
        var now = DateTimeOffset.UtcNow;
        var paper = new ContentPaper
        {
            Id = Guid.NewGuid().ToString("N"),
            SubtestCode = "listening",
            Title = "Listening Backfill Test",
            Slug = $"listening-{Guid.NewGuid():N}",
            Status = ContentStatus.Draft,
            ExtractedTextJson = extractedTextJson,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.ContentPapers.Add(paper);
        await db.SaveChangesAsync();
        return paper;
    }

    private static string BuildCanonicalJson()
    {
        var list = new List<object>();
        var num = 1;
        for (var i = 0; i < 24; i++)
            list.Add(new
            {
                id = $"a-{i}",
                number = num++,
                partCode = i < 12 ? "A1" : "A2",
                type = "short_answer",
                text = $"Part A item {i + 1}",
                correctAnswer = $"answer-{i}",
                acceptedAnswers = new[] { $"answer-{i}", $"alt-{i}" },
                skillTag = "numbers_units",
                transcriptExcerpt = $"…answer-{i}…",
                transcriptEvidenceStartMs = i * 1000,
                transcriptEvidenceEndMs = i * 1000 + 800,
            });
        for (var i = 0; i < 6; i++)
            list.Add(new
            {
                id = $"b-{i}",
                number = num++,
                partCode = "B",
                type = "multiple_choice_3",
                text = $"Part B item {i + 1}",
                options = new[] { "First option", "Second option", "Third option" },
                correctAnswer = "First option",
                optionDistractorCategory = new[] { (string?)null, "too_strong", "out_of_scope" },
                optionDistractorWhy = new[] { (string?)null, "Stronger than the speaker said.", "This was the wrong speaker." },
            });
        for (var i = 0; i < 12; i++)
            list.Add(new
            {
                id = $"c-{i}",
                number = num++,
                partCode = i < 6 ? "C1" : "C2",
                type = "multiple_choice_3",
                text = $"Part C item {i + 1}",
                options = new[] { "Alpha", "Beta", "Gamma" },
                correctAnswer = i % 3 == 0 ? "Alpha" : (i % 3 == 1 ? "Beta" : "Gamma"),
                speakerAttitude = i % 2 == 0 ? "concerned" : "optimistic",
            });

        var extracts = new[]
        {
            new { partCode = "A1", kind = "consultation", title = "Consultation 1", accentCode = "en-GB",
                  speakers = new[] { new { id = "s1", role = "GP", gender = "f" } }, audioStartMs = 0, audioEndMs = 300_000 },
            new { partCode = "A2", kind = "consultation", title = "Consultation 2", accentCode = "en-AU",
                  speakers = new[] { new { id = "s1", role = "Specialist", gender = "m" } }, audioStartMs = 300_000, audioEndMs = 600_000 },
        };
        var transcript = new[]
        {
            new { startMs = 0, endMs = 4000, partCode = "A1", speakerId = "s1", text = "Doctor: hello." },
            new { startMs = 4001, endMs = 8000, partCode = "A2", speakerId = "s1", text = "Doctor: welcome back." },
        };
        return JsonSerializer.Serialize(new
        {
            listeningQuestions = list,
            listeningExtracts = extracts,
            listeningTranscriptSegments = transcript,
        });
    }

    [Fact]
    public async Task Backfill_BuildsCanonical_Parts_Extracts_Questions_Options()
    {
        var (db, svc) = Build();
        var paper = await AddPaperAsync(db, BuildCanonicalJson());

        var report = await svc.BackfillPaperAsync(paper.Id, "admin-1", default);

        Assert.True(report.Success);
        Assert.Equal(5, report.PartsCreated);     // A1, A2, B, C1, C2
        Assert.Equal(5, report.ExtractsCreated);  // one per part (default per-part extract)
        Assert.Equal(42, report.QuestionsCreated);
        // 6 Part B + 12 Part C MCQ = 18 questions × 3 options = 54 options
        Assert.Equal(54, report.OptionsCreated);

        var parts = await db.ListeningParts.AsNoTracking().Where(p => p.PaperId == paper.Id).OrderBy(p => p.PartCode).ToListAsync();
        Assert.Equal(new[]
        {
            ListeningPartCode.A1, ListeningPartCode.A2, ListeningPartCode.B,
            ListeningPartCode.C1, ListeningPartCode.C2,
        }, parts.Select(p => p.PartCode));

        var partA1 = parts.Single(p => p.PartCode == ListeningPartCode.A1);
        Assert.Equal(12, partA1.MaxRawScore);

        // Phase 5 metadata round-trips into the relational extract row.
        var a1Extract = await db.ListeningExtracts.AsNoTracking()
            .SingleAsync(e => e.ListeningPartId == partA1.Id);
        Assert.Equal(ListeningExtractKind.Consultation, a1Extract.Kind);
        Assert.Equal("en-GB", a1Extract.AccentCode);
        Assert.Equal(0, a1Extract.AudioStartMs);
        Assert.Equal(300_000, a1Extract.AudioEndMs);
        Assert.Contains("\"role\":\"GP\"", a1Extract.SpeakersJson);
        Assert.Contains("\"text\":\"Doctor: hello.\"", a1Extract.TranscriptSegmentsJson);

        var qs = await db.ListeningQuestions.AsNoTracking().Where(q => q.PaperId == paper.Id).ToListAsync();
        Assert.Equal(42, qs.Count);
        Assert.Equal(Enumerable.Range(1, 42), qs.OrderBy(q => q.QuestionNumber).Select(q => q.QuestionNumber));

        // Part C1 question[0]: speakerAttitude="concerned" round-trips.
        var c1Q = qs.OrderBy(q => q.QuestionNumber).First(q => q.QuestionNumber == 31);
        Assert.Equal(ListeningSpeakerAttitude.Concerned, c1Q.SpeakerAttitude);

        // Options with distractor category + why-wrong round-trip.
        var bQ = qs.Single(q => q.QuestionNumber == 25);
        var bOpts = await db.ListeningQuestionOptions.AsNoTracking()
            .Where(o => o.ListeningQuestionId == bQ.Id)
            .OrderBy(o => o.DisplayOrder)
            .ToListAsync();
        Assert.Equal(3, bOpts.Count);
        Assert.True(bOpts[0].IsCorrect);   // "First option" matches correctAnswer
        Assert.Null(bOpts[0].DistractorCategory);
        Assert.Equal(ListeningDistractorCategory.TooStrong, bOpts[1].DistractorCategory);
        Assert.Equal("Stronger than the speaker said.", bOpts[1].WhyWrongMarkdown);
        Assert.Equal(ListeningDistractorCategory.OutOfScope, bOpts[2].DistractorCategory);
    }

    [Fact]
    public async Task Backfill_IsIdempotent_RewritesEachRun()
    {
        var (db, svc) = Build();
        var paper = await AddPaperAsync(db, BuildCanonicalJson());

        var first = await svc.BackfillPaperAsync(paper.Id, "admin-1", default);
        var second = await svc.BackfillPaperAsync(paper.Id, "admin-1", default);

        Assert.True(first.Success);
        Assert.True(second.Success);
        Assert.Equal(first.QuestionsCreated, second.QuestionsCreated);
        Assert.Equal(first.OptionsCreated, second.OptionsCreated);

        // Second run should not duplicate rows.
        var partCount = await db.ListeningParts.CountAsync(p => p.PaperId == paper.Id);
        var qCount = await db.ListeningQuestions.CountAsync(q => q.PaperId == paper.Id);
        Assert.Equal(5, partCount);
        Assert.Equal(42, qCount);
    }

    [Fact]
    public async Task Backfill_RefusesWhenRelationalAttemptsExist()
    {
        var (db, svc) = Build();
        var paper = await AddPaperAsync(db, BuildCanonicalJson());
        var first = await svc.BackfillPaperAsync(paper.Id, "admin-1", default);
        Assert.True(first.Success);
        var questionCountBefore = await db.ListeningQuestions.CountAsync(q => q.PaperId == paper.Id);
        var now = DateTimeOffset.UtcNow;
        db.ListeningAttempts.Add(new ListeningAttempt
        {
            Id = "attempt-protect-backfill",
            UserId = "learner-1",
            PaperId = paper.Id,
            StartedAt = now,
            LastActivityAt = now,
            Status = ListeningAttemptStatus.InProgress,
            Mode = ListeningAttemptMode.Home,
            MaxRawScore = 42,
            PolicySnapshotJson = "{}",
        });
        await db.SaveChangesAsync();

        var second = await svc.BackfillPaperAsync(paper.Id, "admin-1", default);

        Assert.False(second.Success);
        Assert.Contains("learner attempts", second.Reason);
        Assert.Equal(questionCountBefore, await db.ListeningQuestions.CountAsync(q => q.PaperId == paper.Id));
    }

    [Fact]
    public async Task Backfill_EmptyJson_ReturnsFailure_NoRowsWritten()
    {
        var (db, svc) = Build();
        var paper = await AddPaperAsync(db, "{}");

        var report = await svc.BackfillPaperAsync(paper.Id, "admin-1", default);

        Assert.False(report.Success);
        Assert.Equal(0, report.QuestionsCreated);
        Assert.Equal(0, await db.ListeningParts.CountAsync(p => p.PaperId == paper.Id));
        Assert.Contains("listeningQuestions", report.Reason);
    }

    [Fact]
    public async Task Backfill_NonListeningPaper_RefusesGracefully()
    {
        var (db, svc) = Build();
        var paper = new ContentPaper
        {
            Id = Guid.NewGuid().ToString("N"),
            SubtestCode = "reading", // not listening
            Title = "Reading Paper",
            Slug = $"r-{Guid.NewGuid():N}",
            Status = ContentStatus.Draft,
            ExtractedTextJson = "{}",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.ContentPapers.Add(paper);
        await db.SaveChangesAsync();

        var report = await svc.BackfillPaperAsync(paper.Id, "admin-1", default);

        Assert.False(report.Success);
        Assert.Contains("not a Listening paper", report.Reason);
    }
}
