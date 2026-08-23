using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services;

/// <summary>
/// Admin inspect/unblock for one email. Marketing unsubscribe stays
/// independent of OTP. Never mass-unblocks the Brevo list.
/// </summary>
public sealed class EmailDeliveryMailboxService(
    LearnerDbContext db,
    IBrevoTransactionalMailbox mailbox,
    TimeProvider timeProvider)
{
    public async Task<AdminEmailDeliveryInspectResponse> InspectAsync(string email, CancellationToken ct)
    {
        var trimmed = AuthEmailAddress.TrimAndValidateOrThrow(email);
        var normalizedEmail = AuthEmailAddress.NormalizeOrThrow(trimmed);

        var account = await db.ApplicationUserAccounts
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.NormalizedEmail == normalizedEmail && item.DeletedAt == null, ct);

        bool? marketingOptIn = null;
        IReadOnlyList<NotificationSuppressionItem> suppressions = [];
        AdminEmailOtpDeliveryItem? latestOtp = null;

        if (account is not null)
        {
            var registration = await db.LearnerRegistrationProfiles
                .AsNoTracking()
                .FirstOrDefaultAsync(profile => profile.ApplicationUserAccountId == account.Id, ct);
            marketingOptIn = registration?.MarketingOptIn;

            suppressions = (await db.NotificationSuppressions
                    .AsNoTracking()
                    .Where(item => item.AuthAccountId == account.Id && item.Channel == NotificationChannel.Email && item.IsActive)
                    .OrderByDescending(item => item.UpdatedAt)
                    .ToListAsync(ct))
                .Select(MapSuppression)
                .ToArray();

            var challenge = await db.EmailOtpChallenges
                .AsNoTracking()
                .Where(item => item.ApplicationUserAccountId == account.Id && item.DeliveryChannel == "email")
                .OrderByDescending(item => item.CreatedAt)
                .FirstOrDefaultAsync(ct);
            if (challenge is not null)
            {
                latestOtp = new AdminEmailOtpDeliveryItem(
                    challenge.Id,
                    challenge.Purpose,
                    challenge.DeliveryStatus,
                    challenge.DeliveryReason,
                    challenge.CreatedAt,
                    challenge.SentAt,
                    challenge.DeliveryUpdatedAt);
            }
        }

        AdminBrevoBlocklistItem blocklist;
        try
        {
            var blocked = await mailbox.FindBlockedContactAsync(trimmed, ct);
            blocklist = blocked is null
                ? new AdminBrevoBlocklistItem(false, trimmed, null, null, null, null, null)
                : new AdminBrevoBlocklistItem(true, blocked.Email, blocked.ReasonCode, blocked.ReasonMessage, blocked.SenderEmail, blocked.BlockedAt, null);
        }
        catch (Exception ex)
        {
            blocklist = new AdminBrevoBlocklistItem(false, trimmed, null, null, null, null, ex.Message);
        }

        return new AdminEmailDeliveryInspectResponse(
            trimmed,
            normalizedEmail,
            account?.Id,
            marketingOptIn,
            AuthOtpUnblocked: true,
            suppressions,
            latestOtp,
            blocklist);
    }

    public async Task<AdminEmailDeliveryUnblockResponse> UnblockAsync(
        string adminId,
        string adminName,
        string email,
        CancellationToken ct)
    {
        var trimmed = AuthEmailAddress.TrimAndValidateOrThrow(email);
        var normalizedEmail = AuthEmailAddress.NormalizeOrThrow(trimmed);

        var account = await db.ApplicationUserAccounts
            .FirstOrDefaultAsync(item => item.NormalizedEmail == normalizedEmail && item.DeletedAt == null, ct);

        var releasedIds = new List<string>();
        if (account is not null)
        {
            var now = timeProvider.GetUtcNow();
            var suppressions = await db.NotificationSuppressions
                .Where(item => item.AuthAccountId == account.Id
                    && item.Channel == NotificationChannel.Email
                    && item.IsActive
                    && (item.EventKey == null
                        || item.EventKey == EmailLanes.NonAuthSuppressionEventKey
                        || (item.ReasonCode.StartsWith("brevo_")
                            && item.EventKey != EmailLanes.MarketingSuppressionEventKey)))
                .ToListAsync(ct);

            foreach (var suppression in suppressions)
            {
                suppression.IsActive = false;
                suppression.ReleasedByAdminId = adminId;
                suppression.ReleasedByAdminName = adminName;
                suppression.ReleasedAt = now;
                suppression.UpdatedAt = now;
                releasedIds.Add(suppression.Id.ToString());
            }
        }

        var wasBlocked = false;
        var unblocked = false;
        try
        {
            wasBlocked = await mailbox.FindBlockedContactAsync(trimmed, ct) is not null;
            unblocked = await mailbox.UnblockContactAsync(trimmed, ct) || !wasBlocked;
        }
        catch (Exception ex)
        {
            throw ApiException.Validation(
                "brevo_unblock_failed",
                $"Brevo could not unblock {trimmed}: {ex.Message}");
        }

        db.AuditEvents.Add(new AuditEvent
        {
            Id = $"AUD-{Guid.NewGuid():N}",
            OccurredAt = timeProvider.GetUtcNow(),
            ActorId = adminId,
            ActorName = adminName,
            Action = "transactional_email_unblocked",
            ResourceType = "EmailMailbox",
            ResourceId = account?.Id ?? normalizedEmail,
            Details = $"Unblocked transactional mailbox for {trimmed}. BrevoUnblocked={unblocked}; released={releasedIds.Count}."
        });
        await db.SaveChangesAsync(ct);

        return new AdminEmailDeliveryUnblockResponse(
            trimmed,
            normalizedEmail,
            account?.Id,
            unblocked,
            wasBlocked,
            releasedIds.Count,
            releasedIds);
    }

    private static NotificationSuppressionItem MapSuppression(NotificationSuppression suppression)
        => new(
            suppression.Id,
            suppression.AuthAccountId,
            "email",
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
}
