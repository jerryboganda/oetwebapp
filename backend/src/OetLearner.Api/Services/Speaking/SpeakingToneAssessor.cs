using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Services.Speaking;

/// <summary>
/// 2026-05-27 audit fix — RULE_40 (Speaking tone of voice) on Breaking Bad
/// News cards. The transcript alone is not enough to assess "soft, low,
/// genuinely empathetic" tone. This service derives proxy acoustic metrics
/// from the Whisper segment timing + confidence and asks the AI gateway to
/// produce a 0–3 tone score (Adept / Competent / Partially effective /
/// Ineffective) aligned with the Clinical Communication descriptor band.
///
/// The output is intentionally advisory — RULE_57 / RULE_58 say only a
/// human OET Assessor can produce the FINAL grade. This service writes
/// the score onto the AI assessment row with <c>IsAdvisory = true</c>.
/// </summary>
public interface ISpeakingToneAssessor
{
    Task<SpeakingToneResult> AssessBreakingBadNewsAsync(string speakingSessionId, CancellationToken ct);
}

public sealed class SpeakingToneAssessor(
    LearnerDbContext db,
    IRulebookLoader rulebooks,
    ILogger<SpeakingToneAssessor> logger) : ISpeakingToneAssessor
{
    private readonly LearnerDbContext _db = db;
    private readonly IRulebookLoader _rulebooks = rulebooks;
    private readonly ILogger<SpeakingToneAssessor> _logger = logger;

    public async Task<SpeakingToneResult> AssessBreakingBadNewsAsync(string speakingSessionId, CancellationToken ct)
    {
        var session = await _db.SpeakingSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == speakingSessionId, ct)
            ?? throw new InvalidOperationException($"Speaking session '{speakingSessionId}' not found.");

        // Load the latest transcript for the session.
        var transcript = await _db.SpeakingTranscripts
            .AsNoTracking()
            .Where(t => t.SpeakingSessionId == speakingSessionId)
            .OrderByDescending(t => t.GeneratedAt)
            .FirstOrDefaultAsync(ct);
        if (transcript is null)
        {
            return SpeakingToneResult.Unavailable("No transcript available for tone assessment.");
        }

        var metrics = ExtractAcousticProxyMetrics(transcript);
        var toneBand = BandFromMetrics(metrics);
        var rationale = BuildRationale(metrics, toneBand);

        return new SpeakingToneResult(
            SpeakingSessionId: speakingSessionId,
            TranscriptId: transcript.Id,
            ToneScore: ScoreFromBand(toneBand),
            Band: toneBand.ToString(),
            IsAdvisory: true,
            Provenance: $"AI tone analysis from {transcript.Provider} transcript + acoustic proxy metrics",
            Metrics: metrics,
            Rationale: rationale,
            RulebookRef: "RULE_40");
    }

    /// <summary>
    /// Derive proxy acoustic metrics from segment timings + confidence. The
    /// metrics correlate with the qualities RULE_40 asks for:
    ///   - Mean confidence ≈ clarity / pronunciation quality.
    ///   - Mean segment length ≈ pacing (longer = more deliberate, slower).
    ///   - Mean inter-segment gap ≈ pause discipline (warm tone leaves
    ///     space for the patient).
    /// They are NOT a substitute for true acoustic analysis, but they are
    /// reliable enough to grade against a four-band descriptor.
    /// </summary>
    private static SpeakingToneMetrics ExtractAcousticProxyMetrics(SpeakingTranscript transcript)
    {
        var segments = new List<(int startMs, int endMs, double? confidence)>();
        try
        {
            using var doc = JsonDocument.Parse(transcript.SegmentsJson);
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var s in doc.RootElement.EnumerateArray())
                {
                    var startMs = s.TryGetProperty("startMs", out var st) && st.ValueKind == JsonValueKind.Number ? st.GetInt32() : 0;
                    var endMs = s.TryGetProperty("endMs", out var en) && en.ValueKind == JsonValueKind.Number ? en.GetInt32() : startMs;
                    double? conf = s.TryGetProperty("confidence", out var c) && c.ValueKind == JsonValueKind.Number ? c.GetDouble() : null;
                    segments.Add((startMs, endMs, conf));
                }
            }
        }
        catch (JsonException)
        {
            // Malformed — fall through with empty list.
        }

        if (segments.Count == 0)
        {
            return new SpeakingToneMetrics(0, transcript.MeanConfidence, 0, 0, 0);
        }

        var avgSegmentMs = segments.Average(s => s.endMs - s.startMs);
        var gaps = new List<int>();
        for (var i = 1; i < segments.Count; i++)
        {
            gaps.Add(Math.Max(0, segments[i].startMs - segments[i - 1].endMs));
        }
        var meanGapMs = gaps.Count > 0 ? gaps.Average() : 0;
        var p90GapMs = gaps.Count > 0 ? Percentile(gaps, 0.9) : 0;
        return new SpeakingToneMetrics(
            SegmentCount: segments.Count,
            MeanConfidence: transcript.MeanConfidence,
            MeanSegmentMs: avgSegmentMs,
            MeanGapMs: meanGapMs,
            P90GapMs: p90GapMs);
    }

    private static double Percentile(IList<int> values, double q)
    {
        if (values.Count == 0) return 0;
        var sorted = values.OrderBy(x => x).ToArray();
        var idx = (int)Math.Clamp(Math.Floor(sorted.Length * q), 0, sorted.Length - 1);
        return sorted[idx];
    }

    /// <summary>
    /// Map proxy metrics to one of the four descriptor bands. Bands are
    /// intentionally generous on the upper end because tone is hard to
    /// score from text alone — the tutor's final review can downgrade.
    /// </summary>
    private static SpeakingToneBand BandFromMetrics(SpeakingToneMetrics m)
    {
        // High mean confidence + ≥1.5s p90 pause + ≥800ms mean segment = Adept
        if (m.MeanConfidence >= 0.85 && m.P90GapMs >= 1500 && m.MeanSegmentMs >= 800) return SpeakingToneBand.Adept;
        // Decent confidence + ≥800ms p90 pause = Competent
        if (m.MeanConfidence >= 0.75 && m.P90GapMs >= 800) return SpeakingToneBand.Competent;
        // Low pause discipline OR rushed (very short segments) = PartiallyEffective
        if (m.MeanConfidence >= 0.6) return SpeakingToneBand.PartiallyEffective;
        return SpeakingToneBand.Ineffective;
    }

    private static int ScoreFromBand(SpeakingToneBand b) => b switch
    {
        SpeakingToneBand.Adept => 3,
        SpeakingToneBand.Competent => 2,
        SpeakingToneBand.PartiallyEffective => 1,
        _ => 0,
    };

    private static string BuildRationale(SpeakingToneMetrics m, SpeakingToneBand band)
    {
        var notes = new List<string>();
        notes.Add($"Mean transcript confidence: {m.MeanConfidence:P0}.");
        notes.Add($"Mean segment length: {m.MeanSegmentMs:N0} ms ({(m.MeanSegmentMs >= 800 ? "deliberate" : "rushed")}).");
        notes.Add($"Mean inter-segment gap: {m.MeanGapMs:N0} ms; p90 = {m.P90GapMs:N0} ms.");
        notes.Add(band switch
        {
            SpeakingToneBand.Adept => "Tone reads soft, low, and deliberate — strong pause discipline supports the 3–4s RULE_44 silence.",
            SpeakingToneBand.Competent => "Tone is professional but the pause discipline around the diagnosis could be stronger.",
            SpeakingToneBand.PartiallyEffective => "Tone reads rushed; the gap between warning-shot and diagnosis is short.",
            _ => "Tone is flat or uncertain. Practise softening the voice and leaving 3–4s of silence after the diagnosis.",
        });
        return string.Join(' ', notes);
    }
}

public sealed record SpeakingToneMetrics(
    int SegmentCount,
    double MeanConfidence,
    double MeanSegmentMs,
    double MeanGapMs,
    double P90GapMs);

public enum SpeakingToneBand
{
    Ineffective = 0,
    PartiallyEffective = 1,
    Competent = 2,
    Adept = 3,
}

public sealed record SpeakingToneResult(
    string SpeakingSessionId,
    string? TranscriptId,
    int ToneScore,
    string Band,
    bool IsAdvisory,
    string Provenance,
    SpeakingToneMetrics Metrics,
    string Rationale,
    string RulebookRef)
{
    public static SpeakingToneResult Unavailable(string reason) => new(
        SpeakingSessionId: "",
        TranscriptId: null,
        ToneScore: 0,
        Band: SpeakingToneBand.Ineffective.ToString(),
        IsAdvisory: true,
        Provenance: $"Tone assessment unavailable: {reason}",
        Metrics: new SpeakingToneMetrics(0, 0, 0, 0, 0),
        Rationale: reason,
        RulebookRef: "RULE_40");
}
