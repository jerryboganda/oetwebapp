using System.Globalization;
using System.Linq.Expressions;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Content;

namespace OetLearner.Api.Services;

public partial class ExpertService
{
    public async Task<ExpertWritingReviewBundleResponse> GetWritingReviewBundleAsync(string reviewRequestId, string reviewerId, CancellationToken ct)
    {
        var context = await LoadReadContextAsync(reviewRequestId, reviewerId, ct, requireActiveAssignment: true);
        if (!string.Equals(context.ReviewRequest.SubtestCode, "writing", StringComparison.OrdinalIgnoreCase))
        {
            throw ApiException.Validation("review_type_mismatch", "This review is not a writing review.");
        }

        var writingScores = NormalizeAiSuggestedScores(context.Evaluation, isWriting: true);
        var paperAssets = await LoadWritingPaperAssetResponsesAsync(context.Attempt.Id, ct);
        var voiceNotes = await LoadReviewVoiceNoteResponsesAsync(context.ReviewRequest.Id, ct);
        return new ExpertWritingReviewBundleResponse(
            context.ReviewRequest.Id,
            context.Attempt.UserId,
            context.Learner?.DisplayName ?? "Unknown learner",
            context.Learner?.ActiveProfessionId ?? "nursing",
            "writing",
            "writing",
            ToAiConfidence(context.Evaluation?.ConfidenceBand),
            MapPriority(context.ReviewRequest.TurnaroundOption),
            CalculateSlaDueAt(context.ReviewRequest),
            MapSlaState(context.ReviewRequest, DateTimeOffset.UtcNow),
            IsOverdue(context.ReviewRequest, DateTimeOffset.UtcNow),
            context.ActiveAssignment?.AssignedReviewerId,
            ResolveReviewerName(context.ActiveAssignment?.AssignedReviewerId, context.AssignedReviewers),
            MapAssignmentState(context.ActiveAssignment),
            MapQueueStatus(context.ReviewRequest, context.ActiveAssignment, reviewerId, DateTimeOffset.UtcNow),
            context.Attempt.ContentId,
            context.Attempt.Id,
            context.ReviewRequest.CreatedAt,
            context.Attempt.DraftContent,
            context.Content?.CaseNotes ?? "Case notes are not available for this writing review.",
            BuildWritingAiDraftFeedback(context.Evaluation),
            writingScores,
            ExtractModelAnswer(context.Content),
            context.Draft,
            BuildPermissions(context.ReviewRequest, context.ActiveAssignment, reviewerId),
            new Dictionary<string, ExpertArtifactStateResponse>(StringComparer.OrdinalIgnoreCase)
            {
                ["aiDraftFeedback"] = BuildEvaluationArtifactState(context.Evaluation, "AI draft feedback is still being prepared."),
                ["paperAssets"] = BuildPaperArtifactState(paperAssets),
                ["voiceNotes"] = BuildVoiceNoteArtifactState(voiceNotes)
            },
            paperAssets,
            voiceNotes);
    }

    public async Task<object> GetWritingReviewVoiceNotesAsync(string reviewRequestId, string reviewerId, CancellationToken ct)
    {
        var context = await LoadReadContextAsync(reviewRequestId, reviewerId, ct, requireActiveAssignment: true);
        if (!string.Equals(context.ReviewRequest.SubtestCode, "writing", StringComparison.OrdinalIgnoreCase))
        {
            throw ApiException.Validation("review_type_mismatch", "This review is not a writing review.");
        }

        return new { reviewRequestId, items = await LoadReviewVoiceNoteResponsesAsync(reviewRequestId, ct) };
    }

