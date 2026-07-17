using Microsoft.EntityFrameworkCore;
using OetWithDrHesham.Api.Data;
using OetWithDrHesham.Api.Domain;
using OetWithDrHesham.Api.Services.Content;

namespace OetWithDrHesham.Api.Services.Speaking;

// P4.4 — Speaking expert review voice-note service.
//
// Sister service of the writing voice-note logic in ExpertService
// (see AddWritingReviewVoiceNoteAsync / GetWritingReviewVoiceNotesAsync /
// LoadReviewVoiceNoteResponsesAsync). Kept in its own service so the
// speaking flow can diverge (ASR ingestion, alignment to transcript
// timestamps, etc.) without bloating ExpertService further.
//
// Responsibilities here are intentionally narrow:
//   * Persist a SpeakingReviewVoiceNote row tied to a ReviewRequest +
//     uploaded MediaAsset.
//   * List voice notes for a review, newest first, for the marker UI.
//   * Delete a voice note — only the original author or an admin caller.
//
// The service owns the review + media authorization boundary because the
// HTTP route and future workers all converge here.
public sealed class SpeakingReviewVoiceNoteService
{
    private readonly LearnerDbContext _db;
    private readonly IFileStorage _fileStorage;

    public SpeakingReviewVoiceNoteService(LearnerDbContext db, IFileStorage fileStorage)
    {
        _db = db;
        _fileStorage = fileStorage;
    }

    private const int MaxDurationSeconds = 3600;
    private const int MaxWrittenNotesLength = 4000;
    private const int MaxRubricJsonLength = 8000;

    public async Task<SpeakingReviewVoiceNote> CreateAsync(
        string reviewRequestId,
        string expertUserId,
        string mediaAssetId,
        int durationSeconds,
        string? writtenNotes,
        string? rubricJson,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(reviewRequestId))
        {
            throw ApiException.Validation(
                "review_request_required",
                "Review request id is required.",
                [new ApiFieldError("reviewRequestId", "required", "Provide the review request id.")]);
        }
        if (string.IsNullOrWhiteSpace(expertUserId))
        {
            throw ApiException.Validation(
                "expert_user_required",
                "Expert user id is required.",
                [new ApiFieldError("expertUserId", "required", "Provide the expert user id.")]);
        }
        if (string.IsNullOrWhiteSpace(mediaAssetId))
        {
            throw ApiException.Validation(
                "media_asset_required",
                "Media asset id is required.",
                [new ApiFieldError("mediaAssetId", "required", "Upload the voice note before attaching it.")]);
        }
        if (durationSeconds is < 0 or > MaxDurationSeconds)
        {
            throw ApiException.Validation(
                "invalid_voice_note_duration",
                "Voice note duration is outside the allowed range.",
                [new ApiFieldError("durationSeconds", "out_of_range",
                    $"Voice notes must be between 0 and {MaxDurationSeconds} seconds.")]);
        }

        var trimmedNotes = writtenNotes?.Trim();
        if (trimmedNotes is { Length: > MaxWrittenNotesLength })
        {
            throw ApiException.Validation(
                "voice_note_notes_too_long",
                "Voice note written notes are too long.",
                [new ApiFieldError("writtenNotes", "too_long",
                    $"Voice note written notes cannot exceed {MaxWrittenNotesLength} characters.")]);
        }

        var rubric = string.IsNullOrWhiteSpace(rubricJson) ? "{}" : rubricJson!.Trim();
        if (rubric.Length > MaxRubricJsonLength)
        {
            throw ApiException.Validation(
                "voice_note_rubric_too_long",
                "Voice note rubric summary is too long.",
                [new ApiFieldError("rubricJson", "too_long",
                    $"Voice note rubric JSON cannot exceed {MaxRubricJsonLength} characters.")]);
        }

        var review = await LoadOwnedSpeakingReviewAsync(reviewRequestId, expertUserId, ct);
        if (review.State is ReviewRequestState.Completed or ReviewRequestState.Cancelled)
        {
            throw ApiException.Conflict(
                "review_not_editable",
                "Completed or cancelled reviews cannot be modified.");
        }

