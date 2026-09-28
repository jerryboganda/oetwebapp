namespace OetLearner.Api.Services;

public sealed class NotificationRule
{
    public Guid Id { get; set; }
    public string EventKey { get; set; } = default!;
    public string? AudienceRole { get; set; }
    public string Channels { get; set; } = "InApp,Email,Push";
    public int Priority { get; set; } = 5;
    public int? DelaySeconds { get; set; }
    public int? ExpiryMinutes { get; set; }
    public string? FallbackChannels { get; set; }
    public string? RequiredConsentCategory { get; set; }
    public bool BypassQuietHours { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
