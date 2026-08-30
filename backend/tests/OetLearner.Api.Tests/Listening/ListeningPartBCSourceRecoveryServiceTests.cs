using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Content;
using OetLearner.Api.Services.Listening;

namespace OetLearner.Api.Tests.Listening;

/// <summary>
/// Part B/C source recovery restores the printed question above an item from
/// the paper's OWN cached question-paper text. The invariants that matter to a
/// candidate: it never invents wording, never overwrites a stem a human
/// authored, never touches the answer key, and reports whatever the source
/// cannot support instead of leaving it silently blank.
/// </summary>
public class ListeningPartBCSourceRecoveryServiceTests
{
    private const string PaperId = "paper-atlas-08";
    private const string QuestionPaperAssetId = "asset-qp-1";

    // The shape a question paper's extracted text takes: printed number, the
    // context + question, then the three options.
    private const string QuestionPaperText = """
        Part B

        27. You hear the beginning of a training session for nurses about to start work on a paediatric ward. What is the focus of today's session?
        A comparing equipment used with patients of different ages B gaining an awareness of how some equipment is used C learning how best to organise some equipment

        28. You hear an occupational therapist briefing a trainee about a home visit. What is the priority for today's visit?
        A helping the patient to regain independence in everyday tasks B meeting a family member who has concerns about the patient C ensuring that a mechanical device is appropriate for the patient
        """;

