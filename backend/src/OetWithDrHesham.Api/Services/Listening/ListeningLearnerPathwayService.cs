using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OetWithDrHesham.Api.Contracts;
using OetWithDrHesham.Api.Data;
using OetWithDrHesham.Api.Domain;
using OetWithDrHesham.Api.Services;

namespace OetWithDrHesham.Api.Services.Listening;

// ═════════════════════════════════════════════════════════════════════════════
// Listening Module Pathway — Phase 1 Orchestrator (A6)
//
// Composes the three pathway-pillar services already authored under this
// namespace:
//   • IListeningSkillScoringService    (A2) — L1..L8 + accent rolling scores
//   • IListeningLearnerGradingService  (A3) — deterministic per-attempt grading
//   • IListeningPathwayGenerator       (A4) — pure-function roadmap generator
//
// Implements the learner flow:
//   early flow → audio_check → foundation → practice → mastery
//
// Reference: mirrors ReadingLearnerPathwayService.cs in shape and convention.
//
// All wall-clock reads go through the injected TimeProvider so unit tests can
// freeze time. SaveChangesAsync is called once per public method to keep
// partial commits off the table.
// ═════════════════════════════════════════════════════════════════════════════

public interface IListeningLearnerPathwayService
{
    Task<LearnerListeningProfileResponse> GetProfileAsync(string userId, CancellationToken ct);
    Task<AudioCheckResponse> SubmitAudioCheckAsync(
        string userId, AudioCheckRequest request, CancellationToken ct);
    Task<PathwayResponse> GetPathwayAsync(string userId, CancellationToken ct);
    Task<PathwayStatusResponse> GetStageAsync(string userId, CancellationToken ct);
    Task SaveSessionNotesAsync(
        string userId, Guid sessionId, string? questionId,
        string noteMarkdown, CancellationToken ct);
}

public sealed class ListeningLearnerPathwayService : IListeningLearnerPathwayService
{
    private readonly LearnerDbContext _db;
    private readonly TimeProvider _clock;
    private readonly ILogger<ListeningLearnerPathwayService> _logger;

    public ListeningLearnerPathwayService(
        LearnerDbContext db,
        TimeProvider clock,
        ILogger<ListeningLearnerPathwayService> logger)
    {
        _db = db;
        _clock = clock;
        _logger = logger;
    }

    // ─────────────────────────────────────────────────────────────────────
    // Profile / stage queries
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Fetch the learner's listening profile, or throw if early flow has not
    /// happened yet. Surfaces as 404 at the endpoint layer.
    /// </summary>
    public async Task<LearnerListeningProfileResponse> GetProfileAsync(
        string userId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        var profile = await _db.LearnerListeningProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == userId, ct)
            ?? throw new InvalidOperationException("Listening profile not found");

