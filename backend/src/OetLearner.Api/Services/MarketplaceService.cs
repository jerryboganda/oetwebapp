using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services;

/// <summary>
/// Content Marketplace — educator submission portal and community content browsing.
/// </summary>
public class MarketplaceService(LearnerDbContext db)
{
    // ── Contributor Profile ─────────────────────────────────

    public async Task<object> GetOrCreateContributorProfileAsync(string userId, CancellationToken ct)
    {
        var contributor = await db.ContentContributors
            .FirstOrDefaultAsync(c => c.UserId == userId, ct);

        if (contributor == null)
        {
            contributor = new ContentContributor
            {
                Id = $"cc-{Guid.NewGuid():N}",
                UserId = userId,
                DisplayName = "Contributor",
                Bio = null,
                VerificationStatus = "unverified",
                SubmissionCount = 0,
                ApprovedCount = 0,
                Rating = 0,
                CreatedAt = DateTimeOffset.UtcNow
            };
            db.ContentContributors.Add(contributor);
            await db.SaveChangesAsync(ct);
        }

        return MapContributor(contributor);
    }

    public async Task<object> UpdateContributorProfileAsync(string userId, MarketplaceProfileUpdateRequest request, CancellationToken ct)
    {
        var contributor = await db.ContentContributors
            .FirstOrDefaultAsync(c => c.UserId == userId, ct)
            ?? throw ApiException.NotFound("CONTRIBUTOR_NOT_FOUND", "Contributor profile not found.");

        if (request.DisplayName != null) contributor.DisplayName = request.DisplayName;
        if (request.Bio != null) contributor.Bio = request.Bio;

        await db.SaveChangesAsync(ct);
        return MapContributor(contributor);
    }

    // ── Content Submissions ─────────────────────────────────

    public async Task<object> CreateSubmissionAsync(string userId, MarketplaceSubmissionRequest request, CancellationToken ct)
    {
        var contributor = await db.ContentContributors
            .FirstOrDefaultAsync(c => c.UserId == userId, ct)
            ?? throw ApiException.Validation("NO_PROFILE", "Please create a contributor profile first.");

        var submissionId = $"ms-{Guid.NewGuid():N}";
        var now = DateTimeOffset.UtcNow;

        var submission = new ContentSubmission
        {
            Id = submissionId,
            ContributorId = contributor.Id,
            ExamFamilyCode = request.ExamFamilyCode ?? "oet",
            SubtestCode = request.SubtestCode,
            Title = request.Title,
            Description = request.Description,
            ContentPayloadJson = request.ContentPayloadJson ?? "{}",
            ContentType = request.ContentType ?? "practice_task",
            ProfessionId = request.ProfessionId,
            Difficulty = request.Difficulty ?? "medium",
            Tags = request.Tags,
            Status = "pending",
            SubmittedAt = now,
            CreatedAt = now
        };
        db.ContentSubmissions.Add(submission);

        contributor.SubmissionCount++;
        await db.SaveChangesAsync(ct);

        return MapSubmission(submission);
    }

