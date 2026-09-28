using System.ComponentModel.DataAnnotations;
using System.Linq.Expressions;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Endpoints;
using OetLearner.Api.Security;
using OetLearner.Api.Services.Billing;
using OetLearner.Api.Services.Conversation;
using OetLearner.Api.Services.Entitlements;

namespace OetLearner.Api.Services;

public partial class AdminService
{

    // ════════════════════════════════════════════
    //  Webhook Monitoring
    // ════════════════════════════════════════════

    public async Task<object> GetWebhookEventsAsync(
        string? gateway, string? status, int page, int pageSize, CancellationToken ct)
    {
        var query = db.PaymentWebhookEvents.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(gateway))
            query = query.Where(e => e.Gateway == gateway);
        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(e => e.ProcessingStatus == status);

        var total = await query.CountAsync(ct);
        var pageEvents = await query
            .OrderByDescending(e => e.ReceivedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(e => new
            {
                e.Id,
                e.Gateway,
                e.EventType,
                e.GatewayEventId,
                e.ProcessingStatus,
                e.VerificationStatus,
                e.VerifiedAt,
                e.PayloadSha256,
                e.ParserVersion,
                e.GatewayTransactionId,
                e.NormalizedStatus,
                e.AttemptCount,
                e.RetryCount,
                e.LastAttemptedAt,
                e.LastRetriedAt,
                e.ErrorMessage,
                e.ReceivedAt,
                e.ProcessedAt
            })
            .ToListAsync(ct);

        var items = pageEvents.Select(e =>
        {
            var retryBlockedReason = LearnerService.GetPaymentWebhookRetryBlockedReason(new PaymentWebhookEvent
            {
                Id = e.Id,
                ProcessingStatus = e.ProcessingStatus,
                VerificationStatus = e.VerificationStatus,
                VerifiedAt = e.VerifiedAt,
                PayloadSha256 = e.PayloadSha256,
                ParserVersion = e.ParserVersion,
                GatewayTransactionId = e.GatewayTransactionId,
                NormalizedStatus = e.NormalizedStatus
            });
            return new
            {
                id = e.Id,
                gateway = e.Gateway,
                eventType = e.EventType,
                gatewayEventId = e.GatewayEventId,
                processingStatus = e.ProcessingStatus,
                verificationStatus = e.VerificationStatus,
                gatewayTransactionId = e.GatewayTransactionId,
                normalizedStatus = e.NormalizedStatus,
                attemptCount = e.AttemptCount,
                retryCount = e.RetryCount,
                lastAttemptedAt = e.LastAttemptedAt,
                lastRetriedAt = e.LastRetriedAt,
                errorMessage = e.ErrorMessage,
                receivedAt = e.ReceivedAt,
                processedAt = e.ProcessedAt,
                retryable = retryBlockedReason is null,
                retryBlockedReason
            };
        }).ToList();

        return new { items, total, page, pageSize };
    }

    public async Task<object> GetWebhookSummaryAsync(CancellationToken ct)
    {
        var events = db.PaymentWebhookEvents.AsNoTracking();
        var now = DateTimeOffset.UtcNow;
        var last24h = now.AddHours(-24);
        var total = await events.CountAsync(ct);
        var recent24h = await events.CountAsync(e => e.ReceivedAt >= last24h, ct);
        var failed = await events.CountAsync(e => e.ProcessingStatus == "failed", ct);
        var failed24h = await events.CountAsync(e => e.ProcessingStatus == "failed" && e.ReceivedAt >= last24h, ct);

        var byStatus = await events
            .GroupBy(e => e.ProcessingStatus)
            .Select(g => new { status = g.Key, count = g.Count() })
            .ToListAsync(ct);

        var byGateway = await events
            .GroupBy(e => e.Gateway)
            .Select(g => new { gateway = g.Key, count = g.Count() })
            .ToListAsync(ct);

        var recentFailureEvents = await events
            .Where(e => e.ProcessingStatus == "failed")
            .OrderByDescending(e => e.ReceivedAt)
            .Take(5)
            .Select(e => new
            {
                e.Id,
                e.EventType,
                e.ProcessingStatus,
                e.VerificationStatus,
                e.VerifiedAt,
                e.PayloadSha256,
                e.ParserVersion,
                e.GatewayTransactionId,
                e.NormalizedStatus,
                e.ErrorMessage,
                e.ReceivedAt
            })
            .ToListAsync(ct);

        var recentFailures = recentFailureEvents.Select(e =>
        {
            var retryBlockedReason = LearnerService.GetPaymentWebhookRetryBlockedReason(new PaymentWebhookEvent
            {
                Id = e.Id,
                ProcessingStatus = e.ProcessingStatus,
                VerificationStatus = e.VerificationStatus,
                VerifiedAt = e.VerifiedAt,
                PayloadSha256 = e.PayloadSha256,
                ParserVersion = e.ParserVersion,
                GatewayTransactionId = e.GatewayTransactionId,
                NormalizedStatus = e.NormalizedStatus
            });
            return new
            {
                id = e.Id,
                eventType = e.EventType,
                errorMessage = e.ErrorMessage,
                receivedAt = e.ReceivedAt,
                retryable = retryBlockedReason is null,
                retryBlockedReason
            };
        }).ToList();

        return new
        {
            total,
            recent24h,
            failed,
            failed24h,
            byStatus,
            byGateway,
            recentFailures
        };
    }

    public async Task<object> RetryWebhookAsync(
        string actorId, string actorName, string eventId, CancellationToken ct)
    {
        if (!Guid.TryParse(eventId, out var webhookEventId))
        {
            throw ApiException.Validation("invalid_webhook_id", "Webhook event id is invalid.");
        }

        var result = await learnerService.RetryVerifiedPaymentWebhookAsync(webhookEventId, actorId, actorName, ct);

        await LogAuditAsync(actorId, actorName, "RetryWebhook", "PaymentWebhookEvent", eventId,
            $"Retried webhook: {result.Status} ({result.ProcessingStatus})", ct);

        return result;
    }
}
