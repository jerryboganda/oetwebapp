using System.Text.Json;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Billing;

/// <summary>
/// Applies coupon-variant side effects beyond the headline percent/fixed discount:
///  - <c>trial_extension_days</c> extends Subscription.NextRenewalAt while in trial.
///  - <c>free_months</c> shifts Subscription.NextRenewalAt forward by N months.
///  - <c>first_month_only</c> caps the headline discount to the first billing cycle.
///  - <c>bogo</c> / <c>bundle_discount</c> are handled by checkout quote math (no
///    side-effect post-checkout).
/// </summary>
public interface ICouponVariantApplicator
{
    void Apply(BillingCoupon coupon, Subscription subscription);
}

public sealed class CouponVariantApplicator : ICouponVariantApplicator
{
    public void Apply(BillingCoupon coupon, Subscription subscription)
    {
        if (coupon is null || subscription is null) return;
        var variant = coupon.CouponVariant ?? "percent_off";
        var metadata = ParseMetadata(coupon.VariantMetadataJson);

        switch (variant)
        {
            case "trial_extension_days":
                {
                    if (subscription.Status != SubscriptionStatus.Trial) return;
                    var days = ReadInt(metadata, "extensionDays", 7);
                    subscription.NextRenewalAt = subscription.NextRenewalAt.AddDays(days);
                    subscription.ChangedAt = DateTimeOffset.UtcNow;
                    break;
                }
            case "free_months":
                {
                    var months = ReadInt(metadata, "freeMonths", 1);
                    if (months > 0)
                    {
                        subscription.NextRenewalAt = subscription.NextRenewalAt.AddMonths(months);
                        subscription.ChangedAt = DateTimeOffset.UtcNow;
                    }
                    break;
                }
            // Other variants apply at quote time (handled in LearnerService.Billing); nothing to do here.
        }
    }

    private static Dictionary<string, JsonElement> ParseMetadata(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return new();
            var result = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                result[prop.Name] = prop.Value.Clone();
            }
            return result;
        }
        catch
        {
            return new();
        }
    }

    private static int ReadInt(Dictionary<string, JsonElement> metadata, string key, int fallback)
    {
        if (!metadata.TryGetValue(key, out var el)) return fallback;
        return el.ValueKind switch
        {
            JsonValueKind.Number => el.TryGetInt32(out var v) ? v : fallback,
            JsonValueKind.String => int.TryParse(el.GetString(), out var v) ? v : fallback,
            _ => fallback,
        };
    }
}
