using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Billing;

namespace OetLearner.Api.Services;

public sealed partial class MockService
{
    public async Task<object> ReportMockLeakAsync(string userId, MockLeakReportRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.MockBundleId) && string.IsNullOrWhiteSpace(request.MockAttemptId))
        {
            throw ApiException.Validation(
                "mock_leak_report_target_required",
                "Select the mock bundle or attempt you are reporting.",
                [new ApiFieldError("mockBundleId", "required", "Provide a bundle or attempt id.")]);
        }

        MockAttempt? attempt = null;
        string? bundleId = string.IsNullOrWhiteSpace(request.MockBundleId) ? null : request.MockBundleId.Trim();
        if (!string.IsNullOrWhiteSpace(request.MockAttemptId))
        {
            attempt = await db.MockAttempts.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == request.MockAttemptId && x.UserId == userId, ct)
                ?? throw ApiException.NotFound("mock_attempt_not_found", "Mock attempt not found.");
            bundleId ??= attempt.MockBundleId;
        }

        var notes = JsonSupport.Serialize(new
        {
            reason = request.Reason?.Trim(),
            evidenceUrl = request.EvidenceUrl?.Trim(),
            pageOrQuestion = request.PageOrQuestion?.Trim()
        });
        var review = new MockContentReview
        {
            Id = $"mock-review-{Guid.NewGuid():N}",
            MockBundleId = bundleId,
            MockAttemptId = attempt?.Id ?? request.MockAttemptId,
            ReportedByUserId = userId,
            ReviewType = "leak_report",
            Severity = "high",
            Status = "open",
            Notes = notes,
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.MockContentReviews.Add(review);
        RecordEvent(userId, "mock_leak_reported", new { reviewId = review.Id, bundleId, request.MockAttemptId });
        await db.SaveChangesAsync(ct);
        return new { id = review.Id, status = review.Status, severity = review.Severity };
    }

    public async Task<IReadOnlyList<MockLeakReportSummary>> ListLeakReportsAsync(
        string? status, int limit, CancellationToken ct)
    {
        var clamped = limit <= 0 ? 50 : Math.Min(limit, 200);
        var query = db.MockContentReviews.AsNoTracking()
            .Include(x => x.MockBundle)
            .AsQueryable();
        if (!string.IsNullOrWhiteSpace(status))
        {
            var normalised = status.Trim().ToLowerInvariant();
            if (!LeakReportStatuses.Contains(normalised))
            {
                throw ApiException.Validation(
                    "mock_leak_report_status_invalid",
                    "Status must be one of open, investigating, resolved, dismissed.");
            }
            query = query.Where(x => x.Status == normalised);
        }

        var rows = await query
            .OrderByDescending(x => x.CreatedAt)
            .Take(clamped)
            .ToListAsync(ct);

        var reporterIds = rows
            .Select(x => x.ReportedByUserId)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct()
            .ToArray();
        var displayNames = reporterIds.Length == 0
            ? new Dictionary<string, string>(0)
            : await db.Users.AsNoTracking()
                .Where(u => reporterIds.Contains(u.Id))
                .Select(u => new { u.Id, u.DisplayName })
                .ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);

        return rows.Select(row => ToLeakReportSummary(row, displayNames)).ToArray();
    }

    public async Task<MockLeakReportSummary> UpdateLeakReportAsync(
        string adminId,
        string id,
        MockLeakReportUpdateRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Status)
            || !LeakReportStatuses.Contains(request.Status.Trim().ToLowerInvariant()))
        {
            throw ApiException.Validation(
                "mock_leak_report_status_invalid",
                "Status must be one of open, investigating, resolved, dismissed.");
        }
        var nextStatus = request.Status.Trim().ToLowerInvariant();

        var review = await db.MockContentReviews
            .Include(x => x.MockBundle)
            .FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw ApiException.NotFound("mock_leak_report_not_found", "Leak report not found.");

        if (TerminalLeakReportStatuses.Contains(review.Status)
            && !string.Equals(review.Status, nextStatus, StringComparison.OrdinalIgnoreCase))
        {
            throw ApiException.Validation(
                "mock_leak_report_status_locked",
                $"Report is already {review.Status} and cannot transition to {nextStatus}.");
        }

        var now = DateTimeOffset.UtcNow;
        var previousStatus = review.Status;
        var note = string.IsNullOrWhiteSpace(request.ResolutionNote) ? null : request.ResolutionNote.Trim();

        // The notes column already stores the original learner-submitted JSON
        // payload (reason, evidenceUrl, pageOrQuestion). To preserve that
        // payload while recording the admin resolution note we merge under a
        // dedicated key so the original report is never overwritten.
        if (note is not null)
        {
            var payload = string.IsNullOrWhiteSpace(review.Notes)
                ? new Dictionary<string, object?>()
                : JsonSupport.Deserialize<Dictionary<string, object?>>(review.Notes, new Dictionary<string, object?>());
            payload["adminResolutionNote"] = note;
            payload["adminResolutionAt"] = now;
            payload["adminResolutionBy"] = adminId;
            review.Notes = JsonSupport.Serialize(payload);
        }

        review.Status = nextStatus;
        if (TerminalLeakReportStatuses.Contains(nextStatus))
        {
            review.ResolvedAt = now;
            review.ResolvedByAdminId = adminId;
        }
        else
        {
            review.ResolvedAt = null;
            review.ResolvedByAdminId = null;
        }

        db.AuditEvents.Add(new AuditEvent
        {
            Id = $"audit-{Guid.NewGuid():N}",
            OccurredAt = now,
            ActorId = adminId,
            ActorName = adminId,
            Action = "MockLeakReport.Updated",
            ResourceType = "MockContentReview",
            ResourceId = review.Id,
            Details = JsonSupport.Serialize(new
            {
                previousStatus,
                nextStatus,
                hasResolutionNote = note is not null
            })
        });

        await db.SaveChangesAsync(ct);

        var displayNames = string.IsNullOrWhiteSpace(review.ReportedByUserId)
            ? new Dictionary<string, string>(0)
            : await db.Users.AsNoTracking()
                .Where(u => u.Id == review.ReportedByUserId)
                .Select(u => new { u.Id, u.DisplayName })
                .ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);

        return ToLeakReportSummary(review, displayNames);
    }

    private static MockLeakReportSummary ToLeakReportSummary(
        MockContentReview row,
        IReadOnlyDictionary<string, string> displayNames)
    {
        string? reasonCode = null;
        string? details = null;
        string? evidenceUrl = null;
        string? pageOrQuestion = null;
        string? resolutionNote = null;
        if (!string.IsNullOrWhiteSpace(row.Notes))
        {
            var payload = JsonSupport.Deserialize<Dictionary<string, object?>>(
                row.Notes, new Dictionary<string, object?>());
            reasonCode = payload.TryGetValue("reason", out var reason) ? reason?.ToString() : null;
            evidenceUrl = payload.TryGetValue("evidenceUrl", out var url) ? url?.ToString() : null;
            pageOrQuestion = payload.TryGetValue("pageOrQuestion", out var pq) ? pq?.ToString() : null;
            resolutionNote = payload.TryGetValue("adminResolutionNote", out var rn) ? rn?.ToString() : null;
            details = reasonCode;
        }

        string? displayName = null;
        if (!string.IsNullOrWhiteSpace(row.ReportedByUserId)
            && displayNames.TryGetValue(row.ReportedByUserId!, out var dn))
        {
            displayName = dn;
        }

        return new MockLeakReportSummary(
            Id: row.Id,
            BundleId: row.MockBundleId,
            BundleTitle: row.MockBundle?.Title,
            AttemptId: row.MockAttemptId,
            Severity: row.Severity,
            Status: row.Status,
            ReasonCode: reasonCode,
            Details: details,
            EvidenceUrl: evidenceUrl,
            PageOrQuestion: pageOrQuestion,
            ReportedByUserId: row.ReportedByUserId,
            ReportedByUserDisplayName: displayName,
            CreatedAt: row.CreatedAt,
            ResolvedAt: row.ResolvedAt,
            ResolvedByAdminId: row.ResolvedByAdminId,
            ResolutionNote: resolutionNote);
    }
}
