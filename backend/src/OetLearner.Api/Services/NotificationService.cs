using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Settings;

namespace OetLearner.Api.Services;

public sealed record NotificationFeedQuery(
    int Page = 1,
    int PageSize = 20,
    bool UnreadOnly = false,
    string? Category = null,
    string? Channel = null);

internal sealed record StoredNotificationEventPreference(
    bool? InAppEnabled,
    bool? EmailEnabled,
    bool? PushEnabled,
    string? EmailMode);

internal sealed record NotificationPolicyDecision(
    string Timezone,
    bool QuietHoursEnabled,
    int? QuietHoursStartMinutes,
    int? QuietHoursEndMinutes,
    bool InAppEnabled,
    bool EmailEnabled,
    bool PushEnabled,
    NotificationEmailMode EmailMode,
    int? MaxDeliveriesPerHour,
    int? MaxDeliveriesPerDay,
    DateTimeOffset? DeferredPushUntilUtc,
    string? EmailDisabledReasonCode,
    string? EmailDisabledMessage,
    string? PushDisabledReasonCode,
    string? PushDisabledMessage);

internal sealed record NotificationFrequencyCapCheck(
    bool IsAllowed,
    string? ReasonCode = null,
    string? Message = null);

internal sealed record NotificationChannelCompliance(
    bool IsEnabled,
    string? DisabledReasonCode = null,
    string? DisabledMessage = null);

internal sealed record NotificationDigestJobPayload(
    string AuthAccountId,
    string LocalDateBucket,
    string Timezone);

internal sealed record NotificationFrequencyCapReservationKey(
    string AuthAccountId,
    NotificationChannel Channel,
    string EventKey,
    string Category,
    string Window);

