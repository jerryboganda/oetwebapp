using System.Globalization;
using System.Linq.Expressions;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Content;

namespace OetLearner.Api.Services;

public partial class ExpertService(LearnerDbContext db, ILogger<ExpertService> logger, IFileStorage fileStorage, PlatformLinkService platformLinks, NotificationService notifications, PronunciationService pronunciationService)
{
    // Canonical writing criterion codes (match rulebooks/writing/common/assessment-criteria.json).
    // Purpose is scored 0\u20133; all others 0\u20137 (rulebook R16.1 / R16.2). British spelling is intentional.
    private static readonly string[] WritingCriteria = ["purpose", "content", "conciseness_clarity", "genre_style", "organisation_layout", "language"];

    // Canonical OET Speaking 9-criterion codes per official CBLA format
    // (source: rulebooks/speaking/common/assessment-criteria.json; Dr. Ahmed Hesham corrections April 2026).
    //   Linguistic (4, scale 0\u20136 each):
    //     intelligibility, fluency, appropriateness, grammar (Resources of Grammar & Expression)
    //   Clinical Communication (5, scale 0\u20133 each):
    //     relationshipBuilding, patientPerspective, providingStructure,
    //     informationGathering, informationGiving
    // The legacy aggregate "clinicalCommunication" key is DEPRECATED; it is not accepted on new writes.
    private static readonly string[] SpeakingCriteria = [
        "intelligibility", "fluency", "appropriateness", "grammar",
        "relationshipBuilding", "patientPerspective", "providingStructure",
        "informationGathering", "informationGiving"
    ];
    private static readonly string[] SpeakingLinguisticCriteria = ["intelligibility", "fluency", "appropriateness", "grammar"];
    private static readonly string[] SpeakingClinicalCriteria = ["relationshipBuilding", "patientPerspective", "providingStructure", "informationGathering", "informationGiving"];
    private const int MaxQueuePageSize = 100;
    private const int MaxLearnerPageSize = 100;
    private const int MaxFinalCommentLength = 4000;
    private const int MaxCriterionCommentLength = 1500;
    private const int MaxCommentTextLength = 1500;
    private const int MaxScratchpadLength = 4000;
    private const int MaxChecklistItemLabelLength = 200;
    private const int MaxChecklistItemCount = 12;
    private const int MaxReworkReasonLength = 1000;
    private const int MaxCalibrationNotesLength = 1500;
    private const int MaxVoiceNoteTranscriptLength = 12000;
    private const int MaxVoiceNoteWrittenNotesLength = 4000;
    private const int MaxVoiceNoteDurationSeconds = 3600;

    private const int SlaHoursStandardForSubmit = 48;
    private const int SlaHoursExpressForSubmit = 12;

    private async Task<ExpertUser> EnsureExpertAsync(string reviewerId, CancellationToken ct)
    {
        var expert = await db.ExpertUsers.FirstOrDefaultAsync(existingExpert => existingExpert.Id == reviewerId, ct);
        if (expert is null)
        {
            throw ApiException.Forbidden("expert_profile_not_found", "Expert profile not found.");
        }

        if (!expert.IsActive)
        {
            throw ApiException.Forbidden("account_suspended", "This expert account is not available.");
        }

        return expert;
    }

    private async Task<ReadContext> LoadReadContextAsync(string reviewRequestId, string reviewerId, CancellationToken ct, bool requireActiveAssignment)
    {
        await EnsureExpertAsync(reviewerId, ct);

        var reviewRequest = await db.ReviewRequests
            .AsNoTracking()
            .FirstOrDefaultAsync(existingReviewRequest => existingReviewRequest.Id == reviewRequestId, ct)
            ?? throw ApiException.NotFound("review_request_not_found", "The requested review does not exist.");

        var attempt = await db.Attempts
            .AsNoTracking()
            .FirstOrDefaultAsync(existingAttempt => existingAttempt.Id == reviewRequest.AttemptId, ct)
            ?? throw ApiException.NotFound("attempt_not_found", "The attempt linked to this review could not be found.");

        var learner = await db.Users.AsNoTracking().FirstOrDefaultAsync(user => user.Id == attempt.UserId, ct);
        var content = await db.ContentItems.AsNoTracking().FirstOrDefaultAsync(item => item.Id == attempt.ContentId, ct);
        var evaluation = await FirstOrDefaultOrderedDescendingAsync(
            db.Evaluations
                .AsNoTracking()
                .Where(existingEvaluation => existingEvaluation.AttemptId == attempt.Id),
            existingEvaluation => existingEvaluation.GeneratedAt,
            ct);

        var assignments = await db.ExpertReviewAssignments
            .AsNoTracking()
            .Where(assignment => assignment.ReviewRequestId == reviewRequestId)
            .ToListAsync(ct);

        var activeAssignment = assignments
            .Where(assignment => assignment.ClaimState != ExpertAssignmentState.Released)
            .OrderByDescending(assignment => assignment.AssignedAt ?? DateTimeOffset.MinValue)
            .FirstOrDefault();

        var hasHistoricalAccess = assignments.Any(assignment => string.Equals(assignment.AssignedReviewerId, reviewerId, StringComparison.Ordinal))
            || await db.ExpertReviewDrafts.AsNoTracking().AnyAsync(draft => draft.ReviewRequestId == reviewRequestId && draft.ReviewerId == reviewerId, ct);

        if (requireActiveAssignment)
        {
            if (activeAssignment is null || !string.Equals(activeAssignment.AssignedReviewerId, reviewerId, StringComparison.Ordinal))
            {
                throw ApiException.Forbidden("review_not_owned", "Claim this review before opening the workspace.");
            }
        }
        else if (activeAssignment is not null && !string.Equals(activeAssignment.AssignedReviewerId, reviewerId, StringComparison.Ordinal) && !hasHistoricalAccess)
        {
            throw ApiException.Forbidden("review_not_visible", "This review is assigned to another reviewer.");
        }

        var assignedReviewerIds = assignments
            .Select(assignment => assignment.AssignedReviewerId)
            .Where(assignedReviewerId => !string.IsNullOrWhiteSpace(assignedReviewerId))
            .Distinct()
            .Cast<string>()
            .ToList();

        var assignedReviewers = assignedReviewerIds.Count == 0
            ? new Dictionary<string, ExpertUser>()
            : await db.ExpertUsers
                .AsNoTracking()
                .Where(expert => assignedReviewerIds.Contains(expert.Id))
                .ToDictionaryAsync(expert => expert.Id, ct);

        var draftEntity = await FirstOrDefaultOrderedDescendingAsync(
            db.ExpertReviewDrafts
                .AsNoTracking()
                .Where(existingDraft => existingDraft.ReviewRequestId == reviewRequestId && existingDraft.ReviewerId == reviewerId),
            existingDraft => existingDraft.DraftSavedAt,
            ct);

        return new ReadContext(reviewRequest, attempt, learner, activeAssignment, content, evaluation, BuildDraftResponse(draftEntity), assignedReviewers);
    }

