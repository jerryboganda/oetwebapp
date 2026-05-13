using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Reading;

namespace OetLearner.Api.Services.Content;

public sealed class MediaAssetAccessService(
    LearnerDbContext db,
    IContentEntitlementService contentEntitlements,
    IReadingPolicyService readingPolicy)
{
    public async Task<bool> CanAccessAsync(ClaimsPrincipal principal, string mediaAssetId, CancellationToken ct)
    {
        var media = await db.MediaAssets.AsNoTracking().FirstOrDefaultAsync(asset => asset.Id == mediaAssetId, ct);
        return media is not null && await CanAccessAsync(principal, media, ct);
    }

    public async Task<bool> CanAccessAsync(ClaimsPrincipal principal, MediaAsset media, CancellationToken ct)
    {
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return false;
        }

        var role = principal.FindFirstValue(ClaimTypes.Role) ?? string.Empty;
        if (string.Equals(role, ApplicationUserRoles.Admin, StringComparison.OrdinalIgnoreCase)
            || string.Equals(media.UploadedBy, userId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(role, ApplicationUserRoles.Expert, StringComparison.OrdinalIgnoreCase))
        {
            if (await CanAssignedExpertAccessWritingPaperAssetAsync(userId, media.Id, ct)
                || await CanAssignedExpertAccessReviewVoiceNoteAsync(userId, media.Id, ct))
            {
                return true;
            }

            return false;
        }

        if (!string.Equals(role, ApplicationUserRoles.Learner, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (await CanLearnerAccessReviewVoiceNoteAsync(userId, media.Id, ct))
        {
            return true;
        }

        var profession = principal.FindFirstValue("prof") ?? principal.FindFirstValue("profession");
        if (string.IsNullOrWhiteSpace(profession))
        {
            profession = await db.Users
                .AsNoTracking()
                .Where(user => user.Id == userId)
                .Select(user => user.ActiveProfessionId)
                .SingleOrDefaultAsync(ct);
        }

        var normalizedProfession = profession?.Trim().ToLowerInvariant();
        var learnerVisibleRoles = LearnerVisiblePaperAssetRoles;
        var attachedPaperAssets = await db.ContentPaperAssets
            .AsNoTracking()
            .Where(asset => asset.MediaAssetId == media.Id
                && asset.Paper != null
                && asset.Paper.Status == ContentStatus.Published)
            .Select(asset => new
            {
                asset.Role,
                asset.IsPrimary,
                Paper = asset.Paper!,
            })
            .ToListAsync(ct);

        var isReadingQuestionPaperAsset = attachedPaperAssets.Any(candidate =>
            candidate.Role == PaperAssetRole.QuestionPaper
            && string.Equals(candidate.Paper.SubtestCode, "reading", StringComparison.OrdinalIgnoreCase));
        if (isReadingQuestionPaperAsset
            && !(await readingPolicy.ResolveForUserAsync(userId, ct)).AllowPaperReadingMode)
        {
            return false;
        }

        var candidatePaperAssets = attachedPaperAssets
            .Where(asset => asset.IsPrimary
                && learnerVisibleRoles.Contains(asset.Role)
                && (asset.Paper.AppliesToAllProfessions
                    || (!string.IsNullOrWhiteSpace(normalizedProfession)
                        && asset.Paper.ProfessionId == normalizedProfession)))
            .ToList();

        foreach (var candidate in candidatePaperAssets)
        {
            var paper = candidate.Paper;
            var entitlement = await contentEntitlements.AllowAccessAsync(userId, paper, ct);
            if (entitlement.Allowed)
            {
                return true;
            }
        }

        if (attachedPaperAssets.Count > 0)
        {
            return false;
        }

        if (await IsPublishedFreePreviewMediaAsync(media.Id, ct))
        {
            return true;
        }

        return false;
    }

    private static readonly PaperAssetRole[] LearnerVisiblePaperAssetRoles =
    [
        PaperAssetRole.Audio,
        PaperAssetRole.QuestionPaper,
        PaperAssetRole.CaseNotes,
        PaperAssetRole.RoleCard,
        PaperAssetRole.WarmUpQuestions,
    ];

    private Task<bool> IsPublishedFreePreviewMediaAsync(string mediaAssetId, CancellationToken ct)
        => db.FreePreviewAssets
            .AsNoTracking()
            .AnyAsync(preview =>
                preview.MediaAssetId == mediaAssetId
                && preview.Status == ContentStatus.Published, ct);

    private Task<bool> CanAssignedExpertAccessWritingPaperAssetAsync(string expertId, string mediaAssetId, CancellationToken ct)
        => db.WritingAttemptAssets
            .AsNoTracking()
            .Where(asset => asset.MediaAssetId == mediaAssetId)
            .Join(db.ReviewRequests.AsNoTracking(), asset => asset.AttemptId, review => review.AttemptId, (asset, review) => review)
            .Join(db.ExpertReviewAssignments.AsNoTracking(), review => review.Id, assignment => assignment.ReviewRequestId, (review, assignment) => assignment)
            .AnyAsync(assignment => assignment.AssignedReviewerId == expertId
                && assignment.ClaimState != ExpertAssignmentState.Released, ct);

    private Task<bool> CanAssignedExpertAccessReviewVoiceNoteAsync(string expertId, string mediaAssetId, CancellationToken ct)
        => db.ReviewVoiceNotes
            .AsNoTracking()
            .Where(note => note.MediaAssetId == mediaAssetId)
            .Join(db.ExpertReviewAssignments.AsNoTracking(), note => note.ReviewRequestId, assignment => assignment.ReviewRequestId, (note, assignment) => assignment)
            .AnyAsync(assignment => assignment.AssignedReviewerId == expertId
                && assignment.ClaimState != ExpertAssignmentState.Released, ct);

    private Task<bool> CanLearnerAccessReviewVoiceNoteAsync(string learnerId, string mediaAssetId, CancellationToken ct)
        => db.ReviewVoiceNotes
            .AsNoTracking()
            .Where(note => note.MediaAssetId == mediaAssetId && note.Status == "ready")
            .Join(db.ReviewRequests.AsNoTracking().Where(review => review.State == ReviewRequestState.Completed), note => note.ReviewRequestId, review => review.Id, (note, review) => review)
            .Join(db.Attempts.AsNoTracking(), review => review.AttemptId, attempt => attempt.Id, (review, attempt) => attempt)
            .AnyAsync(attempt => attempt.UserId == learnerId, ct);
}
