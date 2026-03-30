namespace OetLearner.Api.Configuration;

public sealed class AuthTokenOptions
{
    public const string SectionName = "AuthTokens";
    public const int MinimumSigningKeyLength = 32;

    public string? Issuer { get; set; }
    public string? Audience { get; set; }
    public string? AccessTokenSigningKey { get; set; }
    public string? RefreshTokenSigningKey { get; set; }
    public TimeSpan AccessTokenLifetime { get; set; }
    public TimeSpan RefreshTokenLifetime { get; set; }
    public TimeSpan OtpLifetime { get; set; }
    public string? AuthenticatorIssuer { get; set; }
}
