namespace OetLearner.Api.Configuration;

public sealed class BootstrapOptions
{
    public bool? AutoMigrate { get; set; }
    public bool? SeedDemoData { get; set; }
    public string? ExpertEmail { get; set; }
    public string? ExpertPassword { get; set; }
    public string? ExpertDisplayName { get; set; }
}
