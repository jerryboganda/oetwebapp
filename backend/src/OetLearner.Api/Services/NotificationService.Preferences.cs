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
                if (!NotificationCatalog.TryParseKey(eventKey, out var parsedEventKey))
                {
                    throw ApiException.Validation(
                        "invalid_notification_event_key",
                        $"Unsupported notification event key '{eventKey}'.",
                        [new ApiFieldError("eventPreferences", "invalid_event_key", "Provide a supported event key from the notification catalog.")]);
                }

                var catalogEntry = NotificationCatalog.Get(parsedEventKey);
                EnsureProtectedPreferenceRequestIsAllowed(catalogEntry, overridePayload);
                var canonicalEventKey = NotificationCatalog.GetKey(parsedEventKey);

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
                    overrides.Remove(canonicalEventKey);
                }
                else
                {
                    overrides[canonicalEventKey] = updatedOverride;
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
        => _ = NormalizeAudienceRole(audienceRole);

    private static string NormalizeAudienceRole(string audienceRole)
    {
        if (string.Equals(audienceRole, ApplicationUserRoles.Learner, StringComparison.OrdinalIgnoreCase))
        {
            return ApplicationUserRoles.Learner;
        }

        if (string.Equals(audienceRole, ApplicationUserRoles.Expert, StringComparison.OrdinalIgnoreCase))
        {
            return ApplicationUserRoles.Expert;
        }

        if (string.Equals(audienceRole, ApplicationUserRoles.Admin, StringComparison.OrdinalIgnoreCase))
        {
            return ApplicationUserRoles.Admin;
        }

        throw ApiException.Validation(
            "invalid_notification_audience",
            $"Unsupported audience role '{audienceRole}'.",
            [new ApiFieldError("audienceRole", "invalid_audience_role", "Use learner, expert, or admin.")]);
    }

    private static void EnsureProtectedPolicyRequestIsAllowed(
        NotificationCatalogEntry catalogEntry,
        AdminNotificationPolicyUpdateRequest request)
    {
        if (!NotificationCatalog.IsPolicyProtected(catalogEntry))
        {
            return;
        }

        var disablesRequiredChannel =
            (catalogEntry.DefaultChannels.InAppEnabled && request.InAppEnabled == false)
            || (catalogEntry.DefaultChannels.EmailEnabled && request.EmailEnabled == false)
            || (catalogEntry.DefaultChannels.PushEnabled && request.PushEnabled == false);
        var disablesRequiredEmail = catalogEntry.DefaultChannels.EmailEnabled
            && ParseEmailMode(request.EmailMode) == NotificationEmailMode.Off;
        var addsFrequencyCap = (request.MaxDeliveriesPerHour.HasValue && request.MaxDeliveriesPerHour.Value > 0)
            || (request.MaxDeliveriesPerDay.HasValue && request.MaxDeliveriesPerDay.Value > 0);

        if (!disablesRequiredChannel && !disablesRequiredEmail && !addsFrequencyCap)
        {
            return;
        }

        throw ApiException.Validation(
            "protected_notification_policy",
            $"{NotificationCatalog.GetKey(catalogEntry.Key)} is a protected notification and cannot be disabled or frequency-capped.",
            [new ApiFieldError("eventKey", "protected_policy", "Critical, security, verification, and payment receipt notifications cannot be disabled or capped by policy overrides.")]);
    }

    private static void EnsureProtectedPreferenceRequestIsAllowed(
        NotificationCatalogEntry catalogEntry,
        NotificationEventPreferencePayload request)
    {
        if (!NotificationCatalog.IsPolicyProtected(catalogEntry))
        {
            return;
        }

        var disablesRequiredChannel =
            (catalogEntry.DefaultChannels.InAppEnabled && request.InAppEnabled == false)
            || (catalogEntry.DefaultChannels.EmailEnabled && request.EmailEnabled == false)
            || (catalogEntry.DefaultChannels.PushEnabled && request.PushEnabled == false);
        var disablesRequiredEmail = catalogEntry.DefaultChannels.EmailEnabled
            && ParseEmailMode(request.EmailMode) == NotificationEmailMode.Off;

        if (!disablesRequiredChannel && !disablesRequiredEmail)
        {
            return;
        }

        throw ApiException.Validation(
            "protected_notification_preference",
            $"{NotificationCatalog.GetKey(catalogEntry.Key)} is a protected notification and cannot be disabled.",
            [new ApiFieldError("eventPreferences", "protected_policy", "Critical, security, verification, and payment receipt notifications cannot be disabled.")]);
    }

    private static bool ResolveGlobalAudienceEmailEnabled(
        IReadOnlyDictionary<(string AudienceRole, string EventKey), NotificationPolicyOverride> overrideLookup,
        string audienceRole)
        => overrideLookup.TryGetValue((audienceRole, NotificationCatalog.GlobalPolicyEventKey), out var globalOverride)
            ? globalOverride.EmailEnabled ?? true
            : true;

    private static AdminNotificationAudienceChannelPolicy ResolveGlobalAudienceChannelPolicy(
        IReadOnlyDictionary<(string AudienceRole, string EventKey), NotificationPolicyOverride> overrideLookup,
        string audienceRole)
        => overrideLookup.TryGetValue((audienceRole, NotificationCatalog.GlobalPolicyEventKey), out var globalOverride)
            ? new AdminNotificationAudienceChannelPolicy(
                globalOverride.InAppEnabled ?? true,
                globalOverride.EmailEnabled ?? true,
                globalOverride.PushEnabled ?? true)
            : new AdminNotificationAudienceChannelPolicy(true, true, true);

    private static (int? MaxPerHour, int? MaxPerDay) ResolveEffectiveFrequencyCap(
        NotificationCatalogEntry catalogEntry,
        NotificationPolicyOverride? globalOverride,
        NotificationPolicyOverride? eventOverride,
        bool isPolicyProtected)
    {
        if (isPolicyProtected)
        {
            return (null, null);
        }

        var defaultCap = ResolveDefaultFrequencyCap(catalogEntry);
        return (
            ResolveFrequencyCapLimit(eventOverride?.MaxDeliveriesPerHour, globalOverride?.MaxDeliveriesPerHour, defaultCap.MaxPerHour),
            ResolveFrequencyCapLimit(eventOverride?.MaxDeliveriesPerDay, globalOverride?.MaxDeliveriesPerDay, defaultCap.MaxPerDay));
    }

    private static int? ResolveFrequencyCapLimit(int? eventLimit, int? globalLimit, int? defaultLimit)
    {
        if (eventLimit.HasValue)
        {
            return eventLimit.Value <= 0 ? null : eventLimit.Value;
        }

        if (globalLimit.HasValue)
        {
            return globalLimit.Value <= 0 ? null : globalLimit.Value;
        }

        return defaultLimit;
    }

    private static (int? MaxPerHour, int? MaxPerDay) ResolveDefaultFrequencyCap(NotificationCatalogEntry catalogEntry)
    {
        if (NotificationCatalog.IsPolicyProtected(catalogEntry))
        {
            return (null, null);
        }

        return catalogEntry.Category.ToLowerInvariant() switch
        {
            "marketing" => (1, 3),
            "engagement" => (2, 6),
            "learning" or "reminders" or "study_plan" => (3, 8),
            _ => (null, null)
        };
    }

    private static int NormalizeFrequencyCapLimit(int value, string fieldName)
    {
        if (value is < 0 or > MaxFrequencyCapLimit)
        {
            throw ApiException.Validation(
                "invalid_notification_frequency_cap",
            $"Notification frequency caps must be between 0 and {MaxFrequencyCapLimit} deliveries.",
            [new ApiFieldError(fieldName, "invalid_frequency_cap", $"Use 0 for no cap, or a value between 1 and {MaxFrequencyCapLimit}.")]);
        }

        return value;
    }

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
}
