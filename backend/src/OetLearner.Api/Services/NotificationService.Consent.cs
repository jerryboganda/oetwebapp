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
    public async Task<IReadOnlyList<NotificationConsentItem>> GetConsentsAsync(string authAccountId, CancellationToken ct)
    {
        var consents = await db.NotificationConsents
            .AsNoTracking()
            .Where(consent => consent.AuthAccountId == authAccountId)
            .ToListAsync(ct);

        return BuildConsentItemsForAccount(authAccountId, consents);
    }

    public async Task<NotificationConsentItem> UpdateConsentAsync(
        string authAccountId,
        string channel,
        NotificationConsentUpdateRequest request,
        CancellationToken ct)
    {
        var parsedChannel = ParseChannelFilter(channel);
        return await UpsertNotificationConsentAsync(
            authAccountId,
            parsedChannel,
            NormalizeConsentCategory(request.Category),
            request.IsGranted,
            NormalizeConsentSource(request.Source, "user"),
            request.Reason,
            adminId: null,
            adminName: null,
            ct);
    }

    public async Task<AdminNotificationConsentResponse> GetAdminConsentsAsync(
        int page,
        int pageSize,
        string? authAccountId,
        string? channel,
        CancellationToken ct)
    {
        var normalizedPage = Math.Max(1, page);
        var normalizedPageSize = Math.Clamp(pageSize, 1, MaxPageSize);
        var channelFilter = string.IsNullOrWhiteSpace(channel) ? (NotificationChannel?)null : ParseChannelFilter(channel);
        var normalizedAuthAccountId = string.IsNullOrWhiteSpace(authAccountId) ? null : authAccountId.Trim();

        if (!string.IsNullOrWhiteSpace(normalizedAuthAccountId))
        {
            var accountExists = await db.ApplicationUserAccounts
                .AsNoTracking()
                .AnyAsync(account => account.Id == normalizedAuthAccountId && account.DeletedAt == null, ct);
            if (!accountExists)
            {
                throw ApiException.NotFound("auth_account_not_found", "Notification account not found.");
            }

            var accountConsents = await db.NotificationConsents
                .AsNoTracking()
                .Where(consent => consent.AuthAccountId == normalizedAuthAccountId)
                .ToListAsync(ct);

            var accountItems = BuildConsentItemsForAccount(normalizedAuthAccountId, accountConsents)
                .Where(item => !channelFilter.HasValue || string.Equals(item.Channel, NormalizeChannel(channelFilter.Value), StringComparison.OrdinalIgnoreCase))
                .ToArray();

            return new AdminNotificationConsentResponse(
                accountItems.Skip((normalizedPage - 1) * normalizedPageSize).Take(normalizedPageSize).ToArray(),
                accountItems.Length,
                normalizedPage,
                normalizedPageSize);
        }

        var query = db.NotificationConsents.AsNoTracking().AsQueryable();
        if (channelFilter.HasValue)
        {
            query = query.Where(consent => consent.Channel == channelFilter.Value);
        }

        var totalCount = await query.CountAsync(ct);
        var rows = await query
            .OrderByDescending(consent => consent.UpdatedAt)
            .Skip((normalizedPage - 1) * normalizedPageSize)
            .Take(normalizedPageSize)
            .ToListAsync(ct);

        return new AdminNotificationConsentResponse(rows.Select(MapConsent).ToArray(), totalCount, normalizedPage, normalizedPageSize);
    }

    public async Task<NotificationConsentItem> SetAdminConsentAsync(
        string adminId,
        string adminName,
        string authAccountId,
        string channel,
        NotificationConsentUpdateRequest request,
        CancellationToken ct)
    {
        var accountExists = await db.ApplicationUserAccounts
            .AsNoTracking()
            .AnyAsync(account => account.Id == authAccountId && account.DeletedAt == null, ct);
        if (!accountExists)
        {
            throw ApiException.NotFound("auth_account_not_found", "Notification account not found.");
        }

        var parsedChannel = ParseChannelFilter(channel);
        var item = await UpsertNotificationConsentAsync(
            authAccountId,
            parsedChannel,
            NormalizeConsentCategory(request.Category),
            request.IsGranted,
            NormalizeConsentSource(request.Source, "admin"),
            request.Reason,
            adminId,
            adminName,
            ct,
            saveChanges: false);

        db.AuditEvents.Add(new AuditEvent
        {
            Id = $"AUD-{Guid.NewGuid():N}",
            OccurredAt = timeProvider.GetUtcNow(),
            ActorId = adminId,
            ActorName = adminName,
            Action = "notification_consent_updated",
            ResourceType = "NotificationConsent",
            ResourceId = $"{authAccountId}:{NormalizeChannel(parsedChannel)}",
            Details = $"Set notification consent to {request.IsGranted} for {authAccountId}/{NormalizeChannel(parsedChannel)}/{NormalizeConsentCategory(request.Category)}"
        });
        await db.SaveChangesAsync(ct);

        return item;
    }

    public async Task<AdminNotificationSuppressionResponse> GetAdminSuppressionsAsync(
        int page,
        int pageSize,
        string? authAccountId,
        string? channel,
        bool activeOnly,
        CancellationToken ct)
    {
        var normalizedPage = Math.Max(1, page);
        var normalizedPageSize = Math.Clamp(pageSize, 1, MaxPageSize);
        var channelFilter = string.IsNullOrWhiteSpace(channel) ? (NotificationChannel?)null : ParseChannelFilter(channel);
        var normalizedAuthAccountId = string.IsNullOrWhiteSpace(authAccountId) ? null : authAccountId.Trim();

        var query = db.NotificationSuppressions.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(normalizedAuthAccountId))
        {
            query = query.Where(suppression => suppression.AuthAccountId == normalizedAuthAccountId);
        }

        if (channelFilter.HasValue)
        {
            query = query.Where(suppression => suppression.Channel == channelFilter.Value);
        }

        if (activeOnly)
        {
            query = query.Where(suppression => suppression.IsActive);
        }

        var totalCount = await query.CountAsync(ct);
        var rows = await query
            .OrderByDescending(suppression => suppression.UpdatedAt)
            .Skip((normalizedPage - 1) * normalizedPageSize)
            .Take(normalizedPageSize)
            .ToListAsync(ct);

        return new AdminNotificationSuppressionResponse(rows.Select(MapSuppression).ToArray(), totalCount, normalizedPage, normalizedPageSize);
    }

    public async Task<NotificationSuppressionItem> CreateAdminSuppressionAsync(
        string adminId,
        string adminName,
        AdminNotificationSuppressionCreateRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.AuthAccountId))
        {
            throw ApiException.Validation(
                "auth_account_required",
                "An auth account id is required for notification suppression.",
                [new ApiFieldError("authAccountId", "required", "Provide the account that should be suppressed.")]);
        }

        var authAccountId = request.AuthAccountId.Trim();

        var accountExists = await db.ApplicationUserAccounts
            .AsNoTracking()
            .AnyAsync(account => account.Id == authAccountId && account.DeletedAt == null, ct);
        if (!accountExists)
        {
            throw ApiException.NotFound("auth_account_not_found", "Notification account not found.");
        }

        var parsedChannel = ParseChannelFilter(request.Channel);
        if (parsedChannel == NotificationChannel.InApp)
        {
            throw ApiException.Validation(
                "invalid_notification_suppression_channel",
                "In-app notifications cannot be suppressed because they are the canonical notification record.",
                [new ApiFieldError("channel", "invalid_suppression_channel", "Use email, push, sms, or whatsapp.")]);
        }

        var normalizedEventKey = NormalizeOptionalEventKey(request.EventKey);
        var now = timeProvider.GetUtcNow();
        var startsAt = request.StartsAt ?? now;
        if (request.ExpiresAt.HasValue && request.ExpiresAt.Value <= startsAt)
        {
            throw ApiException.Validation(
                "invalid_suppression_expiry",
                "Notification suppression expiry must be after the start time.",
                [new ApiFieldError("expiresAt", "invalid_range", "Use an expiry after startsAt.")]);
        }

        var suppression = new NotificationSuppression
        {
            Id = Guid.NewGuid(),
            AuthAccountId = authAccountId,
            Channel = parsedChannel,
            EventKey = normalizedEventKey,
            IsActive = true,
            ReasonCode = NormalizeReasonCode(request.ReasonCode),
            Reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim(),
            CreatedByAdminId = adminId,
            CreatedByAdminName = adminName,
            StartsAt = startsAt,
            ExpiresAt = request.ExpiresAt,
            CreatedAt = now,
            UpdatedAt = now
        };

        db.NotificationSuppressions.Add(suppression);
        db.AuditEvents.Add(new AuditEvent
        {
            Id = $"AUD-{Guid.NewGuid():N}",
            OccurredAt = now,
            ActorId = adminId,
            ActorName = adminName,
            Action = "notification_suppression_created",
            ResourceType = "NotificationSuppression",
            ResourceId = suppression.Id.ToString(),
            Details = $"Suppressed {NormalizeChannel(parsedChannel)} notifications for {authAccountId}"
        });

        await db.SaveChangesAsync(ct);
        return MapSuppression(suppression);
    }

    public async Task<NotificationSuppressionItem> ReleaseAdminSuppressionAsync(
        string adminId,
        string adminName,
        Guid suppressionId,
        CancellationToken ct)
    {
        var suppression = await db.NotificationSuppressions
            .FirstOrDefaultAsync(existingSuppression => existingSuppression.Id == suppressionId, ct)
            ?? throw ApiException.NotFound("notification_suppression_not_found", "Notification suppression not found.");

        if (suppression.IsActive)
        {
            suppression.IsActive = false;
            suppression.ReleasedByAdminId = adminId;
            suppression.ReleasedByAdminName = adminName;
            suppression.ReleasedAt = timeProvider.GetUtcNow();
            suppression.UpdatedAt = timeProvider.GetUtcNow();

            db.AuditEvents.Add(new AuditEvent
            {
                Id = $"AUD-{Guid.NewGuid():N}",
                OccurredAt = timeProvider.GetUtcNow(),
                ActorId = adminId,
                ActorName = adminName,
                Action = "notification_suppression_released",
                ResourceType = "NotificationSuppression",
                ResourceId = suppression.Id.ToString(),
                Details = $"Released notification suppression for {suppression.AuthAccountId}/{NormalizeChannel(suppression.Channel)}"
            });

            await db.SaveChangesAsync(ct);
        }

        return MapSuppression(suppression);
    }

    private static IReadOnlyList<NotificationConsentItem> BuildConsentItemsForAccount(
        string authAccountId,
        IReadOnlyCollection<NotificationConsent> consents)
    {
        var consentLookup = consents
            .Where(consent => string.Equals(consent.Category, "global", StringComparison.OrdinalIgnoreCase))
            .GroupBy(consent => consent.Channel)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(consent => consent.UpdatedAt).First());
        var globalItems = ConsentManagedChannels
            .Select(channel => consentLookup.TryGetValue(channel, out var consent)
                ? MapConsent(consent)
                : BuildDefaultConsent(authAccountId, channel))
            .ToArray();

        var categoryItems = consents
            .Where(consent => !string.Equals(consent.Category, "global", StringComparison.OrdinalIgnoreCase))
            .OrderBy(consent => consent.Channel)
            .ThenBy(consent => consent.Category, StringComparer.OrdinalIgnoreCase)
            .Select(MapConsent)
            .ToArray();

        return globalItems.Concat(categoryItems).ToArray();
    }

    private static NotificationConsentItem BuildDefaultConsent(string authAccountId, NotificationChannel channel)
        => new(
            authAccountId,
            NormalizeChannel(channel),
            "global",
            !RequiresExplicitConsent(channel),
            RequiresExplicitConsent(channel),
            "default",
            null,
            !RequiresExplicitConsent(channel) ? DateTimeOffset.UnixEpoch : null,
            null,
            DateTimeOffset.UnixEpoch);

    private static NotificationConsentItem MapConsent(NotificationConsent consent)
        => new(
            consent.AuthAccountId,
            NormalizeChannel(consent.Channel),
            consent.Category,
            consent.IsGranted,
            RequiresExplicitConsent(consent.Channel),
            consent.Source,
            consent.Reason,
            consent.GrantedAt,
            consent.RevokedAt,
            consent.UpdatedAt);

    private static NotificationSuppressionItem MapSuppression(NotificationSuppression suppression)
        => new(
            suppression.Id,
            suppression.AuthAccountId,
            NormalizeChannel(suppression.Channel),
            suppression.EventKey,
            suppression.IsActive,
            suppression.ReasonCode,
            suppression.Reason,
            suppression.StartsAt,
            suppression.ExpiresAt,
            suppression.CreatedAt,
            suppression.UpdatedAt,
            suppression.ReleasedAt,
            suppression.CreatedByAdminName,
            suppression.ReleasedByAdminName);

    private static bool RequiresExplicitConsent(NotificationChannel channel)
        => channel is NotificationChannel.Sms or NotificationChannel.WhatsApp;

    private static bool SuppressionAppliesToEvent(string? suppressionEventKey, string eventKey, string category)
    {
        if (string.IsNullOrWhiteSpace(suppressionEventKey))
        {
            // Legacy global email suppressions must never block OTP / verification / reset.
            return !EmailLanes.IsAuthEvent(eventKey);
        }

        if (string.Equals(suppressionEventKey, eventKey, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(suppressionEventKey, EmailLanes.MarketingSuppressionEventKey, StringComparison.OrdinalIgnoreCase))
        {
            return EmailLanes.IsMarketing(category, eventKey);
        }

        if (string.Equals(suppressionEventKey, EmailLanes.NonAuthSuppressionEventKey, StringComparison.OrdinalIgnoreCase))
        {
            return !EmailLanes.IsAuthEvent(eventKey);
        }

        return false;
    }

    private async Task<NotificationConsentItem> UpsertNotificationConsentAsync(
        string authAccountId,
        NotificationChannel channel,
        string category,
        bool isGranted,
        string source,
        string? reason,
        string? adminId,
        string? adminName,
        CancellationToken ct,
        bool saveChanges = true)
    {
        EnsureConsentManagedChannel(channel);

        var now = timeProvider.GetUtcNow();
        var consent = await db.NotificationConsents
            .FirstOrDefaultAsync(existingConsent =>
                existingConsent.AuthAccountId == authAccountId
                && existingConsent.Channel == channel
                && existingConsent.Category == category, ct);

        if (consent is null)
        {
            consent = new NotificationConsent
            {
                Id = Guid.NewGuid(),
                AuthAccountId = authAccountId,
                Channel = channel,
                Category = category,
                CreatedAt = now
            };
            db.NotificationConsents.Add(consent);
        }

        consent.IsGranted = isGranted;
        consent.Source = source;
        consent.Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        consent.UpdatedByAdminId = adminId;
        consent.UpdatedByAdminName = adminName;
        consent.GrantedAt = isGranted ? now : consent.GrantedAt;
        consent.RevokedAt = isGranted ? null : now;
        consent.UpdatedAt = now;

        if (saveChanges)
        {
            await db.SaveChangesAsync(ct);
        }

        return MapConsent(consent);
    }

    private async Task<NotificationChannelCompliance> ResolveChannelComplianceAsync(
        string authAccountId,
        NotificationChannel channel,
        string eventKey,
        string category,
        CancellationToken ct)
    {
        if (channel == NotificationChannel.InApp)
        {
            return new NotificationChannelCompliance(true);
        }

        var now = timeProvider.GetUtcNow();
        var suppressionCandidates = await db.NotificationSuppressions
            .AsNoTracking()
            .Where(suppression =>
                suppression.AuthAccountId == authAccountId
                && suppression.Channel == channel
                && suppression.IsActive
                && (suppression.EventKey == null
                    || suppression.EventKey == eventKey
                    || suppression.EventKey == EmailLanes.MarketingSuppressionEventKey
                    || suppression.EventKey == EmailLanes.NonAuthSuppressionEventKey))
            .Select(suppression => new
            {
                suppression.EventKey,
                suppression.ReasonCode,
                suppression.Reason,
                suppression.StartsAt,
                suppression.ExpiresAt
            })
            .ToListAsync(ct);

        var activeSuppression = suppressionCandidates
            .Where(suppression =>
                (!suppression.StartsAt.HasValue || suppression.StartsAt <= now)
                && (!suppression.ExpiresAt.HasValue || suppression.ExpiresAt > now)
                && SuppressionAppliesToEvent(suppression.EventKey, eventKey, category))
            .OrderByDescending(suppression => string.Equals(suppression.EventKey, eventKey, StringComparison.OrdinalIgnoreCase))
            .ThenBy(suppression => suppression.ExpiresAt ?? DateTimeOffset.MaxValue)
            .FirstOrDefault();

        if (activeSuppression is not null)
        {
            return new NotificationChannelCompliance(
                false,
                activeSuppression.ReasonCode,
                activeSuppression.Reason ?? $"Notification delivery was suppressed by {activeSuppression.ReasonCode}.");
        }

        if (!RequiresExplicitConsent(channel))
        {
            return new NotificationChannelCompliance(true);
        }

        var consent = await db.NotificationConsents
            .AsNoTracking()
            .Where(existingConsent =>
                existingConsent.AuthAccountId == authAccountId
                && existingConsent.Channel == channel
                && (existingConsent.Category == category || existingConsent.Category == "global"))
            .OrderByDescending(existingConsent => existingConsent.Category == category)
            .FirstOrDefaultAsync(ct);

        return consent?.IsGranted == true
            ? new NotificationChannelCompliance(true)
            : new NotificationChannelCompliance(
                false,
                "explicit_consent_required",
                $"{NormalizeChannel(channel)} delivery requires explicit consent for the {category} category.");
    }

    private async Task<NotificationFrequencyCapCheck> ResolveFrequencyCapAsync(
        NotificationEvent notificationEvent,
        NotificationChannel channel,
        NotificationPolicyDecision decision,
        CancellationToken ct)
    {
        if (channel is not (NotificationChannel.Email or NotificationChannel.Push)
            || (!decision.MaxDeliveriesPerHour.HasValue && !decision.MaxDeliveriesPerDay.HasValue))
        {
            return new NotificationFrequencyCapCheck(true);
        }

        var now = timeProvider.GetUtcNow();
        if (decision.MaxDeliveriesPerHour.HasValue
            && await HasReachedFrequencyCapAsync(notificationEvent, channel, now.AddHours(-1), decision.MaxDeliveriesPerHour.Value, ct))
        {
            return new NotificationFrequencyCapCheck(
                false,
                "frequency_cap_exceeded",
                $"{NormalizeChannel(channel)} delivery skipped because {notificationEvent.EventKey} reached the hourly cap of {decision.MaxDeliveriesPerHour.Value} for this account.");
        }

        if (decision.MaxDeliveriesPerDay.HasValue
            && await HasReachedFrequencyCapAsync(notificationEvent, channel, now.AddDays(-1), decision.MaxDeliveriesPerDay.Value, ct))
        {
            return new NotificationFrequencyCapCheck(
                false,
                "frequency_cap_exceeded",
                $"{NormalizeChannel(channel)} delivery skipped because {notificationEvent.EventKey} reached the daily cap of {decision.MaxDeliveriesPerDay.Value} for this account.");
        }

        return new NotificationFrequencyCapCheck(true);
    }

    private async Task<bool> HasReachedFrequencyCapAsync(
        NotificationEvent notificationEvent,
        NotificationChannel channel,
        DateTimeOffset windowStartUtc,
        int limit,
        CancellationToken ct)
    {
        var deliveredEventCount = await CountFrequencyCapDeliveriesAsync(notificationEvent, channel, windowStartUtc, ct);

        return deliveredEventCount >= limit;
    }

    private async Task<NotificationFrequencyCapCheck> TryReserveFrequencyCapSlotAsync(
        NotificationEvent notificationEvent,
        NotificationChannel channel,
        NotificationPolicyDecision decision,
        IDictionary<NotificationFrequencyCapReservationKey, int> reservations,
        CancellationToken ct)
    {
        if (channel is not (NotificationChannel.Email or NotificationChannel.Push)
            || (!decision.MaxDeliveriesPerHour.HasValue && !decision.MaxDeliveriesPerDay.HasValue))
        {
            return new NotificationFrequencyCapCheck(true);
        }

        var now = timeProvider.GetUtcNow();
        var checks = new List<(NotificationFrequencyCapReservationKey Key, DateTimeOffset WindowStartUtc, int Limit, string WindowLabel)>();
        if (decision.MaxDeliveriesPerHour.HasValue)
        {
            checks.Add((
                new NotificationFrequencyCapReservationKey(notificationEvent.RecipientAuthAccountId, channel, notificationEvent.EventKey, notificationEvent.Category, "hour"),
                now.AddHours(-1),
                decision.MaxDeliveriesPerHour.Value,
                "hourly"));
        }

        if (decision.MaxDeliveriesPerDay.HasValue)
        {
            checks.Add((
                new NotificationFrequencyCapReservationKey(notificationEvent.RecipientAuthAccountId, channel, notificationEvent.EventKey, notificationEvent.Category, "day"),
                now.AddDays(-1),
                decision.MaxDeliveriesPerDay.Value,
                "daily"));
        }

        foreach (var check in checks)
        {
            var persistedCount = await CountFrequencyCapDeliveriesAsync(notificationEvent, channel, check.WindowStartUtc, ct);
            reservations.TryGetValue(check.Key, out var reservedCount);
            if (persistedCount + reservedCount >= check.Limit)
            {
                return new NotificationFrequencyCapCheck(
                    false,
                    "frequency_cap_exceeded",
                    $"{NormalizeChannel(channel)} delivery skipped because {notificationEvent.EventKey} reached the {check.WindowLabel} cap of {check.Limit} for this account.");
            }
        }

        foreach (var check in checks)
        {
            reservations.TryGetValue(check.Key, out var reservedCount);
            reservations[check.Key] = reservedCount + 1;
        }

        return new NotificationFrequencyCapCheck(true);
    }

    private async Task<int> CountFrequencyCapDeliveriesAsync(
        NotificationEvent notificationEvent,
        NotificationChannel channel,
        DateTimeOffset windowStartUtc,
        CancellationToken ct)
    {
        return await db.NotificationDeliveryAttempts
            .AsNoTracking()
            .Where(attempt =>
                attempt.AuthAccountId == notificationEvent.RecipientAuthAccountId
                && attempt.NotificationEventId != notificationEvent.Id
                && attempt.Channel == channel
                && attempt.AttemptedAt >= windowStartUtc
                && FrequencyCapCountedStatuses.Contains(attempt.Status))
            .Join(
                db.NotificationEvents.AsNoTracking().Where(existingEvent =>
                    existingEvent.RecipientAuthAccountId == notificationEvent.RecipientAuthAccountId
                    && existingEvent.EventKey == notificationEvent.EventKey
                    && existingEvent.Category == notificationEvent.Category),
                attempt => attempt.NotificationEventId,
                existingEvent => existingEvent.Id,
                (attempt, _) => attempt.NotificationEventId)
            .Distinct()
            .CountAsync(ct);
    }

    private static string NormalizeConsentCategory(string? category)
    {
        if (string.IsNullOrWhiteSpace(category))
        {
            return "global";
        }

        var normalized = category.Trim().ToLowerInvariant().Replace('-', '_');
        return normalized.Length <= 64 ? normalized : normalized[..64];
    }

    private static void EnsureConsentManagedChannel(NotificationChannel channel)
    {
        if (RequiresExplicitConsent(channel))
        {
            return;
        }

        throw ApiException.Validation(
            "invalid_notification_consent_channel",
            $"Notification consent is only managed for sms and whatsapp channels, not {NormalizeChannel(channel)}.",
            [new ApiFieldError("channel", "invalid_consent_channel", "Use sms or whatsapp.")]);
    }

    private static string NormalizeConsentSource(string? source, string fallback)
    {
        var normalized = string.IsNullOrWhiteSpace(source) ? fallback : source.Trim().ToLowerInvariant().Replace('-', '_');
        return normalized.Length <= 64 ? normalized : normalized[..64];
    }

    private static string NormalizeReasonCode(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "manual_suppression";
        }

        var normalized = new string(value
            .Trim()
            .ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) ? character : '_')
            .ToArray());

        return normalized.Length <= 128 ? normalized : normalized[..128];
    }

    private static string? NormalizeOptionalEventKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!NotificationCatalog.TryParseKey(value, out var eventKey))
        {
            throw ApiException.Validation(
                "invalid_notification_event_key",
                $"Unsupported notification event key '{value}'.",
                [new ApiFieldError("eventKey", "invalid_event_key", "Use an event key returned by the notification catalog.")]);
        }

        return NotificationCatalog.GetKey(eventKey);
    }
}
