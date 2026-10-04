using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Speaking;

/// <summary>
/// Speaking grader calibration — the expert side (owner spec 4 Oct 2026). The AI grader keeps a
/// "Provisional" label until its scores have been compared with an OET expert's own marks on real
/// performances. Dr Hesham marks a performance here, blind to the AI score; the calibration run
/// (a later increment) compares the two.
///
/// Rules this service enforces:
/// <list type="bullet">
///   <item>Blind: nothing it returns is read from an AI assessment or any other AI result.</item>
///   <item>Ids only: a sample points at the existing transcript and recordings; nothing is copied.</item>
///   <item>Promotion is the one learner-data write: the performance's audio is kept for
///     <see cref="CalibrationAudioRetention"/> and an audit event says so.</item>
/// </list>
/// </summary>
public sealed partial class SpeakingGraderCalibrationService(
    LearnerDbContext db,
    TimeProvider clock,
    SpeakingAiAssessmentService? assessor = null)
{
    /// <summary>How long a promoted performance's audio is kept (the privacy call recorded in the plan).</summary>
    public static readonly TimeSpan CalibrationAudioRetention = TimeSpan.FromDays(365);

    // Coverage a calibration report needs before it means anything. Proposed thresholds — the owner confirms them.
    public const int RequiredLabelled = 30;
    public const int RequiredPerGrade = 3;
    public const int RequiredNearPassLine = 10;
    public const double RequiredAudioShare = 0.8;
    private const int NearPassLow = 320;
    private const int NearPassHigh = 380;

    private static readonly string[] Grades = ["A", "B", "C+", "C", "D", "E"];

    /// <summary>The nine official criteria in marking order, with their maximum marks.</summary>
    public static readonly IReadOnlyList<SpeakingGraderCalibrationCriterion> Criteria =
    [
        new("intelligibility", "Intelligibility", "linguistic", 6),
        new("fluency", "Fluency", "linguistic", 6),
        new("appropriateness", "Appropriateness of language", "linguistic", 6),
        new("grammarExpression", "Resources of grammar and expression", "linguistic", 6),
        new("relationshipBuilding", "Relationship building", "clinical", 3),
        new("patientPerspective", "Understanding and incorporating the patient's perspective", "clinical", 3),
        new("structure", "Providing structure", "clinical", 3),
        new("informationGathering", "Information gathering", "clinical", 3),
        new("informationGiving", "Information giving", "clinical", 3),
    ];

    // ── Candidates ───────────────────────────────────────────────────────

    /// <summary>Finished AI cards with a usable transcript that have not been promoted yet, newest first.
    /// No learner identity and no AI result.</summary>
    public async Task<IReadOnlyList<SpeakingGraderCalibrationCandidate>> ListCandidatesAsync(int take, CancellationToken ct)
    {
        var limit = Math.Clamp(take, 1, 100);
        var promoted = db.SpeakingGraderCalibrationSamples.Select(s => s.SpeakingSessionId);

        var rows = await db.SpeakingSessions.AsNoTracking()
            .Where(s => s.State == SpeakingSessionState.Finished
                && (s.Mode == SpeakingSessionMode.AiSelfPractice || s.Mode == SpeakingSessionMode.AiExam)
                && !promoted.Contains(s.Id)
                && db.SpeakingTranscripts.Any(t => t.SpeakingSessionId == s.Id
                    && t.IsLatest
                    && t.Provider != SpeakingTranscriptionPipeline.StateFailed
                    && t.Provider != SpeakingTranscriptionPipeline.StateQueued
                    && t.Provider != SpeakingTranscriptionPipeline.StateProcessing))
            .OrderByDescending(s => s.UpdatedAt)
            .Take(limit)
            .Select(s => new
            {
                s.Id,
                s.RolePlayCardId,
                s.ElapsedSeconds,
                s.UpdatedAt,
                HasAudio = db.SpeakingRecordings.Any(r => r.SpeakingSessionId == s.Id && !r.IsArchived && !r.IsWarmup),
            })
            .ToListAsync(ct);

        var cards = await CardsAsync(rows.Select(r => r.RolePlayCardId), ct);
        return rows
            .Select(r => new SpeakingGraderCalibrationCandidate(
                r.Id,
                cards.TryGetValue(r.RolePlayCardId, out var card) ? card.ProfessionId : "",
                cards.TryGetValue(r.RolePlayCardId, out card) ? card.ScenarioTitle : "",
                r.UpdatedAt,
                r.ElapsedSeconds,
                r.HasAudio))
            .ToList();
    }

    // ── Promote ──────────────────────────────────────────────────────────

    /// <summary>Promote a finished AI card for the expert to mark. Pins its transcript, records whether it
    /// has audio, keeps that audio for a year and writes an audit event.</summary>
    public async Task<SpeakingGraderCalibrationSampleRow> PromoteAsync(
        string adminId, string adminName, string? sessionId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw ApiException.Validation("speaking_calibration_session_required", "Choose a performance to promote.");
        }

        var session = await db.SpeakingSessions
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct)
            ?? throw ApiException.NotFound("speaking_calibration_session_not_found", "That performance does not exist.");

        if (session.State != SpeakingSessionState.Finished
            || session.Mode is not (SpeakingSessionMode.AiSelfPractice or SpeakingSessionMode.AiExam))
        {
            throw ApiException.Conflict("speaking_calibration_session_not_eligible",
                "Only a finished AI role-play can be used for calibration.");
        }

        if (await db.SpeakingGraderCalibrationSamples.AnyAsync(s => s.SpeakingSessionId == sessionId, ct))
        {
            throw ApiException.Conflict("speaking_calibration_already_promoted", "That performance is already in the calibration set.");
        }

        var transcript = await db.SpeakingTranscripts.AsNoTracking()
            .Where(t => t.SpeakingSessionId == sessionId
                && t.IsLatest
                && t.Provider != SpeakingTranscriptionPipeline.StateFailed
                && t.Provider != SpeakingTranscriptionPipeline.StateQueued
                && t.Provider != SpeakingTranscriptionPipeline.StateProcessing)
            .Select(t => new { t.Id })
            .FirstOrDefaultAsync(ct)
            ?? throw ApiException.Conflict("speaking_calibration_no_transcript", "That performance has no usable transcript.");

        var card = await db.RolePlayCards.AsNoTracking()
            .Where(c => c.Id == session.RolePlayCardId)
            .Select(c => new { c.Id, c.ProfessionId, c.ScenarioTitle })
            .FirstOrDefaultAsync(ct)
            ?? throw ApiException.NotFound("speaking_calibration_card_not_found", "The role-play card for that performance no longer exists.");

        var recordings = await db.SpeakingRecordings
            .Where(r => r.SpeakingSessionId == sessionId && !r.IsArchived && !r.IsWarmup)
            .ToListAsync(ct);

        var now = clock.GetUtcNow();
        var keepUntil = now + CalibrationAudioRetention;
        foreach (var recording in recordings.Where(r => r.RetentionExpiresAt is null || r.RetentionExpiresAt < keepUntil))
        {
            recording.RetentionExpiresAt = keepUntil;
        }

        var sample = new SpeakingGraderCalibrationSample
        {
            Id = $"spgc_{Guid.NewGuid():N}",
            SpeakingSessionId = sessionId,
            TranscriptId = transcript.Id,
            RolePlayCardId = card.Id,
            ProfessionId = card.ProfessionId,
            HasAudio = recordings.Count > 0,
            Status = SpeakingGraderCalibrationSampleStatus.Pending,
            PromotedById = adminId,
            PromotedAt = now,
            UpdatedAt = now,
        };
        db.SpeakingGraderCalibrationSamples.Add(sample);
        db.AuditEvents.Add(new AuditEvent
        {
            Id = $"audit-{Guid.NewGuid():N}",
            OccurredAt = now,
            ActorId = adminId,
            ActorName = adminName,
            Action = "SpeakingGraderCalibrationSamplePromoted",
            ResourceType = "SpeakingGraderCalibrationSample",
            ResourceId = sample.Id,
            Details = JsonSerializer.Serialize(new
            {
                sessionId,
                clips = recordings.Count,
                audioKeptUntil = recordings.Count > 0 ? keepUntil : (DateTimeOffset?)null,
            }),
        });
        await db.SaveChangesAsync(ct);

        return ToRow(sample, card.ScenarioTitle);
    }

    // ── Overview ─────────────────────────────────────────────────────────

    public async Task<SpeakingGraderCalibrationOverview> GetOverviewAsync(CancellationToken ct)
    {
        var samples = await db.SpeakingGraderCalibrationSamples.AsNoTracking()
            .OrderByDescending(s => s.PromotedAt)
            .ToListAsync(ct);
        var cards = await CardsAsync(samples.Select(s => s.RolePlayCardId), ct);
        var rows = samples
            .Select(s => ToRow(s, cards.TryGetValue(s.RolePlayCardId, out var card) ? card.ScenarioTitle : ""))
            .ToList();
        return new SpeakingGraderCalibrationOverview(BuildCoverage(samples), rows);
    }

    /// <summary>Coverage of the labelled set against what a calibration report needs. Pure: unit-tested directly.</summary>
    public static SpeakingGraderCalibrationCoverage BuildCoverage(IReadOnlyCollection<SpeakingGraderCalibrationSample> samples)
    {
        var labelled = samples
            .Where(s => s.Status == SpeakingGraderCalibrationSampleStatus.Labelled && s.ExpertOverallScaled is not null)
            .ToList();
        var byGrade = Grades.ToDictionary(g => g, _ => 0, StringComparer.Ordinal);
        foreach (var sample in labelled)
        {
            byGrade[OetScoring.OetGradeLetterFromScaled(sample.ExpertOverallScaled!.Value)]++;
        }

        var near = labelled.Count(s => s.ExpertOverallScaled is >= NearPassLow and <= NearPassHigh);
        var audioShare = labelled.Count == 0 ? 0 : labelled.Count(s => s.HasAudio) / (double)labelled.Count;

        var unmet = new List<string>();
        if (labelled.Count < RequiredLabelled)
        {
            unmet.Add($"Mark {RequiredLabelled - labelled.Count} more performance(s): {labelled.Count} of {RequiredLabelled} marked.");
        }

        foreach (var grade in Grades.Where(g => byGrade[g] < RequiredPerGrade))
        {
            unmet.Add($"Grade {grade}: {byGrade[grade]} of {RequiredPerGrade} marked.");
        }

        if (near < RequiredNearPassLine)
        {
            unmet.Add($"Near the pass line ({NearPassLow}-{NearPassHigh}): {near} of {RequiredNearPassLine} marked.");
        }

        if (labelled.Count > 0 && audioShare < RequiredAudioShare)
        {
            unmet.Add($"Audio: {Math.Round(audioShare * 100)}% of marked performances have audio ({Math.Round(RequiredAudioShare * 100)}% needed).");
        }

        return new SpeakingGraderCalibrationCoverage(
            Total: samples.Count,
            Labelled: labelled.Count,
            Pending: samples.Count(s => s.Status == SpeakingGraderCalibrationSampleStatus.Pending),
            Excluded: samples.Count(s => s.Status == SpeakingGraderCalibrationSampleStatus.Excluded),
            LabelledByGrade: byGrade,
            LabelledNearPassLine: near,
            AudioShare: Math.Round(audioShare, 3),
            RequiredLabelled: RequiredLabelled,
            RequiredPerGrade: RequiredPerGrade,
            RequiredNearPassLine: RequiredNearPassLine,
            RequiredAudioShare: RequiredAudioShare,
            MeetsCoverage: unmet.Count == 0,
            Unmet: unmet);
    }

    // ── Blind labelling view ─────────────────────────────────────────────

    /// <summary>The blind view: the card, the transcript the grader would read and the audio clips — no AI value.</summary>
    public async Task<SpeakingGraderCalibrationSampleDetail> GetDetailAsync(string sampleId, CancellationToken ct)
    {
        var sample = await FindSampleAsync(sampleId, tracked: false, ct);

        var card = await db.RolePlayCards.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == sample.RolePlayCardId, ct)
            ?? throw ApiException.NotFound("speaking_calibration_card_not_found", "The role-play card for that performance no longer exists.");

        var segmentsJson = await db.SpeakingTranscripts.AsNoTracking()
            .Where(t => t.Id == sample.TranscriptId)
            .Select(t => t.SegmentsJson)
            .FirstOrDefaultAsync(ct);

        var clips = await db.SpeakingRecordings.AsNoTracking()
            .Where(r => r.SpeakingSessionId == sample.SpeakingSessionId && !r.IsArchived && !r.IsWarmup)
            .OrderBy(r => r.CreatedAt)
            .Select(r => new SpeakingGraderCalibrationAudioClip(r.Id, r.DurationSeconds, r.MimeType))
            .ToListAsync(ct);

        return new SpeakingGraderCalibrationSampleDetail(
            sample.Id,
            StatusCode(sample.Status),
            sample.HasAudio,
            new SpeakingGraderCalibrationCard(
                card.ScenarioTitle,
                card.ProfessionId,
                card.Setting,
                card.CandidateRole,
                card.InterlocutorRole,
                card.Background,
                card.Tasks),
            ReadTranscript(segmentsJson),
            clips,
            Criteria,
            ReadLabel(sample),
            sample.ExcludedReason);
    }

    /// <summary>The storage path and mime type of one of the sample's clips, for the audio stream.</summary>
    public async Task<(string StoragePath, string MimeType)> GetClipAsync(string sampleId, string recordingId, CancellationToken ct)
    {
        var sample = await FindSampleAsync(sampleId, tracked: false, ct);
        var recording = await db.SpeakingRecordings.AsNoTracking()
            .Include(r => r.MediaAsset)
            .FirstOrDefaultAsync(r => r.Id == recordingId
                && r.SpeakingSessionId == sample.SpeakingSessionId
                && !r.IsArchived, ct);
        if (recording?.MediaAsset is null || string.IsNullOrWhiteSpace(recording.MediaAsset.StoragePath))
        {
            throw ApiException.NotFound("speaking_calibration_audio_not_found", "That audio clip is not available.");
        }

        return (recording.MediaAsset.StoragePath, recording.MediaAsset.MimeType);
    }

    // ── Label / exclude ──────────────────────────────────────────────────

    /// <summary>Record the expert's nine criterion scores and overall /500. Re-labelling replaces the earlier marks.</summary>
    public async Task<SpeakingGraderCalibrationSampleRow> LabelAsync(
        string adminId, string sampleId, SpeakingGraderCalibrationLabelRequest request, CancellationToken ct)
    {
        var scores = ValidateLabel(request);
        var sample = await FindSampleAsync(sampleId, tracked: true, ct);
        var now = clock.GetUtcNow();

        sample.ExpertScoresJson = JsonSerializer.Serialize(
            Criteria.ToDictionary(c => c.Code, c => scores[c.Code]));
        sample.ExpertOverallScaled = request.OverallScaled;
        sample.ExpertNotes = (request.Notes ?? string.Empty).Trim();
        sample.ExcludedReason = string.Empty;
        sample.Status = SpeakingGraderCalibrationSampleStatus.Labelled;
        sample.LabelledById = adminId;
        sample.LabelledAt = now;
        sample.UpdatedAt = now;
        await db.SaveChangesAsync(ct);

        return ToRow(sample, await CardTitleAsync(sample.RolePlayCardId, ct));
    }

    /// <summary>Mark a performance unusable (no speech, wrong card, broken audio). It stays for audit and is never reported.</summary>
    public async Task<SpeakingGraderCalibrationSampleRow> ExcludeAsync(
        string sampleId, SpeakingGraderCalibrationExcludeRequest request, CancellationToken ct)
    {
        var reason = (request.Reason ?? string.Empty).Trim();
        if (reason.Length == 0 || reason.Length > 500)
        {
            throw ApiException.Validation("speaking_calibration_reason_required",
                "Say briefly why this performance cannot be used (up to 500 characters).");
        }

        var sample = await FindSampleAsync(sampleId, tracked: true, ct);
        sample.Status = SpeakingGraderCalibrationSampleStatus.Excluded;
        sample.ExcludedReason = reason;
        sample.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);

        return ToRow(sample, await CardTitleAsync(sample.RolePlayCardId, ct));
    }

    /// <summary>Every criterion present and in range, overall in steps of 10 between 0 and 500.</summary>
    public static IReadOnlyDictionary<string, int> ValidateLabel(SpeakingGraderCalibrationLabelRequest request)
    {
        var scores = request.Scores;
        var errors = new List<ApiFieldError>();
        foreach (var criterion in Criteria)
        {
            if (scores is null || !scores.TryGetValue(criterion.Code, out var value))
            {
                errors.Add(new ApiFieldError(criterion.Code, "required", $"{criterion.Label} needs a score."));
            }
            else if (value < 0 || value > criterion.Max)
            {
                errors.Add(new ApiFieldError(criterion.Code, "out_of_range", $"{criterion.Label} is scored 0 to {criterion.Max}."));
            }
        }

        if (request.OverallScaled is not { } overall || overall < 0 || overall > 500 || overall % 10 != 0)
        {
            errors.Add(new ApiFieldError("overallScaled", "invalid", "The overall result is 0 to 500 in steps of 10."));
        }

        if ((request.Notes?.Length ?? 0) > 2000)
        {
            errors.Add(new ApiFieldError("notes", "too_long", "Notes can be up to 2000 characters."));
        }

        if (errors.Count > 0)
        {
            throw ApiException.Validation("speaking_calibration_label_invalid", "Check the marks and try again.", errors);
        }

        return scores!;
    }

    // ── helpers ──────────────────────────────────────────────────────────

    private async Task<SpeakingGraderCalibrationSample> FindSampleAsync(string sampleId, bool tracked, CancellationToken ct)
    {
        var query = tracked ? db.SpeakingGraderCalibrationSamples : db.SpeakingGraderCalibrationSamples.AsNoTracking();
        return await query.FirstOrDefaultAsync(s => s.Id == sampleId, ct)
            ?? throw ApiException.NotFound("speaking_calibration_sample_not_found", "That calibration sample does not exist.");
    }

    private async Task<Dictionary<string, (string ProfessionId, string ScenarioTitle)>> CardsAsync(
        IEnumerable<string> cardIds, CancellationToken ct)
    {
        var ids = cardIds.Distinct().ToList();
        var cards = await db.RolePlayCards.AsNoTracking()
            .Where(c => ids.Contains(c.Id))
            .Select(c => new { c.Id, c.ProfessionId, c.ScenarioTitle })
            .ToListAsync(ct);
        return cards.ToDictionary(c => c.Id, c => (c.ProfessionId, c.ScenarioTitle), StringComparer.Ordinal);
    }

    private async Task<string> CardTitleAsync(string cardId, CancellationToken ct)
        => await db.RolePlayCards.AsNoTracking()
            .Where(c => c.Id == cardId)
            .Select(c => c.ScenarioTitle)
            .FirstOrDefaultAsync(ct) ?? string.Empty;

    private static SpeakingGraderCalibrationSampleRow ToRow(SpeakingGraderCalibrationSample sample, string cardTitle)
        => new(
            sample.Id,
            sample.SpeakingSessionId,
            sample.ProfessionId,
            cardTitle,
            sample.HasAudio,
            StatusCode(sample.Status),
            sample.ExpertOverallScaled,
            sample.ExpertOverallScaled is { } overall ? OetScoring.OetGradeLetterFromScaled(overall) : null,
            sample.PromotedAt,
            sample.LabelledAt);

    private static string StatusCode(SpeakingGraderCalibrationSampleStatus status) => status switch
    {
        SpeakingGraderCalibrationSampleStatus.Labelled => "labelled",
        SpeakingGraderCalibrationSampleStatus.Excluded => "excluded",
        _ => "pending",
    };

    private static SpeakingGraderCalibrationLabel? ReadLabel(SpeakingGraderCalibrationSample sample)
    {
        if (sample.ExpertScoresJson is null || sample.ExpertOverallScaled is not { } overall)
        {
            return null;
        }

        try
        {
            var scores = JsonSerializer.Deserialize<Dictionary<string, int>>(sample.ExpertScoresJson);
            return scores is null ? null : new SpeakingGraderCalibrationLabel(scores, overall, sample.ExpertNotes);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The transcript as the grader reads it: the leading connection-check chatter is not shown.</summary>
    private static IReadOnlyList<SpeakingGraderCalibrationTranscriptLine> ReadTranscript(string? segmentsJson)
    {
        if (string.IsNullOrWhiteSpace(segmentsJson))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(SpeakingTranscriptEvidence.StripConnectivityChatter(segmentsJson));
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var lines = new List<SpeakingGraderCalibrationTranscriptLine>();
            foreach (var segment in document.RootElement.EnumerateArray())
            {
                if (segment.ValueKind != JsonValueKind.Object
                    || !segment.TryGetProperty("text", out var text)
                    || text.ValueKind != JsonValueKind.String
                    || string.IsNullOrWhiteSpace(text.GetString()))
                {
                    continue;
                }

                lines.Add(new SpeakingGraderCalibrationTranscriptLine(
                    segment.TryGetProperty("speaker", out var speaker) && speaker.ValueKind == JsonValueKind.String
                        ? speaker.GetString() ?? ""
                        : "",
                    ReadMs(segment, "startMs"),
                    ReadMs(segment, "endMs"),
                    text.GetString()!.Trim()));
            }

            return lines;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static int ReadMs(JsonElement segment, string property)
        => segment.TryGetProperty(property, out var value) && value.TryGetDouble(out var ms) ? (int)Math.Max(0, ms) : 0;
}
