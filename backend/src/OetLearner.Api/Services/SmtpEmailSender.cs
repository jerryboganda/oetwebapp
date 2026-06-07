using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using System.Diagnostics;
using OetLearner.Api.Configuration;
using OetLearner.Api.Services.Settings;

namespace OetLearner.Api.Services;

public sealed record EmailAttachment(
    string FileName,
    string ContentType,
    byte[] Content);

public sealed record EmailMessage(
    string To,
    string Subject,
    string TextBody,
    string? HtmlBody = null,
    string? TemplateKey = null,
    IReadOnlyDictionary<string, object?>? TemplateParameters = null,
    IReadOnlyCollection<EmailAttachment>? Attachments = null);

public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}

public sealed class SmtpEmailSender(
    IOptions<SmtpOptions> options,
    IRuntimeSettingsProvider runtimeSettings,
    IWebHostEnvironment environment,
    ILogger<SmtpEmailSender> logger) : IEmailSender
{
    private readonly IOptions<SmtpOptions> _options = options;

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        var optionsSnapshot = _options.Value;
        if (!optionsSnapshot.Enabled)
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

        var emailSettings = (await runtimeSettings.GetAsync(cancellationToken)).Email;

        if (string.IsNullOrWhiteSpace(emailSettings.SmtpHost))
        {
            throw new InvalidOperationException("SMTP:Host must be configured when SMTP is enabled.");
        }

        if (string.IsNullOrWhiteSpace(emailSettings.SmtpFromAddress))
        {
            throw new InvalidOperationException("SMTP:FromEmail must be configured when SMTP is enabled.");
        }

        var port = emailSettings.SmtpPort ?? 587;

        logger.LogInformation(
            "SMTP sending email: To={To} Subject={Subject} Host={Host}:{Port} From={From} SSL={Ssl}",
            message.To, message.Subject, emailSettings.SmtpHost, emailSettings.SmtpPort, emailSettings.SmtpFromAddress, optionsSnapshot.EnableSsl);

        var sw = Stopwatch.StartNew();

        // MimeKit owns UTF-8 encoding by default; the BodyBuilder assembles a
        // multipart/alternative (text + html) with any attachments.
        using var mimeMessage = new MimeMessage();
        mimeMessage.From.Add(new MailboxAddress(emailSettings.SmtpFromName ?? string.Empty, emailSettings.SmtpFromAddress));
        mimeMessage.To.Add(MailboxAddress.Parse(message.To));
        mimeMessage.Subject = message.Subject ?? string.Empty;

        var bodyBuilder = new BodyBuilder { TextBody = message.TextBody };
        if (!string.IsNullOrWhiteSpace(message.HtmlBody))
        {
            bodyBuilder.HtmlBody = message.HtmlBody;
        }

        if (message.Attachments is { Count: > 0 })
        {
            foreach (var attachment in message.Attachments)
            {
                bodyBuilder.Attachments.Add(
                    attachment.FileName,
                    attachment.Content,
                    ContentType.Parse(attachment.ContentType));
            }
        }

        mimeMessage.Body = bodyBuilder.ToMessageBody();

        // Brevo's relay listens on 587 (STARTTLS). Use implicit TLS only on the
        // classic SMTPS port 465; otherwise upgrade in-band via STARTTLS. Unlike
        // System.Net.Mail.SmtpClient, MailKit implements SASL LOGIN/PLAIN in
        // managed code, so it actually authenticates against Brevo on Linux.
        var secureSocketOptions = optionsSnapshot.EnableSsl
            ? (port == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls)
            : SecureSocketOptions.StartTlsWhenAvailable;

        using var smtpClient = new SmtpClient { Timeout = 30_000 };

        try
        {
            await smtpClient.ConnectAsync(emailSettings.SmtpHost, port, secureSocketOptions, cancellationToken);

            if (!string.IsNullOrWhiteSpace(emailSettings.SmtpUsername))
            {
                await smtpClient.AuthenticateAsync(
                    emailSettings.SmtpUsername,
                    emailSettings.SmtpPassword ?? string.Empty,
                    cancellationToken);
            }

            await smtpClient.SendAsync(mimeMessage, cancellationToken);
            await smtpClient.DisconnectAsync(quit: true, cancellationToken);

            sw.Stop();
            logger.LogInformation(
                "SMTP email sent successfully: To={To} Subject={Subject} ElapsedMs={ElapsedMs}",
                message.To, message.Subject, sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            logger.LogError(ex,
                "SMTP send failed: To={To} Subject={Subject} Error={Error} ElapsedMs={ElapsedMs}",
                message.To, message.Subject, ex.Message, sw.ElapsedMilliseconds);
            throw;
        }
    }
}
