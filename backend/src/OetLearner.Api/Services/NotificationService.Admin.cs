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

public sealed partial class NotificationService
{
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

        var globalChannelEnabledByAudience = new Dictionary<string, AdminNotificationAudienceChannelPolicy>(StringComparer.OrdinalIgnoreCase)
        {
            [ApplicationUserRoles.Learner] = ResolveGlobalAudienceChannelPolicy(overrideLookup, ApplicationUserRoles.Learner),
            [ApplicationUserRoles.Expert] = ResolveGlobalAudienceChannelPolicy(overrideLookup, ApplicationUserRoles.Expert),
            [ApplicationUserRoles.Admin] = ResolveGlobalAudienceChannelPolicy(overrideLookup, ApplicationUserRoles.Admin)
        };

        var rows = NotificationCatalog.All
            .Select(entry =>
            {
                overrideLookup.TryGetValue((entry.AudienceRole, NotificationCatalog.GlobalPolicyEventKey), out var globalOverride);
                overrideLookup.TryGetValue((entry.AudienceRole, NotificationCatalog.GetKey(entry.Key)), out var rowOverride);
                var isPolicyProtected = NotificationCatalog.IsPolicyProtected(entry);
                var frequencyCap = ResolveEffectiveFrequencyCap(entry, globalOverride, rowOverride, isPolicyProtected);
                return new AdminNotificationPolicyRow(
                    entry.AudienceRole,
                    NotificationCatalog.GetKey(entry.Key),
                    entry.Category,
                    entry.Label,
                    isPolicyProtected ? entry.DefaultChannels.InAppEnabled : rowOverride?.InAppEnabled ?? entry.DefaultChannels.InAppEnabled,
                    isPolicyProtected ? entry.DefaultChannels.EmailEnabled : rowOverride?.EmailEnabled ?? entry.DefaultChannels.EmailEnabled,
                    isPolicyProtected ? entry.DefaultChannels.PushEnabled : rowOverride?.PushEnabled ?? entry.DefaultChannels.PushEnabled,
                    NormalizeEmailMode(isPolicyProtected ? entry.DefaultChannels.EmailMode : rowOverride?.EmailMode ?? entry.DefaultChannels.EmailMode),
                    frequencyCap.MaxPerHour,
                    frequencyCap.MaxPerDay,
                    isPolicyProtected,
                    rowOverride is not null,
                    rowOverride?.UpdatedAt,
                    rowOverride?.UpdatedByAdminId,
                    rowOverride?.UpdatedByAdminName);
            })
            .OrderBy(row => row.AudienceRole)
            .ThenBy(row => row.Category)
            .ThenBy(row => row.Label)
            .ToArray();

