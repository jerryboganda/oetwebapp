using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Listening;

// ═════════════════════════════════════════════════════════════════════════════
// ListeningStarterContentSeeder — Wave 5b of the OET Listening gap-fill plan.
//
// Idempotently materialises hand-authored 42-question fixtures from
// `Data/SeedData/listening/*.json` into the database. Each fixture upserts a
// Draft ContentPaper + the authored question map + extract metadata. The
// resulting paper is available for admin review and (if approved) backfill
// into the relational ListeningPart / Extract / Question / Option tables.
//
// Idempotency key: ContentPaper.Slug from each fixture's `slug` field. Re-runs
// of an unchanged fixture are no-ops; updating the JSON re-applies the
// structure via IListeningAuthoringService.ReplaceStructureAsync (which bumps
// per-question Version, so in-flight attempts re-pin transparently).
//
// Enable via:   Seed:ListeningStarter:Enabled = true
// Skip on CI / production:   leave Enabled = false (the default).
// ═════════════════════════════════════════════════════════════════════════════

public interface IListeningStarterContentSeeder
{
    Task<int> SeedAsync(CancellationToken ct);
}

public sealed class ListeningStarterContentSeederOptions
{
    public const string SectionName = "Seed:ListeningStarter";

    /// <summary>If false, seeder no-ops. Default false so CI and production
    /// containers without the dev seed assets are unaffected.</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>Override the JSON fixture directory. Empty → use the default
    /// at <c>backend/src/OetLearner.Api/Data/SeedData/listening</c> relative to
    /// the content root.</summary>
    public string FixtureRoot { get; set; } = string.Empty;
}

public sealed class ListeningStarterContentSeeder(
    LearnerDbContext db,
    IListeningAuthoringService authoring,
    IHostEnvironment env,
    IOptions<ListeningStarterContentSeederOptions> opts,
    ILogger<ListeningStarterContentSeeder> logger) : IListeningStarterContentSeeder
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public async Task<int> SeedAsync(CancellationToken ct)
    {
        var options = opts.Value;
        if (!options.Enabled)
        {
            logger.LogInformation(
                "ListeningStarterContentSeeder disabled (Seed:ListeningStarter:Enabled=false); skipping.");
            return 0;
        }

        var root = ResolveFixtureRoot(options.FixtureRoot);
        if (root is null || !Directory.Exists(root))
        {
            logger.LogInformation("ListeningStarterContentSeeder: no fixture root found; skipping.");
            return 0;
        }

        var fixtures = Directory.EnumerateFiles(root, "*.json", SearchOption.TopDirectoryOnly)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (fixtures.Count == 0) return 0;

        var seeded = 0;
        foreach (var path in fixtures)
        {
            try
            {
                if (await SeedOneAsync(path, ct)) seeded++;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "ListeningStarterContentSeeder: failed to ingest {Path}", path);
            }
        }
        logger.LogInformation(
            "ListeningStarterContentSeeder: ingested {Count} fixture(s) from {Root}",
            seeded, root);
        return seeded;
    }

    private async Task<bool> SeedOneAsync(string path, CancellationToken ct)
    {
        await using var fs = File.OpenRead(path);
        var fixture = await JsonSerializer.DeserializeAsync<StarterFixture>(fs, JsonOpts, ct);
        if (fixture is null || string.IsNullOrWhiteSpace(fixture.Slug))
        {
            logger.LogWarning("Fixture {Path} missing slug — skipping", path);
            return false;
        }

        var now = DateTimeOffset.UtcNow;
        var paper = await db.ContentPapers
            .FirstOrDefaultAsync(p => p.Slug == fixture.Slug && p.SubtestCode == "listening", ct);

        if (paper is null)
        {
            paper = new ContentPaper
            {
                Id = $"listening-starter-{fixture.Slug}",
                Slug = fixture.Slug,
                SubtestCode = "listening",
                Title = fixture.Title,
                Difficulty = fixture.Difficulty ?? "standard",
                Status = ContentStatus.Draft,
                AppliesToAllProfessions = fixture.ProfessionScope == "all"
                    || string.IsNullOrWhiteSpace(fixture.ProfessionScope),
                ProfessionId = string.IsNullOrWhiteSpace(fixture.ProfessionId) ? null : fixture.ProfessionId,
                CreatedAt = now,
                UpdatedAt = now,
                ExtractedTextJson = "{}",
            };
            db.ContentPapers.Add(paper);
            await db.SaveChangesAsync(ct);
            logger.LogInformation(
                "Seeded ContentPaper '{Slug}' from {Path}", fixture.Slug, Path.GetFileName(path));
        }

        // Replace structure + extracts atomically. Both ops are idempotent —
        // re-running with unchanged JSON is a no-op aside from version bumps
        // on questions whose normalised shape actually differs.
        if (fixture.Questions is { Count: > 0 })
        {
            await authoring.ReplaceStructureAsync(paper.Id, fixture.Questions, adminId: "system", ct);
        }
        if (fixture.Extracts is { Count: > 0 })
        {
            await authoring.ReplaceExtractsAsync(paper.Id, fixture.Extracts, adminId: "system", ct);
        }
        return true;
    }

    private string? ResolveFixtureRoot(string configured)
    {
        if (!string.IsNullOrWhiteSpace(configured) && Directory.Exists(configured)) return configured;

        // Probe a list of likely candidates so the seeder works whether the
        // process is launched from the repo root, the API project, or under a
        // `dotnet test` runner.
        var candidates = new List<string>
        {
            Path.Combine(env.ContentRootPath, "Data", "SeedData", "listening"),
            Path.Combine(env.ContentRootPath, "..", "OetLearner.Api", "Data", "SeedData", "listening"),
            Path.Combine(AppContext.BaseDirectory, "Data", "SeedData", "listening"),
        };
        foreach (var c in candidates)
        {
            var normalised = Path.GetFullPath(c);
            if (Directory.Exists(normalised)) return normalised;
        }
        return null;
    }

    private sealed record StarterFixture
    {
        public string Slug { get; init; } = string.Empty;
        public string Title { get; init; } = string.Empty;
        public string? Difficulty { get; init; }
        public string? ProfessionScope { get; init; }
        public string? ProfessionId { get; init; }
        public List<ListeningAuthoredExtract>? Extracts { get; init; }
        public List<ListeningAuthoredQuestion>? Questions { get; init; }
    }
}
