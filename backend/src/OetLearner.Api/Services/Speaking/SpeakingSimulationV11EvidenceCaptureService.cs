using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Speaking;

/// <summary>
/// Converts only authoritative transcript/recording rows into v1.1 evidence.
/// It never manufactures a transcript or marks unverifiable audio as passed.
/// </summary>
public sealed class SpeakingSimulationV11EvidenceCaptureService(
    LearnerDbContext db,
    ILogger<SpeakingSimulationV11EvidenceCaptureService> logger)
{
    private static readonly Regex FillerRegex = new(
        @"\b(?:um|uh|er|erm|you know)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex ConsecutiveWordRegex = new(
        @"\b([A-Za-z]+)\s+\1\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public async Task<SpeakingSimulationV11EvidenceCaptureResult> CaptureAsync(
        string speakingSessionId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(speakingSessionId))
        {
            throw ApiException.Validation(
                "SPEAKING_SESSION_ID_REQUIRED",
                "Speaking session id is required.");
        }

        var session = await db.SpeakingSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == speakingSessionId, ct)
            ?? throw ApiException.NotFound(
                "speaking_session_not_found",
                "That Speaking session does not exist.");

        var hasPersonaSnapshot = await db.SpeakingSimulationV11PersonaRuntimeSnapshots
            .AnyAsync(x => x.SpeakingSessionId == speakingSessionId, ct);
        if (!hasPersonaSnapshot)
        {
            return new SpeakingSimulationV11EvidenceCaptureResult(
                SourceTranscriptAvailable: false,
                TurnCount: 0,
                AudioQualityStatus: SpeakingSimulationV11AudioQualityStatus.Pending,
                AudioQualityIssueCode: "v11_persona_snapshot_missing");
        }

        var transcript = await db.SpeakingTranscripts
            .AsNoTracking()
            .Where(x => x.SpeakingSessionId == speakingSessionId && x.IsLatest)
            .OrderByDescending(x => x.GeneratedAt)
            .FirstOrDefaultAsync(ct);

        var recording = await db.SpeakingRecordings
            .AsNoTracking()
            .Where(x => x.SpeakingSessionId == speakingSessionId && !x.IsWarmup)
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(ct);

        var quality = CaptureAudioQuality(session, recording);
        db.SpeakingSimulationV11AudioQualityChecks.Add(quality);

        var timing = await CaptureTimingAsync(session, ct);
        db.SpeakingSimulationV11CardTimingSnapshots.Add(timing);

        if (transcript is null)
        {
            await db.SaveChangesAsync(ct);
            return new SpeakingSimulationV11EvidenceCaptureResult(
                SourceTranscriptAvailable: false,
                TurnCount: 0,
                AudioQualityStatus: quality.Status,
                AudioQualityIssueCode: quality.IssueCode);
        }

        var sourceTurns = ParseSourceTurns(transcript.SegmentsJson);
        if (sourceTurns.Count == 0)
        {
            logger.LogWarning(
                "v1.1 evidence capture found no valid source turns for session {SessionId}, transcript {TranscriptId}.",
                speakingSessionId,
                transcript.Id);
            await db.SaveChangesAsync(ct);
            return new SpeakingSimulationV11EvidenceCaptureResult(
                SourceTranscriptAvailable: true,
                TurnCount: 0,
                AudioQualityStatus: quality.Status,
                AudioQualityIssueCode: quality.IssueCode);
        }

        var existing = await db.SpeakingSimulationV11TurnEvidenceRows
            .AnyAsync(x => x.SpeakingSessionId == speakingSessionId
                && x.SourceTranscriptId == transcript.Id, ct);
        if (!existing)
        {
            var snapshot = await db.SpeakingSimulationV11PersonaRuntimeSnapshots
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.SpeakingSessionId == speakingSessionId, ct);
            var card = await db.RolePlayCards
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == session.RolePlayCardId, ct);
            var script = await db.InterlocutorScripts
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.RolePlayCardId == session.RolePlayCardId, ct);
            var jargonTerms = ParseStringArray(script?.LayLanguageTriggersJson);
            var cardVersion = snapshot?.CardVersion
                ?? (card?.UpdatedAt == default
                    ? "unversioned"
                    : card!.UpdatedAt.UtcDateTime.ToString("O"));

            for (var index = 0; index < sourceTurns.Count; index++)
            {
                var source = sourceTurns[index];
                var previous = index > 0 ? sourceTurns[index - 1] : null;
                var isOverlap = previous is not null
                    && !string.Equals(previous.Speaker, source.Speaker, StringComparison.OrdinalIgnoreCase)
                    && source.StartMs < previous.EndMs
                    && previous.StartMs < source.EndMs;
                var gapPause = previous is not null
                    && string.Equals(previous.Speaker, source.Speaker, StringComparison.OrdinalIgnoreCase)
                    && source.StartMs - previous.EndMs >= 1000;
                var isCandidate = string.Equals(source.Speaker, "candidate", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(source.Speaker, "learner", StringComparison.OrdinalIgnoreCase);
                var durationMs = Math.Max(0, source.EndMs - source.StartMs);

                db.SpeakingSimulationV11TurnEvidenceRows.Add(
                    new SpeakingSimulationV11TurnEvidence
                    {
                        Id = $"spv11_turn_{Guid.NewGuid():N}",
                        SpeakingSessionId = speakingSessionId,
                        SourceTranscriptId = transcript.Id,
                        SourceRecordingId = recording?.Id,
                        CardVersion = cardVersion,
                        TurnNumber = index + 1,
                        Speaker = source.Speaker,
                        StartMs = source.StartMs,
                        EndMs = source.EndMs,
                        Text = source.Text,
                        WordConfidenceJson = source.WordConfidenceJson,
                        AsrProvider = transcript.Provider,
                        IsInterrupted = isCandidate && source.Interrupted,
                        IsOverlap = isOverlap,
                        IsMonologue = isCandidate && durationMs >= 30_000,
                        FillerCount = FillerRegex.Matches(source.Text).Count,
                        PauseCount = gapPause ? 1 : 0,
                        FalseStartCount = CountFalseStarts(source.Text),
                        RepetitionCount = ConsecutiveWordRegex.Matches(source.Text).Count,
                        JargonCount = isCandidate
                            ? CountJargon(source.Text, jargonTerms)
                            : 0,
                        CapturedAt = DateTimeOffset.UtcNow,
                        CreatedAt = DateTimeOffset.UtcNow,
                    });
            }
        }

        await db.SaveChangesAsync(ct);
        return new SpeakingSimulationV11EvidenceCaptureResult(
            SourceTranscriptAvailable: true,
            TurnCount: sourceTurns.Count,
            AudioQualityStatus: quality.Status,
            AudioQualityIssueCode: quality.IssueCode);
    }

    public async Task AttachAssessmentAsync(
        string speakingSessionId,
        string assessmentId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(assessmentId))
        {
            throw ApiException.Validation(
                "SPEAKING_ASSESSMENT_ID_REQUIRED",
                "Assessment id is required.");
        }

        var turns = await db.SpeakingSimulationV11TurnEvidenceRows
            .Where(x => x.SpeakingSessionId == speakingSessionId && x.AssessmentId == null)
            .ToListAsync(ct);
        foreach (var turn in turns)
        {
            turn.AssessmentId = assessmentId;
        }

        var quality = await db.SpeakingSimulationV11AudioQualityChecks
            .Where(x => x.SpeakingSessionId == speakingSessionId && x.AssessmentId == null)
            .ToListAsync(ct);
        foreach (var check in quality)
        {
            check.AssessmentId = assessmentId;
        }

        await db.SaveChangesAsync(ct);
    }

    internal static List<SourceTurn> ParseSourceTurns(string? segmentsJson)
    {
        var turns = new List<SourceTurn>();
        if (string.IsNullOrWhiteSpace(segmentsJson))
        {
            return turns;
        }

        try
        {
            using var document = JsonDocument.Parse(segmentsJson);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return turns;
            }

            foreach (var element in document.RootElement.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object
                    || !element.TryGetProperty("text", out var textElement)
                    || textElement.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var text = textElement.GetString()?.Trim() ?? string.Empty;
                if (text.Length == 0)
                {
                    continue;
                }

                var speaker = element.TryGetProperty("speaker", out var speakerElement)
                    && speakerElement.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(speakerElement.GetString())
                    ? speakerElement.GetString()!.Trim().ToLowerInvariant()
                    : "candidate";
                var startMs = ReadLong(element, "startMs");
                var endMs = Math.Max(startMs, ReadLong(element, "endMs", startMs));
                var interrupted = element.TryGetProperty("interrupted", out var interruptedElement)
                    && interruptedElement.ValueKind == JsonValueKind.True;
                var wordConfidenceJson = element.TryGetProperty("words", out var words)
                    && words.ValueKind == JsonValueKind.Array
                    ? words.GetRawText()
                    : "[]";

                turns.Add(new SourceTurn(
                    speaker,
                    startMs,
                    endMs,
                    text,
                    wordConfidenceJson,
                    interrupted));
            }
        }
        catch (JsonException)
        {
            turns.Clear();
        }

        return turns;
    }

    private async Task<SpeakingSimulationV11CardTimingSnapshot> CaptureTimingAsync(
        SpeakingSession session,
        CancellationToken ct)
    {
        var card = await db.RolePlayCards
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == session.RolePlayCardId, ct);
        var prepSeconds = card?.PrepTimeSeconds is > 0 ? card.PrepTimeSeconds : 180;
        var rolePlaySeconds = card?.RolePlayTimeSeconds is > 0 ? card.RolePlayTimeSeconds : 300;
        var prepDeadline = session.PrepStartedAt?.AddSeconds(prepSeconds);
        var rolePlayDeadline = session.RolePlayStartedAt?.AddSeconds(rolePlaySeconds);
        var now = DateTimeOffset.UtcNow;
        var elapsed = session.EndedAt is { } ended && session.RolePlayStartedAt is { } started
            ? Math.Max(0, (int)(ended - started).TotalSeconds)
            : session.ElapsedSeconds > 0
                ? session.ElapsedSeconds
                : session.RolePlayStartedAt is { } liveStart
                    ? Math.Max(0, (int)(now - liveStart).TotalSeconds)
                    : null;

        var snapshot = await db.SpeakingSimulationV11CardTimingSnapshots
            .FirstOrDefaultAsync(x => x.SpeakingSessionId == session.Id, ct);
        if (snapshot is not null)
        {
            snapshot.PrepStartedAt = session.PrepStartedAt;
            snapshot.ActiveStartedAt = session.RolePlayStartedAt;
            snapshot.EndedAt = session.EndedAt;
            snapshot.PrepSeconds = prepSeconds;
            snapshot.RolePlaySeconds = rolePlaySeconds;
            snapshot.PrepDeadlineAt = prepDeadline;
            snapshot.RolePlayDeadlineAt = rolePlayDeadline;
            snapshot.ServerElapsedSeconds = elapsed;
            snapshot.SourceCardVersion = card?.UpdatedAt == default
                ? "unversioned"
                : card?.UpdatedAt.UtcDateTime.ToString("O");
            snapshot.CapturedAt = now;
            return snapshot;
        }

        return new SpeakingSimulationV11CardTimingSnapshot
        {
            Id = $"spv11_timing_{Guid.NewGuid():N}",
            ExamSessionId = session.ExamSessionId,
            SpeakingSessionId = session.Id,
            CardSlot = string.IsNullOrWhiteSpace(session.ExamSlot) ? "standalone" : session.ExamSlot,
            PrepStartedAt = session.PrepStartedAt,
            ActiveStartedAt = session.RolePlayStartedAt,
            EndedAt = session.EndedAt,
            PrepSeconds = prepSeconds,
            RolePlaySeconds = rolePlaySeconds,
            PrepDeadlineAt = prepDeadline,
            RolePlayDeadlineAt = rolePlayDeadline,
            ServerElapsedSeconds = elapsed,
            ServerAuthoritative = true,
            SourceCardVersion = card?.UpdatedAt == default
                ? "unversioned"
                : card?.UpdatedAt.UtcDateTime.ToString("O"),
            CapturedAt = now,
            CreatedAt = now,
        };
    }

    private static SpeakingSimulationV11AudioQualityCheck CaptureAudioQuality(
        SpeakingSession session,
        SpeakingRecording? recording)
    {
        var now = DateTimeOffset.UtcNow;
        var status = SpeakingSimulationV11AudioQualityStatus.Passed;
        string? issueCode = null;
        var details = new Dictionary<string, object?>
        {
            ["originalAudioRequired"] = true,
            ["metadataOnlyCheck"] = true,
        };

        if (recording is null)
        {
            status = SpeakingSimulationV11AudioQualityStatus.NeedsReview;
            issueCode = "original_audio_missing";
        }
        else if (recording.Kind != SpeakingRecordingKind.Audio)
        {
            status = SpeakingSimulationV11AudioQualityStatus.NeedsReview;
            issueCode = "non_audio_recording";
        }
        else if (recording.IsArchived)
        {
            status = SpeakingSimulationV11AudioQualityStatus.NeedsReview;
            issueCode = "original_audio_archived";
        }
        else if (recording.SizeBytes <= 0 || string.IsNullOrWhiteSpace(recording.Sha256))
        {
            status = SpeakingSimulationV11AudioQualityStatus.NeedsReview;
            issueCode = "audio_integrity_metadata_missing";
        }
        else if (!recording.MimeType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase))
        {
            status = SpeakingSimulationV11AudioQualityStatus.Failed;
            issueCode = "audio_mime_invalid";
        }

        details["issueCode"] = issueCode;
        details["sha256Present"] = !string.IsNullOrWhiteSpace(recording?.Sha256);
        details["durationSeconds"] = recording?.DurationSeconds;
        details["mimeType"] = recording?.MimeType;

        return new SpeakingSimulationV11AudioQualityCheck
        {
            Id = $"spv11_quality_{Guid.NewGuid():N}",
            SpeakingSessionId = session.Id,
            SourceRecordingId = recording?.Id,
            SourceMediaAssetId = recording?.MediaAssetId,
            Status = status,
            OriginalSha256 = recording?.Sha256,
            MimeType = recording?.MimeType,
            SizeBytes = recording?.SizeBytes,
            DurationSeconds = recording?.DurationSeconds,
            IssueCode = issueCode,
            DetailsJson = JsonSerializer.Serialize(details),
            CheckedAt = now,
            CreatedAt = now,
        };
    }

    private static long ReadLong(JsonElement element, string property, long fallback = 0)
        => element.TryGetProperty(property, out var value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt64(out var number)
            ? Math.Max(0, number)
            : fallback;

    private static int CountFalseStarts(string text)
        => text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Count(x => x.EndsWith("-", StringComparison.Ordinal));

    private static int CountJargon(string text, IReadOnlyCollection<string> jargonTerms)
        => jargonTerms.Count(term => text.Contains(term, StringComparison.OrdinalIgnoreCase));

    private static string[] ParseStringArray(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Array
                ? document.RootElement.EnumerateArray()
                    .Where(x => x.ValueKind == JsonValueKind.String)
                    .Select(x => x.GetString()!.Trim())
                    .Where(x => x.Length > 0)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray()
                : [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    internal sealed record SourceTurn(
        string Speaker,
        long StartMs,
        long EndMs,
        string Text,
        string WordConfidenceJson,
        bool Interrupted);
}

public sealed record SpeakingSimulationV11EvidenceCaptureResult(
    bool SourceTranscriptAvailable,
    int TurnCount,
    SpeakingSimulationV11AudioQualityStatus AudioQualityStatus,
    string? AudioQualityIssueCode);
