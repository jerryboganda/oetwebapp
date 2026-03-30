namespace OetLearner.Api.Configuration;

public sealed class ExternalAuthOptions
{
    public const string SectionName = "ExternalAuth";

    public ExternalAuthProviderOptions Google { get; set; } = new();
    public ExternalAuthProviderOptions Facebook { get; set; } = new();
    public ExternalAuthProviderOptions LinkedIn { get; set; } = new();

    public ExternalAuthProviderOptions GetProvider(string provider)
        => provider.ToLowerInvariant() switch
        {
            ExternalAuthProviders.Google => Google,
            ExternalAuthProviders.Facebook => Facebook,
            ExternalAuthProviders.LinkedIn => LinkedIn,
            _ => throw new InvalidOperationException($"Unsupported external auth provider '{provider}'.")
        };
}

public sealed class ExternalAuthProviderOptions
{
    public bool Enabled { get; set; }
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
}

public static class ExternalAuthProviders
{
    public const string Google = "google";
    public const string Facebook = "facebook";
    public const string LinkedIn = "linkedin";

    public static readonly string[] All = [Google, Facebook, LinkedIn];
}
