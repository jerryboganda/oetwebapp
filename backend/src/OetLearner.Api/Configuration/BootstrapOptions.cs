namespace OetLearner.Api.Configuration;

public sealed class BootstrapOptions
{
    public bool? AutoMigrate { get; set; }
    public bool? SeedDemoData { get; set; }
}