    private async Task<TrackedWriteContext> LoadWriteContextAsync(string reviewRequestId, string reviewerId, CancellationToken ct)
    {
        var expert = await EnsureExpertAsync(reviewerId, ct);

        var reviewRequest = await db.ReviewRequests.FirstOrDefaultAsync(existingReviewRequest => existingReviewRequest.Id == reviewRequestId, ct)
            ?? throw ApiException.NotFound("review_request_not_found", "The requested review does not exist.");

        if (reviewRequest.State == ReviewRequestState.Completed || reviewRequest.State == ReviewRequestState.Cancelled)
        {
            throw ApiException.Conflict("review_not_editable", "Completed reviews cannot be modified.");
        }

        var activeAssignment = await GetActiveAssignmentAsync(reviewRequestId, tracked: true, ct);
        if (activeAssignment is null || !string.Equals(activeAssignment.AssignedReviewerId, reviewerId, StringComparison.Ordinal))
        {
            throw ApiException.Forbidden("review_not_owned", "You can only modify reviews currently assigned to you.");
        }

        var attempt = await db.Attempts.FirstOrDefaultAsync(existingAttempt => existingAttempt.Id == reviewRequest.AttemptId, ct)
            ?? throw ApiException.NotFound("attempt_not_found", "The attempt linked to this review could not be found.");

        return new TrackedWriteContext(reviewRequest, attempt, activeAssignment, expert);
    }

    private async Task<List<ReviewRequest>> LoadAccessibleLearnerReviewRequestsAsync(string learnerId, string reviewerId, CancellationToken ct)
    {
        var learnerAttemptIds = await db.Attempts
            .AsNoTracking()
            .Where(attempt => attempt.UserId == learnerId)
            .Select(attempt => attempt.Id)
            .ToListAsync(ct);

        if (learnerAttemptIds.Count == 0)
        {
            return [];
        }

        var accessibleReviewIds = await db.ExpertReviewAssignments
            .AsNoTracking()
            .Where(assignment => assignment.AssignedReviewerId == reviewerId)
            .Select(assignment => assignment.ReviewRequestId)
            .Distinct()
            .ToListAsync(ct);

        if (accessibleReviewIds.Count == 0)
        {
            return [];
        }

        return await ToOrderedListDescendingAsync(
            db.ReviewRequests
                .AsNoTracking()
                .Where(reviewRequest => learnerAttemptIds.Contains(reviewRequest.AttemptId) && accessibleReviewIds.Contains(reviewRequest.Id)),
            reviewRequest => reviewRequest.CompletedAt ?? reviewRequest.CreatedAt,
            ct);
    }

    private async Task<ExpertReviewAssignment?> GetActiveAssignmentAsync(string reviewRequestId, bool tracked, CancellationToken ct)
    {
        var query = db.ExpertReviewAssignments.Where(assignment => assignment.ReviewRequestId == reviewRequestId && assignment.ClaimState != ExpertAssignmentState.Released);
        if (!tracked)
        {
            query = query.AsNoTracking();
        }

        if (!db.Database.IsSqlite())
        {
            return await query
                .OrderByDescending(assignment => assignment.AssignedAt ?? DateTimeOffset.MinValue)
                .ThenByDescending(assignment => assignment.ReleasedAt ?? DateTimeOffset.MinValue)
                .FirstOrDefaultAsync(ct);
        }

        return (await query.ToListAsync(ct))
            .OrderByDescending(assignment => assignment.AssignedAt ?? DateTimeOffset.MinValue)
            .ThenByDescending(assignment => assignment.ReleasedAt ?? DateTimeOffset.MinValue)
            .FirstOrDefault();
    }

    private async Task<List<TItem>> ToOrderedListDescendingAsync<TItem, TKey>(
        IQueryable<TItem> query,
        Expression<Func<TItem, TKey>> orderBy,
        CancellationToken ct,
        int? take = null)
    {
        if (!db.Database.IsSqlite())
        {
            IQueryable<TItem> orderedQuery = query.OrderByDescending(orderBy);
            if (take is int takeCount)
            {
                orderedQuery = orderedQuery.Take(takeCount);
            }

            return await orderedQuery.ToListAsync(ct);
        }

        IEnumerable<TItem> orderedItems = (await query.ToListAsync(ct))
            .OrderByDescending(orderBy.Compile());

        if (take is int takeLimit)
        {
            orderedItems = orderedItems.Take(takeLimit);
        }

        return orderedItems.ToList();
    }

    private async Task<TItem?> FirstOrDefaultOrderedDescendingAsync<TItem, TKey>(
        IQueryable<TItem> query,
        Expression<Func<TItem, TKey>> orderBy,
        CancellationToken ct)
    {
        if (!db.Database.IsSqlite())
        {
            return await query
                .OrderByDescending(orderBy)
                .FirstOrDefaultAsync(ct);
        }

        return (await query.ToListAsync(ct))
            .OrderByDescending(orderBy.Compile())
            .FirstOrDefault();
    }

