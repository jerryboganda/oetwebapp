namespace OetLearner.Api.Domain;

/// <summary>
/// Maps every <see cref="AiFeatureCodes"/> value to exactly one reporting bucket of the AI Pipeline
/// Control Center usage dashboard (owner directive 2026-10-09, phase 2). The buckets answer "what did
/// Writing grading / Speaking grading / the reviewers / live voice cost" without asking each caller to
/// remember a second taxonomy: the dashboard groups <see cref="AiUsageRecord.FeatureCode"/> through this
/// map, so the pipeline stages and the usage rows can never drift apart.
///
/// <para>Bucket keys are stable strings (not an enum) so stored JSON/labels never break when new buckets
/// appear. Everything not named here — coach, drills, OCR, STT, admin tooling — is <see cref="Other"/>:
/// it must stay visible so per-provider totals reconcile with /admin/ai-usage.</para>
/// </summary>
public static class AiUsageStageBuckets
{
    public const string WritingGrade = "writing-grade";
    public const string WritingReview = "writing-review";
    public const string SpeakingGrade = "speaking-grade";
    public const string SpeakingReview = "speaking-review";
    public const string SpeakingAudio = "speaking-audio";
    public const string Other = "other";

    /// <summary>Display labels for the six buckets, in dashboard order.</summary>
    public static readonly IReadOnlyList<string> All =
        [WritingGrade, WritingReview, SpeakingGrade, SpeakingReview, SpeakingAudio, Other];

    public static string Label(string bucket) => bucket switch
    {
        WritingGrade => "Writing grading",
        WritingReview => "Writing reviewer",
        SpeakingGrade => "Speaking grading",
        SpeakingReview => "Speaking reviewer",
        SpeakingAudio => "Speaking audio model",
        Other => "Other AI features",
        _ => bucket,
    };

    public static string For(string? featureCode) => featureCode switch
    {
        AiFeatureCodes.WritingGrade or
        AiFeatureCodes.WritingSampleScore or
        AiFeatureCodes.MockFullGrade => WritingGrade,
        AiFeatureCodes.WritingGradeReview => WritingReview,
        AiFeatureCodes.SpeakingGrade => SpeakingGrade,
        AiFeatureCodes.SpeakingGradeReview => SpeakingReview,
        AiFeatureCodes.SpeakingAudioAssess => SpeakingAudio,
        _ => Other,
    };
}
