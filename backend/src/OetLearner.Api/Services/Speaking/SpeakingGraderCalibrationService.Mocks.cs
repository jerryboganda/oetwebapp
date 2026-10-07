using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Speaking;

/// <summary>
/// The Full Mock side of grader calibration (owner request 7 Oct 2026). The combined grader
/// (<c>speaking.score.v3-combined</c>) makes ONE judgement over a whole two-card test, so it is calibrated against
/// ONE expert mark of the whole test. Same rules as a card sample: blind by construction (nothing here reads an AI
/// assessment), ids only, promotion keeps BOTH cards' audio for <see cref="CalibrationAudioRetention"/> and writes
/// an audit event, and a sample inside a running mock-scope calibration run is frozen.
/// </summary>
public sealed partial class SpeakingGraderCalibrationService
{
    // ── Candidates ───────────────────────────────────────────────────────

    /// <summary>Completed two-card AI exams that have not been promoted yet, newest first. Both cards must have a
    /// usable latest transcript and the learner must have held a calibration-covered recording consent when the
    /// exam was recorded. No learner identity and no AI result.</summary>
    public async Task<IReadOnlyList<SpeakingGraderCalibrationMockCandidate>> ListMockCandidatesAsync(int take, CancellationToken ct)
    {
        var limit = Math.Clamp(take, 1, 100);
        var promoted = db.SpeakingGraderCalibrationMockSamples.Select(s => s.SpeakingExamId);

        var exams = await db.SpeakingExamSessions.AsNoTracking()
            .Where(e => e.State == SpeakingExamState.Completed
                && e.Mode == SpeakingExamMode.Ai
                && e.SessionAId != null
                && e.SessionBId != null
                && !promoted.Contains(e.Id)
                && CalibrationConsents().Any(c => c.UserId == e.UserId
                    && c.AcceptedAt <= (e.ActiveAStartedAt ?? e.IntroStartedAt ?? e.CompletedAt ?? e.UpdatedAt)))
            .OrderByDescending(e => e.UpdatedAt)
            .Take(limit)
            .Select(e => new { e.Id, e.ProfessionId, e.SessionAId, e.SessionBId, e.CompletedAt, e.UpdatedAt })
            .ToListAsync(ct);

        var candidates = new List<SpeakingGraderCalibrationMockCandidate>(exams.Count);
        foreach (var exam in exams)
        {
            var sessionIds = new[] { exam.SessionAId!, exam.SessionBId! };
            var sessions = await db.SpeakingSessions.AsNoTracking()
                .Where(s => sessionIds.Contains(s.Id))
                .Select(s => new { s.Id, s.RolePlayCardId })
                .ToListAsync(ct);
            if (sessions.Count != 2) continue;
            var sessionA = sessions.First(s => s.Id == exam.SessionAId);
            var sessionB = sessions.First(s => s.Id == exam.SessionBId);

            var usable = await db.SpeakingTranscripts.AsNoTracking()
                .Where(t => sessionIds.Contains(t.SpeakingSessionId)
                    && t.IsLatest
                    && t.SegmentsJson != "[]"
                    && t.Provider != SpeakingTranscriptionPipeline.StateFailed
                    && t.Provider != SpeakingTranscriptionPipeline.StateQueued
                    && t.Provider != SpeakingTranscriptionPipeline.StateProcessing)
                .Select(t => t.SpeakingSessionId)
                .Distinct()
                .ToListAsync(ct);
            if (usable.Count < 2) continue;

            var cards = await CardsAsync(new[] { sessionA.RolePlayCardId, sessionB.RolePlayCardId }, ct);
            if (!cards.ContainsKey(sessionA.RolePlayCardId) || !cards.ContainsKey(sessionB.RolePlayCardId)) continue;

            var sessionsWithAudio = await db.SpeakingRecordings.AsNoTracking()
                .Where(r => sessionIds.Contains(r.SpeakingSessionId) && !r.IsArchived && !r.IsWarmup)
                .Select(r => r.SpeakingSessionId)
                .Distinct()
                .ToListAsync(ct);

            candidates.Add(new SpeakingGraderCalibrationMockCandidate(
                exam.Id,
                exam.ProfessionId,
                cards[sessionA.RolePlayCardId].ScenarioTitle,
                cards[sessionB.RolePlayCardId].ScenarioTitle,
                exam.CompletedAt ?? exam.UpdatedAt,
                sessionsWithAudio.Count >= 2));
        }

        return candidates;
    }

