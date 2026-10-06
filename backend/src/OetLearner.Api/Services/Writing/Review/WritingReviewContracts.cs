using System.Security.Cryptography;
using System.Text;

namespace OetLearner.Api.Services.Writing.Review;

/// <summary>
/// Secondary reviewer of a primary Writing grade (owner handoff 6 Oct 2026, feature code
/// <c>writing.grade.review</c>). <see cref="Off"/> = never runs; <see cref="Shadow"/> = runs and records what it
/// WOULD change in admin notes only (the result is never changed and never held); <see cref="Enforce"/> = the
/// reviewed result is what the candidate gets, and a reviewer outage HOLDS the letter for a BOUNDED number of
/// re-queues (<c>WritingGradeChainOptions.ReviewMaxHolds</c>), after which it completes on its primary result,
/// flagged for a tutor (<c>WritingGradeReviewer.HoldExhausted</c>), so a letter is never left Queued.
/// </summary>
public enum WritingReviewMode
{
    Off = 0,
    Shadow = 1,
    Enforce = 2,
}

public enum WritingReviewStatus
{
    /// <summary>No review happened (not enabled, or an admin kill-list entry): the primary result stands.</summary>
    Skipped = 0,

    /// <summary>Shadow run: the proposals are in the admin notes, the result is untouched.</summary>
    Shadowed = 1,

    /// <summary>The applier's output replaces the primary scores and findings.</summary>
    Reviewed = 2,
}

public enum WritingReviewFindingOrigin
{
    /// <summary>Deterministic rule engine.</summary>
    Rule = 0,

    /// <summary>The primary AI grader.</summary>
    Ai = 1,

    /// <summary>Added by the reviewer (a false negative of the primary).</summary>
    Reviewer = 2,
}

/// <summary>The one failure a held review raises. The primary result and credit hold stay on the letter.</summary>
public static class WritingReviewHold
{
    public const string UnavailableCode = "writing_review_unavailable";
    public const string UnavailableMessage = "Writing grading hit a processing error. Please retry.";

    internal static ApiException Unavailable()
        => ApiException.ServiceUnavailable(UnavailableCode, UnavailableMessage, retryable: true);
}

/// <summary>Primary or reviewed scores. The raw total and the band are always derived in code.</summary>
public sealed record WritingReviewScores(int C1, int C2, int C3, int C4, int C5, int C6, int ScaledScore)
{
    public int RawTotal => C1 + C2 + C3 + C4 + C5 + C6;

    /// <summary>Grade letter of the candidate-reported (rounded) score, never of the raw total.</summary>
    public string Band => OetScoring.OetReportedGradeLetter(ScaledScore);
}

/// <summary>One numbered row of the finding table the reviewer sees (ids f1..fn; reviewer-added rows are a1..an).</summary>
public sealed record WritingReviewFinding(
    string Id,
    string Fingerprint,
    WritingReviewFindingOrigin Origin,
    WritingAssessmentRuleFinding Finding)
{
    public static WritingReviewFinding From(string id, WritingReviewFindingOrigin origin, WritingAssessmentRuleFinding finding)
        => new(id, FingerprintOf(finding), origin, finding);

    /// <summary>First 12 hex chars of SHA256(rule id without the AI prefix | quote | criterion | first 80 chars of the message).</summary>
    public static string FingerprintOf(WritingAssessmentRuleFinding finding)
    {
        var rule = (finding.RuleId ?? string.Empty).Trim();
        if (rule.StartsWith("AI:", StringComparison.OrdinalIgnoreCase)) rule = rule[3..];
        var message = (finding.Message ?? string.Empty).Trim().ToLowerInvariant();
        if (message.Length > 80) message = message[..80];
        var material = string.Join(
            '|',
            rule.ToLowerInvariant(),
            (finding.Quote ?? string.Empty).Trim().ToLowerInvariant(),
            finding.PrimaryCriterionCode ?? string.Empty,
            message);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material)))[..12].ToLowerInvariant();
    }
}

