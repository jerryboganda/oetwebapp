using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Ai;
using OetLearner.Api.Services.AiManagement;
using OetLearner.Api.Services.Content;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Services.Speaking;

/// <summary>What the audio judge made of the candidate's recording. <see cref="Unavailable"/> always carries a reason.</summary>
public sealed record SpeakingAudioEvidence
{
    public const string StatusAudio = "audio";
    public const string StatusUnavailable = "unavailable";

    /// <summary><c>audio</c> (a verified judgement) or <c>unavailable</c> (grading goes on from the transcript, labelled).</summary>
    public required string Status { get; init; }

    /// <summary>Why the audio could not be used: a stable machine code (see <see cref="SpeakingAudioEvidenceService.ReasonText"/>).</summary>
    public string? Reason { get; init; }

    public int? IntelligibilityScore { get; init; }
    public string? IntelligibilityRationale { get; init; }
    public string AudioQuality { get; init; } = "unknown";
    public bool PatientVoiceBleed { get; init; }
    public string Confidence { get; init; } = "low";
    public IReadOnlyList<SpeakingAudioObservation> Observations { get; init; } = [];
    public SpeakingFluencyEvidence? Fluency { get; init; }

    /// <summary>What the model said it heard in the first words. Used to verify the judgement and by the release probe;
    /// never stored with a grade (it is the candidate's own speech).</summary>
    public string? HeardOpening { get; init; }

    /// <summary>The audio model that listened (as the provider reported it).</summary>
    public string? Model { get; init; }

    public int ClipCount { get; init; }
    public int DurationMs { get; init; }

    public bool IsAudio => Status == StatusAudio;

    public static SpeakingAudioEvidence Unavailable(string reason) => new() { Status = StatusUnavailable, Reason = reason };
}

/// <summary>Fluency evidence heard in the audio. The grader's Fluency score stays its own; this only informs it.</summary>
public sealed record SpeakingFluencyEvidence(
    int? SpeechRateWpm,
    int LongPauses,
    int HesitationCount,
    int FillerCount,
    int RestartCount,
    IReadOnlyList<string> Observations);

/// <param name="SegmentsJson">The candidate transcript the grader reads (leading connection-check chatter already removed).</param>
/// <param name="FreeSampleGrant">Same value the grade call uses, so a funded session is never refused by the plan gate.</param>
public sealed record SpeakingAudioAssessRequest(
    string SessionId,
    string? UserId,
    string ProfessionId,
    string CardToken,
    string SegmentsJson,
    bool FreeSampleGrant,
    AiAssessmentContext Context);

/// <summary>The release probe's finding for one uploaded clip.</summary>
/// <param name="Evidence">The same verified judgement a real grade would get (or why there is none).</param>
/// <param name="HeardOpening">What the model said it heard in the first words, whether or not it was trusted.</param>
/// <param name="OpeningSimilarity">Shared share (0..1) of the first twelve words between what it heard and what was said.</param>
public sealed record SpeakingAudioProbeResult(
    SpeakingAudioEvidence Evidence, string? HeardOpening, double OpeningSimilarity, int DurationMs, int LatencyMs);

public interface ISpeakingAudioEvidenceService
{
    /// <summary>True when the admin has switched the audio stage on for real grades.</summary>
    Task<bool> IsEnabledAsync(CancellationToken ct);

    /// <summary>Runs the audio stage. Never throws for an audio problem: the result says "unavailable" and why.</summary>
    Task<SpeakingAudioEvidence> AssessAsync(SpeakingAudioAssessRequest request, CancellationToken ct);

    /// <summary>
    /// The release probe: one uploaded clip and the phrase that was actually said in it, judged exactly as a real
    /// grade would be. Unlike <see cref="AssessAsync"/> it lets a transcoder or provider failure through, so the
    /// admin sees what is wrong. Runs on the platform account (no learner, no plan gate).
    /// </summary>
    Task<SpeakingAudioProbeResult> ProbeAsync(Stream audio, string mimeType, string spokenPhrase, CancellationToken ct);
}

