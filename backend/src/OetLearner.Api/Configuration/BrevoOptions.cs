namespace OetLearner.Api.Configuration;

public sealed class BrevoOptions
{
    public const string SectionName = "Brevo";

    public bool Enabled { get; set; }
    public string ApiKey { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://api.brevo.com/v3";
    public string FromEmail { get; set; } = string.Empty;
    public string FromName { get; set; } = "OET Learner";
    public string? WebhookSecret { get; set; }
    public int? EmailVerificationTemplateId { get; set; }
    public int? PasswordResetTemplateId { get; set; }
    public int? WelcomeTemplateId { get; set; }
    public int? PasswordChangedTemplateId { get; set; }
    public int? MfaEnabledTemplateId { get; set; }
    public int? AdminInviteTemplateId { get; set; }
    public int? SecurityAlertTemplateId { get; set; }
    public int? ReviewCompletedTemplateId { get; set; }
}