/// <summary>A required case-note fact the letter does not carry (computed in code from the fact map).</summary>
public sealed record WritingReviewMissingFact(string Ref, string Text);

/// <summary>One persisted reviewer pass: the raw reviewer JSON, so a resume re-applies it with no provider call.</summary>
public sealed record WritingReviewPassRecord(
    string Kind,
    string Json,
    string? Provider,
    string? Model,
    string? UsageRecordId,
    DateTimeOffset CompletedAt);

/// <summary>
/// The reviewer's resume state, kept inside <c>WritingSubmission.ProviderResultJson</c> (never in a DTO). A stage
/// is reused only when <see cref="Version"/> and <see cref="InputFingerprint"/> match the run being resumed.
/// </summary>
public sealed record WritingReviewStageRecord(
    string Version,
    string InputFingerprint,
    IReadOnlyList<WritingReviewPassRecord> Passes);

/// <summary>Everything the reviewer needs for one letter. Built by the grading pipeline.</summary>
public sealed record WritingReviewRequest(
    Guid SubmissionId,
    string UserId,
    int GradeEpoch,
    DateTimeOffset? ClaimedAt,
    string SubmissionMode,
    string Profession,
    string LetterType,
    string TaskSnapshot,
    string CaseNotesSnapshot,
    string Letter,
    WritingReviewScores Primary,
    string PrimaryModel,
    IReadOnlyList<WritingReviewFinding> Findings,
    IReadOnlyList<WritingReviewMissingFact> MissingRequiredFacts,
    IReadOnlyList<string> CurrentPriorities,
    WritingReviewStageRecord? Persisted,
    Func<WritingReviewStageRecord, CancellationToken, Task> PersistStageAsync,
    WritingReviewMode Mode);

/// <summary>
/// What the reviewer decided, after the deterministic applier. <see cref="Scores"/> and <see cref="Findings"/> replace the
/// primary values only when <see cref="Status"/> is <see cref="WritingReviewStatus.Reviewed"/>.
/// </summary>
public sealed record WritingReviewOutcome(
    WritingReviewStatus Status,
    WritingReviewScores Scores,
    IReadOnlyList<WritingReviewFinding> Findings,
    IReadOnlyCollection<string> DuplicateFingerprints,
    IReadOnlyList<string> TutorReasons,
    WritingReviewAdminNotes Notes,
    IReadOnlyList<string> EnhancedFailures);