public sealed partial class NotificationService(
    LearnerDbContext db,
    IEmailSender emailSender,
    IWebPushDispatcher webPushDispatcher,
    IMobilePushDispatcher mobilePushDispatcher,
    IHubContext<NotificationHub> hubContext,
    PlatformLinkService platformLinks,
    TimeProvider timeProvider,
    IOptions<WebPushOptions> webPushOptions,
    IRuntimeSettingsProvider runtimeSettingsProvider,
    IOptions<NotificationProofHarnessOptions> notificationProofOptions,
    IWebHostEnvironment environment,
    ILogger<NotificationService> logger)
{
    private const int MaxPageSize = 100;
    private const int MaxFrequencyCapLimit = 10000;
    private static readonly NotificationDeliveryStatus[] FrequencyCapCountedStatuses =
    [
        NotificationDeliveryStatus.Sent,
        NotificationDeliveryStatus.Delivered,
        NotificationDeliveryStatus.Opened,
        NotificationDeliveryStatus.Clicked
    ];
    private static readonly string[] ReviewUpdateEventKeys =
    [
        NotificationCatalog.GetKey(NotificationEventKey.LearnerReviewRequested),
        NotificationCatalog.GetKey(NotificationEventKey.LearnerReviewCompleted),
        NotificationCatalog.GetKey(NotificationEventKey.LearnerEvaluationCompleted),
        NotificationCatalog.GetKey(NotificationEventKey.LearnerEvaluationFailed)
    ];

    public async Task<string?> CreateForLearnerAsync(
        NotificationEventKey eventKey,
        string learnerUserId,
        string entityType,
        string entityId,
        string versionOrDateBucket,
        IReadOnlyDictionary<string, object?> payload,
        CancellationToken ct)
    {
        var learner = await db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(user => user.Id == learnerUserId, ct);
        if (learner is null || string.IsNullOrWhiteSpace(learner.AuthAccountId))
        {
            return null;
        }

        return await CreateForAuthAccountAsync(eventKey, learner.AuthAccountId, ApplicationUserRoles.Learner, entityType, entityId, versionOrDateBucket, payload, enqueueFanoutJob: true, ct);
    }

    public async Task<string?> CreateForExpertAsync(
        NotificationEventKey eventKey,
        string expertUserId,
        string entityType,
        string entityId,
        string versionOrDateBucket,
        IReadOnlyDictionary<string, object?> payload,
        CancellationToken ct)
    {
        var expert = await db.ExpertUsers
            .AsNoTracking()
            .FirstOrDefaultAsync(user => user.Id == expertUserId, ct);
        if (expert is null || string.IsNullOrWhiteSpace(expert.AuthAccountId))
        {
            return null;
        }

        return await CreateForAuthAccountAsync(eventKey, expert.AuthAccountId, ApplicationUserRoles.Expert, entityType, entityId, versionOrDateBucket, payload, enqueueFanoutJob: true, ct);
    }

    public async Task<IReadOnlyList<string>> CreateForAdminsAsync(
        NotificationEventKey eventKey,
        string entityType,
        string entityId,
        string versionOrDateBucket,
        IReadOnlyDictionary<string, object?> payload,
        CancellationToken ct)
    {
        var adminIds = await db.ApplicationUserAccounts
            .AsNoTracking()
            .Where(account => account.Role == ApplicationUserRoles.Admin && account.DeletedAt == null)
            .Select(account => account.Id)
            .ToListAsync(ct);

        var createdIds = new List<string>();
        foreach (var adminId in adminIds)
        {
            var createdId = await CreateForAuthAccountAsync(eventKey, adminId, ApplicationUserRoles.Admin, entityType, entityId, versionOrDateBucket, payload, enqueueFanoutJob: true, ct);
            if (!string.IsNullOrWhiteSpace(createdId))
            {
                createdIds.Add(createdId);
            }
        }

        return createdIds;
    }

    public async Task<string?> CreateForAuthAccountAsync(
        NotificationEventKey eventKey,
        string authAccountId,
        string audienceRole,
        string entityType,
        string entityId,
        string versionOrDateBucket,
        IReadOnlyDictionary<string, object?> payload,
        bool enqueueFanoutJob,
        CancellationToken ct)
    {
        var catalog = NotificationCatalog.Get(eventKey);
        if (!string.Equals(catalog.AudienceRole, audienceRole, StringComparison.OrdinalIgnoreCase))
        {
            throw ApiException.Validation(
                "notification_audience_mismatch",
                $"Event {eventKey} is not valid for audience {audienceRole}.",
                [new ApiFieldError("audienceRole", "invalid", "The notification event does not match the audience role.")]);
        }

        var dedupeKey = NotificationScheduling.BuildDedupeKey(eventKey, authAccountId, entityType, entityId, versionOrDateBucket);
        var existing = await db.NotificationEvents
            .AsNoTracking()
            .FirstOrDefaultAsync(notificationEvent => notificationEvent.DedupeKey == dedupeKey, ct);
        if (existing is not null)
        {
            return existing.Id;
        }

        var normalizedPayload = NormalizePayload(payload);
        var notificationEvent = new NotificationEvent
        {
            Id = $"nev-{Guid.NewGuid():N}",
            RecipientAuthAccountId = authAccountId,
            RecipientRole = audienceRole,
            EventKey = NotificationCatalog.GetKey(eventKey),
            Category = catalog.Category,
            Title = NotificationCatalog.BuildTitle(eventKey, normalizedPayload),
            Body = NotificationCatalog.BuildBody(eventKey, normalizedPayload),
            ActionUrl = NormalizeActionUrl(NotificationCatalog.BuildActionUrl(eventKey, normalizedPayload)),
            Severity = catalog.DefaultSeverity,
            State = AsyncState.Queued,
            EntityType = entityType,
            EntityId = entityId,
            VersionOrDateBucket = versionOrDateBucket,
            DedupeKey = dedupeKey,
            PayloadJson = JsonSupport.Serialize(normalizedPayload),
            CreatedAt = timeProvider.GetUtcNow()
        };

        db.NotificationEvents.Add(notificationEvent);
        if (enqueueFanoutJob)
        {
            db.BackgroundJobs.Add(new BackgroundJobItem
            {
                Id = $"job-{Guid.NewGuid():N}",
                Type = JobType.NotificationFanout,
                State = AsyncState.Queued,
                ResourceId = notificationEvent.Id,
                PayloadJson = JsonSupport.Serialize(new { notificationEventId = notificationEvent.Id }),
                CreatedAt = timeProvider.GetUtcNow(),
                AvailableAt = timeProvider.GetUtcNow().AddSeconds(1),
                LastTransitionAt = timeProvider.GetUtcNow(),
                StatusReasonCode = "queued",
                StatusMessage = "Notification fan-out queued.",
                Retryable = true,
                RetryAfterMs = 2000
            });
        }

        await db.SaveChangesAsync(ct);
        return notificationEvent.Id;
    }

    public async Task<NotificationFeedResponse> GetFeedAsync(string authAccountId, NotificationFeedQuery query, CancellationToken ct)
    {
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, MaxPageSize);

        var baseQuery = db.NotificationInboxItems
            .AsNoTracking()
            .Where(item => item.AuthAccountId == authAccountId);

        var unreadCount = await baseQuery.CountAsync(item => !item.IsRead, ct);
        var items = await GetFeedItemsAsync(baseQuery, ct);

        if (query.UnreadOnly)
        {
            items = items.Where(item => !item.IsRead).ToList();
        }

        if (!string.IsNullOrWhiteSpace(query.Category))
        {
            items = items.Where(item => string.Equals(item.Category, query.Category, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        if (!string.IsNullOrWhiteSpace(query.Channel))
        {
            var normalizedChannel = NormalizeChannel(query.Channel);
            items = items.Where(item =>
            {
                var channels = JsonSupport.Deserialize(item.ChannelsJson, Array.Empty<string>());
                return channels.Any(channel => string.Equals(channel, normalizedChannel, StringComparison.OrdinalIgnoreCase));
            }).ToList();
        }

        var totalCount = items.Count;
        var pagedItems = items
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(MapFeedItem)
            .ToArray();

        return new NotificationFeedResponse(pagedItems, totalCount, unreadCount, page, pageSize);
    }

    private async Task<List<NotificationInboxItem>> GetFeedItemsAsync(
        IQueryable<NotificationInboxItem> query,
        CancellationToken ct)
    {
        if (!db.Database.IsSqlite())
        {
            return await query
                .OrderByDescending(item => item.CreatedAt)
                .Take(500)
                .ToListAsync(ct);
        }

        return (await query.ToListAsync(ct))
            .OrderByDescending(item => item.CreatedAt)
            .Take(500)
            .ToList();
    }

    public async Task MarkReadAsync(string authAccountId, string notificationId, CancellationToken ct)
    {
        var item = await db.NotificationInboxItems
            .FirstOrDefaultAsync(existingItem => existingItem.AuthAccountId == authAccountId && existingItem.Id == notificationId, ct)
            ?? throw ApiException.NotFound("notification_not_found", "Notification not found.");

        if (!item.IsRead)
        {
            item.IsRead = true;
            item.ReadAt = timeProvider.GetUtcNow();
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task MarkAllReadAsync(string authAccountId, CancellationToken ct)
    {
        var unreadItems = await db.NotificationInboxItems
            .Where(item => item.AuthAccountId == authAccountId && !item.IsRead)
            .ToListAsync(ct);

        if (unreadItems.Count == 0)
        {
            return;
        }

        var now = timeProvider.GetUtcNow();
        foreach (var item in unreadItems)
        {
            item.IsRead = true;
            item.ReadAt = now;
        }

        await db.SaveChangesAsync(ct);
    }

    private static readonly HashSet<string> ValidPlatforms = new(StringComparer.OrdinalIgnoreCase) { "android", "ios", "web" };

    // Delivery events we persist even when they do not create a suppression.
    // API "Sent" only means Brevo accepted the request — later blocked/bounce
    // events are the real delivery outcome.
    private static readonly HashSet<string> BrevoTrackedEvents = new(StringComparer.OrdinalIgnoreCase)
    {
        "hard_bounce", "blocked", "spam", "invalid_email", "unsubscribed",
        "soft_bounce", "delivered", "unique_opened", "opened", "click", "error"
    };

    private static readonly HashSet<string> BrevoReputationEvents = new(StringComparer.OrdinalIgnoreCase)
    {
        "hard_bounce", "blocked", "spam", "invalid_email"
    };

    private static Dictionary<string, string?> NormalizePayload(IReadOnlyDictionary<string, object?> payload)
    {
        var normalized = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in payload)
        {
            normalized[key] = NormalizePayloadValue(value);
        }

        return normalized;
    }

    private static string? NormalizePayloadValue(object? value)
        => value switch
        {
            null => null,
            string text => text,
            DateTimeOffset dateTimeOffset => dateTimeOffset.ToString("O", CultureInfo.InvariantCulture),
            DateTime dateTime => dateTime.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            DateOnly dateOnly => dateOnly.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            TimeOnly timeOnly => timeOnly.ToString("HH:mm", CultureInfo.InvariantCulture),
            JsonElement jsonElement => NormalizePayloadValue(jsonElement),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString()
        };

    private static string? NormalizePayloadValue(JsonElement value)
        => value.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            JsonValueKind.String => value.GetString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Number => value.ToString(),
            _ => value.GetRawText()
        };

    private static NotificationFeedItem MapFeedItem(NotificationInboxItem item)
    {
        var channels = JsonSupport.Deserialize(item.ChannelsJson, Array.Empty<string>())
            .Select(NormalizeChannel)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new NotificationFeedItem(
            item.Id,
            item.EventKey,
            item.Category,
            item.Title,
            item.Body,
            NormalizeActionUrl(item.ActionUrl),
            NormalizeSeverity(item.Severity),
            item.IsRead,
            channels,
            item.CreatedAt,
            item.ReadAt);
    }

    private static readonly NotificationChannel[] ConsentManagedChannels =
    [
        NotificationChannel.Sms,
        NotificationChannel.WhatsApp
    ];

    private static string NormalizeChannel(string? channel)
        => channel?.Trim().ToLowerInvariant() switch
        {
            "inapp" or "in_app" or "in-app" => "in_app",
            "email" => "email",
            "push" => "push",
            "sms" or "text" => "sms",
            "whatsapp" or "whats_app" or "whats-app" => "whatsapp",
            null or "" => "in_app",
            var other => other.Replace('-', '_')
        };

    private static string NormalizeChannel(NotificationChannel channel)
        => channel switch
        {
            NotificationChannel.InApp => "in_app",
            NotificationChannel.Email => "email",
            NotificationChannel.Push => "push",
            NotificationChannel.Sms => "sms",
            NotificationChannel.WhatsApp => "whatsapp",
            _ => throw new ArgumentOutOfRangeException(nameof(channel), channel, "Unsupported notification channel.")
        };

    private static NotificationChannel ParseChannelFilter(string value)
        => NormalizeChannel(value) switch
        {
            "in_app" => NotificationChannel.InApp,
            "email" => NotificationChannel.Email,
            "push" => NotificationChannel.Push,
            "sms" => NotificationChannel.Sms,
            "whatsapp" => NotificationChannel.WhatsApp,
            var other => throw ApiException.Validation(
                "invalid_notification_channel",
                $"Unsupported notification channel '{other}'.",
                [new ApiFieldError("channel", "invalid_channel", "Use in_app, email, push, sms, or whatsapp.")])
        };

    private static string NormalizeSeverity(NotificationSeverity severity)
        => severity switch
        {
            NotificationSeverity.Info => "info",
            NotificationSeverity.Success => "success",
            NotificationSeverity.Warning => "warning",
            NotificationSeverity.Critical => "critical",
            _ => throw new ArgumentOutOfRangeException(nameof(severity), severity, "Unsupported notification severity.")
        };

    private static string NormalizeDeliveryStatus(NotificationDeliveryStatus status)
        => status switch
        {
            NotificationDeliveryStatus.Pending => "pending",
            NotificationDeliveryStatus.Sent => "sent",
            NotificationDeliveryStatus.Suppressed => "suppressed",
            NotificationDeliveryStatus.Failed => "failed",
            NotificationDeliveryStatus.Expired => "expired",
            NotificationDeliveryStatus.Created => "created",
            NotificationDeliveryStatus.Queued => "queued",
            NotificationDeliveryStatus.Delivered => "delivered",
            NotificationDeliveryStatus.Opened => "opened",
            NotificationDeliveryStatus.Clicked => "clicked",
            NotificationDeliveryStatus.Bounced => "bounced",
            NotificationDeliveryStatus.Unsubscribed => "unsubscribed",
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unsupported notification delivery status.")
        };

    private static NotificationDeliveryStatus ParseDeliveryStatusFilter(string value)
        => value.Trim().ToLowerInvariant().Replace('-', '_') switch
        {
            "pending" => NotificationDeliveryStatus.Pending,
            "sent" => NotificationDeliveryStatus.Sent,
            "suppressed" => NotificationDeliveryStatus.Suppressed,
            "failed" => NotificationDeliveryStatus.Failed,
            "expired" => NotificationDeliveryStatus.Expired,
            "created" => NotificationDeliveryStatus.Created,
            "queued" => NotificationDeliveryStatus.Queued,
            "delivered" => NotificationDeliveryStatus.Delivered,
            "opened" => NotificationDeliveryStatus.Opened,
            "clicked" => NotificationDeliveryStatus.Clicked,
            "bounced" => NotificationDeliveryStatus.Bounced,
            "unsubscribed" => NotificationDeliveryStatus.Unsubscribed,
            var other => throw ApiException.Validation(
                "invalid_notification_delivery_status",
                $"Unsupported notification delivery status '{other}'.",
                [new ApiFieldError("status", "invalid_status", "Use a supported delivery status such as pending, sent, delivered, bounced, or unsubscribed.")])
        };

    private static string? NormalizeActionUrl(string? actionUrl)
    {
        if (string.IsNullOrWhiteSpace(actionUrl))
        {
            return null;
        }

        var trimmed = actionUrl.Trim();
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out _))
        {
            return trimmed;
        }

        return trimmed.StartsWith("/", StringComparison.Ordinal) ? trimmed : $"/{trimmed.TrimStart('~', '/')}";
    }

    private static string BuildPlainTextEmailBody(string subject, string body, string? actionUrl)
    {
        var builder = new List<string>
        {
            subject,
            string.Empty,
            body
        };

        if (!string.IsNullOrWhiteSpace(actionUrl))
        {
            builder.Add(string.Empty);
            builder.Add($"Open: {actionUrl}");
        }

        return string.Join(Environment.NewLine, builder);
    }

    private static string BuildHtmlEmailBody(string subject, string body, string? actionUrl)
    {
        var encodedSubject = System.Net.WebUtility.HtmlEncode(subject);
        var encodedBody = System.Net.WebUtility.HtmlEncode(body).Replace(Environment.NewLine, "<br />", StringComparison.Ordinal);
        var ctaMarkup = string.IsNullOrWhiteSpace(actionUrl)
            ? string.Empty
            : $"<p style=\"margin-top:24px;\"><a href=\"{System.Net.WebUtility.HtmlEncode(actionUrl)}\" style=\"display:inline-block;padding:12px 18px;background:#0f172a;color:#ffffff;text-decoration:none;border-radius:10px;font-weight:600;\">Open notification</a></p>";

        return $"""
                <html>
                  <body style="font-family:Arial,Helvetica,sans-serif;background:#f8fafc;color:#0f172a;padding:24px;">
                    <div style="max-width:640px;margin:0 auto;background:#ffffff;border:1px solid #e2e8f0;border-radius:16px;padding:24px;">
                      <h1 style="font-size:22px;margin:0 0 16px;">{encodedSubject}</h1>
                      <p style="font-size:15px;line-height:1.6;margin:0;">{encodedBody}</p>
                      {ctaMarkup}
                    </div>
                  </body>
                </html>
                """;
    }

    private static bool GetFeatureFlagState(
        IReadOnlyDictionary<string, bool> flags,
        string key,
        bool fallback)
        => flags.TryGetValue(key, out var enabled) ? enabled : fallback;

    private static bool ResolveChannelState(
        bool systemEnabled,
        bool adminGlobalEnabled,
        bool? adminEventOverride,
        bool userGlobalEnabled,
        bool? userEventOverride,
        bool catalogDefaultEnabled)
    {
        if (!systemEnabled || !adminGlobalEnabled || adminEventOverride == false)
        {
            return false;
        }

        if (userEventOverride.HasValue)
        {
            return userEventOverride.Value;
        }

        return userGlobalEnabled && (adminEventOverride ?? catalogDefaultEnabled);
    }

    private static NotificationEmailMode ResolveEmailMode(
        NotificationEmailMode catalogDefault,
        NotificationEmailMode? adminOverride,
        string? userOverride)
    {
        if (ParseEmailMode(userOverride) is { } parsedUserOverride)
        {
            return parsedUserOverride;
        }

        if (adminOverride.HasValue)
        {
            return adminOverride.Value;
        }

        return catalogDefault;
    }

    private static bool ResolveStoredChannelPreference(
        IReadOnlyDictionary<string, StoredNotificationEventPreference> overrides,
        string eventKey,
        bool globalEnabled,
        bool defaultEnabled,
        NotificationChannel channel = NotificationChannel.Email)
    {
        overrides.TryGetValue(eventKey, out var storedOverride);
        var eventOverride = channel switch
        {
            NotificationChannel.InApp => storedOverride?.InAppEnabled,
            NotificationChannel.Email => storedOverride?.EmailEnabled,
            NotificationChannel.Push => storedOverride?.PushEnabled,
            _ => null
        };

        return eventOverride ?? (globalEnabled && defaultEnabled);
    }

    private static string? Coalesce(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
}