        return MapProfileToResponse(profile);
    }

    /// <summary>
    /// Record an audio-check outcome (§5.4). Pass values ("clear" / "quiet")
    /// advance the stage to "diagnostic"; "failed" keeps the learner on
    /// audio_check so they're prompted to retry hardware. Idempotent — the
    /// timestamp is only set on first success.
    /// </summary>
    public async Task<AudioCheckResponse> SubmitAudioCheckAsync(
        string userId, AudioCheckRequest request, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentNullException.ThrowIfNull(request);

        var now = _clock.GetUtcNow();
        var profile = await _db.LearnerListeningProfiles
            .FirstOrDefaultAsync(p => p.UserId == userId, ct);

        if (profile is null)
        {
            profile = new LearnerListeningProfile
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                TargetBand = "B",
                ExamDate = null,
                HoursPerWeek = 5,
                Profession = "Medicine",
                EnglishExposureSource = "mixed",
                ComfortBritish = 3,
                ComfortAustralian = 3,
                ComfortVarious = 3,
                HasTakenBefore = false,
                PreviousScore = null,
                SelfRatedSpeed = 3,
                SelfRatedNoteTaking = 3,
                SelfRatedSpelling = 3,
                CurrentStage = "audio_check",
                OnboardingCompletedAt = now,
                UpdatedAt = now,
            };
            _db.LearnerListeningProfiles.Add(profile);

            await RefreshAccentSelfConfidenceAsync(
                userId,
                profile.ComfortBritish,
                profile.ComfortAustralian,
                profile.ComfortVarious,
                ct);
        }
        var outcome = (request.Outcome ?? string.Empty).Trim().ToLowerInvariant();

        var passed = outcome is "clear" or "quiet";

        if (passed)
        {
            // First pass — capture the timestamp and advance the stage. We
            // only advance when still on audio_check; re-takers don't get
            // demoted from later stages.
            if (profile.AudioCheckPassedAt is null)
            {
                profile.AudioCheckPassedAt = now;
            }
            if (profile.CurrentStage == "audio_check")
            {
                profile.CurrentStage = "foundation";
            }
            profile.UpdatedAt = now;
        }
        else
        {
            // "failed" — record the touch but don't advance. We deliberately
            // skip an audit-style note here for Phase 1 (cf. spec note); the
            // frontend retries the player and re-submits.
            profile.UpdatedAt = now;
            _logger.LogInformation(
                "Listening audio check reported failure for user {UserId} (outcome={Outcome}, volume={Volume}).",
                userId, outcome, request.VolumeLevel);
        }

        await _db.SaveChangesAsync(ct);

        return new AudioCheckResponse
        {
            Success = passed,
            CurrentStage = profile.CurrentStage,
            AudioCheckPassedAt = profile.AudioCheckPassedAt,
        };
    }

    // ─────────────────────────────────────────────────────────────────────
    // Pathway + stage queries
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Return the full multi-week roadmap for the learner. Surfaces as 404
    /// at the endpoint layer if the diagnostic has not generated one yet.
    /// </summary>
    public async Task<PathwayResponse> GetPathwayAsync(string userId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        var pathway = await _db.LearnerListeningPathways
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == userId, ct)
            ?? throw new InvalidOperationException(
                "No listening pathway yet — complete the diagnostic first.");

        var weeks = DeserializeWeeks(pathway.WeeksJson);

        return new PathwayResponse
        {
            TotalWeeks = pathway.TotalWeeks,
            GeneratedAt = pathway.GeneratedAt,
            Weeks = weeks,
        };
    }

    /// <summary>
    /// Lightweight probe used by the listening landing page to decide which
    /// CTA to surface. Safe to call even before early flow — returns
    /// HasProfile=false in that case rather than throwing.
    /// </summary>
    public async Task<PathwayStatusResponse> GetStageAsync(string userId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        var profile = await _db.LearnerListeningProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == userId, ct);

        if (profile is null)
        {
            return new PathwayStatusResponse
            {
                HasProfile = false,
                CurrentStage = "none",
            };
        }

        // Most-recent completed diagnostic — used for the dashboard chip.
        var diagnosticCompletedAt = await _db.ListeningPracticeSessions
            .AsNoTracking()
            .Where(s => s.UserId == userId
                && s.SessionType == "diagnostic"
                && s.CompletedAt != null)
            .OrderByDescending(s => s.CompletedAt)
            .Select(s => s.CompletedAt)
            .FirstOrDefaultAsync(ct);

        int? daysUntilExam = null;
        if (profile.ExamDate is { } exam)
        {
            var totalDays = (exam - _clock.GetUtcNow()).TotalDays;
            daysUntilExam = totalDays > 0
                ? (int)Math.Ceiling(totalDays)
                : 0;
        }

        return new PathwayStatusResponse
        {
            HasProfile = true,
            CurrentStage = profile.CurrentStage,
            DiagnosticCompletedAt = diagnosticCompletedAt,
            PathwayGeneratedAt = profile.PathwayGeneratedAt,
            CurrentReadinessScore = profile.CurrentReadinessScore,
            DaysUntilExam = daysUntilExam,
        };
    }

    /// <summary>
    /// Auto-save endpoint for Part-A scratch notes. Upserts on
    /// (UserId, SessionId, QuestionId) so each note row has a single home
    /// regardless of how many keystrokes the client throttled into.
    /// </summary>
    public async Task SaveSessionNotesAsync(
        string userId, Guid sessionId, string? questionId,
        string noteMarkdown, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        noteMarkdown ??= string.Empty;

        var session = await _db.ListeningPracticeSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId, ct)
            ?? throw new InvalidOperationException("Session not found");

        var now = _clock.GetUtcNow();
        await UpsertNoteAsync(userId, session.Id, questionId, noteMarkdown, now, ct);

        await _db.SaveChangesAsync(ct);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Private helpers — notes upsert
    // ─────────────────────────────────────────────────────────────────────

    private async Task UpsertNoteAsync(
        string userId,
        Guid sessionId,
        string? questionId,
        string noteMarkdown,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var note = await _db.ListeningPracticeNotes
            .FirstOrDefaultAsync(n =>
                n.UserId == userId
                && n.PracticeSessionId == sessionId
                && n.ListeningQuestionId == questionId, ct);

        if (note is null)
        {
            note = new ListeningPracticeNote
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                PracticeSessionId = sessionId,
                ListeningQuestionId = questionId,
            };
            _db.ListeningPracticeNotes.Add(note);
        }

        note.NoteMarkdown = noteMarkdown;
        note.CharacterCount = noteMarkdown.Length;
        note.LastSavedAt = now;
    }

    /// <summary>
    /// Push the learner's self-reported accent comfort onto the four
    /// LearnerAccentProgress rows so the dashboard chips reflect the initial flow
    /// even before the diagnostic runs. EnglishExposureSource intentionally
    /// does not seed comfort here — it's used by the pathway generator
    /// separately, not the dashboard.
    /// </summary>
    private async Task RefreshAccentSelfConfidenceAsync(
        string userId,
        int comfortBritish,
        int comfortAustralian,
        int comfortVarious,
        CancellationToken ct)
    {
        var existing = await _db.LearnerAccentProgresses
            .Where(a => a.UserId == userId)
            .ToListAsync(ct);

        // "various" maps to both US and non_native because early flow only
        // captures three buckets but the dashboard renders four. The diagnostic
        // overwrites these with measured values, so the duplication is a
        // best-effort placeholder.
        var seeds = new (string Accent, int Rating)[]
        {
            ("british", comfortBritish),
            ("australian", comfortAustralian),
            ("us", comfortVarious),
            ("non_native", comfortVarious),
        };

        foreach (var (accent, rating) in seeds)
        {
            var row = existing.FirstOrDefault(a =>
                string.Equals(a.Accent, accent, StringComparison.OrdinalIgnoreCase));
            if (row is null)
            {
                row = new LearnerAccentProgress
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    Accent = accent,
                    AccuracyPercentage = 0m,
                    QuestionsAttempted = 0,
                    QuestionsCorrect = 0,
                    MinutesListened = 0,
                };
                _db.LearnerAccentProgresses.Add(row);
            }
            row.SelfConfidenceRating = rating;
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // Private helpers — projections + parsing
    // ─────────────────────────────────────────────────────────────────────

    private static LearnerListeningProfileResponse MapProfileToResponse(LearnerListeningProfile profile)
    {
        return new LearnerListeningProfileResponse
        {
            UserId = profile.UserId,
            TargetBand = profile.TargetBand,
            ExamDate = profile.ExamDate,
            HoursPerWeek = profile.HoursPerWeek,
            Profession = profile.Profession,
            EnglishExposureSource = profile.EnglishExposureSource,
            ComfortBritish = profile.ComfortBritish,
            ComfortAustralian = profile.ComfortAustralian,
            ComfortVarious = profile.ComfortVarious,
            HasTakenBefore = profile.HasTakenBefore,
            PreviousScore = profile.PreviousScore,
            SelfRatedSpeed = profile.SelfRatedSpeed,
            SelfRatedNoteTaking = profile.SelfRatedNoteTaking,
            SelfRatedSpelling = profile.SelfRatedSpelling,
            CurrentStage = profile.CurrentStage,
            CurrentReadinessScore = profile.CurrentReadinessScore,
            PredictedScore = profile.PredictedScore,
            OnboardingCompletedAt = profile.OnboardingCompletedAt,
            AudioCheckPassedAt = profile.AudioCheckPassedAt,
            PathwayGeneratedAt = profile.PathwayGeneratedAt,
            UpdatedAt = profile.UpdatedAt,
        };
    }

    private static List<RoadmapWeekDto> DeserializeWeeks(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "[]") return new List<RoadmapWeekDto>();
        try
        {
            var list = JsonSerializer.Deserialize<List<RoadmapWeekDto>>(json);
            return list ?? new List<RoadmapWeekDto>();
        }
        catch (JsonException)
        {
            return new List<RoadmapWeekDto>();
        }
    }

}

