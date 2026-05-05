using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Recalls;

/// <summary>
/// Recall-set seeder (Recalls Year/Source Pack v1, 2026-05-05).
///
/// Reads packs from <c>Data/SeedData/recall-sets/*.json</c> and ensures every
/// listed term exists as a <see cref="VocabularyTerm"/> row tagged with the
/// pack's <see cref="VocabularyTerm.RecallSetCodesJson"/> code.
///
/// Behaviour (idempotent, never destructive):
/// <list type="bullet">
///   <item>Term EXISTS (any provenance): tag is added if missing. Other fields
///         are NOT touched — admin curation is preserved.</item>
///   <item>Term MISSING: a new row is INSERTED with <c>Status=draft</c>,
///         <c>Category=recall_term</c>, ProfessionId=null (general),
///         placeholder Definition/ExampleSentence, and the recall-set code
///         applied. Admin enriches via the existing CRUD pages at
///         <c>/admin/content/vocabulary/{id}</c> and promotes to <c>active</c>.</item>
///   <item>Multi-tag: a term in several packs (e.g. "headaches" in old +
///         2023-2025 + 2026) ends up with all codes in
///         <c>RecallSetCodesJson</c>.</item>
///   <item>Failures (bad JSON, etc.) are logged and skipped — never throw at boot.</item>
/// </list>
/// </summary>
public static class RecallSetTagSeeder
{
    private const string SeedFolderRelative = "Data/SeedData/recall-sets";
    private const string CreatedCategory = "recall_term";
    private const string CreatedDefinitionPlaceholder = "(pending — admin to add definition)";
    private const string CreatedExamplePlaceholder = "(pending — admin to add example sentence)";
    private const string ProvenancePrefix = "seed:recall-set-pack-v1";

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
            logger.LogInformation("RecallSetTagSeeder: seed folder not found at {Folder}; skipping.", folder);
            return;
        }

        var files = Directory.GetFiles(folder, "*.json", SearchOption.TopDirectoryOnly);
        if (files.Length == 0)
        {
            logger.LogInformation("RecallSetTagSeeder: no JSON files in {Folder}; skipping.", folder);
            return;
        }

        var totalCreated = 0;
        var totalTagged = 0;

        foreach (var file in files.OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var (created, tagged) = await SeedFileAsync(db, file, logger, cancellationToken);
                totalCreated += created;
                totalTagged += tagged;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "RecallSetTagSeeder: failed to seed {File}; continuing.", Path.GetFileName(file));
            }
        }

        logger.LogInformation(
            "RecallSetTagSeeder: complete. files={Files}, terms_created={Created}, terms_tagged={Tagged}.",
            files.Length, totalCreated, totalTagged);
    }

    private static async Task<(int created, int tagged)> SeedFileAsync(
        LearnerDbContext db,
        string path,
        ILogger logger,
        CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        var pack = await JsonSerializer.DeserializeAsync<TagPack>(stream, JsonOptions, ct);
        if (pack is null)
        {
            logger.LogWarning("RecallSetTagSeeder: {File} could not be parsed; skipping.", Path.GetFileName(path));
            return (0, 0);
        }

        var code = RecallSetCodes.Normalise(pack.RecallSetCode);
        if (code is null)
        {
            logger.LogWarning("RecallSetTagSeeder: {File} has unknown recallSetCode '{Code}'; skipping.",
                Path.GetFileName(path), pack.RecallSetCode);
            return (0, 0);
        }

        if (pack.Terms is null || pack.Terms.Count == 0)
        {
            logger.LogInformation("RecallSetTagSeeder: {File} has no terms (placeholder); skipping.", Path.GetFileName(path));
            return (0, 0);
        }

        var examType = string.IsNullOrWhiteSpace(pack.ExamTypeCode) ? "OET" : pack.ExamTypeCode.Trim();
        var profession = string.IsNullOrWhiteSpace(pack.ProfessionId) ? null : pack.ProfessionId.Trim().ToLowerInvariant();
        var provenance = string.IsNullOrWhiteSpace(pack.SourceProvenance)
            ? $"{ProvenancePrefix}:{code}"
            : Truncate(pack.SourceProvenance.Trim(), 512);

        // De-duplicate inputs by lowered key while preserving the first-seen
        // display form (e.g. "Meniere's disease", not "meniere's disease").
        var wantedDisplay = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var raw in pack.Terms)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var display = raw.Trim();
            var key = display.ToLowerInvariant();
            if (!wantedDisplay.ContainsKey(key)) wantedDisplay[key] = display;
        }
        if (wantedDisplay.Count == 0) return (0, 0);

        var query = db.VocabularyTerms.Where(v => v.ExamTypeCode == examType);
        if (profession is null) query = query.Where(v => v.ProfessionId == null);
        else query = query.Where(v => v.ProfessionId == profession);

        var candidates = await query.ToListAsync(ct);
        var byKey = candidates
            .GroupBy(v => v.Term.Trim().ToLowerInvariant(), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        var created = 0;
        var tagged = 0;
        var changed = false;
        var now = DateTimeOffset.UtcNow;

        foreach (var (key, display) in wantedDisplay)
        {
            if (byKey.TryGetValue(key, out var rows))
            {
                foreach (var row in rows)
                {
                    var current = ParseCodes(row.RecallSetCodesJson);
                    if (current.Add(code))
                    {
                        row.RecallSetCodesJson = SerialiseCodes(current);
                        row.UpdatedAt = now;
                        tagged++;
                        changed = true;
                    }
                }
                continue;
            }

            var entity = new VocabularyTerm
            {
                Id = $"vt-{Guid.NewGuid():N}",
                Term = Truncate(display, 128),
                Definition = CreatedDefinitionPlaceholder,
                ExampleSentence = CreatedExamplePlaceholder,
                ExamTypeCode = examType,
                ProfessionId = profession,
                Category = CreatedCategory,
                Difficulty = "medium",
                SynonymsJson = "[]",
                CollocationsJson = "[]",
                RelatedTermsJson = "[]",
                CommonMistakesJson = "[]",
                SimilarSoundingJson = "[]",
                OetSubtestTagsJson = "[]",
                RecallSetCodesJson = SerialiseCodes(new[] { code }),
                SourceProvenance = provenance,
                Status = "draft",
                CreatedAt = now,
                UpdatedAt = now,
            };
            db.VocabularyTerms.Add(entity);
            byKey[key] = new List<VocabularyTerm> { entity };
            created++;
            changed = true;
        }

        if (changed) await db.SaveChangesAsync(ct);
        logger.LogInformation(
            "RecallSetTagSeeder: {File} created {Created} new term(s) and tagged {Tagged} existing term(s) with '{Code}'.",
            Path.GetFileName(path), created, tagged, code);
        return (created, tagged);
    }

    private static HashSet<string> ParseCodes(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "[]") return new HashSet<string>(StringComparer.Ordinal);
        try
        {
            var arr = JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
            return arr
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim().ToLowerInvariant())
                .ToHashSet(StringComparer.Ordinal);
        }
        catch
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }
    }

    private static string SerialiseCodes(IEnumerable<string> codes)
    {
        var ordered = codes.OrderBy(c => c, StringComparer.Ordinal).ToList();
        return JsonSerializer.Serialize(ordered);
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s.Substring(0, max);

    private static string ResolveSeedFolder(IWebHostEnvironment env)
    {
        var candidates = new[]
        {
            Path.Combine(env.ContentRootPath, SeedFolderRelative),
            Path.Combine(AppContext.BaseDirectory, SeedFolderRelative),
        };
        return candidates.FirstOrDefault(Directory.Exists) ?? candidates[0];
    }

    private sealed class TagPack
    {
        public int SchemaVersion { get; set; } = 1;
        public string? RecallSetCode { get; set; }
        public string? ExamTypeCode { get; set; }
        public string? ProfessionId { get; set; }
        public string? DisplayName { get; set; }
        public string? SourceProvenance { get; set; }
        public List<string>? Terms { get; set; }
    }
}