    // ── Promote ──────────────────────────────────────────────────────────

    /// <summary>Promote a completed two-card AI exam for the expert to mark as ONE performance. Pins both cards'
    /// transcripts, records whether both cards have audio, keeps that audio for a year and writes an audit event.</summary>
    public async Task<SpeakingGraderCalibrationMockSampleRow> PromoteMockAsync(
        string adminId, string adminName, string? examId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(examId))
        {
            throw ApiException.Validation("speaking_calibration_exam_required", "Choose a Full Mock to promote.");
        }

        var exam = await db.SpeakingExamSessions
            .FirstOrDefaultAsync(e => e.Id == examId, ct)
            ?? throw ApiException.NotFound("speaking_calibration_exam_not_found", "That Full Mock does not exist.");

        if (exam.State != SpeakingExamState.Completed
            || exam.Mode != SpeakingExamMode.Ai
            || string.IsNullOrWhiteSpace(exam.SessionAId)
            || string.IsNullOrWhiteSpace(exam.SessionBId))
        {
            throw ApiException.Conflict("speaking_calibration_exam_not_eligible",
                "Only a completed two-card AI Full Mock can be used for calibration.");
        }

        if (await db.SpeakingGraderCalibrationMockSamples.AnyAsync(s => s.SpeakingExamId == examId, ct))
        {
            throw ApiException.Conflict("speaking_calibration_already_promoted", "That Full Mock is already in the calibration set.");
        }

        var recordedAt = exam.ActiveAStartedAt ?? exam.IntroStartedAt ?? exam.CompletedAt ?? exam.UpdatedAt;
        if (!await CalibrationConsents().AnyAsync(c => c.UserId == exam.UserId && c.AcceptedAt <= recordedAt, ct))
        {
            throw ApiException.Conflict("speaking_calibration_consent_missing",
                "The learner had not accepted the consent wording that covers quality assurance and grader calibration "
                + "when this Full Mock was recorded, so it cannot be used.");
        }

        var sessionIds = new[] { exam.SessionAId!, exam.SessionBId! };
        var sessions = await db.SpeakingSessions.AsNoTracking()
            .Where(s => sessionIds.Contains(s.Id))
            .ToListAsync(ct);
        if (sessions.Count != 2)
        {
            throw ApiException.NotFound("speaking_calibration_session_not_found", "A card session of that Full Mock does not exist.");
        }
        var sessionA = sessions.First(s => s.Id == exam.SessionAId);
        var sessionB = sessions.First(s => s.Id == exam.SessionBId);

        var transcriptA = await UsableTranscriptAsync(sessionA.Id, ct)
            ?? throw ApiException.Conflict("speaking_calibration_no_transcript", "Card A has no usable transcript.");
        var transcriptB = await UsableTranscriptAsync(sessionB.Id, ct)
            ?? throw ApiException.Conflict("speaking_calibration_no_transcript", "Card B has no usable transcript.");

        var cardA = await db.RolePlayCards.AsNoTracking()
            .Where(c => c.Id == sessionA.RolePlayCardId)
            .Select(c => new { c.Id, c.ScenarioTitle })
            .FirstOrDefaultAsync(ct)
            ?? throw ApiException.NotFound("speaking_calibration_card_not_found", "Card A of that Full Mock no longer exists.");
        var cardB = await db.RolePlayCards.AsNoTracking()
            .Where(c => c.Id == sessionB.RolePlayCardId)
            .Select(c => new { c.Id, c.ScenarioTitle })
            .FirstOrDefaultAsync(ct)
            ?? throw ApiException.NotFound("speaking_calibration_card_not_found", "Card B of that Full Mock no longer exists.");

        var recordingsA = await RecordingsToKeepAsync(sessionA.Id, ct);
        var recordingsB = await RecordingsToKeepAsync(sessionB.Id, ct);

        var now = clock.GetUtcNow();
        var keepUntil = now + CalibrationAudioRetention;

