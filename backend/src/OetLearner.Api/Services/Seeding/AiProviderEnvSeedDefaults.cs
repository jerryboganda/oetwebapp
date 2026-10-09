namespace OetLearner.Api.Services.Seeding;

/// <summary>
/// How one provider row is created from the environment at first boot.
///
/// <para>
/// This table exists because the bootstrapper used to hardcode a single vendor's identity into
/// every row it created: <c>Name = "DigitalOcean Serverless Inference (GLM-5)"</c> and prices of
/// 0.015 / 0.075 per 1K. Those numbers are $15 / $75 per 1M — <b>100x too high</b> for every
/// current Z.AI model — so a cost screen built on them does not merely lack precision, it reads as
/// an order of magnitude too expensive and would mislead any judgement about spend.
/// </para>
///
/// <para>
/// Everything a row needs to be recognisable and correctly costed is therefore declared per
/// provider code, here, where a reviewer can see it: the display name, the default base URL and
/// model, and a rate resolver.
/// </para>
/// </summary>
public readonly record struct ProviderEnvSeed(
    string Code,
    string DisplayName,
    string DefaultBaseUrl,
    string DefaultModel,
    int FailoverPriority,
    Func<string?, (decimal PromptPer1k, decimal CompletionPer1k)> RateResolver);

public static class AiProviderEnvSeedDefaults
{
    /// <summary>The original DO Serverless row. Kept exactly as it was seeded so the boot path
    /// for an existing deployment produces an identical row.</summary>
    public const string DigitalOceanCode = "digitalocean-serverless";

    private static readonly Dictionary<string, ProviderEnvSeed> Seeds = new(StringComparer.OrdinalIgnoreCase)
    {
        [DigitalOceanCode] = new(
            DigitalOceanCode,
            "DigitalOcean Serverless Inference (GLM-5)",
            "https://inference.do-ai.run/v1",
            "glm-5",
            100,
            // Unchanged historical values. DO's published rate card is not part of this change;
            // an admin can correct the row from /admin/ai-providers at any time.
            _ => (0.015m, 0.075m)),

        [ZaiProviderDefaults.ProviderCode] = new(
            ZaiProviderDefaults.ProviderCode,
            ZaiProviderDefaults.ProviderName,
            ZaiProviderDefaults.BaseUrl,
            ZaiProviderDefaults.DefaultModel,
            ZaiProviderDefaults.FailoverPriority,
            ZaiProviderDefaults.RatesFor),
    };

    public static bool IsKnown(string? code)
        => !string.IsNullOrWhiteSpace(code) && Seeds.ContainsKey(code.Trim().ToLowerInvariant());

    /// <summary>Seed metadata for <paramref name="code"/>, or <see langword="null"/> for a code we
    /// have never heard of — in which case the caller must not invent a name or a price.</summary>
    public static ProviderEnvSeed? For(string? code)
        => !string.IsNullOrWhiteSpace(code) && Seeds.TryGetValue(code.Trim().ToLowerInvariant(), out var seed)
            ? seed
            : null;

    public static IReadOnlyCollection<string> KnownCodes => Seeds.Keys;
}