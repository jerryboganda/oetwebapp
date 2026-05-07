using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Content;

namespace OetLearner.Api.Services.Seeding;

// ═════════════════════════════════════════════════════════════════════════════
// MockSampleSeeder
//
// On Development startup, ingests three fully-assembled draft MockBundle rows
// from local sample files in `Project Real Content/` so the admin (and
// learner) can immediately preview the new mock wizard + flow.
//
// Each bundle = Listening Sample N + Reading Sample N + Writing N + Speaking
// Card X, attached as MockBundleSection rows in L→R→W→S order
// (40 / 60 / 45 / 20 minutes). Bundles stay in Draft so the admin must still
// open them in the wizard and author the structured items (Listening 24/6/12,
// Reading 20/6/16) before publishing.
//
// Idempotency: keyed on MockBundle.Slug (sample-mock-{n}). Files dedup by
// SHA-256. Per-bundle paper slugs are sample-mock-{n}-{subtest}; if a
// pre-existing slug collides, a numeric suffix is appended.
//
// Strict invariants honoured:
//   • All file *writes* go through IFileStorage.WriteAsync.
//   • Reads from `Project Real Content/` use System.IO directly (these are
//     repo files supplied by the project owner, mirroring how
//     ListeningSampleSeeder loads its sample folders).
//   • Non-fatal: any IO/parse failure logs a warning, never throws to
//     startup.
// ═════════════════════════════════════════════════════════════════════════════

