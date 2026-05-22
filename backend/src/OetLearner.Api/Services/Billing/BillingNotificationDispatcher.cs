using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;

namespace OetLearner.Api.Services.Billing;

/// <summary>
/// Phase 9 — fan-out of billing domain events to email / SMS / WhatsApp.
/// Channel providers are pluggable; this class handles template lookup,
/// idempotency, and persistence to the dispatch log.
/// </summary>
public interface IBillingNotificationDispatcher
{
    Task DispatchAsync(BillingNotificationEvent evt, CancellationToken ct);
}

public sealed record BillingNotificationEvent(
    string EventCode,
    string EventId,
    string UserId,
    IReadOnlyDictionary<string, string> Variables,
    IReadOnlyList<string>? ForceChannels = null);

public interface IBillingNotificationChannel
{
    string Channel { get; }
    Task SendAsync(string userId, string subject, string body, CancellationToken ct);
}

public sealed class BillingNotificationDispatcher : IBillingNotificationDispatcher
{
    private readonly LearnerDbContext _db;
    private readonly IReadOnlyDictionary<string, IBillingNotificationChannel> _channels;
    private readonly ILogger<BillingNotificationDispatcher> _logger;

    public BillingNotificationDispatcher(LearnerDbContext db, IEnumerable<IBillingNotificationChannel> channels, ILogger<BillingNotificationDispatcher> logger)
    {
        _db = db;
        _channels = channels.ToDictionary(c => c.Channel, StringComparer.OrdinalIgnoreCase);
        _logger = logger;
    }

    public async Task DispatchAsync(BillingNotificationEvent evt, CancellationToken ct)
    {
        var templates = await _db.BillingNotificationTemplates
            .Where(t => t.Code == evt.EventCode && t.IsActive && t.LocaleTag == "en")
            .ToListAsync(ct);

        foreach (var template in templates)
        {
            if (evt.ForceChannels is { Count: > 0 } && !evt.ForceChannels.Contains(template.Channel, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            // Idempotency guard: unique on (UserId, EventCode, EventId, TemplateCode).
            var existing = await _db.BillingNotificationDispatchLogs
                .Where(l => l.UserId == evt.UserId && l.EventCode == evt.EventCode && l.EventId == evt.EventId && l.TemplateCode == template.Code)
                .Select(l => l.Id)
                .FirstOrDefaultAsync(ct);
            if (existing is not null)
            {
                continue;
            }

            var renderedBody = RenderTemplate(template.BodyTemplate, evt.Variables);
            var renderedSubject = template.Subject is null ? null : RenderTemplate(template.Subject, evt.Variables);

            var log = new BillingNotificationDispatchLog
            {
                Id = Guid.NewGuid().ToString("N"),
                UserId = evt.UserId,
                EventCode = evt.EventCode,
                EventId = evt.EventId,
                TemplateCode = template.Code,
                Channel = template.Channel,
                Status = "queued",
                CreatedAt = DateTimeOffset.UtcNow,
            };
            _db.BillingNotificationDispatchLogs.Add(log);
            await _db.SaveChangesAsync(ct);

            try
            {
                if (_channels.TryGetValue(template.Channel, out var channel))
                {
                    await channel.SendAsync(evt.UserId, renderedSubject ?? string.Empty, renderedBody, ct);
                    log.Status = "sent";
                    log.SentAt = DateTimeOffset.UtcNow;
                }
                else
                {
                    log.Status = "failed";
                    log.FailureReason = $"No channel registered for '{template.Channel}'.";
                }
            }
            catch (Exception ex)
            {
                log.Status = "failed";
                log.FailureReason = ex.Message;
                _logger.LogError(ex, "Billing notification channel {Channel} dispatch failed.", template.Channel);
            }

            await _db.SaveChangesAsync(ct);
        }
    }

    private static string RenderTemplate(string template, IReadOnlyDictionary<string, string> vars)
    {
        // Minimal {{var}} replacement. Production should harden against missing vars.
        var output = template;
        foreach (var (k, v) in vars)
        {
            output = output.Replace("{{" + k + "}}", v, StringComparison.OrdinalIgnoreCase);
        }
        return output;
    }
}

/// <summary>
/// Email channel backed by the existing <see cref="IEmailSender"/> pipeline
/// (Brevo / SMTP / dev-logger). Resolves the learner's email from
/// ApplicationUserAccounts and hands off to the same sender that auth uses,
/// so deliverability + rotation + DKIM keys stay consistent.
/// </summary>
public sealed class EmailBillingChannel : IBillingNotificationChannel
{
    public string Channel => "email";
    private readonly LearnerDbContext _db;
    private readonly IEmailSender _emailSender;
    private readonly ILogger<EmailBillingChannel> _logger;

    public EmailBillingChannel(LearnerDbContext db, IEmailSender emailSender, ILogger<EmailBillingChannel> logger)
    {
        _db = db;
        _emailSender = emailSender;
        _logger = logger;
    }

    public async Task SendAsync(string userId, string subject, string body, CancellationToken ct)
    {
        var email = await _db.ApplicationUserAccounts
            .Where(u => u.Id == userId && u.DeletedAt == null)
            .Select(u => u.Email)
            .FirstOrDefaultAsync(ct);
        if (string.IsNullOrWhiteSpace(email))
        {
            _logger.LogWarning("EmailBillingChannel: no email for user {UserId}", userId);
            return;
        }

        await _emailSender.SendAsync(new EmailMessage(
            To: email,
            Subject: subject,
            TextBody: body,
            HtmlBody: body), ct);
    }
}

public sealed class StubSmsBillingChannel : IBillingNotificationChannel
{
    public string Channel => "sms";
    private readonly ILogger<StubSmsBillingChannel> _logger;
    public StubSmsBillingChannel(ILogger<StubSmsBillingChannel> logger) => _logger = logger;
    public Task SendAsync(string userId, string subject, string body, CancellationToken ct)
    {
        _logger.LogInformation("[stub sms] user={UserId}", userId);
        return Task.CompletedTask;
    }
}

public sealed class StubWhatsAppBillingChannel : IBillingNotificationChannel
{
    public string Channel => "whatsapp";
    private readonly ILogger<StubWhatsAppBillingChannel> _logger;
    public StubWhatsAppBillingChannel(ILogger<StubWhatsAppBillingChannel> logger) => _logger = logger;
    public Task SendAsync(string userId, string subject, string body, CancellationToken ct)
    {
        _logger.LogInformation("[stub whatsapp] user={UserId}", userId);
        return Task.CompletedTask;
    }
}
