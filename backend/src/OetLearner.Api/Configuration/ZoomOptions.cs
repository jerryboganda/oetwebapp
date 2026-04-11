namespace OetLearner.Api.Configuration;

public sealed class ZoomOptions
{
    public const string SectionName = "Zoom";

    /// <summary>Whether Zoom integration is enabled. When false, meetings won't be created automatically.</summary>
    public bool Enabled { get; set; }

    /// <summary>Zoom Server-to-Server OAuth Account ID.</summary>
    public string? AccountId { get; set; }

    /// <summary>Zoom Server-to-Server OAuth Client ID.</summary>
    public string? ClientId { get; set; }

    /// <summary>Zoom Server-to-Server OAuth Client Secret.</summary>
    public string? ClientSecret { get; set; }

    /// <summary>Zoom API base URL.</summary>
    public string ApiBaseUrl { get; set; } = "https://api.zoom.us/v2";

    /// <summary>Zoom OAuth token URL.</summary>
    public string TokenUrl { get; set; } = "https://zoom.us/oauth/token";

    /// <summary>Zoom user email or ID used to create meetings (central platform account).</summary>
    public string? HostUserId { get; set; }

    /// <summary>Webhook verification token for Zoom webhooks.</summary>
    public string? WebhookVerificationToken { get; set; }

    /// <summary>Allow sandbox/mock responses when credentials are not configured.</summary>
    public bool AllowSandboxFallback { get; set; } = true;
}
