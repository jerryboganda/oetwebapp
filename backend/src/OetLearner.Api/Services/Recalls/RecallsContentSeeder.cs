using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Recalls;

/// <summary>
/// Recalls Content Pack v1 (2026-05-05).
///
/// Idempotent boot-time loader that hydrates <see cref="VocabularyTerm"/> rows
/// from versioned JSON files in <c>Data/SeedData/recalls/*.json</c>.
///
/// Contract:
/// - Source of truth is Git (the JSON files in repo).
/// - Survives container rebuilds because (a) the postgres volume persists, and
///   (b) even if the volume is wiped the seeder rehydrates from the JSON.
/// - Upsert key: <c>(Term, ExamTypeCode, ProfessionId)</c>.
/// - Content hash: SHA-256 of canonical row JSON. Only updates DB rows whose hash
///   changed since last seed; skips identical rows.
/// - <b>Never deletes.</b> If a term is removed from JSON, the DB row stays
///   (admins archive via <c>/admin/content/vocabulary</c>).
/// - Default <c>Status='draft'</c>. Admin promotes to <c>'active'</c> after
///   review (zero-hallucination contract — see docs/RECALLS-DATA-ENTRY-PLAN.md §4).
/// - Skips files that fail JSON schema validation (logged warning, never throws).
/// - Skips when DB is in-memory and there are no JSON files (test mode).
/// </summary>
public static class RecallsContentSeeder
{
    private const string SeedFolderRelative = "Data/SeedData/recalls";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static async Task EnsureAsync(
        LearnerDbContext db,
        IWebHostEnvironment environment,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        var folder = ResolveSeedFolder(environment);
        if (!Directory.Exists(folder))
        {
            logger.LogInformation("RecallsContentSeeder: seed folder not found at {Folder}; skipping.", folder);
            return;
        }

        var files = Directory.GetFiles(folder, "*.json", SearchOption.TopDirectoryOnly);
        if (files.Length == 0)
        {
            logger.LogInformation("RecallsContentSeeder: no JSON files in {Folder}; skipping.", folder);
            return;
        }

        var totalAdded = 0;
        var totalUpdated = 0;
        var totalSkipped = 0;
        var totalRejected = 0;

        foreach (var file in files.OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var (added, updated, skipped, rejected) = await SeedFileAsync(db, file, logger, cancellationToken);
                totalAdded += added;
                totalUpdated += updated;
                totalSkipped += skipped;
                totalRejected += rejected;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "RecallsContentSeeder: failed to seed {File}; continuing.", Path.GetFileName(file));
            }
        }

        logger.LogInformation(
            "RecallsContentSeeder: complete. files={Files}, added={Added}, updated={Updated}, unchanged={Skipped}, rejected={Rejected}.",
            files.Length, totalAdded, totalUpdated, totalSkipped, totalRejected);
    }

    private static async Task<(int added, int updated, int skipped, int rejected)> SeedFileAsync(
        LearnerDbContext db,
        string path,
        ILogger logger,
        CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        var pack = await JsonSerializer.DeserializeAsync<SeedPack>(stream, JsonOptions, ct);
        if (pack is null || pack.Terms is null)
        {
            logger.LogWarning("RecallsContentSeeder: {File} has no terms; skipping.", Path.GetFileName(path));
            return (0, 0, 0, 0);
        }

        var profession = (pack.ProfessionId ?? Path.GetFileNameWithoutExtension(path)).Trim().ToLowerInvariant();
        var examType = string.IsNullOrWhiteSpace(pack.ExamTypeCode) ? "OET" : pack.ExamTypeCode.Trim();
        var defaultStatus = string.IsNullOrWhiteSpace(pack.DefaultStatus) ? "draft" : pack.DefaultStatus.Trim();
        var baseProvenance = string.IsNullOrWhiteSpace(pack.SourceProvenance)
            ? "seed:recalls-content-pack-v1"
            : pack.SourceProvenance.Trim();

        // Pre-load existing terms for this (profession, examType) so we hash-compare
        // without re-querying per row.
        var existing = await db.VocabularyTerms
            .Where(t => t.ProfessionId == profession && t.ExamTypeCode == examType)
            .ToDictionaryAsync(
                t => Normalise(t.Term),
                t => t,
                StringComparer.Ordinal,
                ct);

        var added = 0;
        var updated = 0;
        var skipped = 0;
        var rejected = 0;
        var seenInFile = new HashSet<string>(StringComparer.Ordinal);

        foreach (var raw in pack.Terms)
        {
            var validation = Validate(raw);
            if (validation is not null)
            {
                rejected++;
                logger.LogWarning("RecallsContentSeeder: rejected term '{Term}' in {File}: {Reason}",
                    raw.Term ?? "(null)", Path.GetFileName(path), validation);
                continue;
            }

            var key = Normalise(raw.Term!);
            if (!seenInFile.Add(key))
            {
                rejected++;
                logger.LogWarning("RecallsContentSeeder: duplicate term '{Term}' in {File}; skipping second occurrence.",
                    raw.Term, Path.GetFileName(path));
                continue;
            }

            var slug = Slug(raw.Term!);
            var rowProvenance = $"{baseProvenance}:{profession}:{slug}";
            var hash = ComputeHash(raw, rowProvenance);

            if (existing.TryGetValue(key, out var current))
            {
                // Only update if the seed-managed payload changed AND the row was
                // originally seeded by us. Never touch admin-edited rows whose
                // SourceProvenance no longer carries our prefix.
                var wasSeedManaged = current.SourceProvenance is not null
                    && current.SourceProvenance.StartsWith(baseProvenance, StringComparison.Ordinal);
                if (!wasSeedManaged)
                {
                    skipped++;
                    continue;
                }

                if (current.SourceProvenance == $"{rowProvenance}#{hash}")
                {
                    skipped++;
                    continue;
                }

                ApplyTo(current, raw, profession, examType, defaultStatus, $"{rowProvenance}#{hash}");
                current.UpdatedAt = DateTimeOffset.UtcNow;
                updated++;
            }
            else
            {
                var entity = new VocabularyTerm
                {
                    Id = $"vt-{Guid.NewGuid():N}",
                    Term = raw.Term!.Trim(),
                    ExamTypeCode = examType,
                    ProfessionId = profession,
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow,
                };
                ApplyTo(entity, raw, profession, examType, defaultStatus, $"{rowProvenance}#{hash}");
                db.VocabularyTerms.Add(entity);
                added++;
            }
        }

        if (added > 0 || updated > 0)
        {
            await db.SaveChangesAsync(ct);
        }
        return (added, updated, skipped, rejected);
    }

    private static void ApplyTo(
        VocabularyTerm e,
        SeedTerm raw,
        string profession,
        string examType,
        string defaultStatus,
        string provenance)
    {
        e.Definition = Truncate(raw.Definition!.Trim(), 1024);
        e.ExampleSentence = Truncate(raw.ExampleSentence!.Trim(), 2048);
        e.Category = Truncate((raw.Category ?? "general").Trim().ToLowerInvariant(), 64);
        e.Difficulty = (raw.Difficulty ?? "medium").Trim().ToLowerInvariant() switch
        {
            "easy" or "medium" or "hard" => raw.Difficulty!.Trim().ToLowerInvariant(),
            _ => "medium",
        };
        e.IpaPronunciation = Truncate(raw.Ipa, 64);
        e.AmericanSpelling = Truncate(raw.AmericanSpelling, 128);
        e.ContextNotes = Truncate(raw.ContextNotes, 1024);
        e.SynonymsJson = SerialiseList(raw.Synonyms);
        e.CollocationsJson = SerialiseList(raw.Collocations);
        e.RelatedTermsJson = SerialiseList(raw.RelatedTerms);
        e.CommonMistakesJson = SerialiseList(raw.CommonMistakes);
        e.SimilarSoundingJson = SerialiseList(raw.SimilarSounding);
        e.OetSubtestTagsJson = SerialiseList(raw.OetSubtestTags?.Select(t => t.Trim().ToLowerInvariant()).ToList());
        e.SourceProvenance = Truncate(provenance, 512);
        // Only set Status on insert; preserve admin-promoted status on update.
        if (string.IsNullOrWhiteSpace(e.Status) || e.Status == "active" || e.Status == "archived")
        {
            // already promoted or archived — keep as-is on update
            if (string.IsNullOrWhiteSpace(e.Status)) e.Status = defaultStatus;
        }
        else
        {
            e.Status = defaultStatus;
        }
        e.ProfessionId = profession;
        e.ExamTypeCode = examType;
    }

    private static string SerialiseList(List<string>? list)
    {
        if (list is null || list.Count == 0) return "[]";
        var cleaned = list
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return JsonSerializer.Serialize(cleaned);
    }

    private static string? Validate(SeedTerm t)
    {
        if (t is null) return "term is null";
        if (string.IsNullOrWhiteSpace(t.Term)) return "term required";
        if (t.Term!.Length > 128) return "term too long (>128)";
        if (string.IsNullOrWhiteSpace(t.Definition)) return "definition required";
        if (string.IsNullOrWhiteSpace(t.ExampleSentence)) return "exampleSentence required";
        if (string.IsNullOrWhiteSpace(t.Category)) return "category required";
        if (t.OetSubtestTags is null || t.OetSubtestTags.Count == 0) return "oetSubtestTags required (≥1)";
        return null;
    }

    private static string Normalise(string s) => s.Trim().ToLowerInvariant();

    private static string Slug(string s)
    {
        var lower = s.Trim().ToLowerInvariant();
        var sb = new StringBuilder(lower.Length);
        foreach (var c in lower)
        {
            if (char.IsLetterOrDigit(c)) sb.Append(c);
            else if (c == ' ' || c == '-' || c == '_') sb.Append('-');
        }
        var result = sb.ToString().Trim('-');
        return result.Length > 64 ? result[..64] : result;
    }

    private static string Truncate(string? value, int max)
    {
        if (string.IsNullOrEmpty(value)) return value!;
        return value.Length <= max ? value : value[..max];
    }

    private static string ComputeHash(SeedTerm raw, string provenance)
    {
        var canonical = JsonSerializer.Serialize(new
        {
            t = raw.Term?.Trim().ToLowerInvariant(),
            d = raw.Definition?.Trim(),
            e = raw.ExampleSentence?.Trim(),
            c = raw.Category?.Trim().ToLowerInvariant(),
            tags = raw.OetSubtestTags?.Select(s => s.Trim().ToLowerInvariant()).OrderBy(s => s, StringComparer.Ordinal).ToList(),
            diff = raw.Difficulty?.Trim().ToLowerInvariant(),
            ipa = raw.Ipa?.Trim(),
            us = raw.AmericanSpelling?.Trim(),
            syn = raw.Synonyms?.OrderBy(s => s, StringComparer.Ordinal).ToList(),
            col = raw.Collocations?.OrderBy(s => s, StringComparer.Ordinal).ToList(),
            rel = raw.RelatedTerms?.OrderBy(s => s, StringComparer.Ordinal).ToList(),
            ctx = raw.ContextNotes?.Trim(),
            mis = raw.CommonMistakes?.OrderBy(s => s, StringComparer.Ordinal).ToList(),
            ss = raw.SimilarSounding?.OrderBy(s => s, StringComparer.Ordinal).ToList(),
            p = provenance,
        });
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(bytes).ToLowerInvariant()[..16];
    }

    private static string ResolveSeedFolder(IWebHostEnvironment env)
    {
        // Look in (in order): ContentRoot, AppContext.BaseDirectory.
        var candidates = new[]
        {
            Path.Combine(env.ContentRootPath, SeedFolderRelative),
            Path.Combine(AppContext.BaseDirectory, SeedFolderRelative),
        };
        return candidates.FirstOrDefault(Directory.Exists) ?? candidates[0];
    }

    // ── Seed payload DTOs ────────────────────────────────────────────────

    private sealed class SeedPack
    {
        public int SchemaVersion { get; set; } = 1;
        public string? ProfessionId { get; set; }
        public string? ExamTypeCode { get; set; }
        public string? SourceProvenance { get; set; }
        public string? DefaultStatus { get; set; }
        public List<SeedTerm>? Terms { get; set; }
    }

    private sealed class SeedTerm
    {
        public string? Term { get; set; }
        public string? Definition { get; set; }
        public string? ExampleSentence { get; set; }
        public string? Category { get; set; }
        public List<string>? OetSubtestTags { get; set; }
        public string? Difficulty { get; set; }
        public string? Ipa { get; set; }
        public string? AmericanSpelling { get; set; }
        public List<string>? Synonyms { get; set; }
        public List<string>? Collocations { get; set; }
        public List<string>? RelatedTerms { get; set; }
        public string? ContextNotes { get; set; }
        public List<string>? CommonMistakes { get; set; }
        public List<string>? SimilarSounding { get; set; }
    }
}
