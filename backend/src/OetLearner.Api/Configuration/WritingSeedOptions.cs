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
    /// canonical OET Writing 1-6 sample papers in <c>Draft</c> status if they
    /// are missing. Existing rows (matched by <c>SourceProvenance</c> seed id)
    /// are never overwritten.</summary>
    public bool Enabled { get; set; }

    /// <summary>Optional override for the seed JSON path. Relative paths are
    /// resolved against <c>IHostEnvironment.ContentRootPath</c>. When empty,
    /// the default <c>Data/Seeds/writing-samples.v1.json</c> is used.</summary>
    public string? SeedFilePath { get; set; }
}
