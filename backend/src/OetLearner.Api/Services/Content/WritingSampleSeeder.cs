using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;

namespace OetLearner.Api.Services.Content;

/// <summary>
/// One-shot startup seeder that loads canonical OET Writing sample papers
/// from one or more JSON files under <c>Data/Seeds/</c>. The default v1 file
/// holds the 5-6 medicine PDFs extracted by <c>scripts/extract-writing-pdfs</c>;
/// additional batches (e.g. multi-profession v2 stubs) are layered via the
/// <see cref="WritingSeedOptions.SeedFilePathsCsv"/> option.
///
/// The seeder is idempotent — papers are matched by <c>SourceProvenance</c>
/// seed id and never overwritten once created. Papers default to
/// <see cref="ContentStatus.Published"/>; <see cref="WritingSeedOptions.AutoPublish"/>
/// (and the per-file <see cref="WritingSeedOptions.AutoPublishByFile"/> override)
/// flip them to <c>Draft</c> so stub rows stay invisible until the content team
/// fills them in.
///
/// Disabled by default. Enable per-environment with
/// <c>Content:WritingSeed:Enabled=true</c>.
/// </summary>
public sealed class WritingSampleSeeder(
    IServiceScopeFactory scopeFactory,
    IOptions<WritingSeedOptions> options,
    IHostEnvironment env,
    ILogger<WritingSampleSeeder> logger) : BackgroundService
{
    private const string SeederAdminId = "system:writing-seed";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await SeedAsync(stoppingToken);
        }
        catch (OperationCanceledException) { /* shutdown */ }
        catch (Exception ex)
        {
            logger.LogError(ex, "WritingSampleSeeder failed.");
        }
    }

    /// <summary>Public entry point so tests can drive the seeder without
    /// reaching into reflection. Returns the total number of rows created
    /// across all seed files on this invocation (0 when disabled, all files
    /// missing, or all rows already present).</summary>
    public async Task<int> SeedAsync(CancellationToken ct)
    {
        if (!options.Value.Enabled)
        {
            logger.LogDebug("WritingSampleSeeder disabled (Content:WritingSeed:Enabled=false).");
            return 0;
        }

        var seedFiles = ResolveSeedFiles();
        if (seedFiles.Count == 0)
        {
            logger.LogInformation("WritingSampleSeeder skipped — no seed files configured.");
            return 0;
        }

        var totalCreated = 0;
        foreach (var seedFile in seedFiles)
        {
            ct.ThrowIfCancellationRequested();
            totalCreated += await SeedFromFileAsync(seedFile, ct);
        }
        return totalCreated;
    }

    private async Task<int> SeedFromFileAsync(string seedFile, CancellationToken ct)
    {
        if (!File.Exists(seedFile))
        {
            logger.LogInformation("WritingSampleSeeder skipped — seed file not found at {Path}.", seedFile);
            return 0;
        }

        WritingSeedFile? doc;
        await using (var stream = File.OpenRead(seedFile))
        {
            doc = await JsonSerializer.DeserializeAsync<WritingSeedFile>(stream,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, ct);
        }
        if (doc?.Samples is null || doc.Samples.Count == 0)
        {
            logger.LogInformation("WritingSampleSeeder: seed file {Path} has no samples.", seedFile);
            return 0;
        }

        var autoPublish = ResolveAutoPublish(seedFile);

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();

        int created = 0, skipped = 0;
        foreach (var sample in doc.Samples)
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(sample.SeedId)
                || string.IsNullOrWhiteSpace(sample.LetterType)
                || string.IsNullOrWhiteSpace(sample.Slug)
                || !WritingContentStructure.IsCanonicalLetterType(sample.LetterType))
            {
                logger.LogWarning("WritingSampleSeeder: skipping malformed sample {Slug}.", sample.Slug);
                continue;
            }

            if (!WritingContentStructure.IsLetterTypeAllowedForProfession(sample.Profession, sample.LetterType))
            {
                logger.LogWarning(
                    "WritingSampleSeeder: skipping sample {Slug} — letter type '{LetterType}' is not allowed for profession '{Profession}'.",
                    sample.Slug, sample.LetterType, sample.Profession);
                continue;
            }

            // Idempotency: match by SourceProvenance == seedId.
            var exists = await db.ContentPapers
                .AsNoTracking()
                .AnyAsync(p => p.SourceProvenance == sample.SeedId, ct);
            if (exists) { skipped++; continue; }

            var slugCollision = await db.ContentPapers
                .AsNoTracking()
                .AnyAsync(p => p.Slug == sample.Slug, ct);
            if (slugCollision)
            {
                logger.LogInformation(
                    "WritingSampleSeeder: slug '{Slug}' already in use — skipping (assumed pre-existing).",
                    sample.Slug);
                skipped++;
                continue;
            }

            var now = DateTimeOffset.UtcNow;
            var structure = new Dictionary<string, object?>
            {
                ["letterType"] = sample.LetterType,
                ["taskPrompt"] = BuildDefaultTaskPrompt(sample),
                ["caseNotes"] = sample.CaseNotesText ?? string.Empty,
                ["modelAnswer"] = sample.ModelAnswerText ?? string.Empty,
                ["criteriaFocus"] = new[]
                {
                    "purpose", "content", "conciseness_clarity",
                    "genre_style", "organisation_layout", "language",
                },
                ["seed"] = new Dictionary<string, object?>
                {
                    ["seedId"] = sample.SeedId,
                    ["sourceFolder"] = sample.SourceFolder,
                    ["caseNotesPdf"] = sample.CaseNotesPdf,
                    ["modelAnswerPdf"] = sample.ModelAnswerPdf,
                    ["seededAt"] = now.ToString("o"),
                },
            };

            var extractedTextJson = JsonSupport.Serialize(new Dictionary<string, object?>
            {
                [WritingContentStructure.StructureKey] = structure,
            });

            var status = autoPublish ? ContentStatus.Published : ContentStatus.Draft;
            var sourceFileName = Path.GetFileNameWithoutExtension(seedFile);
            var paper = new ContentPaper
            {
                Id = Guid.NewGuid().ToString("N"),
                SubtestCode = "writing",
                Title = sample.Title ?? sample.Slug,
                Slug = sample.Slug,
                ProfessionId = string.IsNullOrWhiteSpace(sample.Profession) ? null : sample.Profession.ToLowerInvariant(),
                AppliesToAllProfessions = false,
                Difficulty = "standard",
                EstimatedDurationMinutes = 45,
                CardType = null,
                LetterType = sample.LetterType,
                Priority = 0,
                TagsCsv = $"seed,{sourceFileName}",
                SourceProvenance = sample.SeedId,
                Status = status,
                PublishedAt = autoPublish ? now : null,
                CreatedAt = now,
                UpdatedAt = now,
                CreatedByAdminId = SeederAdminId,
                ExtractedTextJson = extractedTextJson,
            };
            // PublishedRevisionId points at the latest committed revision and
            // is required by the ContentItem FK regardless of publish status.
            // Setting it on Draft seeds keeps the schema valid; the actual
            // visibility gate is Status + PublishedAt.
            paper.PublishedRevisionId ??= paper.Id;
            db.ContentPapers.Add(paper);

            // Project to learner-visible ContentItem so the paper is reachable
            // through the standard learner content surfaces (mirrors
            // ContentPaperService.UpsertWritingContentItemAsync but called
            // inline because we bypass the asset-presence publish gate —
            // seeded papers are inline-text only). Draft papers still get a
            // matching ContentItem so the admin surfaces can see them; the
            // ContentItem status follows the paper status.
            var detail = WritingContentStructure.BuildContentItemDetail(paper);
            var criteriaFocus = SpeakingContentStructure.ReadStringList(
                SpeakingContentStructure.ReadValue(detail, "criteriaFocus"));
            db.ContentItems.Add(new ContentItem
            {
                Id = paper.Id,
                ContentType = "writing_task",
                SubtestCode = "writing",
                Title = paper.Title,
                ProfessionId = paper.AppliesToAllProfessions ? null : paper.ProfessionId,
                Difficulty = paper.Difficulty,
                EstimatedDurationMinutes = paper.EstimatedDurationMinutes,
                CriteriaFocusJson = JsonSupport.Serialize(criteriaFocus),
                ScenarioType = paper.LetterType,
                ModeSupportJson = JsonSupport.Serialize(new[] { "learning", "exam" }),
                PublishedRevisionId = paper.PublishedRevisionId,
                Status = status,
                CaseNotes = WritingContentStructure.BuildCaseNotesText(detail),
                DetailJson = JsonSupport.Serialize(detail),
                ModelAnswerJson = JsonSupport.Serialize(WritingContentStructure.BuildModelAnswerPayload(paper)),
                SourceType = "content-paper",
                SourceProvenance = paper.SourceProvenance!,
                RightsStatus = "owned",
                CreatedAt = now,
                CreatedBy = SeederAdminId,
            });

            db.AuditEvents.Add(new AuditEvent
            {
                Id = Guid.NewGuid().ToString("N"),
                OccurredAt = now,
                ActorId = SeederAdminId,
                ActorName = SeederAdminId,
                Action = "ContentPaperSeeded",
                ResourceType = "ContentPaper",
                ResourceId = paper.Id,
                Details = $"writing-sample:{sample.Slug}",
            });
            if (autoPublish)
            {
                db.AuditEvents.Add(new AuditEvent
                {
                    Id = Guid.NewGuid().ToString("N"),
                    OccurredAt = now,
                    ActorId = SeederAdminId,
                    ActorName = SeederAdminId,
                    Action = "ContentPaperPublished",
                    ResourceType = "ContentPaper",
                    ResourceId = paper.Id,
                    Details = $"writing-sample:{sample.Slug}",
                });
            }
            created++;
        }

        if (created > 0)
        {
            await db.SaveChangesAsync(ct);
        }
        logger.LogInformation(
            "WritingSampleSeeder ({File}) complete — created {Created} (autoPublish={AutoPublish}), skipped {Skipped} (total {Total}).",
            Path.GetFileName(seedFile), created, autoPublish, skipped, doc.Samples.Count);
        return created;
    }

    /// <summary>Resolves the ordered list of seed file paths from configuration.
    /// If <see cref="WritingSeedOptions.SeedFilePathsCsv"/> is set, those paths
    /// are used (in order). Otherwise falls back to the single
    /// <see cref="WritingSeedOptions.SeedFilePath"/> override, or the default
    /// <c>Data/Seeds/writing-samples.v1.json</c>.</summary>
    private IReadOnlyList<string> ResolveSeedFiles()
    {
        var csv = options.Value.SeedFilePathsCsv;
        if (!string.IsNullOrWhiteSpace(csv))
        {
            return csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(MapToAbsolutePath)
                .ToList();
        }

        var explicitPath = options.Value.SeedFilePath;
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            return new[] { MapToAbsolutePath(explicitPath) };
        }

        return new[] { Path.Combine(env.ContentRootPath, "Data", "Seeds", "writing-samples.v1.json") };
    }

    private string MapToAbsolutePath(string raw)
        => Path.IsPathRooted(raw) ? raw : Path.Combine(env.ContentRootPath, raw);

    private bool ResolveAutoPublish(string seedFile)
    {
        var fileName = Path.GetFileName(seedFile);
        if (options.Value.AutoPublishByFile is { Count: > 0 } map
            && map.TryGetValue(fileName, out var perFile))
        {
            return perFile;
        }
        return options.Value.AutoPublish;
    }

    private static string BuildDefaultTaskPrompt(WritingSeedSample sample)
    {
        var typeLabel = sample.LetterType switch
        {
            "routine_referral" => "routine referral letter",
            "urgent_referral" => "urgent referral letter",
            "non_medical_referral" => "referral letter to a non-medical professional",
            "update_discharge" => "update and discharge letter to the GP",
            "update_referral_specialist_to_gp" => "update and referral letter (specialist to GP)",
            "transfer_letter" => "transfer letter",
            _ => "letter",
        };
        return $"Using the case notes provided, write a {typeLabel}. " +
               "Expand relevant case notes into complete sentences. Do not use note form. " +
               "Address the letter as indicated. The body of your letter should be approximately 180–200 words.";
    }

    // Local DTOs matching writing-samples.v*.json shape.
    private sealed class WritingSeedFile
    {
        public int SchemaVersion { get; set; }
        public string? SourceAuthority { get; set; }
        public List<WritingSeedSample>? Samples { get; set; }
    }

    private sealed class WritingSeedSample
    {
        public string? SeedId { get; set; }
        public string? Slug { get; set; }
        public string? Title { get; set; }
        public string? Profession { get; set; }
        public string? LetterType { get; set; }
        public string? SourceFolder { get; set; }
        public string? CaseNotesPdf { get; set; }
        public string? ModelAnswerPdf { get; set; }
        public string? CaseNotesText { get; set; }
        public string? ModelAnswerText { get; set; }
    }
}
