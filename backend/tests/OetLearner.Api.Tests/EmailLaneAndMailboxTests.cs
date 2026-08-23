using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Settings;

namespace OetLearner.Api.Tests;

public class EmailLaneAndMailboxTests
{
    [Fact]
    public void AuthTemplate_UsesAuthSender()
    {
        var settings = Snapshot();
        var message = new EmailMessage("learner@example.test", "Verify", "code", TemplateKey: EmailTemplateKeys.EmailVerificationOtp);
        var lane = EmailLanes.Resolve(message);
        var sender = EmailLanes.ResolveSender(settings, lane);

        Assert.Equal(EmailLane.Auth, lane);
        Assert.Equal(EmailLanes.DefaultAuthFromAddress, sender.Email);
        Assert.Equal(EmailLanes.DefaultAuthFromName, sender.Name);
    }

    [Fact]
    public void MarketingEvent_UsesUpdatesSender()
    {
        var settings = Snapshot();
        var message = new EmailMessage("learner@example.test", "Credits low", "body", EventKey: "LearnerCreditsLow", Category: "billing");
        var lane = EmailLanes.Resolve(message);
        var sender = EmailLanes.ResolveSender(settings, lane);

        Assert.Equal(EmailLane.Marketing, lane);
        Assert.Equal(EmailLanes.DefaultMarketingFromAddress, sender.Email);
    }

    [Fact]
    public void ProductAndSupport_UseDedicatedSenders()
    {
        var settings = Snapshot();
        var product = EmailLanes.ResolveSender(settings, EmailLanes.Resolve(new EmailMessage("a@b.c", "Review", "body", Category: "reviews")));
        var support = EmailLanes.ResolveSender(settings, EmailLanes.Resolve(new EmailMessage("a@b.c", "Alert", "body", Category: "operations")));

        Assert.Equal(EmailLanes.DefaultProductFromAddress, product.Email);
        Assert.Equal(EmailLanes.DefaultSupportFromAddress, support.Email);
    }

    [Fact]
    public async Task UnsubscribedWebhook_DoesNotCreateGlobalSuppression()
    {
        await using var db = CreateDb();
        var now = new DateTimeOffset(2026, 8, 23, 12, 0, 0, TimeSpan.Zero);
        var account = await SeedAccountAsync(db, "auth_lane_001", "learner@example.test");
        db.LearnerRegistrationProfiles.Add(new LearnerRegistrationProfile
        {
            Id = "reg-lane-001",
            ApplicationUserAccountId = account.Id,
            LearnerUserId = "learner-lane-001",
            FirstName = "Lane",
            LastName = "Test",
            ExamTypeId = "medicine",
            ProfessionId = "doctor",
            SessionId = "session-lane-001",
            CountryTarget = "UK",
            MobileNumber = "+441111111111",
            AgreeToTerms = true,
            AgreeToPrivacy = true,
            MarketingOptIn = true
        });
        db.EmailOtpChallenges.Add(new EmailOtpChallenge
        {
            Id = Guid.NewGuid(),
            ApplicationUserAccountId = account.Id,
            Purpose = "verify_email",
            CodeHash = "hash",
            CreatedAt = now.AddMinutes(-2),
            ExpiresAt = now.AddMinutes(8),
            DeliveryChannel = "email",
            SentAt = now.AddMinutes(-2),
            DeliveryStatus = "accepted"
        });
        await db.SaveChangesAsync();

        var service = CreateNotificationService(db, now);
        var count = await service.HandleBrevoWebhookEventsAsync(
            """{"event":"unsubscribed","email":"learner@example.test","reason":"unsubscribed via email"}""",
            "webhook-secret",
            CancellationToken.None);

        Assert.Equal(1, count);
        var suppressions = await db.NotificationSuppressions.ToListAsync();
        Assert.DoesNotContain(suppressions, item => item.EventKey == null);
        Assert.Contains(suppressions, item => item.EventKey == EmailLanes.MarketingSuppressionEventKey && item.IsActive);
        Assert.False((await db.LearnerRegistrationProfiles.SingleAsync()).MarketingOptIn);
        Assert.Equal("unsubscribed", (await db.EmailOtpChallenges.SingleAsync()).DeliveryStatus);
    }

