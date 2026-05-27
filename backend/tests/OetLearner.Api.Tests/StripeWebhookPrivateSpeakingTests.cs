using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Domain.Billing;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Billing;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests;

public sealed class StripeWebhookPrivateSpeakingTests
{
    [Fact]
    public async Task CompletedPrivateSpeakingCheckout_ConfirmsBookingAndQueuesFollowUpJobs()
    {
        using var factory = new TestWebApplicationFactory();
        var webhookId = await SeedPrivateSpeakingCheckoutAsync(
            factory,
            bookingId: "psb-completed-1",
            sessionId: "cs_private_speaking_completed",
            eventType: "checkout.session.async_payment_succeeded");

        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<LearnerService>();

        var result = await service.ApplyVerifiedPaymentWebhookEventAsync(
            webhookId,
            "cs_private_speaking_completed",
            "completed",
            PaymentWebhookCategories.Payment,
            "cs_private_speaking_completed",
            CancellationToken.None);

        Assert.Equal("completed", result.ProcessingStatus);
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var booking = await db.PrivateSpeakingBookings.SingleAsync(b => b.Id == "psb-completed-1");
        Assert.Equal(PrivateSpeakingPaymentStatus.Succeeded, booking.PaymentStatus);
        Assert.Equal(PrivateSpeakingBookingStatus.Confirmed, booking.Status);
        Assert.Equal("pi_private_speaking_completed", booking.StripePaymentIntentId);
        Assert.Null(booking.ReservationExpiresAt);

        var jobTypes = await db.BackgroundJobs
            .Where(job => job.ResourceId == "psb-completed-1")
            .Select(job => job.Type)
            .ToArrayAsync();
        Assert.Contains(JobType.PrivateSpeakingZoomCreate, jobTypes);
        Assert.Contains(JobType.PrivateSpeakingBookingConfirmation, jobTypes);
    }

    [Fact]
    public async Task PendingPrivateSpeakingCheckout_DoesNotConfirmBooking()
    {
        using var factory = new TestWebApplicationFactory();
        var webhookId = await SeedPrivateSpeakingCheckoutAsync(
            factory,
            bookingId: "psb-pending-1",
            sessionId: "cs_private_speaking_pending",
            eventType: "checkout.session.completed");

        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<LearnerService>();

        var result = await service.ApplyVerifiedPaymentWebhookEventAsync(
            webhookId,
            "cs_private_speaking_pending",
            "pending",
            PaymentWebhookCategories.Payment,
            "cs_private_speaking_pending",
            CancellationToken.None);

        Assert.Equal("ignored", result.ProcessingStatus);
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var booking = await db.PrivateSpeakingBookings.SingleAsync(b => b.Id == "psb-pending-1");
        Assert.Equal(PrivateSpeakingPaymentStatus.Processing, booking.PaymentStatus);
        Assert.Equal(PrivateSpeakingBookingStatus.PendingPayment, booking.Status);
        Assert.Empty(await db.BackgroundJobs.Where(job => job.ResourceId == "psb-pending-1").ToArrayAsync());
    }

    [Theory]
    [InlineData("checkout.session.async_payment_failed", PrivateSpeakingBookingStatus.Failed)]
    [InlineData("checkout.session.expired", PrivateSpeakingBookingStatus.Expired)]
    public async Task FailedPrivateSpeakingCheckout_ReleasesBooking(string eventType, PrivateSpeakingBookingStatus expectedStatus)
    {
        using var factory = new TestWebApplicationFactory();
        var bookingId = $"psb-{expectedStatus.ToString().ToLowerInvariant()}-1";
        var sessionId = $"cs_private_speaking_{expectedStatus.ToString().ToLowerInvariant()}";
        var webhookId = await SeedPrivateSpeakingCheckoutAsync(factory, bookingId, sessionId, eventType);

        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<LearnerService>();

        var result = await service.ApplyVerifiedPaymentWebhookEventAsync(
            webhookId,
            sessionId,
            "failed",
            PaymentWebhookCategories.Payment,
            sessionId,
            CancellationToken.None);

        Assert.Equal("completed", result.ProcessingStatus);
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var booking = await db.PrivateSpeakingBookings.SingleAsync(b => b.Id == bookingId);
        Assert.Equal(PrivateSpeakingPaymentStatus.Failed, booking.PaymentStatus);
        Assert.Equal(expectedStatus, booking.Status);
    }

