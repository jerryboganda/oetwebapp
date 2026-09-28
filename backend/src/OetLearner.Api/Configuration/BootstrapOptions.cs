namespace OetLearner.Api.Configuration;

public sealed class BootstrapOptions
{
    public const string SectionName = "Bootstrap";

    public bool? AutoMigrate { get; set; }
    public bool? SeedDemoData { get; set; }

    /// <summary>
    /// When true, skip EnsureCreated/Migrate/pending-migration checks and
    /// reference-data seeding. Use only when pointing a local process at an
    /// already-initialized database that must not be mutated at startup.
    /// </summary>
    public bool SkipSchemaChanges { get; set; }
}
