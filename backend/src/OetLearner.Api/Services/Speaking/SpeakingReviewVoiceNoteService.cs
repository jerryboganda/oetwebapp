using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Speaking;

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
// Validation of the linked MediaAsset (mime type, ownership, ready
// state) is performed by the caller (endpoint layer / upload pipeline)
// to keep this service free of HTTP/upload concerns. The service still
// confirms the row + the review exist before saving.
public sealed class SpeakingReviewVoiceNoteService(LearnerDbContext db)
{
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

        var review = await db.ReviewRequests
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == reviewRequestId, ct)
            ?? throw ApiException.NotFound("review_request_not_found", "Review request not found.");

        if (!string.Equals(review.SubtestCode, "speaking", StringComparison.OrdinalIgnoreCase))
        {
            throw ApiException.Validation(
                "review_type_mismatch",
                "Voice notes can only be attached to speaking reviews.");
        }

        var mediaExists = await db.MediaAssets
            .AsNoTracking()
            .AnyAsync(m => m.Id == mediaAssetId, ct);
        if (!mediaExists)
        {
            throw ApiException.NotFound(
                "voice_note_media_not_found",
                "Upload the voice note before attaching it to the review.");
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

        db.SpeakingReviewVoiceNotes.Add(note);
        await db.SaveChangesAsync(ct);
        return note;
    }

    public async Task<IReadOnlyList<SpeakingReviewVoiceNote>> ListForReviewAsync(
        string reviewRequestId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(reviewRequestId))
        {
            throw ApiException.Validation(
                "review_request_required",
                "Review request id is required.");
        }

        return await db.SpeakingReviewVoiceNotes
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

        var note = await db.SpeakingReviewVoiceNotes
            .FirstOrDefaultAsync(n => n.Id == voiceNoteId, ct)
            ?? throw ApiException.NotFound("voice_note_not_found", "Voice note not found.");

        if (!isAdmin && !string.Equals(note.ExpertUserId, expertUserId, StringComparison.OrdinalIgnoreCase))
        {
            throw ApiException.Forbidden(
                "voice_note_delete_forbidden",
                "Only the original author or an admin can delete this voice note.");
        }

        db.SpeakingReviewVoiceNotes.Remove(note);
        await db.SaveChangesAsync(ct);
    }
}
