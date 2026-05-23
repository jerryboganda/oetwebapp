using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Billing;

/// <summary>
/// Applies / reverses OET 2026 add-on entitlement grants in response to
/// payment + refund webhooks.
///
/// <para>Idempotent on the tuple <c>(scope=addon_grant, key=&lt;eventId&gt;)</c>
/// stored in <see cref="IdempotencyRecord"/>. Duplicate webhook deliveries
/// (the norm for Stripe / PayPal / Checkout.com retries) become a no-op.</para>
///
/// <para>For refunds, the same idempotency key is used with
/// <c>scope=addon_refund</c>. Counters clamp at zero. Tutor Book unlock is
/// not auto-revoked on refund — leave that to admin action.</para>
/// </summary>
public interface IAddonGrantProcessor
{
    Task<AddonGrantResult> ApplyAsync(string eventId, string subscriptionId, string addOnCode, CancellationToken ct = default);
    Task<AddonGrantResult> ReverseAsync(string eventId, string subscriptionId, string addOnCode, CancellationToken ct = default);
}

public sealed record AddonGrantResult(bool Applied, bool DuplicateSkipped, string? Reason);

public sealed class AddonGrantProcessor(LearnerDbContext db, ILogger<AddonGrantProcessor> logger) : IAddonGrantProcessor
{
    private const string GrantScope = "addon_grant";
    private const string RefundScope = "addon_refund";

    public async Task<AddonGrantResult> ApplyAsync(string eventId, string subscriptionId, string addOnCode, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(eventId)) return new(false, false, "event_id_missing");

        var idemKey = $"{subscriptionId}:{addOnCode}:{eventId}";
        var alreadyApplied = await db.IdempotencyRecords.AsNoTracking()
            .AnyAsync(r => r.Scope == GrantScope && r.Key == idemKey, ct);
        if (alreadyApplied)
        {
            logger.LogInformation("AddonGrantProcessor — duplicate webhook delivery {EventId} skipped.", eventId);
            return new(false, true, "duplicate");
        }

        var subscription = await db.Subscriptions.FirstOrDefaultAsync(s => s.Id == subscriptionId, ct);
        if (subscription is null) return new(false, false, "subscription_missing");

        var addOn = await db.BillingAddOns.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Code == addOnCode || a.Id == addOnCode, ct);
        if (addOn is null) return new(false, false, "addon_missing");

        SubscriptionBundleInitializer.ApplyAddOnGrant(subscription, addOn);

        db.IdempotencyRecords.Add(new IdempotencyRecord
        {
            Id = $"idem_{Guid.NewGuid():N}"[..Math.Min(128, 37)],
            Scope = GrantScope,
            Key = idemKey,
            ResponseJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                subscriptionId,
                addOnCode,
                eventId,
                appliedAt = DateTimeOffset.UtcNow
            }),
            CreatedAt = DateTimeOffset.UtcNow,
        });

        await db.SaveChangesAsync(ct);
        logger.LogInformation("AddonGrantProcessor — applied {AddOn} to {Subscription} (event {EventId}).", addOnCode, subscriptionId, eventId);
        return new(true, false, null);
    }

    public async Task<AddonGrantResult> ReverseAsync(string eventId, string subscriptionId, string addOnCode, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(eventId)) return new(false, false, "event_id_missing");

        var idemKey = $"{subscriptionId}:{addOnCode}:{eventId}";
        var alreadyReversed = await db.IdempotencyRecords.AsNoTracking()
            .AnyAsync(r => r.Scope == RefundScope && r.Key == idemKey, ct);
        if (alreadyReversed) return new(false, true, "duplicate");

        var subscription = await db.Subscriptions.FirstOrDefaultAsync(s => s.Id == subscriptionId, ct);
        if (subscription is null) return new(false, false, "subscription_missing");

        var addOn = await db.BillingAddOns.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Code == addOnCode || a.Id == addOnCode, ct);
        if (addOn is null) return new(false, false, "addon_missing");

        // Use BillingAddOn (live) — the granted counters mirror the live grant amounts.
        // Counters clamp at zero inside the helper.
        if (addOn.LettersGranted > 0)
        {
            subscription.WritingAssessmentsRemaining = Math.Max(0, subscription.WritingAssessmentsRemaining - addOn.LettersGranted);
        }
        if (addOn.SessionsGranted > 0)
        {
            subscription.SpeakingSessionsRemaining = Math.Max(0, subscription.SpeakingSessionsRemaining - addOn.SessionsGranted);
        }
        if (addOn.GrantCredits > 0)
        {
            subscription.AiCreditsRemaining = Math.Max(0, subscription.AiCreditsRemaining - addOn.GrantCredits);
        }

        db.IdempotencyRecords.Add(new IdempotencyRecord
        {
            Id = $"idem_{Guid.NewGuid():N}"[..Math.Min(128, 37)],
            Scope = RefundScope,
            Key = idemKey,
            ResponseJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                subscriptionId,
                addOnCode,
                eventId,
                reversedAt = DateTimeOffset.UtcNow
            }),
            CreatedAt = DateTimeOffset.UtcNow,
        });

        await db.SaveChangesAsync(ct);
        logger.LogInformation("AddonGrantProcessor — reversed {AddOn} on {Subscription} (event {EventId}).", addOnCode, subscriptionId, eventId);
        return new(true, false, null);
    }
}
