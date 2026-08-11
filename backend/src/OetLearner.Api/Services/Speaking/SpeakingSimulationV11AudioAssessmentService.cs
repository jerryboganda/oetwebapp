using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Content;
using OetLearner.Api.Services.Pronunciation;

namespace OetLearner.Api.Services.Speaking;

/// <summary>
/// Performs the acoustic part of the released v1.1 assessment through the
/// explicitly approved phoneme provider. The raw learner audio is opened
/// from the configured storage abstraction and is sent only to that provider;
/// it is never placed in the general grounded-language-model request.
/// </summary>
public sealed class SpeakingSimulationV11AudioAssessmentService(
    LearnerDbContext db,
    IFileStorage storage,
    IPronunciationPhonemeProvider phonemeProvider,
    ILogger<SpeakingSimulationV11AudioAssessmentService> logger)
{
    public async Task<SpeakingSimulationV11AudioAssessmentResult> AssessAsync(
        string userId,
        string professionId,
        IReadOnlyList<SpeakingSimulationV11TurnEvidence> candidateTurns,
        string? approvedProvider,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(approvedProvider))
        {
            return Unavailable("audio_assessment_provider_not_approved");
        }

        if (!string.Equals(approvedProvider, phonemeProvider.Name, StringComparison.OrdinalIgnoreCase))
        {
            return Unavailable("audio_assessment_provider_mismatch");
        }

        if (!await phonemeProvider.IsConfiguredAsync(ct))
        {
            return Unavailable("audio_assessment_provider_unconfigured");
        }

        var recordingIds = candidateTurns
            .Select(x => x.SourceRecordingId)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (recordingIds.Length == 0)
        {
            return Unavailable("candidate_audio_missing");
        }

        var recordingRows = await db.SpeakingRecordings
            .AsNoTracking()
            .Include(x => x.MediaAsset)
            .Where(x => recordingIds.Contains(x.Id) && !x.IsArchived)
            .ToListAsync(ct);
        var recordings = recordingRows.ToDictionary(x => x.Id, StringComparer.Ordinal);

        var scores = new List<double>();
        var sourceIds = new List<string>();
        foreach (var turn in candidateTurns)
        {
            if (string.IsNullOrWhiteSpace(turn.SourceRecordingId)
                || !recordings.TryGetValue(turn.SourceRecordingId, out var recording)
                || recording.MediaAsset is null
                || string.IsNullOrWhiteSpace(recording.MediaAsset.StoragePath))
            {
                return Unavailable("candidate_audio_source_unavailable");
            }

            var storageKey = recording.MediaAsset.StoragePath;
            if (!await storage.ExistsAsync(storageKey, ct))
            {
                return Unavailable("candidate_audio_blob_missing");
            }

            try
            {
                await using var audio = await storage.OpenReadAsync(storageKey, ct);
                var result = await phonemeProvider.AnalyzePhonemesAsync(
                    new AsrRequest(
                        UserId: userId,
                        Audio: audio,
                        AudioMimeType: recording.MimeType,
                        ReferenceText: turn.Text,
                        TargetPhoneme: string.Empty,
                        Locale: "en-GB",
                        TargetRuleId: null,
                        RulebookProfession: professionId,
                        AudioBytes: recording.SizeBytes),
                    ct);

                if (double.IsNaN(result.OverallScore)
                    || double.IsInfinity(result.OverallScore)
                    || result.OverallScore is < 0 or > 100)
                {
                    return Unavailable("audio_assessment_result_invalid");
                }

                scores.Add(result.OverallScore);
                sourceIds.Add(recording.Id);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "v1.1 acoustic assessment failed for recording {RecordingId}.",
                    recording.Id);
                return Unavailable("audio_assessment_provider_error");
            }
        }

        if (scores.Count == 0)
        {
            return Unavailable("audio_assessment_no_result");
        }

        return new SpeakingSimulationV11AudioAssessmentResult(
            IsAvailable: true,
            ProviderName: phonemeProvider.Name,
            Score: Math.Round(scores.Average(), 2, MidpointRounding.AwayFromZero),
            SourceRecordingIds: sourceIds.Distinct(StringComparer.Ordinal).ToArray(),
            IssueCode: null,
            Summary: $"Acoustic intelligibility/pronunciation assessment from {scores.Count} candidate audio turn(s) using {phonemeProvider.Name}.");
    }

    private static SpeakingSimulationV11AudioAssessmentResult Unavailable(string code)
        => new(
            IsAvailable: false,
            ProviderName: null,
            Score: null,
            SourceRecordingIds: Array.Empty<string>(),
            IssueCode: code,
            Summary: "The approved acoustic assessment could not be completed; no intelligibility/pronunciation score is generated.");
}

public sealed record SpeakingSimulationV11AudioAssessmentResult(
    bool IsAvailable,
    string? ProviderName,
    double? Score,
    IReadOnlyList<string> SourceRecordingIds,
    string? IssueCode,
    string Summary);