    private static List<ExpertQueueItemResponse> ApplyQueueFilters(List<ExpertQueueItemResponse> items, ExpertQueueQueryRequest request)
    {
        var filtered = items.AsEnumerable();
        var typeFilter = SplitCsv(request.Type);
        var professionFilter = SplitCsv(request.Profession);
        var priorityFilter = SplitCsv(request.Priority);
        var statusFilter = SplitCsv(request.Status);
        var confidenceFilter = SplitCsv(request.Confidence);
        var assignmentFilter = SplitCsv(request.Assignment);

        if (typeFilter.Count > 0)
        {
            filtered = filtered.Where(item => typeFilter.Contains(item.Type));
        }

        if (professionFilter.Count > 0)
        {
            filtered = filtered.Where(item => professionFilter.Contains(item.Profession));
        }

        if (priorityFilter.Count > 0)
        {
            filtered = filtered.Where(item => priorityFilter.Contains(item.Priority));
        }

        if (statusFilter.Count > 0)
        {
            filtered = filtered.Where(item => statusFilter.Contains(item.Status));
        }

        if (confidenceFilter.Count > 0)
        {
            filtered = filtered.Where(item => confidenceFilter.Contains(item.AiConfidence));
        }

        if (assignmentFilter.Count > 0)
        {
            filtered = filtered.Where(item =>
            {
                var isAssigned = !string.Equals(item.AssignmentState, "unassigned", StringComparison.OrdinalIgnoreCase);
                return (assignmentFilter.Contains("assigned") && isAssigned) || (assignmentFilter.Contains("unassigned") && !isAssigned);
            });
        }

        if (request.Overdue == true)
        {
            filtered = filtered.Where(item => item.IsOverdue);
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var query = request.Search.Trim().ToLowerInvariant();
            filtered = filtered.Where(item => item.Id.Contains(query, StringComparison.OrdinalIgnoreCase)
                || item.LearnerName.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        return filtered
            .OrderByDescending(item => item.IsOverdue)
            .ThenBy(item => item.SlaDue)
            .ThenBy(item => PriorityWeight(item.Priority))
            .ThenBy(item => item.CreatedAt)
            .ToList();
    }

    private static HashSet<string> SplitCsv(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(part => part.ToLowerInvariant())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static ExpertQueueItemResponse? BuildQueueItem(
        ReviewRequest reviewRequest,
        IReadOnlyDictionary<string, Attempt> attempts,
        IReadOnlyDictionary<string, LearnerUser> learners,
        IReadOnlyDictionary<string, ExpertReviewAssignment> activeAssignments,
        IReadOnlyDictionary<string, ExpertUser> reviewers,
        IReadOnlyDictionary<string, Evaluation> evaluations,
        string reviewerId,
        DateTimeOffset now)
    {
        if (!attempts.TryGetValue(reviewRequest.AttemptId, out var attempt))
        {
            return null;
        }

        activeAssignments.TryGetValue(reviewRequest.Id, out var activeAssignment);
        var isVisible = activeAssignment is null || string.Equals(activeAssignment.AssignedReviewerId, reviewerId, StringComparison.Ordinal);
        if (!isVisible)
        {
            return null;
        }

        learners.TryGetValue(attempt.UserId, out var learner);
        evaluations.TryGetValue(attempt.Id, out var evaluation);
        var type = string.Equals(reviewRequest.SubtestCode, "speaking", StringComparison.OrdinalIgnoreCase) ? "speaking" : "writing";
        var assignedReviewerName = ResolveReviewerName(activeAssignment?.AssignedReviewerId, reviewers);
        var slaDue = CalculateSlaDueAt(reviewRequest);
        var isOverdue = IsOverdue(reviewRequest, now);

        return new ExpertQueueItemResponse(
            reviewRequest.Id,
            attempt.UserId,
            learner?.DisplayName ?? "Unknown learner",
            learner?.ActiveProfessionId ?? "nursing",
            reviewRequest.SubtestCode,
            type,
            ToAiConfidence(evaluation?.ConfidenceBand),
            MapPriority(reviewRequest.TurnaroundOption),
            slaDue,
            MapSlaState(reviewRequest, now),
            isOverdue,
            activeAssignment?.AssignedReviewerId,
            assignedReviewerName,
            MapAssignmentState(activeAssignment),
            MapQueueStatus(reviewRequest, activeAssignment, reviewerId, now),
            attempt.ContentId,
            attempt.Id,
            reviewRequest.CreatedAt,
            BuildPermissions(reviewRequest, activeAssignment, reviewerId));
    }

    private static ExpertReviewActionsResponse BuildPermissions(ReviewRequest reviewRequest, ExpertReviewAssignment? activeAssignment, string reviewerId)
    {
        var isOwnedByReviewer = activeAssignment is not null && string.Equals(activeAssignment.AssignedReviewerId, reviewerId, StringComparison.Ordinal);
        var isClaimedByReviewer = isOwnedByReviewer && activeAssignment!.ClaimState == ExpertAssignmentState.Claimed;
        var isCompleted = reviewRequest.State == ReviewRequestState.Completed;
        var canClaim = !isCompleted && (activeAssignment is null || isOwnedByReviewer) && !isClaimedByReviewer;
        var canRelease = !isCompleted && isOwnedByReviewer;
        var canOpen = isOwnedByReviewer;
        var canMutate = isOwnedByReviewer && !isCompleted;

        return new ExpertReviewActionsResponse(
            canClaim,
            canRelease,
            canOpen,
            canMutate,
            canMutate,
            canMutate,
            isCompleted);
    }

    private static string MapQueueStatus(ReviewRequest reviewRequest, ExpertReviewAssignment? activeAssignment, string reviewerId, DateTimeOffset now)
    {
        if (IsOverdue(reviewRequest, now))
        {
            return "overdue";
        }

        if (reviewRequest.State == ReviewRequestState.Completed)
        {
            return "completed";
        }

        if (activeAssignment is not null && string.Equals(activeAssignment.AssignedReviewerId, reviewerId, StringComparison.Ordinal))
        {
            return activeAssignment.ClaimState == ExpertAssignmentState.Claimed ? "in_progress" : "assigned";
        }

        return reviewRequest.State switch
        {
            ReviewRequestState.InReview => "assigned",
            ReviewRequestState.Failed => "blocked",
            _ => "queued"
        };
    }

    private static string MapAssignmentState(ExpertReviewAssignment? activeAssignment)
    {
        if (activeAssignment is null)
        {
            return "unassigned";
        }

        return activeAssignment.ClaimState switch
        {
            ExpertAssignmentState.Assigned => "assigned",
            ExpertAssignmentState.Claimed => "claimed",
            ExpertAssignmentState.Reassigned => "reassigned",
            _ => "assigned"
        };
    }

    private static string MapPriority(string? turnaround)
    {
        return turnaround?.ToLowerInvariant() switch
        {
            "express" => "high",
            "standard" => "normal",
            _ => "normal"
        };
    }

    private static int PriorityWeight(string priority)
    {
        return priority.ToLowerInvariant() switch
        {
            "high" => 0,
            "normal" => 1,
            "low" => 2,
            _ => 3
        };
    }

    private static DateTimeOffset CalculateSlaDueAt(ReviewRequest reviewRequest)
        => reviewRequest.CreatedAt.AddHours(string.Equals(reviewRequest.TurnaroundOption, "express", StringComparison.OrdinalIgnoreCase) ? 24 : 48);

    private static bool IsOverdue(ReviewRequest reviewRequest, DateTimeOffset now)
        => reviewRequest.State != ReviewRequestState.Completed && CalculateSlaDueAt(reviewRequest) <= now;

    private static string MapSlaState(ReviewRequest reviewRequest, DateTimeOffset now)
    {
        var slaDue = CalculateSlaDueAt(reviewRequest);

        if (reviewRequest.State == ReviewRequestState.Completed)
        {
            return (reviewRequest.CompletedAt ?? now) <= slaDue ? "completed_on_time" : "completed_late";
        }

        if (slaDue <= now)
        {
            return "overdue";
        }

        return slaDue - now <= TimeSpan.FromHours(6) ? "at_risk" : "on_track";
    }

    private static string? ResolveReviewerName(string? reviewerId, IReadOnlyDictionary<string, ExpertUser> reviewers)
    {
        if (string.IsNullOrWhiteSpace(reviewerId))
        {
            return null;
        }

        return reviewers.TryGetValue(reviewerId, out var reviewer) ? reviewer.DisplayName : reviewerId;
    }

    private static string ToAiConfidence(ConfidenceBand? confidenceBand)
    {
        return confidenceBand?.ToString().ToLowerInvariant() ?? "unknown";
    }

    private static Dictionary<string, int> NormalizeAiSuggestedScores(Evaluation? evaluation, bool isWriting)
    {
        var normalized = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var payload = JsonSupport.Deserialize<List<Dictionary<string, object?>>>(evaluation?.CriterionScoresJson, []);
        foreach (var item in payload)
        {
            var criterionCode = NormalizeCriterionKey(item.TryGetValue("criterionCode", out var value) ? value?.ToString() : null, isWriting ? WritingCriteria : SpeakingCriteria);
            if (criterionCode is null)
            {
                continue;
            }

            normalized[criterionCode] = ParseScoreRangeAverage(item.TryGetValue("scoreRange", out var scoreRange) ? scoreRange?.ToString() : null) ?? 0;
        }

        if (normalized.Count > 0)
        {
            return normalized;
        }

        return isWriting
            ? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                // Purpose is the ONLY writing criterion on the 0\u20133 scale; others 0\u20137 (rulebook R16.1 / R16.2).
                ["purpose"] = 2,
                ["content"] = 4,
                ["conciseness_clarity"] = 4,
                ["genre_style"] = 4,
                ["organisation_layout"] = 4,
                ["language"] = 4
            }
            : new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                // Linguistic (0–6)
                ["intelligibility"] = 4,
                ["fluency"] = 3,
                ["appropriateness"] = 4,
                ["grammar"] = 4,
                // Clinical Communication (0–3)
                ["relationshipBuilding"] = 2,
                ["patientPerspective"] = 2,
                ["providingStructure"] = 2,
                ["informationGathering"] = 2,
                ["informationGiving"] = 2
            };
    }

    private static ExpertArtifactStateResponse BuildEvaluationArtifactState(Evaluation? evaluation, string pendingMessage)
    {
        if (evaluation is null)
        {
            return new ExpertArtifactStateResponse("queued", false, pendingMessage);
        }

        var state = evaluation.State switch
        {
            AsyncState.Completed => "completed",
            AsyncState.Failed => "failed",
            AsyncState.Processing => "processing",
            _ => "queued"
        };

        return new ExpertArtifactStateResponse(state, false, state == "completed" ? null : pendingMessage);
    }

    private static ExpertArtifactStateResponse BuildTranscriptArtifactState(IReadOnlyList<ExpertTranscriptLineResponse> transcriptLines)
    {
        return transcriptLines.Count == 0
            ? new ExpertArtifactStateResponse("processing", false, "Transcript is still being processed.")
            : new ExpertArtifactStateResponse("completed", false, null);
    }

    private static ExpertArtifactStateResponse BuildAudioArtifactState(Attempt attempt)
    {
        if (!string.IsNullOrWhiteSpace(attempt.AudioObjectKey))
        {
            return new ExpertArtifactStateResponse("completed", false, null);
        }

        return attempt.AudioUploadState switch
        {
            UploadState.Failed => new ExpertArtifactStateResponse("failed", false, "The learner audio upload did not complete successfully."),
            UploadState.Pending => new ExpertArtifactStateResponse("queued", false, "Audio is not available yet."),
            _ => new ExpertArtifactStateResponse("processing", false, "Audio is still being finalized.")
        };
    }

    private static ExpertArtifactStateResponse BuildPaperArtifactState(IReadOnlyList<WritingPaperAssetResponse> assets)
    {
        if (assets.Count == 0) return new ExpertArtifactStateResponse("empty", false, null);
        if (assets.All(asset => asset.ExtractionState == "completed")) return new ExpertArtifactStateResponse("completed", false, null);
        if (assets.Any(asset => asset.ExtractionState == "completed")) return new ExpertArtifactStateResponse("partial", false, "Some handwritten pages could not be extracted.");
        if (assets.Any(asset => asset.ExtractionState == "processing" || asset.ExtractionState == "queued")) return new ExpertArtifactStateResponse("processing", false, "Handwritten pages are still being processed.");
        return new ExpertArtifactStateResponse("failed", false, "OCR did not extract readable text from the uploaded pages.");
    }

    private static ExpertArtifactStateResponse BuildVoiceNoteArtifactState(IReadOnlyList<ReviewVoiceNoteResponse> voiceNotes)
        => voiceNotes.Count == 0
            ? new ExpertArtifactStateResponse("empty", false, "Attach a voice note before returning a Dr. Ahmed writing review.")
            : new ExpertArtifactStateResponse("completed", false, null);

    private async Task<IReadOnlyList<WritingPaperAssetResponse>> LoadWritingPaperAssetResponsesAsync(string attemptId, CancellationToken ct)
    {
        var rows = await db.WritingAttemptAssets
            .AsNoTracking()
            .Where(asset => asset.AttemptId == attemptId)
            .OrderBy(asset => asset.PageNumber)
            .Join(db.MediaAssets.AsNoTracking(), asset => asset.MediaAssetId, media => media.Id, (asset, media) => new { asset, media })
            .ToListAsync(ct);

        return rows.Select(row => new WritingPaperAssetResponse(
            row.asset.Id,
            row.media.Id,
            row.media.OriginalFilename,
            row.media.MimeType,
            row.media.Format,
            row.media.SizeBytes,
            row.asset.PageNumber,
            row.asset.ExtractionState,
            row.asset.ExtractedText?.Length ?? 0,
            row.asset.ExtractionMessage,
            platformLinks.BuildApiUrl($"/v1/media/{Uri.EscapeDataString(row.media.Id)}/content"))).ToList();
    }

    private async Task<IReadOnlyList<ReviewVoiceNoteResponse>> LoadReviewVoiceNoteResponsesAsync(string reviewRequestId, CancellationToken ct)
    {
        var rows = await db.ReviewVoiceNotes
            .AsNoTracking()
            .Where(note => note.ReviewRequestId == reviewRequestId)
            .OrderByDescending(note => note.CreatedAt)
            .Join(db.MediaAssets.AsNoTracking(), note => note.MediaAssetId, media => media.Id, (note, media) => new { note, media })
            .ToListAsync(ct);

        return rows.Select(row => new ReviewVoiceNoteResponse(
            row.note.Id,
            row.note.ReviewRequestId,
            row.media.Id,
            row.media.OriginalFilename,
            row.media.MimeType,
            row.note.DurationSeconds,
            row.note.TranscriptText,
            row.note.WrittenNotes,
            JsonSupport.Deserialize<Dictionary<string, int>>(row.note.RubricJson, new Dictionary<string, int>()),
            row.note.Status,
            row.note.CreatedAt,
            platformLinks.BuildApiUrl($"/v1/media/{Uri.EscapeDataString(row.media.Id)}/content"))).ToList();
    }

    private static string BuildWritingAiDraftFeedback(Evaluation? evaluation)
    {
        if (evaluation is null)
        {
            return "AI feedback is still being prepared for this writing review.";
        }

        var feedbackItems = JsonSupport.Deserialize<List<Dictionary<string, object?>>>(evaluation.FeedbackItemsJson, []);
        var messages = feedbackItems
            .Select(item => item.TryGetValue("message", out var message) ? message?.ToString() : null)
            .Where(message => !string.IsNullOrWhiteSpace(message))
            .Select(message => $"- {message}")
            .ToList();

        if (messages.Count > 0)
        {
            return string.Join(Environment.NewLine, messages);
        }

        var strengths = JsonSupport.Deserialize(evaluation.StrengthsJson, Array.Empty<string>());
        var issues = JsonSupport.Deserialize(evaluation.IssuesJson, Array.Empty<string>());
        var sections = new List<string>();
        if (strengths.Length > 0)
        {
            sections.Add("Strengths:" + Environment.NewLine + string.Join(Environment.NewLine, strengths.Select(strength => $"- {strength}")));
        }

        if (issues.Length > 0)
        {
            sections.Add("Improvement areas:" + Environment.NewLine + string.Join(Environment.NewLine, issues.Select(issue => $"- {issue}")));
        }

        return sections.Count > 0
            ? string.Join(Environment.NewLine + Environment.NewLine, sections)
            : "AI feedback is available but did not contain reviewer-ready notes.";
    }

    private static string? ExtractModelAnswer(ContentItem? content)
    {
        if (content is null || string.IsNullOrWhiteSpace(content.ModelAnswerJson))
        {
            return null;
        }

        var payload = JsonSupport.Deserialize(content.ModelAnswerJson, new Dictionary<string, object?>());
        if (payload.TryGetValue("text", out var value) && value is not null)
        {
            return value.ToString();
        }

        return content.ModelAnswerJson;
    }

    private static ExpertSpeakingRoleCardResponse ExtractRoleCard(ContentItem? content)
    {
        var fallback = new ExpertSpeakingRoleCardResponse(
            "Nurse",
            "Ward",
            "Patient",
            "Provide a clinical handover.",
            null,
            [],
            "neutral",
            "Build rapport and complete the clinical task.",
            "roleplay",
            [],
            SpeakingContentStructure.DefaultPrepTimeSeconds,
            SpeakingContentStructure.DefaultRoleplayTimeSeconds,
            null,
            SpeakingContentStructure.PracticeDisclaimer);
        if (content is null)
        {
            return fallback;
        }

        var payload = SpeakingContentStructure.ExtractStructure(content.DetailJson);
        var candidate = SpeakingContentStructure.ToDictionary(SpeakingContentStructure.ReadValue(payload, "candidateCard"));
        var interlocutor = SpeakingContentStructure.ToDictionary(SpeakingContentStructure.ReadValue(payload, "interlocutorCard"));
        var tasks = FirstNonEmptyList(
            SpeakingContentStructure.ReadStringList(SpeakingContentStructure.ReadValue(candidate, "tasks")),
            SpeakingContentStructure.ReadStringList(SpeakingContentStructure.ReadValue(payload, "tasks")),
            SpeakingContentStructure.ReadStringList(SpeakingContentStructure.ReadValue(payload, "roleObjectives")));
        var warmUps = SpeakingContentStructure.ReadStringList(SpeakingContentStructure.ReadValue(payload, "warmUpQuestions"));

        return new ExpertSpeakingRoleCardResponse(
            SpeakingContentStructure.ReadString(candidate, "candidateRole", "role")
                ?? SpeakingContentStructure.ReadString(payload, "candidateRole", "role")
                ?? fallback.Role,
            SpeakingContentStructure.ReadString(candidate, "setting")
                ?? SpeakingContentStructure.ReadString(payload, "setting")
                ?? fallback.Setting,
            SpeakingContentStructure.ReadString(candidate, "patientRole", "patient")
                ?? SpeakingContentStructure.ReadString(payload, "patientRole", "patient")
                ?? fallback.Patient,
            SpeakingContentStructure.ReadString(candidate, "task", "brief")
                ?? SpeakingContentStructure.ReadString(payload, "task", "brief")
                ?? fallback.Task,
            SpeakingContentStructure.ReadString(candidate, "background")
                ?? SpeakingContentStructure.ReadString(payload, "background", "caseNotes")
                ?? fallback.Background,
            tasks,
            SpeakingContentStructure.ReadString(payload, "patientEmotion") ?? fallback.PatientEmotion,
            SpeakingContentStructure.ReadString(payload, "communicationGoal", "purpose") ?? fallback.CommunicationGoal,
            SpeakingContentStructure.ReadString(payload, "clinicalTopic") ?? content.ScenarioType ?? fallback.ClinicalTopic,
            warmUps,
            SpeakingContentStructure.ReadInt(payload, "prepTimeSeconds") ?? fallback.PrepTimeSeconds,
            SpeakingContentStructure.ReadInt(payload, "roleplayTimeSeconds") ?? fallback.RoleplayTimeSeconds,
            interlocutor.Count > 0 ? interlocutor : null,
            SpeakingContentStructure.ReadString(payload, "disclaimer") ?? fallback.Disclaimer);
    }

    private static List<string> FirstNonEmptyList(params List<string>[] lists)
        => lists.FirstOrDefault(list => list.Count > 0) ?? [];

    private static List<ExpertTranscriptLineResponse> ExtractTranscriptLines(Attempt attempt)
    {
        var transcript = JsonSupport.Deserialize<List<Dictionary<string, object?>>>(attempt.TranscriptJson, []);
        return transcript
            .Select((line, index) => new ExpertTranscriptLineResponse(
                line.TryGetValue("id", out var id) ? id?.ToString() ?? $"line-{index + 1}" : $"line-{index + 1}",
                line.TryGetValue("speaker", out var speaker) ? NormalizeSpeaker(speaker?.ToString()) : "candidate",
                ToDouble(line.TryGetValue("startTime", out var startTime) ? startTime : null),
                ToDouble(line.TryGetValue("endTime", out var endTime) ? endTime : null),
                line.TryGetValue("text", out var text) ? text?.ToString() ?? string.Empty : string.Empty))
            .OrderBy(line => line.StartTime)
            .ToList();
    }

    private static List<ExpertAiFlagResponse> ExtractAiFlags(Evaluation? evaluation)
    {
        var feedbackItems = JsonSupport.Deserialize<List<Dictionary<string, object?>>>(evaluation?.FeedbackItemsJson, []);
        return feedbackItems.Select((item, index) =>
        {
            var anchor = item.TryGetValue("anchor", out var anchorValue)
                ? JsonSupport.Deserialize(JsonSupport.Serialize(anchorValue), new Dictionary<string, object?>())
                : new Dictionary<string, object?>();
            return new ExpertAiFlagResponse(
                item.TryGetValue("feedbackItemId", out var id) ? id?.ToString() ?? $"flag-{index + 1}" : $"flag-{index + 1}",
                NormalizeFlagType(item.TryGetValue("criterionCode", out var criterionCode) ? criterionCode?.ToString() : null),
                item.TryGetValue("message", out var message) ? message?.ToString() ?? "AI flagged this segment for review." : "AI flagged this segment for review.",
                ToDouble(anchor.TryGetValue("startTime", out var startTime) ? startTime : null),
                anchor.TryGetValue("endTime", out var endTime) ? ToNullableDouble(endTime) : null,
                NormalizeSeverity(item.TryGetValue("severity", out var severity) ? severity?.ToString() : null));
        }).ToList();
    }

    private static string NormalizeSpeaker(string? value)
    {
        return string.Equals(value, "interlocutor", StringComparison.OrdinalIgnoreCase) ? "interlocutor" : "candidate";
    }

    private static string NormalizeFlagType(string? value)
    {
        return NormalizeCriterionKey(value, SpeakingCriteria) ?? "review_flag";
    }

    private static string NormalizeSeverity(string? value)
    {
        return value?.ToLowerInvariant() switch
        {
            "error" => "error",
            "warning" => "warning",
            _ => "info"
        };
    }

    private static string ToLabel(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Criterion";
        }

        var withSpaces = System.Text.RegularExpressions.Regex.Replace(
            value.Replace("_", " ", StringComparison.Ordinal),
            "([a-z])([A-Z])",
            "$1 $2");

        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(withSpaces.ToLowerInvariant());
    }

