using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Net;
using System.Net.Mail;
using System.Text;
using OetLearner.Api.Services.Settings;

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

public sealed class RuntimeEmailSender(
    IRuntimeSettingsProvider runtimeSettings,
    BrevoEmailSender brevoEmailSender,
    SmtpEmailSender smtpEmailSender,
    IWebHostEnvironment environment,
    ILogger<RuntimeEmailSender> logger) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        var emailSettings = (await runtimeSettings.GetAsync(cancellationToken)).Email;
        if (emailSettings.BrevoEnabled)
        {
            await brevoEmailSender.SendAsync(message, cancellationToken);
            return;
        }

        if (emailSettings.SmtpEnabled || environment.IsDevelopment())
        {
            await smtpEmailSender.SendAsync(message, cancellationToken);
            return;
        }

        logger.LogError("No transactional email provider is enabled.");
        throw new InvalidOperationException("No transactional email provider is enabled.");
    }
}

public sealed class SmtpEmailSender(
    IRuntimeSettingsProvider runtimeSettings,
    IWebHostEnvironment environment,
    ILogger<SmtpEmailSender> logger) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        var emailSettings = (await runtimeSettings.GetAsync(cancellationToken)).Email;

        if (!emailSettings.SmtpEnabled)
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

        if (string.IsNullOrWhiteSpace(emailSettings.SmtpHost))
        {
            throw new InvalidOperationException("SMTP:Host must be configured when SMTP is enabled.");
        }

        if (string.IsNullOrWhiteSpace(emailSettings.SmtpFromAddress))
        {
            throw new InvalidOperationException("SMTP:FromEmail must be configured when SMTP is enabled.");
        }

        logger.LogInformation(
            "SMTP sending email: To={To} Subject={Subject} Host={Host}:{Port} From={From} SSL={Ssl}",
            message.To, message.Subject, emailSettings.SmtpHost, emailSettings.SmtpPort, emailSettings.SmtpFromAddress, emailSettings.SmtpEnableSsl);

        var sw = Stopwatch.StartNew();

        using var smtpClient = new SmtpClient(emailSettings.SmtpHost, emailSettings.SmtpPort ?? 587)
        {
            EnableSsl = emailSettings.SmtpEnableSsl,
            Timeout = 30_000
        };

        if (!string.IsNullOrWhiteSpace(emailSettings.SmtpUsername))
        {
            smtpClient.Credentials = new NetworkCredential(emailSettings.SmtpUsername, emailSettings.SmtpPassword ?? string.Empty);
        }

        using var mailMessage = new MailMessage
        {
            From = new MailAddress(emailSettings.SmtpFromAddress, emailSettings.SmtpFromName),
            Subject = message.Subject,
            SubjectEncoding = Encoding.UTF8,
            BodyEncoding = Encoding.UTF8
        };
        mailMessage.To.Add(new MailAddress(message.To));

        if (!string.IsNullOrWhiteSpace(message.HtmlBody))
        {
            // Multipart: text/plain body + text/html alternate view
            mailMessage.Body = message.TextBody;
            mailMessage.IsBodyHtml = false;
            mailMessage.AlternateViews.Add(
                AlternateView.CreateAlternateViewFromString(message.HtmlBody, Encoding.UTF8, "text/html"));
        }
        else
        {
            mailMessage.Body = message.TextBody;
            mailMessage.IsBodyHtml = false;
        }

        try
        {
            await smtpClient.SendMailAsync(mailMessage, cancellationToken);
            sw.Stop();
            logger.LogInformation(
                "SMTP email sent successfully: To={To} Subject={Subject} ElapsedMs={ElapsedMs}",
                message.To, message.Subject, sw.ElapsedMilliseconds);
        }
        catch (SmtpException ex)
        {
            sw.Stop();
            logger.LogError(ex,
                "SMTP send failed: To={To} Subject={Subject} StatusCode={StatusCode} ElapsedMs={ElapsedMs}",
                message.To, message.Subject, ex.StatusCode, sw.ElapsedMilliseconds);
            throw;
        }
        catch (Exception ex)
        {
            sw.Stop();
            logger.LogError(ex,
                "SMTP send error: To={To} Subject={Subject} Error={Error} ElapsedMs={ElapsedMs}",
                message.To, message.Subject, ex.Message, sw.ElapsedMilliseconds);
            throw;
        }
    }
}