    public async Task<object> AddWritingReviewVoiceNoteAsync(string reviewRequestId, string reviewerId, ExpertReviewVoiceNoteCreateRequest request, CancellationToken ct)
    {
        var context = await LoadWriteContextAsync(reviewRequestId, reviewerId, ct);
        if (!string.Equals(context.ReviewRequest.SubtestCode, "writing", StringComparison.OrdinalIgnoreCase))
        {
            throw ApiException.Validation("review_type_mismatch", "This review is not a writing review.");
        }

        var media = await db.MediaAssets.FirstOrDefaultAsync(asset => asset.Id == request.MediaAssetId, ct)
            ?? throw ApiException.NotFound("voice_note_media_not_found", "Upload the voice note before attaching it to the review.");
        if (!string.Equals(media.UploadedBy, reviewerId, StringComparison.OrdinalIgnoreCase))
        {
            throw ApiException.Forbidden("voice_note_forbidden", "You can only attach voice notes uploaded by your expert account.");
        }
        if (!media.MimeType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase))
        {
            throw ApiException.Validation(
                "invalid_voice_note_type",
                "Voice notes must be audio files.",
                [new ApiFieldError("mediaAssetId", "invalid_type", "Upload mp3, m4a, wav, ogg, or webm audio.")]);
        }
        if (media.Status != MediaAssetStatus.Ready
            || string.IsNullOrWhiteSpace(media.StoragePath)
            || !await fileStorage.ExistsAsync(media.StoragePath, ct))
        {
            throw ApiException.Validation(
                "voice_note_media_not_ready",
                "Voice note upload must finish processing before it can be attached to the review.",
                [new ApiFieldError("mediaAssetId", "not_ready", "Wait for the audio upload to finish, then attach the voice note again.")]);
        }

        if (request.DurationSeconds is < 0 or > MaxVoiceNoteDurationSeconds)
        {
            throw ApiException.Validation(
                "invalid_voice_note_duration",
                "Voice note duration is outside the allowed range.",
                [new ApiFieldError("durationSeconds", "out_of_range", $"Voice notes must be between 0 and {MaxVoiceNoteDurationSeconds} seconds.")]);
        }
        var transcriptText = request.TranscriptText?.Trim() ?? string.Empty;
        if (transcriptText.Length > MaxVoiceNoteTranscriptLength)
        {
            throw ApiException.Validation(
                "voice_note_transcript_too_long",
                "Voice note transcript is too long.",
                [new ApiFieldError("transcriptText", "too_long", $"Voice note transcripts cannot exceed {MaxVoiceNoteTranscriptLength} characters.")]);
        }
        var writtenNotes = request.WrittenNotes?.Trim() ?? string.Empty;
        if (writtenNotes.Length > MaxVoiceNoteWrittenNotesLength)
        {
            throw ApiException.Validation(
                "voice_note_notes_too_long",
                "Voice note written notes are too long.",
                [new ApiFieldError("writtenNotes", "too_long", $"Voice note written notes cannot exceed {MaxVoiceNoteWrittenNotesLength} characters.")]);
        }
        var normalizedRubricScores = NormalizeScores(request.RubricScores ?? new Dictionary<string, int>(), "writing");

        var note = new ReviewVoiceNote
        {
            Id = $"rvn-{Guid.NewGuid():N}",
            ReviewRequestId = reviewRequestId,
            UploadedByReviewerId = reviewerId,
            MediaAssetId = media.Id,
            DurationSeconds = request.DurationSeconds,
            TranscriptText = transcriptText,
            WrittenNotes = writtenNotes,
            RubricJson = JsonSupport.Serialize(normalizedRubricScores),
            Status = "ready",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        db.ReviewVoiceNotes.Add(note);
        await LogExpertAuditAsync(reviewerId, context.Expert.DisplayName, "Attached Writing Voice Note", reviewRequestId, $"Voice note {note.Id} attached.", ct);
        await RecordExpertEventAsync(reviewerId, "expert_writing_voice_note_attached", new { reviewRequestId, voiceNoteId = note.Id }, ct);
        await db.SaveChangesAsync(ct);

        return new { reviewRequestId, item = (await LoadReviewVoiceNoteResponsesAsync(reviewRequestId, ct)).First(x => x.Id == note.Id) };
    }

