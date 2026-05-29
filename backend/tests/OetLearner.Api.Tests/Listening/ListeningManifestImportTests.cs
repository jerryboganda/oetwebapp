using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Listening;

namespace OetLearner.Api.Tests.Listening;

/// <summary>
/// WS5 — full-test §19 JSON manifest import / export for
/// <see cref="ListeningAuthoringService"/>. Verifies a complete manifest
/// projects to the 42-item authored structure (A24 / B6 / C12) plus 5 extracts,
/// the additive-vs-replace contract, the learner-attempt guard, and a clean
/// export → import round-trip. Mirrors the in-memory DbContext + stubbed
/// backfill setup used by <c>ListeningAuthoringServiceTests</c>.
/// </summary>
public class ListeningManifestImportTests
{
    private static (LearnerDbContext db, ListeningAuthoringService svc) Build()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var db = new LearnerDbContext(options);
        return (db, new ListeningAuthoringService(db, new RecordingBackfillService()));
    }

    private static async Task<ContentPaper> SeedPaperAsync(LearnerDbContext db)
    {
        var now = DateTimeOffset.UtcNow;
        var paper = new ContentPaper
        {
            Id = Guid.NewGuid().ToString("N"),
            SubtestCode = "listening",
            Title = "Listening Mock Test 01",
            Slug = $"listening-{Guid.NewGuid():N}",
            Status = ContentStatus.Draft,
            // ReplaceStructureAsync enforces SourceProvenance at mutation time.
            SourceProvenance = "owner=Acme; legal=original-authoring-attested",
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.ContentPapers.Add(paper);
        await db.SaveChangesAsync();
        return paper;
    }

    // ── Manifest builders ───────────────────────────────────────────────────

    /// <summary>Build the canonical full §19 manifest: 2 Part A consultations
    /// (12 gaps each, Q1–24), 6 Part B workplace MCQs (Q25–30), and 2 Part C
    /// presentations (6 MCQs each, Q31–42) — 42 questions, 5 extracts.</summary>
    private static ListeningStructureManifest CanonicalManifest()
    {
        var partAExtracts = new List<ListeningExtractManifest>();
        var number = 1;
        for (var extractNumber = 1; extractNumber <= 2; extractNumber++)
        {
            var questions = new List<ListeningQuestionManifest>();
            for (var g = 0; g < 12; g++, number++)
            {
                questions.Add(new ListeningQuestionManifest(
                    Number: number,
                    Type: "gap_fill",
                    NoteTextBeforeGap: $"Patient reports",
                    Stem: null,
                    Options: null,
                    CorrectAnswer: $"answer {number}",
                    AcceptedAnswers: new[] { $"answer {number}", $"ans {number}" },
                    Explanation: $"Explanation {number}",
                    DistractorExplanation: null,
                    SkillTag: "specific-detail",
                    Timestamp: "01:30",
                    TranscriptEvidenceStartMs: null,
                    TranscriptEvidenceEndMs: null,
                    TranscriptExcerpt: $"excerpt {number}",
                    OptionDistractorWhy: null,
                    OptionDistractorCategory: null));
            }
            partAExtracts.Add(new ListeningExtractManifest(
                ExtractNumber: extractNumber,
                QuestionNumber: null,
                QuestionRange: null,
                PatientName: $"Patient {extractNumber}",
                ProfessionalRole: "GP",
                Context: null,
                Topic: null,
                Format: null,
                AudioFile: $"a{extractNumber}.mp3",
                ReadingTimeSeconds: 30,
                Transcript: "Part A transcript",
                AccentCode: "en-GB",
                SpeakerAttitude: null,
                TranscriptSegments: null,
                Speakers: null,
                Questions: questions));
        }

        var partBExtracts = new List<ListeningExtractManifest>();
        for (var i = 0; i < 6; i++, number++)
        {
            partBExtracts.Add(new ListeningExtractManifest(
                ExtractNumber: i + 1,
                QuestionNumber: number.ToString(),
                QuestionRange: null,
                PatientName: null,
                ProfessionalRole: null,
                Context: $"Workplace extract {i + 1}",
                Topic: null,
                Format: null,
                AudioFile: $"b{i + 1}.mp3",
                ReadingTimeSeconds: 0,
                Transcript: "Part B transcript",
                AccentCode: "en-AU",
                SpeakerAttitude: null,
                TranscriptSegments: null,
                Speakers: null,
                Questions: new[]
                {
                    new ListeningQuestionManifest(
                        Number: number,
                        Type: "multiple_choice_3",
                        NoteTextBeforeGap: null,
                        Stem: $"What is the main point of extract {i + 1}?",
                        Options: new ListeningOptionsManifest("Option A", "Option B", "Option C"),
                        CorrectAnswer: "B",
                        AcceptedAnswers: null,
                        Explanation: "Because B.",
                        DistractorExplanation: "A and C are wrong.",
                        SkillTag: "gist",
                        Timestamp: null,
                        TranscriptEvidenceStartMs: 1000,
                        TranscriptEvidenceEndMs: 4000,
                        TranscriptExcerpt: "B is correct here",
                        OptionDistractorWhy: new string?[] { "too strong", null, "out of scope" },
                        OptionDistractorCategory: new string?[] { "too_strong", null, "out_of_scope" }),
                }));
        }

        var partCExtracts = new List<ListeningExtractManifest>();
        for (var extractNumber = 1; extractNumber <= 2; extractNumber++)
        {
            var first = number;
            var questions = new List<ListeningQuestionManifest>();
            for (var q = 0; q < 6; q++, number++)
            {
                questions.Add(new ListeningQuestionManifest(
                    Number: number,
                    Type: "multiple_choice_3",
                    NoteTextBeforeGap: null,
                    Stem: $"Part C question {number}",
                    Options: new ListeningOptionsManifest("Choice A", "Choice B", "Choice C"),
                    CorrectAnswer: "C",
                    AcceptedAnswers: null,
                    Explanation: "Because C.",
                    DistractorExplanation: "A and B are wrong.",
                    SkillTag: "speaker-attitude",
                    Timestamp: null,
                    TranscriptEvidenceStartMs: 2000,
                    TranscriptEvidenceEndMs: 5000,
                    TranscriptExcerpt: "C is right here",
                    OptionDistractorWhy: null,
                    OptionDistractorCategory: new string?[] { "reused_keyword", "opposite_meaning", null }));
            }
            partCExtracts.Add(new ListeningExtractManifest(
                ExtractNumber: extractNumber,
                QuestionNumber: null,
                QuestionRange: $"{first}-{number - 1}",
                PatientName: null,
                ProfessionalRole: null,
                Context: null,
                Topic: $"Presentation {extractNumber}",
                Format: "presentation",
                AudioFile: $"c{extractNumber}.mp3",
                ReadingTimeSeconds: 90,
                Transcript: "Part C transcript",
                AccentCode: "en-IE",
                SpeakerAttitude: "critical",
                TranscriptSegments: null,
                Speakers: null,
                Questions: questions));
        }

        return new ListeningStructureManifest(
            TestTitle: "Listening Mock Test 01",
            ModeSupport: new[] { "paper", "computer" },
            StrictMock: true,
            PartA: new ListeningPartManifest(partAExtracts),
            PartB: new ListeningPartManifest(partBExtracts),
            PartC: new ListeningPartManifest(partCExtracts));
    }

    // ── Tests ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Import_FullManifest_Produces42QuestionsAndFiveExtracts()
    {
        var (db, svc) = Build();
        var paper = await SeedPaperAsync(db);

        var result = await svc.ImportManifestAsync(
            paper.Id, CanonicalManifest(), replaceExisting: false, adminId: "admin-1", default);

        // 42 questions split A24 / B6 / C12.
        Assert.Equal(42, result.Structure.Counts.TotalItems);
        Assert.Equal(24, result.Structure.Counts.PartACount);
        Assert.Equal(6, result.Structure.Counts.PartBCount);
        Assert.Equal(12, result.Structure.Counts.PartCCount);

        var questions = result.Structure.Questions;
        Assert.Equal(12, questions.Count(q => q.PartCode == "A1"));
        Assert.Equal(12, questions.Count(q => q.PartCode == "A2"));
        Assert.Equal(6, questions.Count(q => q.PartCode == "B"));
        Assert.Equal(6, questions.Count(q => q.PartCode == "C1"));
        Assert.Equal(6, questions.Count(q => q.PartCode == "C2"));

        // Part A gap folds the note lead-in + gap marker into the stem.
        var a1 = questions.Single(q => q.Number == 1);
        Assert.Equal("short_answer", a1.Type);
        Assert.Contains("____", a1.Stem);
        Assert.Equal("answer 1", a1.CorrectAnswer);
        Assert.Contains("ans 1", a1.AcceptedAnswers!);

        // Part B maps options A/B/C and the single-letter answer.
        var b = questions.Single(q => q.Number == 25);
        Assert.Equal("multiple_choice_3", b.Type);
        Assert.Equal(new[] { "Option A", "Option B", "Option C" }, b.Options);
        Assert.Equal("B", b.CorrectAnswer);

        // Part C carries the extract-level speaker attitude onto each question.
        var c = questions.Single(q => q.Number == 31);
        Assert.Equal("multiple_choice_3", c.Type);
        Assert.Equal("C", c.CorrectAnswer);
        Assert.Equal("critical", c.SpeakerAttitude);

        // Five authored extracts (A1, A2, B, C1, C2).
        var extracts = await svc.GetExtractsAsync(paper.Id, default);
        Assert.Equal(5, extracts.Count);
        Assert.Equal(
            new[] { "A1", "A2", "B", "C1", "C2" },
            extracts.Select(e => e.PartCode).ToArray());

        // Report is returned and reflects the canonical counts.
        Assert.NotNull(result.Report);
        Assert.Equal(42, result.Report.Counts.TotalItems);

        // Import is audited.
        Assert.Contains(db.AuditEvents, e => e.Action == "ListeningManifestImported" && e.ResourceId == paper.Id);
    }

    [Fact]
    public async Task Import_AdditiveOnAuthoredPaper_RejectsWhenReplaceFalse()
    {
        var (db, svc) = Build();
        var paper = await SeedPaperAsync(db);

        // First import seeds the structure.
        await svc.ImportManifestAsync(paper.Id, CanonicalManifest(), replaceExisting: false, "admin-1", default);

        // Second import without replace must be refused (whole-test document).
        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            svc.ImportManifestAsync(paper.Id, CanonicalManifest(), replaceExisting: false, "admin-1", default));

        Assert.Equal("listening_manifest_already_authored", ex.ErrorCode);
        Assert.Equal(StatusCodes.Status409Conflict, ex.StatusCode);
    }

    [Fact]
    public async Task Import_ReplaceExisting_OverwritesAuthoredPaper()
    {
        var (db, svc) = Build();
        var paper = await SeedPaperAsync(db);

        await svc.ImportManifestAsync(paper.Id, CanonicalManifest(), replaceExisting: false, "admin-1", default);

        // Re-import with replace=true succeeds and stays at 42.
        var result = await svc.ImportManifestAsync(paper.Id, CanonicalManifest(), replaceExisting: true, "admin-1", default);

        Assert.Equal(42, result.Structure.Counts.TotalItems);
        var questions = await svc.GetStructureAsync(paper.Id, default);
        Assert.Equal(42, questions.Questions.Count);
    }

    [Fact]
    public async Task Import_RejectsWhenLearnerAttemptsExist()
    {
        var (db, svc) = Build();
        var paper = await SeedPaperAsync(db);

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
        var originalJson = (await db.ContentPapers.AsNoTracking().SingleAsync(p => p.Id == paper.Id)).ExtractedTextJson;

        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            svc.ImportManifestAsync(paper.Id, CanonicalManifest(), replaceExisting: true, "admin-1", default));

        Assert.Equal("listening_manifest_attempts_exist", ex.ErrorCode);
        Assert.Equal(StatusCodes.Status409Conflict, ex.StatusCode);

        // Nothing was written.
        Assert.Equal(originalJson, (await db.ContentPapers.AsNoTracking().SingleAsync(p => p.Id == paper.Id)).ExtractedTextJson);
        Assert.Empty(await db.AuditEvents.Where(e => e.Action == "ListeningManifestImported").ToListAsync());
    }

    [Fact]
    public async Task Import_EmptyManifest_Rejects()
    {
        var (db, svc) = Build();
        var paper = await SeedPaperAsync(db);

        var empty = new ListeningStructureManifest(
            TestTitle: "Empty",
            ModeSupport: null,
            StrictMock: null,
            PartA: new ListeningPartManifest([]),
            PartB: null,
            PartC: null);

        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            svc.ImportManifestAsync(paper.Id, empty, replaceExisting: false, "admin-1", default));

        Assert.Equal("listening_manifest_empty", ex.ErrorCode);
        Assert.Equal(StatusCodes.Status400BadRequest, ex.StatusCode);
    }

    [Fact]
    public async Task Export_Then_Import_RoundTripsTo42QuestionsAndFiveExtracts()
    {
        var (sourceDb, sourceSvc) = Build();
        var sourcePaper = await SeedPaperAsync(sourceDb);
        await sourceSvc.ImportManifestAsync(sourcePaper.Id, CanonicalManifest(), replaceExisting: false, "admin-1", default);

        // Export the authored structure back to a §19 manifest.
        var exported = await sourceSvc.ExportManifestAsync(sourcePaper.Id, default);
        Assert.NotNull(exported.PartA);
        Assert.NotNull(exported.PartB);
        Assert.NotNull(exported.PartC);
        Assert.Equal(2, exported.PartA!.Extracts.Count);
        Assert.Equal(1, exported.PartB!.Extracts.Count); // 6 B questions collapse into one B extract
        Assert.Equal(2, exported.PartC!.Extracts.Count);

        // Serialize → deserialize to prove the manifest survives JSON transport
        // with the default camelCase contract the API binds with.
        var camel = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var json = JsonSerializer.Serialize(exported, camel);
        var reparsed = JsonSerializer.Deserialize<ListeningStructureManifest>(json, camel)!;

        // Import the exported manifest into a fresh paper.
        var (targetDb, targetSvc) = Build();
        var targetPaper = await SeedPaperAsync(targetDb);
        var result = await targetSvc.ImportManifestAsync(targetPaper.Id, reparsed, replaceExisting: false, "admin-2", default);

        Assert.Equal(42, result.Structure.Counts.TotalItems);
        Assert.Equal(24, result.Structure.Counts.PartACount);
        Assert.Equal(6, result.Structure.Counts.PartBCount);
        Assert.Equal(12, result.Structure.Counts.PartCCount);

        var extracts = await targetSvc.GetExtractsAsync(targetPaper.Id, default);
        Assert.Equal(5, extracts.Count);

        // Part B/C MCQ options + answers survive the round trip.
        var b = result.Structure.Questions.Single(q => q.Number == 25);
        Assert.Equal(new[] { "Option A", "Option B", "Option C" }, b.Options);
        Assert.Equal("B", b.CorrectAnswer);
        var c = result.Structure.Questions.Single(q => q.Number == 31);
        Assert.Equal("C", c.CorrectAnswer);
        Assert.Equal("critical", c.SpeakerAttitude);
    }

    // ── Stub backfill (no-op success) — mirrors ListeningAuthoringServiceTests. ──
    private sealed class RecordingBackfillService : IListeningBackfillService
    {
        public bool WasCalled { get; private set; }

        public Task<ListeningBackfillReport> BackfillPaperAsync(string paperId, string adminId, CancellationToken ct)
        {
            WasCalled = true;
            return Task.FromResult(new ListeningBackfillReport(paperId, true, 1, 1, 1, 0, null));
        }

        public Task<ListeningBackfillReport> BackfillPaperAsync(string paperId, string adminId, bool bypassAttemptsGuard, CancellationToken ct)
            => BackfillPaperAsync(paperId, adminId, ct);

        public Task<IReadOnlyList<ListeningBackfillReport>> BackfillAllAsync(string adminId, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<ListeningBackfillReport>>([]);
    }
}
