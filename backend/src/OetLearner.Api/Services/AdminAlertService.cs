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

        // Security spec §4.4: impossible-travel sign-ins in the last 24h
        var last24h = now.AddHours(-24);
        var impossibleTravelCount = await db.SecurityEvents.AsNoTracking()
            .Where(e => e.Kind == SecurityEventKinds.RiskImpossibleTravel && e.OccurredAt >= last24h)
            .CountAsync(ct);
        if (impossibleTravelCount > 0)
            alerts.Add(new AdminAlertItemResponse("impossible_travel", "critical", "Impossible Travel", $"{impossibleTravelCount} sign-in(s) flagged for impossible travel in the last 24h", "/admin/security", now));

        // Refresh-token family reuse — the strongest signal of a copied/stolen
        // session (simultaneous use from two places)
        var reuseCount = await db.SecurityEvents.AsNoTracking()
            .Where(e => e.Kind == SecurityEventKinds.AuthRefreshReuseDetected && e.OccurredAt >= last24h)
            .CountAsync(ct);
        if (reuseCount > 0)
            alerts.Add(new AdminAlertItemResponse("refresh_reuse_detected", "critical", "Refresh Token Reuse", $"{reuseCount} refresh-token reuse event(s) in the last 24h — possible session theft", "/admin/security", now));

        // Device churn: change attempts blocked by the cooldown, last 7 days
        var last7d = now.AddDays(-7);
        var deviceCooldownBlocks = await db.SecurityEvents.AsNoTracking()
            .Where(e => e.Kind == SecurityEventKinds.DeviceChangeBlockedCooldown && e.OccurredAt >= last7d)
            .CountAsync(ct);
        if (deviceCooldownBlocks > 0)
            alerts.Add(new AdminAlertItemResponse("device_change_cooldown", "warning", "Device Change Cooldown Hits", $"{deviceCooldownBlocks} device-change attempt(s) blocked by cooldown in the last 7 days", "/admin/security", now));

        // Capture/tamper signals from video playback in the last 24h
        var captureEventsCount = await db.VideoProtectionEvents.AsNoTracking()
            .Where(e => (e.Kind == VideoProtectionKinds.CaptureDetected
                    || e.Kind == VideoProtectionKinds.ScreenshotDetected
                    || e.Kind == VideoProtectionKinds.WatermarkTampered)
                && e.OccurredAt >= last24h)
            .CountAsync(ct);
        if (captureEventsCount > 0)
            alerts.Add(new AdminAlertItemResponse("capture_protection_triggered", "warning", "Capture Protection Triggered", $"{captureEventsCount} capture/tamper event(s) in the last 24h", "/admin/security", now));

        // Mass playback: an account starting an abnormal number of NEW
        // playback sessions in 24h (each PlaybackSessionStarted event is a
        // genuinely new session — VideoPlaybackSessionService only logs it
        // on issuance of a new/expired session, not on renew or re-use of a
        // still-active one) is a signal of bulk downloading/content
        // extraction rather than normal viewing. Threshold chosen the same
        // way SignInRiskService picks its device-churn threshold (5 distinct
        // device families / 7 days => "frequent_sign_ins"), scaled up for
        // playback's naturally higher event volume: normal viewing is capped
        // by VideoPlaybackSessionService.MaxConcurrentDistinctVideos (3
        // concurrent), so even a heavy binge-watcher rarely starts more than
        // a couple dozen distinct/renewed-after-expiry sessions in one day —
        // 40 is comfortably above that range.
        const int massPlaybackThreshold = 40;
        var massPlaybackAccounts = await db.SecurityEvents.AsNoTracking()
            .Where(e => e.Kind == SecurityEventKinds.PlaybackSessionStarted && e.OccurredAt >= last24h && e.AuthAccountId != null)
            .GroupBy(e => e.AuthAccountId)
            .Where(g => g.Count() >= massPlaybackThreshold)
            .CountAsync(ct);
        if (massPlaybackAccounts > 0)
            alerts.Add(new AdminAlertItemResponse("mass_playback", "warning", "Mass Playback Volume", $"{massPlaybackAccounts} account(s) started {massPlaybackThreshold}+ playback sessions in the last 24h", "/admin/security", now));

        var criticalCount = alerts.Count(a => a.Severity == "critical");
        var warningCount = alerts.Count(a => a.Severity == "warning");
        var infoCount = alerts.Count(a => a.Severity == "info");

        return new AdminAlertSummaryResponse(alerts, criticalCount, warningCount, infoCount, now);
    }
}
