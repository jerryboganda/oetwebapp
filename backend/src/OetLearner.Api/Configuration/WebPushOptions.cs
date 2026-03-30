namespace OetLearner.Api.Configuration;

public sealed class WebPushOptions
{
    public const string SectionName = "WebPush";

    public bool Enabled { get; set; }
    public string Subject { get; set; } = "mailto:support@example.invalid";
    public string PublicKey { get; set; } = string.Empty;
    public string PrivateKey { get; set; } = string.Empty;
}
