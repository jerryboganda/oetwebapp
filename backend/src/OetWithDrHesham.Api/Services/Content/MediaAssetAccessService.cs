using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using OetWithDrHesham.Api.Data;
using OetWithDrHesham.Api.Domain;
using OetWithDrHesham.Api.Services.Reading;

namespace OetWithDrHesham.Api.Services.Content;

public sealed class MediaAssetAccessService(
    LearnerDbContext db,
    IContentEntitlementService contentEntitlements,
    IReadingPolicyService readingPolicy,
    MaterialAccessService materialAccess,
    OetWithDrHesham.Api.Services.VideoLibrary.IVideoEntitlementService videoEntitlements)
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

        var relationKinds = await LoadLearnerMediaRelationKindsAsync(userId, media.Id, ct);

        if (relationKinds.Contains(LearnerReviewVoiceNoteRelation))
        {
            return true;
        }

        if (relationKinds.Contains(LearnerWritingVoiceNoteRelation))
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

        if (relationKinds.Contains(VocabularyAudioRelation))
        {
            return false;
        }

        if (relationKinds.Contains(RulebookRelation)
            && await CanLearnerAccessPublishedRulebookReferencePdfAsync(media.Id, normalizedProfession, ct))
        {
            return true;
        }

        if (relationKinds.Contains(SpeakingResourceRelation)
            && await CanLearnerAccessSpeakingSharedResourceAsync(media.Id, normalizedProfession, ct))
        {
            return true;
        }

        if (relationKinds.Contains(MaterialFileRelation)
            && await materialAccess.CanCandidateAccessMaterialFileAsync(userId, media.Id, ct))
        {
            return true;
        }

        if (relationKinds.Contains(VideoLibraryRelation)
            && await CanLearnerAccessVideoLibraryAssetAsync(userId, media.Id, normalizedProfession, ct))
        {
            return true;
        }

        if (relationKinds.Contains(ResultTemplateRelation)
            && await CanLearnerAccessActiveResultTemplateAsync(media.Id, normalizedProfession, ct))
        {
            return true;
        }

        if (relationKinds.Contains(WritingStimulusRelation)
            && await CanLearnerAccessPublishedWritingStimulusPdfAsync(media.Id, normalizedProfession, ct))
        {
            return true;
        }

        var learnerVisibleRoles = LearnerVisiblePaperAssetRoles;
        var attachedPaperAssets = relationKinds.Contains(PublishedPaperRelation)
            ? await db.ContentPaperAssets
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
                .ToListAsync(ct)
            : [];

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

        if (relationKinds.Contains(FreePreviewRelation))
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

    private const string LearnerReviewVoiceNoteRelation = "learner_review_voice_note";
    private const string LearnerWritingVoiceNoteRelation = "learner_writing_voice_note";
    private const string VocabularyAudioRelation = "vocabulary_audio";
    private const string RulebookRelation = "rulebook";
    private const string SpeakingResourceRelation = "speaking_resource";
    private const string MaterialFileRelation = "material_file";
    private const string VideoLibraryRelation = "video_library";
    private const string ResultTemplateRelation = "result_template";
    private const string WritingStimulusRelation = "writing_stimulus";
    private const string PublishedPaperRelation = "published_paper";
    private const string FreePreviewRelation = "free_preview";

    /// <summary>
    /// Classifies all learner-relevant relationships for one media asset in one
    /// relational command. Expensive entitlement/policy services are then called
    /// only for relationship kinds that actually exist.
    /// </summary>
    private Task<List<string>> LoadLearnerMediaRelationKindsAsync(
        string learnerId,
        string mediaAssetId,
        CancellationToken ct)
    {
        var reviewVoiceNotes = db.ReviewVoiceNotes
            .AsNoTracking()
            .Where(note => note.MediaAssetId == mediaAssetId && note.Status == "ready")
            .Join(
                db.ReviewRequests.AsNoTracking().Where(review => review.State == ReviewRequestState.Completed),
                note => note.ReviewRequestId,
                review => review.Id,
                (note, review) => review)
            .Join(
                db.Attempts.AsNoTracking().Where(attempt => attempt.UserId == learnerId),
                review => review.AttemptId,
                attempt => attempt.Id,
                (review, attempt) => LearnerReviewVoiceNoteRelation);

        var writingVoiceNotes = db.WritingReviewVoiceNotes
            .AsNoTracking()
            .Where(note => note.MediaAssetId == mediaAssetId && note.Status == "ready")
            .Join(
                db.WritingSubmissions.AsNoTracking().Where(submission => submission.UserId == learnerId),
                note => note.SubmissionId,
                submission => submission.Id,
                (note, submission) => submission)
            .Join(
                db.WritingTutorReviews.AsNoTracking().Where(review => review.Status == "submitted"),
                submission => submission.Id,
                review => review.SubmissionId,
                (submission, review) => LearnerWritingVoiceNoteRelation);

        var relationKinds = reviewVoiceNotes
            .Concat(writingVoiceNotes)
            .Concat(db.VocabularyTerms
                .AsNoTracking()
                .Where(term => term.AudioMediaAssetId == mediaAssetId)
                .Select(_ => VocabularyAudioRelation))
            .Concat(db.RulebookVersions
                .AsNoTracking()
                .Where(rulebook => rulebook.ReferencePdfAssetId == mediaAssetId
                    && rulebook.Status == RulebookStatus.Published)
                .Select(_ => RulebookRelation))
            .Concat(db.SpeakingSharedResources
                .AsNoTracking()
                .Where(resource => resource.MediaAssetId == mediaAssetId
                    && resource.Status == ContentStatus.Published)
                .Select(_ => SpeakingResourceRelation))
            .Concat(db.MaterialFiles
                .AsNoTracking()
                .Where(file => file.MediaAssetId == mediaAssetId)
                .Select(_ => MaterialFileRelation))
            .Concat(db.LibraryVideos
                .AsNoTracking()
                .Where(video => video.CustomThumbnailMediaAssetId == mediaAssetId)
                .Select(_ => VideoLibraryRelation))
            .Concat(db.VideoAttachments
                .AsNoTracking()
                .Where(attachment => attachment.MediaAssetId == mediaAssetId)
                .Select(_ => VideoLibraryRelation))
            .Concat(db.VideoCaptionTracks
                .AsNoTracking()
                .Where(track => track.MediaAssetId == mediaAssetId)
                .Select(_ => VideoLibraryRelation))
            .Concat(db.ResultTemplateAssets
                .AsNoTracking()
                .Where(template => template.MediaAssetId == mediaAssetId && template.IsActive)
                .Select(_ => ResultTemplateRelation))
            .Concat(db.WritingScenarios
                .AsNoTracking()
                .Where(scenario =>
                    (scenario.StimulusPdfMediaAssetId == mediaAssetId
                        || scenario.AnswerSheetPdfMediaAssetId == mediaAssetId)
                    && scenario.Status == "published")
                .Select(_ => WritingStimulusRelation))
            .Concat(db.ContentPaperAssets
                .AsNoTracking()
                .Where(asset => asset.MediaAssetId == mediaAssetId
                    && asset.Paper != null
                    && asset.Paper.Status == ContentStatus.Published)
                .Select(_ => PublishedPaperRelation))
            .Concat(db.FreePreviewAssets
                .AsNoTracking()
                .Where(preview => preview.MediaAssetId == mediaAssetId
                    && preview.Status == ContentStatus.Published)
                .Select(_ => FreePreviewRelation));

        return relationKinds.Distinct().ToListAsync(ct);
    }

    /// <summary>
    /// Video Library assets served through /v1/media/{id}/content:
    ///   • custom thumbnail — visible whenever the video itself is visible
    ///     (it shows in the locked catalog too), so NO entitlement check;
    ///   • attachments + caption tracks — require the video entitlement.
    /// All three require the owning video to be Published, release-dated,
    /// and profession-visible.
    /// </summary>
    private async Task<bool> CanLearnerAccessVideoLibraryAssetAsync(
        string userId, string mediaAssetId, string? normalizedProfession, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        var relatedVideos = await db.LibraryVideos
            .AsNoTracking()
            .Where(video => video.Status == ContentStatus.Published
                && (video.PublishAt == null || video.PublishAt <= now)
                && (video.CustomThumbnailMediaAssetId == mediaAssetId
                    || db.VideoAttachments.Any(attachment =>
                        attachment.VideoId == video.Id && attachment.MediaAssetId == mediaAssetId)
                    || db.VideoCaptionTracks.Any(track =>
                        track.VideoId == video.Id && track.MediaAssetId == mediaAssetId)))
            .Select(video => new
            {
                Video = video,
                IsThumbnail = video.CustomThumbnailMediaAssetId == mediaAssetId,
                RequiresEntitlement =
                    db.VideoAttachments.Any(attachment =>
                        attachment.VideoId == video.Id && attachment.MediaAssetId == mediaAssetId)
                    || db.VideoCaptionTracks.Any(track =>
                        track.VideoId == video.Id && track.MediaAssetId == mediaAssetId),
            })
            .ToListAsync(ct);

        if (relatedVideos.Any(candidate =>
            candidate.IsThumbnail
            && OetWithDrHesham.Api.Services.VideoLibrary.VideoLibraryLearnerService.IsProfessionVisible(
                candidate.Video.ProfessionIdsJson,
                normalizedProfession)))
        {
            return true;
        }

        foreach (var candidate in relatedVideos.Where(candidate => candidate.RequiresEntitlement))
        {
            if (!OetWithDrHesham.Api.Services.VideoLibrary.VideoLibraryLearnerService.IsProfessionVisible(
                    candidate.Video.ProfessionIdsJson, normalizedProfession))
            {
                continue;
            }
            var entitlement = await videoEntitlements.AllowAccessAsync(userId, candidate.Video, ct);
            if (entitlement.Allowed)
            {
                return true;
            }
        }
        return false;
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
            .AnyAsync(s => (s.StimulusPdfMediaAssetId == mediaAssetId || s.AnswerSheetPdfMediaAssetId == mediaAssetId)
                && s.Status == "published", ct);

    /// <summary>
    /// Learner variant: grants access when the stimulus PDF belongs to a published scenario
    /// whose <c>Profession</c> is empty (applies to all) OR matches the learner's profession.
    /// Case-insensitive, mirrors <see cref="CanLearnerAccessPublishedRulebookReferencePdfAsync"/>.
    /// </summary>
    private Task<bool> CanLearnerAccessPublishedWritingStimulusPdfAsync(string mediaAssetId, string? normalizedProfession, CancellationToken ct)
        => db.WritingScenarios
            .AsNoTracking()
            .AnyAsync(s =>
                (s.StimulusPdfMediaAssetId == mediaAssetId || s.AnswerSheetPdfMediaAssetId == mediaAssetId)
                && s.Status == "published"
                && (string.IsNullOrEmpty(s.Profession)
                    || (!string.IsNullOrWhiteSpace(normalizedProfession)
                        && s.Profession.ToLower() == normalizedProfession)), ct);
}
