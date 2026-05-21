using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace OetLearner.Api.Domain;

// P4.4 — Voice-note feedback for Speaking expert reviews.
//
// Mirrors the Writing-side <see cref="ReviewVoiceNote"/> entity in
// WritingAssessmentEntities.cs, but is scoped to speaking reviews so the
// two surfaces can evolve independently (different rubric criteria,
// retention rules, etc.).
//
// Field intent:
//   * <see cref="ReviewRequestId"/>  — links to the ReviewRequest the
//     marker is annotating. Indexed (with CreatedAt) for the common
//     "list notes for this review, newest first" query.
//   * <see cref="ExpertUserId"/>     — id of the marker who recorded
//     the voice note (the original author; only this user, or an admin,
//     can delete it).
//   * <see cref="MediaAssetId"/>     — the audio file uploaded via the
//     shared MediaAsset pipeline.
//   * <see cref="DurationSeconds"/>  — denormalised duration for cheap
//     listing without hitting the media table.
//   * <see cref="TranscriptText"/>   — optional ASR transcript (nullable;
//     populated once the ASR pipeline catches up).
//   * <see cref="WrittenNotes"/>     — optional free-form text the
//     marker can attach alongside the audio (max 4000 chars).
//   * <see cref="RubricJson"/>       — optional criterion summary JSON
//     (e.g. {"fluency": 4, "intelligibility": 5}). Default "{}" so the
//     column is never null.
[Index(nameof(ReviewRequestId), nameof(CreatedAt))]
public class SpeakingReviewVoiceNote
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string ReviewRequestId { get; set; } = default!;

    [MaxLength(64)]
    public string ExpertUserId { get; set; } = default!;

    [MaxLength(64)]
    public string MediaAssetId { get; set; } = default!;

    public int DurationSeconds { get; set; }

    public string? TranscriptText { get; set; }

    [MaxLength(4000)]
    public string? WrittenNotes { get; set; }

    [MaxLength(8000)]
    public string RubricJson { get; set; } = "{}";

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
