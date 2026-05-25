using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Billing;

namespace OetLearner.Api.Endpoints;

public static class StripeWebhookEndpoints
{
    public static IEndpointRouteBuilder MapStripeWebhookEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/v1/webhooks/stripe", HandleStripeWebhook).AllowAnonymous();
        return app;
    }

    private static async Task<Results<Ok, BadRequest<string>>> HandleStripeWebhook(
        HttpContext http,
        IStripeService stripeService,
        LearnerDbContext db,
        IServiceScopeFactory scopeFactory,
        IOptions<BillingOptions> billingOptions,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        var webhookSecret = billingOptions.Value.Stripe.WebhookSecret;

        // Read raw body for signature verification
        http.Request.EnableBuffering();
        using var reader = new System.IO.StreamReader(http.Request.Body, leaveOpen: true);
        var rawBody = await reader.ReadToEndAsync(ct);
        http.Request.Body.Seek(0, System.IO.SeekOrigin.Begin);

        var signature = http.Request.Headers["Stripe-Signature"].FirstOrDefault() ?? string.Empty;

        Stripe.Event stripeEvent;
        try
        {
            stripeEvent = stripeService.ConstructWebhookEvent(rawBody, signature, webhookSecret ?? string.Empty);
        }
        catch (Stripe.StripeException ex)
        {
            logger.LogWarning("Stripe webhook signature verification failed: {Message}", ex.Message);
            return TypedResults.BadRequest("Invalid signature.");
        }

        // Idempotency: check if already processed
        var existing = await db.PaymentWebhookEvents
            .Where(e => e.GatewayEventId == stripeEvent.Id)
            .FirstOrDefaultAsync(ct);

        if (existing is not null)
        {
            logger.LogInformation("Stripe webhook {EventId} already processed (status: {Status})", stripeEvent.Id, existing.ProcessingStatus);
            return TypedResults.Ok();
        }

        // Persist the event record
        var webhookRecord = new PaymentWebhookEvent
        {
            Id = Guid.NewGuid(),
            Gateway = "stripe",
            EventType = stripeEvent.Type,
            GatewayEventId = stripeEvent.Id,
            ProcessingStatus = "received",
            VerificationStatus = "verified",
            PayloadJson = rawBody,
            ReceivedAt = DateTimeOffset.UtcNow,
            AttemptCount = 0
        };
        db.PaymentWebhookEvents.Add(webhookRecord);
        await db.SaveChangesAsync(ct);

        // Fire-and-forget processing using a new DI scope to avoid disposed-context issues
        _ = ProcessWebhookAsync(stripeEvent, webhookRecord.Id, scopeFactory, logger);

        return TypedResults.Ok();
    }

    private static async Task ProcessWebhookAsync(
        Stripe.Event stripeEvent,
        Guid recordId,
        IServiceScopeFactory scopeFactory,
        ILogger logger)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var fulfillmentService = scope.ServiceProvider.GetRequiredService<IFulfillmentService>();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();

        try
        {
            switch (stripeEvent.Type)
            {
                case "checkout.session.completed":
                    if (stripeEvent.Data?.Object is Stripe.Checkout.Session session)
                        await fulfillmentService.FulfillCheckoutAsync(session.Id);
                    break;

                case "invoice.paid":
                    if (stripeEvent.Data?.Object is Stripe.Invoice invoice &&
                        invoice.SubscriptionId is not null)
                        await fulfillmentService.FulfillSubscriptionRenewalAsync(invoice.SubscriptionId);
                    break;

                case "customer.subscription.deleted":
                case "customer.subscription.updated":
                    // Handled by subscription sync (Wave 2-A6)
                    break;

                default:
                    logger.LogDebug("Unhandled Stripe event type: {EventType}", stripeEvent.Type);
                    break;
            }

            var record = await db.PaymentWebhookEvents.FindAsync(recordId);
            if (record is not null)
            {
                record.ProcessingStatus = "completed";
                record.ProcessedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync();
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to process Stripe webhook {EventType} ({EventId})", stripeEvent.Type, stripeEvent.Id);
            var record = await db.PaymentWebhookEvents.FindAsync(recordId);
            if (record is not null)
            {
                record.ProcessingStatus = "failed";
                record.ErrorMessage = ex.Message;
                record.AttemptCount++;
                await db.SaveChangesAsync();
            }
        }
    }
}