/// <summary>
/// Technical notes for admins only (report <c>FeatureRecordJson</c> "review" object and the audit event).
/// Never projected to a candidate DTO.
/// </summary>
public sealed class WritingReviewAdminNotes
{
    public string Version { get; set; } = string.Empty;
    public string Mode { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? Reason { get; set; }
    public string? Decision { get; set; }
    public string? Summary { get; set; }
    public string? Provider { get; set; }
    public string? Model { get; set; }
    public string? PrimaryModel { get; set; }
    public int Passes { get; set; }
    public WritingReviewScores? Primary { get; set; }
    public WritingReviewScores? Final { get; set; }
    public int LeakCount { get; set; }
    public bool EnhancedUnresolved { get; set; }
    public string? EnhancedNotes { get; set; }

    /// <summary>"f3:confirmed", "f4:false_positive", "a1:added" ...</summary>
    public List<string> Dispositions { get; } = new();

    /// <summary>Proposals the applier refused, each as "id:code".</summary>
    public List<string> Rejected { get; } = new();

    /// <summary>consistency_enforced, scaled_delta_capped, primary_outside_corridor, enhanced_unresolved ...</summary>
    public List<string> Flags { get; } = new();

    /// <summary>The enhanced (400+) checks that were still failing after the last pass.</summary>
    public List<string> EnhancedFailures { get; } = new();

    public List<string> UsageRecordIds { get; } = new();
}

// ---------------------------------------------------------------------------
// The parsed reviewer reply. Tolerant: absent members read as empty, never as a failure.
// ---------------------------------------------------------------------------

public sealed record WritingReviewFindingVerdict(
    string Id,
    string Verdict,
    string? Severity,
    string? DuplicateOf,
    string? Message,
    string? Fix,
    string? Reason);

public sealed record WritingReviewAddedFinding(
    string Criterion,
    string Severity,
    string Quote,
    string Message,
    string? Fix,
    string? Evidence);

public sealed record WritingReviewOmissionRuling(string FactRef, bool Material, string? Reason);

public sealed record WritingReviewScoreChange(string Criterion, int From, int To, IReadOnlyList<string> FindingIds);

public sealed record WritingReviewDecision(
    string Decision,
    string? Summary,
    IReadOnlyList<WritingReviewFindingVerdict> Findings,
    IReadOnlyList<WritingReviewAddedFinding> Added,
    IReadOnlyList<WritingReviewOmissionRuling> Omissions,
    IReadOnlyDictionary<string, int>? CriterionScores,
    IReadOnlyList<WritingReviewScoreChange> ScoreChanges,
    int? EstimatedScaledScore,
    IReadOnlyList<string> UnsupportedFixIds,
    string? EnhancedNotes,
    IReadOnlyList<string> Anomalies,
    string RawJson);

/// <summary>
/// Policy numbers of the reviewer, bound from <c>Writing:Review</c> with these in-class defaults. The 400+ thresholds,
/// the delta caps and the consistency corridor are owner-tunable; the numbers below are the owner's decisions
/// (6 Oct 2026). There is NO hard 400 cap anywhere: failing the enhanced checks triggers corrective rounds and a
/// bounded recalibration, never a clip.
/// </summary>
public sealed class WritingReviewOptions
{
    public const string SectionName = "Writing:Review";

    /// <summary>Reported score at or above which enhanced verification runs.</summary>
    public int EnhancedThreshold { get; set; } = OetScoring.WritingEnhancedVerificationScaled;

    /// <summary>Largest move of one criterion score in one pass.</summary>
    public int MaxCriterionDeltaPerPass { get; set; } = 2;

    /// <summary>Largest move of the /500 estimate in one pass.</summary>
    public int MaxScaledDeltaPerPass { get; set; } = 60;

    /// <summary>The same, when the raw total moved by 3 or more.</summary>
    public int MaxScaledDeltaWhenRawMoves { get; set; } = 100;

    /// <summary>The /500 score is holistic, not linear in the raw total: a REVIEWER-CHANGED score must sit between
    /// <c>ScaledPerRawPoint * raw + CorridorLowerOffset</c> and <c>ScaledPerRawPoint * raw + CorridorUpperOffset</c>
    /// (open above from <see cref="CorridorOpenFromRaw"/>). A score the reviewer did not touch is never rewritten.</summary>
    public int ScaledPerRawPoint { get; set; } = 10;

    public int CorridorLowerOffset { get; set; } = 20;
    public int CorridorUpperOffset { get; set; } = 100;
    public int CorridorOpenFromRaw { get; set; } = 36;

    /// <summary>Enhanced (400+) checklist: Purpose at least this (0-3).</summary>
    public int Enhanced400MinPurpose { get; set; } = 2;

    /// <summary>Enhanced (400+) checklist: every other criterion at least this (0-7).</summary>
    public int Enhanced400MinOther { get; set; } = 5;

    /// <summary>Enhanced (400+) checklist: at most this many surviving scored minor findings.</summary>
    public int Enhanced400MaxMinorFindings { get; set; } = 5;

    /// <summary>Findings the reviewer may add (false negatives) in one pass.</summary>
    public int MaxAddedFindings { get; set; } = 5;

    /// <summary>Enhanced passes after which a still-failing 400+ is published as recalibrated and flagged rv_unresolved.</summary>
    public int MaxEnhancedRounds { get; set; } = 2;
}
