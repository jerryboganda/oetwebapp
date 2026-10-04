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
    DateTimeOffset? LabelledAt);

/// <summary>
/// How much expert-labelled evidence exists, against the coverage a calibration report needs
/// (proposed thresholds; the owner confirms them). <c>Unmet</c> is plain language, never a code.
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
    IReadOnlyList<string> Unmet);

public sealed record SpeakingGraderCalibrationOverview(
    SpeakingGraderCalibrationCoverage Coverage,
    IReadOnlyList<SpeakingGraderCalibrationSampleRow> Samples);

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

/// <summary>Start a calibration run: how many times each marked performance is graded (at least two) and whether the audio
/// judge runs on every grade (default true).</summary>
public sealed record SpeakingGraderCalibrationRunCreateRequest(int? Repeats, bool? UseAudio);

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
    OetLearner.Api.Services.Speaking.SpeakingCalibrationReport? Report);

/// <summary>What <c>next</c> did: <c>queued</c> | <c>busy</c> | <c>yield</c> | <c>done</c> | <c>complete</c>.</summary>
public sealed record SpeakingGraderCalibrationNext(string State, string? GradeId, SpeakingGraderCalibrationRunProgress Progress);
