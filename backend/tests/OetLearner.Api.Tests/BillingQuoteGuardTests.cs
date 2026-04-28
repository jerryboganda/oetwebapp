using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests;

public class BillingQuoteGuardTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public BillingQuoteGuardTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task BillingQuote_RejectsNonPurchasableCatalogItems()
    {
        const string userId = "billing-guard-user";
        using var client = await CreateClientForUserAsync(userId);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var hiddenPlanCode = $"hidden-{suffix}";
        var incompatibleAddOnCode = $"addon-{suffix}";

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            await db.Database.EnsureCreatedAsync();
            var now = DateTimeOffset.UtcNow;

            db.BillingPlans.Add(new BillingPlan
            {
                Id = $"plan-{suffix}",
                Code = hiddenPlanCode,
                Name = "Hidden Private Plan",
                Description = "Not purchasable from learner checkout.",
                Price = 199m,
                Currency = "AUD",
                Interval = "month",
                DurationMonths = 1,
                IsVisible = false,
                IsRenewable = true,
                IncludedCredits = 12,
                IncludedSubtestsJson = JsonSerializer.Serialize(new[] { "writing", "speaking" }),
                EntitlementsJson = "{}",
                Status = BillingPlanStatus.Active,
                CreatedAt = now,
                UpdatedAt = now
            });

            db.BillingAddOns.Add(new BillingAddOn
            {
                Id = $"addon-{suffix}",
                Code = incompatibleAddOnCode,
                Name = "Premium-only Pack",
                Description = "Only available on premium plans.",
                Price = 25m,
                Currency = "AUD",
                Interval = "one_time",
                DurationDays = 0,
                GrantCredits = 3,
                AppliesToAllPlans = false,
                IsStackable = true,
                QuantityStep = 1,
                CompatiblePlanCodesJson = JsonSerializer.Serialize(new[] { "premium-monthly" }),
                GrantEntitlementsJson = "{}",
                Status = BillingAddOnStatus.Active,
                CreatedAt = now,
                UpdatedAt = now
            });

            await db.SaveChangesAsync();
        }

        var hiddenPlanResponse = await client.GetAsync($"/v1/billing/quote?productType=plan_upgrade&quantity=1&priceId={Uri.EscapeDataString(hiddenPlanCode)}");
        Assert.Equal(HttpStatusCode.BadRequest, hiddenPlanResponse.StatusCode);

        var incompatibleAddOnResponse = await client.GetAsync($"/v1/billing/quote?productType=addon_purchase&quantity=1&priceId={Uri.EscapeDataString(incompatibleAddOnCode)}");
        Assert.Equal(HttpStatusCode.BadRequest, incompatibleAddOnResponse.StatusCode);
    }

    [Fact]
    public async Task BillingQuote_ReleasesExpiredCouponReservationAndPersistsResolvedAddOnCode()
    {
        var userId = $"billing-coupon-{Guid.NewGuid():N}";
        using var client = await CreateClientForUserAsync(userId);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var packCode = $"pack-{suffix}";
        var couponCode = $"PACK{suffix}".ToUpperInvariant();

        string expiredQuoteId;
        string expiredRedemptionId;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            await db.Database.EnsureCreatedAsync();
            var now = DateTimeOffset.UtcNow;
            var subscription = await db.Subscriptions.FirstAsync(x => x.UserId == userId);

            db.BillingAddOns.Add(new BillingAddOn
            {
                Id = $"addon-{suffix}",
                Code = packCode,
                Name = "Guarded Review Pack",
                Description = "Pack used by coupon guard regression tests.",
                Price = 20m,
                Currency = "AUD",
                Interval = "one_time",
                DurationDays = 0,
                GrantCredits = 11,
                AppliesToAllPlans = true,
                IsStackable = true,
                QuantityStep = 1,
                CompatiblePlanCodesJson = "[]",
                GrantEntitlementsJson = "{}",
                Status = BillingAddOnStatus.Active,
                CreatedAt = now,
                UpdatedAt = now
            });

            db.BillingCoupons.Add(new BillingCoupon
            {
                Id = $"coupon-{suffix}",
                Code = couponCode,
                Name = "Pack-only Coupon",
                Description = "Only applies to the guarded review pack.",
                DiscountType = BillingDiscountType.Percentage,
                DiscountValue = 10m,
                Currency = "AUD",
                UsageLimitTotal = 1,
                IsStackable = false,
                ApplicablePlanCodesJson = "[]",
                ApplicableAddOnCodesJson = JsonSerializer.Serialize(new[] { packCode }),
                Status = BillingCouponStatus.Active,
                RedemptionCount = 1,
                CreatedAt = now,
                UpdatedAt = now
            });

            expiredQuoteId = $"quote-{suffix}";
            db.BillingQuotes.Add(new BillingQuote
            {
                Id = expiredQuoteId,
                UserId = userId,
                SubscriptionId = subscription.Id,
                PlanCode = subscription.PlanId,
                AddOnCodesJson = JsonSerializer.Serialize(new[] { packCode }),
                CouponCode = couponCode,
                Currency = "AUD",
                SubtotalAmount = 20m,
                DiscountAmount = 2m,
                TotalAmount = 18m,
                Status = BillingQuoteStatus.Created,
                CreatedAt = now.AddHours(-2),
                ExpiresAt = now.AddMinutes(-30),
                SnapshotJson = "{}"
            });

            expiredRedemptionId = $"redemption-{suffix}";
            db.BillingCouponRedemptions.Add(new BillingCouponRedemption
            {
                Id = expiredRedemptionId,
                CouponCode = couponCode,
                UserId = userId,
                QuoteId = expiredQuoteId,
                DiscountAmount = 2m,
                Currency = "AUD",
                Status = BillingRedemptionStatus.Reserved,
                RedeemedAt = now.AddHours(-2)
            });

            await db.SaveChangesAsync();
        }

        var response = await client.GetAsync($"/v1/billing/quote?productType=review_credits&quantity=11&couponCode={Uri.EscapeDataString(couponCode)}");
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, body);

        using (var json = JsonDocument.Parse(body))
        {
            var addOnCodes = json.RootElement.GetProperty("addOnCodes").EnumerateArray().Select(item => item.GetString()).ToArray();
            Assert.Contains(packCode, addOnCodes);
        }

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            var expiredRedemption = await db.BillingCouponRedemptions.FirstAsync(x => x.Id == expiredRedemptionId);
            var expiredQuote = await db.BillingQuotes.FirstAsync(x => x.Id == expiredQuoteId);
            var coupon = await db.BillingCoupons.FirstAsync(x => x.Code == couponCode);

            Assert.Equal(BillingRedemptionStatus.Voided, expiredRedemption.Status);
            Assert.Equal(BillingQuoteStatus.Expired, expiredQuote.Status);
            Assert.Equal(1, coupon.RedemptionCount);
        }
    }

    [Fact]
    public async Task BillingQuote_DoesNotReleaseAppliedCheckoutCouponReservationDuringQuoteCleanup()
    {
        var userId = $"billing-applied-{Guid.NewGuid():N}";
        using var client = await CreateClientForUserAsync(userId);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var packCode = $"pack-{suffix}";
        var couponCode = $"APPLIED{suffix}".ToUpperInvariant();

        string appliedQuoteId;
        string appliedRedemptionId;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            await db.Database.EnsureCreatedAsync();
            var now = DateTimeOffset.UtcNow;
            var subscription = await db.Subscriptions.FirstAsync(x => x.UserId == userId);

            db.BillingAddOns.Add(new BillingAddOn
            {
                Id = $"addon-{suffix}",
                Code = packCode,
                Name = "Applied Checkout Pack",
                Description = "Pack used by applied checkout coupon cleanup tests.",
                Price = 20m,
                Currency = "AUD",
                Interval = "one_time",
                DurationDays = 0,
                GrantCredits = 13,
                AppliesToAllPlans = true,
                IsStackable = true,
                QuantityStep = 1,
                CompatiblePlanCodesJson = "[]",
                GrantEntitlementsJson = "{}",
                Status = BillingAddOnStatus.Active,
                CreatedAt = now,
                UpdatedAt = now
            });

            db.BillingCoupons.Add(new BillingCoupon
            {
                Id = $"coupon-{suffix}",
                Code = couponCode,
                Name = "Applied Checkout Coupon",
                Description = "Limited coupon reserved by an already opened checkout.",
                DiscountType = BillingDiscountType.Percentage,
                DiscountValue = 10m,
                Currency = "AUD",
                UsageLimitTotal = 1,
                IsStackable = false,
                ApplicablePlanCodesJson = "[]",
                ApplicableAddOnCodesJson = JsonSerializer.Serialize(new[] { packCode }),
                Status = BillingCouponStatus.Active,
                RedemptionCount = 1,
                CreatedAt = now,
                UpdatedAt = now
            });

            appliedQuoteId = $"quote-{suffix}";
            db.BillingQuotes.Add(new BillingQuote
            {
                Id = appliedQuoteId,
                UserId = userId,
                SubscriptionId = subscription.Id,
                PlanCode = subscription.PlanId,
                AddOnCodesJson = JsonSerializer.Serialize(new[] { packCode }),
                CouponCode = couponCode,
                Currency = "AUD",
                SubtotalAmount = 20m,
                DiscountAmount = 2m,
                TotalAmount = 18m,
                Status = BillingQuoteStatus.Applied,
                CheckoutSessionId = $"checkout-{suffix}",
                CreatedAt = now.AddHours(-2),
                ExpiresAt = now.AddMinutes(-30),
                SnapshotJson = "{}"
            });

            appliedRedemptionId = $"redemption-{suffix}";
            db.BillingCouponRedemptions.Add(new BillingCouponRedemption
            {
                Id = appliedRedemptionId,
                CouponCode = couponCode,
                UserId = userId,
                QuoteId = appliedQuoteId,
                CheckoutSessionId = $"checkout-{suffix}",
                DiscountAmount = 2m,
                Currency = "AUD",
                Status = BillingRedemptionStatus.Reserved,
                RedeemedAt = now.AddHours(-2)
            });

            await db.SaveChangesAsync();
        }

        var response = await client.GetAsync($"/v1/billing/quote?productType=review_credits&quantity=13&couponCode={Uri.EscapeDataString(couponCode)}");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            var appliedRedemption = await db.BillingCouponRedemptions.FirstAsync(x => x.Id == appliedRedemptionId);
            var appliedQuote = await db.BillingQuotes.FirstAsync(x => x.Id == appliedQuoteId);
            var coupon = await db.BillingCoupons.FirstAsync(x => x.Code == couponCode);

            Assert.Equal(BillingRedemptionStatus.Reserved, appliedRedemption.Status);
            Assert.Equal(BillingQuoteStatus.Applied, appliedQuote.Status);
            Assert.Equal(1, coupon.RedemptionCount);
        }
    }

    private async Task<HttpClient> CreateClientForUserAsync(string userId)
    {
        await _factory.EnsureLearnerProfileAsync(userId, $"{userId}@example.test", userId);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Debug-UserId", userId);
        client.DefaultRequestHeaders.Add("X-Debug-Email", $"{userId}@example.test");
        client.DefaultRequestHeaders.Add("X-Debug-Name", userId);
        return client;
    }
}
