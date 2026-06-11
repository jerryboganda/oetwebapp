using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Reading;

namespace OetLearner.Api.Services.Content;

public sealed class MediaAssetAccessService(
    LearnerDbContext db,
    IContentEntitlementService contentEntitlements,
    IReadingPolicyService readingPolicy,
    MaterialAccessService materialAccess)
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
                || await CanAssignedExpertAccessReviewVoiceNoteAsync(userId, media.Id, ct)
                || await CanAssignedTutorAccessWritingMarkingVoiceNoteAsync(userId, media.Id, ct)
                || await IsPublishedWritingStimulusPdfAsync(media.Id, ct))
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

        if (await CanLearnerAccessWritingMarkingVoiceNoteAsync(userId, media.Id, ct))
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

        if (await IsVocabularyAudioAsync(media.Id, ct))
        {
            return false;
        }

        if (await CanLearnerAccessPublishedRulebookReferencePdfAsync(media.Id, normalizedProfession, ct))
        {
            return true;
        }

        if (await CanLearnerAccessSpeakingSharedResourceAsync(media.Id, normalizedProfession, ct))
        {
            return true;
        }

        if (await materialAccess.CanCandidateAccessMaterialFileAsync(userId, media.Id, ct))
        {
            return true;
        }

        if (await CanLearnerAccessActiveResultTemplateAsync(media.Id, normalizedProfession, ct))
        {
            return true;
        }

        if (await CanLearnerAccessPublishedWritingStimulusPdfAsync(media.Id, normalizedProfession, ct))
        {
            return true;
        }

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

    private Task<bool> IsVocabularyAudioAsync(string mediaAssetId, CancellationToken ct)
    {
        // Vocabulary/recall audio has its own paid, no-store streaming endpoint.
        // Do not allow learners to bypass that entitlement gate through /v1/media.
        return db.VocabularyTerms
            .AsNoTracking()
            .AnyAsync(term =>
                term.AudioMediaAssetId == mediaAssetId, ct);
    }

    private Task<bool> CanLearnerAccessPublishedRulebookReferencePdfAsync(string mediaAssetId, string? normalizedProfession, CancellationToken ct)
        => db.RulebookVersions
            .AsNoTracking()
            .AnyAsync(rulebook =>
                rulebook.ReferencePdfAssetId == mediaAssetId
                && rulebook.Status == RulebookStatus.Published
                && !string.IsNullOrWhiteSpace(normalizedProfession)
                && rulebook.Profession == normalizedProfession, ct);

    private Task<bool> CanLearnerAccessSpeakingSharedResourceAsync(string mediaAssetId, string? normalizedProfession, CancellationToken ct)
        => db.SpeakingSharedResources
            .AsNoTracking()
            .AnyAsync(resource =>
                resource.MediaAssetId == mediaAssetId
                && resource.Status == ContentStatus.Published
                && (resource.ProfessionId == null
                    || (!string.IsNullOrWhiteSpace(normalizedProfession)
                        && resource.ProfessionId == normalizedProfession)), ct);

    private Task<bool> CanLearnerAccessActiveResultTemplateAsync(string mediaAssetId, string? normalizedProfession, CancellationToken ct)
        => db.ResultTemplateAssets
            .AsNoTracking()
            .AnyAsync(template =>
                template.MediaAssetId == mediaAssetId
                && template.IsActive
                && (template.ProfessionId == null
                    || (!string.IsNullOrWhiteSpace(normalizedProfession)
                        && template.ProfessionId == normalizedProfession)), ct);

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

    // Writing V2 (System A) marking voice note: the owning learner may play it once the
    // tutor review for their submission has been submitted.
    private Task<bool> CanLearnerAccessWritingMarkingVoiceNoteAsync(string learnerId, string mediaAssetId, CancellationToken ct)
        => db.WritingReviewVoiceNotes
            .AsNoTracking()
            .Where(note => note.MediaAssetId == mediaAssetId && note.Status == "ready")
            .Join(db.WritingSubmissions.AsNoTracking(), note => note.SubmissionId, submission => submission.Id, (note, submission) => submission)
            .Where(submission => submission.UserId == learnerId)
            .Join(db.WritingTutorReviews.AsNoTracking().Where(review => review.Status == "submitted"), submission => submission.Id, review => review.SubmissionId, (submission, review) => submission)
            .AnyAsync(ct);

    // Writing V2 (System A) marking voice note: a tutor with an active assignment for the
    // submission may play it (covers second/senior markers; the uploader already matches
    // media.UploadedBy above).
    private Task<bool> CanAssignedTutorAccessWritingMarkingVoiceNoteAsync(string tutorId, string mediaAssetId, CancellationToken ct)
        => db.WritingReviewVoiceNotes
            .AsNoTracking()
            .Where(note => note.MediaAssetId == mediaAssetId)
            .Join(db.WritingTutorReviewAssignments.AsNoTracking(), note => note.SubmissionId, assignment => assignment.SubmissionId, (note, assignment) => assignment)
            .AnyAsync(assignment => assignment.TutorId == tutorId
                && (assignment.Status == "claimed" || assignment.Status == "submitted"), ct);

    /// <summary>
    /// Returns true when the media asset is referenced by at least one <em>published</em>
    /// WritingScenario as its stimulus PDF. Used to gate both expert and learner access.
    /// </summary>
    private Task<bool> IsPublishedWritingStimulusPdfAsync(string mediaAssetId, CancellationToken ct)
        => db.WritingScenarios
            .AsNoTracking()
            .AnyAsync(s => s.StimulusPdfMediaAssetId == mediaAssetId && s.Status == "published", ct);

    /// <summary>
    /// Learner variant: grants access when the stimulus PDF belongs to a published scenario
    /// whose <c>Profession</c> is empty (applies to all) OR matches the learner's profession.
    /// Case-insensitive, mirrors <see cref="CanLearnerAccessPublishedRulebookReferencePdfAsync"/>.
    /// </summary>
    private Task<bool> CanLearnerAccessPublishedWritingStimulusPdfAsync(string mediaAssetId, string? normalizedProfession, CancellationToken ct)
        => db.WritingScenarios
            .AsNoTracking()
            .AnyAsync(s =>
                s.StimulusPdfMediaAssetId == mediaAssetId
                && s.Status == "published"
                && (string.IsNullOrEmpty(s.Profession)
                    || (!string.IsNullOrWhiteSpace(normalizedProfession)
                        && s.Profession.ToLower() == normalizedProfession)), ct);
}