    [Fact]
    public async Task ExpiredLegacyCheckoutSession_IsHandledBeforePaymentTransactionFallback()
    {
        using var factory = new TestWebApplicationFactory();
        var sessionId = "cs_legacy_checkout_expired";
        var webhookId = await SeedLegacyCheckoutSessionAsync(factory.Services, sessionId, "checkout.session.expired");

        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<LearnerService>();

        var result = await service.ApplyVerifiedPaymentWebhookEventAsync(
            webhookId,
            sessionId,
            "failed",
            PaymentWebhookCategories.Payment,
            sessionId,
            CancellationToken.None);

        Assert.Equal("completed", result.ProcessingStatus);
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var checkoutSession = await db.CheckoutSessions.SingleAsync(session => session.StripeSessionId == sessionId);
        Assert.Equal("expired", checkoutSession.Status);

        var webhook = await db.PaymentWebhookEvents.SingleAsync(evt => evt.Id == webhookId);
        Assert.Equal("completed", webhook.ProcessingStatus);
        Assert.Null(webhook.ErrorMessage);
    }

    [Fact]
    public async Task CompletedLegacyCheckoutSession_CallsFulfillmentBeforePaymentTransactionFallback()
    {
        var fulfillment = new RecordingFulfillmentService();
        await using var factory = CreateFactoryWithFulfillment(fulfillment);
        var sessionId = "cs_legacy_checkout_completed";
        var webhookId = await SeedLegacyCheckoutSessionAsync(factory.Services, sessionId, "checkout.session.completed");

        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<LearnerService>();

        var result = await service.ApplyVerifiedPaymentWebhookEventAsync(
            webhookId,
            sessionId,
            "completed",
            PaymentWebhookCategories.Payment,
            sessionId,
            CancellationToken.None);

        Assert.Equal("completed", result.ProcessingStatus);
        Assert.Equal(new[] { sessionId }, fulfillment.CheckoutSessionIds);
        Assert.Empty(fulfillment.GenericCheckoutSessionIds);

        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var webhook = await db.PaymentWebhookEvents.SingleAsync(evt => evt.Id == webhookId);
        Assert.Equal("completed", webhook.ProcessingStatus);
        Assert.Null(webhook.ErrorMessage);
    }

    [Fact]
    public async Task PaidInvoiceWebhook_CallsRenewalFulfillmentBeforePaymentTransactionFallback()
    {
        var fulfillment = new RecordingFulfillmentService();
        await using var factory = CreateFactoryWithFulfillment(fulfillment);
        var invoiceId = "in_legacy_renewal_completed";
        var webhookId = await SeedInvoiceWebhookAsync(factory.Services, invoiceId, "invoice.paid", "completed");

        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<LearnerService>();

        var result = await service.ApplyVerifiedPaymentWebhookEventAsync(
            webhookId,
            invoiceId,
            "completed",
            PaymentWebhookCategories.Payment,
            invoiceId,
            CancellationToken.None);

        Assert.Equal("completed", result.ProcessingStatus);
        Assert.Equal(new[] { invoiceId }, fulfillment.RenewalInvoiceIds);
    Assert.Empty(fulfillment.SubscriptionRenewalIds);

        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var webhook = await db.PaymentWebhookEvents.SingleAsync(evt => evt.Id == webhookId);
        Assert.Equal("completed", webhook.ProcessingStatus);
        Assert.Null(webhook.ErrorMessage);
    }

    private static async Task<Guid> SeedPrivateSpeakingCheckoutAsync(
        TestWebApplicationFactory factory,
        string bookingId,
        string sessionId,
        string eventType)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        db.PrivateSpeakingBookings.Add(new PrivateSpeakingBooking
        {
            Id = bookingId,
            LearnerUserId = "learner-1",
            TutorProfileId = "tutor-profile-1",
            Status = PrivateSpeakingBookingStatus.PendingPayment,
            SessionStartUtc = DateTimeOffset.UtcNow.AddDays(2),
            DurationMinutes = 60,
            PriceMinorUnits = 6900,
            Currency = "USD",
            StripeCheckoutSessionId = sessionId,
            PaymentStatus = PrivateSpeakingPaymentStatus.Processing,
            ZoomStatus = PrivateSpeakingZoomStatus.Pending,
            ReservationExpiresAt = DateTimeOffset.UtcNow.AddMinutes(15),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });

        var webhookId = Guid.NewGuid();
        db.PaymentWebhookEvents.Add(new PaymentWebhookEvent
        {
            Id = webhookId,
            Gateway = "stripe",
            EventType = eventType,
            GatewayEventId = $"evt-{Guid.NewGuid():N}",
            ProcessingStatus = "processing",
            VerificationStatus = "verified",
            VerifiedAt = DateTimeOffset.UtcNow,
            GatewayTransactionId = sessionId,
            NormalizedStatus = "processing",
            PayloadJson = SafeStripeCheckoutPayload(sessionId),
            PayloadSha256 = new string('a', 64),
            ParserVersion = "payment-webhook-v1",
            ReceivedAt = DateTimeOffset.UtcNow,
            AttemptCount = 1,
            LastAttemptedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
        return webhookId;
    }

    private static async Task<Guid> SeedLegacyCheckoutSessionAsync(
        IServiceProvider services,
        string sessionId,
        string eventType)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        db.CheckoutSessions.Add(new CheckoutSession
        {
            Id = Guid.NewGuid(),
            UserId = "learner-1",
            StripeSessionId = sessionId,
            IdempotencyKey = $"checkout-legacy-{Guid.NewGuid():N}",
            Status = "pending",
            TotalAmount = 9900,
            Currency = "AUD",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(24),
        });

        var webhookId = Guid.NewGuid();
        db.PaymentWebhookEvents.Add(new PaymentWebhookEvent
        {
            Id = webhookId,
            Gateway = "stripe",
            EventType = eventType,
            GatewayEventId = $"evt-{Guid.NewGuid():N}",
            ProcessingStatus = "processing",
            VerificationStatus = "verified",
            VerifiedAt = DateTimeOffset.UtcNow,
            GatewayTransactionId = sessionId,
            NormalizedStatus = "failed",
            PayloadJson = SafeStripeCheckoutPayload(sessionId),
            PayloadSha256 = new string('b', 64),
            ParserVersion = "payment-webhook-v1",
            ReceivedAt = DateTimeOffset.UtcNow,
            AttemptCount = 1,
            LastAttemptedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
        return webhookId;
    }

    private static async Task<Guid> SeedInvoiceWebhookAsync(
        IServiceProvider services,
        string invoiceId,
        string eventType,
        string normalizedStatus)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var webhookId = Guid.NewGuid();
        db.PaymentWebhookEvents.Add(new PaymentWebhookEvent
        {
            Id = webhookId,
            Gateway = "stripe",
            EventType = eventType,
            GatewayEventId = $"evt-{Guid.NewGuid():N}",
            ProcessingStatus = "processing",
            VerificationStatus = "verified",
            VerifiedAt = DateTimeOffset.UtcNow,
            GatewayTransactionId = invoiceId,
            NormalizedStatus = normalizedStatus,
            PayloadJson = $"{{\"data\":{{\"object\":{{\"id\":\"{invoiceId}\",\"subscription\":\"sub_legacy_renewal\"}}}}}}",
            PayloadSha256 = new string('c', 64),
            ParserVersion = "payment-webhook-v1",
            ReceivedAt = DateTimeOffset.UtcNow,
            AttemptCount = 1,
            LastAttemptedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
        return webhookId;
    }

    private static WebApplicationFactory<Program> CreateFactoryWithFulfillment(RecordingFulfillmentService fulfillment)
        => new TestWebApplicationFactory().WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IFulfillmentService>();
                services.AddSingleton<IFulfillmentService>(fulfillment);
            });
        });

    private static string SafeStripeCheckoutPayload(string sessionId)
    {
        var paymentIntentId = $"pi_{sessionId.Replace("cs_private_speaking_", "private_speaking_", StringComparison.Ordinal)}";
        return $"{{\"data\":{{\"object\":{{\"id\":\"{sessionId}\",\"payment_intent\":\"{paymentIntentId}\"}}}}}}";
    }

    private sealed class RecordingFulfillmentService : IFulfillmentService
    {
        public List<string> GenericCheckoutSessionIds { get; } = new();

        public List<string> CheckoutSessionIds { get; } = new();

        public List<string> RenewalInvoiceIds { get; } = new();

        public List<string> SubscriptionRenewalIds { get; } = new();

        public Task FulfillAsync(string stripeSessionId, CancellationToken ct = default)
        {
            GenericCheckoutSessionIds.Add(stripeSessionId);
            return Task.CompletedTask;
        }

        public Task FulfillRenewalAsync(string stripeInvoiceId, CancellationToken ct = default)
        {
            RenewalInvoiceIds.Add(stripeInvoiceId);
            return Task.CompletedTask;
        }

        public Task FulfillCheckoutAsync(string stripeSessionId, CancellationToken ct = default)
        {
            CheckoutSessionIds.Add(stripeSessionId);
            return Task.CompletedTask;
        }

        public Task FulfillSubscriptionRenewalAsync(string stripeSubscriptionId, CancellationToken ct = default)
        {
            SubscriptionRenewalIds.Add(stripeSubscriptionId);
            return Task.CompletedTask;
        }

        public Task RevokeAccessAsync(string userId, string reason, CancellationToken ct = default)
            => Task.CompletedTask;
    }
}