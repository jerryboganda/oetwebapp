namespace OetLearner.Api.Configuration;

/// <summary>
/// Wave 7 of docs/SPEAKING-MODULE-PLAN.md - configurable speaking
/// compliance copy and audio retention. Defaults align with the wider
/// platform: audio is treated as sensitive learner content with a long
/// retention window so tutors and learners can re-review old attempts,
/// but operators can shorten this for tighter privacy regimes.
///
/// Phase 7 of the OET Speaking module plan extended this with:
///   * Versioned consent codes (`recording.v1`, `live_video_with_tutor.v1`)
///     stored on every <c>SpeakingComplianceConsent</c> row.
///   * Differentiated retention windows for AI vs tutor-reviewed recordings.
///   * Long-term audit-event retention to support 7-year GDPR access logs.
/// </summary>
public sealed class SpeakingComplianceOptions
{
    /// <summary>
    /// How long uploaded learner speaking audio is retained. Default
    /// 365 days. Set to zero or negative to disable sweeping.
    ///
    /// Used by the legacy <see cref="OetLearner.Api.Services.Speaking.SpeakingAudioRetentionWorker"/>
    /// sweep over <see cref="OetLearner.Api.Domain.Attempt.AudioObjectKey"/>.
    /// </summary>
    public int AudioRetentionDays { get; set; } = 365;

    /// <summary>
    /// Phase 7: default retention applied to a <c>SpeakingRecording</c>
    /// row's <c>RetentionExpiresAt</c> when no tutor review has been
    /// recorded. Defaults to 90 days (the GDPR-friendly minimum the
    /// plan recommends). Set to zero/negative to disable sweeping of
    /// <c>SpeakingRecording</c> blobs.
    /// </summary>
    public int RetentionDaysDefault { get; set; } = 90;

    /// <summary>
    /// Phase 7: extended retention when a session has at least one
    /// submitted <c>SpeakingTutorAssessment</c>. Defaults to 365 days so
    /// the calibration pipeline can re-score it later.
    /// </summary>
    public int RetentionDaysWhenTutorReviewed { get; set; } = 365;

    /// <summary>
    /// Phase 7: how long <see cref="OetLearner.Api.Domain.AuditEvent"/>
    /// rows tied to speaking recordings are retained. Default 7 years
    /// (2555 days) to meet medical-education + GDPR audit-trail norms.
    /// </summary>
    public int AuditLogRetentionDays { get; set; } = 2555;

    /// <summary>
    /// Consent text shown immediately before recording starts. Surfaces
    /// in the device-check screen and on the speaking task page.
    /// </summary>
    public string ConsentText { get; set; } =
        "Your voice is processed during the session to provide AI-powered "
        + "speaking practice and feedback. Recordings are kept for a limited "
        + "period. A recording may be selected, with your identity hidden from "
        + "the reviewer, as an example for quality assurance and calibration of "
        + "our AI grader; selected recordings are kept for up to 365 days and "
        + "may be listened to by qualified expert reviewers for that purpose "
        + "only. You can delete a recording at any time, which also removes it "
        + "from review.";

    /// <summary>
    /// Phase 7: version code stored on every audio recording consent
    /// row. Bump when the consent body changes meaningfully so the
    /// learner can be asked to re-accept.
    /// </summary>
    public string CurrentConsentVersion { get; set; } = SpeakingConsentVersions.Calibration;

    /// <summary>
    /// Phase 7: version code stored on every live-video-with-tutor
    /// consent row. Separate from the general recording consent
    /// because the tutor sees the learner's face — an additional
    /// disclosure required by some privacy regimes.
    /// </summary>
    public string CurrentLiveVideoConsentVersion { get; set; } = "live_video_with_tutor.v1";

    /// <summary>
    /// Banner shown on every speaking results page reminding learners
    /// the score is an estimate, not an official OET result.
    /// </summary>
    public string ScoreDisclaimer { get; set; } =
        "Estimated score, not an official OET result. Use this as a "
        + "practice indicator only.";

    /// <summary>
    /// Phase 7: shorter form of the disclaimer that appears inside the
    /// pre-recording consent banner. Phrased to satisfy plan section
    /// G.7 ("Practice estimate only…") verbatim.
    /// </summary>
    public string ScoreDisclaimerCopy { get; set; } =
        "Practice estimate only. This is not an official OET score or result.";
}

/// <summary>
/// Versions of the recording consent copy. <c>recording.v3</c> (owner decision 2026-10-05) is the first
/// wording that tells the learner a recording can be selected for quality assurance / grader calibration
/// and then kept for 365 days; v1 and v2 do not say so. It is a floor: an older configured value
/// (a stale runtime setting) is read as v3, so no deployment can keep serving the old wording.
/// </summary>
public static class SpeakingConsentVersions
{
    public const string Calibration = "recording.v3";

    /// <summary>Versions whose wording does not cover calibration retention. A list so EF can translate "not one of these".</summary>
    public static readonly string[] Legacy = ["recording.v1", "recording.v2"];

    public static string Effective(string? configured)
    {
        var version = configured?.Trim();
        return string.IsNullOrEmpty(version) || Legacy.Contains(version, StringComparer.OrdinalIgnoreCase)
            ? Calibration
            : version;
    }
}