        var sample = new SpeakingGraderCalibrationMockSample
        {
            Id = $"spgcm_{Guid.NewGuid():N}",
            SpeakingExamId = examId,
            SessionAId = sessionA.Id,
            SessionBId = sessionB.Id,
            TranscriptAId = transcriptA,
            TranscriptBId = transcriptB,
            CardAId = cardA.Id,
            CardBId = cardB.Id,
            ProfessionId = exam.ProfessionId,
            HasAudio = recordingsA.Count > 0 && recordingsB.Count > 0,
            Status = SpeakingGraderCalibrationSampleStatus.Pending,
            PromotedById = adminId,
            PromotedAt = now,
            UpdatedAt = now,
        };
        db.SpeakingGraderCalibrationMockSamples.Add(sample);
        db.AuditEvents.Add(new AuditEvent
        {
            Id = $"audit-{Guid.NewGuid():N}",
            OccurredAt = now,
            ActorId = adminId,
            ActorName = adminName,
            Action = "SpeakingGraderCalibrationMockSamplePromoted",
            ResourceType = "SpeakingGraderCalibrationMockSample",
            ResourceId = sample.Id,
            Details = JsonSerializer.Serialize(new
            {
                examId,
                sessionA = sessionA.Id,
                sessionB = sessionB.Id,
                clips = recordingsA.Count + recordingsB.Count,
                audioKeptUntil = sample.HasAudio ? keepUntil : (DateTimeOffset?)null,
            }),
        });
        await db.SaveChangesAsync(ct);

