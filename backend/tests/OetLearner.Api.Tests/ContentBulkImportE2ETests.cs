using System.IO.Compression;
using System.Text;
using System.Text.Json;
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
    private const string DefaultSourceProvenance = ContentDefaults.DefaultSourceProvenance;

    private static (LearnerDbContext db, InMemoryFileStorage storage, ContentBulkImportService svc,
                    ContentPaperService paperSvc, ContentConventionParser parser)
        Build(ContentUploadOptions? contentUploadOptions = null, IUploadScanner? scanner = null)
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var db = new LearnerDbContext(options);
        var storage = new InMemoryFileStorage();
        var parser = new ContentConventionParser();
        var paperSvc = new ContentPaperService(db);
        var opts = Options.Create(new StorageOptions { LocalRootPath = "/tmp", ContentUpload = contentUploadOptions ?? new() });
        var svc = new ContentBulkImportService(
            db, storage, parser, paperSvc, opts,
            scanner,
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
                var bytes = BuildSignedPayload(path, content);
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
            Add("Speaking_/Speaking Assessment Criteria  ( IMPORTANT NOTE = same for all professions ).pdf", "S-CRITERIA");
            Add("Speaking_/Speaking Intro Questions - Warm Up Questions - ( IMPORTANT NOTE = same for all professions ).pdf", "S-WARMUP");
            Add("Speaking_/Card 4 ( Examination Card )_ MOST IMPORTANT TYPE/4.pdf", "S4");

            // Canonical references — scoring + rulebook PDFs are staged as
            // reviewable drafts; they do not mutate active scoring/rulebook policy.
            Add("Scoring System.txt", "# Scoring\nListening/Reading 30 of 42 equals 350.");
            Add("Writing_/Writing RuleBook ( Medicine only )/OET_Writing_Rulebook_FINAL ( For Medicine Only ).pdf", "W-RULEBOOK");
            Add("Speaking_/Speaking Rulebook ( Medicine Only )/OET_Speaking_Rulebook ( For Medicine only ).pdf", "S-RULEBOOK");

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

    private static byte[] BuildSignedPayload(string path, string content)
    {
        var body = Encoding.UTF8.GetBytes(content);
        var prefix = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".pdf" => Encoding.ASCII.GetBytes("%PDF-1.4\n"),
            ".mp3" => Encoding.ASCII.GetBytes("ID3"),
            ".txt" => Array.Empty<byte>(),
            _ => Array.Empty<byte>(),
        };
        return prefix.Concat(body).ToArray();
    }

    private static void SeedPublishedRulebook(LearnerDbContext db, string kind, string profession)
    {
        var id = $"rb-{kind}-{profession}-published";
        db.RulebookVersions.Add(new RulebookVersion
        {
            Id = id,
            Kind = kind,
            Profession = profession,
            Version = "1.0.0",
            Status = RulebookStatus.Published,
            AuthoritySource = "Test canonical rulebook",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            PublishedAt = DateTimeOffset.UtcNow,
            Sections =
            {
                new RulebookSectionRow
                {
                    Id = $"section-{id}",
                    RulebookVersionId = id,
                    Code = "01",
                    Title = "Core rules",
                    OrderIndex = 1,
                }
            },
            Rules =
            {
                new RulebookRuleRow
                {
                    Id = $"rule-{id}",
                    RulebookVersionId = id,
                    Code = "R01",
                    SectionCode = "01",
                    Title = "Core rule",
                    Body = "Use canonical OET format.",
                    Severity = "major",
                    AppliesToJson = "\"all\"",
                    OrderIndex = 1,
                }
            },
        });
    }

    private static Stream BuildTinyZip(params (string Path, string Content)[] entries)
    {
        var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, content) in entries)
            {
                var entry = zip.CreateEntry(path.Replace('\\', '/'), CompressionLevel.Optimal);
                using var stream = entry.Open();
                var bytes = Encoding.UTF8.GetBytes(content);
                stream.Write(bytes, 0, bytes.Length);
            }
        }
        ms.Position = 0;
        return ms;
    }

    private sealed class RejectingUploadScanner(Func<string, bool> reject)
        : IUploadScanner
    {
        public List<string> ScannedFilenames { get; } = [];

        public Task<(bool clean, string? reason)> ScanAsync(Stream stream, string filename, CancellationToken ct)
        {
            ScannedFilenames.Add(filename);
            return Task.FromResult(reject(filename)
                ? (clean: false, reason: (string?)"test scanner rejected file")
                : (clean: true, reason: (string?)null));
        }
    }

    [Fact]
    public async Task Full_pipeline_creates_papers_assets_and_dedupes_identical_content()
    {
        var (db, storage, svc, _, _) = Build();
        SeedPublishedRulebook(db, "writing", "medicine");
        SeedPublishedRulebook(db, "speaking", "medicine");
        await db.SaveChangesAsync();

        // ── 1. Stage: unzip + parse into a manifest ────────────────────────
        await using var zip = BuildRealShapedZip();
        var session = await svc.StagePayloadAsync("admin-1", zip, "real-content.zip", default);

        // Manifest shape
        Assert.NotEmpty(session.Manifest.Papers);
        // 2 Listening, 1 Reading, 2 Writing, 1 Speaking = 6 proposed papers
        Assert.Equal(6, session.Manifest.Papers.Count);
        Assert.Equal(5, session.Manifest.References.Count);
        Assert.Contains(session.Manifest.References, r => r.Target == ImportReferenceTargets.SpeakingSharedResource && r.SharedResourceKind == SpeakingSharedResourceKinds.AssessmentCriteria);
        Assert.Contains(session.Manifest.References, r => r.Target == ImportReferenceTargets.SpeakingSharedResource && r.SharedResourceKind == SpeakingSharedResourceKinds.WarmUpQuestions);
        Assert.Contains(session.Manifest.References, r => r.Target == ImportReferenceTargets.RulebookReferencePdf && r.Kind == "writing");
        Assert.Contains(session.Manifest.References, r => r.Target == ImportReferenceTargets.RulebookReferencePdf && r.Kind == "speaking");
        Assert.Contains(session.Manifest.References, r => r.Target == ImportReferenceTargets.ScoringPolicyBody);
        Assert.Equal(19, session.Manifest.Inventory.TotalFiles);

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
                OverrideSourceProvenance: DefaultSourceProvenance))
            .Concat(session.Manifest.References.Select(r => new BulkImportApproval(
                r.ProposalId, Approve: true,
                OverrideTitle: null,
                OverrideProfessionId: null,
                OverrideAppliesToAllProfessions: null,
                OverrideCardType: null,
                OverrideLetterType: null,
                OverrideSourceProvenance: DefaultSourceProvenance)))
            .ToList();

        var result = await svc.CommitAsync("admin-1", session.SessionId, approvals, default);

        Assert.Equal(6, result.CreatedPaperCount);
        Assert.Equal(5, result.CreatedReferenceCount);
        // 4 (L1) + 2 (R1) + 2 (W1) + 1 (W3) + 4 (S4 role-card + shared links) + 4 (L2) = 17 paper assets
        Assert.Equal(17, result.CreatedAssetCount);
        // The duplicate audio file should have deduplicated to the same MediaAsset
        Assert.True(result.DeduplicatedAssetCount >= 1,
            $"Expected at least 1 deduplicated asset, got {result.DeduplicatedAssetCount}");

        // ── 3. Verify DB state ─────────────────────────────────────────────
        var papers = await db.ContentPapers.Include(p => p.Assets).ToListAsync();
        Assert.Equal(6, papers.Count);

        // Every paper must be Draft with provenance set
        Assert.All(papers, p => Assert.Equal(ContentStatus.Draft, p.Status));
        Assert.All(papers, p => Assert.Equal(DefaultSourceProvenance, p.SourceProvenance));

        // Distinct MediaAsset count < total attachment count (proves dedup ran)
        var totalAttachments = await db.ContentPaperAssets.CountAsync();
        var distinctMedia = await db.ContentPaperAssets.Select(a => a.MediaAssetId).Distinct().CountAsync();
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
        Assert.False(await storage.ExistsAsync(
            $"uploads/staging/bulk/admin-1/{session.SessionId}/__source.zip",
            CancellationToken.None));

        var s4Db = papers.First(p => p.Title.Contains("Card 4"));
        Assert.Contains(s4Db.Assets, a => a.Role == PaperAssetRole.RoleCard && a.IsPrimary);
        Assert.Contains(s4Db.Assets, a => a.Role == PaperAssetRole.AssessmentCriteria && a.IsPrimary);
        Assert.Contains(s4Db.Assets, a => a.Role == PaperAssetRole.WarmUpQuestions && a.IsPrimary);

        Assert.Equal(2, await db.SpeakingSharedResources.CountAsync());
        Assert.Contains(await db.SpeakingSharedResources.ToListAsync(), row => row.Kind == SpeakingSharedResourceKinds.AssessmentCriteria);
        Assert.Contains(await db.SpeakingSharedResources.ToListAsync(), row => row.Kind == SpeakingSharedResourceKinds.WarmUpQuestions);
        Assert.Equal(1, await db.ScoringPolicies.CountAsync());
        Assert.False(await db.ScoringPolicies.Select(p => p.IsActive).SingleAsync());
        Assert.Equal(4, await db.RulebookVersions.CountAsync());
        Assert.Equal(2, await db.RulebookVersions.CountAsync(r => r.Status == RulebookStatus.Draft && r.ReferencePdfAssetId != null));

        // Every paper asset slot marked primary once per role/part.
        var paperAssets = await db.ContentPaperAssets.ToListAsync();
        Assert.All(
            paperAssets.GroupBy(a => new { a.PaperId, a.Role, a.Part }),
            group => Assert.Equal(1, group.Count(a => a.IsPrimary)));

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
    public async Task Stage_rejects_zip_entry_with_mismatched_file_signature()
    {
        var (_, _, svc, _, _) = Build();
        await using var zip = BuildTinyZip(
            ("Listening/Listening Sample 1/Listening Sample 1 Question-Paper.pdf", "not a pdf"));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.StagePayloadAsync("admin-1", zip, "bad.zip", default));

        Assert.Contains("failed file signature validation", ex.Message);
    }

    [Fact]
    public async Task Stage_accepts_webp_result_template_reference()
    {
        var (db, _, svc, _, _) = Build();
        await using var zip = BuildTinyZip(
            ("Result Templates/OET Result Table.webp", "RIFF0000WEBPimage-body"));

        var session = await svc.StagePayloadAsync("admin-1", zip, "result-template.zip", default);

        var reference = Assert.Single(session.Manifest.References);
        Assert.Equal(ImportReferenceTargets.ResultTemplate, reference.Target);
        Assert.Equal("webp", session.Manifest.Inventory.FilesByExtension.Keys.Single());
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Stage_rejects_zip_entry_that_fails_security_scan()
    {
        var scanner = new RejectingUploadScanner(filename =>
            filename.EndsWith("Question-Paper.pdf", StringComparison.OrdinalIgnoreCase));
        var (db, storage, svc, _, _) = Build(scanner: scanner);
        await using var zip = BuildTinyZip(
            ("Listening/Listening Sample 1/Listening Sample 1 Question-Paper.pdf", "%PDF-1.4\nbody"));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.StagePayloadAsync("admin-1", zip, "scan-fails.zip", default));

        Assert.Contains("failed security scanning", ex.Message);
        Assert.Contains("Listening/Listening Sample 1/Listening Sample 1 Question-Paper.pdf", scanner.ScannedFilenames);
        Assert.False(storage.AnyKeyStartsWith("uploads/staging/bulk/admin-1/"));
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Stage_rejects_zip_traversal_entries_and_cleans_staging()
    {
        var (db, storage, svc, _, _) = Build();
        await using var zip = BuildTinyZip(("../evil.pdf", "bad"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.StagePayloadAsync("admin-1", zip, "bad.zip", default));

        Assert.False(storage.AnyKeyStartsWith("uploads/staging/bulk/admin-1/"));
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Stage_rejects_zip_with_too_many_entries()
    {
        var (db, _, svc, _, _) = Build(new ContentUploadOptions { MaxZipEntries = 1 });
        await using var zip = BuildTinyZip(("one.pdf", "one"), ("two.pdf", "two"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.StagePayloadAsync("admin-1", zip, "too-many.zip", default));

        await db.DisposeAsync();
    }

    [Fact]
    public async Task Stage_rejects_zip_entry_over_uncompressed_limit()
    {
        var (db, _, svc, _, _) = Build(new ContentUploadOptions { MaxZipEntryBytes = 3 });
        await using var zip = BuildTinyZip(("oversize.pdf", "hello"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.StagePayloadAsync("admin-1", zip, "oversize.zip", default));

        await db.DisposeAsync();
    }

    [Fact]
    public async Task Stage_rejects_zip_entry_over_media_type_limit()
    {
        var (db, _, svc, _, _) = Build(new ContentUploadOptions
        {
            MaxZipEntryBytes = 10_000,
            MaxPdfBytes = 10,
        });
        await using var zip = BuildTinyZip(("Listening/Listening Sample 1/Listening Sample 1 Question-Paper.pdf", "%PDF-1.4\nthis pdf is too large"));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.StagePayloadAsync("admin-1", zip, "oversize-type.zip", default));

        Assert.Contains("pdf upload limit", ex.Message);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Stage_rejects_zip_entries_over_compression_ratio_limit()
    {
        var (db, _, svc, _, _) = Build(new ContentUploadOptions
        {
            MaxZipCompressionRatio = 1.05,
            MaxZipEntryBytes = 200_000,
            MaxZipUncompressedBytes = 200_000
        });
        await using var zip = BuildTinyZip(("compressed.pdf", new string('A', 100_000)));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.StagePayloadAsync("admin-1", zip, "compressed.zip", default));

        await db.DisposeAsync();
    }

    [Fact]
    public async Task Published_paper_from_import_is_visible_to_matching_profession()
    {
        var (db, _, svc, paperSvc, _) = Build();
        await using var zip = BuildRealShapedZip();
        var session = await svc.StagePayloadAsync("admin-1", zip, "real-content.zip", default);

        var approvals = session.Manifest.Papers.Select(p => new BulkImportApproval(
            p.ProposalId, Approve: true, null, null, null, null, null, DefaultSourceProvenance))
            .ToList();
        await svc.CommitAsync("admin-1", session.SessionId, approvals, default);

        // Publish Writing 1 after authoring the learner-facing structure that
        // the production writing publish gate requires.
        var w1 = await db.ContentPapers.FirstAsync(p => p.Title.Contains("Writing 1"));
        w1.ExtractedTextJson = WritingContentStructure.ReplaceStructure(w1.ExtractedTextJson, JsonDocument.Parse("""
        {
            "letterType": "routine_referral",
            "taskPrompt": "Write a routine referral letter to the receiving GP using the case notes provided.",
            "caseNotes": "Patient: Ms Sarah Miller. Purpose: routine referral. Include relevant clinical background, current status, and follow-up request.",
            "modelAnswer": "Dear Doctor,\n\nI am writing to refer Ms Sarah Miller for ongoing review following her recent presentation. Please assess her current symptoms, medication response, and any follow-up needs.\n\nYours sincerely,\nNurse"
        }
        """).RootElement);
        await db.SaveChangesAsync();
        await paperSvc.PublishAsync(w1.Id, "admin-1", default);

        var published = await db.ContentPapers.FirstAsync(p => p.Id == w1.Id);
        Assert.Equal(ContentStatus.Published, published.Status);
        Assert.NotNull(published.PublishedAt);

        await db.DisposeAsync();
    }
}
