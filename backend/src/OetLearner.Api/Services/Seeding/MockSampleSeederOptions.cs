namespace OetLearner.Api.Services.Seeding;

/// <summary>
/// Toggles + source-folder for <see cref="MockSampleSeeder"/>. Seeder is
/// disabled by default and only runs in Development (Program.cs gate); the
/// <see cref="Enabled"/> flag exists so a Dev environment can opt out
/// without removing the registration.
/// </summary>
public sealed class MockSampleSeederOptions
{
    public const string SectionName = "MockSampleSeeder";

    /// <summary>If false, the seeder no-ops (returns immediately). Default
    /// false so non-Development environments are unaffected even if the
    /// section is accidentally bound.</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>Path to the repo's <c>Project Real Content/</c> folder.
    /// Relative paths resolve against <c>IHostEnvironment.ContentRootPath</c>.
    /// When empty, the seeder probes a list of likely candidates relative
    /// to the content root and the current working directory.</summary>
    public string SourceRootPath { get; set; } = string.Empty;
}
