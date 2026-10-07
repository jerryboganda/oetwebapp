namespace OetLearner.Api.Configuration;

/// <summary>
/// The acoustic half of Speaking grading (owner spec 4 Oct 2026): an OpenAI audio-chat model listens to the
/// candidate's clips and judges Intelligibility. Bound from <c>Speaking:AudioAssessment</c>.
///
/// Whether the stage runs for real grades is NOT configuration: it is the admin feature flag
/// <see cref="FeatureFlagKey"/> (off until the owner has seen the probe pass), so it can be turned on or off
/// without a deploy. The calibration harness and the probe run the stage regardless of the flag.
/// </summary>
public sealed class SpeakingAudioAssessmentOptions
{
    public const string SectionName = "Speaking:AudioAssessment";

    /// <summary>Admin feature flag (<c>/admin/flags</c>): enabled = every new Speaking grade runs the audio stage.</summary>
    public const string FeatureFlagKey = "speaking_audio_assessment";

    /// <summary>Model to request from the <c>openai-audio</c> row. Empty = that row's default model (editable in the admin UI).</summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>Wall-clock budget for the whole audio call. Grading never waits longer than this for audio.</summary>
    public int TimeoutSeconds { get; set; } = 120;

    /// <summary>Longest audio sent to the model, in seconds. Longer joins are cut (an OET role-play is five minutes).</summary>
    public int MaxAudioSeconds { get; set; } = 360;

    /// <summary>Silence inserted between two candidate clips, in milliseconds, so the model hears them as separate turns.</summary>
    public int GapMilliseconds { get; set; } = 600;

    /// <summary>
    /// Smallest share of the candidate's speech time (0..1) the joined clips must cover for the audio to count as Intelligibility
    /// evidence. Below it the grade stays transcript-limited (<c>audio_insufficient_coverage</c>): ten seconds of a three-minute
    /// performance is not a judgement of that performance. Not applied when the transcript has no usable speech timing.
    /// </summary>
    public double MinimumCoverage { get; set; } = 0.5;

    /// <summary>The ffmpeg executable. The API image installs it on the PATH.</summary>
    public string FfmpegPath { get; set; } = "ffmpeg";
}