    private static double ToDouble(object? value)
    {
        return value switch
        {
            null => 0,
            JsonElement element when element.ValueKind == System.Text.Json.JsonValueKind.Number => element.GetDouble(),
            JsonElement element when element.ValueKind == System.Text.Json.JsonValueKind.String && double.TryParse(element.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed,
            double number => number,
            float number => number,
            decimal number => (double)number,
            int number => number,
            long number => number,
            string text when double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => 0
        };
    }

    private static double? ToNullableDouble(object? value)
    {
        return value is null ? null : ToDouble(value);
    }

    private static ExpertDraftResponse? BuildDraftResponse(ExpertReviewDraft? draft)
    {
        if (draft is null)
        {
            return null;
        }

        return new ExpertDraftResponse(
            draft.Version,
            draft.State,
            JsonSupport.Deserialize(draft.RubricEntriesJson, new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)),
            JsonSupport.Deserialize(draft.CriterionCommentsJson, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)),
            draft.FinalCommentDraft,
            JsonSupport.Deserialize(draft.AnchoredCommentsJson, new List<ExpertAnchoredCommentResponse>()),
            JsonSupport.Deserialize(draft.TimestampCommentsJson, new List<ExpertTimestampCommentResponse>()),
            JsonSupport.Deserialize(draft.ScratchpadJson, string.Empty),
            JsonSupport.Deserialize(draft.ChecklistItemsJson, new List<ExpertChecklistItemResponse>()),
            draft.DraftSavedAt);
    }

    private static string ResolveTodayKey(string timezone)
    {
        try
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById(timezone);
            return TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone).DayOfWeek.ToString().ToLowerInvariant();
        }
        catch (TimeZoneNotFoundException)
        {
            return DateTimeOffset.UtcNow.DayOfWeek.ToString().ToLowerInvariant();
        }
        catch (InvalidTimeZoneException)
        {
            return DateTimeOffset.UtcNow.DayOfWeek.ToString().ToLowerInvariant();
        }
    }

    private static string? BuildReviewRoute(string reviewRequestId)
    {
        if (string.IsNullOrWhiteSpace(reviewRequestId))
        {
            return null;
        }

        if (reviewRequestId.StartsWith("cal-", StringComparison.OrdinalIgnoreCase))
        {
            return $"/expert/calibration/{Uri.EscapeDataString(reviewRequestId)}";
        }

        if (reviewRequestId.StartsWith("review-", StringComparison.OrdinalIgnoreCase))
        {
            return "/expert/queue";
        }

        return null;
    }

    private static string MapReviewRequestState(ReviewRequest reviewRequest, ExpertReviewAssignment? assignment, string reviewerId, DateTimeOffset now)
        => MapQueueStatus(reviewRequest, assignment, reviewerId, now);

    private static Dictionary<string, int> NormalizeScores(Dictionary<string, int> scores, string subtestCode)
    {
        var isWriting = string.Equals(subtestCode, "writing", StringComparison.OrdinalIgnoreCase);
        var criteria = isWriting ? WritingCriteria : SpeakingCriteria;
        var normalized = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var (key, value) in scores)
        {
            var normalizedKey = NormalizeCriterionKey(key, criteria);
            if (normalizedKey is null)
            {
                throw ApiException.Validation(
                    "invalid_rubric_criterion",
                    "One or more rubric criteria are invalid.",
                    [new ApiFieldError("scores", "invalid_criterion", $"'{key}' is not a valid criterion for this review.")]);
            }

            var maxScore = MaxScoreForCriterion(subtestCode, normalizedKey);
            if (value < 0 || value > maxScore)
            {
                throw ApiException.Validation(
                    "invalid_rubric_score",
                    "One or more rubric scores are outside the allowed range.",
                    [new ApiFieldError($"scores.{normalizedKey}", "out_of_range", $"Scores for {normalizedKey} must be between 0 and {maxScore}.")]);
            }

            normalized[normalizedKey] = value;
        }

        return normalized;
    }

    /// <summary>
    /// Per-criterion max score. Writing: Purpose=3, others=7 (rulebook R16.1/R16.2).
    /// Speaking: linguistic=6, clinical-communication cluster=3 (OET CBLA official).
    /// </summary>
    private static int MaxScoreForCriterion(string subtestCode, string criterionCode)
    {
        if (string.Equals(subtestCode, "writing", StringComparison.OrdinalIgnoreCase))
        {
            return string.Equals(criterionCode, "purpose", StringComparison.OrdinalIgnoreCase) ? 3 : 7;
        }
        return SpeakingClinicalCriteria.Contains(criterionCode, StringComparer.OrdinalIgnoreCase) ? 3 : 6;
    }

    private static Dictionary<string, string> NormalizeCriterionComments(Dictionary<string, string> criterionComments, string subtestCode)
    {
        var criteria = string.Equals(subtestCode, "writing", StringComparison.OrdinalIgnoreCase) ? WritingCriteria : SpeakingCriteria;
        var normalized = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (key, value) in criterionComments)
        {
            var normalizedKey = NormalizeCriterionKey(key, criteria);
            if (normalizedKey is null)
            {
                throw ApiException.Validation(
                    "invalid_rubric_comment_criterion",
                    "One or more rubric comment criteria are invalid.",
                    [new ApiFieldError("criterionComments", "invalid_criterion", $"'{key}' is not a valid criterion for this review.")]);
            }

            var trimmed = value?.Trim() ?? string.Empty;
            if (trimmed.Length > MaxCriterionCommentLength)
            {
                throw ApiException.Validation(
                    "criterion_comment_too_long",
                    "One or more criterion comments are too long.",
                    [new ApiFieldError($"criterionComments.{normalizedKey}", "too_long", $"Criterion comments cannot exceed {MaxCriterionCommentLength} characters.")]);
            }

            if (!string.IsNullOrWhiteSpace(trimmed))
            {
                normalized[normalizedKey] = trimmed;
            }
        }

        return normalized;
    }

    private static string NormalizeFinalComment(string? finalComment, bool required)
    {
        var trimmed = finalComment?.Trim() ?? string.Empty;
        if (required && string.IsNullOrWhiteSpace(trimmed))
        {
            throw ApiException.Validation(
                "final_comment_required",
                "Provide a final overall comment before submitting.",
                [new ApiFieldError("finalComment", "required", "Add a final overall comment before submitting this review.")]);
        }

        if (trimmed.Length > MaxFinalCommentLength)
        {
            throw ApiException.Validation(
                "final_comment_too_long",
                "The final overall comment is too long.",
                [new ApiFieldError("finalComment", "too_long", $"Final comments cannot exceed {MaxFinalCommentLength} characters.")]);
        }

        return trimmed;
    }

    private static List<ExpertAnchoredCommentResponse> NormalizeAnchoredComments(List<ExpertAnchoredCommentDto>? comments)
    {
        if (comments is null)
        {
            return [];
        }

        return comments.Select(comment =>
        {
            var text = (comment.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                throw ApiException.Validation(
                    "anchored_comment_required",
                    "Anchored comments cannot be empty.",
                    [new ApiFieldError("anchoredComments", "required", "Provide comment text for each anchored comment.")]);
            }

            if (text.Length > MaxCommentTextLength)
            {
                throw ApiException.Validation(
                    "anchored_comment_too_long",
                    "Anchored comments are too long.",
                    [new ApiFieldError("anchoredComments", "too_long", $"Anchored comments cannot exceed {MaxCommentTextLength} characters.")]);
            }

            if (comment.StartOffset < 0 || comment.EndOffset <= comment.StartOffset)
            {
                throw ApiException.Validation(
                    "anchored_comment_offset_invalid",
                    "Anchored comment offsets are invalid.",
                    [new ApiFieldError("anchoredComments", "invalid_offset", "Anchored comment ranges must end after they begin.")]);
            }

            return new ExpertAnchoredCommentResponse(
                string.IsNullOrWhiteSpace(comment.Id) ? $"ac-{Guid.NewGuid():N}" : comment.Id,
                comment.Criterion,
                text,
                comment.StartOffset,
                comment.EndOffset,
                ParseCreatedAt(comment.CreatedAt));
        }).ToList();
    }

    private static List<ExpertTimestampCommentResponse> NormalizeTimestampComments(List<ExpertTimestampCommentDto>? comments)
    {
        if (comments is null)
        {
            return [];
        }

        return comments.Select(comment =>
        {
            var text = (comment.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                throw ApiException.Validation(
                    "timestamp_comment_required",
                    "Timestamp comments cannot be empty.",
                    [new ApiFieldError("timestampComments", "required", "Provide comment text for each timestamp comment.")]);
            }

            if (text.Length > MaxCommentTextLength)
            {
                throw ApiException.Validation(
                    "timestamp_comment_too_long",
                    "Timestamp comments are too long.",
                    [new ApiFieldError("timestampComments", "too_long", $"Timestamp comments cannot exceed {MaxCommentTextLength} characters.")]);
            }

            if (comment.TimestampStart < 0 || (comment.TimestampEnd is not null && comment.TimestampEnd <= comment.TimestampStart))
            {
                throw ApiException.Validation(
                    "timestamp_comment_range_invalid",
                    "Timestamp comment ranges are invalid.",
                    [new ApiFieldError("timestampComments", "invalid_range", "Timestamp comment ranges must end after they begin.")]);
            }

            return new ExpertTimestampCommentResponse(
                string.IsNullOrWhiteSpace(comment.Id) ? $"tc-{Guid.NewGuid():N}" : comment.Id,
                comment.Criterion,
                text,
                comment.TimestampStart,
                comment.TimestampEnd,
                ParseCreatedAt(comment.CreatedAt));
        }).ToList();
    }

    private static DateTimeOffset ParseCreatedAt(string? value)
    {
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : DateTimeOffset.UtcNow;
    }

    private static void ValidateDraftRequest(string subtestCode, ExpertDraftSaveRequest request)
    {
        _ = NormalizeScores(request.Scores, subtestCode);
        _ = NormalizeCriterionComments(request.CriterionComments, subtestCode);
        _ = NormalizeFinalComment(request.FinalComment, required: false);
        _ = NormalizeAnchoredComments(request.AnchoredComments);
        _ = NormalizeTimestampComments(request.TimestampComments);
        _ = NormalizeScratchpad(request.Scratchpad);
        _ = NormalizeChecklistItems(request.ChecklistItems);
    }

    private static void ValidateSubmitRequest(string subtestCode, ExpertReviewSubmitRequest request)
    {
        var normalizedScores = NormalizeScores(request.Scores, subtestCode);
        _ = NormalizeCriterionComments(request.CriterionComments, subtestCode);
        _ = NormalizeFinalComment(request.FinalComment, required: true);

        var requiredCriteria = string.Equals(subtestCode, "writing", StringComparison.OrdinalIgnoreCase) ? WritingCriteria : SpeakingCriteria;
        var missing = requiredCriteria.Where(criterion => !normalizedScores.ContainsKey(criterion)).ToArray();
        if (missing.Length > 0)
        {
            throw ApiException.Validation(
                "rubric_incomplete",
                "Complete every required rubric score before submitting.",
                missing.Select(criterion => new ApiFieldError($"scores.{criterion}", "required", $"A score for {criterion} is required before submission.")));
        }
    }

    private static string NormalizeReworkReason(string? reason)
    {
        var trimmed = reason?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            throw ApiException.Validation(
                "rework_reason_required",
                "Provide a reason before requesting rework.",
                [new ApiFieldError("reason", "required", "Add a reason for the rework request.")]);
        }

        if (trimmed.Length > MaxReworkReasonLength)
        {
            throw ApiException.Validation(
                "rework_reason_too_long",
                "The rework reason is too long.",
                [new ApiFieldError("reason", "too_long", $"Rework reasons cannot exceed {MaxReworkReasonLength} characters.")]);
        }

        return trimmed;
    }

    private static string NormalizeScratchpad(string? scratchpad)
    {
        var normalized = scratchpad?.Trim() ?? string.Empty;
        if (normalized.Length > MaxScratchpadLength)
        {
            throw ApiException.Validation(
                "scratchpad_too_long",
                "Scratchpad notes are too long.",
                [new ApiFieldError("scratchpad", "too_long", $"Scratchpad notes cannot exceed {MaxScratchpadLength} characters.")]);
        }

        return normalized;
    }

    private static List<ExpertChecklistItemResponse> NormalizeChecklistItems(List<ExpertChecklistItemDto>? checklistItems)
    {
        if (checklistItems is null)
        {
            return [];
        }

        if (checklistItems.Count > MaxChecklistItemCount)
        {
            throw ApiException.Validation(
                "checklist_too_long",
                "Too many checklist items were submitted.",
                [new ApiFieldError("checklistItems", "too_many", $"Checklist items cannot exceed {MaxChecklistItemCount} entries.")]);
        }

        return checklistItems.Select(item =>
        {
            var id = item.Id?.Trim() ?? string.Empty;
            var label = item.Label?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(id))
            {
                throw ApiException.Validation(
                    "checklist_item_id_required",
                    "Checklist items must include an id.",
                    [new ApiFieldError("checklistItems", "required", "Every checklist item must include an id.")]);
            }

            if (string.IsNullOrWhiteSpace(label))
            {
                throw ApiException.Validation(
                    "checklist_item_label_required",
                    "Checklist items must include a label.",
                    [new ApiFieldError("checklistItems", "required", "Every checklist item must include a label.")]);
            }

            if (label.Length > MaxChecklistItemLabelLength)
            {
                throw ApiException.Validation(
                    "checklist_item_label_too_long",
                    "Checklist item labels are too long.",
                    [new ApiFieldError("checklistItems", "too_long", $"Checklist labels cannot exceed {MaxChecklistItemLabelLength} characters.")]);
            }

            return new ExpertChecklistItemResponse(id, label, item.Checked);
        }).ToList();
    }

    private static string? NormalizeCriterionKey(string? rawKey, IReadOnlyCollection<string> allowedCriteria)
    {
        if (string.IsNullOrWhiteSpace(rawKey))
        {
            return null;
        }

        var normalized = rawKey.Trim().Replace("_", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal).ToLowerInvariant();

        return allowedCriteria.FirstOrDefault(candidate =>
        {
            var candidateKey = candidate.Replace("_", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
            return candidateKey == normalized
                // Backward-compat aliases for legacy writing criterion codes stored before canonical rename.
                || (candidate == "conciseness_clarity" && (normalized == "conciseness" || normalized == "clarity"))
                || (candidate == "genre_style" && (normalized == "genre" || normalized == "style"))
                || (candidate == "organisation_layout" && (normalized == "organisation" || normalized == "organization" || normalized == "layout"))
                // Speaking aliases: collapse the many grammar spellings to canonical "grammar".
                || (candidate == "grammar" && (normalized == "grammarexpression" || normalized == "resources" || normalized == "resourcesofgrammarandexpression" || normalized == "resourcesofgrammarexpression"))
                // Speaking clinical criteria: accept snake_case / spaced / partial forms.
                || (candidate == "relationshipBuilding" && (normalized == "relationshipbuilding" || normalized == "relationship"))
                || (candidate == "patientPerspective" && (normalized == "patientperspective" || normalized == "understandingpatientperspective" || normalized == "understandingandincorporatingpatientsperspective" || normalized == "patientperspectives"))
                || (candidate == "providingStructure" && (normalized == "providingstructure" || normalized == "structure"))
                || (candidate == "informationGathering" && (normalized == "informationgathering" || normalized == "gathering"))
                || (candidate == "informationGiving" && (normalized == "informationgiving" || normalized == "giving"));
        });
    }

    private static int? ParseScoreRangeAverage(string? scoreRange)
    {
        if (string.IsNullOrWhiteSpace(scoreRange))
        {
            return null;
        }

        var match = System.Text.RegularExpressions.Regex.Match(scoreRange, "(\\d+)(?:-(\\d+))?");
        if (!match.Success)
        {
            return null;
        }

        var first = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        var second = match.Groups[2].Success ? int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture) : first;
        return (int)Math.Round((first + second) / 2.0, MidpointRounding.AwayFromZero);
    }

    private async Task LogExpertAuditAsync(string actorId, string actorName, string action, string? resourceId, string? details, CancellationToken ct)
    {
        db.AuditEvents.Add(new AuditEvent
        {
            Id = $"aud-{Guid.NewGuid():N}",
            OccurredAt = DateTimeOffset.UtcNow,
            ActorId = actorId,
            ActorName = actorName,
            Action = action,
            ResourceType = "ExpertReview",
            ResourceId = resourceId,
            Details = details
        });

        await Task.CompletedTask;
    }

    private async Task RecordExpertEventAsync(string reviewerId, string eventName, object payload, CancellationToken ct)
    {
        db.AnalyticsEvents.Add(new AnalyticsEventRecord
        {
            Id = $"evt-{Guid.NewGuid():N}",
            UserId = reviewerId,
            EventName = eventName,
            PayloadJson = JsonSupport.Serialize(payload),
            OccurredAt = DateTimeOffset.UtcNow
        });

        await Task.CompletedTask;
    }

    private sealed record ReadContext(
        ReviewRequest ReviewRequest,
        Attempt Attempt,
        LearnerUser? Learner,
        ExpertReviewAssignment? ActiveAssignment,
        ContentItem? Content,
        Evaluation? Evaluation,
        ExpertDraftResponse? Draft,
        IReadOnlyDictionary<string, ExpertUser> AssignedReviewers);

    private sealed record TrackedWriteContext(
        ReviewRequest ReviewRequest,
        Attempt Attempt,
        ExpertReviewAssignment ActiveAssignment,
        ExpertUser Expert);
}
