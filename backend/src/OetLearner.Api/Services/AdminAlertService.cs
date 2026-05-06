using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services;

public class AdminAlertService(LearnerDbContext db)
{
    public async Task<AdminAlertSummaryResponse> GetAlertsAsync(CancellationToken ct)
    {
        var alerts = new List<AdminAlertItemResponse>();
        var now = DateTimeOffset.UtcNow;

        // SLA overdue reviews
        var overdueReviews = await db.ReviewRequests
            .AsNoTracking()
            .Where(rr => rr.State == ReviewRequestState.InReview && rr.CompletedAt == null)
            .CountAsync(ct);
        if (overdueReviews > 0)
            alerts.Add(new AdminAlertItemResponse("sla_overdue", "warning", "Active Reviews", $"{overdueReviews} reviews in progress", "/admin/review-ops", now));

        // Stuck reviews (queued > 48h)
        var stuckThreshold = now.AddHours(-48);
        var stuckReviews = await db.ReviewRequests
            .AsNoTracking()
            .Where(rr => rr.State == ReviewRequestState.Queued && rr.CreatedAt < stuckThreshold)
            .CountAsync(ct);
        if (stuckReviews > 0)
            alerts.Add(new AdminAlertItemResponse("stuck_reviews", "warning", "Stuck Reviews", $"{stuckReviews} reviews queued >48 hours", "/admin/review-ops", now));

        // Pending escalation count
        var pendingEscalations = await db.Set<ReviewEscalation>()
            .AsNoTracking()
            .Where(e => e.Status == "pending")
            .CountAsync(ct);
        if (pendingEscalations > 0)
            alerts.Add(new AdminAlertItemResponse("pending_escalations", "warning", "Pending Escalations", $"{pendingEscalations} escalations need review", "/admin/escalations", now));

        // Pending freeze requests
        var pendingFreezes = await db.Set<AccountFreezeRecord>()
            .AsNoTracking()
            .Where(f => f.Status == FreezeStatus.PendingApproval)
            .CountAsync(ct);
        if (pendingFreezes > 0)
            alerts.Add(new AdminAlertItemResponse("pending_freezes", "info", "Pending Freezes", $"{pendingFreezes} subscription freeze requests", "/admin/freeze", now));

        // Publish request backlog
        var pendingPublish = await db.ContentItems
            .AsNoTracking()
            .Where(c => c.Status == ContentStatus.EditorReview || c.Status == ContentStatus.PublisherApproval)
            .CountAsync(ct);
        if (pendingPublish >= 5)
            alerts.Add(new AdminAlertItemResponse("publish_backlog", "warning", "Publish Backlog", $"{pendingPublish} items awaiting review/publish", "/admin/content/publish-requests", now));
        else if (pendingPublish > 0)
            alerts.Add(new AdminAlertItemResponse("publish_backlog", "info", "Publish Backlog", $"{pendingPublish} items awaiting review/publish", "/admin/content/publish-requests", now));

        var criticalCount = alerts.Count(a => a.Severity == "critical");
        var warningCount = alerts.Count(a => a.Severity == "warning");
        var infoCount = alerts.Count(a => a.Severity == "info");

        return new AdminAlertSummaryResponse(alerts, criticalCount, warningCount, infoCount, now);
    }
}
