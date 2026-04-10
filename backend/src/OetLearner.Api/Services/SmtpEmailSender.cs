using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Net;
using System.Net.Mail;
using System.Text;
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

        logger.LogInformation(
            "SMTP sending email: To={To} Subject={Subject} Host={Host}:{Port} From={From} SSL={Ssl}",
            message.To, message.Subject, _options.Host, _options.Port, _options.FromEmail, _options.EnableSsl);

        var sw = Stopwatch.StartNew();

        using var smtpClient = new SmtpClient(_options.Host, _options.Port)
        {
            EnableSsl = _options.EnableSsl,
            Timeout = 30_000
        };

        if (!string.IsNullOrWhiteSpace(_options.Username))
        {
            smtpClient.Credentials = new NetworkCredential(_options.Username, _options.Password ?? string.Empty);
        }

        using var mailMessage = new MailMessage
        {
            From = new MailAddress(_options.FromEmail, _options.FromName),
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
