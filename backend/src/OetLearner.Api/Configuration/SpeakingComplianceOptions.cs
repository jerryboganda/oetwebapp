namespace OetLearner.Api.Configuration;

/// <summary>
/// Wave 7 of docs/SPEAKING-MODULE-PLAN.md - configurable speaking
/// compliance copy and audio retention. Defaults align with the wider
/// platform: audio is treated as sensitive learner content with a long
/// retention window so tutors and learners can re-review old attempts,
/// but operators can shorten this for tighter privacy regimes.
/// </summary>
public sealed class SpeakingComplianceOptions
{
    /// <summary>
    /// How long uploaded learner speaking audio is retained. Default
    /// 365 days. Set to zero or negative to disable sweeping.
    /// </summary>
    public int AudioRetentionDays { get; set; } = 365;

    /// <summary>
    /// Consent text shown immediately before recording starts. Surfaces
    /// in the device-check screen and on the speaking task page.
    /// </summary>
    public string ConsentText { get; set; } =
        "By recording you agree that your audio will be processed by our "
        + "AI evaluator and may be reviewed by a human tutor. Recordings "
        + "are stored securely and deleted after the configured retention "
        + "window.";

    /// <summary>
    /// Banner shown on every speaking results page reminding learners
    /// the score is an estimate, not an official OET result.
    /// </summary>
    public string ScoreDisclaimer { get; set; } =
        "Estimated score, not an official OET result. Use this as a "
        + "practice indicator only.";
}
