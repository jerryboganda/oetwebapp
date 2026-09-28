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
    public async Task<int> HandleBrevoWebhookEventsAsync(string rawPayload, string? providedSecret, CancellationToken ct)
    {
        var settings = await runtimeSettingsProvider.GetAsync(ct);
        var configuredSecret = settings.Email.BrevoWebhookSecret;
        if (string.IsNullOrWhiteSpace(configuredSecret) || !FixedTimeSecretEquals(configuredSecret, providedSecret ?? string.Empty))
        {
            throw ApiException.Unauthorized("invalid_webhook_secret", "The Brevo webhook secret is missing or incorrect.");
        }

        var events = ParseBrevoWebhookEvents(rawPayload);
        var now = timeProvider.GetUtcNow();
        var suppressedCount = 0;

        foreach (var webhookEvent in events)
        {
            if (string.IsNullOrWhiteSpace(webhookEvent.Email) || string.IsNullOrWhiteSpace(webhookEvent.Event)
                || !BrevoTrackedEvents.Contains(webhookEvent.Event))
            {
                continue;
            }

            string normalizedEmail;
            try
            {
                normalizedEmail = AuthEmailAddress.NormalizeOrThrow(webhookEvent.Email);
            }
            catch (ApiException)
            {
                continue;
            }

            var account = await db.ApplicationUserAccounts
                .FirstOrDefaultAsync(a => a.NormalizedEmail == normalizedEmail && a.DeletedAt == null, ct);
            if (account is null)
            {
                continue;
            }

            await RecordOtpDeliveryOutcomeAsync(account.Id, webhookEvent, now, ct);
            await RecordNotificationDeliveryOutcomeAsync(account.Id, webhookEvent, now, ct);

            var eventName = webhookEvent.Event.ToLowerInvariant();
            var isUnsubscribe = string.Equals(eventName, "unsubscribed", StringComparison.OrdinalIgnoreCase);
            var isReputation = BrevoReputationEvents.Contains(eventName);
            if (!isUnsubscribe && !isReputation)
            {
                continue;
            }

            // Marketing unsubscribe must never become a global email kill-switch.
            // Auth/OTP/password-reset keep sending from auth@ regardless.
            var scopedEventKey = isUnsubscribe
                ? EmailLanes.MarketingSuppressionEventKey
                : EmailLanes.NonAuthSuppressionEventKey;

            if (isUnsubscribe)
            {
                var registration = await db.LearnerRegistrationProfiles
                    .FirstOrDefaultAsync(profile => profile.ApplicationUserAccountId == account.Id, ct);
                if (registration is { MarketingOptIn: true })
                {
                    registration.MarketingOptIn = false;
                }
            }

            var alreadySuppressed = await db.NotificationSuppressions.AnyAsync(
                s => s.AuthAccountId == account.Id
                    && s.Channel == NotificationChannel.Email
                    && s.IsActive
                    && s.EventKey == scopedEventKey,
                ct);
            if (alreadySuppressed)
            {
                continue;
            }

            db.NotificationSuppressions.Add(new NotificationSuppression
            {
                Id = Guid.NewGuid(),
                AuthAccountId = account.Id,
                Channel = NotificationChannel.Email,
                EventKey = scopedEventKey,
                IsActive = true,
                ReasonCode = $"brevo_{eventName}",
                Reason = string.IsNullOrWhiteSpace(webhookEvent.Reason)
                    ? $"Brevo reported '{webhookEvent.Event}' for this address."
                    : $"Brevo reported '{webhookEvent.Event}': {webhookEvent.Reason}",
                CreatedByAdminId = "system:brevo-webhook",
                CreatedByAdminName = "Brevo Webhook",
                StartsAt = now,
                ExpiresAt = null,
                CreatedAt = now,
                UpdatedAt = now
            });
            suppressedCount++;
        }

        if (suppressedCount > 0)
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Brevo webhook recorded scoped email suppression for {Count} account(s).", suppressedCount);
        }
        else
        {
            await db.SaveChangesAsync(ct);
        }

        return suppressedCount;
    }

    private async Task RecordOtpDeliveryOutcomeAsync(
        string authAccountId,
        BrevoWebhookEvent webhookEvent,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var challenge = await db.EmailOtpChallenges
            .Where(item => item.ApplicationUserAccountId == authAccountId
                && item.DeliveryChannel == "email"
                && item.VerifiedAt == null)
            .OrderByDescending(item => item.CreatedAt)
            .FirstOrDefaultAsync(ct);
        if (challenge is null)
        {
            return;
        }

        challenge.DeliveryStatus = webhookEvent.Event.ToLowerInvariant();
        challenge.DeliveryReason = string.IsNullOrWhiteSpace(webhookEvent.Reason) ? null : webhookEvent.Reason.Trim();
        challenge.DeliveryUpdatedAt = now;
    }

    private async Task RecordNotificationDeliveryOutcomeAsync(
        string authAccountId,
        BrevoWebhookEvent webhookEvent,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var status = MapBrevoDeliveryStatus(webhookEvent.Event);
        if (status is null)
        {
            return;
        }

        var recent = await db.NotificationDeliveryAttempts
            .Where(attempt =>
                attempt.AuthAccountId == authAccountId
                && attempt.Channel == NotificationChannel.Email
                && attempt.Status == NotificationDeliveryStatus.Sent
                && attempt.AttemptedAt >= now.AddHours(-24))
            .OrderByDescending(attempt => attempt.AttemptedAt)
            .FirstOrDefaultAsync(ct);
        if (recent is null)
        {
            return;
        }

        recent.Status = status.Value;
        recent.ErrorCode = $"brevo_{webhookEvent.Event.ToLowerInvariant()}";
        recent.ErrorMessage = string.IsNullOrWhiteSpace(webhookEvent.Reason) ? webhookEvent.Event : webhookEvent.Reason;
        recent.CompletedAt = now;
    }

    private static NotificationDeliveryStatus? MapBrevoDeliveryStatus(string eventName)
        => eventName.ToLowerInvariant() switch
        {
            "delivered" => NotificationDeliveryStatus.Delivered,
            "unique_opened" or "opened" => NotificationDeliveryStatus.Opened,
            "click" => NotificationDeliveryStatus.Clicked,
            "hard_bounce" or "soft_bounce" or "blocked" or "invalid_email" or "error" => NotificationDeliveryStatus.Bounced,
            "unsubscribed" => NotificationDeliveryStatus.Unsubscribed,
            "spam" => NotificationDeliveryStatus.Failed,
            _ => null
        };

    private static bool FixedTimeSecretEquals(string configured, string provided)
    {
        var configuredBytes = System.Text.Encoding.UTF8.GetBytes(configured);
        var providedBytes = System.Text.Encoding.UTF8.GetBytes(provided);
        return configuredBytes.Length == providedBytes.Length
            && System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(configuredBytes, providedBytes);
    }

    private static List<BrevoWebhookEvent> ParseBrevoWebhookEvents(string rawPayload)
    {
        if (string.IsNullOrWhiteSpace(rawPayload))
        {
            return [];
        }

        try
        {
            var trimmed = rawPayload.TrimStart();
            if (trimmed.StartsWith('['))
            {
                return System.Text.Json.JsonSerializer.Deserialize<List<BrevoWebhookEvent>>(rawPayload) ?? [];
            }

            var single = System.Text.Json.JsonSerializer.Deserialize<BrevoWebhookEvent>(rawPayload);
            return single is null ? [] : [single];
        }
        catch (System.Text.Json.JsonException)
        {
            return [];
        }
    }

    private sealed record BrevoWebhookEvent(
        [property: System.Text.Json.Serialization.JsonPropertyName("event")] string? Event,
        [property: System.Text.Json.Serialization.JsonPropertyName("email")] string? Email,
        [property: System.Text.Json.Serialization.JsonPropertyName("reason")] string? Reason);
}
