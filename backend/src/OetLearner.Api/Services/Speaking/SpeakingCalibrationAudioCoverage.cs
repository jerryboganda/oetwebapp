using OetLearner.Api.Contracts;

namespace OetLearner.Api.Services.Speaking;

/// <summary>
/// What the stored candidate clips of a performance cover, for the calibration pages. Capture-quality facts only: it reads the transcript
/// the grader reads (candidate turns, leading connection-check chatter removed) and the session's clips, never a grade, so it cannot
/// anchor the expert's blind marks. It makes an incomplete recording visible before anyone marks or grades it.
/// </summary>
internal static class SpeakingCalibrationAudioCoverage
{
    /// <summary>The clips flagged with whether a transcript turn points at them, and the clip/turn/second totals against the speech.</summary>
    public static (IReadOnlyList<SpeakingGraderCalibrationAudioClip> Clips, SpeakingGraderCalibrationAudioCoverage Coverage) Describe(
        string? segmentsJson, IReadOnlyList<SpeakingGraderCalibrationAudioClip> clips)
    {
        var turns = SpeakingAudioEvidenceService.ReadCandidateTurns(segmentsJson);
        var linkedIds = turns
            .Where(t => t.RecordingId is not null)
            .Select(t => t.RecordingId!)
            .ToHashSet(StringComparer.Ordinal);
        var marked = clips.Select(c => c with { Linked = linkedIds.Contains(c.RecordingId) }).ToList();
        var speechMs = turns.Sum(t => Math.Max(0, t.EndMs - t.StartMs));
        return (
            marked,
            new SpeakingGraderCalibrationAudioCoverage(
                Clips: marked.Count,
                LinkedClips: marked.Count(c => c.Linked),
                AudioSeconds: marked.Sum(c => c.DurationSeconds),
                CandidateSpeechSeconds: (int)Math.Round(speechMs / 1000.0),
                CandidateTurns: turns.Count,
                TurnsWithClip: turns.Count(t => t.RecordingId is not null)));
    }
}
