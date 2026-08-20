namespace OetLearner.Api.Configuration;

/// <summary>
/// Whop (primary Global / Outside Egypt) configuration. Inactive until
/// <see cref="ApiKey"/> is set. Secrets must stay server-side.
/// </summary>
public sealed class WhopOptions
{
    public string ApiBaseUrl { get; set; } = "https://api.whop.com/api/v1";

    /// <summary>Company API key (Admin permissions). Authorization: Bearer.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Optional company id for inline checkout plans (biz_...).</summary>
    public string? CompanyId { get; set; }

    /// <summary>Optional webhook signing secret. When unset, payments are confirmed via the Whop API.</summary>
    public string? WebhookSecret { get; set; }

    public string? SuccessUrl { get; set; }
    public string? CancelUrl { get; set; }
}
