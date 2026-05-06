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
/// One-shot startup seeder that loads the canonical OET Writing 1-6 sample
/// papers from <c>Data/Seeds/writing-samples.v1.json</c> (extracted from the
/// real PDF samples by <c>scripts/extract-writing-pdfs</c>).
///
/// The seeder is idempotent — papers are matched by <c>SourceProvenance</c>
/// seed id and never overwritten once created. Papers are created already
/// <see cref="ContentStatus.Published"/> with the full inline writing
/// structure populated and a corresponding learner-facing <c>ContentItem</c>
/// row, so they appear immediately on the learner Writing surface. Admins
/// can still attach official PDFs after the fact via the canonical CRUD
/// path through <see cref="IContentPaperService"/>; the asset-presence
/// publish gate is bypassed here because seeded papers are inline-text only.
///
/// Disabled by default. Enable per-environment with
/// <c>Content:WritingSeed:Enabled=true</c>. Optional override
/// <c>Content:WritingSeed:SeedFilePath</c> can point at an alternative JSON
/// file (test hook).
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
    /// reaching into reflection. Returns the number of rows created on this
    /// invocation (0 when disabled, file missing, or all rows already present).</summary>
    public async Task<int> SeedAsync(CancellationToken ct)
    {
        if (!options.Value.Enabled)
        {
            logger.LogDebug("WritingSampleSeeder disabled (Content:WritingSeed:Enabled=false).");
            return 0;
        }

        var seedFile = ResolveSeedFile();
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
            logger.LogInformation("WritingSampleSeeder: seed file has no samples.");
            return 0;
        }

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
                TagsCsv = "seed,writing-samples-v1",
                SourceProvenance = sample.SeedId,
                Status = ContentStatus.Published,
                PublishedAt = now,
                CreatedAt = now,
                UpdatedAt = now,
                CreatedByAdminId = SeederAdminId,
                ExtractedTextJson = extractedTextJson,
            };
            paper.PublishedRevisionId ??= paper.Id;
            db.ContentPapers.Add(paper);

            // Project to learner-visible ContentItem so the paper is reachable
            // through the standard learner content surfaces (mirrors
            // ContentPaperService.UpsertWritingContentItemAsync but called
            // inline because we bypass the asset-presence publish gate —
            // seeded papers are inline-text only).
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
                Status = ContentStatus.Published,
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
            created++;
        }

        if (created > 0)
        {
            await db.SaveChangesAsync(ct);
        }
        logger.LogInformation(
            "WritingSampleSeeder complete — created {Created}, skipped {Skipped} (total {Total}).",
            created, skipped, doc.Samples.Count);
        return created;
    }

    private string ResolveSeedFile()
    {
        var explicitPath = options.Value.SeedFilePath;
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            return Path.IsPathRooted(explicitPath)
                ? explicitPath
                : Path.Combine(env.ContentRootPath, explicitPath);
        }
        return Path.Combine(env.ContentRootPath, "Data", "Seeds", "writing-samples.v1.json");
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

    // Local DTOs matching writing-samples.v1.json shape.
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
