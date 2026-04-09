using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;

namespace OetLearner.Api.Services;

public static class EmailTemplateKeys
{
    public const string EmailVerificationOtp = "email_verification_otp";
    public const string PasswordResetOtp = "password_reset_otp";
    public const string Welcome = "welcome";
    public const string PasswordChanged = "password_changed";
    public const string MfaEnabled = "mfa_enabled";
    public const string AdminInvite = "admin_invite";
    public const string SecurityAlert = "security_alert";
    public const string ReviewCompleted = "review_completed";
}

public sealed class BrevoEmailSender(
    HttpClient httpClient,
    IOptions<BrevoOptions> options,
    IWebHostEnvironment environment,
    ILogger<BrevoEmailSender> logger) : IEmailSender
{
    private readonly BrevoOptions _options = options.Value;

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            if (environment.IsDevelopment())
            {
                logger.LogInformation(
                    "Brevo disabled for development. To={To} Subject={Subject} Body={Body}",
                    message.To,
                    message.Subject,
                    message.TextBody);
                return;
            }

            throw new InvalidOperationException("Brevo is disabled and the application is not running in Development.");
        }

        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new InvalidOperationException("Brevo:ApiKey must be configured when Brevo is enabled.");
        }

        if (string.IsNullOrWhiteSpace(_options.FromEmail))
        {
            throw new InvalidOperationException("Brevo:FromEmail must be configured when Brevo is enabled.");
        }

        var request = new BrevoSendRequest
        {
            Sender = new BrevoSender(_options.FromEmail, _options.FromName),
            To = [new BrevoRecipient(message.To)],
            Subject = string.IsNullOrWhiteSpace(message.Subject) ? "OET Learner" : message.Subject
        };

        if (message.TemplateKey is not null)
        {
            request.TemplateId = ResolveTemplateId(message.TemplateKey);
            request.Params = message.TemplateParameters?.ToDictionary(pair => pair.Key, pair => pair.Value) ?? new Dictionary<string, object?>();
        }
        else
        {
            request.TextContent = message.TextBody;
            request.HtmlContent = message.HtmlBody;
        }

        using var response = await httpClient.PostAsJsonAsync("/smtp/email", request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            logger.LogError("Brevo send failed. Status={StatusCode} Response={Response}", (int)response.StatusCode, payload);
            throw new InvalidOperationException($"Brevo email send failed with status {(int)response.StatusCode}.");
        }
    }

    private int ResolveTemplateId(string templateKey)
        => templateKey switch
        {
            EmailTemplateKeys.EmailVerificationOtp => _options.EmailVerificationTemplateId ?? throw new InvalidOperationException("Brevo:EmailVerificationTemplateId must be configured."),
            EmailTemplateKeys.PasswordResetOtp => _options.PasswordResetTemplateId ?? throw new InvalidOperationException("Brevo:PasswordResetTemplateId must be configured."),
            EmailTemplateKeys.Welcome => _options.WelcomeTemplateId ?? throw new InvalidOperationException("Brevo:WelcomeTemplateId must be configured."),
            EmailTemplateKeys.PasswordChanged => _options.PasswordChangedTemplateId ?? throw new InvalidOperationException("Brevo:PasswordChangedTemplateId must be configured."),
            EmailTemplateKeys.MfaEnabled => _options.MfaEnabledTemplateId ?? throw new InvalidOperationException("Brevo:MfaEnabledTemplateId must be configured."),
            EmailTemplateKeys.AdminInvite => _options.AdminInviteTemplateId ?? throw new InvalidOperationException("Brevo:AdminInviteTemplateId must be configured."),
            EmailTemplateKeys.SecurityAlert => _options.SecurityAlertTemplateId ?? throw new InvalidOperationException("Brevo:SecurityAlertTemplateId must be configured."),
            EmailTemplateKeys.ReviewCompleted => _options.ReviewCompletedTemplateId ?? throw new InvalidOperationException("Brevo:ReviewCompletedTemplateId must be configured."),
            _ => throw new InvalidOperationException($"Unsupported Brevo template key '{templateKey}'.")
        };

    private sealed record BrevoSendRequest
    {
        public BrevoSender Sender { get; init; } = default!;
        public IReadOnlyCollection<BrevoRecipient> To { get; init; } = [];
        public string Subject { get; init; } = string.Empty;
        public string? TextContent { get; set; }
        public string? HtmlContent { get; set; }
        public int? TemplateId { get; set; }
        public Dictionary<string, object?>? Params { get; set; }
    }

    private sealed record BrevoSender(string Email, string Name);

    private sealed record BrevoRecipient(string Email);
}