        return ToMockRow(sample, cardA.ScenarioTitle, cardB.ScenarioTitle);
    }

    // ── Overview ─────────────────────────────────────────────────────────

    public async Task<SpeakingGraderCalibrationMockOverview> GetMockOverviewAsync(CancellationToken ct)
    {
        var samples = await db.SpeakingGraderCalibrationMockSamples.AsNoTracking()
            .OrderByDescending(s => s.PromotedAt)
            .ToListAsync(ct);
        var cardTitles = await MockCardTitlesAsync(samples, ct);
        var unusable = await UnusableMockSampleIdsAsync(samples, ct);
        var rows = samples
            .Select(s => ToMockRow(
                s,
                cardTitles.TryGetValue((s.Id, "A"), out var a) ? a : "",
                cardTitles.TryGetValue((s.Id, "B"), out var b) ? b : "",
                !unusable.Contains(s.Id)))
            .ToList();

        // The same coverage shape as the card set, over the expert's mock marks. It says what the approved
        // VALIDATION run will need; an owner pilot needs none of it (a pilot run just grades what is marked).
        var coverageInput = samples
            .Select(s => new SpeakingGraderCalibrationSample
            {
                Id = s.Id,
                Status = s.Status,
                ExpertOverallScaled = s.ExpertOverallScaled,
                HasAudio = s.HasAudio,
            })
            .ToList();
        return new SpeakingGraderCalibrationMockOverview(BuildCoverage(coverageInput, unusable), rows);
    }

    // ── Blind labelling view ─────────────────────────────────────────────

    /// <summary>The blind view of a whole two-card test: both cards, both cleaned transcripts and both clip lists —
    /// one mark. No AI value.</summary>
    public async Task<SpeakingGraderCalibrationMockSampleDetail> GetMockDetailAsync(string sampleId, CancellationToken ct)
    {
        var sample = await db.SpeakingGraderCalibrationMockSamples.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == sampleId, ct)
            ?? throw ApiException.NotFound("speaking_calibration_sample_not_found", "That calibration sample does not exist.");

        var cards = await db.RolePlayCards.AsNoTracking()
            .Where(c => c.Id == sample.CardAId || c.Id == sample.CardBId)
            .ToListAsync(ct);
        var cardA = cards.FirstOrDefault(c => c.Id == sample.CardAId)
            ?? throw ApiException.NotFound("speaking_calibration_card_not_found", "Card A of that Full Mock no longer exists.");
        var cardB = cards.FirstOrDefault(c => c.Id == sample.CardBId)
            ?? throw ApiException.NotFound("speaking_calibration_card_not_found", "Card B of that Full Mock no longer exists.");

        var transcriptJsons = await db.SpeakingTranscripts.AsNoTracking()
            .Where(t => t.Id == sample.TranscriptAId || t.Id == sample.TranscriptBId)
            .Select(t => new { t.Id, t.SegmentsJson })
            .ToListAsync(ct);

        var clips = await db.SpeakingRecordings.AsNoTracking()
            .Where(r => (r.SpeakingSessionId == sample.SessionAId || r.SpeakingSessionId == sample.SessionBId)
                && !r.IsArchived && !r.IsWarmup)
            .OrderBy(r => r.CreatedAt)
            .Select(r => new { r.Id, r.SpeakingSessionId, r.DurationSeconds, r.MimeType })
            .ToListAsync(ct);

        return new SpeakingGraderCalibrationMockSampleDetail(
            sample.Id,
            StatusCode(sample.Status),
            sample.HasAudio,
            MockCard(cardA),
            MockCard(cardB),
            ReadTranscript(transcriptJsons.FirstOrDefault(t => t.Id == sample.TranscriptAId)?.SegmentsJson),
            ReadTranscript(transcriptJsons.FirstOrDefault(t => t.Id == sample.TranscriptBId)?.SegmentsJson),
            clips.Where(c => c.SpeakingSessionId == sample.SessionAId)
                .Select(c => new SpeakingGraderCalibrationAudioClip(c.Id, c.DurationSeconds, c.MimeType))
                .ToList(),
            clips.Where(c => c.SpeakingSessionId == sample.SessionBId)
                .Select(c => new SpeakingGraderCalibrationAudioClip(c.Id, c.DurationSeconds, c.MimeType))
                .ToList(),
            Criteria,
            ReadMockLabel(sample),
            sample.ExcludedReason);
    }

    /// <summary>The storage path and mime type of one of the mock's clips, for the audio stream. The clip must
    /// belong to one of the mock's two sessions.</summary>
    public async Task<(string StoragePath, string MimeType)> GetMockClipAsync(
        string sampleId, string recordingId, CancellationToken ct)
    {
        var sample = await db.SpeakingGraderCalibrationMockSamples.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == sampleId, ct)
            ?? throw ApiException.NotFound("speaking_calibration_sample_not_found", "That calibration sample does not exist.");
        var recording = await db.SpeakingRecordings.AsNoTracking()
            .Include(r => r.MediaAsset)
            .FirstOrDefaultAsync(r => r.Id == recordingId
                && (r.SpeakingSessionId == sample.SessionAId || r.SpeakingSessionId == sample.SessionBId)
                && !r.IsArchived, ct);
        if (recording?.MediaAsset is null || string.IsNullOrWhiteSpace(recording.MediaAsset.StoragePath))
        {
            throw ApiException.NotFound("speaking_calibration_audio_not_found", "That audio clip is not available.");
        }

        return (recording.MediaAsset.StoragePath, recording.MediaAsset.MimeType);
    }

    // ── Label / exclude ──────────────────────────────────────────────────

    /// <summary>Record the expert's ONE set of nine criterion scores and overall /500 for the whole test.
    /// Re-labelling replaces the earlier marks.</summary>
    public async Task<SpeakingGraderCalibrationMockSampleRow> LabelMockAsync(
        string adminId, string sampleId, SpeakingGraderCalibrationLabelRequest request, CancellationToken ct)
    {
        var scores = ValidateLabel(request);
        await EnsureMockNotInActiveRunAsync(sampleId, ct);
        var sample = await db.SpeakingGraderCalibrationMockSamples
            .FirstOrDefaultAsync(s => s.Id == sampleId, ct)
            ?? throw ApiException.NotFound("speaking_calibration_sample_not_found", "That calibration sample does not exist.");
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

        var titles = await MockCardTitlesAsync([sample], ct);
        return ToMockRow(
            sample,
            titles.TryGetValue((sample.Id, "A"), out var a) ? a : "",
            titles.TryGetValue((sample.Id, "B"), out var b) ? b : "");
    }

    /// <summary>Mark a whole-test performance unusable (no speech, wrong card, broken audio). Kept for audit, never reported.</summary>
    public async Task<SpeakingGraderCalibrationMockSampleRow> ExcludeMockAsync(
        string sampleId, SpeakingGraderCalibrationExcludeRequest request, CancellationToken ct)
    {
        var reason = (request.Reason ?? string.Empty).Trim();
        if (reason.Length == 0 || reason.Length > 500)
        {
            throw ApiException.Validation("speaking_calibration_reason_required",
                "Say briefly why this Full Mock cannot be used (up to 500 characters).");
        }

        await EnsureMockNotInActiveRunAsync(sampleId, ct);
        var sample = await db.SpeakingGraderCalibrationMockSamples
            .FirstOrDefaultAsync(s => s.Id == sampleId, ct)
            ?? throw ApiException.NotFound("speaking_calibration_sample_not_found", "That calibration sample does not exist.");
        sample.Status = SpeakingGraderCalibrationSampleStatus.Excluded;
        sample.ExcludedReason = reason;
        sample.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);

        var titles = await MockCardTitlesAsync([sample], ct);
        return ToMockRow(
            sample,
            titles.TryGetValue((sample.Id, "A"), out var a) ? a : "",
            titles.TryGetValue((sample.Id, "B"), out var b) ? b : "");
    }

    /// <summary>Audit that an admin streamed one of the mock's clips, before the bytes are sent. No learner identity.</summary>
    public async Task AuditMockClipAccessAsync(
        string adminId, string adminName, string sampleId, string recordingId, CancellationToken ct)
    {
        var sample = await db.SpeakingGraderCalibrationMockSamples.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == sampleId, ct)
            ?? throw ApiException.NotFound("speaking_calibration_sample_not_found", "That calibration sample does not exist.");
        db.AuditEvents.Add(new AuditEvent
        {
            Id = $"audit-{Guid.NewGuid():N}",
            OccurredAt = clock.GetUtcNow(),
            ActorId = adminId,
            ActorName = string.IsNullOrWhiteSpace(adminName) ? adminId : adminName,
            Action = "SpeakingRecordingAccessed",
            ResourceType = "SpeakingRecording",
            ResourceId = recordingId,
            Details = JsonSerializer.Serialize(new
            {
                examId = sample.SpeakingExamId,
                purpose = "Grader calibration (blind Full Mock labelling)",
                sampleId,
            }),
        });
        await db.SaveChangesAsync(ct);
    }

    // ── helpers ──────────────────────────────────────────────────────────

    /// <summary>The id of the latest usable transcript of a session (what the grader would read), or null.</summary>
    private async Task<string?> UsableTranscriptAsync(string sessionId, CancellationToken ct)
        => await db.SpeakingTranscripts.AsNoTracking()
            .Where(t => t.SpeakingSessionId == sessionId
                && t.IsLatest
                && t.SegmentsJson != "[]"
                && t.Provider != SpeakingTranscriptionPipeline.StateFailed
                && t.Provider != SpeakingTranscriptionPipeline.StateQueued
                && t.Provider != SpeakingTranscriptionPipeline.StateProcessing)
            .OrderByDescending(t => t.GeneratedAt)
            .Select(t => t.Id)
            .FirstOrDefaultAsync(ct);

    /// <summary>The live non-warm-up recordings of a session, with their retention extended to the calibration window.</summary>
    private async Task<List<SpeakingRecording>> RecordingsToKeepAsync(string sessionId, CancellationToken ct)
    {
        var recordings = await db.SpeakingRecordings
            .Where(r => r.SpeakingSessionId == sessionId && !r.IsArchived && !r.IsWarmup)
            .ToListAsync(ct);
        var keepUntil = clock.GetUtcNow() + CalibrationAudioRetention;
        foreach (var recording in recordings.Where(r => r.RetentionExpiresAt is null || r.RetentionExpiresAt < keepUntil))
        {
            recording.RetentionExpiresAt = keepUntil;
        }

        return recordings;
    }

    private async Task<Dictionary<(string SampleId, string Slot), string>> MockCardTitlesAsync(
        IReadOnlyCollection<SpeakingGraderCalibrationMockSample> samples, CancellationToken ct)
    {
        var ids = samples.SelectMany(s => new[] { s.CardAId, s.CardBId }).Distinct().ToList();
        var cards = await db.RolePlayCards.AsNoTracking()
            .Where(c => ids.Contains(c.Id))
            .Select(c => new { c.Id, c.ScenarioTitle })
            .ToListAsync(ct);
        var titleById = cards.ToDictionary(c => c.Id, c => c.ScenarioTitle, StringComparer.Ordinal);
        var titles = new Dictionary<(string, string), string>();
        foreach (var sample in samples)
        {
            if (titleById.TryGetValue(sample.CardAId, out var a)) titles[(sample.Id, "A")] = a;
            if (titleById.TryGetValue(sample.CardBId, out var b)) titles[(sample.Id, "B")] = b;
        }

        return titles;
    }

    private static SpeakingGraderCalibrationCard MockCard(RolePlayCard card)
        => new(
            card.ScenarioTitle,
            card.ProfessionId,
            card.Setting,
            card.CandidateRole,
            card.InterlocutorRole,
            card.Background,
            card.Tasks);

    private static SpeakingGraderCalibrationMockSampleRow ToMockRow(
        SpeakingGraderCalibrationMockSample sample, string cardATitle, string cardBTitle, bool usable = true)
        => new(
            sample.Id,
            sample.SpeakingExamId,
            sample.ProfessionId,
            cardATitle,
            cardBTitle,
            sample.HasAudio,
            StatusCode(sample.Status),
            sample.ExpertOverallScaled,
            sample.ExpertOverallScaled is { } overall ? OetScoring.OetGradeLetterFromScaled(overall) : null,
            sample.PromotedAt,
            sample.LabelledAt,
            usable);

    private static SpeakingGraderCalibrationLabel? ReadMockLabel(SpeakingGraderCalibrationMockSample sample)
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

    /// <summary>Mock samples that can no longer be graded or replayed: a pinned transcript is gone, audio a sample
    /// counted on is archived/deleted, or the learner has withdrawn the consent that covered calibration.</summary>
    public async Task<HashSet<string>> UnusableMockSampleIdsAsync(
        IReadOnlyCollection<SpeakingGraderCalibrationMockSample> samples, CancellationToken ct)
    {
        var unusable = new HashSet<string>(StringComparer.Ordinal);
        if (samples.Count == 0) return unusable;

        var transcriptIds = samples.SelectMany(s => new[] { s.TranscriptAId, s.TranscriptBId }).Distinct().ToList();
        var sessionIds = samples.SelectMany(s => new[] { s.SessionAId, s.SessionBId }).Distinct().ToList();
        var examIds = samples.Select(s => s.SpeakingExamId).Distinct().ToList();

        var livingTranscripts = (await db.SpeakingTranscripts.AsNoTracking()
                .Where(t => transcriptIds.Contains(t.Id) && t.SegmentsJson != "[]")
                .Select(t => t.Id)
                .ToListAsync(ct))
            .ToHashSet(StringComparer.Ordinal);
        var sessionsWithAudio = (await db.SpeakingRecordings.AsNoTracking()
                .Where(r => sessionIds.Contains(r.SpeakingSessionId) && !r.IsArchived && !r.IsWarmup)
                .Select(r => r.SpeakingSessionId)
                .Distinct()
                .ToListAsync(ct))
            .ToHashSet(StringComparer.Ordinal);
        var exams = await db.SpeakingExamSessions.AsNoTracking()
            .Where(e => examIds.Contains(e.Id))
            .Select(e => new { e.Id, e.UserId, e.ActiveAStartedAt, e.IntroStartedAt, e.CompletedAt, e.UpdatedAt })
            .ToListAsync(ct);
        var examById = exams.ToDictionary(e => e.Id, StringComparer.Ordinal);
        var consents = (await CalibrationConsents()
                .Where(c => exams.Select(e => e.UserId).Contains(c.UserId))
                .Select(c => new { c.UserId, c.AcceptedAt })
                .ToListAsync(ct))
            .ToLookup(c => c.UserId, c => c.AcceptedAt, StringComparer.Ordinal);

        foreach (var sample in samples)
        {
            var covered = false;
            if (examById.TryGetValue(sample.SpeakingExamId, out var exam))
            {
                var recordedAt = exam.ActiveAStartedAt ?? exam.IntroStartedAt ?? exam.CompletedAt ?? exam.UpdatedAt;
                covered = consents[exam.UserId].Any(acceptedAt => acceptedAt <= recordedAt);
            }

            var audioOk = !sample.HasAudio
                || (sessionsWithAudio.Contains(sample.SessionAId) && sessionsWithAudio.Contains(sample.SessionBId));
            if (!livingTranscripts.Contains(sample.TranscriptAId)
                || !livingTranscripts.Contains(sample.TranscriptBId)
                || !audioOk
                || !covered)
            {
                unusable.Add(sample.Id);
            }
        }

        return unusable;
    }

    /// <summary>A mock sample inside a running mock-scope run is frozen: its marks are what the run compares against.</summary>
    private async Task EnsureMockNotInActiveRunAsync(string sampleId, CancellationToken ct)
    {
        var inActiveRun = await db.SpeakingGraderCalibrationGrades.AsNoTracking()
            .AnyAsync(g => g.SampleId == sampleId
                && db.SpeakingGraderCalibrationRuns.Any(r => r.Id == g.RunId
                    && r.Scope == ScopeMock
                    && r.Status == SpeakingGraderCalibrationRunStatus.Running), ct);
        if (inActiveRun)
        {
            throw ApiException.Conflict("speaking_calibration_run_active",
                "A calibration run is using this Full Mock. Finalise or cancel the run before changing its marks.");
        }
    }
}
