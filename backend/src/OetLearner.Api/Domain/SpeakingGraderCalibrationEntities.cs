using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace OetLearner.Api.Domain;

// Speaking grader calibration (owner spec 4 Oct 2026): the AI grader may only lose its
// "Provisional" label after its scores have been compared with an OET expert's own marks on
// real performances. This is the expert side: Dr Hesham marks a performance blind to the AI score
// and the platform stores his nine criterion scores and overall /500. (Not to be confused with
// SpeakingCalibrationSample, which measures drift between human tutors.)
//
// A sample holds ids only. The learner's words and audio stay where they already live
// (SpeakingTranscript / SpeakingRecording); promoting a performance never copies them.

public enum SpeakingGraderCalibrationSampleStatus
{
    /// <summary>Promoted, not yet marked by the expert.</summary>
    Pending = 0,

    /// <summary>The expert has marked it; counts towards coverage and the calibration report.</summary>
    Labelled = 1,

    /// <summary>Unusable (no speech, wrong card, broken audio...); kept for audit, never reported.</summary>
    Excluded = 2,
}

[Index(nameof(SpeakingSessionId), IsUnique = true)]
[Index(nameof(Status))]
public class SpeakingGraderCalibrationSample
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    /// <summary>The finished AI card this performance came from. One sample per session.</summary>
    [MaxLength(64)]
    public string SpeakingSessionId { get; set; } = default!;

    /// <summary>The transcript the grader would read, pinned at promotion so a later re-transcription
    /// can never change what the expert and the AI are compared on.</summary>
    [MaxLength(64)]
    public string TranscriptId { get; set; } = default!;

    [MaxLength(64)]
    public string RolePlayCardId { get; set; } = default!;

    [MaxLength(32)]
    public string ProfessionId { get; set; } = default!;

    /// <summary>True when the performance has at least one stored, non-warm-up audio clip, so the
    /// audio judge can hear it. Measured at promotion.</summary>
    public bool HasAudio { get; set; }

    public SpeakingGraderCalibrationSampleStatus Status { get; set; } = SpeakingGraderCalibrationSampleStatus.Pending;

    /// <summary>The expert's nine criterion scores as <c>{ intelligibility:5, ... }</c> (same codes as
    /// <c>AdminService.SpeakingCriterionCodes</c>). Null until labelled.</summary>
    public string? ExpertScoresJson { get; set; }

    /// <summary>The expert's own overall result, 0–500 in steps of 10. Recorded separately from the
    /// criterion sum so the score mapping can be fitted on the expert's judgement, not on ours.</summary>
    public int? ExpertOverallScaled { get; set; }

    [MaxLength(2000)]
    public string ExpertNotes { get; set; } = string.Empty;

    /// <summary>Why a sample was excluded; empty otherwise.</summary>
    [MaxLength(500)]
    public string ExcludedReason { get; set; } = string.Empty;

    [MaxLength(64)]
    public string PromotedById { get; set; } = default!;

    public DateTimeOffset PromotedAt { get; set; }

    [MaxLength(64)]
    public string? LabelledById { get; set; }

    public DateTimeOffset? LabelledAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
