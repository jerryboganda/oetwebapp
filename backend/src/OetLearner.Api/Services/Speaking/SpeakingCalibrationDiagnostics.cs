using System.Text.Json;

namespace OetLearner.Api.Services.Speaking;

/// <summary>One audio verdict, compact: whether Intelligibility was judged from the sound or from the transcript only, why not,
/// and how much audio there was against how much speech. Numbers and codes only, never audio or text.</summary>
public sealed record SpeakingCalibrationAudioCard(
    string Source,
    string? Reason,
    string? Model,
    int Clips,
    int DurationMs,
    int AudioMs,
    int SpeechMs,
    int Turns,
    int TurnsWithClip,
    double? Coverage,
    string Confidence,
    string AudioQuality,
    bool PatientVoiceBleed,
    int? IntelligibilityScore);

/// <summary>The audio verdict of the performance (<c>Combined</c>, as the grader was given it) and of each card behind it.</summary>
public sealed record SpeakingCalibrationAudioDiagnostics(
    SpeakingCalibrationAudioCard? Combined,
    IReadOnlyList<SpeakingCalibrationAudioCard?> Cards);

/// <summary>
/// Everything needed to explain ONE calibration grade beyond its nine scores (owner request 7 Oct 2026): the raw-to-reported
/// mapping version, Claude's own scores before the secondary review, what the reviewer did, and the audio verdict per card.
/// Stored on the grade as JSON and carried into the report. Numbers and codes only.
/// </summary>
public sealed record SpeakingCalibrationGradeDiagnostics(
    string? MappingVersion,
    IReadOnlyDictionary<string, int>? PrimaryScores,
    SpeakingReviewTrace? Review,
    SpeakingCalibrationAudioDiagnostics? Audio);

internal static class SpeakingCalibrationDiagnosticsJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string Build(SpeakingAiAssessmentService.SpeakingGradeOutcome outcome, SpeakingAudioEvidence? audio)
    {
        var diagnostics = new SpeakingCalibrationGradeDiagnostics(
            OetScoring.SpeakingMappingVersion,
            outcome.Review?.PrimaryScores,
            outcome.Review,
            new SpeakingCalibrationAudioDiagnostics(
                audio is null ? null : Card(audio),
                (outcome.CardAudio ?? []).Select(card => card is null ? null : Card(card)).ToList()));
        return JsonSerializer.Serialize(diagnostics, Options);
    }

    /// <summary>The stored diagnostics of a grade; null when none were stored (a grade from before they existed) or unreadable.</summary>
    public static SpeakingCalibrationGradeDiagnostics? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<SpeakingCalibrationGradeDiagnostics>(json, Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static SpeakingCalibrationAudioCard Card(SpeakingAudioEvidence evidence)
        => new(
            evidence.IsAudio ? "audio" : "transcript_only",
            evidence.Reason,
            evidence.Model,
            evidence.ClipCount,
            evidence.DurationMs,
            evidence.AudioMs,
            evidence.SpeechMs,
            evidence.Turns,
            evidence.TurnsWithClip,
            evidence.Coverage,
            evidence.Confidence,
            evidence.AudioQuality,
            evidence.PatientVoiceBleed,
            evidence.IntelligibilityScore);
}