    public async Task<ExpertSpeakingReviewBundleResponse> GetSpeakingReviewBundleAsync(string reviewRequestId, string reviewerId, CancellationToken ct)
    {
        var context = await LoadReadContextAsync(reviewRequestId, reviewerId, ct, requireActiveAssignment: true);
        if (!string.Equals(context.ReviewRequest.SubtestCode, "speaking", StringComparison.OrdinalIgnoreCase))
        {
            throw ApiException.Validation("review_type_mismatch", "This review is not a speaking review.");
        }

        var transcriptLines = ExtractTranscriptLines(context.Attempt);
        return new ExpertSpeakingReviewBundleResponse(
            context.ReviewRequest.Id,
            context.Attempt.UserId,
            context.Learner?.DisplayName ?? "Unknown learner",
            context.Learner?.ActiveProfessionId ?? "nursing",
            "speaking",
            "speaking",
            ToAiConfidence(context.Evaluation?.ConfidenceBand),
            MapPriority(context.ReviewRequest.TurnaroundOption),
            CalculateSlaDueAt(context.ReviewRequest),
            MapSlaState(context.ReviewRequest, DateTimeOffset.UtcNow),
            IsOverdue(context.ReviewRequest, DateTimeOffset.UtcNow),
            context.ActiveAssignment?.AssignedReviewerId,
            ResolveReviewerName(context.ActiveAssignment?.AssignedReviewerId, context.AssignedReviewers),
            MapAssignmentState(context.ActiveAssignment),
            MapQueueStatus(context.ReviewRequest, context.ActiveAssignment, reviewerId, DateTimeOffset.UtcNow),
            context.Attempt.ContentId,
            context.Attempt.Id,
            context.ReviewRequest.CreatedAt,
            platformLinks.BuildApiUrl($"/v1/expert/reviews/{Uri.EscapeDataString(reviewRequestId)}/speaking/audio"),
            transcriptLines,
            ExtractRoleCard(context.Content),
            ExtractAiFlags(context.Evaluation),
            NormalizeAiSuggestedScores(context.Evaluation, isWriting: false),
            context.Draft,
            BuildPermissions(context.ReviewRequest, context.ActiveAssignment, reviewerId),
            new Dictionary<string, ExpertArtifactStateResponse>(StringComparer.OrdinalIgnoreCase)
            {
                ["audio"] = BuildAudioArtifactState(context.Attempt),
                ["transcript"] = BuildTranscriptArtifactState(transcriptLines),
                ["aiFlags"] = BuildEvaluationArtifactState(context.Evaluation, "AI analysis is still being prepared.")
            });
    }

    public async Task<StoredMediaFile> GetSpeakingReviewAudioAsync(string reviewRequestId, string reviewerId, CancellationToken ct)
    {
        var context = await LoadReadContextAsync(reviewRequestId, reviewerId, ct, requireActiveAssignment: true);
        if (!string.Equals(context.ReviewRequest.SubtestCode, "speaking", StringComparison.OrdinalIgnoreCase))
        {
            throw ApiException.Validation("review_type_mismatch", "This review does not include a speaking audio recording.");
        }

        if (string.IsNullOrWhiteSpace(context.Attempt.AudioObjectKey))
        {
            throw ApiException.NotFound("audio_not_found", "No uploaded audio is available for this speaking attempt.");
        }

        var metadata = JsonSupport.Deserialize(context.Attempt.AudioMetadataJson, new Dictionary<string, object?>());
        var contentType = metadata.TryGetValue("contentType", out var value) ? value?.ToString() : null;

        // Wave 7 of docs/SPEAKING-MODULE-PLAN.md - any non-owner access
        // to a speaking recording is a privacy-sensitive event and must
        // be auditable. We write the audit row before opening the file
        // stream so the side-effect is durable even if the client
        // disconnects mid-stream.
        var actorName = context.AssignedReviewers.TryGetValue(reviewerId, out var assignedExpert)
            ? assignedExpert.DisplayName
            : (await db.ExpertUsers.AsNoTracking().FirstOrDefaultAsync(e => e.Id == reviewerId, ct))?.DisplayName
              ?? reviewerId;
        await LogExpertAuditAsync(
            reviewerId,
            actorName,
            "speaking_recording_accessed",
            reviewRequestId,
            $"Tutor streamed learner speaking audio for attempt {context.Attempt.Id}.",
            ct);
        await db.SaveChangesAsync(ct);

        return await fileStorage.OpenStoredMediaFileAsync(context.Attempt.AudioObjectKey, contentType, ct);
    }

