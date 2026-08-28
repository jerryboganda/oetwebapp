using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

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
    Task<AddonGrantResult> ApplyAsync(
        string eventId,
        string subscriptionId,
        string addOnCode,
        CancellationToken ct = default,
        int quantity = 1,
        string? sourceReferenceId = null);
    Task<AddonGrantResult> ReverseAsync(
        string eventId,
        string subscriptionId,
        string addOnCode,
        CancellationToken ct = default,
        int quantity = 1,
        string? sourceReferenceId = null);
}

public sealed record AddonGrantResult(bool Applied, bool DuplicateSkipped, string? Reason);

public sealed class AddonGrantProcessor(
    LearnerDbContext db,
    ILogger<AddonGrantProcessor> logger,
    IAiPackageCreditService? aiPackageCredits = null) : IAddonGrantProcessor
{
    private const string GrantScope = "addon_grant";
    private const string RefundScope = "addon_refund";

    public async Task<AddonGrantResult> ApplyAsync(
        string eventId,
        string subscriptionId,
        string addOnCode,
        CancellationToken ct = default,
        int quantity = 1,
        string? sourceReferenceId = null)
    {
        if (string.IsNullOrWhiteSpace(eventId)) return new(false, false, "event_id_missing");

        var idemKey = FitDatabaseKey($"{subscriptionId}:{addOnCode}:{eventId}");
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

        quantity = Math.Max(1, quantity);
        sourceReferenceId = FitDatabaseKey(sourceReferenceId ?? AiPackageCreditSources.Addon(subscription.Id, addOn.Code));
        SubscriptionBundleInitializer.ApplyAddOnGrant(subscription, addOn, quantity);

        var aiCreditGrant = ResolveAiCreditGrant(addOn.GrantEntitlementsJson, addOn.GrantCredits) * quantity;
        if (aiCreditGrant > 0)
        {
            var creditReferenceId = FitDatabaseKey($"{sourceReferenceId}:{eventId}");
            var creditAlreadyGranted = await db.AiCreditLedger.AsNoTracking()
                .AnyAsync(entry => entry.UserId == subscription.UserId
                                   && entry.Source == AiCreditSource.Purchase
                                   && entry.ReferenceId == creditReferenceId,
                    ct);
            if (!creditAlreadyGranted)
            {
                var now = DateTimeOffset.UtcNow;
                db.AiCreditLedger.Add(new AiCreditLedgerEntry
                {
                    Id = Guid.NewGuid().ToString("N"),
                    UserId = subscription.UserId,
                    TokensDelta = aiCreditGrant,
                    CostDeltaUsd = 0m,
                    Source = AiCreditSource.Purchase,
                    Description = $"{addOn.Name} AI grading credits",
                    ReferenceId = creditReferenceId,
                    ExpiresAt = addOn.DurationDays > 0 ? now.AddDays(addOn.DurationDays) : null,
                    CreatedAt = now,
                });
            }
        }

        if (aiPackageCredits is not null)
        {
            var walletReference = FitDatabaseKey($"addon:{idemKey}");
            var walletExpiry = addOn.DurationDays > 0
                ? DateTimeOffset.UtcNow.AddDays(addOn.DurationDays)
                : DateTimeOffset.UtcNow.AddDays(180);
            if (string.Equals(addOn.AddonKind, "ai_package", StringComparison.OrdinalIgnoreCase))
            {
                await aiPackageCredits.GrantPackageAsync(
                    subscription.UserId,
                    addOn,
                    quantity,
                    walletReference,
                    null,
                    ct,
                    sourceReferenceId);
            }
            else if (aiCreditGrant > 0)
            {
                await aiPackageCredits.GrantCourseGiftCreditsAsync(
                    subscription.UserId,
                    addOn.Code,
                    addOn.Name,
                    aiCreditGrant,
                    walletReference,
                    walletExpiry,
                    ct,
                    sourceReferenceId);
            }
        }

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

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            var insertedByRace = await db.IdempotencyRecords.AsNoTracking()
                .AnyAsync(r => r.Scope == GrantScope && r.Key == idemKey, ct);
            if (insertedByRace)
            {
                logger.LogInformation("AddonGrantProcessor — duplicate webhook delivery {EventId} skipped after database idempotency race.", eventId);
                return new(false, true, "duplicate");
            }

            throw;
        }
        logger.LogInformation("AddonGrantProcessor — applied {AddOn} to {Subscription} (event {EventId}).", addOnCode, subscriptionId, eventId);
        return new(true, false, null);
    }

    public async Task<AddonGrantResult> ReverseAsync(
        string eventId,
        string subscriptionId,
        string addOnCode,
        CancellationToken ct = default,
        int quantity = 1,
        string? sourceReferenceId = null)
    {
        if (string.IsNullOrWhiteSpace(eventId)) return new(false, false, "event_id_missing");

        var idemKey = FitDatabaseKey($"{subscriptionId}:{addOnCode}:{eventId}");
        var alreadyReversed = await db.IdempotencyRecords.AsNoTracking()
            .AnyAsync(r => r.Scope == RefundScope && r.Key == idemKey, ct);
        if (alreadyReversed) return new(false, true, "duplicate");

        var subscription = await db.Subscriptions.FirstOrDefaultAsync(s => s.Id == subscriptionId, ct);
        if (subscription is null) return new(false, false, "subscription_missing");

        var addOn = await db.BillingAddOns.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Code == addOnCode || a.Id == addOnCode, ct);
        if (addOn is null) return new(false, false, "addon_missing");

        quantity = Math.Max(1, quantity);
        sourceReferenceId = FitDatabaseKey(sourceReferenceId ?? AiPackageCreditSources.Addon(subscription.Id, addOn.Code));
        var aiCreditGrant = ResolveAiCreditGrant(addOn.GrantEntitlementsJson, addOn.GrantCredits) * quantity;
        var authoritativeReversed = aiPackageCredits is null
            ? 0
            : await aiPackageCredits.ReverseGrantsAsync(
                subscription.UserId,
                sourceReferenceId,
                ct);
        var aiCreditReversal = await ResolveUnreversedPurchaseRefundReferenceAsync(
            subscription.UserId,
            subscriptionId,
            addOnCode,
            addOn.Code,
            sourceReferenceId,
            ct);
        if (aiCreditGrant > 0 && authoritativeReversed == 0 && !aiCreditReversal.FoundMatchingPurchase)
        {
            return new(false, false, "ai_credit_purchase_missing");
        }

        if (aiCreditGrant > 0
            && authoritativeReversed == 0
            && aiCreditReversal.FoundMatchingPurchase
            && aiCreditReversal.ReversalReferenceId is null)
        {
            return new(false, false, "ai_credit_purchase_already_reversed");
        }

        // Use BillingAddOn (live) for non-ledger counters — the granted counters
        // mirror the live grant amounts. AI credits reverse by purchase ledger row.
        // Counters clamp at zero inside the helper.
        if (addOn.LettersGranted > 0)
        {
            subscription.WritingAssessmentsRemaining = Math.Max(0, subscription.WritingAssessmentsRemaining - addOn.LettersGranted * quantity);
        }
        if (addOn.SessionsGranted > 0)
        {
            subscription.SpeakingSessionsRemaining = Math.Max(0, subscription.SpeakingSessionsRemaining - addOn.SessionsGranted * quantity);
        }
        if (aiCreditReversal.ReversalReferenceId is not null && aiCreditReversal.Credits > 0)
        {
            subscription.AiCreditsRemaining = Math.Max(0, subscription.AiCreditsRemaining - aiCreditReversal.Credits);
            db.AiCreditLedger.Add(new AiCreditLedgerEntry
            {
                Id = Guid.NewGuid().ToString("N"),
                UserId = subscription.UserId,
                TokensDelta = -aiCreditReversal.Credits,
                CostDeltaUsd = 0m,
                Source = AiCreditSource.AdminAdjustment,
                Description = $"Refund reversal for {addOn.Name} AI grading credits",
                ReferenceId = aiCreditReversal.ReversalReferenceId,
                CreatedAt = DateTimeOffset.UtcNow,
            });
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

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            var insertedByRace = await db.IdempotencyRecords.AsNoTracking()
                .AnyAsync(r => r.Scope == RefundScope && r.Key == idemKey, ct);
            if (insertedByRace)
            {
                logger.LogInformation("AddonGrantProcessor — duplicate refund webhook delivery {EventId} skipped after database idempotency race.", eventId);
                return new(false, true, "duplicate");
            }

            throw;
        }
        logger.LogInformation("AddonGrantProcessor — reversed {AddOn} on {Subscription} (event {EventId}).", addOnCode, subscriptionId, eventId);
        return new(true, false, null);
    }

    private async Task<AiCreditReversalResolution> ResolveUnreversedPurchaseRefundReferenceAsync(
        string userId,
        string subscriptionId,
        string requestedAddOnCode,
        string canonicalAddOnCode,
        string sourceReferenceId,
        CancellationToken ct)
    {
        var prefixes = new[]
            {
                $"{sourceReferenceId}:",
                $"addon:{subscriptionId}:{requestedAddOnCode}:",
                $"addon:{subscriptionId}:{canonicalAddOnCode}:",
            }
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var purchases = await db.AiCreditLedger.AsNoTracking()
            .Where(entry => entry.UserId == userId
                            && entry.Source == AiCreditSource.Purchase
                            && entry.TokensDelta > 0
                            && entry.ReferenceId != null)
            .OrderBy(entry => entry.CreatedAt)
            .ThenBy(entry => entry.Id)
            .ToListAsync(ct);

        var matchingPurchases = purchases
            .Where(entry => prefixes.Any(prefix => entry.ReferenceId!.StartsWith(prefix, StringComparison.Ordinal)))
            .ToList();

        foreach (var purchase in matchingPurchases)
        {
            var reversalReferenceId = BuildAiCreditRefundReference(purchase.ReferenceId!);
            if (reversalReferenceId is null) continue;

            var alreadyReversed = await db.AiCreditLedger.AsNoTracking()
                .AnyAsync(entry => entry.UserId == userId
                                   && entry.Source == AiCreditSource.AdminAdjustment
                                   && entry.ReferenceId == reversalReferenceId,
                    ct);
            if (alreadyReversed)
            {
                continue;
            }

            return new(reversalReferenceId, purchase.TokensDelta, true);
        }

        return new(null, 0, matchingPurchases.Count > 0);
    }

    private sealed record AiCreditReversalResolution(string? ReversalReferenceId, int Credits, bool FoundMatchingPurchase);

    private static string? BuildAiCreditRefundReference(string purchaseReferenceId)
    {
        if (purchaseReferenceId.StartsWith("addon:", StringComparison.Ordinal))
        {
            return "addon-refund:" + purchaseReferenceId["addon:".Length..];
        }

        return null;
    }

    private static int ResolveAiCreditGrant(string? grantEntitlementsJson, int fallbackGrantCredits)
    {
        if (!string.IsNullOrWhiteSpace(grantEntitlementsJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(grantEntitlementsJson);
                if (doc.RootElement.ValueKind == JsonValueKind.Object
                    && TryReadCreditValue(doc.RootElement, out var value))
                {
                    return Math.Max(0, value);
                }
            }
            catch (JsonException)
            {
                return 0;
            }
        }

        return fallbackGrantCredits > 0 && string.IsNullOrWhiteSpace(grantEntitlementsJson)
            ? fallbackGrantCredits
            : 0;
    }

    private static bool TryReadCreditValue(JsonElement root, out int value)
    {
        if (root.TryGetProperty("ai_credits", out var aiCredits) && aiCredits.TryGetInt32(out value))
        {
            return true;
        }

        if (root.TryGetProperty("reviewCredits", out var reviewCredits) && reviewCredits.TryGetInt32(out value))
        {
            return true;
        }

        value = 0;
        return false;
    }

    internal static string FitDatabaseKey(string value, int maxLength = 128)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
        {
            return value;
        }

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
        var prefixLength = Math.Max(0, maxLength - hash.Length - 1);
        return (prefixLength == 0 ? string.Empty : value[..prefixLength]) + "-" + hash;
    }
}