    private static LearnerDbContext NewDb() => new(
        new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static async Task SeedPaperAsync(
        LearnerDbContext db,
        string q27Stem,
        string q28Stem,
        string? questionPaperText = QuestionPaperText,
        string[]? q27OptionTexts = null)
    {
        var now = DateTimeOffset.UtcNow;
        var extracted = new Dictionary<string, object?>
        {
            [QuestionPaperAssetId] = questionPaperText,
            ["listeningQuestions"] = new object[]
            {
                new Dictionary<string, object?>
                {
                    ["number"] = 27, ["partCode"] = "B", ["stem"] = q27Stem, ["type"] = "multiple_choice_3",
                    ["options"] = new[] { "Option A", "Option B", "Option C" },
                    ["correctAnswer"] = "B",
                },
                new Dictionary<string, object?>
                {
                    ["number"] = 28, ["partCode"] = "B", ["stem"] = q28Stem, ["type"] = "multiple_choice_3",
                    ["options"] = new[] { "keeps its own wording", "second", "third" },
                    ["correctAnswer"] = "A",
                },
            },
        };

        db.ContentPapers.Add(new ContentPaper
        {
            Id = PaperId,
            SubtestCode = "listening",
            Title = "Atlas Practice Series — Listening Sample Test 08",
            Slug = "atlas-practice-series-listening-sample-test-08",
            Difficulty = "standard",
            EstimatedDurationMinutes = 42,
            Status = ContentStatus.Published,
            ExtractedTextJson = JsonSerializer.Serialize(extracted),
            CreatedAt = now,
            UpdatedAt = now,
        });

        db.ContentPaperAssets.Add(new ContentPaperAsset
        {
            Id = QuestionPaperAssetId,
            PaperId = PaperId,
            Role = PaperAssetRole.QuestionPaper,
            MediaAssetId = "media-qp-1",
            IsPrimary = true,
            CreatedAt = now,
        });

        db.ListeningQuestions.AddRange(
            NewQuestion("q-27", 27, q27Stem),
            NewQuestion("q-28", 28, q28Stem));

        var q27Options = q27OptionTexts ?? ["", "", ""];
        db.ListeningQuestionOptions.AddRange(
            NewOption("o-27-a", "q-27", "A", 0, q27Options[0], isCorrect: false),
            NewOption("o-27-b", "q-27", "B", 1, q27Options[1], isCorrect: true),
            NewOption("o-27-c", "q-27", "C", 2, q27Options[2], isCorrect: false),
            NewOption("o-28-a", "q-28", "A", 0, "keeps its own wording", isCorrect: true),
            NewOption("o-28-b", "q-28", "B", 1, "second", isCorrect: false),
            NewOption("o-28-c", "q-28", "C", 2, "third", isCorrect: false));

        await db.SaveChangesAsync();
    }

    private static ListeningQuestion NewQuestion(string id, int number, string stem) => new()
    {
        Id = id,
        PaperId = PaperId,
        ListeningPartId = "part-b",
        QuestionNumber = number,
        DisplayOrder = number,
        Points = 1,
        QuestionType = ListeningQuestionType.MultipleChoice3,
        Stem = stem,
        CorrectAnswerJson = "\"B\"",
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
    };

    private static ListeningQuestionOption NewOption(
        string id, string questionId, string key, int order, string text, bool isCorrect) => new()
    {
        Id = id,
        ListeningQuestionId = questionId,
        OptionKey = key,
        DisplayOrder = order,
        Text = text,
        IsCorrect = isCorrect,
    };

    [Fact]
    public async Task Restores_a_blank_stem_from_the_papers_own_question_paper_text()
    {
        await using var db = NewDb();
        // Q27 is the state migration 20261129000000 left behind: blank stem,
        // blank options. Q28 already carries real authored wording.
        await SeedPaperAsync(db, q27Stem: "", q28Stem: "You hear an occupational therapist briefing a trainee. What is the priority for today's visit?");

        var report = await new ListeningPartBCSourceRecoveryService(db)
            .RecoverPaperAsync(PaperId, dryRun: false, adminId: "admin-1", CancellationToken.None);

        Assert.True(report.SourceTextAvailable);
        Assert.Equal(1, report.Recovered);
        Assert.Equal(1, report.AlreadyUsable);
        Assert.Equal(0, report.Unrecoverable);

        var q27 = await db.ListeningQuestions.SingleAsync(q => q.Id == "q-27");
        Assert.Equal(
            "You hear the beginning of a training session for nurses about to start work on a paediatric ward. What is the focus of today's session?",
            q27.Stem);
    }

    [Fact]
    public async Task Restores_blank_option_prose_without_changing_the_answer_key()
    {
        await using var db = NewDb();
        await SeedPaperAsync(db, q27Stem: "", q28Stem: "A real authored Part B question?");

        await new ListeningPartBCSourceRecoveryService(db)
            .RecoverPaperAsync(PaperId, dryRun: false, adminId: "admin-1", CancellationToken.None);

        var options = await db.ListeningQuestionOptions
            .Where(o => o.ListeningQuestionId == "q-27")
            .OrderBy(o => o.DisplayOrder)
            .ToListAsync();

        Assert.Equal("comparing equipment used with patients of different ages", options[0].Text);
        Assert.Equal("gaining an awareness of how some equipment is used", options[1].Text);
        Assert.Equal("learning how best to organise some equipment", options[2].Text);

        // The correct option is still B, and the keys are untouched — a stored
        // learner answer of "B" must keep meaning the same thing.
        Assert.Equal(["A", "B", "C"], options.Select(o => o.OptionKey));
        Assert.True(options.Single(o => o.OptionKey == "B").IsCorrect);
        Assert.Equal(1, options.Count(o => o.IsCorrect));
    }

    [Fact]
    public async Task Never_overwrites_wording_that_is_already_readable()
    {
        await using var db = NewDb();
        const string authored = "You hear an occupational therapist briefing a trainee about a home visit that the team already reviewed. What is the priority?";
        await SeedPaperAsync(db, q27Stem: "", q28Stem: authored);

        await new ListeningPartBCSourceRecoveryService(db)
            .RecoverPaperAsync(PaperId, dryRun: false, adminId: "admin-1", CancellationToken.None);

        var q28 = await db.ListeningQuestions.SingleAsync(q => q.Id == "q-28");
        Assert.Equal(authored, q28.Stem);
        var q28OptionA = await db.ListeningQuestionOptions.SingleAsync(o => o.Id == "o-28-a");
        Assert.Equal("keeps its own wording", q28OptionA.Text);
    }

    [Fact]
    public async Task Rejects_the_generic_heading_and_recovers_the_printed_question_instead()
    {
        await using var db = NewDb();
        // The exact heading migration 20261128000000 wrote across every paper.
        await SeedPaperAsync(
            db,
            q27Stem: "What does the speaker identify as the main clinical priority?",
            q28Stem: "A real authored Part B question?");

        var report = await new ListeningPartBCSourceRecoveryService(db)
            .RecoverPaperAsync(PaperId, dryRun: false, adminId: "admin-1", CancellationToken.None);

        Assert.Equal(1, report.Recovered);
        var q27 = await db.ListeningQuestions.SingleAsync(q => q.Id == "q-27");
        Assert.DoesNotContain("main clinical priority", q27.Stem, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("You hear the beginning of a training session", q27.Stem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_dry_run_reports_everything_and_writes_nothing()
    {
        await using var db = NewDb();
        await SeedPaperAsync(db, q27Stem: "", q28Stem: "A real authored Part B question?");

        var report = await new ListeningPartBCSourceRecoveryService(db)
            .RecoverPaperAsync(PaperId, dryRun: true, adminId: "admin-1", CancellationToken.None);

        Assert.True(report.DryRun);
        Assert.Equal(1, report.Recovered);

        await using var verify = NewDb();
        // A fresh context on the same in-memory store would show a write; assert
        // against the seeded store directly instead.
        var q27 = await db.ListeningQuestions.AsNoTracking().SingleAsync(q => q.Id == "q-27");
        Assert.Equal(string.Empty, q27.Stem);
        Assert.Empty(await db.AuditEvents.ToListAsync());
    }

    [Fact]
    public async Task Reports_an_item_the_source_text_cannot_support_instead_of_inventing_one()
    {
        await using var db = NewDb();
        // Source text that has no printed Q27 at all.
        await SeedPaperAsync(
            db,
            q27Stem: "",
            q28Stem: "A real authored Part B question?",
            questionPaperText: "28. You hear an occupational therapist. What is the priority?\nA first B second C third");

        var report = await new ListeningPartBCSourceRecoveryService(db)
            .RecoverPaperAsync(PaperId, dryRun: false, adminId: "admin-1", CancellationToken.None);

        Assert.Equal(0, report.Recovered);
        Assert.Equal(1, report.Unrecoverable);
        var item = report.Items.Single(i => i.Number == 27);
        Assert.Equal("unrecoverable", item.Status);
        Assert.False(string.IsNullOrWhiteSpace(item.Detail));

        var q27 = await db.ListeningQuestions.SingleAsync(q => q.Id == "q-27");
        Assert.Equal(string.Empty, q27.Stem);
    }

    [Fact]
    public async Task Reports_a_paper_whose_pdf_text_was_never_extracted()
    {
        await using var db = NewDb();
        await SeedPaperAsync(db, q27Stem: "", q28Stem: "", questionPaperText: "");

        var report = await new ListeningPartBCSourceRecoveryService(db)
            .RecoverPaperAsync(PaperId, dryRun: true, adminId: "admin-1", CancellationToken.None);

        Assert.False(report.SourceTextAvailable);
        Assert.Equal(2, report.Unrecoverable);
        Assert.All(report.Items, item =>
            Assert.Contains("extract", item.Detail ?? string.Empty, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Mirrors_the_recovered_wording_into_the_authored_json()
    {
        await using var db = NewDb();
        await SeedPaperAsync(db, q27Stem: "", q28Stem: "A real authored Part B question?");

        await new ListeningPartBCSourceRecoveryService(db)
            .RecoverPaperAsync(PaperId, dryRun: false, adminId: "admin-1", CancellationToken.None);

        var paper = await db.ContentPapers.SingleAsync(p => p.Id == PaperId);
        using var document = JsonDocument.Parse(paper.ExtractedTextJson);
        var authored = document.RootElement.GetProperty("listeningQuestions");
        var q27 = authored.EnumerateArray().Single(item => item.GetProperty("number").GetInt32() == 27);

        Assert.StartsWith(
            "You hear the beginning of a training session",
            q27.GetProperty("stem").GetString(),
            StringComparison.Ordinal);
        // The per-asset raw text entry must survive untouched.
        Assert.Equal(QuestionPaperText, document.RootElement.GetProperty(QuestionPaperAssetId).GetString());
    }

    [Fact]
    public async Task Writes_one_audit_event_naming_the_repaired_numbers()
    {
        await using var db = NewDb();
        await SeedPaperAsync(db, q27Stem: "", q28Stem: "A real authored Part B question?");

        await new ListeningPartBCSourceRecoveryService(db)
            .RecoverPaperAsync(PaperId, dryRun: false, adminId: "admin-7", CancellationToken.None);

        var audit = Assert.Single(await db.AuditEvents.ToListAsync());
        Assert.Equal("ListeningPartBCSourceStemsRecovered", audit.Action);
        Assert.Equal(PaperId, audit.ResourceId);
        Assert.Equal("admin-7", audit.ActorId);
        Assert.Contains("27", audit.Details ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Refuses_a_paper_from_another_subtest()
    {
        await using var db = NewDb();
        var now = DateTimeOffset.UtcNow;
        db.ContentPapers.Add(new ContentPaper
        {
            Id = "reading-1",
            SubtestCode = "reading",
            Title = "Reading paper",
            Slug = "reading-paper",
            Difficulty = "standard",
            Status = ContentStatus.Published,
            CreatedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();

        var service = new ListeningPartBCSourceRecoveryService(db);
        await Assert.ThrowsAsync<ApiException>(() =>
            service.RecoverPaperAsync("reading-1", dryRun: true, adminId: "admin-1", CancellationToken.None));
    }
    // ── Re-extraction fallback + fleet sweep ─────────────────────────────

    /// <summary>
    /// Stands in for the PDF pipeline. A paper ingested before the extractor was
    /// configured has an empty cached entry, and the normal extraction pass
    /// skips any asset that already has one — so without a forced retry the
    /// paper stays blank forever and recovery finds nothing.
    /// </summary>
    private sealed class StubTextExtraction(LearnerDbContext db, string? textToWrite)
        : IContentTextExtractionService
    {
        public int Calls { get; private set; }
        public bool LastForce { get; private set; }

        public async Task<int> ExtractForPaperAsync(string paperId, CancellationToken ct, bool force = false)
        {
            Calls++;
            LastForce = force;
            if (textToWrite is null) return 0;

            var paper = await db.ContentPapers.FirstAsync(p => p.Id == paperId, ct);
            var root = JsonSerializer.Deserialize<Dictionary<string, object?>>(paper.ExtractedTextJson)
                       ?? new Dictionary<string, object?>();
            root[QuestionPaperAssetId] = textToWrite;
            paper.ExtractedTextJson = JsonSerializer.Serialize(root);
            await db.SaveChangesAsync(ct);
            return 1;
        }
    }

    [Fact]
    public async Task Re_extracts_the_pdf_when_the_cached_text_is_empty_then_recovers()
    {
        await using var db = NewDb();
        await SeedPaperAsync(db, q27Stem: "", q28Stem: "A real authored Part B question?", questionPaperText: "");
        var extraction = new StubTextExtraction(db, QuestionPaperText);

        var report = await new ListeningPartBCSourceRecoveryService(db, extraction)
            .RecoverPaperAsync(PaperId, dryRun: false, adminId: "admin-1", CancellationToken.None);

        Assert.Equal(1, extraction.Calls);
        Assert.True(extraction.LastForce);
        Assert.True(report.SourceTextAvailable);
        Assert.Equal(1, report.Recovered);

        var q27 = await db.ListeningQuestions.SingleAsync(q => q.Id == "q-27");
        Assert.StartsWith("You hear the beginning of a training session", q27.Stem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reports_cleanly_when_re_extraction_still_yields_no_text()
    {
        await using var db = NewDb();
        await SeedPaperAsync(db, q27Stem: "", q28Stem: "", questionPaperText: "");
        var extraction = new StubTextExtraction(db, textToWrite: null);

        var report = await new ListeningPartBCSourceRecoveryService(db, extraction)
            .RecoverPaperAsync(PaperId, dryRun: true, adminId: "admin-1", CancellationToken.None);

        Assert.Equal(1, extraction.Calls);
        Assert.False(report.SourceTextAvailable);
        Assert.Equal(2, report.Unrecoverable);
        Assert.Equal(0, report.Recovered);
    }

    [Fact]
    public async Task Does_not_re_extract_when_the_cached_text_already_works()
    {
        await using var db = NewDb();
        await SeedPaperAsync(db, q27Stem: "", q28Stem: "A real authored Part B question?");
        var extraction = new StubTextExtraction(db, QuestionPaperText);

        await new ListeningPartBCSourceRecoveryService(db, extraction)
            .RecoverPaperAsync(PaperId, dryRun: false, adminId: "admin-1", CancellationToken.None);

        Assert.Equal(0, extraction.Calls);
    }

    [Fact]
    public async Task Sweep_covers_every_listening_paper_and_totals_the_outcome()
    {
        await using var db = NewDb();
        await SeedPaperAsync(db, q27Stem: "", q28Stem: "A real authored Part B question?");

        // A second Listening paper whose source cannot support Q27.
        var now = DateTimeOffset.UtcNow;
        db.ContentPapers.Add(new ContentPaper
        {
            Id = "paper-nova-01",
            SubtestCode = "listening",
            Title = "Nova Practice Series — Listening Sample Test 01",
            Slug = "nova-practice-series-listening-sample-test-01",
            Difficulty = "standard",
            Status = ContentStatus.Published,
            ExtractedTextJson = "{}",
            CreatedAt = now,
            UpdatedAt = now,
        });
        db.ListeningQuestions.Add(new ListeningQuestion
        {
            Id = "nova-q-27",
            PaperId = "paper-nova-01",
            ListeningPartId = "part-b",
            QuestionNumber = 27,
            DisplayOrder = 27,
            Points = 1,
            QuestionType = ListeningQuestionType.MultipleChoice3,
            Stem = "",
            CorrectAnswerJson = "\"B\"",
            CreatedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();

        var sweep = await new ListeningPartBCSourceRecoveryService(db)
            .RecoverAllAsync(publishedOnly: true, dryRun: false, adminId: "admin-1", CancellationToken.None);

        Assert.Equal(2, sweep.PapersScanned);
        Assert.Equal(1, sweep.PapersChanged);
        Assert.Equal(1, sweep.TotalRecovered);
        Assert.Equal(1, sweep.TotalStillUnrecoverable);
        Assert.Equal(1, sweep.PapersWithoutSourceText);
        Assert.Empty(sweep.Failures);
        Assert.False(sweep.DryRun);

        // The Atlas paper was actually written; the Nova one was left blank
        // rather than filled with a guess.
        Assert.StartsWith(
            "You hear the beginning of a training session",
            (await db.ListeningQuestions.SingleAsync(q => q.Id == "q-27")).Stem,
            StringComparison.Ordinal);
        Assert.Equal(string.Empty, (await db.ListeningQuestions.SingleAsync(q => q.Id == "nova-q-27")).Stem);
    }

    // ── Papers whose Part B/C content is JSON-only (no relational rows) ─────

    [Fact]
    public async Task Recovers_a_paper_with_no_relational_rows_by_patching_the_authored_json()
    {
        await using var db = NewDb();
        var now = DateTimeOffset.UtcNow;
        var extracted = new Dictionary<string, object?>
        {
            [QuestionPaperAssetId] = QuestionPaperText,
            ["listeningQuestions"] = new object[]
            {
                new Dictionary<string, object?>
                {
                    ["number"] = 27, ["partCode"] = "B", ["type"] = "multiple_choice_3",
                    ["stem"] = "What does the speaker identify as the main clinical priority?",
                    ["options"] = new[]
                    {
                        "comparing equipment used with patients of different ages",
                        "gaining an awareness of how some equipment is used",
                        "learning how best to organise some equipment",
                    },
                    ["correctAnswer"] = "B",
                },
                new Dictionary<string, object?>
                {
                    ["number"] = 28, ["partCode"] = "B", ["type"] = "multiple_choice_3",
                    ["stem"] = "A real authored Part B question that already reads fine?",
                    ["options"] = new[] { "keeps its own wording", "second", "third" },
                    ["correctAnswer"] = "A",
                },
            },
        };

        db.ContentPapers.Add(new ContentPaper
        {
            Id = "paper-json-only",
            SubtestCode = "listening",
            Title = "Nova Practice Series — Listening Test JSON-only",
            Slug = "nova-practice-series-listening-test-json-only",
            Difficulty = "standard",
            EstimatedDurationMinutes = 42,
            Status = ContentStatus.Published,
            ExtractedTextJson = JsonSerializer.Serialize(extracted),
            CreatedAt = now,
            UpdatedAt = now,
        });
        db.ContentPaperAssets.Add(new ContentPaperAsset
        {
            Id = QuestionPaperAssetId,
            PaperId = "paper-json-only",
            Role = PaperAssetRole.QuestionPaper,
            MediaAssetId = "media-qp-json-only",
            IsPrimary = true,
            CreatedAt = now,
        });
        // Deliberately NO db.ListeningQuestions rows — this is the bulk-import
        // shape where Part B/C content lives only in the JSON projection, which
        // the relational recovery path above cannot see at all.
        await db.SaveChangesAsync();

        var report = await new ListeningPartBCSourceRecoveryService(db)
            .RecoverPaperAsync("paper-json-only", dryRun: false, adminId: "admin-1", CancellationToken.None);

        Assert.Equal(2, report.PartBCQuestionCount);
        Assert.Equal(1, report.Recovered);
        Assert.Equal(1, report.AlreadyUsable);
        Assert.Equal(0, report.Unrecoverable);

        var paper = await db.ContentPapers.SingleAsync(p => p.Id == "paper-json-only");
        using var document = JsonDocument.Parse(paper.ExtractedTextJson);
        var authored = document.RootElement.GetProperty("listeningQuestions");
        var q27 = authored.EnumerateArray().Single(item => item.GetProperty("number").GetInt32() == 27);
        var q28 = authored.EnumerateArray().Single(item => item.GetProperty("number").GetInt32() == 28);

        Assert.StartsWith(
            "You hear the beginning of a training session",
            q27.GetProperty("stem").GetString(),
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "main clinical priority", q27.GetProperty("stem").GetString(), StringComparison.OrdinalIgnoreCase);
        // Already-usable wording is left exactly as authored.
        Assert.Equal(
            "A real authored Part B question that already reads fine?", q28.GetProperty("stem").GetString());

        var audit = Assert.Single(await db.AuditEvents.ToListAsync());
        Assert.Equal("ListeningPartBCSourceStemsRecovered", audit.Action);
        Assert.Equal("paper-json-only", audit.ResourceId);
    }

    [Fact]
    public async Task A_json_only_paper_dry_run_reports_and_writes_nothing()
    {
        await using var db = NewDb();
        var now = DateTimeOffset.UtcNow;
        var extracted = new Dictionary<string, object?>
        {
            [QuestionPaperAssetId] = QuestionPaperText,
            ["listeningQuestions"] = new object[]
            {
                new Dictionary<string, object?>
                {
                    ["number"] = 27, ["partCode"] = "B", ["type"] = "multiple_choice_3",
                    ["stem"] = "What does the speaker identify as the main clinical priority?",
                    ["options"] = new[]
                    {
                        "comparing equipment used with patients of different ages",
                        "gaining an awareness of how some equipment is used",
                        "learning how best to organise some equipment",
                    },
                    ["correctAnswer"] = "B",
                },
            },
        };

        db.ContentPapers.Add(new ContentPaper
        {
            Id = "paper-json-only-2",
            SubtestCode = "listening",
            Title = "Nova Practice Series — Listening Test JSON-only 2",
            Slug = "nova-practice-series-listening-test-json-only-2",
            Difficulty = "standard",
            EstimatedDurationMinutes = 42,
            Status = ContentStatus.Published,
            ExtractedTextJson = JsonSerializer.Serialize(extracted),
            CreatedAt = now,
            UpdatedAt = now,
        });
        db.ContentPaperAssets.Add(new ContentPaperAsset
        {
            Id = QuestionPaperAssetId,
            PaperId = "paper-json-only-2",
            Role = PaperAssetRole.QuestionPaper,
            MediaAssetId = "media-qp-json-only-2",
            IsPrimary = true,
            CreatedAt = now,
        });
        await db.SaveChangesAsync();

        var report = await new ListeningPartBCSourceRecoveryService(db)
            .RecoverPaperAsync("paper-json-only-2", dryRun: true, adminId: "admin-1", CancellationToken.None);

        Assert.True(report.DryRun);
        Assert.Equal(1, report.Recovered);

        var paper = await db.ContentPapers.AsNoTracking().SingleAsync(p => p.Id == "paper-json-only-2");
        using var document = JsonDocument.Parse(paper.ExtractedTextJson);
        var q27 = document.RootElement.GetProperty("listeningQuestions").EnumerateArray().Single();
        Assert.Equal(
            "What does the speaker identify as the main clinical priority?",
            q27.GetProperty("stem").GetString());
        Assert.Empty(await db.AuditEvents.ToListAsync());
    }

    [Fact]
    public async Task Sweep_ignores_non_listening_papers()
    {
        await using var db = NewDb();
        await SeedPaperAsync(db, q27Stem: "", q28Stem: "A real authored Part B question?");
        var now = DateTimeOffset.UtcNow;
        db.ContentPapers.Add(new ContentPaper
        {
            Id = "reading-1", SubtestCode = "reading", Title = "Reading paper", Slug = "reading-paper",
            Difficulty = "standard", Status = ContentStatus.Published, CreatedAt = now, UpdatedAt = now,
        });
        await db.SaveChangesAsync();

        var sweep = await new ListeningPartBCSourceRecoveryService(db)
            .RecoverAllAsync(publishedOnly: true, dryRun: true, adminId: "admin-1", CancellationToken.None);

        Assert.Equal(1, sweep.PapersScanned);
        Assert.All(sweep.Papers, r => Assert.Equal(PaperId, r.PaperId));
    }
}
