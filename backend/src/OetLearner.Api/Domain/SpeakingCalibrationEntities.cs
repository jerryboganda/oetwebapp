using System.ComponentModel.DataAnnotations;

namespace OetLearner.Api.Domain;

// Wave 4 of docs/SPEAKING-MODULE-PLAN.md.
//
// Tutor calibration mechanics. Senior reviewers ("admins") publish
// gold-marked recordings (`SpeakingCalibrationSample`) with the
// canonical 9-criterion rubric scores. Tutors then submit their own
// scores against the same recording (`SpeakingCalibrationScore`); the
// admin drift report computes the per-tutor mean absolute error vs the
// gold scores.
//
// The 9 criteria are the same ones `OetScoring.SpeakingCriterionScores`
// projects: 4 linguistic (max=6) + 5 clinical (max=3). We do NOT
// duplicate the rubric definition here — scores are stored as a JSON map
// of `{ criterionCode: rawScore }` and validated against
// `OetScoring.SpeakingCriterionMax(code)` at write time.

public enum SpeakingCalibrationSampleStatus
{
    Draft = 0,
    Published = 1,
    Archived = 2,
}

public class SpeakingCalibrationSample
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string CreatedByAdminId { get; set; } = default!;

    [MaxLength(160)]
    public string Title { get; set; } = default!;

    [MaxLength(2000)]
    public string Description { get; set; } = string.Empty;

    /// <summary>Pointer to the source attempt this calibration sample was
    /// extracted from. The audio is fetched via the existing attempt
    /// recording surface; we do NOT duplicate file storage here.</summary>
    [MaxLength(64)]
    public string SourceAttemptId { get; set; } = default!;

    [MaxLength(32)]
    public string ProfessionId { get; set; } = "nursing";

    [MaxLength(16)]
    public string Difficulty { get; set; } = "core";

    /// <summary>Authoritative criterion scores in the same shape as
    /// `OetScoring.SpeakingCriterionScores` JSON: `{ informationGiving:5, ... }`.
    /// Validated server-side at create time so storage never holds an
    /// out-of-range value.</summary>
    public string GoldScoresJson { get; set; } = "{}";

    [MaxLength(2000)]
    public string CalibrationNotes { get; set; } = string.Empty;

    public SpeakingCalibrationSampleStatus Status { get; set; } = SpeakingCalibrationSampleStatus.Draft;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
}

public class SpeakingCalibrationScore
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string SampleId { get; set; } = default!;

    [MaxLength(64)]
    public string TutorId { get; set; } = default!;

    /// <summary>Same shape as `SpeakingCalibrationSample.GoldScoresJson`.</summary>
    public string ScoresJson { get; set; } = "{}";

    /// <summary>Cached drift score = sum of |tutor - gold| across all 9
    /// criteria. Computed at write time so the drift report is a
    /// single-table aggregation.</summary>
    public double TotalAbsoluteError { get; set; }

    [MaxLength(2000)]
    public string Notes { get; set; } = string.Empty;

    public DateTimeOffset SubmittedAt { get; set; }
}

// Inline transcript comments. Surfaced under the line they target on
// the learner-facing transcript view.
public class SpeakingFeedbackComment
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string AttemptId { get; set; } = default!;

    [MaxLength(64)]
    public string ExpertId { get; set; } = default!;

    /// <summary>Zero-based index into the transcript line array. Stored
    /// (rather than a hash) because transcripts are immutable once
    /// graded.</summary>
    public int TranscriptLineIndex { get; set; }

    /// <summary>One of the 9 SpeakingCriterionScores codes
    /// (e.g. "informationGiving") or "general" for free-text observations
    /// that don't map to a single criterion.</summary>
    [MaxLength(48)]
    public string CriterionCode { get; set; } = "general";

    [MaxLength(2000)]
    public string Body { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }
}