        return new AdminNotificationPoliciesResponse(globalEmailEnabledByAudience, globalChannelEnabledByAudience, rows);
    }

    public async Task<AdminNotificationPolicyRow> UpdateAdminPolicyAsync(
        string adminId,
        string adminName,
        string audienceRole,
        string eventKey,
        AdminNotificationPolicyUpdateRequest request,
        CancellationToken ct)
    {
        audienceRole = NormalizeAudienceRole(audienceRole);

        NotificationCatalogEntry? catalogEntry = null;
        if (string.Equals(eventKey, NotificationCatalog.GlobalPolicyEventKey, StringComparison.OrdinalIgnoreCase))
        {
            eventKey = NotificationCatalog.GlobalPolicyEventKey;
        }
        else
        {
            if (!NotificationCatalog.TryParseKey(eventKey, out var parsedEventKey))
            {
                throw ApiException.Validation(
                    "invalid_notification_event_key",
                    $"Unsupported notification event key '{eventKey}'.",
                    [new ApiFieldError("eventKey", "invalid_event_key", "Use an event key returned by the notification catalog.")]);
            }

            catalogEntry = NotificationCatalog.Get(parsedEventKey);
            eventKey = NotificationCatalog.GetKey(parsedEventKey);
            if (!string.Equals(catalogEntry.AudienceRole, audienceRole, StringComparison.OrdinalIgnoreCase))
            {
                throw ApiException.Validation(
                    "notification_audience_mismatch",
                    $"Event {eventKey} does not belong to audience {audienceRole}.",
                    [new ApiFieldError("audienceRole", "invalid", "The event key does not match the selected audience role.")]);
            }

            EnsureProtectedPolicyRequestIsAllowed(catalogEntry, request);
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

        overrideRow.InAppEnabled = request.InAppEnabled ?? overrideRow.InAppEnabled;
        overrideRow.EmailEnabled = request.EmailEnabled ?? overrideRow.EmailEnabled;
        overrideRow.PushEnabled = request.PushEnabled ?? overrideRow.PushEnabled;
        if (request.EmailMode is not null)
        {
            overrideRow.EmailMode = ParseEmailMode(request.EmailMode);
        }
        if (request.ClearMaxDeliveriesPerHour == true)
        {
            overrideRow.MaxDeliveriesPerHour = null;
        }
        else if (request.MaxDeliveriesPerHour.HasValue)
        {
            overrideRow.MaxDeliveriesPerHour = NormalizeFrequencyCapLimit(request.MaxDeliveriesPerHour.Value, nameof(request.MaxDeliveriesPerHour));
        }
        if (request.ClearMaxDeliveriesPerDay == true)
        {
            overrideRow.MaxDeliveriesPerDay = null;
        }
        else if (request.MaxDeliveriesPerDay.HasValue)
        {
            overrideRow.MaxDeliveriesPerDay = NormalizeFrequencyCapLimit(request.MaxDeliveriesPerDay.Value, nameof(request.MaxDeliveriesPerDay));
        }
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
                "Global Channel Switches",
                overrideRow.InAppEnabled ?? true,
                overrideRow.EmailEnabled ?? true,
                overrideRow.PushEnabled ?? true,
                NormalizeEmailMode(overrideRow.EmailMode ?? NotificationEmailMode.Off),
                overrideRow.MaxDeliveriesPerHour,
                overrideRow.MaxDeliveriesPerDay,
                false,
                true,
                overrideRow.UpdatedAt,
                overrideRow.UpdatedByAdminId,
                overrideRow.UpdatedByAdminName);
        }

        var isProtectedPolicy = NotificationCatalog.IsPolicyProtected(catalogEntry);
        var globalOverride = await db.NotificationPolicyOverrides
            .AsNoTracking()
            .FirstOrDefaultAsync(existingOverride => existingOverride.AudienceRole == audienceRole && existingOverride.EventKey == NotificationCatalog.GlobalPolicyEventKey, ct);
        var frequencyCap = ResolveEffectiveFrequencyCap(catalogEntry, globalOverride, overrideRow, isProtectedPolicy);

        return new AdminNotificationPolicyRow(
            audienceRole,
            eventKey,
            catalogEntry.Category,
            catalogEntry.Label,
            isProtectedPolicy ? catalogEntry.DefaultChannels.InAppEnabled : overrideRow.InAppEnabled ?? catalogEntry.DefaultChannels.InAppEnabled,
            isProtectedPolicy ? catalogEntry.DefaultChannels.EmailEnabled : overrideRow.EmailEnabled ?? catalogEntry.DefaultChannels.EmailEnabled,
            isProtectedPolicy ? catalogEntry.DefaultChannels.PushEnabled : overrideRow.PushEnabled ?? catalogEntry.DefaultChannels.PushEnabled,
            NormalizeEmailMode(isProtectedPolicy ? catalogEntry.DefaultChannels.EmailMode : overrideRow.EmailMode ?? catalogEntry.DefaultChannels.EmailMode),
            frequencyCap.MaxPerHour,
            frequencyCap.MaxPerDay,
            isProtectedPolicy,
            true,
            overrideRow.UpdatedAt,
            overrideRow.UpdatedByAdminId,
            overrideRow.UpdatedByAdminName);
    }

    public async Task<AdminNotificationPolicyRow> ResetAdminPolicyOverrideAsync(
        string adminId,
        string adminName,
        string audienceRole,
        string eventKey,
        CancellationToken ct)
    {
        audienceRole = NormalizeAudienceRole(audienceRole);

        NotificationCatalogEntry? catalogEntry = null;
        if (string.Equals(eventKey, NotificationCatalog.GlobalPolicyEventKey, StringComparison.OrdinalIgnoreCase))
        {
            eventKey = NotificationCatalog.GlobalPolicyEventKey;
        }
        else
        {
            if (!NotificationCatalog.TryParseKey(eventKey, out var parsedEventKey))
            {
                throw ApiException.Validation(
                    "invalid_notification_event_key",
                    $"Unsupported notification event key '{eventKey}'.",
                    [new ApiFieldError("eventKey", "invalid_event_key", "Use an event key returned by the notification catalog.")]);
            }

            catalogEntry = NotificationCatalog.Get(parsedEventKey);
            eventKey = NotificationCatalog.GetKey(parsedEventKey);
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

        if (overrideRow is not null)
        {
            db.NotificationPolicyOverrides.Remove(overrideRow);
            db.AuditEvents.Add(new AuditEvent
            {
                Id = $"AUD-{Guid.NewGuid():N}",
                OccurredAt = timeProvider.GetUtcNow(),
                ActorId = adminId,
                ActorName = adminName,
                Action = "notification_policy_reset",
                ResourceType = "NotificationPolicy",
                ResourceId = $"{audienceRole}:{eventKey}",
                Details = $"Reset notification policy for {audienceRole}/{eventKey} to catalog defaults"
            });

            await db.SaveChangesAsync(ct);
        }

        if (catalogEntry is null)
        {
            return new AdminNotificationPolicyRow(
                audienceRole,
                eventKey,
                "global",
                "Global Channel Switches",
                true,
                true,
                true,
                NormalizeEmailMode(NotificationEmailMode.Off),
                null,
                null,
                false,
                false,
                null,
                null,
                null);
        }

            var isPolicyProtected = NotificationCatalog.IsPolicyProtected(catalogEntry);
            var globalOverride = await db.NotificationPolicyOverrides
                .AsNoTracking()
                .FirstOrDefaultAsync(existingOverride => existingOverride.AudienceRole == audienceRole && existingOverride.EventKey == NotificationCatalog.GlobalPolicyEventKey, ct);
            var frequencyCap = ResolveEffectiveFrequencyCap(catalogEntry, globalOverride, null, isPolicyProtected);

        return new AdminNotificationPolicyRow(
            audienceRole,
            eventKey,
            catalogEntry.Category,
            catalogEntry.Label,
            catalogEntry.DefaultChannels.InAppEnabled,
            catalogEntry.DefaultChannels.EmailEnabled,
            catalogEntry.DefaultChannels.PushEnabled,
            NormalizeEmailMode(catalogEntry.DefaultChannels.EmailMode),
            frequencyCap.MaxPerHour,
            frequencyCap.MaxPerDay,
            isPolicyProtected,
            false,
            null,
            null,
            null);
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

    public async Task<NotificationDeliveryAttemptResponse> GetAdminDeliveriesAsync(
        int page,
        int pageSize,
        string? status,
        string? channel,
        string? audienceRole,
        string? eventKey,
        CancellationToken ct)
    {
        var normalizedPage = Math.Max(1, page);
        var normalizedPageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var statusFilter = string.IsNullOrWhiteSpace(status) ? (NotificationDeliveryStatus?)null : ParseDeliveryStatusFilter(status);
        var channelFilter = string.IsNullOrWhiteSpace(channel) ? (NotificationChannel?)null : ParseChannelFilter(channel);
        var normalizedAudienceRole = string.IsNullOrWhiteSpace(audienceRole) ? null : NormalizeAudienceRole(audienceRole);

        string? normalizedEventKey = null;
        if (!string.IsNullOrWhiteSpace(eventKey))
        {
            if (!NotificationCatalog.TryParseKey(eventKey, out var parsedEventKey))
            {
                throw ApiException.Validation(
                    "invalid_notification_event_key",
                    $"Unsupported notification event key '{eventKey}'.",
                    [new ApiFieldError("eventKey", "invalid_event_key", "Use an event key returned by the notification catalog.")]);
            }

            normalizedEventKey = NotificationCatalog.GetKey(parsedEventKey);
        }

        var baseQuery = db.NotificationDeliveryAttempts
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
                });

        if (statusFilter.HasValue)
        {
            baseQuery = baseQuery.Where(item => item.Status == statusFilter.Value);
        }

        if (channelFilter.HasValue)
        {
            baseQuery = baseQuery.Where(item => item.Channel == channelFilter.Value);
        }

        if (!string.IsNullOrWhiteSpace(normalizedAudienceRole))
        {
            baseQuery = baseQuery.Where(item => item.RecipientRole == normalizedAudienceRole);
        }

        if (!string.IsNullOrWhiteSpace(normalizedEventKey))
        {
            baseQuery = baseQuery.Where(item => item.EventKey == normalizedEventKey);
        }

        var totalCount = await baseQuery.CountAsync(ct);
        var itemRows = await baseQuery
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

        var catalogEntry = NotificationCatalog.Get(eventKey);
        await emailSender.SendAsync(
            new EmailMessage(
                request.RecipientEmail,
                subject,
                BuildPlainTextEmailBody(subject, body, actionUrl),
                BuildHtmlEmailBody(subject, body, actionUrl),
                Category: catalogEntry.Category,
                EventKey: request.EventKey),
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

    /// <summary>
    /// Sends an already-rendered message to one recipient. Used by the admin
    /// notification-template test-send, where the subject and body come from the
    /// stored template rather than the notification catalog, so
    /// <see cref="SendTestEmailAsync"/> (which requires a catalog event key) does
    /// not apply. Propagates provider failures — callers must not report success
    /// when the mail provider is disabled, misconfigured or unreachable.
    /// </summary>
    public Task SendRenderedEmailAsync(
        string recipientEmail,
        string subject,
        string textBody,
        string? htmlBody,
        CancellationToken ct)
        => emailSender.SendAsync(new EmailMessage(recipientEmail, subject, textBody, htmlBody), ct);

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
            throw;
        }
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
            ["subtest"] = string.Equals(audienceRole, ApplicationUserRoles.Expert, StringComparison.OrdinalIgnoreCase) ? "tutor review" : "writing",
            ["itemTitle"] = "Complete your next timed writing task",
            ["dueLabel"] = "tomorrow at 6:00 PM",
            ["message"] = "This is a preview of the notification copy and delivery layout."
        };
}