    public async Task<ExpertDraftResponse> SaveDraftAsync(string reviewRequestId, string reviewerId, ExpertDraftSaveRequest request, CancellationToken ct)
    {
        var context = await LoadWriteContextAsync(reviewRequestId, reviewerId, ct);
        ValidateDraftRequest(context.ReviewRequest.SubtestCode, request);

        var draft = await db.ExpertReviewDrafts
            .FirstOrDefaultAsync(existingDraft => existingDraft.ReviewRequestId == reviewRequestId && existingDraft.ReviewerId == reviewerId, ct);

        if (draft is null)
        {
            draft = new ExpertReviewDraft
            {
                Id = $"erd-{Guid.NewGuid():N}",
                ReviewRequestId = reviewRequestId,
                ReviewerId = reviewerId,
                Version = 0
            };
            db.ExpertReviewDrafts.Add(draft);
        }

        if (request.Version is not null && request.Version != draft.Version)
        {
            throw ApiException.Conflict(
                "draft_version_conflict",
                "This draft has changed since you opened it.",
                [new ApiFieldError("version", "conflict", "Reload the review to merge the latest saved draft before continuing.")]);
        }

        var normalizedScores = NormalizeScores(request.Scores, context.ReviewRequest.SubtestCode);
        var normalizedCriterionComments = NormalizeCriterionComments(request.CriterionComments, context.ReviewRequest.SubtestCode);
        var finalComment = NormalizeFinalComment(request.FinalComment, required: false);
        var anchoredComments = NormalizeAnchoredComments(request.AnchoredComments);
        var timestampComments = NormalizeTimestampComments(request.TimestampComments);
        var scratchpad = NormalizeScratchpad(request.Scratchpad);
        var checklistItems = NormalizeChecklistItems(request.ChecklistItems);

        draft.RubricEntriesJson = JsonSupport.Serialize(normalizedScores);
        draft.CriterionCommentsJson = JsonSupport.Serialize(normalizedCriterionComments);
        draft.FinalCommentDraft = finalComment;
        draft.AnchoredCommentsJson = JsonSupport.Serialize(anchoredComments);
        draft.TimestampCommentsJson = JsonSupport.Serialize(timestampComments);
        draft.ScratchpadJson = JsonSupport.Serialize(scratchpad);
        draft.ChecklistItemsJson = JsonSupport.Serialize(checklistItems);
        draft.Version += 1;
        draft.State = "saved";
        draft.DraftSavedAt = DateTimeOffset.UtcNow;

        context.ReviewRequest.State = ReviewRequestState.InReview;
        context.ActiveAssignment.ClaimState = ExpertAssignmentState.Claimed;

        await LogExpertAuditAsync(reviewerId, context.Expert.DisplayName, "Saved Review Draft", reviewRequestId, "Tutor review draft saved.", ct);
        await RecordExpertEventAsync(reviewerId, "expert_review_draft_saved", new { reviewRequestId, version = draft.Version }, ct);
        await db.SaveChangesAsync(ct);

        return BuildDraftResponse(draft)!;
    }

    public async Task<object> SubmitWritingReviewAsync(string reviewRequestId, string reviewerId, ExpertReviewSubmitRequest request, CancellationToken ct)
    {
        var context = await LoadWriteContextAsync(reviewRequestId, reviewerId, ct);
        if (!string.Equals(context.ReviewRequest.SubtestCode, "writing", StringComparison.OrdinalIgnoreCase))
        {
            throw ApiException.Validation("review_type_mismatch", "This review is not a writing review.");
        }

        var voiceNoteMedia = await db.ReviewVoiceNotes
            .Where(note => note.ReviewRequestId == reviewRequestId && note.Status == "ready")
            .Join(db.MediaAssets,
                note => note.MediaAssetId,
                media => media.Id,
                (note, media) => new { media.Status, media.StoragePath, media.MimeType })
            .ToListAsync(ct);
        var hasVoiceNote = false;
        foreach (var item in voiceNoteMedia)
        {
            if (item.Status == MediaAssetStatus.Ready
                && item.MimeType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(item.StoragePath)
                && await fileStorage.ExistsAsync(item.StoragePath, ct))
            {
                hasVoiceNote = true;
                break;
            }
        }
        if (!hasVoiceNote)
        {
            throw ApiException.Validation(
                "voice_note_required",
                "Attach a voice note before submitting this writing review.",
                [new ApiFieldError("voiceNotes", "required", "Writing reviews completed by Dr. Ahmed require at least one voice note.")]);
        }

        await SubmitReviewAsync(context, reviewerId, request, ct, "Submitted Writing Review", "expert_writing_review_submitted");
        logger.LogInformation("Expert {ReviewerId} submitted writing review for {ReviewRequestId}", reviewerId, reviewRequestId);
        return new { success = true, reviewRequestId };
    }

