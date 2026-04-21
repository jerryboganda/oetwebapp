namespace OetLearner.Api.Configuration;

/// <summary>
/// Settings for the OpenAI-compatible AI provider used by the grounded AI
/// gateway. Production defaults target DigitalOcean Serverless Inference
/// with Anthropic Claude Opus 4.7 and high reasoning effort.
///
/// Deployment environment variables (prefix <c>AI__</c>):
///   AI__BaseUrl=https://inference.do-ai.run/v1
///   AI__ApiKey=sk-do-...
///   AI__ProviderId=digitalocean-serverless
///   AI__DefaultModel=anthropic-claude-opus-4.7
///   AI__ReasoningEffort=high      // low | medium | high
///   AI__DefaultMaxTokens=4096     // Opus 4.7 caps at 8192
///   AI__DefaultTemperature=0.2
///
/// The grounded gateway always wraps prompts with the rulebook + canonical
/// scoring system before they reach this provider.
/// </summary>
public sealed class AiProviderOptions
{
    public const string SectionName = "AI";

    public string ProviderId { get; set; } = "digitalocean-serverless";
    public string BaseUrl { get; set; } = "https://inference.do-ai.run/v1";
    public string ApiKey { get; set; } = "";
    public string DefaultModel { get; set; } = "anthropic-claude-opus-4.7";

    /// <summary>
    /// Reasoning effort hint for Anthropic Claude / OpenAI o-series style
    /// reasoning models. Passed as <c>reasoning_effort</c> in the chat body.
    /// Values accepted by the API today: "low", "medium", "high".
    /// </summary>
    public string ReasoningEffort { get; set; } = "high";

    public int DefaultMaxTokens { get; set; } = 4096;
    public double DefaultTemperature { get; set; } = 0.2;
}

