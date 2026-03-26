namespace OetLearner.Api.Configuration;

public sealed class AuthOptions
{
    public bool UseDevelopmentAuth { get; set; }
    public string? Authority { get; set; }
    public string? Audience { get; set; }
    public string? Issuer { get; set; }
    public string? SigningKey { get; set; }
    public bool RequireHttpsMetadata { get; set; } = true;
}
