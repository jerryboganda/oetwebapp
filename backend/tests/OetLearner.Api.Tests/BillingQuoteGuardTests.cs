using System.Net;
using System.Net.Http.Json;
using System.Text;
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

    [Fact]
    public async Task CheckoutCompletion_UsesQuoteTimePlanSnapshotAfterCatalogMutation()
    {
        var userId = $"billing-plan-snapshot-{Guid.NewGuid():N}";
        using var client = await CreateClientForUserAsync(userId);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var planCode = $"snapshot-plan-{suffix}";
        var planVersionId = $"plan-version-{suffix}-v1";

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            await db.Database.EnsureCreatedAsync();
            var now = DateTimeOffset.UtcNow;
            var plan = new BillingPlan
            {
                Id = $"plan-{suffix}",
                Code = planCode,
                Name = "Snapshot Plan Original",
                Description = "Original plan terms used by quote snapshot tests.",
                Price = 180m,
                Currency = "AUD",
                Interval = "month",
                DurationMonths = 2,
                IncludedCredits = 7,
                IncludedSubtestsJson = JsonSerializer.Serialize(new[] { "writing" }),
                EntitlementsJson = "{}",
                IsVisible = true,
                IsRenewable = true,
                Status = BillingPlanStatus.Active,
                CreatedAt = now,
                UpdatedAt = now
            };
            var version = CreatePlanVersion(plan, planVersionId, 1, now);
            plan.ActiveVersionId = version.Id;
            plan.LatestVersionId = version.Id;
            db.BillingPlans.Add(plan);
            db.BillingPlanVersions.Add(version);
            await db.SaveChangesAsync();
        }

        var checkoutResponse = await client.PostAsJsonAsync("/v1/billing/checkout-sessions", new
        {
            productType = "plan_upgrade",
            quantity = 1,
            priceId = planCode,
            gateway = "paypal"
        });
        var checkoutBody = await checkoutResponse.Content.ReadAsStringAsync();
        Assert.True(checkoutResponse.IsSuccessStatusCode, checkoutBody);
        using var checkoutJson = JsonDocument.Parse(checkoutBody);
        var checkoutSessionId = checkoutJson.RootElement.GetProperty("checkoutSessionId").GetString();
        Assert.False(string.IsNullOrWhiteSpace(checkoutSessionId));

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            var plan = await db.BillingPlans.FirstAsync(x => x.Code == planCode);
            var subscription = await db.Subscriptions.FirstAsync(x => x.UserId == userId);
            subscription.NextRenewalAt = DateTimeOffset.UtcNow.AddMinutes(-5);
            plan.Name = "Snapshot Plan Mutated";
            plan.Price = 999m;
            plan.Currency = "USD";
            plan.Interval = "year";
            plan.DurationMonths = 12;
            plan.IncludedCredits = 99;
            plan.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
        }

        var completionStartedAt = DateTimeOffset.UtcNow;
        await CompletePayPalCheckoutAsync(client, checkoutSessionId!);
        var completionFinishedAt = DateTimeOffset.UtcNow;

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            var subscription = await db.Subscriptions.FirstAsync(x => x.UserId == userId);
            var quote = await db.BillingQuotes.FirstAsync(x => x.CheckoutSessionId == checkoutSessionId);
            var paymentTransaction = await db.PaymentTransactions.FirstAsync(x => x.GatewayTransactionId == checkoutSessionId);
            var invoice = await db.Invoices.FirstAsync(x => x.QuoteId == quote.Id);
            var wallet = await db.Wallets.FirstAsync(x => x.UserId == userId);
            var walletTransaction = await db.WalletTransactions
                .Where(x => x.WalletId == wallet.Id && x.TransactionType == "plan_grant")
                .OrderByDescending(x => x.CreatedAt)
                .FirstAsync();

            Assert.Equal(planCode, subscription.PlanId);
            Assert.Equal(planVersionId, subscription.PlanVersionId);
            Assert.Equal(180m, subscription.PriceAmount);
            Assert.Equal("AUD", subscription.Currency);
            Assert.Equal("month", subscription.Interval);
            Assert.InRange(subscription.NextRenewalAt, completionStartedAt.AddMonths(2).AddSeconds(-5), completionFinishedAt.AddMonths(2).AddSeconds(5));
            Assert.Equal(7, walletTransaction.Amount);
            Assert.Equal("Included credits for Snapshot Plan Original", walletTransaction.Description);
            Assert.Equal(planVersionId, quote.PlanVersionId);
            Assert.Equal(quote.Id, paymentTransaction.QuoteId);
            Assert.Equal(planVersionId, paymentTransaction.PlanVersionId);
            Assert.Equal(planVersionId, invoice.PlanVersionId);
            Assert.Equal(quote.Id, invoice.QuoteId);
            Assert.Equal(checkoutSessionId, invoice.CheckoutSessionId);

            using var snapshotJson = JsonDocument.Parse(quote.SnapshotJson);
            var catalog = snapshotJson.RootElement.GetProperty("catalog");
            Assert.Equal(2, catalog.GetProperty("schemaVersion").GetInt32());
            Assert.Equal(planVersionId, catalog.GetProperty("plan").GetProperty("versionId").GetString());
            Assert.Equal(1, catalog.GetProperty("plan").GetProperty("versionNumber").GetInt32());
        }
    }

    [Fact]
    public async Task CheckoutCompletion_UsesQuoteTimeAddOnSnapshotAfterCatalogMutation()
    {
        var userId = $"billing-addon-snapshot-{Guid.NewGuid():N}";
        using var client = await CreateClientForUserAsync(userId);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var addOnCode = $"snapshot-addon-{suffix}";
        var addOnVersionId = $"addon-version-{suffix}-v1";

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            await db.Database.EnsureCreatedAsync();
            var now = DateTimeOffset.UtcNow;
            var addOn = new BillingAddOn
            {
                Id = $"addon-{suffix}",
                Code = addOnCode,
                Name = "Snapshot Add-on Original",
                Description = "Original add-on terms used by quote snapshot tests.",
                Price = 15m,
                Currency = "AUD",
                Interval = "one_time",
                DurationDays = 30,
                GrantCredits = 4,
                AppliesToAllPlans = true,
                IsRecurring = false,
                IsStackable = true,
                QuantityStep = 1,
                CompatiblePlanCodesJson = "[]",
                GrantEntitlementsJson = "{}",
                Status = BillingAddOnStatus.Active,
                CreatedAt = now,
                UpdatedAt = now
            };
            var version = CreateAddOnVersion(addOn, addOnVersionId, 1, now);
            addOn.ActiveVersionId = version.Id;
            addOn.LatestVersionId = version.Id;
            db.BillingAddOns.Add(addOn);
            db.BillingAddOnVersions.Add(version);
            await db.SaveChangesAsync();
        }

        var checkoutResponse = await client.PostAsJsonAsync("/v1/billing/checkout-sessions", new
        {
            productType = "addon_purchase",
            quantity = 2,
            priceId = addOnCode,
            gateway = "paypal"
        });
        var checkoutBody = await checkoutResponse.Content.ReadAsStringAsync();
        Assert.True(checkoutResponse.IsSuccessStatusCode, checkoutBody);
        using var checkoutJson = JsonDocument.Parse(checkoutBody);
        var checkoutSessionId = checkoutJson.RootElement.GetProperty("checkoutSessionId").GetString();
        Assert.False(string.IsNullOrWhiteSpace(checkoutSessionId));

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            var addOn = await db.BillingAddOns.FirstAsync(x => x.Code == addOnCode);
            addOn.Name = "Snapshot Add-on Mutated";
            addOn.DurationDays = 365;
            addOn.GrantCredits = 99;
            addOn.IsRecurring = true;
            addOn.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
        }

        await CompletePayPalCheckoutAsync(client, checkoutSessionId!);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            var subscription = await db.Subscriptions.FirstAsync(x => x.UserId == userId);
            var subscriptionItem = await db.SubscriptionItems.FirstAsync(x => x.SubscriptionId == subscription.Id && x.ItemCode == addOnCode);
            var quote = await db.BillingQuotes.FirstAsync(x => x.CheckoutSessionId == checkoutSessionId);
            var paymentTransaction = await db.PaymentTransactions.FirstAsync(x => x.GatewayTransactionId == checkoutSessionId);
            var invoice = await db.Invoices.FirstAsync(x => x.QuoteId == quote.Id);
            var wallet = await db.Wallets.FirstAsync(x => x.UserId == userId);
            var walletTransaction = await db.WalletTransactions
                .Where(x => x.WalletId == wallet.Id && x.TransactionType == "credit_purchase")
                .OrderByDescending(x => x.CreatedAt)
                .FirstAsync();

            Assert.Equal("addon", subscriptionItem.ItemType);
            Assert.Equal(addOnVersionId, subscriptionItem.AddOnVersionId);
            Assert.Equal(2, subscriptionItem.Quantity);
            Assert.NotNull(subscriptionItem.EndsAt);
            Assert.InRange(subscriptionItem.EndsAt!.Value, subscriptionItem.StartsAt.AddDays(30).AddSeconds(-5), subscriptionItem.StartsAt.AddDays(30).AddSeconds(5));
            Assert.Equal(8, walletTransaction.Amount);
            Assert.Equal("Snapshot Add-on Original credits", walletTransaction.Description);
            var quoteAddOnVersionIds = JsonSerializer.Deserialize<Dictionary<string, string>>(quote.AddOnVersionIdsJson)!;
            Assert.Equal(addOnVersionId, quoteAddOnVersionIds[addOnCode]);
            Assert.Equal(quote.Id, paymentTransaction.QuoteId);
            Assert.Equal(addOnVersionId, JsonSerializer.Deserialize<Dictionary<string, string>>(paymentTransaction.AddOnVersionIdsJson)![addOnCode]);
            Assert.Equal(addOnVersionId, JsonSerializer.Deserialize<Dictionary<string, string>>(invoice.AddOnVersionIdsJson)![addOnCode]);
            Assert.Equal(quote.Id, invoice.QuoteId);
            Assert.Equal(checkoutSessionId, invoice.CheckoutSessionId);

            using var snapshotJson = JsonDocument.Parse(quote.SnapshotJson);
            var addOnSnapshot = snapshotJson.RootElement.GetProperty("catalog").GetProperty("addOns")[0];
            Assert.Equal(addOnVersionId, addOnSnapshot.GetProperty("versionId").GetString());
            Assert.Equal(1, addOnSnapshot.GetProperty("versionNumber").GetInt32());
        }
    }

    [Fact]
    public async Task BillingQuote_CouponUsageLimitStaysParentWideAfterCouponVersionUpdate()
    {
        var firstUserId = $"billing-coupon-v1-{Guid.NewGuid():N}";
        var secondUserId = $"billing-coupon-v2-{Guid.NewGuid():N}";
        using var firstClient = await CreateClientForUserAsync(firstUserId);
        using var secondClient = await CreateClientForUserAsync(secondUserId);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var packCode = $"version-pack-{suffix}";
        var couponCode = $"VERSION{suffix}".ToUpperInvariant();
        var couponVersion1Id = $"coupon-version-{suffix}-v1";
        var couponVersion2Id = $"coupon-version-{suffix}-v2";

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            await db.Database.EnsureCreatedAsync();
            var now = DateTimeOffset.UtcNow;
            var addOn = new BillingAddOn
            {
                Id = $"addon-{suffix}",
                Code = packCode,
                Name = "Versioned Coupon Pack",
                Description = "Pack used by coupon parent-wide limit tests.",
                Price = 30m,
                Currency = "AUD",
                Interval = "one_time",
                DurationDays = 0,
                GrantCredits = 17,
                AppliesToAllPlans = true,
                IsStackable = true,
                QuantityStep = 1,
                CompatiblePlanCodesJson = "[]",
                GrantEntitlementsJson = "{}",
                Status = BillingAddOnStatus.Active,
                CreatedAt = now,
                UpdatedAt = now
            };
            var addOnVersion = CreateAddOnVersion(addOn, $"addon-version-{suffix}-v1", 1, now);
            addOn.ActiveVersionId = addOnVersion.Id;
            addOn.LatestVersionId = addOnVersion.Id;

            var coupon = new BillingCoupon
            {
                Id = $"coupon-{suffix}",
                Code = couponCode,
                Name = "Versioned Coupon",
                Description = "Limited coupon used by parent-wide limit tests.",
                DiscountType = BillingDiscountType.Percentage,
                DiscountValue = 10m,
                Currency = "AUD",
                UsageLimitTotal = 1,
                IsStackable = false,
                ApplicablePlanCodesJson = "[]",
                ApplicableAddOnCodesJson = JsonSerializer.Serialize(new[] { packCode }),
                Status = BillingCouponStatus.Active,
                CreatedAt = now,
                UpdatedAt = now
            };
            var couponVersion = CreateCouponVersion(coupon, couponVersion1Id, 1, now);
            coupon.ActiveVersionId = couponVersion.Id;
            coupon.LatestVersionId = couponVersion.Id;

            db.BillingAddOns.Add(addOn);
            db.BillingAddOnVersions.Add(addOnVersion);
            db.BillingCoupons.Add(coupon);
            db.BillingCouponVersions.Add(couponVersion);
            await db.SaveChangesAsync();
        }

        var firstResponse = await firstClient.GetAsync($"/v1/billing/quote?productType=review_credits&quantity=17&couponCode={Uri.EscapeDataString(couponCode)}");
        var firstBody = await firstResponse.Content.ReadAsStringAsync();
        Assert.True(firstResponse.IsSuccessStatusCode, firstBody);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            var coupon = await db.BillingCoupons.FirstAsync(x => x.Code == couponCode);
            coupon.Name = "Versioned Coupon Updated";
            coupon.DiscountValue = 15m;
            coupon.UpdatedAt = DateTimeOffset.UtcNow;
            var version = CreateCouponVersion(coupon, couponVersion2Id, 2, coupon.UpdatedAt);
            coupon.ActiveVersionId = version.Id;
            coupon.LatestVersionId = version.Id;
            db.BillingCouponVersions.Add(version);
            await db.SaveChangesAsync();
        }

        var secondResponse = await secondClient.GetAsync($"/v1/billing/quote?productType=review_credits&quantity=17&couponCode={Uri.EscapeDataString(couponCode)}");
        Assert.Equal(HttpStatusCode.BadRequest, secondResponse.StatusCode);
        using (var secondJson = JsonDocument.Parse(await secondResponse.Content.ReadAsStringAsync()))
        {
            Assert.Equal("coupon_exhausted", secondJson.RootElement.GetProperty("code").GetString());
        }

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            var coupon = await db.BillingCoupons.AsNoTracking().FirstAsync(x => x.Code == couponCode);
            var redemption = await db.BillingCouponRedemptions.AsNoTracking().SingleAsync(x => x.CouponId == coupon.Id);

            Assert.Equal(1, coupon.RedemptionCount);
            Assert.Equal(couponVersion1Id, redemption.CouponVersionId);
            Assert.Equal(couponVersion2Id, coupon.ActiveVersionId);
            Assert.Equal(couponVersion2Id, coupon.LatestVersionId);
        }
    }

    private static BillingPlanVersion CreatePlanVersion(BillingPlan plan, string versionId, int versionNumber, DateTimeOffset createdAt) => new()
    {
        Id = versionId,
        PlanId = plan.Id,
        VersionNumber = versionNumber,
        Code = plan.Code,
        Name = plan.Name,
        Description = plan.Description,
        Price = plan.Price,
        Currency = plan.Currency,
        Interval = plan.Interval,
        DurationMonths = plan.DurationMonths,
        IsVisible = plan.IsVisible,
        IsRenewable = plan.IsRenewable,
        TrialDays = plan.TrialDays,
        DisplayOrder = plan.DisplayOrder,
        IncludedCredits = plan.IncludedCredits,
        IncludedSubtestsJson = plan.IncludedSubtestsJson,
        EntitlementsJson = plan.EntitlementsJson,
        Status = plan.Status,
        ArchivedAt = plan.ArchivedAt,
        CreatedAt = createdAt
    };

    private static BillingAddOnVersion CreateAddOnVersion(BillingAddOn addOn, string versionId, int versionNumber, DateTimeOffset createdAt) => new()
    {
        Id = versionId,
        AddOnId = addOn.Id,
        VersionNumber = versionNumber,
        Code = addOn.Code,
        Name = addOn.Name,
        Description = addOn.Description,
        Price = addOn.Price,
        Currency = addOn.Currency,
        Interval = addOn.Interval,
        Status = addOn.Status,
        IsRecurring = addOn.IsRecurring,
        DurationDays = addOn.DurationDays,
        GrantCredits = addOn.GrantCredits,
        GrantEntitlementsJson = addOn.GrantEntitlementsJson,
        CompatiblePlanCodesJson = addOn.CompatiblePlanCodesJson,
        AppliesToAllPlans = addOn.AppliesToAllPlans,
        IsStackable = addOn.IsStackable,
        QuantityStep = addOn.QuantityStep,
        MaxQuantity = addOn.MaxQuantity,
        DisplayOrder = addOn.DisplayOrder,
        CreatedAt = createdAt
    };

    private static BillingCouponVersion CreateCouponVersion(BillingCoupon coupon, string versionId, int versionNumber, DateTimeOffset createdAt) => new()
    {
        Id = versionId,
        CouponId = coupon.Id,
        VersionNumber = versionNumber,
        Code = coupon.Code,
        Name = coupon.Name,
        Description = coupon.Description,
        DiscountType = coupon.DiscountType,
        DiscountValue = coupon.DiscountValue,
        Currency = coupon.Currency,
        Status = coupon.Status,
        StartsAt = coupon.StartsAt,
        EndsAt = coupon.EndsAt,
        UsageLimitTotal = coupon.UsageLimitTotal,
        UsageLimitPerUser = coupon.UsageLimitPerUser,
        MinimumSubtotal = coupon.MinimumSubtotal,
        ApplicablePlanCodesJson = coupon.ApplicablePlanCodesJson,
        ApplicableAddOnCodesJson = coupon.ApplicableAddOnCodesJson,
        IsStackable = coupon.IsStackable,
        Notes = coupon.Notes,
        CreatedAt = createdAt
    };

    private static async Task CompletePayPalCheckoutAsync(HttpClient client, string checkoutSessionId)
    {
        var payload = JsonSerializer.Serialize(new
        {
            id = $"evt-{Guid.NewGuid():N}",
            event_type = "PAYMENT.CAPTURE.COMPLETED",
            resource = new
            {
                supplementary_data = new
                {
                    related_ids = new
                    {
                        order_id = checkoutSessionId
                    }
                }
            }
        });

        using var response = await client.PostAsync(
            "/v1/payment/webhooks/paypal",
            new StringContent(payload, Encoding.UTF8, "application/json"));
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, body);

        using var json = JsonDocument.Parse(body);
        Assert.Equal("completed", json.RootElement.GetProperty("state").GetString());
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
