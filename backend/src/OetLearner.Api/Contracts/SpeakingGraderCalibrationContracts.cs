namespace OetLearner.Api.Contracts;

// Speaking grader calibration — admin contracts (owner spec 4 Oct 2026).
//
// BLIND BY CONSTRUCTION: nothing here carries an AI score, grade or rationale. The labelling view
// is built from the transcript, the card and the audio only, so the expert cannot be anchored by
// the number we are trying to validate.

/// <summary>A finished AI card that could be promoted for the expert to mark. No learner identity.</summary>
public sealed record SpeakingGraderCalibrationCandidate(
    string SessionId,
    string ProfessionId,
    string CardTitle,
    DateTimeOffset FinishedAt,
    int ElapsedSeconds,
    bool HasAudio);

public sealed record SpeakingGraderCalibrationSampleRow(
    string Id,
    string SessionId,
    string ProfessionId,
    string CardTitle,
    bool HasAudio,
    /// <summary>pending | labelled | excluded</summary>
    string Status,
    int? ExpertOverallScaled,
    string? ExpertGrade,
    DateTimeOffset PromotedAt,
    DateTimeOffset? LabelledAt,
    /// <summary>False when the performance can no longer be graded or replayed: its audio expired or was deleted,
    /// the learner withdrew consent, or its transcript was erased. Such a sample is not counted or graded.</summary>
    bool Usable = true);

/// <summary>
/// How much expert-labelled evidence exists, against the coverage a calibration report needs
/// (thresholds approved by the owner 2026-10-05; never relax them). <c>Unmet</c> is plain language, never a code.
/// </summary>
public sealed record SpeakingGraderCalibrationCoverage(
    int Total,
    int Labelled,
    int Pending,
    int Excluded,
    /// <summary>Labelled samples per expert grade: A, B, C+, C, D, E.</summary>
    IReadOnlyDictionary<string, int> LabelledByGrade,
    /// <summary>Labelled samples whose expert overall is 320-380 (the pass line is 350).</summary>
    int LabelledNearPassLine,
    /// <summary>Share (0-1) of labelled samples that have audio.</summary>
    double AudioShare,
    int RequiredLabelled,
    int RequiredPerGrade,
    int RequiredNearPassLine,
    double RequiredAudioShare,
    bool MeetsCoverage,
    IReadOnlyList<string> Unmet,
    /// <summary>Labelled samples with an expert overall of 320-340 (just below the 350 pass line).</summary>
    int LabelledBelowPassLine = 0,
    /// <summary>Labelled samples with an expert overall of 350-380 (at or just above the pass line).</summary>
    int LabelledAtOrAbovePassLine = 0,
    /// <summary>How many labelled samples each side of the pass line the report needs.</summary>
    int RequiredEachSideOfPassLine = 0);

public sealed record SpeakingGraderCalibrationOverview(
    SpeakingGraderCalibrationCoverage Coverage,
    IReadOnlyList<SpeakingGraderCalibrationSampleRow> Samples);

// ── Full Mock samples: one whole two-card test, one expert mark (owner request 7 Oct 2026) ──

/// <summary>A completed two-card AI exam that could be promoted as one Full Mock calibration sample.
/// No learner identity, no AI result.</summary>
public sealed record SpeakingGraderCalibrationMockCandidate(
    string ExamId,
    string ProfessionId,
    string CardATitle,
    string CardBTitle,
    DateTimeOffset FinishedAt,
    bool HasAudio);

public sealed record SpeakingGraderCalibrationMockSampleRow(
    string Id,
    string ExamId,
    string ProfessionId,
    string CardATitle,
    string CardBTitle,
    bool HasAudio,
    /// <summary>pending | labelled | excluded</summary>
    string Status,
    int? ExpertOverallScaled,
    string? ExpertGrade,
    DateTimeOffset PromotedAt,
    DateTimeOffset? LabelledAt,
    bool Usable = true);

public sealed record SpeakingGraderCalibrationMockOverview(
    SpeakingGraderCalibrationCoverage Coverage,
    IReadOnlyList<SpeakingGraderCalibrationMockSampleRow> Samples);

