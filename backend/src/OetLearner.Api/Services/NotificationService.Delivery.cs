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
            var emailFrequencyCap = await ResolveFrequencyCapAsync(notificationEvent, NotificationChannel.Email, decision, ct);
            if (!emailFrequencyCap.IsAllowed)
            {
                await RecordSuppressedAttemptIfMissingAsync(
                    notificationEvent,
                    NotificationChannel.Email,
                    emailFrequencyCap.ReasonCode ?? "frequency_cap_exceeded",
                    emailFrequencyCap.Message ?? "Email delivery skipped because a notification frequency cap was reached.",
                    ct);
            }
            else if (decision.EmailMode == NotificationEmailMode.Immediate)
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
            await RecordSuppressedAttemptIfMissingAsync(
                notificationEvent,
                NotificationChannel.Email,
                decision.EmailDisabledReasonCode ?? "email_disabled",
                decision.EmailDisabledMessage ?? "Email delivery was disabled by policy or user preference.",
                ct);
        }

        if (decision.PushEnabled)
        {
            var pushFrequencyCap = await ResolveFrequencyCapAsync(notificationEvent, NotificationChannel.Push, decision, ct);
            if (!pushFrequencyCap.IsAllowed)
            {
                await RecordSuppressedAttemptIfMissingAsync(
                    notificationEvent,
                    NotificationChannel.Push,
                    pushFrequencyCap.ReasonCode ?? "frequency_cap_exceeded",
                    pushFrequencyCap.Message ?? "Push delivery skipped because a notification frequency cap was reached.",
                    ct);
            }
            else if (decision.DeferredPushUntilUtc.HasValue)
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
            await RecordSuppressedAttemptIfMissingAsync(
                notificationEvent,
                NotificationChannel.Push,
                decision.PushDisabledReasonCode ?? "push_disabled",
                decision.PushDisabledMessage ?? "Push delivery was disabled by policy or user preference.",
                ct);
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
        var digestFrequencyCapReservations = new Dictionary<NotificationFrequencyCapReservationKey, int>();
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
            if (emailAlreadySent)
            {
                continue;
            }

            var emailFrequencyCap = await TryReserveFrequencyCapSlotAsync(notificationEvent, NotificationChannel.Email, decision, digestFrequencyCapReservations, ct);
            if (!emailFrequencyCap.IsAllowed)
            {
                await RecordSuppressedAttemptIfMissingAsync(
                    notificationEvent,
                    NotificationChannel.Email,
                    emailFrequencyCap.ReasonCode ?? "frequency_cap_exceeded",
                    emailFrequencyCap.Message ?? "Email digest delivery skipped because a notification frequency cap was reached.",
                    ct);
                continue;
            }

            digestEvents.Add(notificationEvent);
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
            await emailSender.SendAsync(new EmailMessage(
                account.Email,
                subject,
                textBody,
                htmlBody,
                Category: "product",
                EventKey: "daily_digest"), ct);
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
        var isPolicyProtected = NotificationCatalog.IsPolicyProtected(catalog);

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

        if (isPolicyProtected)
        {
            inAppEnabled = systemInAppEnabled && catalog.DefaultChannels.InAppEnabled;
            emailEnabled = systemEmailEnabled && catalog.DefaultChannels.EmailEnabled;
            pushEnabled = systemPushEnabled && catalog.DefaultChannels.PushEnabled;
        }

        string? emailDisabledReasonCode = null;
        string? emailDisabledMessage = null;
        string? pushDisabledReasonCode = null;
        string? pushDisabledMessage = null;

        if (inAppEnabled)
        {
            inAppEnabled = (await ResolveChannelComplianceAsync(authAccountId, NotificationChannel.InApp, eventKeyName, catalog.Category, ct)).IsEnabled;
        }

        if (emailEnabled)
        {
            var emailCompliance = await ResolveChannelComplianceAsync(authAccountId, NotificationChannel.Email, eventKeyName, catalog.Category, ct);
            emailEnabled = emailCompliance.IsEnabled;
            if (!emailCompliance.IsEnabled)
            {
                emailDisabledReasonCode = emailCompliance.DisabledReasonCode;
                emailDisabledMessage = emailCompliance.DisabledMessage;
            }
        }

        if (pushEnabled)
        {
            var pushCompliance = await ResolveChannelComplianceAsync(authAccountId, NotificationChannel.Push, eventKeyName, catalog.Category, ct);
            pushEnabled = pushCompliance.IsEnabled;
            if (!pushCompliance.IsEnabled)
            {
                pushDisabledReasonCode = pushCompliance.DisabledReasonCode;
                pushDisabledMessage = pushCompliance.DisabledMessage;
            }
        }

        var emailMode = emailEnabled
            ? isPolicyProtected
                ? catalog.DefaultChannels.EmailMode
                : ResolveEmailMode(catalog.DefaultChannels.EmailMode, eventAdminOverride?.EmailMode, userOverride?.EmailMode)
            : NotificationEmailMode.Off;
        var frequencyCap = ResolveEffectiveFrequencyCap(catalog, globalAdminOverride, eventAdminOverride, isPolicyProtected);

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
            frequencyCap.MaxPerHour,
            frequencyCap.MaxPerDay,
            deferredPushUntilUtc,
            emailDisabledReasonCode,
            emailDisabledMessage,
            pushDisabledReasonCode,
            pushDisabledMessage);
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

        var channelsJson = JsonSupport.Serialize(channels.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());

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
                ChannelsJson = channelsJson,
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
            existingItem.ChannelsJson = channelsJson;
        }

        // P0-M 2026-05 hardening: fix the race window where two concurrent
        // ProcessFanoutAsync calls for the same NotificationEventId both
        // miss the FirstOrDefaultAsync above, both stage an Add, and the
        // second SaveChangesAsync hits the unique-index violation
        // (5076 jobs queued up in this state on production).
        // Strategy: catch the DbUpdateException, detach our duplicate
        // insert, reload the row the peer wrote, and apply the same
        // channel/title/body updates so the caller observes the merged
        // state.
        try
        {
            await db.SaveChangesAsync(ct);
            return existingItem;
        }
        catch (DbUpdateException) when (existingItem.Id.StartsWith("nin-", StringComparison.Ordinal)
                                        && db.Entry(existingItem).State == EntityState.Added)
        {
            db.Entry(existingItem).State = EntityState.Detached;
            var winner = await db.NotificationInboxItems
                .FirstOrDefaultAsync(item => item.NotificationEventId == notificationEvent.Id, ct);
            if (winner is null)
            {
                // Should be unreachable - the unique violation implies a row exists.
                throw new InvalidOperationException(
                    $"NotificationInboxItem race detected for event {notificationEvent.Id} but winner row not found.");
            }
            winner.Title = notificationEvent.Title;
            winner.Body = notificationEvent.Body;
            winner.ActionUrl = notificationEvent.ActionUrl;
            winner.Severity = notificationEvent.Severity;
            winner.ChannelsJson = channelsJson;
            await db.SaveChangesAsync(ct);
            return winner;
        }
    }

    private async Task PublishRealtimeAsync(string authAccountId, NotificationInboxItem inboxItem, CancellationToken ct)
    {
        var unreadCount = await db.NotificationInboxItems
            .AsNoTracking()
            .CountAsync(item => item.AuthAccountId == authAccountId && !item.IsRead, ct);

        var envelope = new NotificationRealtimeEnvelope("notification.created", MapFeedItem(inboxItem), unreadCount);

        // A SignalR send failure (serialization error, hub/backplane hiccup) must not
        // abort the rest of ProcessFanoutAsync — email and push delivery for this same
        // event run after this call and would otherwise be skipped too. The inbox item
        // itself is already committed (EnsureInboxItemAsync), so the learner can still
        // see it via the REST feed/polling even if this realtime push fails outright.
        try
        {
            await hubContext.Clients.Group(NotificationHub.AccountGroup(authAccountId)).SendAsync("notification", envelope, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Realtime SignalR push failed for account {AuthAccountId}; continuing with email/mobile push delivery.", authAccountId);
        }
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
            await emailSender.SendAsync(new EmailMessage(
                account.Email,
                subject,
                textBody,
                htmlBody,
                Category: notificationEvent.Category,
                EventKey: notificationEvent.EventKey), ct);
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
        var mobileTokens = await db.MobilePushTokens
            .Where(token => token.AuthAccountId == notificationEvent.RecipientAuthAccountId && token.IsActive && token.Platform != "web")
            .ToListAsync(ct);

        if (subscriptions.Count == 0 && mobileTokens.Count == 0)
        {
            await RecordSuppressedAttemptIfMissingAsync(
                notificationEvent,
                NotificationChannel.Push,
                "push_subscription_missing",
                "Push delivery skipped because the recipient has no active browser or native push registrations.",
                ct);
            return;
        }

        var options = webPushOptions.Value;
        var pushSettings = (await runtimeSettingsProvider.GetAsync(ct)).Push;
        var vapidSubject = Coalesce(pushSettings.VapidSubject, options.Subject);
        var vapidPublicKey = Coalesce(pushSettings.VapidPublicKey, options.PublicKey);
        var vapidPrivateKey = Coalesce(pushSettings.VapidPrivateKey, options.PrivateKey);
        var browserPushConfigured = options.Enabled
            && !string.IsNullOrWhiteSpace(vapidSubject)
            && !string.IsNullOrWhiteSpace(vapidPublicKey)
            && !string.IsNullOrWhiteSpace(vapidPrivateKey);
        var fcmConfigured = pushSettings.IsFcmConfigured;
        var apnsConfigured = pushSettings.IsApnsConfigured;
        var hasConfiguredTarget =
            (subscriptions.Count > 0 && browserPushConfigured)
            || mobileTokens.Any(token => token.Platform == "android" && fcmConfigured)
            || mobileTokens.Any(token => token.Platform == "ios" && apnsConfigured);

        if (!hasConfiguredTarget)
        {
            await RecordSuppressedAttemptIfMissingAsync(
                notificationEvent,
                NotificationChannel.Push,
                "push_not_configured",
                "Push delivery skipped because no configured browser, FCM, or APNs target matched the recipient registrations.",
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
        var mobilePayload = new MobilePushPayload(
            notificationEvent.Id,
            notificationEvent.EventKey,
            notificationEvent.Title,
            notificationEvent.Body,
            platformLinks.BuildWebUrl(notificationEvent.ActionUrl ?? "/"),
            NormalizeSeverity(notificationEvent.Severity),
            unreadCount);

        var pushFailures = 0;

        foreach (var subscription in browserPushConfigured
            ? subscriptions
            : Enumerable.Empty<OetLearner.Api.Domain.PushSubscription>())
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

        foreach (var token in mobileTokens)
        {
            if ((token.Platform == "android" && !fcmConfigured)
                || (token.Platform == "ios" && !apnsConfigured))
            {
                continue;
            }

            var subscriptionId = $"mobile:{token.Id}";
            var tokenAlreadySent = await db.NotificationDeliveryAttempts
                .AsNoTracking()
                .AnyAsync(attempt =>
                    attempt.NotificationEventId == notificationEvent.Id
                    && attempt.Channel == NotificationChannel.Push
                    && attempt.SubscriptionId == subscriptionId
                    && attempt.Status == NotificationDeliveryStatus.Sent, ct);
            if (tokenAlreadySent)
            {
                continue;
            }

            try
            {
                await mobilePushDispatcher.SendAsync(token, mobilePayload, ct);
                token.UpdatedAt = timeProvider.GetUtcNow();

                db.NotificationDeliveryAttempts.Add(new NotificationDeliveryAttempt
                {
                    Id = $"nda-{Guid.NewGuid():N}",
                    NotificationEventId = notificationEvent.Id,
                    AuthAccountId = notificationEvent.RecipientAuthAccountId,
                    Channel = NotificationChannel.Push,
                    SubscriptionId = subscriptionId,
                    Status = NotificationDeliveryStatus.Sent,
                    Provider = token.Platform == "ios" ? "apns" : "fcm",
                    AttemptedAt = timeProvider.GetUtcNow(),
                    CompletedAt = timeProvider.GetUtcNow(),
                    ResponsePayloadJson = pushPayload
                });
            }
            catch (MobilePushDispatchException ex)
            {
                logger.LogWarning(ex, "Native push delivery failed for event {NotificationEventId} token {TokenId}", notificationEvent.Id, token.Id);
                var statusCode = ex.StatusCode ?? 0;
                var isExpired = statusCode is 404 or 410;
                token.UpdatedAt = timeProvider.GetUtcNow();
                if (isExpired)
                {
                    token.IsActive = false;
                }

                db.NotificationDeliveryAttempts.Add(new NotificationDeliveryAttempt
                {
                    Id = $"nda-{Guid.NewGuid():N}",
                    NotificationEventId = notificationEvent.Id,
                    AuthAccountId = notificationEvent.RecipientAuthAccountId,
                    Channel = NotificationChannel.Push,
                    SubscriptionId = subscriptionId,
                    Status = isExpired ? NotificationDeliveryStatus.Expired : NotificationDeliveryStatus.Failed,
                    Provider = token.Platform == "ios" ? "apns" : "fcm",
                    ErrorCode = isExpired ? "native_push_token_expired" : "native_push_send_failed",
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
                logger.LogError(ex, "Unexpected native push delivery failure for event {NotificationEventId} token {TokenId}", notificationEvent.Id, token.Id);
                token.UpdatedAt = timeProvider.GetUtcNow();
                db.NotificationDeliveryAttempts.Add(new NotificationDeliveryAttempt
                {
                    Id = $"nda-{Guid.NewGuid():N}",
                    NotificationEventId = notificationEvent.Id,
                    AuthAccountId = notificationEvent.RecipientAuthAccountId,
                    Channel = NotificationChannel.Push,
                    SubscriptionId = subscriptionId,
                    Status = NotificationDeliveryStatus.Failed,
                    Provider = token.Platform == "ios" ? "apns" : "fcm",
                    ErrorCode = "native_push_send_failed",
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
            throw new InvalidOperationException("One or more push deliveries failed.");
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
            Provider = ResolveProviderName(channel, emailSender.GetType().Name),
            ErrorCode = errorCode,
            ErrorMessage = message,
            AttemptedAt = timeProvider.GetUtcNow()
        });
        await db.SaveChangesAsync(ct);
    }

    private static string ResolveProviderName(NotificationChannel channel, string emailProviderName)
        => channel switch
        {
            NotificationChannel.InApp => "in_app",
            NotificationChannel.Email => emailProviderName,
            NotificationChannel.Push => "web_push",
            NotificationChannel.Sms => "sms",
            NotificationChannel.WhatsApp => "whatsapp",
            _ => "notification"
        };
}
