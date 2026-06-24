using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Tests.Infrastructure;
using Xunit;

namespace OetLearner.Api.Tests.Billing;

/// <summary>
/// OET 2026 entitlement conformance — end-to-end coverage that a Stripe
/// webhook-driven checkout completion provisions the FULL bundled entitlement
/// template (writing assessments, speaking sessions, Tutor Book / Basic English
/// unlocks, access-duration expiry) and not just AI credits + add-on rows.
///
/// <para>The fulfillment entry point (<see cref="LearnerService"/>'s private
/// <c>ApplyCheckoutCompletionAsync</c>) is reached via the internal, fully
/// wired <c>ApplyVerifiedPaymentWebhookEventAsync</c> — the same method the
/// admin "retry verified webhook" path and the live webhook poller call. We
/// resolve a real <see cref="LearnerService"/> from the application's DI
/// container (so every collaborator — AI ledger, wallet, invoice numbering — is
/// the production implementation) and drive it against the in-memory database.</para>
///
/// <para>These assertions are the regression guard for the bug being fixed:
/// before the change, a purchase activated the subscription and granted AI
/// credits but left WritingAssessmentsRemaining / SpeakingSessionsRemaining /
/// TutorBookUnlocked / BasicEnglishUnlocked / ExpiresAt untouched.</para>
/// </summary>
public sealed class CheckoutEntitlementFulfillmentTests : IClassFixture<FirstPartyAuthTestWebApplicationFactory>
{
    private readonly FirstPartyAuthTestWebApplicationFactory _factory;

    public CheckoutEntitlementFulfillmentTests(FirstPartyAuthTestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task PlanPurchase_ProvisionsFullBundle_AndGrantsAiCreditsExactlyOnce()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var ctx = new FulfillmentContext(suffix);
        var now = DateTimeOffset.UtcNow;

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            await db.Database.EnsureCreatedAsync();

            SeedLearnerWithSubscription(db, ctx, now);

            // Plan version models the "Full Condensed (Medicine)" SKU:
            // 5 writing assessments + 1 speaking session, 180-day access,
            // plus 7 bundled AI credits (granted via the AI ledger, NOT by the
            // entitlement helper).
            SeedPlan(
                db,
                ctx,
                bundledWriting: 5,
                bundledSpeaking: 1,
                bundledAiCredits: 7,
                bundledTutorBook: false,
                bundledBasicEnglish: false,
                accessDurationDays: 180,
                now: now);

            SeedQuote(db, ctx, addOnItems: Array.Empty<BillingQuoteLineItem>(), now: now);
            SeedSubscriptionPaymentTransaction(db, ctx, now);
            SeedVerifiedCompletedWebhookEvent(db, ctx, now);

            await db.SaveChangesAsync();
        }

        await DriveCompletionAsync(ctx);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            var subscription = await db.Subscriptions.SingleAsync(s => s.UserId == ctx.UserId);

            Assert.Equal(SubscriptionStatus.Active, subscription.Status);
            Assert.Equal(ctx.PlanCode, subscription.PlanId);
            Assert.Equal(5, subscription.WritingAssessmentsRemaining);
            Assert.Equal(1, subscription.SpeakingSessionsRemaining);
            Assert.False(subscription.TutorBookUnlocked);
            Assert.False(subscription.BasicEnglishUnlocked);
            Assert.NotNull(subscription.ExpiresAt);
            Assert.InRange(subscription.ExpiresAt!.Value, now.AddDays(179), now.AddDays(181));

            // AI credits granted exactly once: 7 on the subscription and a single
            // purchase ledger row for this quote.
            Assert.Equal(7, subscription.AiCreditsRemaining);
            Assert.Equal(
                1,
                await db.AiCreditLedger.CountAsync(e =>
                    e.UserId == ctx.UserId && e.Source == AiCreditSource.Purchase && e.TokensDelta == 7));

