namespace OetLearner.Api.Configuration;

/// <summary>
/// Toggles for the OET 2026 catalog seeder (<c>Oet2026CatalogSeeder</c>).
/// Defaults are safe — seeding is OFF unless <see cref="Enabled"/> is set
/// to <c>true</c> via configuration.
///
/// <para>
/// The seeder loads the canonical 20 plans + 7 add-ons + matching content
/// packages described in the OET 2026 portfolio PDFs from
/// <c>Data/Seeds/oet-2026-catalog.json</c>. UPSERT is idempotent — matching
/// rows are updated in place, missing rows are inserted, no rows are deleted.
/// </para>
/// </summary>
public sealed class Oet2026CatalogSeedOptions
{
    public const string SectionName = "Content:Oet2026Catalog";

    /// <summary>When true, the seeder runs once at startup and writes (or
    /// updates) the OET 2026 plans, add-ons and content packages. Existing
    /// rows are preserved and their fields refreshed from the manifest.</summary>
    public bool Enabled { get; set; }

    /// <summary>Optional override for the seed JSON path. Relative paths are
    /// resolved against <c>IHostEnvironment.ContentRootPath</c>. When empty
    /// the default <c>Data/Seeds/oet-2026-catalog.json</c> is used.</summary>
    public string? SeedFilePath { get; set; }

    /// <summary>When true (default), the seeder also creates / refreshes a
    /// matching <c>ContentPackage</c> row for every plan and add-on so the
    /// marketplace listing surfaces the SKU. Disable to seed billing rows
    /// only (rare — useful for migrating to a separate marketing store).</summary>
    public bool CreateContentPackages { get; set; } = true;
}
