using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Content;

namespace OetLearner.Api.Services.Listening;

// ═════════════════════════════════════════════════════════════════════════════
// ListeningSampleSeeder — Slice E of docs/LISTENING-INGESTION-PRD.md
//
// Idempotently ingests the three "Listening Sample {1,2,3}" folders from
// `Project Real Content/` into the database as Draft ContentPapers with all
// four required asset roles (Audio + QuestionPaper + AudioScript + AnswerKey)
// attached. Admins still click Publish from the UI — this seeder NEVER
// auto-publishes (Status = Draft always).
//
// Idempotency keys:
//   • MediaAsset.Sha256 (file dedup, content-addressed)
//   • ContentPaper.Slug (paper dedup)
//
// Disabled by default; operator opts in via:
//   Seed:ListeningSamples:Enabled = true
//
// Strict invariants honoured:
//   • All file *writes* go through IFileStorage.WriteAsync
//   • Reads from `Project Real Content/` use System.IO directly (operator
//     input, mirrors how SeedData reads JSON packs).
// ═════════════════════════════════════════════════════════════════════════════

public interface IListeningSampleSeeder
{
    Task<int> SeedAsync(CancellationToken ct);
}

public sealed class ListeningSampleSeederOptions
{
    public const string SectionName = "Seed:ListeningSamples";

    /// <summary>Root path containing "Listening Sample 1/2/3" folders. When
    /// empty, the seeder probes a list of likely candidates relative to the
    /// content root and the current working directory.</summary>
    public string SourceRoot { get; set; } = string.Empty;

    /// <summary>If false, seeder no-ops (returns 0). Default false so CI and
    /// production containers without the asset folder are unaffected.</summary>
    public bool Enabled { get; set; } = false;
}