            // Quote consumed so a replay would early-return.
            Assert.Equal(BillingQuoteStatus.Completed, (await db.BillingQuotes.SingleAsync(q => q.Id == ctx.QuoteId)).Status);
        }
    }

    [Fact]
    public async Task PlanPurchase_RenewsConsumedFreezeEntitlement()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var ctx = new FulfillmentContext(suffix);
        var now = DateTimeOffset.UtcNow;

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            await db.Database.EnsureCreatedAsync();

            SeedLearnerWithSubscription(db, ctx, now);
            SeedPlan(db, ctx, bundledWriting: 0, bundledSpeaking: 0, bundledAiCredits: 0,
                bundledTutorBook: false, bundledBasicEnglish: false, accessDurationDays: 180, now: now);
            SeedQuote(db, ctx, addOnItems: Array.Empty<BillingQuoteLineItem>(), now: now);
            SeedSubscriptionPaymentTransaction(db, ctx, now);
            SeedVerifiedCompletedWebhookEvent(db, ctx, now);

            // The learner has already used their one freeze on a prior subscription.
            db.AccountFreezeEntitlements.Add(new AccountFreezeEntitlement
            {
                Id = $"FZE-{suffix}",
                UserId = ctx.UserId,
                ConsumedAt = now.AddMonths(-2),
                ResetAt = null,
            });

            await db.SaveChangesAsync();
        }

        await DriveCompletionAsync(ctx);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            var entitlement = await db.AccountFreezeEntitlements.SingleAsync(x => x.UserId == ctx.UserId);

            // Buying a new plan renews the one-time freeze: ResetAt stamped, so a new
            // freeze becomes available again.
            Assert.NotNull(entitlement.ResetAt);
            Assert.Equal("new_subscription_purchase", entitlement.ResetReason);
        }
    }

    [Fact]
    public async Task PlanPurchase_PermanentTutorBookSku_UnlocksTutorBook_AndLeavesExpiryNull()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var ctx = new FulfillmentContext(suffix);
        var now = DateTimeOffset.UtcNow;

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            await db.Database.EnsureCreatedAsync();

            SeedLearnerWithSubscription(db, ctx, now);
            SeedPlan(
                db,
                ctx,
                bundledWriting: 0,
                bundledSpeaking: 0,
                bundledAiCredits: 0,
                bundledTutorBook: true,
                bundledBasicEnglish: true,
                accessDurationDays: 9999, // permanent
                now: now);
            SeedQuote(db, ctx, addOnItems: Array.Empty<BillingQuoteLineItem>(), now: now);
            SeedSubscriptionPaymentTransaction(db, ctx, now);
            SeedVerifiedCompletedWebhookEvent(db, ctx, now);

            await db.SaveChangesAsync();
        }

        await DriveCompletionAsync(ctx);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            var subscription = await db.Subscriptions.SingleAsync(s => s.UserId == ctx.UserId);

            Assert.True(subscription.TutorBookUnlocked);
            Assert.True(subscription.BasicEnglishUnlocked);
            Assert.Null(subscription.ExpiresAt); // AccessDurationDays >= 9999 => permanent
        }
    }

    [Fact]
    public async Task AddOnPurchase_LetterPack_IncrementsWritingAssessments()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var ctx = new FulfillmentContext(suffix);
        var now = DateTimeOffset.UtcNow;

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            await db.Database.EnsureCreatedAsync();

            // Subscription already holds 2 writing assessments; buying the 5-letter
            // pack must take it to 7.
            SeedLearnerWithSubscription(db, ctx, now, writingAssessmentsRemaining: 2);
            SeedAddOn(db, ctx, lettersGranted: 5, sessionsGranted: 0, addonKind: "writing_assessments", grantCredits: 0, now: now);
            SeedQuote(
                db,
                ctx,
                addOnItems: new[] { AddOnLineItem(ctx) },
                planCode: null, // add-on-only purchase: no plan provisioning
                now: now);
            SeedSubscriptionPaymentTransaction(db, ctx, now);
            SeedVerifiedCompletedWebhookEvent(db, ctx, now);

            await db.SaveChangesAsync();
        }

        await DriveCompletionAsync(ctx);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            var subscription = await db.Subscriptions.SingleAsync(s => s.UserId == ctx.UserId);

            Assert.Equal(7, subscription.WritingAssessmentsRemaining); // 2 + 5
            Assert.Equal(0, subscription.SpeakingSessionsRemaining);
            Assert.False(subscription.TutorBookUnlocked);
            // Non-AI add-on must not have touched AI credits.
            Assert.Equal(0, subscription.AiCreditsRemaining);
            Assert.Empty(await db.AiCreditLedger.Where(e => e.UserId == ctx.UserId).ToListAsync());
        }
    }

    [Fact]
    public async Task AddOnPurchase_TutorBookAddOn_UnlocksTutorBook()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var ctx = new FulfillmentContext(suffix);
        var now = DateTimeOffset.UtcNow;

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            await db.Database.EnsureCreatedAsync();

            SeedLearnerWithSubscription(db, ctx, now);
            SeedAddOn(db, ctx, lettersGranted: 0, sessionsGranted: 0, addonKind: "tutor_book", grantCredits: 0, now: now);
            SeedQuote(db, ctx, addOnItems: new[] { AddOnLineItem(ctx) }, planCode: null, now: now);
            SeedSubscriptionPaymentTransaction(db, ctx, now);
            SeedVerifiedCompletedWebhookEvent(db, ctx, now);

            await db.SaveChangesAsync();
        }

        await DriveCompletionAsync(ctx);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            var subscription = await db.Subscriptions.SingleAsync(s => s.UserId == ctx.UserId);

            Assert.True(subscription.TutorBookUnlocked);
        }
    }

    [Fact]
    public async Task AddOnPurchase_AccessExtension_AdvancesExpiryFromLaterOfNowOrCurrentExpiry()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var ctx = new FulfillmentContext(suffix);
        var now = DateTimeOffset.UtcNow;
        var currentExpiry = now.AddDays(10); // future expiry => baseline is current expiry

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            await db.Database.EnsureCreatedAsync();

            SeedLearnerWithSubscription(db, ctx, now, expiresAt: currentExpiry);
            SeedAddOn(db, ctx, lettersGranted: 0, sessionsGranted: 0, addonKind: "access_extension", grantCredits: 0, now: now, extensionDays: 90);
            SeedQuote(db, ctx, addOnItems: new[] { AddOnLineItem(ctx) }, planCode: null, now: now);
            SeedSubscriptionPaymentTransaction(db, ctx, now);
            SeedVerifiedCompletedWebhookEvent(db, ctx, now);

            await db.SaveChangesAsync();
        }

        await DriveCompletionAsync(ctx);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            var subscription = await db.Subscriptions.SingleAsync(s => s.UserId == ctx.UserId);

            Assert.NotNull(subscription.ExpiresAt);
            // Pushed out 90 days from the later-of(now, currentExpiry) == currentExpiry.
            Assert.InRange(subscription.ExpiresAt!.Value, currentExpiry.AddDays(89), currentExpiry.AddDays(91));
            // Extension must not touch other entitlements or AI credits.
            Assert.Equal(0, subscription.WritingAssessmentsRemaining);
            Assert.Equal(0, subscription.AiCreditsRemaining);
        }
    }

    [Fact]
    public async Task AddOnPurchase_AccessExtension_Replayed_DoesNotDoubleExtend()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var ctx = new FulfillmentContext(suffix);
        var now = DateTimeOffset.UtcNow;
        var currentExpiry = now.AddDays(10);

        var secondEventId = Guid.NewGuid();
        var secondGatewayEventId = $"evt-{suffix}-replay";

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            await db.Database.EnsureCreatedAsync();

            SeedLearnerWithSubscription(db, ctx, now, expiresAt: currentExpiry);
            SeedAddOn(db, ctx, lettersGranted: 0, sessionsGranted: 0, addonKind: "access_extension", grantCredits: 0, now: now, extensionDays: 90);
            SeedQuote(db, ctx, addOnItems: new[] { AddOnLineItem(ctx) }, planCode: null, now: now);
            SeedSubscriptionPaymentTransaction(db, ctx, now);
            SeedVerifiedCompletedWebhookEvent(db, ctx, now);
            db.PaymentWebhookEvents.Add(BuildVerifiedCompletedWebhookEvent(ctx, secondEventId, secondGatewayEventId, now));

            await db.SaveChangesAsync();
        }

        await DriveCompletionAsync(ctx);
        await DriveCompletionAsync(ctx, eventId: secondEventId);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            var subscription = await db.Subscriptions.SingleAsync(s => s.UserId == ctx.UserId);

            // Extended exactly once (90 days), not 180.
            Assert.InRange(subscription.ExpiresAt!.Value, currentExpiry.AddDays(89), currentExpiry.AddDays(91));
        }
    }

    [Fact]
    public async Task AddOnPurchase_LetterPack_DoesNotTouchExpiry()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var ctx = new FulfillmentContext(suffix);
        var now = DateTimeOffset.UtcNow;
        var currentExpiry = now.AddDays(10);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            await db.Database.EnsureCreatedAsync();

            // A letters add-on with a non-zero ExtensionDays would still be a no-op
            // for expiry, because the branch is guarded on the access_extension kind.
            SeedLearnerWithSubscription(db, ctx, now, writingAssessmentsRemaining: 2, expiresAt: currentExpiry);
            SeedAddOn(db, ctx, lettersGranted: 5, sessionsGranted: 0, addonKind: "writing_assessments", grantCredits: 0, now: now, extensionDays: 0);
            SeedQuote(db, ctx, addOnItems: new[] { AddOnLineItem(ctx) }, planCode: null, now: now);
            SeedSubscriptionPaymentTransaction(db, ctx, now);
            SeedVerifiedCompletedWebhookEvent(db, ctx, now);

            await db.SaveChangesAsync();
        }

        await DriveCompletionAsync(ctx);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            var subscription = await db.Subscriptions.SingleAsync(s => s.UserId == ctx.UserId);

            Assert.Equal(7, subscription.WritingAssessmentsRemaining);
            // Expiry untouched by a non-extension add-on.
            Assert.InRange(subscription.ExpiresAt!.Value, currentExpiry.AddSeconds(-1), currentExpiry.AddSeconds(1));
        }
    }

    [Fact]
    public async Task ReplayedCompletion_DoesNotDoubleGrant_AndKeepsAiCreditsExactlyOnce()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var ctx = new FulfillmentContext(suffix);
        var now = DateTimeOffset.UtcNow;

        // A second verified webhook event for the SAME transaction/quote, used to
        // re-drive completion after the quote is already Completed.
        var secondEventId = Guid.NewGuid();
        var secondGatewayEventId = $"evt-{suffix}-replay";

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            await db.Database.EnsureCreatedAsync();

            SeedLearnerWithSubscription(db, ctx, now);
            SeedPlan(
                db,
                ctx,
                bundledWriting: 5,
                bundledSpeaking: 1,
                bundledAiCredits: 7,
                bundledTutorBook: false,
                bundledBasicEnglish: false,
                accessDurationDays: 180,
                now: now);
            SeedQuote(db, ctx, addOnItems: Array.Empty<BillingQuoteLineItem>(), now: now);
            SeedSubscriptionPaymentTransaction(db, ctx, now);
            SeedVerifiedCompletedWebhookEvent(db, ctx, now);
            // Second event row (also verified + failed) so we can drive completion again.
            db.PaymentWebhookEvents.Add(BuildVerifiedCompletedWebhookEvent(ctx, secondEventId, secondGatewayEventId, now));

            await db.SaveChangesAsync();
        }

        // First completion provisions the bundle.
        await DriveCompletionAsync(ctx);
        // Second completion (quote already Completed) must early-return with no effect.
        await DriveCompletionAsync(ctx, eventId: secondEventId);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            var subscription = await db.Subscriptions.SingleAsync(s => s.UserId == ctx.UserId);

            // Bundle stamped once — not doubled.
            Assert.Equal(5, subscription.WritingAssessmentsRemaining);
            Assert.Equal(1, subscription.SpeakingSessionsRemaining);

            // AI credits granted exactly once across both drives.
            Assert.Equal(7, subscription.AiCreditsRemaining);
            Assert.Equal(
                1,
                await db.AiCreditLedger.CountAsync(e =>
                    e.UserId == ctx.UserId && e.Source == AiCreditSource.Purchase));

            // Only one paid invoice for the quote.
            Assert.Equal(1, await db.Invoices.CountAsync(i => i.QuoteId == ctx.QuoteId));
        }
    }

    // ── Driver ────────────────────────────────────────────────────────────────

    private Task DriveCompletionAsync(FulfillmentContext ctx, Guid? eventId = null)
        => DriveCompletionCoreAsync(ctx, eventId ?? ctx.WebhookEventId);

    private async Task DriveCompletionCoreAsync(FulfillmentContext ctx, Guid eventId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<LearnerService>();

        // Same arguments the admin retry path / live poller pass for a verified,
        // completed payment webhook. Category "payment" routes to the completion
        // switch (not refund/dispute).
        var result = await service.ApplyVerifiedPaymentWebhookEventAsync(
            eventId,
            ctx.GatewayTransactionId,
            normalizedStatus: "completed",
            eventCategory: "payment",
            gatewayObjectId: ctx.GatewayTransactionId,
            CancellationToken.None);

        // Fulfillment must have completed, not been ignored — otherwise the bundle
        // assertions below would be vacuously satisfied.
        Assert.Equal("completed", result.ProcessingStatus);
    }

    // ── Seed helpers ────────────────────────────────────────────────────────────

    private static void SeedLearnerWithSubscription(
        LearnerDbContext db,
        FulfillmentContext ctx,
        DateTimeOffset now,
        int writingAssessmentsRemaining = 0,
        DateTimeOffset? expiresAt = null)
    {
        db.Users.Add(new LearnerUser
        {
            Id = ctx.UserId,
            DisplayName = "Entitlement Learner",
            Email = $"{ctx.UserId}@example.test",
            CreatedAt = now,
            LastActiveAt = now,
            AccountStatus = "active",
        });
        db.Subscriptions.Add(new Subscription
        {
            Id = ctx.SubscriptionId,
            UserId = ctx.UserId,
            PlanId = "basic-monthly",
            Status = SubscriptionStatus.Pending,
            StartedAt = default,
            ChangedAt = now,
            NextRenewalAt = now, // forces NextRenewalAt advance on activation
            PriceAmount = 0m,
            Currency = "AUD",
            Interval = "monthly",
            WritingAssessmentsRemaining = writingAssessmentsRemaining,
            SpeakingSessionsRemaining = 0,
            AiCreditsRemaining = 0,
            ExpiresAt = expiresAt,
        });
    }

    private static void SeedPlan(
        LearnerDbContext db,
        FulfillmentContext ctx,
        int bundledWriting,
        int bundledSpeaking,
        int bundledAiCredits,
        bool bundledTutorBook,
        bool bundledBasicEnglish,
        int accessDurationDays,
        DateTimeOffset now)
    {
        db.BillingPlans.Add(new BillingPlan
        {
            Id = ctx.PlanId,
            Code = ctx.PlanCode,
            Name = "Full Condensed (Medicine)",
            Price = 199m,
            Currency = "AUD",
            Interval = "one_time",
            DurationMonths = 6,
            Status = BillingPlanStatus.Active,
            IncludedCredits = 0, // avoid wallet-credit side path in this test
            ActiveVersionId = ctx.PlanVersionId,
            LatestVersionId = ctx.PlanVersionId,
            AccessDurationDays = accessDurationDays,
            BundledWritingAssessments = bundledWriting,
            BundledSpeakingSessions = bundledSpeaking,
            BundledAiCredits = bundledAiCredits,
            BundledTutorBook = bundledTutorBook,
            BundledBasicEnglish = bundledBasicEnglish,
            CreatedAt = now,
            UpdatedAt = now,
        });
        db.BillingPlanVersions.Add(new BillingPlanVersion
        {
            Id = ctx.PlanVersionId,
            PlanId = ctx.PlanId,
            VersionNumber = 1,
            Code = ctx.PlanCode,
            Name = "Full Condensed (Medicine)",
            Price = 199m,
            Currency = "AUD",
            Interval = "one_time",
            DurationMonths = 6,
            IncludedCredits = 0,
            Status = BillingPlanStatus.Active,
            AccessDurationDays = accessDurationDays,
            BundledWritingAssessments = bundledWriting,
            BundledSpeakingSessions = bundledSpeaking,
            BundledAiCredits = bundledAiCredits,
            BundledTutorBook = bundledTutorBook,
            BundledBasicEnglish = bundledBasicEnglish,
            CreatedAt = now,
        });
    }

    private static void SeedAddOn(
        LearnerDbContext db,
        FulfillmentContext ctx,
        int lettersGranted,
        int sessionsGranted,
        string addonKind,
        int grantCredits,
        DateTimeOffset now,
        int extensionDays = 0)
    {
        db.BillingAddOns.Add(new BillingAddOn
        {
            Id = ctx.AddOnId,
            Code = ctx.AddOnCode,
            Name = "Add-on Pack",
            Price = 29m,
            Currency = "AUD",
            Interval = "one_time",
            Status = BillingAddOnStatus.Active,
            IsRecurring = false,
            DurationDays = 0,
            GrantCredits = grantCredits,
            GrantEntitlementsJson = "{}",
            ActiveVersionId = ctx.AddOnVersionId,
            LatestVersionId = ctx.AddOnVersionId,
            AddonKind = addonKind,
            LettersGranted = lettersGranted,
            SessionsGranted = sessionsGranted,
            ExtensionDays = extensionDays,
            CreatedAt = now,
            UpdatedAt = now,
        });
        db.BillingAddOnVersions.Add(new BillingAddOnVersion
        {
            Id = ctx.AddOnVersionId,
            AddOnId = ctx.AddOnId,
            VersionNumber = 1,
            Code = ctx.AddOnCode,
            Name = "Add-on Pack",
            Price = 29m,
            Currency = "AUD",
            Interval = "one_time",
            Status = BillingAddOnStatus.Active,
            IsRecurring = false,
            DurationDays = 0,
            GrantCredits = grantCredits,
            GrantEntitlementsJson = "{}",
            AddonKind = addonKind,
            LettersGranted = lettersGranted,
            SessionsGranted = sessionsGranted,
            ExtensionDays = extensionDays,
            CreatedAt = now,
        });
    }

    private static BillingQuoteLineItem AddOnLineItem(FulfillmentContext ctx)
        => new("addon", ctx.AddOnCode, "Add-on Pack", 29m, "AUD", 1);

    private static void SeedQuote(
        LearnerDbContext db,
        FulfillmentContext ctx,
        IReadOnlyList<BillingQuoteLineItem> addOnItems,
        DateTimeOffset now,
        string? planCode = "__use_default__")
    {
        var effectivePlanCode = planCode == "__use_default__" ? ctx.PlanCode : planCode;
        var hasPlan = !string.IsNullOrWhiteSpace(effectivePlanCode);

        var addOnVersionIds = addOnItems.Count == 0
            ? new Dictionary<string, string>()
            : new Dictionary<string, string> { [ctx.AddOnCode] = ctx.AddOnVersionId };

        // SnapshotJson drives DeserializeQuoteResponse: the add-on loop iterates
        // its "items" filtered to kind == "addon". Serialize via JsonSupport so the
        // shape matches exactly what the production quote builder writes and what
        // DeserializeQuoteResponse reads back (Web/camelCase, case-insensitive).
        var snapshot = new
        {
            items = addOnItems,
            summary = "Test purchase",
            validation = new { },
        };

        db.BillingQuotes.Add(new BillingQuote
        {
            Id = ctx.QuoteId,
            UserId = ctx.UserId,
            SubscriptionId = ctx.SubscriptionId,
            PlanCode = hasPlan ? effectivePlanCode : null,
            PlanVersionId = hasPlan ? ctx.PlanVersionId : null,
            AddOnCodesJson = JsonSupport.Serialize(addOnItems.Select(i => i.Code).ToList()),
            AddOnVersionIdsJson = JsonSupport.Serialize(addOnVersionIds),
            CouponCode = null,
            CouponVersionId = null,
            Currency = "AUD",
            SubtotalAmount = 199m,
            DiscountAmount = 0m,
            TotalAmount = 199m,
            Status = BillingQuoteStatus.Applied,
            CreatedAt = now,
            ExpiresAt = now.AddMinutes(30),
            SnapshotJson = JsonSupport.Serialize(snapshot),
        });
    }

    private static void SeedSubscriptionPaymentTransaction(LearnerDbContext db, FulfillmentContext ctx, DateTimeOffset now)
    {
        db.PaymentTransactions.Add(new PaymentTransaction
        {
            Id = Guid.NewGuid(),
            LearnerUserId = ctx.UserId,
            Gateway = "stripe",
            GatewayTransactionId = ctx.GatewayTransactionId,
            TransactionType = "subscription_payment",
            Status = "pending",
            Amount = 199m,
            Currency = "AUD",
            ProductType = "plan",
            ProductId = ctx.PlanCode,
            QuoteId = ctx.QuoteId,
            AddOnVersionIdsJson = JsonSupport.Serialize(new Dictionary<string, string>()),
            MetadataJson = JsonSupport.Serialize(new { quoteId = ctx.QuoteId }),
            CreatedAt = now,
            UpdatedAt = now,
        });
    }

    private static void SeedVerifiedCompletedWebhookEvent(LearnerDbContext db, FulfillmentContext ctx, DateTimeOffset now)
        => db.PaymentWebhookEvents.Add(BuildVerifiedCompletedWebhookEvent(ctx, ctx.WebhookEventId, ctx.GatewayEventId, now));

    private static PaymentWebhookEvent BuildVerifiedCompletedWebhookEvent(
        FulfillmentContext ctx,
        Guid eventId,
        string gatewayEventId,
        DateTimeOffset now)
        => new()
        {
            Id = eventId,
            Gateway = "stripe",
            EventType = "checkout.session.completed",
            GatewayEventId = gatewayEventId,
            ProcessingStatus = "failed",
            VerificationStatus = "verified",
            VerifiedAt = now,
            PayloadSha256 = new string('a', 64),
            ParserVersion = "payment-webhook-v1",
            GatewayTransactionId = ctx.GatewayTransactionId,
            NormalizedStatus = "completed",
            AttemptCount = 1,
            PayloadJson = "{}",
            ErrorMessage = "Transient local fulfillment failure.",
            ReceivedAt = now,
            ProcessedAt = now,
        };

    private sealed class FulfillmentContext(string suffix)
    {
        public string UserId { get; } = $"entitlement-user-{suffix}";
        public string SubscriptionId { get; } = $"sub-{suffix}";
        public string PlanId { get; } = $"plan-{suffix}";
        public string PlanCode { get; } = $"full-condensed-medicine-{suffix}";
        public string PlanVersionId { get; } = $"plan-version-{suffix}";
        public string AddOnId { get; } = $"addon-{suffix}";
        public string AddOnCode { get; } = $"addon-pack-{suffix}";
        public string AddOnVersionId { get; } = $"addon-version-{suffix}";
        public string QuoteId { get; } = $"quote-{suffix}";
        public string GatewayTransactionId { get; } = $"gw-txn-{suffix}";
        public string GatewayEventId { get; } = $"evt-{suffix}";
        public Guid WebhookEventId { get; } = Guid.NewGuid();
    }
}