    public async Task<object> GetMySubmissionsAsync(string userId, int page, int pageSize, CancellationToken ct)
    {
        var contributor = await db.ContentContributors
            .FirstOrDefaultAsync(c => c.UserId == userId, ct);

        if (contributor == null)
        {
            return new { items = Array.Empty<object>(), total = 0, page, pageSize };
        }

        var query = db.ContentSubmissions.AsNoTracking()
            .Where(s => s.ContributorId == contributor.Id);

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(s => s.SubmittedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return new
        {
            items = items.Select(MapSubmission),
            total,
            page,
            pageSize
        };
    }

    public async Task<object> GetSubmissionAsync(string userId, string submissionId, CancellationToken ct)
    {
        var contributor = await db.ContentContributors
            .FirstOrDefaultAsync(c => c.UserId == userId, ct);

        var submission = await db.ContentSubmissions
            .FirstOrDefaultAsync(s => s.Id == submissionId, ct)
            ?? throw ApiException.NotFound("SUBMISSION_NOT_FOUND", "Content submission not found.");

        // Allow viewing own submission or any approved one
        if (submission.Status != "approved" && (contributor == null || submission.ContributorId != contributor.Id))
            throw ApiException.NotFound("SUBMISSION_NOT_FOUND", "Content submission not found.");

        return MapSubmission(submission);
    }

    // ── Browse Marketplace ──────────────────────────────────

    public async Task<object> BrowseContentAsync(string? examTypeCode, string? subtest, string? search, int page, int pageSize, CancellationToken ct)
    {
        var query = db.ContentSubmissions
            .AsNoTracking()
            .Where(s => s.Status == "approved");

        if (!string.IsNullOrWhiteSpace(examTypeCode))
            query = query.Where(s => s.ExamFamilyCode == examTypeCode);

        if (!string.IsNullOrWhiteSpace(subtest))
            query = query.Where(s => s.SubtestCode == subtest);

        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(s =>
                s.Title.Contains(search) ||
                (s.Description != null && s.Description.Contains(search)) ||
                (s.Tags != null && s.Tags.Contains(search)));

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(s => s.ApprovedAt ?? s.SubmittedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return new
        {
            items = items.Select(MapSubmission),
            total,
            page,
            pageSize
        };
    }

    // ── Admin Review ────────────────────────────────────────

    public async Task<object> GetPendingSubmissionsAsync(int page, int pageSize, CancellationToken ct)
    {
        var query = db.ContentSubmissions.AsNoTracking()
            .Where(s => s.Status == "pending" || s.Status == "in_review");

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderBy(s => s.SubmittedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return new
        {
            items = items.Select(MapSubmission),
            total,
            page,
            pageSize
        };
    }

    public async Task<object> ReviewSubmissionAsync(string adminId, string submissionId, MarketplaceReviewRequest request, CancellationToken ct)
    {
        var submission = await db.ContentSubmissions
            .FirstOrDefaultAsync(s => s.Id == submissionId, ct)
            ?? throw ApiException.NotFound("SUBMISSION_NOT_FOUND", "Content submission not found.");

        if (request.Decision is not ("approved" or "rejected"))
            throw ApiException.Validation("INVALID_DECISION", "Decision must be 'approved' or 'rejected'.");

        var now = DateTimeOffset.UtcNow;
        submission.Status = request.Decision;
        submission.ReviewedBy = adminId;
        submission.ReviewNotes = request.Notes;

        if (request.Decision == "approved")
        {
            submission.ApprovedAt = now;

            // Update contributor stats
            var contributor = await db.ContentContributors.FindAsync([submission.ContributorId], ct);
            if (contributor != null)
            {
                contributor.ApprovedCount++;
            }

            // Optionally create a ContentItem in Draft if requested
            if (request.CreateContentItem)
            {
                var contentId = $"ci-{Guid.NewGuid():N}";
                db.ContentItems.Add(new ContentItem
                {
                    Id = contentId,
                    ExamFamilyCode = submission.ExamFamilyCode,
                    SubtestCode = submission.SubtestCode,
                    ContentType = submission.ContentType ?? "practice_task",
                    ProfessionId = submission.ProfessionId,
                    Title = submission.Title,
                    Difficulty = submission.Difficulty ?? "medium",
                    DetailJson = submission.ContentPayloadJson,
                    Status = ContentStatus.Draft,
                    SourceType = "marketplace",
                    CreatedAt = now,
                    UpdatedAt = now
                });
                submission.PublishedContentId = contentId;
            }
        }

        await db.SaveChangesAsync(ct);
        return MapSubmission(submission);
    }

    // ── Mapping ─────────────────────────────────────────────

    private static object MapContributor(ContentContributor c) => new
    {
        id = c.Id,
        userId = c.UserId,
        displayName = c.DisplayName,
        bio = c.Bio,
        verificationStatus = c.VerificationStatus,
        submissionCount = c.SubmissionCount,
        approvedCount = c.ApprovedCount,
        rating = c.Rating,
        createdAt = c.CreatedAt
    };

    private static object MapSubmission(ContentSubmission s) => new
    {
        id = s.Id,
        contributorId = s.ContributorId,
        examFamilyCode = s.ExamFamilyCode,
        subtestCode = s.SubtestCode,
        title = s.Title,
        description = s.Description,
        contentType = s.ContentType,
        professionId = s.ProfessionId,
        difficulty = s.Difficulty,
        tags = s.Tags,
        status = s.Status,
        reviewedBy = s.ReviewedBy,
        reviewNotes = s.ReviewNotes,
        publishedContentId = s.PublishedContentId,
        submittedAt = s.SubmittedAt,
        approvedAt = s.ApprovedAt,
        createdAt = s.CreatedAt
    };
}

public record MarketplaceProfileUpdateRequest(string? DisplayName, string? Bio);
public record MarketplaceSubmissionRequest(
    string? ExamFamilyCode,
    string SubtestCode,
    string Title,
    string? Description,
    string? ContentPayloadJson,
    string? ContentType,
    string? ProfessionId,
    string? Difficulty,
    string? Tags);
public record MarketplaceReviewRequest(string Decision, string? Notes, bool CreateContentItem);
