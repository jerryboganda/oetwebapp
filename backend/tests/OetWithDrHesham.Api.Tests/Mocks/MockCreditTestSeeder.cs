using Microsoft.EntityFrameworkCore;
using OetWithDrHesham.Api.Data;
using OetWithDrHesham.Api.Domain;

namespace OetWithDrHesham.Api.Tests.Mocks;

/// <summary>
/// Mock attempts debit a mock credit at creation (audit fix 2026-07-03), so
/// endpoint tests that create attempts must first grant the test learner a
/// credit bucket. Seeds the minimal grant chain the entitlement service
/// resolves: BillingAddOn (GrantEntitlementsJson) ← SubscriptionItem (Active)
/// ← Subscription (Active parent). The subscription's plan id deliberately
/// resolves to no BillingPlan row so the premium-unlimited bypass stays off
/// and the credit path is what gets exercised.
/// </summary>
public static class MockCreditTestSeeder
{
    public const string AddOnCode = "test_mock_credit_pack";

    public static async Task SeedMockCreditsAsync(LearnerDbContext db, string userId, int perType = 25)
    {
        var now = DateTimeOffset.UtcNow;

        if (!await db.BillingAddOns.AnyAsync(a => a.Code == AddOnCode))
        {
            db.BillingAddOns.Add(new BillingAddOn
            {
                Id = $"addon-{AddOnCode}",
                Code = AddOnCode,
                Name = "Test mock credit pack",
                Status = BillingAddOnStatus.Active,
                GrantEntitlementsJson =
                    $"{{\"mockFull\":{perType},\"mockLrw\":{perType},\"mockSub\":{perType},\"mockPart\":{perType}," +
                    $"\"mockDiagnostic\":{perType},\"mockFinalReadiness\":{perType},\"mockRemedial\":{perType}," +
                    $"\"mockWriting\":{perType},\"mockSpeakingSession\":{perType}}}",
                DurationDays = 365,
                CreatedAt = now,
                UpdatedAt = now,
            });
        }

        var subscriptionId = $"sub-mock-credits-{userId}";
        if (!await db.Subscriptions.AnyAsync(s => s.Id == subscriptionId))
        {
            db.Subscriptions.Add(new Subscription
            {
                Id = subscriptionId,
                UserId = userId,
                PlanId = "test-mock-credit-carrier",
                Status = SubscriptionStatus.Active,
                NextRenewalAt = now.AddDays(365),
                StartedAt = now.AddHours(-1),
                ChangedAt = now.AddHours(-1),
                PriceAmount = 0,
                Currency = "GBP",
                Interval = "one_time",
            });
            db.SubscriptionItems.Add(new SubscriptionItem
            {
                Id = $"subitem-mock-credits-{userId}",
                SubscriptionId = subscriptionId,
                ItemCode = AddOnCode,
                ItemType = "add_on",
                Quantity = 1,
                Status = SubscriptionItemStatus.Active,
                StartsAt = now.AddHours(-1),
                CreatedAt = now,
                UpdatedAt = now,
            });
        }

        await db.SaveChangesAsync();
    }
}