/// <summary>The blind Full Mock marking view: both cards, both transcripts, both clip lists — one label.</summary>
public sealed record SpeakingGraderCalibrationMockSampleDetail(
    string Id,
    string Status,
    bool HasAudio,
    SpeakingGraderCalibrationCard CardA,
    SpeakingGraderCalibrationCard CardB,
    IReadOnlyList<SpeakingGraderCalibrationTranscriptLine> TranscriptA,
    IReadOnlyList<SpeakingGraderCalibrationTranscriptLine> TranscriptB,
    IReadOnlyList<SpeakingGraderCalibrationAudioClip> ClipsA,
    IReadOnlyList<SpeakingGraderCalibrationAudioClip> ClipsB,
    IReadOnlyList<SpeakingGraderCalibrationCriterion> Criteria,
    SpeakingGraderCalibrationLabel? Label,
    string ExcludedReason);

public sealed record SpeakingGraderCalibrationMockPromoteRequest(string? ExamId);

public sealed record SpeakingGraderCalibrationCriterion(string Code, string Label, string Family, int Max);

public sealed record SpeakingGraderCalibrationCard(
    string Title,
    string ProfessionId,
    string Setting,
    string CandidateRole,
    string InterlocutorRole,
    string Background,
    IReadOnlyList<string> Tasks);

public sealed record SpeakingGraderCalibrationTranscriptLine(
    /// <summary>candidate | patient (as stored).</summary>
    string Speaker,
    int StartMs,
    int EndMs,
    string Text);

public sealed record SpeakingGraderCalibrationAudioClip(string RecordingId, int DurationSeconds, string MimeType);

/// <summary>What the expert has recorded so far (null until labelled).</summary>
public sealed record SpeakingGraderCalibrationLabel(
    IReadOnlyDictionary<string, int> Scores,
    int OverallScaled,
    string Notes);

public sealed record SpeakingGraderCalibrationSampleDetail(
    string Id,
    string Status,
    bool HasAudio,
    SpeakingGraderCalibrationCard Card,
    IReadOnlyList<SpeakingGraderCalibrationTranscriptLine> Transcript,
    IReadOnlyList<SpeakingGraderCalibrationAudioClip> Clips,
    IReadOnlyList<SpeakingGraderCalibrationCriterion> Criteria,
    SpeakingGraderCalibrationLabel? Label,
    string ExcludedReason);

public sealed record SpeakingGraderCalibrationPromoteRequest(string? SessionId);

public sealed record SpeakingGraderCalibrationLabelRequest(
    Dictionary<string, int>? Scores,
    int? OverallScaled,
    string? Notes);

public sealed record SpeakingGraderCalibrationExcludeRequest(string? Reason);

// ── The harness ──────────────────────────────────────────────────────────

/// <summary>Start a calibration run: how many times each marked performance is graded (at least two), whether the audio
/// judge runs on every grade (default true), what it grades (<c>card</c> = each marked single card with the card grader;
/// <c>mock</c> = each marked Full Mock with the combined grader), and whether it is an OWNER PILOT (informational; its
/// verdict can never pass — the full approved coverage is what can).</summary>
public sealed record SpeakingGraderCalibrationRunCreateRequest(
    int? Repeats,
    bool? UseAudio,
    string? Scope = null,
    bool? Pilot = null);

public sealed record SpeakingGraderCalibrationRunProgress(int Total, int Pending, int Queued, int Done, int Failed);

/// <summary>A run with its progress and, when any grade has finished, the report so far (numbers only).</summary>
public sealed record SpeakingGraderCalibrationRunView(
    string Id,
    /// <summary>running | complete | cancelled</summary>
    string Status,
    string GraderVersion,
    int Repeats,
    bool UseAudio,
    DateTimeOffset CreatedAt,
    DateTimeOffset? FinalizedAt,
    SpeakingGraderCalibrationRunProgress Progress,
    OetLearner.Api.Services.Speaking.SpeakingCalibrationReport? Report,
    /// <summary>card | mock: which marked set the run grades.</summary>
    string Scope = "card",
    /// <summary>An OWNER PILOT run: informational comparison, never a pass.</summary>
    bool Pilot = false);

/// <summary>What <c>next</c> did: <c>queued</c> | <c>busy</c> | <c>yield</c> | <c>done</c> | <c>complete</c>.</summary>
public sealed record SpeakingGraderCalibrationNext(string State, string? GradeId, SpeakingGraderCalibrationRunProgress Progress);