public sealed class MockSampleSeeder(
    LearnerDbContext db,
    IFileStorage storage,
    IHostEnvironment env,
    IOptions<MockSampleSeederOptions> opts,
    ILogger<MockSampleSeeder> logger)
{
    private const string SeederActorId = "system:mock-sample-seed";

    private const string Provenance =
        "Source: Internal sample mock dataset (Project Real Content/), seeded for development preview only.";

    private const string ListeningFolder = "Listening ( IMPORTANT NOTE =  Same for All Professions )";
    private const string ReadingFolder   = "Reading ( IMPORTANT NOTE = Same for All Professions )";
    private const string WritingFolder   = "Writing_";
    private const string SpeakingFolder  = "Speaking_";

    private static readonly string[] WritingDirs =
    [
        "Writing 1 ( Routine Referral )",
        "Writing 2 ( Referral to non medical profession as occupational therapist - manager of human supportive scheme - psychologist - radiographer )",
        "Writing 3 ( Urgent Referral )",
    ];

    private static readonly (string Folder, string CardType)[] SpeakingCards =
    [
        ("Card 1 ( Already known Pt )",                       "already_known_pt"),
        ("Card 4 ( Examination Card )_ MOST IMPORTANT TYPE",  "examination"),
        ("Card 5 ( First visit - Emergency Card )",           "first_visit_emergency"),
    ];

    /// <summary>L → R → W → S, with admin-spec time limits.</summary>
    private static readonly (string Subtest, int TimeLimit)[] SectionPlan =
    [
        ("listening", 40),
        ("reading",   60),
        ("writing",   45),
        ("speaking",  20),
    ];

    public async Task SeedAsync(CancellationToken ct)
    {
        if (!opts.Value.Enabled)
        {
            logger.LogDebug("MockSampleSeeder disabled (MockSampleSeeder:Enabled=false); skipping.");
            return;
        }

        var sourceRoot = ResolveSourceRoot(opts.Value.SourceRootPath);
        if (sourceRoot is null)
        {
            logger.LogInformation(
                "MockSampleSeeder: source root not found (configured='{Configured}'); skipping.",
                opts.Value.SourceRootPath);
            return;
        }

        logger.LogInformation("MockSampleSeeder: ingesting from {Root}", sourceRoot);

        for (var n = 1; n <= 3; n++)
        {
            try
            {
                await SeedBundleAsync(sourceRoot, n, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "MockSampleSeeder: bundle {Index} failed; continuing with remaining bundles.", n);
            }
        }
    }

    private async Task SeedBundleAsync(string sourceRoot, int index, CancellationToken ct)
    {
        var bundleSlug = $"sample-mock-{index}";
        var existing = await db.MockBundles.AsNoTracking()
            .AnyAsync(b => b.Slug == bundleSlug, ct);
        if (existing)
        {
            logger.LogInformation(
                "MockSampleSeeder: Sample Mock {Index} already seeded — skipping ('{Slug}').",
                index, bundleSlug);
            return;
        }

        var listeningDir = Path.Combine(sourceRoot, ListeningFolder, $"Listening Sample {index}");
        var readingDir   = Path.Combine(sourceRoot, ReadingFolder,   $"Reading Sample {index}");
        var writingDir   = Path.Combine(sourceRoot, WritingFolder,   WritingDirs[index - 1]);
        var (speakingFolderName, speakingCardType) = SpeakingCards[index - 1];
        var speakingDir  = Path.Combine(sourceRoot, SpeakingFolder,  speakingFolderName);

        if (!Directory.Exists(listeningDir) || !Directory.Exists(readingDir)
            || !Directory.Exists(writingDir) || !Directory.Exists(speakingDir))
        {
            logger.LogWarning(
                "MockSampleSeeder: bundle {Index} missing source dir(s); skipping. (L={L} R={R} W={W} S={S})",
                index, Directory.Exists(listeningDir), Directory.Exists(readingDir),
                Directory.Exists(writingDir), Directory.Exists(speakingDir));
            return;
        }

        var now = DateTimeOffset.UtcNow;

        var listening = await BuildListeningPaperAsync(listeningDir, index, now, ct);
        var reading   = await BuildReadingPaperAsync(readingDir, index, now, ct);
        var writing   = await BuildWritingPaperAsync(writingDir, index, now, ct);
        var speaking  = await BuildSpeakingPaperAsync(speakingDir, index, speakingCardType, now, ct);

        if (listening is null || reading is null || writing is null || speaking is null)
        {
            logger.LogWarning(
                "MockSampleSeeder: bundle {Index} could not assemble all four papers; skipping bundle.", index);
            // Detach any tracked-but-unsaved papers so the partial set is
            // not accidentally committed by a later SaveChanges.
            foreach (var entry in db.ChangeTracker.Entries<ContentPaper>().ToList())
            {
                if (entry.State == EntityState.Added) entry.State = EntityState.Detached;
            }
            foreach (var entry in db.ChangeTracker.Entries<ContentPaperAsset>().ToList())
            {
                if (entry.State == EntityState.Added) entry.State = EntityState.Detached;
            }
            foreach (var entry in db.ChangeTracker.Entries<MediaAsset>().ToList())
            {
                if (entry.State == EntityState.Added) entry.State = EntityState.Detached;
            }
            return;
        }

        var papers = new[] { listening, reading, writing, speaking };

        var bundle = new MockBundle
        {
            Id = $"mock-bundle-{Guid.NewGuid():N}",
            Title = $"Sample Mock {index} (Medicine, Practice Set)",
            Slug = bundleSlug,
            MockType = MockTypes.Full,
            ProfessionId = "medicine",
            AppliesToAllProfessions = false,
            Status = ContentStatus.Draft,
            SourceProvenance = Provenance,
            EstimatedDurationMinutes = SectionPlan.Sum(p => p.TimeLimit),
            Difficulty = "exam_ready",
            SourceStatus = MockSourceStatuses.NeedsReview,
            QualityStatus = MockQualityStatuses.Draft,
            ReleasePolicy = MockReleasePolicies.Instant,
            CreatedByAdminId = SeederActorId,
            UpdatedByAdminId = SeederActorId,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.MockBundles.Add(bundle);

        for (var i = 0; i < SectionPlan.Length; i++)
        {
            var (subtest, timeLimit) = SectionPlan[i];
            var paper = papers[i]!;
            db.MockBundleSections.Add(new MockBundleSection
            {
                Id = $"mock-bundle-section-{Guid.NewGuid():N}",
                MockBundleId = bundle.Id,
                SectionOrder = i + 1,
                SubtestCode = subtest,
                ContentPaperId = paper.Id,
                TimeLimitMinutes = timeLimit,
                ReviewEligible = subtest is "writing" or "speaking",
                IsRequired = true,
                CreatedAt = now,
            });
        }

        db.AuditEvents.Add(new AuditEvent
        {
            Id = Guid.NewGuid().ToString("N"),
            OccurredAt = now,
            ActorId = SeederActorId,
            ActorName = SeederActorId,
            Action = "MockBundle.SampleSeeded",
            ResourceType = "MockBundle",
            ResourceId = bundle.Id,
            Details = bundleSlug,
        });

        await db.SaveChangesAsync(ct);
        logger.LogInformation(
            "MockSampleSeeder: created bundle '{Slug}' ({Title}) with {SectionCount} draft sections.",
            bundleSlug, bundle.Title, SectionPlan.Length);
    }

    // ─── Per-subtest paper builders ─────────────────────────────────────────

    private async Task<ContentPaper?> BuildListeningPaperAsync(
        string dir, int index, DateTimeOffset now, CancellationToken ct)
    {
        var question = FindOne(dir, $"Listening Sample {index} Question*.pdf");
        var script   = FindOne(dir, $"Listening Sample {index} Audio-Script.pdf");
        var answers  = FindOne(dir, $"Listening Sample {index} Answer*.pdf");
        var audioDir = Path.Combine(dir, $"Audio {index}");
        var audio    = Directory.Exists(audioDir) ? FindOne(audioDir, "*.mp3") : null;

        if (question is null || script is null || answers is null || audio is null)
        {
            logger.LogWarning(
                "MockSampleSeeder: Listening sample {Index} missing files (q={Q} s={S} a={A} m={M}).",
                index, question is not null, script is not null, answers is not null, audio is not null);
            return null;
        }

        var paper = await CreatePaperAsync(
            subtest: "listening",
            slug: await UniquePaperSlugAsync($"sample-mock-{index}-listening", ct),
            title: $"Sample Mock {index} — Listening",
            cardType: null, letterType: null,
            durationMinutes: 40, now: now);

        await AttachAssetAsync(paper, PaperAssetRole.Audio,         audio,    "Audio",         0, now, ct);
        await AttachAssetAsync(paper, PaperAssetRole.QuestionPaper, question, "Question Paper", 1, now, ct);
        await AttachAssetAsync(paper, PaperAssetRole.AudioScript,   script,   "Audio Script",   2, now, ct);
        await AttachAssetAsync(paper, PaperAssetRole.AnswerKey,     answers,  "Answer Key",     3, now, ct);
        return paper;
    }

    private async Task<ContentPaper?> BuildReadingPaperAsync(
        string dir, int index, DateTimeOffset now, CancellationToken ct)
    {
        var partA  = FindOne(dir, "Part A Reading*.pdf") ?? FindOne(dir, "Reading Part A*.pdf");
        var partBC = FindOne(dir, "Reading Part B*.pdf");
        if (partA is null || partBC is null)
        {
            logger.LogWarning(
                "MockSampleSeeder: Reading sample {Index} missing files (a={A} bc={BC}).",
                index, partA is not null, partBC is not null);
            return null;
        }

        var paper = await CreatePaperAsync(
            subtest: "reading",
            slug: await UniquePaperSlugAsync($"sample-mock-{index}-reading", ct),
            title: $"Sample Mock {index} — Reading",
            cardType: null, letterType: null,
            durationMinutes: 60, now: now);

        // Two QuestionPaper rows distinguished by Part. The (Paper, Role,
        // Part, IsPrimary) unique index permits both as primary.
        await AttachAssetAsync(paper, PaperAssetRole.QuestionPaper, partA,  "Part A",   0, now, ct, part: "A");
        await AttachAssetAsync(paper, PaperAssetRole.QuestionPaper, partBC, "Part B+C", 1, now, ct, part: "B+C");
        return paper;
    }

    private async Task<ContentPaper?> BuildWritingPaperAsync(
        string dir, int index, DateTimeOffset now, CancellationToken ct)
    {
        var pdfs = Directory.EnumerateFiles(dir, "*.pdf", SearchOption.TopDirectoryOnly)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var modelAnswer = pdfs.FirstOrDefault(p =>
            Path.GetFileName(p).Contains("Answer Sheet", StringComparison.OrdinalIgnoreCase));
        var caseNotes = pdfs.FirstOrDefault(p => !ReferenceEquals(p, modelAnswer));
        if (modelAnswer is null || caseNotes is null)
        {
            logger.LogWarning(
                "MockSampleSeeder: Writing sample {Index} missing PDFs (found {Count}).",
                index, pdfs.Count);
            return null;
        }

        var letterType = index switch
        {
            1 => "routine_referral",
            2 => "non_medical_referral",
            3 => "urgent_referral",
            _ => null,
        };

        var paper = await CreatePaperAsync(
            subtest: "writing",
            slug: await UniquePaperSlugAsync($"sample-mock-{index}-writing", ct),
            title: $"Sample Mock {index} — Writing",
            cardType: null, letterType: letterType,
            durationMinutes: 45, now: now);

        await AttachAssetAsync(paper, PaperAssetRole.CaseNotes,   caseNotes,   "Case Notes",   0, now, ct);
        await AttachAssetAsync(paper, PaperAssetRole.ModelAnswer, modelAnswer, "Model Answer", 1, now, ct);
        return paper;
    }

    private async Task<ContentPaper?> BuildSpeakingPaperAsync(
        string dir, int index, string cardType, DateTimeOffset now, CancellationToken ct)
    {
        var roleCard = Directory.EnumerateFiles(dir, "*.pdf", SearchOption.TopDirectoryOnly)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (roleCard is null)
        {
            logger.LogWarning("MockSampleSeeder: Speaking sample {Index} missing role card PDF.", index);
            return null;
        }

        var paper = await CreatePaperAsync(
            subtest: "speaking",
            slug: await UniquePaperSlugAsync($"sample-mock-{index}-speaking", ct),
            title: $"Sample Mock {index} — Speaking",
            cardType: cardType, letterType: null,
            durationMinutes: 20, now: now);

        await AttachAssetAsync(paper, PaperAssetRole.RoleCard, roleCard, "Role Card", 0, now, ct);
        return paper;
    }

    // ─── Helpers ───────────────────────────────────────────────────────────

    private Task<ContentPaper> CreatePaperAsync(
        string subtest, string slug, string title,
        string? cardType, string? letterType,
        int durationMinutes, DateTimeOffset now)
    {
        var paper = new ContentPaper
        {
            Id = Guid.NewGuid().ToString("N"),
            SubtestCode = subtest,
            Title = title,
            Slug = slug,
            ProfessionId = "medicine",
            AppliesToAllProfessions = false,
            Difficulty = "standard",
            EstimatedDurationMinutes = durationMinutes,
            CardType = cardType,
            LetterType = letterType,
            Priority = 0,
            TagsCsv = "seed,sample-mock",
            SourceProvenance = Provenance,
            Status = ContentStatus.Draft,
            ExtractedTextJson = "{}",
            CreatedByAdminId = SeederActorId,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.ContentPapers.Add(paper);
        return Task.FromResult(paper);
    }

    private async Task<string> UniquePaperSlugAsync(string baseSlug, CancellationToken ct)
    {
        var slug = baseSlug;
        var n = 2;
        while (await db.ContentPapers.AsNoTracking().AnyAsync(p => p.Slug == slug, ct))
        {
            slug = $"{baseSlug}-{n++}";
        }
        return slug;
    }

    private async Task AttachAssetAsync(
        ContentPaper paper, PaperAssetRole role, string sourceFilePath,
        string label, int displayOrder, DateTimeOffset now, CancellationToken ct,
        string? part = null)
    {
        var media = await EnsureMediaAssetAsync(sourceFilePath, ct);
        db.ContentPaperAssets.Add(new ContentPaperAsset
        {
            Id = Guid.NewGuid().ToString("N"),
            PaperId = paper.Id,
            Role = role,
            Part = part,
            MediaAssetId = media.Id,
            Title = label,
            DisplayOrder = displayOrder,
            IsPrimary = true,
            CreatedAt = now,
        });
    }

    private async Task<MediaAsset> EnsureMediaAssetAsync(string sourceFilePath, CancellationToken ct)
    {
        var info = new FileInfo(sourceFilePath);
        var sha = await ComputeSha256Async(sourceFilePath, ct);

        // 1. Already persisted MediaAsset with this SHA → reuse.
        var existing = await db.MediaAssets.FirstOrDefaultAsync(m => m.Sha256 == sha, ct);
        if (existing is not null) return existing;

        // 2. Already added to this DbContext (within the same SaveChanges
        //    boundary) → reuse the in-flight entity to avoid PK collisions
        //    when the same file backs multiple roles in one bundle.
        var pending = db.ChangeTracker.Entries<MediaAsset>()
            .Where(e => e.State == EntityState.Added)
            .Select(e => e.Entity)
            .FirstOrDefault(m => m.Sha256 == sha);
        if (pending is not null) return pending;

        var ext = info.Extension.TrimStart('.').ToLowerInvariant();
        var storageKey = $"uploads/published/{sha}.{ext}";
        if (!storage.Exists(storageKey))
        {
            await using var src = new FileStream(sourceFilePath, FileMode.Open, FileAccess.Read,
                FileShare.Read, 81920, useAsync: true);
            await storage.WriteAsync(storageKey, src, ct);
        }

        var (kind, mime) = ext switch
        {
            "mp3" => ("audio",    "audio/mpeg"),
            "pdf" => ("document", "application/pdf"),
            _     => ("document", "application/octet-stream"),
        };

        var media = new MediaAsset
        {
            Id = $"med_{sha[..16]}",
            OriginalFilename = info.Name,
            MimeType = mime,
            Format = ext,
            SizeBytes = info.Length,
            StoragePath = storageKey,
            Status = MediaAssetStatus.Ready,
            Sha256 = sha,
            MediaKind = kind,
            UploadedBy = SeederActorId,
            UploadedAt = DateTimeOffset.UtcNow,
            ProcessedAt = DateTimeOffset.UtcNow,
        };
        db.MediaAssets.Add(media);
        return media;
    }

    private static async Task<string> ComputeSha256Async(string filePath, CancellationToken ct)
    {
        await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read,
            FileShare.Read, 81920, useAsync: true);
        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(stream, ct);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private string? ResolveSourceRoot(string configured)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            var resolved = Path.IsPathRooted(configured)
                ? configured
                : Path.GetFullPath(Path.Combine(env.ContentRootPath, configured));
            if (Directory.Exists(resolved)) return resolved;
        }

        var contentRoot = env.ContentRootPath;
        var candidates = new[]
        {
            Path.Combine(contentRoot, "Project Real Content"),
            Path.Combine(contentRoot, "..", "Project Real Content"),
            Path.Combine(contentRoot, "..", "..", "Project Real Content"),
            Path.Combine(contentRoot, "..", "..", "..", "Project Real Content"),
            Path.Combine(contentRoot, "..", "..", "..", "..", "Project Real Content"),
            Path.Combine(Directory.GetCurrentDirectory(), "Project Real Content"),
        };

        foreach (var c in candidates)
        {
            try
            {
                var full = Path.GetFullPath(c);
                if (Directory.Exists(full)) return full;
            }
            catch { /* ignore malformed candidate */ }
        }
        return null;
    }

    private static string? FindOne(string dir, string pattern)
    {
        try
        {
            return Directory.EnumerateFiles(dir, pattern, SearchOption.TopDirectoryOnly)
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }
        catch { return null; }
    }
}
