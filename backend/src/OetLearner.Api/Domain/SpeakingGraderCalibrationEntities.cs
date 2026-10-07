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

// ── The Full Mock sample (owner request 7 Oct 2026): one whole two-card test as one performance ──
//
// The combined grader (speaking.score.v3-combined) makes ONE judgement over both role-plays, so it is
// calibrated against ONE expert mark of the whole test, not two card marks. Same rules as a card sample:
// ids only, blind by construction, promotion keeps both cards' audio for a year and writes an audit event.

[Index(nameof(SpeakingExamId), IsUnique = true)]
[Index(nameof(Status))]
public class SpeakingGraderCalibrationMockSample
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    /// <summary>The finished two-card AI exam this sample came from. One sample per exam.</summary>
    [MaxLength(64)]
    public string SpeakingExamId { get; set; } = default!;

    [MaxLength(64)]
    public string SessionAId { get; set; } = default!;

    [MaxLength(64)]
    public string SessionBId { get; set; } = default!;

    /// <summary>The transcripts the grader would read, pinned at promotion.</summary>
    [MaxLength(64)]
    public string TranscriptAId { get; set; } = default!;

    [MaxLength(64)]
    public string TranscriptBId { get; set; } = default!;

    [MaxLength(64)]
    public string CardAId { get; set; } = default!;

    [MaxLength(64)]
    public string CardBId { get; set; } = default!;

    [MaxLength(32)]
    public string ProfessionId { get; set; } = default!;

    /// <summary>True when BOTH cards have at least one stored, non-warm-up audio clip. Measured at promotion.</summary>
    public bool HasAudio { get; set; }

    public SpeakingGraderCalibrationSampleStatus Status { get; set; } = SpeakingGraderCalibrationSampleStatus.Pending;

    /// <summary>The expert's ONE set of nine criterion scores for the whole test (same codes as a card sample).</summary>
    public string? ExpertScoresJson { get; set; }

    /// <summary>The expert's own overall result for the whole test, 0–500 in steps of 10.</summary>
    public int? ExpertOverallScaled { get; set; }

    [MaxLength(2000)]
    public string ExpertNotes { get; set; } = string.Empty;

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

// ── The harness: grading the labelled performances with the grader under test ──
//
// A run grades every labelled performance `Repeats` times with the CURRENT grader (the same grader core a learner's grade
// uses) and keeps only numbers: nothing here is a learner-facing result, no credit is touched and no assessment row is
// written. The report compares these grades with the expert's marks.

public enum SpeakingGraderCalibrationRunStatus
{
    Running = 0,
    Complete = 1,
    Cancelled = 2,
}

public enum SpeakingGraderCalibrationGradeStatus
{
    Pending = 0,

    /// <summary>A durable operation is queued or running for it.</summary>
    Queued = 1,

    Done = 2,

    /// <summary>The last attempt failed (retried by the next `next` call up to the attempt limit).</summary>
    Failed = 3,
}

public class SpeakingGraderCalibrationRun
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    /// <summary>The grader version(s) that actually graded, set when the run is finalised (empty while running).</summary>
    [MaxLength(160)]
    public string GraderVersion { get; set; } = string.Empty;

    /// <summary>How many times each performance is graded (at least two: repeatability is part of the verdict).</summary>
    public int Repeats { get; set; }

    /// <summary>Run the audio stage on every grade, whatever the admin flag says (the flag guards learner grades).</summary>
    public bool UseAudio { get; set; }

    /// <summary>What the run grades: <c>card</c> = each expert-marked single card with the card grader;
    /// <c>mock</c> = each expert-marked Full Mock with the combined grader (speaking.score.v3-combined).</summary>
    [MaxLength(8)]
    public string Scope { get; set; } = "card";

    /// <summary>An OWNER PILOT run (owner request 7 Oct 2026): the comparison is informational and its verdict can
    /// never pass, so a tiny set can be compared before the full validation set is collected. Only a pilot=false
    /// run over the full approved coverage can earn a grader version the loss of the "Provisional" label.</summary>
    public bool Pilot { get; set; }

    public SpeakingGraderCalibrationRunStatus Status { get; set; } = SpeakingGraderCalibrationRunStatus.Running;

    [MaxLength(64)]
    public string CreatedById { get; set; } = default!;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? FinalizedAt { get; set; }

    /// <summary>The report as frozen at finalisation (numbers only).</summary>
    public string? ReportJson { get; set; }
}

[Index(nameof(RunId), nameof(SampleId), nameof(Repeat), IsUnique = true)]
public class SpeakingGraderCalibrationGrade
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string RunId { get; set; } = default!;

    [MaxLength(64)]
    public string SampleId { get; set; } = default!;

    /// <summary>1-based repeat number of this performance within the run.</summary>
    public int Repeat { get; set; }

    public SpeakingGraderCalibrationGradeStatus Status { get; set; } = SpeakingGraderCalibrationGradeStatus.Pending;

    public int Attempts { get; set; }

    /// <summary>The durable operation executing the current attempt.</summary>
    [MaxLength(64)]
    public string? OperationId { get; set; }

    public DateTimeOffset? QueuedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>The grader's nine criterion scores as <c>{ intelligibility:5, ... }</c>.</summary>
    public string? ScoresJson { get; set; }

    public int? RawTotal { get; set; }

    /// <summary>What the platform would report today for these nine scores (the current raw-to-reported map).</summary>
    public int? ReportedScaled { get; set; }

    /// <summary><c>audio</c> (judged from the recording) or <c>transcript_only</c>.</summary>
    [MaxLength(16)]
    public string? IntelligibilitySource { get; set; }

    [MaxLength(32)]
    public string? Provider { get; set; }

    [MaxLength(96)]
    public string? ModelId { get; set; }

    [MaxLength(160)]
    public string? GraderVersion { get; set; }

    /// <summary>A reason code for a failed attempt; never learner text.</summary>
    [MaxLength(64)]
    public string? Error { get; set; }
}
