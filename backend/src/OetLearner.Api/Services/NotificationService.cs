using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using WebPush;

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
    DateTimeOffset? DeferredPushUntilUtc);

internal sealed record NotificationDigestJobPayload(
    string AuthAccountId,
    string LocalDateBucket,
    string Timezone);

public sealed class NotificationService(
    LearnerDbContext db,
    IEmailSender emailSender,
    IWebPushDispatcher webPushDispatcher,
    IHubContext<NotificationHub> hubContext,
    PlatformLinkService platformLinks,
    TimeProvider timeProvider,
    IOptions<WebPushOptions> webPushOptions,
    IOptions<NotificationProofHarnessOptions> notificationProofOptions,
    IWebHostEnvironment environment,
    ILogger<NotificationService> logger)
{
    private const int MaxPageSize = 100;
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

    public async Task<NotificationPreferencePayload> GetPreferencesAsync(string authAccountId, string audienceRole, CancellationToken ct)
    {
        var preference = await EnsurePreferenceAsync(authAccountId, audienceRole, ct);
        return await BuildPreferencePayloadAsync(preference, audienceRole, ct);
    }

    public async Task<NotificationPreferencePayload> PatchPreferencesAsync(
        string authAccountId,
        string audienceRole,
        NotificationPreferencePatchRequest request,
        CancellationToken ct)
    {
        var preference = await EnsurePreferenceAsync(authAccountId, audienceRole, ct);

        if (!string.IsNullOrWhiteSpace(request.Timezone))
        {
            preference.Timezone = request.Timezone;
        }

        if (request.GlobalInAppEnabled.HasValue)
        {
            preference.GlobalInAppEnabled = request.GlobalInAppEnabled.Value;
        }

        if (request.GlobalEmailEnabled.HasValue)
        {
            preference.GlobalEmailEnabled = request.GlobalEmailEnabled.Value;
        }

        if (request.GlobalPushEnabled.HasValue)
        {
            preference.GlobalPushEnabled = request.GlobalPushEnabled.Value;
        }

        if (request.QuietHoursEnabled.HasValue)
        {
            preference.QuietHoursEnabled = request.QuietHoursEnabled.Value;
        }

        if (request.QuietHoursStartLocalTime is not null)
        {
            preference.QuietHoursStartMinutes = NotificationScheduling.ParseLocalTimeToMinutes(request.QuietHoursStartLocalTime);
        }

        if (request.QuietHoursEndLocalTime is not null)
        {
            preference.QuietHoursEndMinutes = NotificationScheduling.ParseLocalTimeToMinutes(request.QuietHoursEndLocalTime);
        }

        var overrides = ReadStoredEventPreferences(preference.EventOverridesJson);
        if (request.EventPreferences is not null)
        {
            foreach (var (eventKey, overridePayload) in request.EventPreferences)
            {
                if (!NotificationCatalog.TryParseKey(eventKey, out _))
                {
                    throw ApiException.Validation(
                        "invalid_notification_event_key",
                        $"Unsupported notification event key '{eventKey}'.",
                        [new ApiFieldError("eventPreferences", "invalid_event_key", "Provide a supported event key from the notification catalog.")]);
                }

                var updatedOverride = new StoredNotificationEventPreference(
                    overridePayload.InAppEnabled,
                    overridePayload.EmailEnabled,
                    overridePayload.PushEnabled,
                    NormalizeEmailMode(overridePayload.EmailMode));

                if (updatedOverride.InAppEnabled is null
                    && updatedOverride.EmailEnabled is null
                    && updatedOverride.PushEnabled is null
                    && updatedOverride.EmailMode is null)
                {
                    overrides.Remove(eventKey);
                }
                else
                {
                    overrides[eventKey] = updatedOverride;
                }
            }
        }

        preference.EventOverridesJson = JsonSupport.Serialize(overrides);
        preference.UpdatedAt = timeProvider.GetUtcNow();

        if (string.Equals(audienceRole, ApplicationUserRoles.Learner, StringComparison.OrdinalIgnoreCase))
        {
            await MirrorLegacyLearnerNotificationsAsync(authAccountId, preference, ct);
        }

        await db.SaveChangesAsync(ct);
        return await BuildPreferencePayloadAsync(preference, audienceRole, ct);
    }

    public async Task<object> UpsertPushSubscriptionAsync(string authAccountId, PushSubscriptionPayload payload, CancellationToken ct)
    {
        var existing = await db.PushSubscriptions
            .FirstOrDefaultAsync(subscription => subscription.Endpoint == payload.Endpoint, ct);

        if (existing is null)
        {
            existing = new Domain.PushSubscription
            {
                Id = Guid.NewGuid(),
                AuthAccountId = authAccountId,
                Endpoint = payload.Endpoint,
                P256dh = payload.P256dh,
                Auth = payload.Auth,
                ExpiresAt = payload.ExpiresAt,
                IsActive = true,
                UserAgent = payload.UserAgent,
                CreatedAt = timeProvider.GetUtcNow(),
                UpdatedAt = timeProvider.GetUtcNow()
            };
            db.PushSubscriptions.Add(existing);
        }
        else
        {
            if (!string.Equals(existing.AuthAccountId, authAccountId, StringComparison.Ordinal))
            {
                throw ApiException.Forbidden("push_subscription_forbidden", "This push subscription belongs to another account.");
            }

            existing.P256dh = payload.P256dh;
            existing.Auth = payload.Auth;
            existing.ExpiresAt = payload.ExpiresAt;
            existing.IsActive = true;
            existing.UserAgent = payload.UserAgent;
            existing.FailureReasonCode = null;
            existing.UpdatedAt = timeProvider.GetUtcNow();
        }

        await db.SaveChangesAsync(ct);
        return new { subscriptionId = existing.Id };
    }

    public async Task DeletePushSubscriptionAsync(string authAccountId, Guid subscriptionId, CancellationToken ct)
    {
        var subscription = await db.PushSubscriptions
            .FirstOrDefaultAsync(existingSubscription => existingSubscription.Id == subscriptionId && existingSubscription.AuthAccountId == authAccountId, ct)
            ?? throw ApiException.NotFound("push_subscription_not_found", "Push subscription not found.");

        subscription.IsActive = false;
        subscription.UpdatedAt = timeProvider.GetUtcNow();
        await db.SaveChangesAsync(ct);
    }

    private static readonly HashSet<string> ValidPlatforms = new(StringComparer.OrdinalIgnoreCase) { "android", "ios", "web" };

    public async Task<object> RegisterPushTokenAsync(string authAccountId, RegisterPushTokenRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Token))
        {
            throw ApiException.Validation(
                "push_token_required",
                "A device push token is required.",
                [new ApiFieldError("token", "required", "Provide a non-empty device push token.")]);
        }

        if (!ValidPlatforms.Contains(request.Platform))
        {
            throw ApiException.Validation(
                "push_token_invalid_platform",
                $"Platform '{request.Platform}' is not supported. Use android, ios, or web.",
                [new ApiFieldError("platform", "invalid", "Allowed values: android, ios, web.")]);
        }

        var normalizedPlatform = request.Platform.ToLowerInvariant();

        var existing = await db.MobilePushTokens
            .FirstOrDefaultAsync(t => t.Token == request.Token, ct);

        if (existing is not null)
        {
            existing.AuthAccountId = authAccountId;
            existing.Platform = normalizedPlatform;
            existing.IsActive = true;
            existing.UpdatedAt = timeProvider.GetUtcNow();
        }
        else
        {
            existing = new Domain.MobilePushToken
            {
                Id = Guid.NewGuid(),
                AuthAccountId = authAccountId,
                Token = request.Token,
                Platform = normalizedPlatform,
                IsActive = true,
                CreatedAt = timeProvider.GetUtcNow(),
                UpdatedAt = timeProvider.GetUtcNow()
            };
            db.MobilePushTokens.Add(existing);
        }

        await db.SaveChangesAsync(ct);
        return new { tokenId = existing.Id };
    }

    public Task<IReadOnlyList<AdminNotificationCatalogEntry>> GetAdminCatalogAsync(CancellationToken ct)
        => Task.FromResult<IReadOnlyList<AdminNotificationCatalogEntry>>(NotificationCatalog.ToAdminEntries());

    public async Task<AdminNotificationPoliciesResponse> GetAdminPoliciesAsync(CancellationToken ct)
    {
        var overrides = await db.NotificationPolicyOverrides
            .AsNoTracking()
            .ToListAsync(ct);

        var overrideLookup = overrides.ToDictionary(overrideRow => (overrideRow.AudienceRole, overrideRow.EventKey), overrideRow => overrideRow);
        var globalEmailEnabledByAudience = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
        {
            [ApplicationUserRoles.Learner] = ResolveGlobalAudienceEmailEnabled(overrideLookup, ApplicationUserRoles.Learner),
            [ApplicationUserRoles.Expert] = ResolveGlobalAudienceEmailEnabled(overrideLookup, ApplicationUserRoles.Expert),
            [ApplicationUserRoles.Admin] = ResolveGlobalAudienceEmailEnabled(overrideLookup, ApplicationUserRoles.Admin)
        };

        var rows = NotificationCatalog.All
            .Select(entry =>
            {
                overrideLookup.TryGetValue((entry.AudienceRole, NotificationCatalog.GetKey(entry.Key)), out var rowOverride);
                return new AdminNotificationPolicyRow(
                    entry.AudienceRole,
                    NotificationCatalog.GetKey(entry.Key),
                    entry.Category,
                    entry.Label,
                    rowOverride?.InAppEnabled ?? entry.DefaultChannels.InAppEnabled,
                    rowOverride?.EmailEnabled ?? entry.DefaultChannels.EmailEnabled,
                    rowOverride?.PushEnabled ?? entry.DefaultChannels.PushEnabled,
                    NormalizeEmailMode(rowOverride?.EmailMode ?? entry.DefaultChannels.EmailMode),
                    rowOverride is not null,
                    rowOverride?.UpdatedAt,
                    rowOverride?.UpdatedByAdminId,
                    rowOverride?.UpdatedByAdminName);
            })
            .OrderBy(row => row.AudienceRole)
            .ThenBy(row => row.Category)
            .ThenBy(row => row.Label)
            .ToArray();

        return new AdminNotificationPoliciesResponse(globalEmailEnabledByAudience, rows);
    }

    public async Task<AdminNotificationPolicyRow> UpdateAdminPolicyAsync(
        string adminId,
        string adminName,
        string audienceRole,
        string eventKey,
        AdminNotificationPolicyUpdateRequest request,
        CancellationToken ct)
    {
        ValidateAudienceRole(audienceRole);

        NotificationCatalogEntry? catalogEntry = null;
        if (!string.Equals(eventKey, NotificationCatalog.GlobalPolicyEventKey, StringComparison.OrdinalIgnoreCase))
        {
            if (!NotificationCatalog.TryParseKey(eventKey, out var parsedEventKey))
            {
                throw ApiException.Validation(
                    "invalid_notification_event_key",
                    $"Unsupported notification event key '{eventKey}'.",
                    [new ApiFieldError("eventKey", "invalid_event_key", "Use an event key returned by the notification catalog.")]);
            }

            catalogEntry = NotificationCatalog.Get(parsedEventKey);
            if (!string.Equals(catalogEntry.AudienceRole, audienceRole, StringComparison.OrdinalIgnoreCase))
            {
                throw ApiException.Validation(
                    "notification_audience_mismatch",
                    $"Event {eventKey} does not belong to audience {audienceRole}.",
                    [new ApiFieldError("audienceRole", "invalid", "The event key does not match the selected audience role.")]);
            }
        }

        var overrideRow = await db.NotificationPolicyOverrides
            .FirstOrDefaultAsync(existingOverride => existingOverride.AudienceRole == audienceRole && existingOverride.EventKey == eventKey, ct);

        if (overrideRow is null)
        {
            overrideRow = new NotificationPolicyOverride
            {
                Id = Guid.NewGuid(),
                AudienceRole = audienceRole,
                EventKey = eventKey
            };
            db.NotificationPolicyOverrides.Add(overrideRow);
        }

        overrideRow.InAppEnabled = request.InAppEnabled;
        overrideRow.EmailEnabled = request.EmailEnabled;
        overrideRow.PushEnabled = request.PushEnabled;
        overrideRow.EmailMode = ParseEmailMode(request.EmailMode);
        overrideRow.UpdatedByAdminId = adminId;
        overrideRow.UpdatedByAdminName = adminName;
        overrideRow.UpdatedAt = timeProvider.GetUtcNow();

        db.AuditEvents.Add(new AuditEvent
        {
            Id = $"AUD-{Guid.NewGuid():N}",
            OccurredAt = timeProvider.GetUtcNow(),
            ActorId = adminId,
            ActorName = adminName,
            Action = "notification_policy_updated",
            ResourceType = "NotificationPolicy",
            ResourceId = $"{audienceRole}:{eventKey}",
            Details = $"Updated notification policy for {audienceRole}/{eventKey}"
        });

        await db.SaveChangesAsync(ct);

        if (catalogEntry is null)
        {
            return new AdminNotificationPolicyRow(
                audienceRole,
                eventKey,
                "global",
                "Global Email Switch",
                request.InAppEnabled ?? false,
                request.EmailEnabled ?? true,
                request.PushEnabled ?? false,
                NormalizeEmailMode(ParseEmailMode(request.EmailMode) ?? NotificationEmailMode.Off),
                true,
                overrideRow.UpdatedAt,
                overrideRow.UpdatedByAdminId,
                overrideRow.UpdatedByAdminName);
        }

        return new AdminNotificationPolicyRow(
            audienceRole,
            eventKey,
            catalogEntry.Category,
            catalogEntry.Label,
            overrideRow.InAppEnabled ?? catalogEntry.DefaultChannels.InAppEnabled,
            overrideRow.EmailEnabled ?? catalogEntry.DefaultChannels.EmailEnabled,
            overrideRow.PushEnabled ?? catalogEntry.DefaultChannels.PushEnabled,
            NormalizeEmailMode(overrideRow.EmailMode ?? catalogEntry.DefaultChannels.EmailMode),
            true,
            overrideRow.UpdatedAt,
            overrideRow.UpdatedByAdminId,
            overrideRow.UpdatedByAdminName);
    }

    public async Task<AdminNotificationHealthSnapshot> GetAdminHealthAsync(CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow();
        var since = now.AddHours(-24);

        var queuedEvents = await db.BackgroundJobs.CountAsync(job => job.Type == JobType.NotificationFanout && job.State == AsyncState.Queued, ct);
        var failedEvents = await db.NotificationDeliveryAttempts.CountAsync(attempt => attempt.Status == NotificationDeliveryStatus.Failed && attempt.AttemptedAt >= since, ct);
        var unreadInboxItems = await db.NotificationInboxItems.CountAsync(item => !item.IsRead, ct);
        var pendingDigestJobs = await db.BackgroundJobs.CountAsync(job => job.Type == JobType.NotificationDigestDispatch && job.State == AsyncState.Queued, ct);
        var activePushSubscriptions = await db.PushSubscriptions.CountAsync(subscription => subscription.IsActive, ct);
        var expiredPushSubscriptions = await db.PushSubscriptions.CountAsync(subscription => !subscription.IsActive && subscription.LastFailureAt >= since, ct);

        var channelSnapshotRows = await db.NotificationDeliveryAttempts
            .AsNoTracking()
            .Where(attempt => attempt.AttemptedAt >= since)
            .GroupBy(attempt => attempt.Channel)
            .Select(group => new
            {
                Channel = group.Key,
                SentCount = group.Count(attempt => attempt.Status == NotificationDeliveryStatus.Sent),
                FailedCount = group.Count(attempt => attempt.Status == NotificationDeliveryStatus.Failed),
                SuppressedCount = group.Count(attempt => attempt.Status == NotificationDeliveryStatus.Suppressed || attempt.Status == NotificationDeliveryStatus.Expired)
            })
            .ToListAsync(ct);

        var channelSnapshots = channelSnapshotRows
            .Select(row => new AdminNotificationHealthChannelSnapshot(
                NormalizeChannel(row.Channel),
                row.SentCount,
                row.FailedCount,
                row.SuppressedCount))
            .ToList();

        var failureQueueRows = await db.NotificationDeliveryAttempts
            .AsNoTracking()
            .Join(
                db.NotificationEvents.AsNoTracking(),
                attempt => attempt.NotificationEventId,
                notificationEvent => notificationEvent.Id,
                (attempt, notificationEvent) => new { attempt, notificationEvent })
            .Where(row => row.attempt.Status == NotificationDeliveryStatus.Failed || row.attempt.Status == NotificationDeliveryStatus.Expired)
            .OrderByDescending(row => row.attempt.AttemptedAt)
            .Take(25)
            .Select(row => new
            {
                NotificationEventId = row.notificationEvent.Id,
                row.notificationEvent.EventKey,
                row.notificationEvent.RecipientRole,
                Channel = row.attempt.Channel,
                Status = row.attempt.Status,
                row.attempt.ErrorCode,
                row.attempt.ErrorMessage,
                row.attempt.AttemptedAt
            })
            .ToListAsync(ct);

        var failureQueue = failureQueueRows
            .Select(row => new AdminNotificationFailureQueueItem(
                row.NotificationEventId,
                row.EventKey,
                row.RecipientRole,
                NormalizeChannel(row.Channel),
                NormalizeDeliveryStatus(row.Status),
                row.ErrorCode,
                row.ErrorMessage,
                row.AttemptedAt))
            .ToList();

        return new AdminNotificationHealthSnapshot(
            now,
            queuedEvents,
            failedEvents,
            unreadInboxItems,
            failedEvents,
            pendingDigestJobs,
            activePushSubscriptions,
            expiredPushSubscriptions,
            channelSnapshots,
            failureQueue);
    }

    public async Task<NotificationDeliveryAttemptResponse> GetAdminDeliveriesAsync(int page, int pageSize, CancellationToken ct)
    {
        var normalizedPage = Math.Max(1, page);
        var normalizedPageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var totalCount = await db.NotificationDeliveryAttempts.CountAsync(ct);
        var itemRows = await db.NotificationDeliveryAttempts
            .AsNoTracking()
            .Join(
                db.NotificationEvents.AsNoTracking(),
                attempt => attempt.NotificationEventId,
                notificationEvent => notificationEvent.Id,
                (attempt, notificationEvent) => new
                {
                    attempt.Id,
                    NotificationEventId = notificationEvent.Id,
                    notificationEvent.EventKey,
                    notificationEvent.RecipientRole,
                    attempt.Channel,
                    attempt.Status,
                    attempt.Provider,
                    attempt.ErrorCode,
                    attempt.ErrorMessage,
                    attempt.AttemptedAt,
                    attempt.CompletedAt
                })
            .OrderByDescending(item => item.AttemptedAt)
            .Skip((normalizedPage - 1) * normalizedPageSize)
            .Take(normalizedPageSize)
            .ToListAsync(ct);

        var items = itemRows
            .Select(row => new NotificationDeliveryAttemptItem(
                row.Id,
                row.NotificationEventId,
                row.EventKey,
                row.RecipientRole,
                NormalizeChannel(row.Channel),
                NormalizeDeliveryStatus(row.Status),
                row.Provider,
                row.ErrorCode,
                row.ErrorMessage,
                row.AttemptedAt,
                row.CompletedAt))
            .ToList();

        return new NotificationDeliveryAttemptResponse(items, totalCount, normalizedPage, normalizedPageSize);
    }

    public async Task SendTestEmailAsync(string adminId, string adminName, AdminNotificationTestEmailRequest request, CancellationToken ct)
    {
        ValidateAudienceRole(request.AudienceRole);
        if (!NotificationCatalog.TryParseKey(request.EventKey, out var eventKey))
        {
            throw ApiException.Validation(
                "invalid_notification_event_key",
                $"Unsupported notification event key '{request.EventKey}'.",
                [new ApiFieldError("eventKey", "invalid_event_key", "Use an event key from the notification catalog.")]);
        }

        var sampleTokens = BuildSampleTokens(request.EventKey, request.AudienceRole);
        var subject = $"[Test] {NotificationCatalog.BuildTitle(eventKey, sampleTokens)}";
        var body = NotificationCatalog.BuildBody(eventKey, sampleTokens);
        var actionUrl = NormalizeActionUrl(NotificationCatalog.BuildActionUrl(eventKey, sampleTokens));

        await emailSender.SendAsync(
            new EmailMessage(
                request.RecipientEmail,
                subject,
                BuildPlainTextEmailBody(subject, body, actionUrl),
                BuildHtmlEmailBody(subject, body, actionUrl)),
            ct);

        db.AuditEvents.Add(new AuditEvent
        {
            Id = $"AUD-{Guid.NewGuid():N}",
            OccurredAt = timeProvider.GetUtcNow(),
            ActorId = adminId,
            ActorName = adminName,
            Action = "notification_test_email_sent",
            ResourceType = "NotificationPolicy",
            ResourceId = $"{request.AudienceRole}:{request.EventKey}",
            Details = $"Sent notification test email to {request.RecipientEmail}"
        });

        await db.SaveChangesAsync(ct);
    }

    public async Task<AdminNotificationProofTriggerResponse> TriggerProofNotificationAsync(
        string adminId,
        string adminName,
        AdminNotificationProofTriggerRequest request,
        CancellationToken ct)
    {
        try
        {
            EnsureProofHarnessEnabled();

            if (!NotificationCatalog.TryParseKey(request.EventKey, out var eventKey))
            {
                throw ApiException.Validation(
                    "invalid_notification_event_key",
                    $"Unsupported notification event key '{request.EventKey}'.",
                    [new ApiFieldError("eventKey", "invalid_event_key", "Use an event key from the notification catalog.")]);
            }

            var catalog = NotificationCatalog.Get(eventKey);
            var account = await db.ApplicationUserAccounts
                .AsNoTracking()
                .FirstOrDefaultAsync(existingAccount =>
                    existingAccount.Email == request.RecipientEmail
                    && existingAccount.Role == catalog.AudienceRole
                    && existingAccount.DeletedAt == null, ct)
                ?? throw ApiException.Validation(
                    "notification_proof_recipient_not_found",
                    $"Could not find an active {catalog.AudienceRole} account for '{request.RecipientEmail}'.",
                    [new ApiFieldError("recipientEmail", "not_found", "Use a dedicated learner, expert, or admin test account email.")]);

            var tokens = BuildProofTokens(request.EventKey, catalog.AudienceRole, request.Tokens);
            var entityType = string.IsNullOrWhiteSpace(request.EntityType) ? $"proof_{catalog.AudienceRole}" : request.EntityType.Trim();
            var entityId = string.IsNullOrWhiteSpace(request.EntityId) ? $"{request.EventKey}:{account.Id}" : request.EntityId.Trim();
            var versionOrDateBucket = string.IsNullOrWhiteSpace(request.VersionOrDateBucket)
                ? timeProvider.GetUtcNow().UtcDateTime.Ticks.ToString()
                : request.VersionOrDateBucket.Trim();

            var eventId = await CreateForAuthAccountAsync(eventKey, account.Id, catalog.AudienceRole, entityType, entityId, versionOrDateBucket, tokens, enqueueFanoutJob: false, ct)
                ?? throw ApiException.Validation("notification_proof_create_failed", "The notification proof event could not be created.");

            var processedImmediately = false;
            if (request.ProcessImmediately)
            {
                await ProcessFanoutAsync(new BackgroundJobItem
                {
                    Id = $"job-proof-{Guid.NewGuid():N}",
                    Type = JobType.NotificationFanout,
                    State = AsyncState.Processing,
                    ResourceId = eventId,
                    PayloadJson = JsonSupport.Serialize(new { notificationEventId = eventId }),
                    CreatedAt = timeProvider.GetUtcNow(),
                    AvailableAt = timeProvider.GetUtcNow(),
                    LastTransitionAt = timeProvider.GetUtcNow(),
                    StatusReasonCode = "processing",
                    StatusMessage = "Proof harness processing started.",
                    Retryable = false
                }, ct);
                processedImmediately = true;
            }

            var digestDispatchedImmediately = false;
            if (request.DispatchDigestImmediately)
            {
                var digestJobs = await db.BackgroundJobs
                    .Where(job => job.Type == JobType.NotificationDigestDispatch && job.State == AsyncState.Queued)
                    .OrderBy(job => job.CreatedAt)
                    .ToListAsync(ct);

                foreach (var digestJob in digestJobs)
                {
                    var digestPayload = JsonSupport.Deserialize(
                        digestJob.PayloadJson,
                        new NotificationDigestJobPayload(string.Empty, string.Empty, "UTC"));
                    if (!string.Equals(digestPayload.AuthAccountId, account.Id, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    await ProcessBackgroundProofJobAsync(digestJob, job => ProcessDigestDispatchAsync(job, ct), ct);
                    digestDispatchedImmediately = true;
                }
            }

            db.AuditEvents.Add(new AuditEvent
            {
                Id = $"AUD-{Guid.NewGuid():N}",
                OccurredAt = timeProvider.GetUtcNow(),
                ActorId = adminId,
                ActorName = adminName,
                Action = "notification_proof_triggered",
                ResourceType = "NotificationProof",
                ResourceId = eventId,
                Details = $"Triggered notification proof for {request.RecipientEmail}"
            });
            await db.SaveChangesAsync(ct);

            var notificationEvent = await db.NotificationEvents
                .AsNoTracking()
                .FirstAsync(existingEvent => existingEvent.Id == eventId, ct);
            var inboxItem = await db.NotificationInboxItems
                .AsNoTracking()
                .FirstOrDefaultAsync(item => item.NotificationEventId == eventId, ct);

            return new AdminNotificationProofTriggerResponse(
                notificationEvent.Id,
                notificationEvent.EventKey,
                notificationEvent.RecipientRole,
                account.Email,
                account.Id,
                notificationEvent.Title,
                notificationEvent.Body,
                NormalizeActionUrl(notificationEvent.ActionUrl),
                NormalizeSeverity(notificationEvent.Severity),
                inboxItem?.Id,
                processedImmediately,
                digestDispatchedImmediately);
        }
        catch (Exception ex) when (ex is not ApiException)
        {
            logger.LogError(ex, "Notification proof trigger failed for {EventKey} to {RecipientEmail}. Base: {BaseMessage}", request.EventKey, request.RecipientEmail, ex.GetBaseException().Message);
            try
            {
                var logPath = Path.Combine(Path.GetTempPath(), "oet-notification-proof-errors.log");
                var logEntry = $"[{DateTimeOffset.UtcNow:O}] {request.EventKey} -> {request.RecipientEmail}{Environment.NewLine}{ex}{Environment.NewLine}{new string('-', 80)}{Environment.NewLine}";
                await File.AppendAllTextAsync(logPath, logEntry, ct);
            }
            catch
            {
            }
            throw;
        }
    }

    public async Task ProcessFanoutAsync(BackgroundJobItem job, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(job.ResourceId))
        {
            return;
        }

        var notificationEvent = await db.NotificationEvents
            .FirstOrDefaultAsync(existingEvent => existingEvent.Id == job.ResourceId, ct);
        if (notificationEvent is null)
        {
            return;
        }

        notificationEvent.State = AsyncState.Processing;
        notificationEvent.FanoutAttempts += 1;
        await db.SaveChangesAsync(ct);

        if (!NotificationCatalog.TryParseKey(notificationEvent.EventKey, out var eventKey))
        {
            notificationEvent.State = AsyncState.Failed;
            await db.SaveChangesAsync(ct);
            return;
        }

        var decision = await ResolvePolicyDecisionAsync(notificationEvent.RecipientAuthAccountId, notificationEvent.RecipientRole, eventKey, ct);
        var inboxItem = decision.InAppEnabled
            ? await EnsureInboxItemAsync(notificationEvent, decision, ct)
            : null;

        if (inboxItem is not null)
        {
            await PublishRealtimeAsync(notificationEvent.RecipientAuthAccountId, inboxItem, ct);
        }

        if (decision.EmailEnabled)
        {
            if (decision.EmailMode == NotificationEmailMode.Immediate)
            {
                await SendImmediateEmailAsync(notificationEvent, ct);
            }
            else if (decision.EmailMode == NotificationEmailMode.DailyDigest)
            {
                await EnsureDigestJobAsync(notificationEvent, decision, ct);
            }
        }
        else
        {
            await RecordSuppressedAttemptIfMissingAsync(notificationEvent, NotificationChannel.Email, "email_disabled", "Email delivery was disabled by policy or user preference.", ct);
        }

        if (decision.PushEnabled)
        {
            if (decision.DeferredPushUntilUtc.HasValue)
            {
                await QueueDeferredPushAsync(notificationEvent, decision.DeferredPushUntilUtc.Value, ct);
            }
            else
            {
                await SendPushAsync(notificationEvent, ct);
            }
        }
        else
        {
            await RecordSuppressedAttemptIfMissingAsync(notificationEvent, NotificationChannel.Push, "push_disabled", "Push delivery was disabled by policy or user preference.", ct);
        }

        notificationEvent.State = AsyncState.Completed;
        notificationEvent.ProcessedAt = timeProvider.GetUtcNow();
        await db.SaveChangesAsync(ct);
    }

    public async Task ProcessDigestDispatchAsync(BackgroundJobItem job, CancellationToken ct)
    {
        var payload = JsonSupport.Deserialize(job.PayloadJson, new NotificationDigestJobPayload(job.ResourceId ?? string.Empty, string.Empty, "UTC"));
        if (string.IsNullOrWhiteSpace(payload.AuthAccountId) || string.IsNullOrWhiteSpace(payload.LocalDateBucket))
        {
            return;
        }

        var notificationEvents = await db.NotificationEvents
            .AsNoTracking()
            .Where(notificationEvent => notificationEvent.RecipientAuthAccountId == payload.AuthAccountId)
            .OrderBy(notificationEvent => notificationEvent.CreatedAt)
            .ToListAsync(ct);

        var digestEvents = new List<NotificationEvent>();
        foreach (var notificationEvent in notificationEvents)
        {
            if (!NotificationCatalog.TryParseKey(notificationEvent.EventKey, out var eventKey))
            {
                continue;
            }

            var localDateBucket = NotificationScheduling.GetLocalDateBucket(notificationEvent.CreatedAt, payload.Timezone);
            if (!string.Equals(localDateBucket, payload.LocalDateBucket, StringComparison.Ordinal))
            {
                continue;
            }

            var decision = await ResolvePolicyDecisionAsync(notificationEvent.RecipientAuthAccountId, notificationEvent.RecipientRole, eventKey, ct);
            if (!decision.EmailEnabled || decision.EmailMode != NotificationEmailMode.DailyDigest)
            {
                continue;
            }

            var emailAlreadySent = await db.NotificationDeliveryAttempts
                .AnyAsync(attempt =>
                    attempt.NotificationEventId == notificationEvent.Id
                    && attempt.Channel == NotificationChannel.Email
                    && attempt.Status == NotificationDeliveryStatus.Sent, ct);
            if (!emailAlreadySent)
            {
                digestEvents.Add(notificationEvent);
            }
        }

        if (digestEvents.Count == 0)
        {
            return;
        }

        var account = await db.ApplicationUserAccounts
            .AsNoTracking()
            .FirstOrDefaultAsync(existingAccount => existingAccount.Id == payload.AuthAccountId, ct)
            ?? throw ApiException.NotFound("auth_account_not_found", "Notification account not found.");

        var subject = $"Your OET Prep daily digest for {payload.LocalDateBucket}";
        var textBody = string.Join(
            Environment.NewLine + Environment.NewLine,
            digestEvents.Select(notificationEvent =>
                $"- {notificationEvent.Title}{Environment.NewLine}{notificationEvent.Body}{Environment.NewLine}{platformLinks.BuildWebUrl(notificationEvent.ActionUrl ?? "/")}"));
        var htmlBody = "<ul>" + string.Join(string.Empty, digestEvents.Select(notificationEvent =>
            $"<li><strong>{notificationEvent.Title}</strong><br />{notificationEvent.Body}<br /><a href=\"{platformLinks.BuildWebUrl(notificationEvent.ActionUrl ?? "/")}\">Open</a></li>")) + "</ul>";

        try
        {
            await emailSender.SendAsync(new EmailMessage(account.Email, subject, textBody, htmlBody), ct);
            foreach (var notificationEvent in digestEvents)
            {
                db.NotificationDeliveryAttempts.Add(new NotificationDeliveryAttempt
                {
                    Id = $"nda-{Guid.NewGuid():N}",
                    NotificationEventId = notificationEvent.Id,
                    AuthAccountId = payload.AuthAccountId,
                    Channel = NotificationChannel.Email,
                    Status = NotificationDeliveryStatus.Sent,
                    Provider = "digest-email",
                    AttemptedAt = timeProvider.GetUtcNow(),
                    CompletedAt = timeProvider.GetUtcNow()
                });
            }

            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send digest email for account {AuthAccountId} bucket {LocalDateBucket}", payload.AuthAccountId, payload.LocalDateBucket);
            foreach (var notificationEvent in digestEvents)
            {
                db.NotificationDeliveryAttempts.Add(new NotificationDeliveryAttempt
                {
                    Id = $"nda-{Guid.NewGuid():N}",
                    NotificationEventId = notificationEvent.Id,
                    AuthAccountId = payload.AuthAccountId,
                    Channel = NotificationChannel.Email,
                    Status = NotificationDeliveryStatus.Failed,
                    Provider = "digest-email",
                    ErrorCode = "email_send_failed",
                    ErrorMessage = ex.Message,
                    AttemptedAt = timeProvider.GetUtcNow()
                });
            }

            await db.SaveChangesAsync(ct);
            throw;
        }
    }

    private async Task<NotificationPreference> EnsurePreferenceAsync(string authAccountId, string audienceRole, CancellationToken ct)
    {
        ValidateAudienceRole(audienceRole);

        var preference = await db.NotificationPreferences
            .FirstOrDefaultAsync(existingPreference => existingPreference.AuthAccountId == authAccountId, ct);
        var changed = false;
        var now = timeProvider.GetUtcNow();

        if (preference is null)
        {
            preference = new NotificationPreference
            {
                Id = Guid.NewGuid(),
                AuthAccountId = authAccountId,
                Timezone = await ResolveDefaultTimezoneAsync(authAccountId, audienceRole, ct),
                CreatedAt = now,
                UpdatedAt = now
            };
            db.NotificationPreferences.Add(preference);
            changed = true;
        }
        else if (string.IsNullOrWhiteSpace(preference.Timezone))
        {
            preference.Timezone = await ResolveDefaultTimezoneAsync(authAccountId, audienceRole, ct);
            changed = true;
        }

        if (string.Equals(audienceRole, ApplicationUserRoles.Learner, StringComparison.OrdinalIgnoreCase)
            && !HasExplicitPreferenceState(preference))
        {
            changed |= await ApplyLegacyLearnerSettingsToPreferenceAsync(authAccountId, preference, ct);
        }

        if (changed)
        {
            preference.UpdatedAt = now;
            await db.SaveChangesAsync(ct);
        }

        return preference;
    }

    private async Task<NotificationPreferencePayload> BuildPreferencePayloadAsync(
        NotificationPreference preference,
        string audienceRole,
        CancellationToken ct)
    {
        var storedOverrides = ReadStoredEventPreferences(preference.EventOverridesJson);
        var eventPreferences = NotificationCatalog.All
            .Where(entry => string.Equals(entry.AudienceRole, audienceRole, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(
                entry => NotificationCatalog.GetKey(entry.Key),
                entry =>
                {
                    storedOverrides.TryGetValue(NotificationCatalog.GetKey(entry.Key), out var storedOverride);
                    return new NotificationEventPreferencePayload(
                        storedOverride?.InAppEnabled,
                        storedOverride?.EmailEnabled,
                        storedOverride?.PushEnabled,
                        storedOverride?.EmailMode);
                },
                StringComparer.OrdinalIgnoreCase);

        var legacyLearnerSettings = string.Equals(audienceRole, ApplicationUserRoles.Learner, StringComparison.OrdinalIgnoreCase)
            ? await LoadLegacyLearnerSettingsAsync(preference.AuthAccountId, preference, ct)
            : new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        return new NotificationPreferencePayload(
            preference.Timezone,
            preference.GlobalInAppEnabled,
            preference.GlobalEmailEnabled,
            preference.GlobalPushEnabled,
            preference.QuietHoursEnabled,
            NotificationScheduling.FormatMinutesAsLocalTime(preference.QuietHoursStartMinutes),
            NotificationScheduling.FormatMinutesAsLocalTime(preference.QuietHoursEndMinutes),
            eventPreferences,
            legacyLearnerSettings);
    }

    private async Task<Dictionary<string, object?>> LoadLegacyLearnerSettingsAsync(
        string authAccountId,
        NotificationPreference preference,
        CancellationToken ct)
    {
        var learnerId = await db.Users
            .AsNoTracking()
            .Where(user => user.AuthAccountId == authAccountId)
            .Select(user => user.Id)
            .FirstOrDefaultAsync(ct);

        if (string.IsNullOrWhiteSpace(learnerId))
        {
            return BuildLegacyLearnerSettings(preference);
        }

        var settings = await db.Settings
            .AsNoTracking()
            .FirstOrDefaultAsync(existingSettings => existingSettings.UserId == learnerId, ct);

        var merged = settings is null
            ? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            : JsonSupport.Deserialize(settings.NotificationsJson, new Dictionary<string, object?>());

        foreach (var (key, value) in BuildLegacyLearnerSettings(preference))
        {
            merged[key] = value;
        }

        return merged;
    }

    private static Dictionary<string, StoredNotificationEventPreference> ReadStoredEventPreferences(string? json)
        => JsonSupport.Deserialize(json, new Dictionary<string, StoredNotificationEventPreference>(StringComparer.OrdinalIgnoreCase));

    private static string? NormalizeEmailMode(string? value)
        => ParseEmailMode(value) is { } mode ? NormalizeEmailMode(mode) : null;

    private static string NormalizeEmailMode(NotificationEmailMode value)
        => value switch
        {
            NotificationEmailMode.Off => "off",
            NotificationEmailMode.Immediate => "immediate",
            NotificationEmailMode.DailyDigest => "daily_digest",
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported notification email mode.")
        };

    private static NotificationEmailMode? ParseEmailMode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "off" => NotificationEmailMode.Off,
            "immediate" => NotificationEmailMode.Immediate,
            "daily_digest" or "daily-digest" or "dailydigest" or "digest" or "daily" => NotificationEmailMode.DailyDigest,
            _ => throw ApiException.Validation(
                "invalid_notification_email_mode",
                $"Unsupported notification email mode '{value}'.",
                [new ApiFieldError("emailMode", "invalid_email_mode", "Use off, immediate, or daily_digest.")])
        };
    }

    private static void ValidateAudienceRole(string audienceRole)
    {
        if (string.Equals(audienceRole, ApplicationUserRoles.Learner, StringComparison.OrdinalIgnoreCase)
            || string.Equals(audienceRole, ApplicationUserRoles.Expert, StringComparison.OrdinalIgnoreCase)
            || string.Equals(audienceRole, ApplicationUserRoles.Admin, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        throw ApiException.Validation(
            "invalid_notification_audience",
            $"Unsupported audience role '{audienceRole}'.",
            [new ApiFieldError("audienceRole", "invalid_audience_role", "Use learner, expert, or admin.")]);
    }

    private static bool ResolveGlobalAudienceEmailEnabled(
        IReadOnlyDictionary<(string AudienceRole, string EventKey), NotificationPolicyOverride> overrideLookup,
        string audienceRole)
        => overrideLookup.TryGetValue((audienceRole, NotificationCatalog.GlobalPolicyEventKey), out var globalOverride)
            ? globalOverride.EmailEnabled ?? true
            : true;

    private async Task<bool> MirrorLegacyLearnerNotificationsAsync(
        string authAccountId,
        NotificationPreference preference,
        CancellationToken ct)
    {
        var learner = await db.Users
            .FirstOrDefaultAsync(user => user.AuthAccountId == authAccountId, ct);
        if (learner is null)
        {
            return false;
        }

        var settings = await db.Settings.FirstOrDefaultAsync(existingSettings => existingSettings.UserId == learner.Id, ct);
        if (settings is null)
        {
            settings = new LearnerSettings
            {
                Id = Guid.NewGuid(),
                UserId = learner.Id
            };
            db.Settings.Add(settings);
        }

        var merged = JsonSupport.Deserialize(settings.NotificationsJson, new Dictionary<string, object?>());
        foreach (var (key, value) in BuildLegacyLearnerSettings(preference))
        {
            merged[key] = value;
        }

        settings.NotificationsJson = JsonSupport.Serialize(merged);
        return true;
    }

    private async Task<bool> ApplyLegacyLearnerSettingsToPreferenceAsync(
        string authAccountId,
        NotificationPreference preference,
        CancellationToken ct)
    {
        var learnerId = await db.Users
            .AsNoTracking()
            .Where(user => user.AuthAccountId == authAccountId)
            .Select(user => user.Id)
            .FirstOrDefaultAsync(ct);

        if (string.IsNullOrWhiteSpace(learnerId))
        {
            return false;
        }

        var settings = await db.Settings
            .AsNoTracking()
            .FirstOrDefaultAsync(existingSettings => existingSettings.UserId == learnerId, ct);
        if (settings is null || string.IsNullOrWhiteSpace(settings.NotificationsJson))
        {
            return false;
        }

        var values = JsonSupport.Deserialize(settings.NotificationsJson, new Dictionary<string, object?>());
        if (values.Count == 0)
        {
            return false;
        }

        var changed = false;

        if (TryReadBoolean(values, "globalInAppEnabled", out var globalInAppEnabled)
            || TryReadBoolean(values, "inAppEnabled", out globalInAppEnabled))
        {
            preference.GlobalInAppEnabled = globalInAppEnabled;
            changed = true;
        }

        if (TryReadBoolean(values, "globalEmailEnabled", out var globalEmailEnabled)
            || TryReadBoolean(values, "emailEnabled", out globalEmailEnabled))
        {
            preference.GlobalEmailEnabled = globalEmailEnabled;
            changed = true;
        }

        if (TryReadBoolean(values, "globalPushEnabled", out var globalPushEnabled)
            || TryReadBoolean(values, "pushEnabled", out globalPushEnabled))
        {
            preference.GlobalPushEnabled = globalPushEnabled;
            changed = true;
        }

        if (TryReadBoolean(values, "quietHoursEnabled", out var quietHoursEnabled))
        {
            preference.QuietHoursEnabled = quietHoursEnabled;
            changed = true;
        }

        if (TryReadString(values, "quietHoursStartLocalTime", out var quietHoursStart))
        {
            preference.QuietHoursStartMinutes = NotificationScheduling.ParseLocalTimeToMinutes(quietHoursStart);
            changed = true;
        }

        if (TryReadString(values, "quietHoursEndLocalTime", out var quietHoursEnd))
        {
            preference.QuietHoursEndMinutes = NotificationScheduling.ParseLocalTimeToMinutes(quietHoursEnd);
            changed = true;
        }

        var overrides = ReadStoredEventPreferences(preference.EventOverridesJson);

        if (TryReadBoolean(values, "emailReminders", out var emailReminders))
        {
            changed |= SetEventOverride(
                overrides,
                NotificationCatalog.GetKey(NotificationEventKey.LearnerStudyPlanDueReminder),
                emailEnabled: emailReminders,
                emailMode: emailReminders ? NormalizeEmailMode(NotificationEmailMode.DailyDigest) : NormalizeEmailMode(NotificationEmailMode.Off));
            changed |= SetEventOverride(
                overrides,
                NotificationCatalog.GetKey(NotificationEventKey.LearnerStudyPlanRegenerated),
                emailEnabled: emailReminders,
                emailMode: emailReminders ? NormalizeEmailMode(NotificationEmailMode.DailyDigest) : NormalizeEmailMode(NotificationEmailMode.Off));
        }

        if (TryReadString(values, "reminderCadence", out var reminderCadence))
        {
            var normalizedCadence = reminderCadence.Trim().ToLowerInvariant();
            if (normalizedCadence == "off")
            {
                changed |= SetEventOverride(
                    overrides,
                    NotificationCatalog.GetKey(NotificationEventKey.LearnerStudyPlanDueReminder),
                    emailEnabled: false,
                    emailMode: NormalizeEmailMode(NotificationEmailMode.Off));
            }
            else if (normalizedCadence is "daily" or "weekly")
            {
                changed |= SetEventOverride(
                    overrides,
                    NotificationCatalog.GetKey(NotificationEventKey.LearnerStudyPlanDueReminder),
                    emailEnabled: true,
                    emailMode: NormalizeEmailMode(NotificationEmailMode.DailyDigest));
            }
        }

        if (TryReadBoolean(values, "reviewUpdates", out var reviewUpdates))
        {
            foreach (var reviewEventKey in ReviewUpdateEventKeys)
            {
                changed |= SetEventOverride(
                    overrides,
                    reviewEventKey,
                    inAppEnabled: reviewUpdates,
                    emailEnabled: reviewUpdates,
                    pushEnabled: reviewUpdates);
            }
        }

        if (changed)
        {
            preference.EventOverridesJson = JsonSupport.Serialize(overrides);
        }

        return changed;
    }

    private async Task<string> ResolveDefaultTimezoneAsync(string authAccountId, string audienceRole, CancellationToken ct)
    {
        if (string.Equals(audienceRole, ApplicationUserRoles.Learner, StringComparison.OrdinalIgnoreCase))
        {
            var learnerTimezone = await db.Users
                .AsNoTracking()
                .Where(user => user.AuthAccountId == authAccountId)
                .Select(user => user.Timezone)
                .FirstOrDefaultAsync(ct);

            if (!string.IsNullOrWhiteSpace(learnerTimezone))
            {
                return learnerTimezone;
            }
        }

        if (string.Equals(audienceRole, ApplicationUserRoles.Expert, StringComparison.OrdinalIgnoreCase))
        {
            var expertTimezone = await db.ExpertUsers
                .AsNoTracking()
                .Where(user => user.AuthAccountId == authAccountId)
                .Select(user => user.Timezone)
                .FirstOrDefaultAsync(ct);

            if (!string.IsNullOrWhiteSpace(expertTimezone))
            {
                return expertTimezone;
            }
        }

        return "UTC";
    }

    private static bool HasExplicitPreferenceState(NotificationPreference preference)
    {
        if (!preference.GlobalInAppEnabled
            || !preference.GlobalEmailEnabled
            || !preference.GlobalPushEnabled
            || preference.QuietHoursEnabled
            || preference.QuietHoursStartMinutes.HasValue
            || preference.QuietHoursEndMinutes.HasValue)
        {
            return true;
        }

        var overrides = ReadStoredEventPreferences(preference.EventOverridesJson);
        return overrides.Count > 0;
    }

    private static Dictionary<string, object?> BuildLegacyLearnerSettings(NotificationPreference preference)
    {
        var overrides = ReadStoredEventPreferences(preference.EventOverridesJson);
        var studyPlanReminderKey = NotificationCatalog.GetKey(NotificationEventKey.LearnerStudyPlanDueReminder);
        var studyPlanRegeneratedKey = NotificationCatalog.GetKey(NotificationEventKey.LearnerStudyPlanRegenerated);

        var reminderEmailEnabled =
            ResolveStoredChannelPreference(overrides, studyPlanReminderKey, preference.GlobalEmailEnabled, NotificationCatalog.Get(NotificationEventKey.LearnerStudyPlanDueReminder).DefaultChannels.EmailEnabled)
            || ResolveStoredChannelPreference(overrides, studyPlanRegeneratedKey, preference.GlobalEmailEnabled, NotificationCatalog.Get(NotificationEventKey.LearnerStudyPlanRegenerated).DefaultChannels.EmailEnabled);

        var reviewUpdatesEnabled = ReviewUpdateEventKeys.Any(reviewEventKey =>
            ResolveStoredChannelPreference(overrides, reviewEventKey, preference.GlobalInAppEnabled, NotificationCatalog.Get(Enum.Parse<NotificationEventKey>(reviewEventKey, true)).DefaultChannels.InAppEnabled, NotificationChannel.InApp)
            || ResolveStoredChannelPreference(overrides, reviewEventKey, preference.GlobalEmailEnabled, NotificationCatalog.Get(Enum.Parse<NotificationEventKey>(reviewEventKey, true)).DefaultChannels.EmailEnabled, NotificationChannel.Email)
            || ResolveStoredChannelPreference(overrides, reviewEventKey, preference.GlobalPushEnabled, NotificationCatalog.Get(Enum.Parse<NotificationEventKey>(reviewEventKey, true)).DefaultChannels.PushEnabled, NotificationChannel.Push));

        overrides.TryGetValue(studyPlanReminderKey, out var reminderOverride);
        var reminderMode = ParseEmailMode(reminderOverride?.EmailMode);
        var reminderCadence = !reminderEmailEnabled || reminderMode == NotificationEmailMode.Off ? "off" : "daily";

        return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["emailReminders"] = reminderEmailEnabled,
            ["reviewUpdates"] = reviewUpdatesEnabled,
            ["reminderCadence"] = reminderCadence,
            ["globalInAppEnabled"] = preference.GlobalInAppEnabled,
            ["globalEmailEnabled"] = preference.GlobalEmailEnabled,
            ["globalPushEnabled"] = preference.GlobalPushEnabled,
            ["quietHoursEnabled"] = preference.QuietHoursEnabled,
            ["quietHoursStartLocalTime"] = NotificationScheduling.FormatMinutesAsLocalTime(preference.QuietHoursStartMinutes),
            ["quietHoursEndLocalTime"] = NotificationScheduling.FormatMinutesAsLocalTime(preference.QuietHoursEndMinutes)
        };
    }

    private static bool TryReadBoolean(IReadOnlyDictionary<string, object?> values, string key, out bool value)
    {
        value = default;
        if (!values.TryGetValue(key, out var raw) || raw is null)
        {
            return false;
        }

        switch (raw)
        {
            case bool boolean:
                value = boolean;
                return true;
            case string text when bool.TryParse(text, out var parsedBoolean):
                value = parsedBoolean;
                return true;
            case JsonElement { ValueKind: JsonValueKind.True }:
                value = true;
                return true;
            case JsonElement { ValueKind: JsonValueKind.False }:
                value = false;
                return true;
            case JsonElement { ValueKind: JsonValueKind.String } jsonString when bool.TryParse(jsonString.GetString(), out var parsedJsonBoolean):
                value = parsedJsonBoolean;
                return true;
            case JsonElement { ValueKind: JsonValueKind.Number } number when number.TryGetInt32(out var numericValue):
                value = numericValue != 0;
                return true;
            default:
                return false;
        }
    }

    private static bool TryReadString(IReadOnlyDictionary<string, object?> values, string key, out string value)
    {
        value = string.Empty;
        if (!values.TryGetValue(key, out var raw) || raw is null)
        {
            return false;
        }

        switch (raw)
        {
            case string text:
                value = text;
                return true;
            case JsonElement { ValueKind: JsonValueKind.String } jsonString when !string.IsNullOrWhiteSpace(jsonString.GetString()):
                value = jsonString.GetString()!;
                return true;
            default:
                return false;
        }
    }

    private static bool SetEventOverride(
        IDictionary<string, StoredNotificationEventPreference> overrides,
        string eventKey,
        bool? inAppEnabled = null,
        bool? emailEnabled = null,
        bool? pushEnabled = null,
        string? emailMode = null)
    {
        overrides.TryGetValue(eventKey, out var current);
        var updated = new StoredNotificationEventPreference(
            inAppEnabled ?? current?.InAppEnabled,
            emailEnabled ?? current?.EmailEnabled,
            pushEnabled ?? current?.PushEnabled,
            emailMode ?? current?.EmailMode);

        if (updated.InAppEnabled is null
            && updated.EmailEnabled is null
            && updated.PushEnabled is null
            && updated.EmailMode is null)
        {
            return overrides.Remove(eventKey);
        }

        if (Equals(current, updated))
        {
            return false;
        }

        overrides[eventKey] = updated;
        return true;
    }

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

    private static string NormalizeChannel(string? channel)
        => channel?.Trim().ToLowerInvariant() switch
        {
            "inapp" or "in_app" or "in-app" => "in_app",
            "email" => "email",
            "push" => "push",
            null or "" => "in_app",
            var other => other.Replace('-', '_')
        };

    private static string NormalizeChannel(NotificationChannel channel)
        => channel switch
        {
            NotificationChannel.InApp => "in_app",
            NotificationChannel.Email => "email",
            NotificationChannel.Push => "push",
            _ => throw new ArgumentOutOfRangeException(nameof(channel), channel, "Unsupported notification channel.")
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
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unsupported notification delivery status.")
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

    private async Task<NotificationPolicyDecision> ResolvePolicyDecisionAsync(
        string authAccountId,
        string audienceRole,
        NotificationEventKey eventKey,
        CancellationToken ct)
    {
        var catalog = NotificationCatalog.Get(eventKey);
        var preference = await EnsurePreferenceAsync(authAccountId, audienceRole, ct);
        var eventKeyName = NotificationCatalog.GetKey(eventKey);
        var userOverrides = ReadStoredEventPreferences(preference.EventOverridesJson);
        userOverrides.TryGetValue(eventKeyName, out var userOverride);

        var relevantAdminOverrides = await db.NotificationPolicyOverrides
            .AsNoTracking()
            .Where(overrideRow =>
                overrideRow.AudienceRole == audienceRole
                && (overrideRow.EventKey == NotificationCatalog.GlobalPolicyEventKey || overrideRow.EventKey == eventKeyName))
            .ToListAsync(ct);

        var globalAdminOverride = relevantAdminOverrides.FirstOrDefault(overrideRow =>
            string.Equals(overrideRow.EventKey, NotificationCatalog.GlobalPolicyEventKey, StringComparison.OrdinalIgnoreCase));
        var eventAdminOverride = relevantAdminOverrides.FirstOrDefault(overrideRow =>
            string.Equals(overrideRow.EventKey, eventKeyName, StringComparison.OrdinalIgnoreCase));

        var featureFlags = await db.FeatureFlags
            .AsNoTracking()
            .Where(flag =>
                flag.Key == "notifications-enabled"
                || flag.Key == "notifications-in-app"
                || flag.Key == "notifications-email"
                || flag.Key == "notifications-push")
            .ToListAsync(ct);

        var featureLookup = featureFlags.ToDictionary(flag => flag.Key, flag => flag.Enabled, StringComparer.OrdinalIgnoreCase);
        var notificationsEnabled = GetFeatureFlagState(featureLookup, "notifications-enabled", true);
        var systemInAppEnabled = notificationsEnabled && GetFeatureFlagState(featureLookup, "notifications-in-app", true);
        var systemEmailEnabled = notificationsEnabled && GetFeatureFlagState(featureLookup, "notifications-email", true);
        var systemPushEnabled = notificationsEnabled && GetFeatureFlagState(featureLookup, "notifications-push", true);

        var inAppEnabled = ResolveChannelState(
            systemInAppEnabled,
            globalAdminOverride?.InAppEnabled ?? true,
            eventAdminOverride?.InAppEnabled,
            preference.GlobalInAppEnabled,
            userOverride?.InAppEnabled,
            catalog.DefaultChannels.InAppEnabled);

        var emailEnabled = ResolveChannelState(
            systemEmailEnabled,
            globalAdminOverride?.EmailEnabled ?? true,
            eventAdminOverride?.EmailEnabled,
            preference.GlobalEmailEnabled,
            userOverride?.EmailEnabled,
            catalog.DefaultChannels.EmailEnabled);

        var pushEnabled = ResolveChannelState(
            systemPushEnabled,
            globalAdminOverride?.PushEnabled ?? true,
            eventAdminOverride?.PushEnabled,
            preference.GlobalPushEnabled,
            userOverride?.PushEnabled,
            catalog.DefaultChannels.PushEnabled);

        var emailMode = emailEnabled
            ? ResolveEmailMode(catalog.DefaultChannels.EmailMode, eventAdminOverride?.EmailMode, userOverride?.EmailMode)
            : NotificationEmailMode.Off;

        DateTimeOffset? deferredPushUntilUtc = null;
        if (pushEnabled
            && preference.QuietHoursEnabled
            && NotificationScheduling.IsWithinQuietHours(
                timeProvider.GetUtcNow(),
                preference.Timezone,
                preference.QuietHoursStartMinutes,
                preference.QuietHoursEndMinutes))
        {
            deferredPushUntilUtc = NotificationScheduling.GetNextQuietHoursEndUtc(
                timeProvider.GetUtcNow(),
                preference.Timezone,
                preference.QuietHoursStartMinutes,
                preference.QuietHoursEndMinutes);
        }

        return new NotificationPolicyDecision(
            preference.Timezone,
            preference.QuietHoursEnabled,
            preference.QuietHoursStartMinutes,
            preference.QuietHoursEndMinutes,
            inAppEnabled,
            emailEnabled,
            pushEnabled,
            emailMode,
            deferredPushUntilUtc);
    }

    private async Task<NotificationInboxItem> EnsureInboxItemAsync(
        NotificationEvent notificationEvent,
        NotificationPolicyDecision decision,
        CancellationToken ct)
    {
        var existingItem = await db.NotificationInboxItems
            .FirstOrDefaultAsync(item => item.NotificationEventId == notificationEvent.Id, ct);

        var channels = new List<string>();
        if (decision.InAppEnabled)
        {
            channels.Add(NormalizeChannel(NotificationChannel.InApp));
        }

        if (decision.EmailEnabled)
        {
            channels.Add(NormalizeChannel(NotificationChannel.Email));
        }

        if (decision.PushEnabled)
        {
            channels.Add(NormalizeChannel(NotificationChannel.Push));
        }

        if (existingItem is null)
        {
            existingItem = new NotificationInboxItem
            {
                Id = $"nin-{Guid.NewGuid():N}",
                NotificationEventId = notificationEvent.Id,
                AuthAccountId = notificationEvent.RecipientAuthAccountId,
                EventKey = notificationEvent.EventKey,
                Category = notificationEvent.Category,
                Title = notificationEvent.Title,
                Body = notificationEvent.Body,
                ActionUrl = notificationEvent.ActionUrl,
                Severity = notificationEvent.Severity,
                ChannelsJson = JsonSupport.Serialize(channels.Distinct(StringComparer.OrdinalIgnoreCase).ToArray()),
                CreatedAt = notificationEvent.CreatedAt
            };
            db.NotificationInboxItems.Add(existingItem);
        }
        else
        {
            existingItem.Title = notificationEvent.Title;
            existingItem.Body = notificationEvent.Body;
            existingItem.ActionUrl = notificationEvent.ActionUrl;
            existingItem.Severity = notificationEvent.Severity;
            existingItem.ChannelsJson = JsonSupport.Serialize(channels.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
        }

        await db.SaveChangesAsync(ct);
        return existingItem;
    }

    private async Task PublishRealtimeAsync(string authAccountId, NotificationInboxItem inboxItem, CancellationToken ct)
    {
        var unreadCount = await db.NotificationInboxItems
            .AsNoTracking()
            .CountAsync(item => item.AuthAccountId == authAccountId && !item.IsRead, ct);

        var envelope = new NotificationRealtimeEnvelope("notification.created", MapFeedItem(inboxItem), unreadCount);
        await hubContext.Clients.Group(NotificationHub.AccountGroup(authAccountId)).SendAsync("notification", envelope, ct);
    }

    private async Task SendImmediateEmailAsync(NotificationEvent notificationEvent, CancellationToken ct)
    {
        var emailAlreadySent = await db.NotificationDeliveryAttempts
            .AsNoTracking()
            .AnyAsync(attempt =>
                attempt.NotificationEventId == notificationEvent.Id
                && attempt.Channel == NotificationChannel.Email
                && attempt.Status == NotificationDeliveryStatus.Sent, ct);
        if (emailAlreadySent)
        {
            return;
        }

        var account = await db.ApplicationUserAccounts
            .AsNoTracking()
            .FirstOrDefaultAsync(existingAccount => existingAccount.Id == notificationEvent.RecipientAuthAccountId, ct);

        if (account is null || string.IsNullOrWhiteSpace(account.Email))
        {
            db.NotificationDeliveryAttempts.Add(new NotificationDeliveryAttempt
            {
                Id = $"nda-{Guid.NewGuid():N}",
                NotificationEventId = notificationEvent.Id,
                AuthAccountId = notificationEvent.RecipientAuthAccountId,
                Channel = NotificationChannel.Email,
                Status = NotificationDeliveryStatus.Failed,
                Provider = emailSender.GetType().Name,
                ErrorCode = "recipient_email_missing",
                ErrorMessage = "The notification recipient does not have a deliverable email address.",
                AttemptedAt = timeProvider.GetUtcNow()
            });
            await db.SaveChangesAsync(ct);
            return;
        }

        var absoluteActionUrl = platformLinks.BuildWebUrl(notificationEvent.ActionUrl ?? "/");
        var subject = notificationEvent.Title;
        var textBody = BuildPlainTextEmailBody(subject, notificationEvent.Body, absoluteActionUrl);
        var htmlBody = BuildHtmlEmailBody(subject, notificationEvent.Body, absoluteActionUrl);

        try
        {
            await emailSender.SendAsync(new EmailMessage(account.Email, subject, textBody, htmlBody), ct);
            db.NotificationDeliveryAttempts.Add(new NotificationDeliveryAttempt
            {
                Id = $"nda-{Guid.NewGuid():N}",
                NotificationEventId = notificationEvent.Id,
                AuthAccountId = notificationEvent.RecipientAuthAccountId,
                Channel = NotificationChannel.Email,
                Status = NotificationDeliveryStatus.Sent,
                Provider = emailSender.GetType().Name,
                AttemptedAt = timeProvider.GetUtcNow(),
                CompletedAt = timeProvider.GetUtcNow()
            });
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send notification email for event {NotificationEventId}", notificationEvent.Id);
            db.NotificationDeliveryAttempts.Add(new NotificationDeliveryAttempt
            {
                Id = $"nda-{Guid.NewGuid():N}",
                NotificationEventId = notificationEvent.Id,
                AuthAccountId = notificationEvent.RecipientAuthAccountId,
                Channel = NotificationChannel.Email,
                Status = NotificationDeliveryStatus.Failed,
                Provider = emailSender.GetType().Name,
                ErrorCode = "email_send_failed",
                ErrorMessage = ex.Message,
                AttemptedAt = timeProvider.GetUtcNow()
            });
            await db.SaveChangesAsync(ct);
            throw;
        }
    }

    private async Task EnsureDigestJobAsync(
        NotificationEvent notificationEvent,
        NotificationPolicyDecision decision,
        CancellationToken ct)
    {
        var localDateBucket = NotificationScheduling.GetLocalDateBucket(notificationEvent.CreatedAt, decision.Timezone);
        var existingDigestJobs = await db.BackgroundJobs
            .AsNoTracking()
            .Where(job =>
                job.Type == JobType.NotificationDigestDispatch
                && job.ResourceId == notificationEvent.RecipientAuthAccountId
                && (job.State == AsyncState.Queued || job.State == AsyncState.Processing))
            .ToListAsync(ct);

        foreach (var existingDigestJob in existingDigestJobs)
        {
            var existingPayload = JsonSupport.Deserialize(existingDigestJob.PayloadJson, new NotificationDigestJobPayload(notificationEvent.RecipientAuthAccountId, string.Empty, decision.Timezone));
            if (string.Equals(existingPayload.LocalDateBucket, localDateBucket, StringComparison.Ordinal))
            {
                return;
            }
        }

        var timezoneInfo = NotificationScheduling.ResolveTimeZone(decision.Timezone);
        var eventLocalTime = NotificationScheduling.ConvertToLocalTime(notificationEvent.CreatedAt, decision.Timezone);
        var digestLocalTime = DateOnly.FromDateTime(eventLocalTime.DateTime)
            .AddDays(1)
            .ToDateTime(new TimeOnly(8, 0), DateTimeKind.Unspecified);
        var digestAvailableAt = new DateTimeOffset(digestLocalTime, timezoneInfo.GetUtcOffset(digestLocalTime)).ToUniversalTime();
        if (digestAvailableAt <= timeProvider.GetUtcNow())
        {
            digestAvailableAt = timeProvider.GetUtcNow().AddMinutes(1);
        }

        db.BackgroundJobs.Add(new BackgroundJobItem
        {
            Id = $"job-{Guid.NewGuid():N}",
            Type = JobType.NotificationDigestDispatch,
            State = AsyncState.Queued,
            ResourceId = notificationEvent.RecipientAuthAccountId,
            PayloadJson = JsonSupport.Serialize(new NotificationDigestJobPayload(notificationEvent.RecipientAuthAccountId, localDateBucket, decision.Timezone)),
            CreatedAt = timeProvider.GetUtcNow(),
            AvailableAt = digestAvailableAt,
            LastTransitionAt = timeProvider.GetUtcNow(),
            StatusReasonCode = "queued",
            StatusMessage = $"Notification digest queued for {localDateBucket}.",
            Retryable = true,
            RetryAfterMs = 60000
        });

        await db.SaveChangesAsync(ct);
    }

    private async Task QueueDeferredPushAsync(NotificationEvent notificationEvent, DateTimeOffset deferredUntilUtc, CancellationToken ct)
    {
        var existingDeferredPushJobs = await db.BackgroundJobs
            .AsNoTracking()
            .Where(job =>
                job.Type == JobType.NotificationFanout
                && job.ResourceId == notificationEvent.Id
                && job.State == AsyncState.Queued)
            .ToListAsync(ct);

        if (existingDeferredPushJobs.Any(job => job.AvailableAt >= deferredUntilUtc.AddSeconds(-30)))
        {
            return;
        }

        db.BackgroundJobs.Add(new BackgroundJobItem
        {
            Id = $"job-{Guid.NewGuid():N}",
            Type = JobType.NotificationFanout,
            State = AsyncState.Queued,
            ResourceId = notificationEvent.Id,
            PayloadJson = JsonSupport.Serialize(new { notificationEventId = notificationEvent.Id, deferredPush = true }),
            CreatedAt = timeProvider.GetUtcNow(),
            AvailableAt = deferredUntilUtc,
            LastTransitionAt = timeProvider.GetUtcNow(),
            StatusReasonCode = "quiet_hours_deferred",
            StatusMessage = "Push delivery deferred until quiet hours end.",
            Retryable = true
        });

        await db.SaveChangesAsync(ct);
    }

    private async Task SendPushAsync(NotificationEvent notificationEvent, CancellationToken ct)
    {
        var subscriptions = await db.PushSubscriptions
            .Where(subscription => subscription.AuthAccountId == notificationEvent.RecipientAuthAccountId && subscription.IsActive)
            .ToListAsync(ct);

        if (subscriptions.Count == 0)
        {
            await RecordSuppressedAttemptIfMissingAsync(
                notificationEvent,
                NotificationChannel.Push,
                "push_subscription_missing",
                "Push delivery skipped because the recipient has no active browser push subscriptions.",
                ct);
            return;
        }

        var options = webPushOptions.Value;
        if (!options.Enabled
            || string.IsNullOrWhiteSpace(options.Subject)
            || string.IsNullOrWhiteSpace(options.PublicKey)
            || string.IsNullOrWhiteSpace(options.PrivateKey))
        {
            await RecordSuppressedAttemptIfMissingAsync(
                notificationEvent,
                NotificationChannel.Push,
                "push_not_configured",
                "Push delivery skipped because web push is not configured for this environment.",
                ct);
            return;
        }

        var unreadCount = await db.NotificationInboxItems
            .AsNoTracking()
            .CountAsync(item => item.AuthAccountId == notificationEvent.RecipientAuthAccountId && !item.IsRead, ct);

        var pushPayload = JsonSupport.Serialize(new
        {
            notificationId = notificationEvent.Id,
            eventKey = notificationEvent.EventKey,
            title = notificationEvent.Title,
            body = notificationEvent.Body,
            actionUrl = platformLinks.BuildWebUrl(notificationEvent.ActionUrl ?? "/"),
            severity = NormalizeSeverity(notificationEvent.Severity),
            unreadCount
        });

        var pushFailures = 0;

        foreach (var subscription in subscriptions)
        {
            var subscriptionId = subscription.Id.ToString();
            var subscriptionAlreadySent = await db.NotificationDeliveryAttempts
                .AsNoTracking()
                .AnyAsync(attempt =>
                    attempt.NotificationEventId == notificationEvent.Id
                    && attempt.Channel == NotificationChannel.Push
                    && attempt.SubscriptionId == subscriptionId
                    && attempt.Status == NotificationDeliveryStatus.Sent, ct);
            if (subscriptionAlreadySent)
            {
                continue;
            }

            try
            {
                await webPushDispatcher.SendAsync(subscription, pushPayload, ct);

                subscription.LastSuccessfulAt = timeProvider.GetUtcNow();
                subscription.LastFailureAt = null;
                subscription.FailureReasonCode = null;
                subscription.UpdatedAt = timeProvider.GetUtcNow();

                db.NotificationDeliveryAttempts.Add(new NotificationDeliveryAttempt
                {
                    Id = $"nda-{Guid.NewGuid():N}",
                    NotificationEventId = notificationEvent.Id,
                    AuthAccountId = notificationEvent.RecipientAuthAccountId,
                    Channel = NotificationChannel.Push,
                    SubscriptionId = subscriptionId,
                    Status = NotificationDeliveryStatus.Sent,
                    Provider = "web_push",
                    AttemptedAt = timeProvider.GetUtcNow(),
                    CompletedAt = timeProvider.GetUtcNow(),
                    ResponsePayloadJson = pushPayload
                });
            }
            catch (PushDispatchException ex)
            {
                logger.LogWarning(ex, "Web push delivery failed for event {NotificationEventId} subscription {SubscriptionId}", notificationEvent.Id, subscription.Id);
                var statusCode = ex.StatusCode ?? 0;
                var isExpired = statusCode is 404 or 410;

                subscription.LastFailureAt = timeProvider.GetUtcNow();
                subscription.UpdatedAt = timeProvider.GetUtcNow();
                subscription.FailureReasonCode = isExpired ? "subscription_expired" : "push_send_failed";
                if (isExpired)
                {
                    subscription.IsActive = false;
                }

                db.NotificationDeliveryAttempts.Add(new NotificationDeliveryAttempt
                {
                    Id = $"nda-{Guid.NewGuid():N}",
                    NotificationEventId = notificationEvent.Id,
                    AuthAccountId = notificationEvent.RecipientAuthAccountId,
                    Channel = NotificationChannel.Push,
                    SubscriptionId = subscriptionId,
                    Status = isExpired ? NotificationDeliveryStatus.Expired : NotificationDeliveryStatus.Failed,
                    Provider = "web_push",
                    ErrorCode = isExpired ? "subscription_expired" : "push_send_failed",
                    ErrorMessage = ex.Message,
                    AttemptedAt = timeProvider.GetUtcNow(),
                    ResponsePayloadJson = pushPayload
                });

                if (!isExpired)
                {
                    pushFailures += 1;
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unexpected web push delivery failure for event {NotificationEventId} subscription {SubscriptionId}", notificationEvent.Id, subscription.Id);
                subscription.LastFailureAt = timeProvider.GetUtcNow();
                subscription.UpdatedAt = timeProvider.GetUtcNow();
                subscription.FailureReasonCode = "push_send_failed";

                db.NotificationDeliveryAttempts.Add(new NotificationDeliveryAttempt
                {
                    Id = $"nda-{Guid.NewGuid():N}",
                    NotificationEventId = notificationEvent.Id,
                    AuthAccountId = notificationEvent.RecipientAuthAccountId,
                    Channel = NotificationChannel.Push,
                    SubscriptionId = subscriptionId,
                    Status = NotificationDeliveryStatus.Failed,
                    Provider = "web_push",
                    ErrorCode = "push_send_failed",
                    ErrorMessage = ex.Message,
                    AttemptedAt = timeProvider.GetUtcNow(),
                    ResponsePayloadJson = pushPayload
                });
                pushFailures += 1;
            }
        }

        await db.SaveChangesAsync(ct);

        if (pushFailures > 0)
        {
            throw new InvalidOperationException("One or more web push deliveries failed.");
        }
    }

    private async Task RecordSuppressedAttemptIfMissingAsync(
        NotificationEvent notificationEvent,
        NotificationChannel channel,
        string errorCode,
        string message,
        CancellationToken ct)
    {
        var existingAttempt = await db.NotificationDeliveryAttempts
            .AsNoTracking()
            .AnyAsync(attempt =>
                attempt.NotificationEventId == notificationEvent.Id
                && attempt.Channel == channel
                && attempt.Status == NotificationDeliveryStatus.Suppressed
                && attempt.ErrorCode == errorCode, ct);
        if (existingAttempt)
        {
            return;
        }

        db.NotificationDeliveryAttempts.Add(new NotificationDeliveryAttempt
        {
            Id = $"nda-{Guid.NewGuid():N}",
            NotificationEventId = notificationEvent.Id,
            AuthAccountId = notificationEvent.RecipientAuthAccountId,
            Channel = channel,
            Status = NotificationDeliveryStatus.Suppressed,
            Provider = channel == NotificationChannel.Push ? "web_push" : emailSender.GetType().Name,
            ErrorCode = errorCode,
            ErrorMessage = message,
            AttemptedAt = timeProvider.GetUtcNow()
        });
        await db.SaveChangesAsync(ct);
    }

    private void EnsureProofHarnessEnabled()
    {
        if (environment.IsDevelopment() || notificationProofOptions.Value.Enabled)
        {
            return;
        }

        throw ApiException.NotFound("notification_proof_harness_disabled", "Notification proof harness is not enabled in this environment.");
    }

    private async Task ProcessBackgroundProofJobAsync(
        BackgroundJobItem job,
        Func<BackgroundJobItem, Task> processor,
        CancellationToken ct)
    {
        job.State = AsyncState.Processing;
        job.StatusReasonCode = "processing";
        job.StatusMessage = "Proof harness processing started.";
        job.LastTransitionAt = timeProvider.GetUtcNow();
        await db.SaveChangesAsync(ct);

        try
        {
            await processor(job);
            job.State = AsyncState.Completed;
            job.StatusReasonCode = "completed";
            job.StatusMessage = "Proof harness processing completed.";
            job.LastTransitionAt = timeProvider.GetUtcNow();
        }
        catch (Exception ex)
        {
            job.State = AsyncState.Failed;
            job.StatusReasonCode = "proof_processing_failed";
            job.StatusMessage = ex.Message;
            job.LastTransitionAt = timeProvider.GetUtcNow();
            await db.SaveChangesAsync(ct);
            throw;
        }

        await db.SaveChangesAsync(ct);
    }

    private static Dictionary<string, object?> BuildProofTokens(
        string eventKey,
        string audienceRole,
        IReadOnlyDictionary<string, string?>? overrides)
    {
        var tokens = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in BuildSampleTokens(eventKey, audienceRole))
        {
            tokens[key] = value;
        }

        if (overrides is not null)
        {
            foreach (var (key, value) in overrides)
            {
                tokens[key] = value;
            }
        }

        return tokens;
    }

    private static IReadOnlyDictionary<string, string?> BuildSampleTokens(string eventKey, string audienceRole)
        => new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["attemptId"] = "att-sample-001",
            ["mockAttemptId"] = "mock-sample-001",
            ["reviewRequestId"] = "review-sample-001",
            ["subtest"] = string.Equals(audienceRole, ApplicationUserRoles.Expert, StringComparison.OrdinalIgnoreCase) ? "expert review" : "writing",
            ["itemTitle"] = "Complete your next timed writing task",
            ["dueLabel"] = "tomorrow at 6:00 PM",
            ["message"] = "This is a preview of the notification copy and delivery layout."
        };

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
}
