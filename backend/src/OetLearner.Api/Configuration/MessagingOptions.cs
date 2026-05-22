namespace OetLearner.Api.Configuration;

/// <summary>Twilio SMS configuration. Inactive when AccountSid/AuthToken unset.</summary>
public sealed class TwilioOptions
{
    public bool Enabled { get; set; } = false;
    public string ApiBaseUrl { get; set; } = "https://api.twilio.com";
    public string? AccountSid { get; set; }
    public string? AuthToken { get; set; }
    /// <summary>E.164 phone number SMS is sent from.</summary>
    public string? FromNumber { get; set; }
    /// <summary>Optional Messaging Service SID — when set, takes precedence over FromNumber.</summary>
    public string? MessagingServiceSid { get; set; }
}

/// <summary>Meta WhatsApp Business Cloud API configuration. Inactive when AccessToken unset.</summary>
public sealed class WhatsAppOptions
{
    public bool Enabled { get; set; } = false;
    public string ApiBaseUrl { get; set; } = "https://graph.facebook.com/v20.0";
    public string? AccessToken { get; set; }
    /// <summary>Phone-number id assigned by Meta to the business WhatsApp sender.</summary>
    public string? PhoneNumberId { get; set; }
    /// <summary>Optional fallback template name when freeform isn't allowed (>24h since last user msg).</summary>
    public string? FallbackTemplateName { get; set; }
}
