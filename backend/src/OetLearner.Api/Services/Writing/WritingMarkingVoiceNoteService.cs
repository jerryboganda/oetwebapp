using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Content;

namespace OetLearner.Api.Services.Writing;

// Submission-keyed tutor voice note for the Writing marking surface (System A).
//
// The older expert console attaches voice notes to a ReviewRequest (see
// ExpertService.AddWritingReviewVoiceNoteAsync + ReviewVoiceNote). The Writing V2
// marking workspace is keyed by WritingSubmission.Id and has no ReviewRequest, so a
// thin submission-keyed slice lives here. One overall note per submission — the upsert
// path replaces any prior row so re-recording is idempotent.
//
// Authorization is owned by the callers: the tutor endpoints gate on
// CanAccessSubmissionAsync (an active assignment for this tutor); the learner endpoint
// gates on submission ownership + the tutor review being submitted (so a learner never
// hears a half-finished note). Media playback is further gated by
// MediaAssetAccessService, which recognises this table for both roles.
public sealed class WritingMarkingVoiceNoteService
{
    private readonly LearnerDbContext _db;
    private readonly IFileStorage _fileStorage;

    public WritingMarkingVoiceNoteService(LearnerDbContext db, IFileStorage fileStorage)
    {
        _db = db;
        _fileStorage = fileStorage;
    }

    private const int MaxDurationSeconds = 600; // 10 minutes — generous for an overall note.

    /// <summary>
    /// Attach (or replace) the single overall voice note for a submission. The caller MUST
    /// have already verified the tutor may access the submission.
    /// </summary>
    public async Task<WritingMarkingVoiceNoteDto> UpsertAsync(
        Guid submissionId,
        string tutorId,
        string mediaAssetId,
        int durationSeconds,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(tutorId))
        {
            throw ApiException.Validation("tutor_required", "Tutor id is required.");
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

        var media = await _db.MediaAssets
            .AsNoTracking()
            .FirstOrDefaultAsync(asset => asset.Id == mediaAssetId, ct)
            ?? throw ApiException.NotFound(
                "voice_note_media_not_found",
                "Upload the voice note before attaching it to the review.");
        if (!string.Equals(media.UploadedBy, tutorId, StringComparison.OrdinalIgnoreCase))
        {
            throw ApiException.Forbidden(
                "voice_note_forbidden",
                "You can only attach voice notes uploaded by your account.");
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

        var now = DateTimeOffset.UtcNow;
        var existing = await _db.WritingReviewVoiceNotes
            .FirstOrDefaultAsync(n => n.SubmissionId == submissionId, ct);
        if (existing is not null)
        {
            existing.TutorId = tutorId;
            existing.MediaAssetId = media.Id;
            existing.DurationSeconds = durationSeconds;
            existing.Status = "ready";
            existing.UpdatedAt = now;
            await _db.SaveChangesAsync(ct);
            return ToDto(existing);
        }

        var note = new WritingReviewVoiceNote
        {
            Id = Guid.NewGuid(),
            SubmissionId = submissionId,
            TutorId = tutorId,
            MediaAssetId = media.Id,
            DurationSeconds = durationSeconds,
            Status = "ready",
            CreatedAt = now,
            UpdatedAt = now,
        };
        _db.WritingReviewVoiceNotes.Add(note);
        await _db.SaveChangesAsync(ct);
        return ToDto(note);
    }

    /// <summary>Latest note for a submission (tutor/internal — caller verifies access).</summary>
    public async Task<WritingMarkingVoiceNoteDto?> GetForSubmissionAsync(Guid submissionId, CancellationToken ct)
    {
        var note = await _db.WritingReviewVoiceNotes
            .AsNoTracking()
            .FirstOrDefaultAsync(n => n.SubmissionId == submissionId, ct);
        return note is null ? null : ToDto(note);
    }

    /// <summary>
    /// Learner-facing fetch. Throws 404 when the submission is not owned by the user (IDOR guard).
    /// Returns null when the submission is owned but no submitted note exists yet.
    /// </summary>
    public async Task<WritingMarkingVoiceNoteDto?> GetForLearnerAsync(string userId, Guid submissionId, CancellationToken ct)
    {
        var owned = await _db.WritingSubmissions
            .AsNoTracking()
            .AnyAsync(s => s.Id == submissionId && s.UserId == userId, ct);
        if (!owned)
        {
            throw ApiException.NotFound("submission_not_found", "Submission not found.");
        }

        // Only surface once the tutor review for this submission has been submitted, so the
        // learner never hears a draft note.
        var reviewSubmitted = await _db.WritingTutorReviews
            .AsNoTracking()
            .AnyAsync(r => r.SubmissionId == submissionId && r.Status == "submitted", ct);
        if (!reviewSubmitted)
        {
            return null;
        }

        var note = await _db.WritingReviewVoiceNotes
            .AsNoTracking()
            .FirstOrDefaultAsync(n => n.SubmissionId == submissionId && n.Status == "ready", ct);
        return note is null ? null : ToDto(note);
    }

    private static WritingMarkingVoiceNoteDto ToDto(WritingReviewVoiceNote note)
        => new(
            note.Id.ToString(),
            note.SubmissionId.ToString(),
            note.MediaAssetId,
            $"/v1/media/{note.MediaAssetId}/content",
            note.DurationSeconds,
            note.Status,
            note.CreatedAt.ToString("o"));
}

/// <summary>camelCase wire shape consumed by lib/writing/exam-api.ts.</summary>
public sealed record WritingMarkingVoiceNoteDto(
    string Id,
    string SubmissionId,
    string MediaAssetId,
    string Url,
    int? DurationSeconds,
    string Status,
    string CreatedAt);
