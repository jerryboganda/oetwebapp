namespace OetLearner.Api.Configuration;

public sealed class PlatformOptions
{
    public string? PublicApiBaseUrl { get; set; }
    public string? PublicWebBaseUrl { get; set; }
    public string FallbackEmailDomain { get; set; } = "example.invalid";
}
