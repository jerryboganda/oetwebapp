namespace OetLearner.Api.Configuration;

/// <summary>
/// Toggles for the one-shot Writing sample seeder
/// (<c>WritingSampleSeeder</c>). Defaults are safe — seeding is OFF unless
/// <see cref="Enabled"/> is set to <c>true</c> via configuration.
/// </summary>
public sealed class WritingSeedOptions
{
    public const string SectionName = "Content:WritingSeed";

    /// <summary>When true, the seeder runs once at startup and creates the
    /// canonical OET Writing sample papers if they are missing. Existing rows
    /// (matched by <c>SourceProvenance</c> seed id) are never overwritten.</summary>
    public bool Enabled { get; set; }

    /// <summary>Optional override for the seed JSON path. Relative paths are
    /// resolved against <c>IHostEnvironment.ContentRootPath</c>. When empty
    /// AND <see cref="SeedFilePathsCsv"/> is also empty, the default
    /// <c>Data/Seeds/writing-samples.v1.json</c> is used.</summary>
    public string? SeedFilePath { get; set; }

    /// <summary>Optional comma-separated list of seed JSON paths. When set,
    /// each file is loaded in order. Used to layer additional seed batches
    /// (e.g. v2 multi-profession stubs) on top of the canonical v1 file
    /// without rewriting it.</summary>
    public string? SeedFilePathsCsv { get; set; }

    /// <summary>When true (default), seeded papers are created with
    /// <c>ContentStatus.Published</c>. When false, they are created as
    /// <c>Draft</c> — useful for v2 stub rows whose bodies have not been
    /// filled in yet. Can be overridden per-file via
    /// <see cref="AutoPublishByFile"/>.</summary>
    public bool AutoPublish { get; set; } = true;

    /// <summary>Optional per-file overrides keyed by filename
    /// (case-insensitive match on <c>Path.GetFileName</c>). When a file's
    /// name is present here, the value wins over <see cref="AutoPublish"/>.
    /// Allows v2 stubs to default to Draft while v1 stays Published.</summary>
    public Dictionary<string, bool> AutoPublishByFile { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
