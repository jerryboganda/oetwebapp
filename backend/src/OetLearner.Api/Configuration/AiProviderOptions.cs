namespace OetLearner.Api.Configuration;

/// <summary>
/// Settings for the OpenAI-compatible AI provider used by the grounded AI
/// gateway. Keep secrets out of source control — populate via environment
/// variables in deployment:
///
///   AI__BaseUrl=https://inference.do-ai.run/v1
///   AI__ApiKey=sk-do-...
///   AI__ProviderId=digitalocean
///   AI__DefaultModel=anthropic-claude-opus-4.7
///
/// The grounded gateway always wraps prompts with the rulebook + canonical
/// scoring system before they reach this provider.
/// </summary>
public sealed class AiProviderOptions
{
    public const string SectionName = "AI";

    public string ProviderId { get; set; } = "digitalocean";
    public string BaseUrl { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public string DefaultModel { get; set; } = "anthropic-claude-opus-4.7";
}
