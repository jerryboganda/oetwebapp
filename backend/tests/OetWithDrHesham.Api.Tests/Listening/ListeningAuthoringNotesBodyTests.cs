using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetWithDrHesham.Api.Data;
using OetWithDrHesham.Api.Domain;
using OetWithDrHesham.Api.Services.Listening;

namespace OetWithDrHesham.Api.Tests.Listening;

/// <summary>
/// Part A note-completion body (<c>notesBody</c>) authoring coverage for
/// <see cref="ListeningAuthoringService"/>. Verifies the body round-trips
/// verbatim through replace/save for Part A (A1) and is forced null for Part
/// B/C; that an extract PATCH can set the body in isolation; that the body
/// survives a manifest import → export round trip; and that a legacy manifest
/// (no body, but per-question <c>noteTextBeforeGap</c> lead-ins) synthesises a
/// gap-marked body. Mirrors the in-memory DbContext + stubbed backfill harness
/// used by <see cref="ListeningManifestImportTests"/>.
/// </summary>
public class ListeningAuthoringNotesBodyTests
{
    private static (LearnerDbContext db, ListeningAuthoringService svc) Build()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var db = new LearnerDbContext(options);
        return (db, new ListeningAuthoringService(db, new NoOpBackfillService()));
    }

    private static async Task<ContentPaper> SeedPaperAsync(LearnerDbContext db)
    {
        var now = DateTimeOffset.UtcNow;
        var paper = new ContentPaper
        {
            Id = Guid.NewGuid().ToString("N"),
            SubtestCode = "listening",
            Title = "Listening NotesBody Test",
            Slug = $"listening-{Guid.NewGuid():N}",
            Status = ContentStatus.Draft,
            // Replace paths enforce SourceProvenance at mutation time.
            SourceProvenance = "owner=Acme; legal=original-authoring-attested",
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.ContentPapers.Add(paper);
        await db.SaveChangesAsync();
        return paper;
    }

    private const string A1Body =
        "Patient: Mr Jones\n## History\n- Presenting complaint: ____\n- Onset: ____\nContext line with no gap";

    private static ListeningAuthoredExtract A1Extract(string? notesBody) => new(
        PartCode: "A1",
        DisplayOrder: 0,
        Kind: "consultation",
        Title: "Consultation 1",
        AccentCode: "en-GB",
        Speakers: new List<ListeningAuthoredSpeaker>(),
        AudioStartMs: null,
        AudioEndMs: null,
        TimeLimitSeconds: null,
        NotesBodyMarkdown: notesBody);

    private static ListeningAuthoredExtract B1Extract(string? notesBody) => new(
        PartCode: "B1",
        DisplayOrder: 0,
        Kind: "workplace",
        Title: "Workplace extract 1",
        AccentCode: "en-AU",
        Speakers: new List<ListeningAuthoredSpeaker>(),
        AudioStartMs: null,
        AudioEndMs: null,
        TimeLimitSeconds: null,
        NotesBodyMarkdown: notesBody);

    // ── Tests ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task ReplaceExtracts_RoundTripsA1NotesBodyVerbatim()
    {
        var (db, svc) = Build();
        var paper = await SeedPaperAsync(db);

        await svc.ReplaceExtractsAsync(
            paper.Id,
            new[] { A1Extract(A1Body) },
            adminId: "admin-1",
            default);

        var reread = await svc.GetExtractsAsync(paper.Id, default);
        var a1 = reread.Single(e => e.PartCode == "A1");
        Assert.Equal(A1Body, a1.NotesBodyMarkdown);
    }

    [Fact]
    public async Task ReplaceExtracts_ForcesNotesBodyNullForPartB()
    {
        var (db, svc) = Build();
        var paper = await SeedPaperAsync(db);

        // A B1 extract that (incorrectly) carries a notesBody must be stored null.
        await svc.ReplaceExtractsAsync(
            paper.Id,
            new[] { B1Extract("- this should be dropped ____") },
            adminId: "admin-1",
            default);

        var reread = await svc.GetExtractsAsync(paper.Id, default);
        var b1 = reread.Single(e => e.PartCode == "B1");
        Assert.Null(b1.NotesBodyMarkdown);
    }

    [Fact]
    public async Task PatchExtract_SetsOnlyNotesBody_LeavesOtherFieldsIntact()
    {
        var (db, svc) = Build();
        var paper = await SeedPaperAsync(db);

        // Seed an A1 extract with a title/accent but no body.
        await svc.ReplaceExtractsAsync(
            paper.Id,
            new[] { A1Extract(null) },
            adminId: "admin-1",
            default);

        // PATCH only notesBody.
        await svc.PatchExtractAsync(
            paper.Id,
            extractCode: "A1",
            new ListeningExtractPatch(NotesBodyMarkdown: A1Body),
            adminId: "admin-1",
            default);

        var reread = await svc.GetExtractsAsync(paper.Id, default);
        var a1 = reread.Single(e => e.PartCode == "A1");
        Assert.Equal(A1Body, a1.NotesBodyMarkdown);
        // Untouched fields survive the patch.
        Assert.Equal("Consultation 1", a1.Title);
        Assert.Equal("en-GB", a1.AccentCode);
        Assert.Equal("consultation", a1.Kind);
    }

    [Fact]
    public async Task ManifestImportExport_RoundTripsPartANotesBody()
    {
        var (db, svc) = Build();
        var paper = await SeedPaperAsync(db);

        var manifest = ManifestWithPartA(notesBody: A1Body, withLegacyLeadIns: false);
        await svc.ImportManifestAsync(paper.Id, manifest, replaceExisting: false, "admin-1", default);

        // The imported A1 extract carries the body.
        var importedExtracts = await svc.GetExtractsAsync(paper.Id, default);
        Assert.Equal(A1Body, importedExtracts.Single(e => e.PartCode == "A1").NotesBodyMarkdown);

        // Export re-emits notesBody on the partA extract.
        var exported = await svc.ExportManifestAsync(paper.Id, default);
        Assert.NotNull(exported.PartA);
        var exportedA1 = exported.PartA!.Extracts.First();
        Assert.Equal(A1Body, exportedA1.NotesBody);

        // And it survives a JSON transport + re-import into a fresh paper.
        var camel = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var json = JsonSerializer.Serialize(exported, camel);
        var reparsed = JsonSerializer.Deserialize<ListeningStructureManifest>(json, camel)!;

        var (db2, svc2) = Build();
        var paper2 = await SeedPaperAsync(db2);
        await svc2.ImportManifestAsync(paper2.Id, reparsed, replaceExisting: false, "admin-2", default);
        var reimported = await svc2.GetExtractsAsync(paper2.Id, default);
        Assert.Equal(A1Body, reimported.Single(e => e.PartCode == "A1").NotesBodyMarkdown);
    }

    [Fact]
    public async Task LegacyManifest_SynthesizesA1BodyWith12GapMarkers()
    {
        var (db, svc) = Build();
        var paper = await SeedPaperAsync(db);

        // No extract-level notesBody, but 12 Part A questions each carrying a
        // legacy noteTextBeforeGap lead-in.
        var manifest = ManifestWithPartA(notesBody: null, withLegacyLeadIns: true);
        await svc.ImportManifestAsync(paper.Id, manifest, replaceExisting: false, "admin-1", default);

        var extracts = await svc.GetExtractsAsync(paper.Id, default);
        var a1Body = extracts.Single(e => e.PartCode == "A1").NotesBodyMarkdown;

        Assert.False(string.IsNullOrWhiteSpace(a1Body));
        // One gap marker synthesised per Part A question (12 of them).
        var gapCount = a1Body!.Split("____").Length - 1;
        Assert.Equal(12, gapCount);
        // Lead-ins appear in question-number order.
        var firstLeadIndex = a1Body.IndexOf("Lead 1", StringComparison.Ordinal);
        var lastLeadIndex = a1Body.IndexOf("Lead 12", StringComparison.Ordinal);
        Assert.True(firstLeadIndex >= 0 && lastLeadIndex > firstLeadIndex);
    }

    // ── Manifest builder: a single Part A extract (A1) with 12 gap questions ──

    private static ListeningStructureManifest ManifestWithPartA(string? notesBody, bool withLegacyLeadIns)
    {
        var questions = new List<ListeningQuestionManifest>();
        for (var i = 1; i <= 12; i++)
        {
            questions.Add(new ListeningQuestionManifest(
                Number: i,
                Type: "gap_fill",
                NoteTextBeforeGap: withLegacyLeadIns ? $"Lead {i}" : null,
                Stem: null,
                Options: null,
                CorrectAnswer: $"answer {i}",
                AcceptedAnswers: null,
                Explanation: null,
                DistractorExplanation: null,
                SkillTag: "specific-detail",
                Timestamp: null,
                TranscriptEvidenceStartMs: null,
                TranscriptEvidenceEndMs: null,
                TranscriptExcerpt: null,
                OptionDistractorWhy: null,
                OptionDistractorCategory: null));
        }

        var partAExtract = new ListeningExtractManifest(
            ExtractNumber: 1,
            QuestionNumber: null,
            QuestionRange: null,
            PatientName: "Patient 1",
            ProfessionalRole: "GP",
            Context: null,
            Topic: null,
            Format: null,
            AudioFile: "a1.mp3",
            ReadingTimeSeconds: 30,
            Transcript: "Part A transcript",
            AccentCode: "en-GB",
            SpeakerAttitude: null,
            TranscriptSegments: null,
            Speakers: null,
            Questions: questions,
            NotesBody: notesBody);

        return new ListeningStructureManifest(
            TestTitle: "Legacy/NotesBody Part A",
            ModeSupport: new[] { "paper", "computer" },
            StrictMock: true,
            PartA: new ListeningPartManifest(new[] { partAExtract }),
            PartB: null,
            PartC: null);
    }

    // ── Stub backfill (no relational rows seeded → resync is a no-op). ──
    private sealed class NoOpBackfillService : IListeningBackfillService
    {
        public Task<ListeningBackfillReport> BackfillPaperAsync(string paperId, string adminId, CancellationToken ct)
            => Task.FromResult(new ListeningBackfillReport(paperId, true, 1, 1, 1, 0, null));

        public Task<ListeningBackfillReport> BackfillPaperAsync(string paperId, string adminId, bool bypassAttemptsGuard, CancellationToken ct)
            => BackfillPaperAsync(paperId, adminId, ct);

        public Task<IReadOnlyList<ListeningBackfillReport>> BackfillAllAsync(string adminId, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<ListeningBackfillReport>>([]);
    }
}