        var media = await _db.MediaAssets
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == mediaAssetId, ct)
            ?? throw ApiException.NotFound(
                "voice_note_media_not_found",
                "Upload the voice note before attaching it to the review.");
        if (!string.Equals(media.UploadedBy, expertUserId, StringComparison.OrdinalIgnoreCase))
        {
            throw ApiException.Forbidden(
                "voice_note_forbidden",
                "You can only attach voice notes uploaded by your expert account.");
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
            || !await _fileStorage.ExistsAsync(media.StoragePath, ct))
        {
            throw ApiException.Validation(
                "voice_note_media_not_ready",
                "Voice note upload must finish processing before it can be attached to the review.",
                [new ApiFieldError("mediaAssetId", "not_ready", "Wait for the audio upload to finish, then attach the voice note again.")]);
        }

        // NB: TranscriptText is intentionally left null on create — the ASR
        // pipeline (P4.2) backfills it asynchronously when transcription lands.
        var note = new SpeakingReviewVoiceNote
        {
            Id = $"srvn-{Guid.NewGuid():N}",
            ReviewRequestId = reviewRequestId,
            ExpertUserId = expertUserId,
            MediaAssetId = mediaAssetId,
            DurationSeconds = durationSeconds,
            TranscriptText = null,
            WrittenNotes = string.IsNullOrEmpty(trimmedNotes) ? null : trimmedNotes,
            RubricJson = rubric,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        _db.SpeakingReviewVoiceNotes.Add(note);
        await _db.SaveChangesAsync(ct);
        return note;
    }

    public async Task<IReadOnlyList<SpeakingReviewVoiceNote>> ListForReviewAsync(
        string reviewRequestId,
        string expertUserId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(reviewRequestId))
        {
            throw ApiException.Validation(
                "review_request_required",
                "Review request id is required.");
        }
        if (string.IsNullOrWhiteSpace(expertUserId))
        {
            throw ApiException.Validation(
                "expert_user_required",
                "Expert user id is required.");
        }

        _ = await LoadOwnedSpeakingReviewAsync(reviewRequestId, expertUserId, ct);

        return await _db.SpeakingReviewVoiceNotes
            .AsNoTracking()
            .Where(n => n.ReviewRequestId == reviewRequestId)
            .OrderByDescending(n => n.CreatedAt)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Delete a voice note. Only the original author may delete by default.
    /// Pass <paramref name="isAdmin"/> = true to allow admin overrides.
    /// </summary>
    public async Task DeleteAsync(
        string voiceNoteId,
        string expertUserId,
        CancellationToken ct,
        bool isAdmin = false)
    {
        if (string.IsNullOrWhiteSpace(voiceNoteId))
        {
            throw ApiException.Validation(
                "voice_note_id_required",
                "Voice note id is required.");
        }

        var note = await _db.SpeakingReviewVoiceNotes
            .FirstOrDefaultAsync(n => n.Id == voiceNoteId, ct)
            ?? throw ApiException.NotFound("voice_note_not_found", "Voice note not found.");

        if (!isAdmin && !string.Equals(note.ExpertUserId, expertUserId, StringComparison.OrdinalIgnoreCase))
        {
            throw ApiException.Forbidden(
                "voice_note_delete_forbidden",
                "Only the original author or an admin can delete this voice note.");
        }

        _db.SpeakingReviewVoiceNotes.Remove(note);
        await _db.SaveChangesAsync(ct);
    }

    private async Task<ReviewRequest> LoadOwnedSpeakingReviewAsync(
        string reviewRequestId,
        string expertUserId,
        CancellationToken ct)
    {
        var review = await _db.ReviewRequests
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == reviewRequestId, ct)
            ?? throw ApiException.NotFound("review_request_not_found", "Review request not found.");

        if (!string.Equals(review.SubtestCode, "speaking", StringComparison.OrdinalIgnoreCase))
        {
            throw ApiException.Validation(
                "review_type_mismatch",
                "Voice notes can only be attached to speaking reviews.");
        }

        var assigned = await _db.ExpertReviewAssignments
            .AsNoTracking()
            .AnyAsync(a =>
                a.ReviewRequestId == reviewRequestId
                && a.AssignedReviewerId == expertUserId
                && a.ClaimState != ExpertAssignmentState.Released,
                ct);
        if (!assigned)
        {
            throw ApiException.Forbidden(
                "review_not_owned",
                "You can only access voice notes for speaking reviews assigned to you.");
        }

        return review;
    }
}
