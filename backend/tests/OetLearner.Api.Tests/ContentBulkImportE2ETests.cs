using System.IO.Compression;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Content;

namespace OetLearner.Api.Tests;

/// <summary>
/// End-to-end integration test for the Content Upload bulk-import pipeline.
/// Builds a real ZIP in-memory matching the Project Real Content/ folder
/// structure, feeds it through the full pipeline (parser → staging → commit
/// → MediaAsset + ContentPaper + ContentPaperAsset creation with SHA dedup),
/// and verifies the resulting DB state matches the expected shape.
///
/// This is the "prove the whole pipeline works on real data" test.
/// </summary>
public class ContentBulkImportE2ETests
{
    private static (LearnerDbContext db, InMemoryFileStorage storage, ContentBulkImportService svc,
                    ContentPaperService paperSvc, ContentConventionParser parser)
        Build()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var db = new LearnerDbContext(options);
        var storage = new InMemoryFileStorage();
        var parser = new ContentConventionParser();
        var paperSvc = new ContentPaperService(db);
        var opts = Options.Create(new StorageOptions { LocalRootPath = "/tmp", ContentUpload = new() });
        var svc = new ContentBulkImportService(
            db, storage, parser, paperSvc, opts,
            NullLogger<ContentBulkImportService>.Instance);
        return (db, storage, svc, paperSvc, parser);
    }

    /// <summary>Build an in-memory ZIP mirroring the Project Real Content
    /// folder structure. Uses tiny distinguishable payloads so we can verify
    /// SHA-256 dedup works without handling 50 MB of audio.</summary>
    private static Stream BuildRealShapedZip()
    {
        var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Add(string path, string content)
            {
                var e = zip.CreateEntry(path.Replace('\\', '/'), CompressionLevel.Fastest);
                using var s = e.Open();
                var bytes = Encoding.UTF8.GetBytes(content);
                s.Write(bytes, 0, bytes.Length);
            }

            // Listening Sample 1 — all-professions
            Add("Listening ( IMPORTANT NOTE =  Same for All Professions )/Listening Sample 1/Audio 1/Audio 1.mp3", "LISTENING-AUDIO-1-PAYLOAD");
            Add("Listening ( IMPORTANT NOTE =  Same for All Professions )/Listening Sample 1/Listening Sample 1 Question-Paper.pdf", "L1-Q");
            Add("Listening ( IMPORTANT NOTE =  Same for All Professions )/Listening Sample 1/Listening Sample 1 Audio-Script.pdf", "L1-S");
            Add("Listening ( IMPORTANT NOTE =  Same for All Professions )/Listening Sample 1/Listening Sample 1 Answer-Key.pdf", "L1-A");

            // Reading Sample 1 — parts A and B+C
            Add("Reading ( IMPORTANT NOTE = Same for All Professions )/Reading Sample 1/Part A Reading ( Diarrhea ).pdf", "R1-A");
            Add("Reading ( IMPORTANT NOTE = Same for All Professions )/Reading Sample 1/Reading Part B&C.pdf", "R1-BC");

            // Writing — routine referral + urgent referral (medicine)
            Add("Writing_/Writing 1 ( Routine Referral )/Ms Sarah Miller - Case Notes.pdf", "W1-CN");
            Add("Writing_/Writing 1 ( Routine Referral )/Ms Sarah Miller - Answer Sheet.pdf", "W1-AS");
            Add("Writing_/Writing 3 ( Urgent Referral )/Leo Bennett - Urgent Referral.pdf", "W3-CN");

            // Speaking — examination card (medicine)
            Add("Speaking_/Card 4 ( Examination Card )_ MOST IMPORTANT TYPE/4.pdf", "S4");

            // Duplicate content: an identical Listening audio file elsewhere,
            // SHOULD dedup to the same MediaAsset.
            Add("Listening ( IMPORTANT NOTE =  Same for All Professions )/Listening Sample 2/Audio 2/Audio 2.mp3", "LISTENING-AUDIO-1-PAYLOAD");
            Add("Listening ( IMPORTANT NOTE =  Same for All Professions )/Listening Sample 2/Listening Sample 2 Question-Paper.pdf", "L2-Q");
            Add("Listening ( IMPORTANT NOTE =  Same for All Professions )/Listening Sample 2/Listening Sample 2 Audio-Script.pdf", "L2-S");
            Add("Listening ( IMPORTANT NOTE =  Same for All Professions )/Listening Sample 2/Listening Sample 2 Answer-Key.pdf", "L2-A");
        }
        ms.Position = 0;
        return ms;
    }

    [Fact]
    public async Task Full_pipeline_creates_papers_assets_and_dedupes_identical_content()
    {
        var (db, storage, svc, _, _) = Build();

        // ── 1. Stage: unzip + parse into a manifest ────────────────────────
        await using var zip = BuildRealShapedZip();
        var session = await svc.StagePayloadAsync("admin-1", zip, "real-content.zip", default);

        // Manifest shape
        Assert.NotEmpty(session.Manifest.Papers);
        // 2 Listening, 1 Reading, 2 Writing, 1 Speaking = 6 proposed papers
        Assert.Equal(6, session.Manifest.Papers.Count);

        // Listening Sample 1 must have all 4 roles detected
        var l1 = session.Manifest.Papers.First(p => p.Title.Contains("Listening Sample 1"));
        Assert.Equal("listening", l1.SubtestCode);
        Assert.True(l1.AppliesToAllProfessions);
        Assert.Contains(l1.Assets, a => a.Role == PaperAssetRole.Audio);
        Assert.Contains(l1.Assets, a => a.Role == PaperAssetRole.QuestionPaper);
        Assert.Contains(l1.Assets, a => a.Role == PaperAssetRole.AudioScript);
        Assert.Contains(l1.Assets, a => a.Role == PaperAssetRole.AnswerKey);

        // Writing 1 must have letter type + profession
        var w1 = session.Manifest.Papers.First(p => p.Title.Contains("Writing 1"));
        Assert.Equal("writing", w1.SubtestCode);
        Assert.Equal("routine_referral", w1.LetterType);
        Assert.Equal("medicine", w1.ProfessionId);
        Assert.Contains(w1.Assets, a => a.Role == PaperAssetRole.CaseNotes);
        Assert.Contains(w1.Assets, a => a.Role == PaperAssetRole.ModelAnswer);

        // Speaking Card 4 must have card type + "MOST IMPORTANT" still detected as examination
        var s4 = session.Manifest.Papers.First(p => p.Title.Contains("Card 4"));
        Assert.Equal("speaking", s4.SubtestCode);
        Assert.Equal("examination", s4.CardType);
        Assert.Equal("medicine", s4.ProfessionId);

        // ── 2. Commit: approve everything ──────────────────────────────────
        var approvals = session.Manifest.Papers
            .Select(p => new BulkImportApproval(
                p.ProposalId, Approve: true,
                OverrideTitle: null,
                OverrideProfessionId: null,
                OverrideAppliesToAllProfessions: null,
                OverrideCardType: null,
                OverrideLetterType: null,
                OverrideSourceProvenance: "Authored by Dr Hesham"))
            .ToList();

        var result = await svc.CommitAsync("admin-1", session.SessionId, approvals, default);

        Assert.Equal(6, result.CreatedPaperCount);
        // 4 (L1) + 2 (R1) + 2 (W1) + 1 (W3) + 1 (S4) + 4 (L2) = 14 assets
        Assert.Equal(14, result.CreatedAssetCount);
        // The duplicate audio file should have deduplicated to the same MediaAsset
        Assert.True(result.DeduplicatedAssetCount >= 1,
            $"Expected at least 1 deduplicated asset, got {result.DeduplicatedAssetCount}");

        // ── 3. Verify DB state ─────────────────────────────────────────────
        var papers = await db.ContentPapers.Include(p => p.Assets).ToListAsync();
        Assert.Equal(6, papers.Count);

        // Every paper must be Draft with provenance set
        Assert.All(papers, p => Assert.Equal(ContentStatus.Draft, p.Status));
        Assert.All(papers, p => Assert.Equal("Authored by Dr Hesham", p.SourceProvenance));

        // Distinct MediaAsset count < total attachment count (proves dedup ran)
        var totalAttachments = await db.ContentPaperAssets.CountAsync();
        var distinctMedia = await db.MediaAssets.CountAsync();
        Assert.True(distinctMedia < totalAttachments,
            $"Dedup failed: {distinctMedia} media assets for {totalAttachments} attachments.");

        // Both Listening papers point at the SAME audio MediaAsset
        var l1Db = papers.First(p => p.Title.Contains("Listening Sample 1"));
        var l2Db = papers.First(p => p.Title.Contains("Listening Sample 2"));
        var l1Audio = l1Db.Assets.First(a => a.Role == PaperAssetRole.Audio).MediaAssetId;
        var l2Audio = l2Db.Assets.First(a => a.Role == PaperAssetRole.Audio).MediaAssetId;
        Assert.Equal(l1Audio, l2Audio);

        // Every audio MediaAsset has a SHA-256 populated
        var audioRows = await db.MediaAssets.Where(m => m.MediaKind == "audio").ToListAsync();
        Assert.NotEmpty(audioRows);
        Assert.All(audioRows, m => Assert.Equal(64, m.Sha256?.Length ?? 0));

        // Staging is cleaned
        Assert.False(storage.Exists($"uploads/staging/bulk/admin-1/{session.SessionId}/__source.zip"));

        // Every paper asset marked IsPrimary=true (attachment default)
        var paperAssets = await db.ContentPaperAssets.ToListAsync();
        Assert.All(paperAssets, a => Assert.True(a.IsPrimary));

        // Audit trail: one ContentPaperCreated + one ContentPaperAssetAttached per event
        var audit = await db.AuditEvents.Where(e => e.ResourceType == "ContentPaper").ToListAsync();
        Assert.Contains(audit, e => e.Action == "ContentPaperCreated");
        Assert.Contains(audit, e => e.Action == "ContentPaperAssetAttached");

        await db.DisposeAsync();
    }

    [Fact]
    public async Task Commit_only_creates_approved_papers()
    {
        var (db, _, svc, _, _) = Build();
        await using var zip = BuildRealShapedZip();
        var session = await svc.StagePayloadAsync("admin-1", zip, "real-content.zip", default);

        // Approve only Listening Sample 1
        var approvals = session.Manifest.Papers.Select(p => new BulkImportApproval(
            p.ProposalId,
            Approve: p.Title.Contains("Listening Sample 1"),
            null, null, null, null, null, "A"))
            .ToList();

        var result = await svc.CommitAsync("admin-1", session.SessionId, approvals, default);
        Assert.Equal(1, result.CreatedPaperCount);

        var papers = await db.ContentPapers.ToListAsync();
        Assert.Single(papers);
        Assert.Contains("Listening Sample 1", papers[0].Title);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Published_paper_from_import_is_visible_to_matching_profession()
    {
        var (db, _, svc, paperSvc, _) = Build();
        await using var zip = BuildRealShapedZip();
        var session = await svc.StagePayloadAsync("admin-1", zip, "real-content.zip", default);

        var approvals = session.Manifest.Papers.Select(p => new BulkImportApproval(
            p.ProposalId, Approve: true, null, null, null, null, null, "Authored by Dr Hesham"))
            .ToList();
        await svc.CommitAsync("admin-1", session.SessionId, approvals, default);

        // Publish Writing 1 (needs CaseNotes primary — already attached)
        var w1 = await db.ContentPapers.FirstAsync(p => p.Title.Contains("Writing 1"));
        await paperSvc.PublishAsync(w1.Id, "admin-1", default);

        var published = await db.ContentPapers.FirstAsync(p => p.Id == w1.Id);
        Assert.Equal(ContentStatus.Published, published.Status);
        Assert.NotNull(published.PublishedAt);

        await db.DisposeAsync();
    }
}
