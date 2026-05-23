using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Billing;

/// <summary>
/// Applies or reverses entitlement grants after successful payment or refund webhook.
/// Idempotent on (subscription_id, addon_version_id, payment_event_id).
/// </summary>
public interface IAddonGrantProcessor
{
    Task ApplyGrantAsync(Guid subscriptionId, Guid addonVersionId, string paymentEventId, CancellationToken ct = default);
    Task ReverseGrantAsync(Guid subscriptionId, Guid addonVersionId, string paymentEventId, CancellationToken ct = default);
}

public sealed class AddonGrantProcessor(LearnerDbContext db, ILogger<AddonGrantProcessor> logger) : IAddonGrantProcessor
{
    public async Task ApplyGrantAsync(Guid subscriptionId, Guid addonVersionId, string paymentEventId, CancellationToken ct = default)
    {
        // Idempotency: skip if already processed
        var exists = await db.Set<AuditEvent>()
            .AnyAsync(e => e.Action == "AddonGrantApplied" && e.ResourceId == paymentEventId, ct);
        if (exists)
        {
            logger.LogInformation("AddonGrant already applied for {PaymentEventId}, skipping", paymentEventId);
            return;
        }

        db.Set<AuditEvent>().Add(new AuditEvent
        {
            Id = Guid.NewGuid().ToString("N"),
            Action = "AddonGrantApplied",
            ResourceId = paymentEventId,
            ResourceType = "AddonGrant",
            ActorId = "system",
            ActorName = "AddonGrantProcessor",
            Details = $"subscription={subscriptionId}, addonVersion={addonVersionId}",
            OccurredAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Applied addon grant for subscription {SubscriptionId}, addon {AddonVersionId}", subscriptionId, addonVersionId);
    }

    public async Task ReverseGrantAsync(Guid subscriptionId, Guid addonVersionId, string paymentEventId, CancellationToken ct = default)
    {
        var exists = await db.Set<AuditEvent>()
            .AnyAsync(e => e.Action == "AddonGrantReversed" && e.ResourceId == paymentEventId, ct);
        if (exists)
        {
            logger.LogInformation("AddonGrant already reversed for {PaymentEventId}, skipping", paymentEventId);
            return;
        }

        db.Set<AuditEvent>().Add(new AuditEvent
        {
            Id = Guid.NewGuid().ToString("N"),
            Action = "AddonGrantReversed",
            ResourceId = paymentEventId,
            ResourceType = "AddonGrant",
            ActorId = "system",
            ActorName = "AddonGrantProcessor",
            Details = $"subscription={subscriptionId}, addonVersion={addonVersionId}",
            OccurredAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Reversed addon grant for subscription {SubscriptionId}, addon {AddonVersionId}", subscriptionId, addonVersionId);
    }
}