/// <summary>
/// The acoustic half of Speaking grading (owner spec 4 Oct 2026). The candidate's stored clips are joined into one
/// mp3 and an OpenAI audio-chat model (provider row <c>openai-audio</c>) judges Intelligibility from the SOUND:
///
/// <list type="bullet">
///   <item>The model is never sent the transcript, so it cannot read the answer off the text. It is asked what it
///     hears in the first words and that is checked against the first candidate turn of the transcript; a mismatch
///     (silence, the wrong audio, a made-up judgement) discards the audio result.</item>
///   <item>It is pinned to its own provider row and never goes through the grade chain (the Claude Max route is
///     untouched, RULE MAX-ALWAYS-ON).</item>
///   <item>Every failure becomes <see cref="SpeakingAudioEvidence.Unavailable"/>. Grading never fails because of audio.</item>
/// </list>
/// </summary>
public sealed class SpeakingAudioEvidenceService(
    LearnerDbContext db,
    IFileStorage storage,
    ISpeakingAudioTranscoder transcoder,
    IAiGatewayService gateway,
    IOptions<SpeakingAudioAssessmentOptions>? options = null,
    ILogger<SpeakingAudioEvidenceService>? logger = null,
    IRemoteSpeakingJoin? remoteJoin = null) : ISpeakingAudioEvidenceService
{
    public const string PromptTemplateId = "speaking.audio_assess.v1";

    /// <summary>The stage version stored in the grader version: the model that listened decides it.</summary>
    public static string StageVersion(string? model) => $"audio-openai.v1:{(string.IsNullOrWhiteSpace(model) ? "default" : model.Trim())}";

    private const int MinimumDurationMs = 1500;
    private const double MinimumOpeningMatch = 0.4;

    private readonly SpeakingAudioAssessmentOptions _options = options?.Value ?? new SpeakingAudioAssessmentOptions();

    public async Task<bool> IsEnabledAsync(CancellationToken ct)
        => await db.FeatureFlags.AsNoTracking()
            .AnyAsync(f => f.Key == SpeakingAudioAssessmentOptions.FeatureFlagKey && f.Enabled, ct);

    public async Task<SpeakingAudioEvidence> AssessAsync(SpeakingAudioAssessRequest request, CancellationToken ct)
    {
        try
        {
            return await AssessCoreAsync(request, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (SpeakingAudioTranscoderUnavailableException ex)
        {
            logger?.LogWarning(ex, "Speaking audio stage: transcoder unavailable for session {SessionId}.", request.SessionId);
            return SpeakingAudioEvidence.Unavailable("audio_transcoder_unavailable");
        }
        catch (AiQuotaDeniedException ex)
        {
            logger?.LogWarning("Speaking audio stage refused by the AI quota gate for session {SessionId}: {Code}.", request.SessionId, ex.ErrorCode);
            return SpeakingAudioEvidence.Unavailable("ai_refused");
        }
        catch (AiBudgetExhaustedException)
        {
            return SpeakingAudioEvidence.Unavailable("ai_refused");
        }
        catch (OperationCanceledException)
        {
            // The stage's own budget ran out (the caller did not cancel).
            logger?.LogWarning("Speaking audio stage timed out for session {SessionId}.", request.SessionId);
            return SpeakingAudioEvidence.Unavailable("timeout");
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Speaking audio stage failed for session {SessionId}.", request.SessionId);
            return SpeakingAudioEvidence.Unavailable("provider_error");
        }
    }

    private async Task<SpeakingAudioEvidence> AssessCoreAsync(SpeakingAudioAssessRequest request, CancellationToken ct)
    {
        var turns = ReadCandidateTurns(request.SegmentsJson);
        var recordings = await SpeakingAudioClips.LoadAsync(db, request.SessionId, turns, ct);
        if (recordings.Count == 0)
        {
            return SpeakingAudioEvidence.Unavailable("no_audio");
        }

        // A join a helper already prepared for exactly these clips (flag-gated, fail-soft, null when there is none) skips the local
        // ffmpeg run. The local transcoder below is ALWAYS the fallback: the grade never waits for, or depends on, a remote job.
        var join = await TryServePrecomputedJoinAsync(request.SessionId, recordings, ct);
        if (join is null)
        {
            // Every clip is opened from storage and joined; none is kept or written anywhere else.
            var streams = new List<Stream>(recordings.Count);
            try
            {
                var inputs = new List<SpeakingAudioClipInput>(recordings.Count);
                foreach (var recording in recordings)
                {
                    var path = recording.StoragePath;
                    if (string.IsNullOrWhiteSpace(path) || !await storage.ExistsAsync(path, ct))
                    {
                        return SpeakingAudioEvidence.Unavailable("audio_missing_blob");
                    }

                    var stream = await storage.OpenReadAsync(path, ct);
                    streams.Add(stream);
                    inputs.Add(new SpeakingAudioClipInput(stream, recording.MimeType));
                }

                join = await transcoder.JoinToMp3Async(inputs, ct);
            }
            finally
            {
                foreach (var stream in streams) await stream.DisposeAsync();
            }
        }

        if (join.DurationMs < MinimumDurationMs)
        {
            return SpeakingAudioEvidence.Unavailable("audio_too_short");
        }

        var result = await JudgeAsync(
            join, request.ProfessionId, request.CardToken, turns.Count, SpeechSeconds(turns),
            request.UserId, request.FreeSampleGrant, request.Context, ct);

        var firstCandidateText = turns.FirstOrDefault(t => !string.IsNullOrWhiteSpace(t.Text))?.Text;
        // Some turns have a clip and some do not: the judgement covers only part of the candidate's speech.
        var partialCoverage = turns.Any(t => t.RecordingId is null) && turns.Any(t => t.RecordingId is not null);
        return ParseResponse(result.Completion, firstCandidateText, result.ResolvedModel, join.ClipCount, join.DurationMs, partialCoverage);
    }

    public async Task<SpeakingAudioProbeResult> ProbeAsync(Stream audio, string mimeType, string spokenPhrase, CancellationToken ct)
    {
        var started = Stopwatch.GetTimestamp();
        var join = await transcoder.JoinToMp3Async([new SpeakingAudioClipInput(audio, mimeType)], ct);
        if (join.DurationMs < MinimumDurationMs)
        {
            return new SpeakingAudioProbeResult(SpeakingAudioEvidence.Unavailable("audio_too_short"), null, 0, join.DurationMs, 0);
        }

        var result = await JudgeAsync(
            join, "medicine", "role_play", candidateTurns: 1, speechSeconds: Math.Max(1, join.DurationMs / 1000),
            userId: null, freeSampleGrant: false, AiAssessmentContext.Practice, ct);
        var evidence = ParseResponse(result.Completion, spokenPhrase, result.ResolvedModel, join.ClipCount, join.DurationMs, partialCoverage: false);
        return new SpeakingAudioProbeResult(
            evidence,
            evidence.HeardOpening,
            string.IsNullOrWhiteSpace(evidence.HeardOpening) ? 0 : OpeningSimilarity(evidence.HeardOpening, spokenPhrase),
            join.DurationMs,
            (int)Stopwatch.GetElapsedTime(started).TotalMilliseconds);
    }

    /// <summary>The one audio-chat call: the joined clips, and a prompt that names no transcript word.</summary>
    private async Task<AiGatewayResult> JudgeAsync(
        SpeakingAudioJoin join, string professionId, string cardToken, int candidateTurns, int speechSeconds,
        string? userId, bool freeSampleGrant, AiAssessmentContext context, CancellationToken ct)
    {
        var prompt = gateway.BuildGroundedPrompt(new AiGroundingContext
        {
            Kind = RuleKind.Speaking,
            Profession = ParseProfession(professionId),
            Task = AiTaskMode.Score,
            CardType = cardToken,
        });

        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(_options.TimeoutSeconds, 10, 600)));
        return await gateway.CompleteAsync(new AiGatewayRequest
        {
            Prompt = prompt,
            // The model hears the clips; it is NEVER given the transcript text.
            UserInput = BuildUserPrompt(join.ClipCount, candidateTurns, speechSeconds, Guid.NewGuid().ToString("N")),
            Provider = AiProviderRegistry.SpeakingAudioProviderCode,
            Model = _options.Model?.Trim() ?? string.Empty,
            Temperature = 0,
            MaxTokens = 1500,
            FeatureCode = AiFeatureCodes.SpeakingAudioAssess,
            PromptTemplateId = PromptTemplateId,
            UserId = userId,
            FreeSampleGrant = freeSampleGrant,
            AssessmentContext = context,
            AudioAttachments = [new AiProviderAudioAttachment { MimeType = "audio/mpeg", Data = join.Mp3 }],
        }, budget.Token);
    }

    // ── What the candidate said, and which clip carries it ───────────────────────────────

    internal sealed record CandidateTurn(string Text, int StartMs, int EndMs, string? RecordingId);

    /// <summary>The candidate's turns in order. Reads the stored segment JSON leniently (any property casing).</summary>
    internal static IReadOnlyList<CandidateTurn> ReadCandidateTurns(string? segmentsJson)
    {
        var turns = new List<CandidateTurn>();
        if (string.IsNullOrWhiteSpace(segmentsJson)) return turns;

        // Callers normally pass already-stripped segments (the classic grader does); this strip is the
        // safety net for the probe and calibration callers, so a clip whose whole turn is connection
        // chatter never reaches the joined audio. Mirrors SpeakingTranscriptEvidence: only LEADING chatter
        // goes, and the zone ends at the first segment carrying real speech, whatever its speaker.
        var leadingChatter = true;
        try
        {
            using var document = JsonDocument.Parse(segmentsJson);
            if (document.RootElement.ValueKind != JsonValueKind.Array) return turns;
            foreach (var segment in document.RootElement.EnumerateArray())
            {
                if (segment.ValueKind != JsonValueKind.Object) continue;
                var text = ReadString(segment, "text") ?? string.Empty;
                if (leadingChatter)
                {
                    text = SpeakingTranscriptEvidence.StripLeadingChatterText(text);
                    if (text.Length == 0) continue;
                    leadingChatter = false;
                }

                var speaker = ReadString(segment, "speaker");
                if (!string.Equals(speaker, "candidate", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(speaker, "learner", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var recordingId = ReadString(segment, "sourceRecordingId");
                turns.Add(new CandidateTurn(
                    text,
                    ReadInt(segment, "startMs"),
                    ReadInt(segment, "endMs"),
                    string.IsNullOrWhiteSpace(recordingId) ? null : recordingId.Trim()));
            }
        }
        catch (JsonException)
        {
            // An unreadable transcript simply has no clip map: the stage falls back to the session recording.
        }

        return turns;
    }

    /// <summary>
    /// The join a remote helper prepared for exactly these clips, or null (flag off, no job, a different clip list, anything doubtful).
    /// Never throws for a remote problem and never waits: a hit only saves the local ffmpeg run.
    /// </summary>
    private async Task<SpeakingAudioJoin?> TryServePrecomputedJoinAsync(
        string sessionId, IReadOnlyList<SpeakingClipRow> clips, CancellationToken ct)
    {
        if (remoteJoin is null) return null;

        var shas = new List<string>(clips.Count);
        foreach (var clip in clips)
        {
            // A clip with no known hash cannot be matched to a precomputed join: the local path decides.
            if (clip.Sha256 is not { } sha) return null;
            shas.Add(sha);
        }

        try
        {
            return await remoteJoin.TryServeAsync(sessionId, shas, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Speaking audio stage: the precomputed join of session {SessionId} could not be used; joining locally.", sessionId);
            return null;
        }
    }

    private static int SpeechSeconds(IReadOnlyList<CandidateTurn> turns)
        => (int)Math.Round(turns.Sum(t => Math.Max(0, t.EndMs - t.StartMs)) / 1000.0);

    // ── The request ───────────────────────────────────────────────────────────────────────

    internal static string BuildUserPrompt(int clipCount, int candidateTurns, int speechSeconds, string nonce)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are listening to the CANDIDATE's side of one OET Speaking role-play. The audio is "
            + (clipCount == 1 ? "one recording" : $"{clipCount} short clips")
            + " of the candidate speaking, joined with brief silences between turns. The patient's voice is not part of it.");
        sb.AppendLine($"The candidate speaks in about {Math.Max(candidateTurns, 1)} turn(s), roughly {Math.Max(speechSeconds, 1)} seconds in total.");
        sb.AppendLine();
        sb.AppendLine("Your job here is narrow. Judge only what you can HEAR; do not guess from the likely content of a consultation.");
        sb.AppendLine("1. Write what you hear in the first 8 to 12 words of the audio as \"heardOpening\" (exactly the words spoken). This shows you listened to this recording.");
        sb.AppendLine("2. Judge INTELLIGIBILITY on the official 0-6 band descriptors in the system prompt, the way a listener hears it: how much effort the speech costs a native-English listener, from the pronunciation of individual sounds, word stress, sentence stress, intonation, rhythm and the effect of the speaker's first-language accent. First list what you notice as observations (each one a specific word or feature: a sound replaced by another, stress on the wrong syllable, flat or odd intonation, rhythm that is hard to follow, an accent feature that needs extra concentration). Then choose the band from those observations: 6 only when you noticed nothing that costs the listener any effort and the prosody is used effectively; 5 when you noticed a few errors or a noticeable accent but understanding was never strained; 4 when you had to concentrate at times; 3 or lower when it was hard to follow or caused serious strain. Do not give credit just because you managed to work out the words: being understood is not the same as being easy to understand. Judge the SOUND of the speech — not whether the content is right, and not grammar or vocabulary.");
        sb.AppendLine("3. Report fluency EVIDENCE you can hear (speech rate, long pauses of about two seconds or more, hesitations, fillers, restarts). Do not score fluency.");
        sb.AppendLine();
        sb.AppendLine("Return ONLY this JSON object (no markdown, no prose):");
        sb.AppendLine("""
{
  "heardOpening": "",
  "audioUsable": true,
  "audioQuality": "good|fair|poor",
  "patientVoiceBleed": false,
  "intelligibility": {
    "observations": [ { "clip": 1, "approxSecond": 0, "issue": "", "example": "" } ],
    "rationale": "",
    "score": 0
  },
  "fluency": {
    "speechRateWpm": 0,
    "longPauses": 0,
    "hesitationCount": 0,
    "fillerCount": 0,
    "restartCount": 0,
    "observations": [ "" ]
  },
  "confidence": "low|medium|high"
}
""");
        sb.AppendLine("Rules:");
        sb.AppendLine("  * `score` is a whole number from 0 to 6, written last: it must follow from the observations and the rationale above it.");
        sb.AppendLine("  * `rationale` is 1-3 plain sentences a candidate can read, saying what in the sound earned that band. Never write rule IDs or internal codes.");
        sb.AppendLine("  * At most 6 intelligibility observations; each names the clip (1-based), the approximate second within the audio, the sound or word affected, and, where useful, how it was said.");
        sb.AppendLine("  * If the audio is silent, only noise, or not speech, set `audioUsable` to false.");
        sb.AppendLine("  * If you can hear a second speaker (the patient) in the audio, set `patientVoiceBleed` to true and judge only the candidate's voice.");
        sb.AppendLine("  * `confidence` is how reliable your judgement is given the recording quality.");
        sb.AppendLine($"(Request reference, ignore: {nonce})");
        return sb.ToString();
    }

    // ── The answer ────────────────────────────────────────────────────────────────────────

    /// <summary>Parses and verifies the model's reply. Pure: unit-tested directly.</summary>
    internal static SpeakingAudioEvidence ParseResponse(
        string? completion, string? firstCandidateText, string? model, int clipCount, int durationMs, bool partialCoverage)
    {
        var json = AiProviderPayloadBuilder.ExtractFirstJsonValue(completion);
        if (json is null) return SpeakingAudioEvidence.Unavailable("parse_error");

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return SpeakingAudioEvidence.Unavailable("parse_error");

            if (root.TryGetProperty("audioUsable", out var usable) && usable.ValueKind == JsonValueKind.False)
            {
                return SpeakingAudioEvidence.Unavailable("audio_unusable");
            }

            // The model was not given the transcript, so what it says it heard must match the transcript's own
            // opening. If it does not, it did not listen to this audio and nothing it judged can be trusted.
            var heard = ReadString(root, "heardOpening");
            if (!string.IsNullOrWhiteSpace(firstCandidateText)
                && (string.IsNullOrWhiteSpace(heard) || !OpeningMatches(heard, firstCandidateText)))
            {
                return SpeakingAudioEvidence.Unavailable("audio_unverified") with { HeardOpening = heard };
            }

            if (!root.TryGetProperty("intelligibility", out var intelligibility)
                || intelligibility.ValueKind != JsonValueKind.Object
                || !TryReadScore(intelligibility, "score", out var score))
            {
                return SpeakingAudioEvidence.Unavailable("parse_error");
            }

            var quality = ReadString(root, "audioQuality")?.Trim().ToLowerInvariant() switch
            {
                "good" => "good",
                "fair" => "fair",
                "poor" => "poor",
                _ => "unknown",
            };
            var bleed = root.TryGetProperty("patientVoiceBleed", out var bleedElement) && bleedElement.ValueKind == JsonValueKind.True;
            var confidence = ReadString(root, "confidence")?.Trim().ToLowerInvariant() switch
            {
                "high" => "high",
                "low" => "low",
                _ => "medium",
            };
            // Weak recording, a second voice or only part of the speech covered: keep the judgement, say it is limited.
            if (quality == "poor" || bleed || partialCoverage) confidence = "low";

            return new SpeakingAudioEvidence
            {
                Status = SpeakingAudioEvidence.StatusAudio,
                IntelligibilityScore = Math.Clamp(score, 0, 6),
                IntelligibilityRationale = Clip(ReadString(intelligibility, "rationale"), 600),
                AudioQuality = quality,
                PatientVoiceBleed = bleed,
                Confidence = confidence,
                Observations = ReadObservations(intelligibility),
                Fluency = root.TryGetProperty("fluency", out var fluency) && fluency.ValueKind == JsonValueKind.Object
                    ? ReadFluency(fluency)
                    : null,
                Model = string.IsNullOrWhiteSpace(model) ? null : model.Trim(),
                ClipCount = clipCount,
                DurationMs = durationMs,
                HeardOpening = heard,
            };
        }
        catch (JsonException)
        {
            return SpeakingAudioEvidence.Unavailable("parse_error");
        }
    }

    /// <summary>True when what the model heard fits the first words of the transcript: enough shared words, or one is
    /// contained in the other (a short "Hello" inside a longer opening).</summary>
    internal static bool OpeningMatches(string heard, string transcriptText)
    {
        var (setA, setB) = OpeningWordSets(heard, transcriptText);
        if (setA.Count == 0 || setB.Count == 0) return false;

        var shared = setA.Intersect(setB).Count();
        if (shared == 0) return false;
        if (shared == setA.Count || shared == setB.Count) return true;
        return shared / (double)setA.Union(setB).Count() >= MinimumOpeningMatch;
    }

    /// <summary>Shared share (0..1, Jaccard) of the first twelve words of the two texts: the probe's measure of whether the
    /// model heard what was said.</summary>
    internal static double OpeningSimilarity(string heard, string transcriptText)
    {
        var (setA, setB) = OpeningWordSets(heard, transcriptText);
        var union = setA.Union(setB).Count();
        return union == 0 ? 0 : setA.Intersect(setB).Count() / (double)union;
    }

    private static (HashSet<string> A, HashSet<string> B) OpeningWordSets(string a, string b)
        => (Words(a).Take(12).ToHashSet(StringComparer.Ordinal), Words(b).Take(12).ToHashSet(StringComparer.Ordinal));

    private static IEnumerable<string> Words(string text)
        => System.Text.RegularExpressions.Regex
            .Replace(text.ToLowerInvariant(), @"[^\p{L}\p{N}\s']", " ")
            .Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.Trim('\''))
            .Where(w => w.Length > 0);

    private static IReadOnlyList<SpeakingAudioObservation> ReadObservations(JsonElement intelligibility)
    {
        var observations = new List<SpeakingAudioObservation>();
        if (!intelligibility.TryGetProperty("observations", out var list) || list.ValueKind != JsonValueKind.Array) return observations;
        foreach (var item in list.EnumerateArray())
        {
            if (observations.Count >= 6) break;
            if (item.ValueKind != JsonValueKind.Object) continue;
            var issue = Clip(ReadString(item, "issue"), 240);
            if (string.IsNullOrWhiteSpace(issue)) continue;
            observations.Add(new SpeakingAudioObservation(
                Math.Max(1, ReadInt(item, "clip")),
                Math.Max(0, ReadInt(item, "approxSecond")),
                issue!,
                Clip(ReadString(item, "example"), 160)));
        }

        return observations;
    }

    private static SpeakingFluencyEvidence ReadFluency(JsonElement fluency)
    {
        var notes = new List<string>();
        if (fluency.TryGetProperty("observations", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in list.EnumerateArray())
            {
                if (notes.Count >= 5) break;
                if (item.ValueKind != JsonValueKind.String) continue;
                var text = Clip(item.GetString(), 240);
                if (!string.IsNullOrWhiteSpace(text)) notes.Add(text!);
            }
        }

        var wpm = ReadInt(fluency, "speechRateWpm");
        return new SpeakingFluencyEvidence(
            wpm is > 0 and < 400 ? wpm : null,
            CountOf(fluency, "longPauses"),
            CountOf(fluency, "hesitationCount"),
            CountOf(fluency, "fillerCount"),
            CountOf(fluency, "restartCount"),
            notes);
    }

    /// <summary>A count the model gave as a number, or as a list whose length is the count.</summary>
    private static int CountOf(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value)) return 0;
        if (value.ValueKind == JsonValueKind.Array) return Math.Min(value.GetArrayLength(), 999);
        return Math.Clamp(ReadInt(element, property), 0, 999);
    }

    private static bool TryReadScore(JsonElement element, string property, out int score)
    {
        score = 0;
        if (!element.TryGetProperty(property, out var value)) return false;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number))
        {
            score = (int)Math.Round(number, MidpointRounding.AwayFromZero);
            return true;
        }

        if (value.ValueKind == JsonValueKind.String
            && double.TryParse(value.GetString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed))
        {
            score = (int)Math.Round(parsed, MidpointRounding.AwayFromZero);
            return true;
        }

        return false;
    }

    private static string? ReadString(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;
        foreach (var candidate in element.EnumerateObject())
        {
            if (string.Equals(candidate.Name, property, StringComparison.OrdinalIgnoreCase)
                && candidate.Value.ValueKind == JsonValueKind.String)
            {
                return candidate.Value.GetString();
            }
        }

        return null;
    }

    private static int ReadInt(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object) return 0;
        foreach (var candidate in element.EnumerateObject())
        {
            if (!string.Equals(candidate.Name, property, StringComparison.OrdinalIgnoreCase)) continue;
            if (candidate.Value.ValueKind == JsonValueKind.Number && candidate.Value.TryGetDouble(out var number))
            {
                return (int)Math.Clamp(Math.Round(number), int.MinValue, int.MaxValue);
            }

            return 0;
        }

        return 0;
    }

    private static string? Clip(string? text, int max)
    {
        var trimmed = text?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed.Length <= max ? trimmed : trimmed[..max].TrimEnd();
    }

    private static ExamProfession ParseProfession(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return ExamProfession.Medicine;
        var normalised = raw
            .Replace("-", "", StringComparison.Ordinal)
            .Replace("_", "", StringComparison.Ordinal)
            .Replace(" ", "", StringComparison.Ordinal);
        return Enum.TryParse<ExamProfession>(normalised, ignoreCase: true, out var parsed)
            ? parsed
            : ExamProfession.Medicine;
    }

    /// <summary>Plain-language wording for a reason code, for the learner-facing label.</summary>
    public static string ReasonText(string? reason) => reason switch
    {
        "no_audio" => "no audio recording was kept for this attempt",
        "audio_missing_blob" or "audio_too_short" or "audio_unusable" => "the recording could not be used",
        "audio_unverified" => "the audio could not be matched to the transcript",
        "audio_transcoder_unavailable" => "the audio could not be prepared for analysis",
        "partial_audio" => "audio evidence was available for only one of the two role-plays",
        "timeout" or "provider_error" or "ai_refused" or "parse_error" => "the audio analysis was not available",
        _ => "audio evidence was not available",
    };
}
