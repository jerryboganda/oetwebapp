using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Mail;
using OetLearner.Api.Configuration;

namespace OetLearner.Api.Services;

public sealed record EmailMessage(
    string To,
    string Subject,
    string TextBody,
    string? HtmlBody = null,
    string? TemplateKey = null,
    IReadOnlyDictionary<string, object?>? TemplateParameters = null);

public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}

public sealed class SmtpEmailSender(
    IOptions<SmtpOptions> options,
    IWebHostEnvironment environment,
    ILogger<SmtpEmailSender> logger) : IEmailSender
{
    private readonly SmtpOptions _options = options.Value;

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            if (environment.IsDevelopment())
            {
                logger.LogInformation(
                    "SMTP disabled for development. To={To} Subject={Subject} Body={Body}",
                    message.To,
                    message.Subject,
                    message.TextBody);
                return;
            }

            throw new InvalidOperationException("SMTP is disabled and the application is not running in Development.");
        }

        if (string.IsNullOrWhiteSpace(_options.Host))
        {
            throw new InvalidOperationException("SMTP:Host must be configured when SMTP is enabled.");
        }

        if (string.IsNullOrWhiteSpace(_options.FromEmail))
        {
            throw new InvalidOperationException("SMTP:FromEmail must be configured when SMTP is enabled.");
        }

        using var smtpClient = new SmtpClient(_options.Host, _options.Port)
        {
            EnableSsl = _options.EnableSsl
        };

        if (!string.IsNullOrWhiteSpace(_options.Username))
        {
            smtpClient.Credentials = new NetworkCredential(_options.Username, _options.Password ?? string.Empty);
        }

        using var mailMessage = new MailMessage
        {
            From = new MailAddress(_options.FromEmail, _options.FromName),
            Subject = message.Subject,
            Body = message.TextBody,
            IsBodyHtml = false
        };
        mailMessage.To.Add(new MailAddress(message.To));

        if (!string.IsNullOrWhiteSpace(message.HtmlBody))
        {
            mailMessage.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(message.HtmlBody, null, "text/html"));
            mailMessage.IsBodyHtml = true;
        }

        await smtpClient.SendMailAsync(mailMessage);
    }
}