    public async Task<object> SubmitSpeakingReviewAsync(string reviewRequestId, string reviewerId, ExpertReviewSubmitRequest request, CancellationToken ct)
    {
        var context = await LoadWriteContextAsync(reviewRequestId, reviewerId, ct);
        if (!string.Equals(context.ReviewRequest.SubtestCode, "speaking", StringComparison.OrdinalIgnoreCase))
        {
            throw ApiException.Validation("review_type_mismatch", "This review is not a speaking review.");
        }

        await SubmitReviewAsync(context, reviewerId, request, ct, "Submitted Speaking Review", "expert_speaking_review_submitted");

        // ── L8: Bridge speaking review → pronunciation assessment ──
        try
        {
            var criterionScores = new Dictionary<string, object?>();
            if (request.Scores is not null)
            {
                foreach (var score in request.Scores)
                    criterionScores[score.Key] = (double)score.Value;
            }
            await pronunciationService.CreateFromSpeakingReviewAsync(
                context.Attempt.UserId, context.Attempt.Id, criterionScores, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Non-critical: failed to create pronunciation assessment from speaking review {ReviewRequestId}", reviewRequestId);
        }

        logger.LogInformation("Expert {ReviewerId} submitted speaking review for {ReviewRequestId}", reviewerId, reviewRequestId);
        return new { success = true, reviewRequestId };
    }

    public async Task<object> RequestReworkAsync(string reviewRequestId, string reviewerId, ExpertReworkRequest request, CancellationToken ct)
    {
        var context = await LoadWriteContextAsync(reviewRequestId, reviewerId, ct);
        var reason = NormalizeReworkReason(request.Reason);

        context.ReviewRequest.State = ReviewRequestState.Queued;
        context.ReviewRequest.CompletedAt = null;
        context.ActiveAssignment.ClaimState = ExpertAssignmentState.Released;
        context.ActiveAssignment.ReleasedAt = DateTimeOffset.UtcNow;
        context.ActiveAssignment.ReasonCode = reason;

        await LogExpertAuditAsync(reviewerId, context.Expert.DisplayName, "Requested Review Rework", reviewRequestId, reason, ct);
        await RecordExpertEventAsync(reviewerId, "expert_review_rework_requested", new { reviewRequestId, reason }, ct);
        await db.SaveChangesAsync(ct);
        await notifications.CreateForLearnerAsync(
            NotificationEventKey.LearnerReviewReworkRequested,
            context.Attempt.UserId,
            "review_request",
            reviewRequestId,
            (context.ActiveAssignment.ReleasedAt ?? DateTimeOffset.UtcNow).UtcDateTime.Ticks.ToString(),
            new Dictionary<string, object?>
            {
                ["attemptId"] = context.Attempt.Id,
                ["reviewRequestId"] = reviewRequestId,
                ["subtest"] = context.ReviewRequest.SubtestCode,
                ["message"] = $"Your reviewer requested follow-up work before finalising the {context.ReviewRequest.SubtestCode} review: {reason}"
            },
            ct);
        await notifications.CreateForAdminsAsync(
            NotificationEventKey.AdminReviewOpsAction,
            "review_request",
            reviewRequestId,
            (context.ActiveAssignment.ReleasedAt ?? DateTimeOffset.UtcNow).UtcDateTime.Ticks.ToString(),
            new Dictionary<string, object?>
            {
                ["reviewRequestId"] = reviewRequestId,
                ["message"] = $"Expert {context.Expert.DisplayName} requested rework on review {reviewRequestId}: {reason}"
            },
            ct);

        return new { success = true, reviewRequestId };
    }

    public async Task<ExpertReviewHistoryResponse> GetReviewHistoryAsync(string reviewRequestId, string reviewerId, CancellationToken ct)
    {
        await EnsureExpertAsync(reviewerId, ct);

        var reviewRequest = await db.ReviewRequests.AsNoTracking()
            .FirstOrDefaultAsync(rr => rr.Id == reviewRequestId, ct)
            ?? throw ApiException.NotFound("review_request_not_found", "The requested review does not exist.");

        List<ExpertReviewAssignment> assignments;
        var assignmentsQuery = db.ExpertReviewAssignments.AsNoTracking()
            .Where(a => a.ReviewRequestId == reviewRequestId);

        if (!db.Database.IsSqlite())
        {
            assignments = await assignmentsQuery
                .OrderBy(a => a.AssignedAt ?? DateTimeOffset.MinValue)
                .ToListAsync(ct);
        }
        else
        {
            assignments = (await assignmentsQuery.ToListAsync(ct))
                .OrderBy(a => a.AssignedAt ?? DateTimeOffset.MinValue)
                .ToList();
        }

        // Verify the requesting expert has access (current or historical)
        var hasAccess = assignments.Any(a => string.Equals(a.AssignedReviewerId, reviewerId, StringComparison.Ordinal));
        if (!hasAccess)
        {
            throw ApiException.Forbidden("review_history_forbidden", "You can only view history for reviews you are or were assigned to.");
        }

        var reviewerIds = assignments
            .Select(a => a.AssignedReviewerId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct()
            .Cast<string>()
            .ToList();

        var reviewers = reviewerIds.Count == 0
            ? new Dictionary<string, ExpertUser>()
            : await db.ExpertUsers.AsNoTracking()
                .Where(e => reviewerIds.Contains(e.Id))
                .ToDictionaryAsync(e => e.Id, ct);

        List<ExpertReviewDraft> drafts;
        var draftsQuery = db.ExpertReviewDrafts.AsNoTracking()
            .Where(d => d.ReviewRequestId == reviewRequestId);

        if (!db.Database.IsSqlite())
        {
            drafts = await draftsQuery
                .OrderBy(d => d.DraftSavedAt)
                .ToListAsync(ct);
        }
        else
        {
            drafts = (await draftsQuery.ToListAsync(ct))
                .OrderBy(d => d.DraftSavedAt)
                .ToList();
        }

        List<AuditEvent> auditEvents;
        var auditEventsQuery = db.AuditEvents.AsNoTracking()
            .Where(ae => ae.ResourceId == reviewRequestId && ae.ResourceType == "ExpertReview");

        if (!db.Database.IsSqlite())
        {
            auditEvents = await auditEventsQuery
                .OrderBy(ae => ae.OccurredAt)
                .Take(100)
                .ToListAsync(ct);
        }
        else
        {
            auditEvents = (await auditEventsQuery.ToListAsync(ct))
                .OrderBy(ae => ae.OccurredAt)
                .Take(100)
                .ToList();
        }

        var historyEntries = new List<ExpertReviewHistoryEntryResponse>();

        // Add assignment events
        foreach (var assignment in assignments)
        {
            var reviewerName = assignment.AssignedReviewerId is not null && reviewers.TryGetValue(assignment.AssignedReviewerId, out var r)
                ? r.DisplayName : assignment.AssignedReviewerId ?? "Unknown";

            historyEntries.Add(new ExpertReviewHistoryEntryResponse(
                assignment.AssignedAt ?? DateTimeOffset.MinValue,
                assignment.ClaimState.ToString().ToLowerInvariant(),
                reviewerName,
                assignment.ReasonCode));

            if (assignment.ReleasedAt is not null)
            {
                historyEntries.Add(new ExpertReviewHistoryEntryResponse(
                    assignment.ReleasedAt.Value,
                    "released",
                    reviewerName,
                    assignment.ReasonCode));
            }
        }

        // Add audit trail entries
        foreach (var ae in auditEvents)
        {
            historyEntries.Add(new ExpertReviewHistoryEntryResponse(
                ae.OccurredAt,
                ae.Action.ToLowerInvariant(),
                ae.ActorName,
                ae.Details));
        }

        historyEntries = historyEntries.OrderBy(e => e.Timestamp).ToList();

        return new ExpertReviewHistoryResponse(
            reviewRequestId,
            reviewRequest.State.ToString().ToLowerInvariant(),
            reviewRequest.CreatedAt,
            reviewRequest.CompletedAt,
            drafts.Count,
            historyEntries);
    }

    private async Task SubmitReviewAsync(TrackedWriteContext context, string reviewerId, ExpertReviewSubmitRequest request, CancellationToken ct, string auditAction, string analyticsEvent)
    {
        ValidateSubmitRequest(context.ReviewRequest.SubtestCode, request);

        var draft = await db.ExpertReviewDrafts
            .FirstOrDefaultAsync(existingDraft => existingDraft.ReviewRequestId == context.ReviewRequest.Id && existingDraft.ReviewerId == reviewerId, ct);

        if (draft is null)
        {
            draft = new ExpertReviewDraft
            {
                Id = $"erd-{Guid.NewGuid():N}",
                ReviewRequestId = context.ReviewRequest.Id,
                ReviewerId = reviewerId,
                Version = 0
            };
            db.ExpertReviewDrafts.Add(draft);
        }

        if (request.Version is not null && request.Version != draft.Version)
        {
            throw ApiException.Conflict(
                "draft_version_conflict",
                "This draft has changed since you opened it.",
                [new ApiFieldError("version", "conflict", "Reload the review to merge the latest saved draft before submitting.")]);
        }

        draft.RubricEntriesJson = JsonSupport.Serialize(NormalizeScores(request.Scores, context.ReviewRequest.SubtestCode));
        draft.CriterionCommentsJson = JsonSupport.Serialize(NormalizeCriterionComments(request.CriterionComments, context.ReviewRequest.SubtestCode));
        draft.FinalCommentDraft = NormalizeFinalComment(request.FinalComment, required: true);
        draft.Version += 1;
        draft.State = "submitted";
        draft.DraftSavedAt = DateTimeOffset.UtcNow;

        context.ReviewRequest.State = ReviewRequestState.Completed;
        context.ReviewRequest.CompletedAt = DateTimeOffset.UtcNow;
        context.ActiveAssignment.ClaimState = ExpertAssignmentState.Released;
        context.ActiveAssignment.ReleasedAt = DateTimeOffset.UtcNow;
        context.ActiveAssignment.ReasonCode = "submitted";

        // Phase 4 follow-up: record the SLA outcome so admins can audit
        // on-time submissions alongside the overdue snapshots written by
        // ExpertAutoAssignmentService.ProcessSlaEscalationsAsync.
        WriteSubmissionSlaSnapshot(context, reviewerId);

        await LogExpertAuditAsync(reviewerId, context.Expert.DisplayName, auditAction, context.ReviewRequest.Id, draft.FinalCommentDraft, ct);
        await RecordExpertEventAsync(reviewerId, analyticsEvent, new { reviewRequestId = context.ReviewRequest.Id, version = draft.Version }, ct);
        await db.SaveChangesAsync(ct);
        await notifications.CreateForLearnerAsync(
            NotificationEventKey.LearnerReviewCompleted,
            context.Attempt.UserId,
            "review_request",
            context.ReviewRequest.Id,
            (context.ReviewRequest.CompletedAt ?? DateTimeOffset.UtcNow).UtcDateTime.Ticks.ToString(),
            new Dictionary<string, object?>
            {
                ["attemptId"] = context.Attempt.Id,
                ["reviewRequestId"] = context.ReviewRequest.Id,
                ["subtest"] = context.ReviewRequest.SubtestCode,
                ["message"] = $"Your {context.ReviewRequest.SubtestCode} tutor review is now ready."
            },
            ct);

        // ── Escalation auto-trigger: compare AI vs human scores ──
        await TryCreateEscalationAsync(context, reviewerId, request.Scores, ct);
    }

    /// <summary>Writes an <see cref="ExpertSlaSnapshot"/> row at submit time so
    /// the audit trail captures both met and overdue outcomes (overdue rows
    /// are written by the SLA-escalation background job). Idempotent enough:
    /// each (reviewRequestId, expertId) pair gets one met snapshot per
    /// successful submission. Failure here is non-fatal — the wider submit
    /// transaction is more important than the audit row.</summary>
    private void WriteSubmissionSlaSnapshot(TrackedWriteContext context, string reviewerId)
    {
        try
        {
            var assignedAt = context.ActiveAssignment.AssignedAt ?? context.ReviewRequest.CreatedAt;
            var slaHours = string.Equals(context.ReviewRequest.TurnaroundOption, "express",
                StringComparison.OrdinalIgnoreCase)
                ? SlaHoursExpressForSubmit
                : SlaHoursStandardForSubmit;
            var slaDueAt = assignedAt.AddHours(slaHours);
            var now = context.ReviewRequest.CompletedAt ?? DateTimeOffset.UtcNow;
            var wasMet = now <= slaDueAt;

            db.ExpertSlaSnapshots.Add(new ExpertSlaSnapshot
            {
                Id = $"sla-{Guid.NewGuid():N}",
                ReviewRequestId = context.ReviewRequest.Id,
                ExpertId = reviewerId,
                SlaDueAt = slaDueAt,
                WasMet = wasMet,
                SlaState = wasMet ? "met" : "overdue",
                CreatedAt = now,
            });
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to write SLA snapshot for review {ReviewRequestId}", context.ReviewRequest.Id);
        }
    }

    /// <summary>
    /// Creates a ReviewEscalation if the average divergence between AI-suggested and human scores exceeds the threshold.
    /// </summary>
    private async Task TryCreateEscalationAsync(TrackedWriteContext context, string reviewerId, Dictionary<string, int> humanScores, CancellationToken ct)
    {
        const int DivergenceThreshold = 40; // OET 0-500 scale; ~1 band difference across criteria

        try
        {
            var evaluation = await db.Evaluations
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.AttemptId == context.Attempt.Id, ct);

            if (evaluation is null || evaluation.State != AsyncState.Completed)
                return;

            var isWriting = string.Equals(context.ReviewRequest.SubtestCode, "writing", StringComparison.OrdinalIgnoreCase);
            var aiScores = NormalizeAiSuggestedScores(evaluation, isWriting);

            if (aiScores.Count == 0 || humanScores.Count == 0)
                return;

            var aiAvg = aiScores.Values.Average();
            var humanAvg = humanScores.Values.Average();
            var scaledAi = (int)Math.Round(aiAvg * (500.0 / (isWriting ? 7.0 : 6.0)));
            var scaledHuman = (int)Math.Round(humanAvg * (500.0 / (isWriting ? 7.0 : 6.0)));
            var divergence = Math.Abs(scaledAi - scaledHuman);

            if (divergence < DivergenceThreshold)
                return;

            var escalation = new ReviewEscalation
            {
                Id = $"ESC-{Guid.NewGuid():N}",
                ReviewRequestId = context.ReviewRequest.Id,
                OriginalReviewerId = reviewerId,
                SubtestCode = context.ReviewRequest.SubtestCode,
                TriggerCriterion = "average_divergence",
                AiScore = scaledAi,
                HumanScore = scaledHuman,
                Divergence = divergence,
                Status = "pending",
                CreatedAt = DateTimeOffset.UtcNow
            };

            db.ReviewEscalations.Add(escalation);
            await db.SaveChangesAsync(ct);

            logger.LogWarning(
                "Escalation {EscalationId} created: AI={AiScore} Human={HumanScore} Divergence={Divergence} for ReviewRequest={ReviewRequestId}",
                escalation.Id, scaledAi, scaledHuman, divergence, context.ReviewRequest.Id);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to check/create escalation for review {ReviewRequestId}", context.ReviewRequest.Id);
        }
    }
}