    [Fact]
    public async Task Unblock_RemovesTransactionalBlock_AndKeepsMarketingOptOut()
    {
        await using var db = CreateDb();
        var now = new DateTimeOffset(2026, 8, 23, 13, 0, 0, TimeSpan.Zero);
        var account = await SeedAccountAsync(db, "auth_lane_002", "blocked@example.test");
        db.NotificationSuppressions.AddRange(
            new NotificationSuppression
            {
                Id = Guid.NewGuid(),
                AuthAccountId = account.Id,
                Channel = NotificationChannel.Email,
                EventKey = null,
                IsActive = true,
                ReasonCode = "brevo_unsubscribed",
                CreatedByAdminId = "system",
                CreatedByAdminName = "Brevo",
                CreatedAt = now,
                UpdatedAt = now
            },
            new NotificationSuppression
            {
                Id = Guid.NewGuid(),
                AuthAccountId = account.Id,
                Channel = NotificationChannel.Email,
                EventKey = EmailLanes.MarketingSuppressionEventKey,
                IsActive = true,
                ReasonCode = "brevo_unsubscribed",
                CreatedByAdminId = "system",
                CreatedByAdminName = "Brevo",
                CreatedAt = now,
                UpdatedAt = now
            });
        await db.SaveChangesAsync();

        var mailbox = new FakeMailbox();
        mailbox.Blocked["blocked@example.test"] = new BrevoBlockedContact(
            "blocked@example.test",
            "unsubscribedViaEmail",
            "unsubscribed via email",
            "auth@oetwithdrhesham.co.uk",
            now);
        var service = new EmailDeliveryMailboxService(db, mailbox, new FixedClock(now));

        var result = await service.UnblockAsync("admin-1", "Admin", "blocked@example.test", CancellationToken.None);

        Assert.True(result.BrevoUnblocked);
        Assert.True(result.BrevoWasBlocked);
        Assert.Equal(1, result.ReleasedSuppressionCount);
        Assert.False(mailbox.Blocked.ContainsKey("blocked@example.test"));
        Assert.True(await db.NotificationSuppressions.AnyAsync(item =>
            item.EventKey == EmailLanes.MarketingSuppressionEventKey && item.IsActive));
        Assert.False(await db.NotificationSuppressions.AnyAsync(item => item.EventKey == null && item.IsActive));
    }

    private static EmailSettingsSnapshot Snapshot()
        => new(
            SmtpFromAddress: null,
            SmtpFromName: null,
            AuthFromAddress: EmailLanes.DefaultAuthFromAddress,
            AuthFromName: EmailLanes.DefaultAuthFromName,
            MarketingFromAddress: EmailLanes.DefaultMarketingFromAddress,
            MarketingFromName: EmailLanes.DefaultMarketingFromName,
            ProductFromAddress: EmailLanes.DefaultProductFromAddress,
            ProductFromName: EmailLanes.DefaultProductFromName,
            SupportFromAddress: EmailLanes.DefaultSupportFromAddress,
            SupportFromName: EmailLanes.DefaultSupportFromName);

    private static LearnerDbContext CreateDb()
        => new(new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static async Task<ApplicationUserAccount> SeedAccountAsync(LearnerDbContext db, string id, string email)
    {
        var account = new ApplicationUserAccount
        {
            Id = id,
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            PasswordHash = "hash",
            Role = ApplicationUserRoles.Learner,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        db.ApplicationUserAccounts.Add(account);
        await db.SaveChangesAsync();
        return account;
    }

    private static NotificationService CreateNotificationService(LearnerDbContext db, DateTimeOffset now)
    {
        var settings = TestRuntimeSettingsProvider.Base() with
        {
            Email = TestRuntimeSettingsProvider.Base().Email with { BrevoWebhookSecret = "webhook-secret" }
        };

        return new NotificationService(
            db,
            emailSender: null!,
            webPushDispatcher: null!,
            mobilePushDispatcher: null!,
            hubContext: null!,
            platformLinks: null!,
            timeProvider: new FixedClock(now),
            webPushOptions: Options.Create(new WebPushOptions()),
            runtimeSettingsProvider: new TestRuntimeSettingsProvider(settings),
            notificationProofOptions: Options.Create(new NotificationProofHarnessOptions()),
            environment: null!,
            logger: NullLogger<NotificationService>.Instance);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FakeMailbox : IBrevoTransactionalMailbox
    {
        public Dictionary<string, BrevoBlockedContact> Blocked { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Task<BrevoBlockedContact?> FindBlockedContactAsync(string email, CancellationToken cancellationToken = default)
            => Task.FromResult(Blocked.TryGetValue(email, out var contact) ? contact : null);

        public Task<bool> UnblockContactAsync(string email, CancellationToken cancellationToken = default)
            => Task.FromResult(Blocked.Remove(email));
    }
}