public sealed class ListeningSampleSeeder(
    LearnerDbContext db,
    IFileStorage storage,
    IHostEnvironment env,
    IOptions<ListeningSampleSeederOptions> opts,
    ILogger<ListeningSampleSeeder> logger) : IListeningSampleSeeder
{
    private const string DefaultListeningFolderName =
        "Listening ( IMPORTANT NOTE =  Same for All Professions )";

    private static readonly (PaperAssetRole Role, string Label, int Order)[] RoleOrder =
    [
        (PaperAssetRole.QuestionPaper, "Question Paper", 0),
        (PaperAssetRole.AudioScript,   "Audio Script",   1),
        (PaperAssetRole.AnswerKey,     "Answer Key",     2),
        (PaperAssetRole.Audio,         "Audio",          3),
    ];

    public async Task<int> SeedAsync(CancellationToken ct)
    {
        var options = opts.Value;
        if (!options.Enabled)
        {
            logger.LogInformation(
                "ListeningSampleSeeder disabled (Seed:ListeningSamples:Enabled=false); skipping.");
            return 0;
        }

        var sourceRoot = ResolveSourceRoot(options.SourceRoot);
        if (sourceRoot is null)
        {
            logger.LogInformation(
                "ListeningSampleSeeder: source folder not found (looked for '{Folder}' near content root). Skipping.",
                DefaultListeningFolderName);
            return 0;
        }

        logger.LogInformation("ListeningSampleSeeder: ingesting from {Root}", sourceRoot);

        var created = 0;
        for (var i = 1; i <= 3; i++)
        {
            try
            {
                if (await SeedSampleAsync(sourceRoot, i, ct))
                {
                    created++;
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "ListeningSampleSeeder: sample {SampleIndex} failed; continuing with remaining samples.",
                    i);
            }
        }

        return created;
    }

    private string? ResolveSourceRoot(string configured)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return Directory.Exists(configured) ? Path.GetFullPath(configured) : null;
        }

        var contentRoot = env.ContentRootPath;
        var candidates = new[]
        {
            Path.Combine(contentRoot, "Project Real Content", DefaultListeningFolderName),
            Path.Combine(contentRoot, "..", "Project Real Content", DefaultListeningFolderName),
            Path.Combine(contentRoot, "..", "..", "Project Real Content", DefaultListeningFolderName),
            Path.Combine(contentRoot, "..", "..", "..", "Project Real Content", DefaultListeningFolderName),
            Path.Combine(contentRoot, "..", "..", "..", "..", "Project Real Content", DefaultListeningFolderName),
            Path.Combine(Directory.GetCurrentDirectory(), "Project Real Content", DefaultListeningFolderName),
        };

        foreach (var candidate in candidates)
        {
            try
            {
                var full = Path.GetFullPath(candidate);
                if (Directory.Exists(full)) return full;
            }
            catch
            {
                // ignore malformed candidate
            }
        }

        return null;
    }

    /// <summary>Returns true when a *new* ContentPaper row was inserted.</summary>
    private async Task<bool> SeedSampleAsync(string sourceRoot, int sampleIndex, CancellationToken ct)
    {
        var sampleDir = Path.Combine(sourceRoot, $"Listening Sample {sampleIndex}");
        if (!Directory.Exists(sampleDir))
        {
            logger.LogWarning("ListeningSampleSeeder: '{Dir}' not found; skipping sample {SampleIndex}.",
                sampleDir, sampleIndex);
            return false;
        }

        var question = FindOne(sampleDir, $"Listening Sample {sampleIndex} Question*.pdf");
        var script   = FindOne(sampleDir, $"Listening Sample {sampleIndex} Audio-Script.pdf");
        var answers  = FindOne(sampleDir, $"Listening Sample {sampleIndex} Answer*.pdf")
                    ?? FindOne(sampleDir, $"Listening Sample {sampleIndex} Answers.pdf");
        var audioPath = Path.Combine(sampleDir, $"Audio {sampleIndex}", $"Audio {sampleIndex}.mp3");
        var audio = File.Exists(audioPath) ? audioPath : null;

        if (question is null || script is null || answers is null || audio is null)
        {
            logger.LogWarning(
                "ListeningSampleSeeder: sample {SampleIndex} is missing one or more files (question={Q}, script={S}, answers={A}, audio={M}); skipping.",
                sampleIndex, question is not null, script is not null, answers is not null, audio is not null);
            return false;
        }

        var slug = $"listening-sample-{sampleIndex}-medicine";
        var paperId = $"cp_listening_sample_{sampleIndex}";

        var paper = await db.ContentPapers
            .Include(p => p.Assets)
            .FirstOrDefaultAsync(p => p.Slug == slug, ct);

        var createdNew = false;
        if (paper is null)
        {
            var now = DateTimeOffset.UtcNow;
            paper = new ContentPaper
            {
                Id = paperId,
                Slug = slug,
                SubtestCode = "listening",
                Title = $"Real Sample {sampleIndex} — Listening",
                ProfessionId = "medicine",
                AppliesToAllProfessions = false,
                Difficulty = "B2",
                EstimatedDurationMinutes = 45,
                Status = ContentStatus.Draft,
                Priority = 0,
                TagsCsv = string.Empty,
                SourceProvenance =
                    "Real OET sample (admin-curated, ingested via ListeningSampleSeeder)",
                ExtractedTextJson = "{}",
                CreatedByAdminId = "system:seed",
                CreatedAt = now,
                UpdatedAt = now,
            };
            db.ContentPapers.Add(paper);
            await db.SaveChangesAsync(ct);
            createdNew = true;
        }

        // Idempotently upsert each role's MediaAsset + ContentPaperAsset.
        await UpsertAssetAsync(paper, PaperAssetRole.QuestionPaper, question, ct);
        await UpsertAssetAsync(paper, PaperAssetRole.AudioScript,   script,   ct);
        await UpsertAssetAsync(paper, PaperAssetRole.AnswerKey,     answers,  ct);
        await UpsertAssetAsync(paper, PaperAssetRole.Audio,         audio,    ct);

        return createdNew;
    }

    private async Task UpsertAssetAsync(
        ContentPaper paper,
        PaperAssetRole role,
        string sourceFilePath,
        CancellationToken ct)
    {
        // Skip if this (PaperId, Role) is already attached.
        if (paper.Assets.Any(a => a.Role == role))
        {
            return;
        }

        var media = await EnsureMediaAssetAsync(sourceFilePath, ct);

        var (_, _, order) = RoleOrder.First(r => r.Role == role);
        var label = RoleOrder.First(r => r.Role == role).Label;
        var asset = new ContentPaperAsset
        {
            Id = $"cpa_{paper.Id}_{role.ToString().ToLowerInvariant()}",
            PaperId = paper.Id,
            Role = role,
            MediaAssetId = media.Id,
            Title = label,
            DisplayOrder = order,
            IsPrimary = true,
            Part = null,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.ContentPaperAssets.Add(asset);
        paper.Assets.Add(asset);
        await db.SaveChangesAsync(ct);
    }

    private async Task<MediaAsset> EnsureMediaAssetAsync(string sourceFilePath, CancellationToken ct)
    {
        var info = new FileInfo(sourceFilePath);
        var sha = await ComputeSha256Async(sourceFilePath, ct);

        var existing = await db.MediaAssets.FirstOrDefaultAsync(m => m.Sha256 == sha, ct);
        var ext = info.Extension.TrimStart('.').ToLowerInvariant();
        var storageKey = $"uploads/published/{sha}.{ext}";

        if (!storage.Exists(storageKey))
        {
            await using var src = new FileStream(sourceFilePath, FileMode.Open, FileAccess.Read,
                FileShare.Read, 81920, useAsync: true);
            await storage.WriteAsync(storageKey, src, ct);
        }

        if (existing is not null)
        {
            // If a prior row points at a different key (e.g. legacy sharded
            // path) leave it; otherwise ensure StoragePath matches.
            return existing;
        }

        var (kind, mime) = ext switch
        {
            "mp3"  => ("audio",    "audio/mpeg"),
            "pdf"  => ("document", "application/pdf"),
            _      => ("document", "application/octet-stream"),
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
            UploadedBy = "system:seed",
            UploadedAt = DateTimeOffset.UtcNow,
            ProcessedAt = DateTimeOffset.UtcNow,
        };
        db.MediaAssets.Add(media);
        await db.SaveChangesAsync(ct);
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

    private static string? FindOne(string dir, string pattern)
    {
        try
        {
            return Directory.EnumerateFiles(dir, pattern, SearchOption.TopDirectoryOnly)
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }
}
