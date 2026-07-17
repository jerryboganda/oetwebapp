using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OetWithDrHesham.Api.Data;
using OetWithDrHesham.Api.Domain;

namespace OetWithDrHesham.Api.Services.Mocks;

/// <summary>
/// Mocks Module Phase 3.3 — multi-stage editorial content review state machine.
///
/// Drives a mock bundle from <c>academic → medical → language → technical →
/// pilot → published</c> with one <see cref="MockContentReview"/> row written
/// per transition (ReviewType = <c>editorial_stage</c>). Stage progression must
/// be monotonic and may not skip backwards. The service publishes the bundle
/// (flips <see cref="MockBundle.Status"/> to <see cref="ContentStatus.Published"/>
/// and stamps <see cref="MockBundle.PublishedAt"/>) only when the transition
/// target is <see cref="MockBundleReviewStages.Published"/>.
///
/// Mirrors the publish-gate pattern from <c>GrammarPublishGateService</c>:
/// callers may surface the current stage via the summary endpoint, but the
/// authoritative transition still goes through <see cref="AdvanceStageAsync"/>.
/// </summary>
public sealed class MockBundleReviewStageService(
    LearnerDbContext db,
    ILogger<MockBundleReviewStageService>? logger = null)
{
    /// <summary>Canonical ReviewType for rows the stage service writes.</summary>
    public const string EditorialReviewType = "editorial_stage";

    /// <summary>
    /// Persist a stage transition for the bundle. The <paramref name="nextStage"/>
    /// must be one of <see cref="MockBundleReviewStages.All"/> and may not
    /// regress the current pipeline index. When the next stage is
    /// <c>published</c>, the bundle is published in the same transaction.
    /// </summary>
    public async Task AdvanceStageAsync(
        string mockBundleId,
        string nextStage,
        string adminId,
        string? notes,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(mockBundleId))
            throw ApiException.Validation("MOCK_BUNDLE_REQUIRED", "mockBundleId is required.");

        if (string.IsNullOrWhiteSpace(adminId))
            throw ApiException.Validation("ADMIN_ID_REQUIRED", "adminId is required.");

        if (!MockBundleReviewStages.IsValid(nextStage))
            throw ApiException.Validation("INVALID_STAGE",
                $"Stage '{nextStage}' is not one of {string.Join(", ", MockBundleReviewStages.Ordered)}.");

        var bundle = await db.MockBundles.FirstOrDefaultAsync(b => b.Id == mockBundleId, ct)
            ?? throw ApiException.NotFound("MOCK_BUNDLE_NOT_FOUND",
                $"Mock bundle '{mockBundleId}' not found.");

        var current = await GetCurrentStageAsync(mockBundleId, ct);
        var currentIndex = current is null ? -1 : MockBundleReviewStages.IndexOf(current);
        var nextIndex = MockBundleReviewStages.IndexOf(nextStage);

        if (nextIndex <= currentIndex)
        {
            throw ApiException.Conflict("STAGE_NOT_MONOTONIC",
                $"Stage '{nextStage}' is not after current stage '{current ?? "(none)"}'. " +
                "Stage transitions must move forward through the pipeline.");
        }

        var now = DateTimeOffset.UtcNow;
        var transition = new MockContentReview
        {
            Id = Guid.NewGuid().ToString("N"),
            MockBundleId = mockBundleId,
            ReviewType = EditorialReviewType,
            Severity = "info",
            Status = "open",
            Stage = nextStage.ToLowerInvariant(),
            Notes = (notes ?? string.Empty).Trim(),
            CreatedAt = now,
        };
        db.MockContentReviews.Add(transition);

        // Publishing path — flip bundle status alongside the transition row.
        if (string.Equals(nextStage, MockBundleReviewStages.Published, StringComparison.OrdinalIgnoreCase))
        {
            bundle.Status = ContentStatus.Published;
            bundle.PublishedAt = now;
            bundle.UpdatedAt = now;
            bundle.UpdatedByAdminId = adminId;

            transition.Status = "resolved";
            transition.ResolvedAt = now;
            transition.ResolvedByAdminId = adminId;
        }

        db.AuditEvents.Add(new AuditEvent
        {
            Id = Guid.NewGuid().ToString("N"),
            ActorId = adminId,
            ActorName = adminId,
            Action = "MockBundleReviewStageAdvanced",
            ResourceType = "MockBundle",
            ResourceId = mockBundleId,
            Details = $"Stage advanced {current ?? "(none)"} → {nextStage}",
            OccurredAt = now,
        });

        await db.SaveChangesAsync(ct);
        logger?.LogInformation(
            "Mock bundle {BundleId} advanced {From} → {To} by {AdminId}",
            mockBundleId, current ?? "(none)", nextStage, adminId);
    }

    /// <summary>
    /// Read the current stage progression for a bundle. Returns the chain of
    /// transitions plus the latest stage and a "isPublished" convenience flag.
    /// </summary>
    public async Task<MockBundleReviewStageSummary> GetSummaryAsync(string mockBundleId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(mockBundleId))
            throw ApiException.Validation("MOCK_BUNDLE_REQUIRED", "mockBundleId is required.");

        var bundle = await db.MockBundles.AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == mockBundleId, ct)
            ?? throw ApiException.NotFound("MOCK_BUNDLE_NOT_FOUND",
                $"Mock bundle '{mockBundleId}' not found.");

        var rows = await db.MockContentReviews.AsNoTracking()
            .Where(r => r.MockBundleId == mockBundleId
                     && r.ReviewType == EditorialReviewType)
            .OrderBy(r => r.CreatedAt)
            .Select(r => new MockBundleReviewStageEntry(
                r.Id,
                r.Stage,
                r.Notes,
                r.CreatedAt,
                r.ResolvedAt,
                r.ResolvedByAdminId))
            .ToListAsync(ct);

        string? currentStage = rows.Count == 0 ? null : rows[^1].Stage;
        var isPublished = string.Equals(currentStage, MockBundleReviewStages.Published, StringComparison.OrdinalIgnoreCase)
                          || bundle.Status == ContentStatus.Published
                          || bundle.PublishedAt is not null;

        return new MockBundleReviewStageSummary(
            MockBundleId: mockBundleId,
            CurrentStage: currentStage,
            IsPublished: isPublished,
            PublishedAt: bundle.PublishedAt,
            Transitions: rows);
    }

    private async Task<string?> GetCurrentStageAsync(string mockBundleId, CancellationToken ct)
    {
        var latest = await db.MockContentReviews.AsNoTracking()
            .Where(r => r.MockBundleId == mockBundleId
                     && r.ReviewType == EditorialReviewType)
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => r.Stage)
            .FirstOrDefaultAsync(ct);

        return string.IsNullOrWhiteSpace(latest) ? null : latest;
    }
}

/// <summary>
/// A single editorial stage transition row exposed by
/// <see cref="MockBundleReviewStageService.GetSummaryAsync"/>.
/// </summary>
public sealed record MockBundleReviewStageEntry(
    string Id,
    string Stage,
    string Notes,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ResolvedAt,
    string? ResolvedByAdminId);

/// <summary>
/// Read model for the editorial review stage of a mock bundle.
/// </summary>
public sealed record MockBundleReviewStageSummary(
    string MockBundleId,
    string? CurrentStage,
    bool IsPublished,
    DateTimeOffset? PublishedAt,
    IReadOnlyList<MockBundleReviewStageEntry> Transitions);
