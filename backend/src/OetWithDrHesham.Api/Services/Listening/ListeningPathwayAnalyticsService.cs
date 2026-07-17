using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OetWithDrHesham.Api.Contracts;
using OetWithDrHesham.Api.Data;
using OetWithDrHesham.Api.Domain;
using OetWithDrHesham.Api.Services;

namespace OetWithDrHesham.Api.Services.Listening;

// ═════════════════════════════════════════════════════════════════════════════
// Listening Module Pathway — Phase 5 Analytics Dashboard
//
// Implements OET_LISTENING_MODULE_PATHWAY.md §19 (analytics + readiness).
//
// SEPARATE from the existing ListeningAnalyticsService.cs (admin/student
// per-paper analytics over the V2 attempt surface). This service feeds the
// pathway-side analytics dashboard rendered at /listening/stats:
//
//   • readiness score      — composite 0–100 across six components.
//   • skill radar          — L1..L8 rolling mastery (overlays diagnostic).
//   • accent chart         — BR/AU/US/NN per-bucket accuracy + minutes.
//   • score history        — completed diagnostic + mock sessions over time.
//   • note-taking stats    — Part-A character volume + drop tags.
//   • spelling stats       — meaning-right / spelling-wrong items.
//   • calendar heatmap     — last 90 days of question attempts.
//
// All wall-clock reads go through TimeProvider so unit tests can freeze time.
// SaveChangesAsync is called only when the readiness score is persisted — the
// rest of the surface is pure projection.
// ═════════════════════════════════════════════════════════════════════════════

public interface IListeningPathwayAnalyticsService
{
    /// <summary>Hero block of the dashboard — readiness + stage + last mock.</summary>
    Task<ListeningDashboardDto> GetDashboardAsync(string userId, CancellationToken ct);

    /// <summary>L1..L8 radar projection. Mirrors the diagnostic-results version.</summary>
    Task<SkillRadarDto> GetSkillRadarAsync(string userId, CancellationToken ct);

    /// <summary>Per-accent rolling progress + confidence.</summary>
    Task<AccentChartDto> GetAccentChartAsync(string userId, CancellationToken ct);

    /// <summary>Time-series of diagnostic + mock scaled scores.</summary>
    Task<ScoreHistoryDto> GetScoreHistoryAsync(string userId, CancellationToken ct);

    /// <summary>Re-compute the composite readiness score (0–100). Persists to
    /// LearnerListeningProfile.CurrentReadinessScore as a side effect.</summary>
    Task<int> CalculateReadinessAsync(string userId, CancellationToken ct);

    /// <summary>Cumulative Part-A note volume + drop tags across all sessions.</summary>
    Task<NoteTakingStatsDto> GetNoteTakingStatsAsync(string userId, CancellationToken ct);

    /// <summary>Cumulative spelling-tolerance miss count + sample pairs.</summary>
    Task<SpellingStatsDto> GetSpellingStatsAsync(string userId, CancellationToken ct);

    /// <summary>Last 90 days of question-attempt activity, one row per date.</summary>
    Task<CalendarHeatmapDto> GetCalendarAsync(string userId, CancellationToken ct);
}

public sealed class ListeningPathwayAnalyticsService : IListeningPathwayAnalyticsService
{
    /// <summary>Spec §9.6 component weights — must sum to 1.0.</summary>
    private const double WeightMockAverage = 0.40;
    private const double WeightLowestSkill = 0.20;
    private const double WeightPronunciationRetention = 0.10;
    private const double WeightAccentConfidence = 0.15;
    private const double WeightDaysUntilExam = 0.05;
    private const double WeightConsistency = 0.10;

    /// <summary>Heatmap window — covers the §19.7 "last quarter" view.</summary>
    private const int CalendarLookbackDays = 90;

    /// <summary>The pathway-side mock count we sample for the score average
    /// component of the readiness calculation.</summary>
    private const int MockAverageSampleSize = 5;

    /// <summary>Typical Part-A note-volume window in characters — same constant
    /// as the diagnostic results page (spec §6.4).</summary>
    private const int TypicalNotesLow = 80;
    private const int TypicalNotesHigh = 120;

    private static readonly IReadOnlyDictionary<string, string> SkillLabels =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["L1"] = "Detail capture",
            ["L2"] = "Note-taking speed",
            ["L3"] = "Spelling accuracy",
            ["L4"] = "Gist comprehension",
            ["L5"] = "Distractor recognition",
            ["L6"] = "Inference",
            ["L7"] = "Speaker stance",
            ["L8"] = "Accent adaptation",
        };

    private static readonly string[] AllSkillCodes =
        ["L1", "L2", "L3", "L4", "L5", "L6", "L7", "L8"];

    private static readonly IReadOnlyDictionary<string, string> AccentLabels =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["british"] = "British (UK)",
            ["australian"] = "Australian",
            ["us"] = "North American",
            ["non_native"] = "Non-native",
        };

    private static readonly string[] AllAccents =
        ["british", "australian", "us", "non_native"];

    private readonly LearnerDbContext _db;
    private readonly IListeningSkillScoringService _scoring;
    private readonly TimeProvider _clock;
    private readonly ILogger<ListeningPathwayAnalyticsService> _logger;

    public ListeningPathwayAnalyticsService(
        LearnerDbContext db,
        IListeningSkillScoringService scoring,
        TimeProvider clock,
        ILogger<ListeningPathwayAnalyticsService> logger)
    {
        _db = db;
        _scoring = scoring;
        _clock = clock;
        _logger = logger;
    }

    // ─────────────────────────────────────────────────────────────────────
    // Dashboard hero block
    // ─────────────────────────────────────────────────────────────────────

    public async Task<ListeningDashboardDto> GetDashboardAsync(
        string userId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        var profile = await _db.LearnerListeningProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == userId, ct);

        // Profile-missing dashboard — emit safe zeros so the frontend can
        // render a direct listening CTA without erroring.
        if (profile is null)
        {
            return new ListeningDashboardDto(
                ReadinessScore: 0,
                CurrentStage: "audio_check",
                DaysUntilExam: null,
                LastMockScaledScore: null,
                AveragePronunciationRetention: null);
        }

        // Make sure the persisted readiness score reflects the latest skill /
        // accent / mock state. Falling out of the cache here cheaply on each
        // dashboard load is fine — the computation is sub-millisecond.
        var readiness = await CalculateReadinessAsync(userId, ct);

        var lastMock = await _db.ListeningPracticeSessions
            .AsNoTracking()
            .Where(s => s.UserId == userId
                && s.SessionType == "mock"
                && s.CompletedAt != null
                && s.Score != null)
            .OrderByDescending(s => s.CompletedAt)
            .Select(s => new { s.Score })
            .FirstOrDefaultAsync(ct);

        int? lastMockScaled = lastMock?.Score is int raw
            ? OetScoring.OetRawToScaled(raw)
            : null;

        int? daysUntilExam = profile.ExamDate is { } exam
            ? Math.Max(0, (int)Math.Ceiling((exam - _clock.GetUtcNow()).TotalDays))
            : null;

        var pronunciationAvg = await _db.LearnerPronunciationCards
            .AsNoTracking()
            .Where(c => c.UserId == userId && c.LastReviewedAt != null)
            .Select(c => (decimal?)c.RetentionScore)
            .AverageAsync(ct);

        return new ListeningDashboardDto(
            ReadinessScore: readiness,
            CurrentStage: profile.CurrentStage,
            DaysUntilExam: daysUntilExam,
            LastMockScaledScore: lastMockScaled,
            AveragePronunciationRetention: pronunciationAvg);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Skill radar
    // ─────────────────────────────────────────────────────────────────────

    public async Task<SkillRadarDto> GetSkillRadarAsync(
        string userId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        var (skills, _) = await _scoring.GetScoresAsync(userId, ct);
        var byCode = skills.ToDictionary(s => s.SkillCode, StringComparer.OrdinalIgnoreCase);

        var radar = new List<SkillScoreDto>(AllSkillCodes.Length);
        foreach (var code in AllSkillCodes)
        {
            byCode.TryGetValue(code, out var row);
            radar.Add(new SkillScoreDto
            {
                SkillCode = code,
                Label = SkillLabels.TryGetValue(code, out var label) ? label : code,
                CurrentScore = row?.CurrentScore ?? 0m,
                DiagnosticScore = row?.DiagnosticScore ?? 0m,
                QuestionsAttempted = row?.QuestionsAttempted ?? 0,
                QuestionsCorrect = row?.QuestionsCorrect ?? 0,
            });
        }
        return new SkillRadarDto(radar);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Accent chart
    // ─────────────────────────────────────────────────────────────────────

    public async Task<AccentChartDto> GetAccentChartAsync(
        string userId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        var (_, accents) = await _scoring.GetScoresAsync(userId, ct);
        var byAccent = accents.ToDictionary(a => a.Accent, StringComparer.OrdinalIgnoreCase);

        var chart = new List<AccentProgressDto>(AllAccents.Length);
        foreach (var code in AllAccents)
        {
            byAccent.TryGetValue(code, out var row);
            chart.Add(new AccentProgressDto
            {
                Accent = code,
                Label = AccentLabels.TryGetValue(code, out var label) ? label : code,
                AccuracyPercentage = row?.AccuracyPercentage ?? 0m,
                QuestionsAttempted = row?.QuestionsAttempted ?? 0,
                MinutesListened = row?.MinutesListened ?? 0,
                SelfConfidenceRating = row?.SelfConfidenceRating ?? 0,
            });
        }
        return new AccentChartDto(chart);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Score history
    // ─────────────────────────────────────────────────────────────────────

    public async Task<ScoreHistoryDto> GetScoreHistoryAsync(
        string userId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        var sessions = await _db.ListeningPracticeSessions
            .AsNoTracking()
            .Where(s => s.UserId == userId
                && (s.SessionType == "mock" || s.SessionType == "diagnostic")
                && s.CompletedAt != null
                && s.Score != null
                && s.TotalQuestions != null)
            .OrderBy(s => s.CompletedAt)
            .Select(s => new
            {
                CompletedAt = s.CompletedAt!.Value,
                Score = s.Score!.Value,
                Total = s.TotalQuestions!.Value,
            })
            .ToListAsync(ct);

        var points = new List<MockHistoryPoint>(sessions.Count);
        foreach (var s in sessions)
        {
            // Diagnostic sessions are 23-item — project them onto the canonical
            // 42-item raw scale before passing through OetScoring so the line
            // doesn't dip artificially when diagnostics and mocks interleave.
            var rawAt42 = s.Total > 0
                ? (int)Math.Round(
                    (double)s.Score / s.Total * OetScoring.ListeningReadingRawMax,
                    MidpointRounding.AwayFromZero)
                : 0;
            var scaled = OetScoring.OetRawToScaled(rawAt42);
            points.Add(new MockHistoryPoint(
                At: s.CompletedAt,
                RawScore: s.Score,
                ScaledScore: scaled));
        }

        return new ScoreHistoryDto(points);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Readiness
    // ─────────────────────────────────────────────────────────────────────

    public async Task<int> CalculateReadinessAsync(
        string userId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        // Pull all the source rows in one trip each. Sequential, not parallel,
        // because LearnerDbContext doesn't support concurrent operations.
        var profile = await _db.LearnerListeningProfiles
            .FirstOrDefaultAsync(p => p.UserId == userId, ct);
        if (profile is null) return 0;

        var skillRows = await _db.LearnerListeningSkillScores
            .AsNoTracking()
            .Where(s => s.UserId == userId)
            .ToListAsync(ct);

        var accentRows = await _db.LearnerAccentProgresses
            .AsNoTracking()
            .Where(a => a.UserId == userId)
            .ToListAsync(ct);

        // Most-recent N mock sessions for the average score component.
        var recentMocks = await _db.ListeningPracticeSessions
            .AsNoTracking()
            .Where(s => s.UserId == userId
                && s.SessionType == "mock"
                && s.CompletedAt != null
                && s.Score != null)
            .OrderByDescending(s => s.CompletedAt)
            .Take(MockAverageSampleSize)
            .Select(s => new { s.Score, s.TotalQuestions })
            .ToListAsync(ct);

        var pronunciationAvg = await _db.LearnerPronunciationCards
            .AsNoTracking()
            .Where(c => c.UserId == userId && c.LastReviewedAt != null)
            .Select(c => (double?)c.RetentionScore)
            .AverageAsync(ct) ?? 0.0;

        var now = _clock.GetUtcNow();

        // ── Component 1: mock average score (0–100) ─────────────────────
        var mockComponent = 0.0;
        if (recentMocks.Count > 0)
        {
            var scaledScores = recentMocks
                .Where(m => m.Score is int && m.TotalQuestions is int t && t > 0)
                .Select(m =>
                {
                    var rawAt42 = (int)Math.Round(
                        (double)m.Score!.Value / m.TotalQuestions!.Value
                            * OetScoring.ListeningReadingRawMax,
                        MidpointRounding.AwayFromZero);
                    return OetScoring.OetRawToScaled(rawAt42);
                })
                .ToList();

            if (scaledScores.Count > 0)
            {
                mockComponent = Math.Clamp(scaledScores.Average() / 5.0, 0, 100);
            }
        }

        // ── Component 2: lowest sub-skill (0–100) ──────────────────────
        // Floor lifts the radar's weakest spot to the readiness model.
        var lowestSkillComponent = skillRows.Count > 0
            ? Math.Clamp((double)skillRows.Min(s => s.CurrentScore) * 10.0, 0, 100)
            : 0.0;

        // ── Component 3: pronunciation retention (0–100) ───────────────
        var pronunciationComponent = Math.Clamp(pronunciationAvg, 0, 100);

        // ── Component 4: accent confidence avg (0–100) ─────────────────
        // We use AccuracyPercentage (objective) plus self-confidence (1–5)
        // weighted 80/20 because the chart already privileges the objective
        // signal in the radar visualisation.
        var accentComponent = 0.0;
        if (accentRows.Count > 0)
        {
            var objective = (double)accentRows.Average(a => a.AccuracyPercentage);
            var subjective = accentRows.Average(a => a.SelfConfidenceRating);
            // Map 1–5 onto 0–100 (1→0, 5→100) so it sits on the same scale as
            // AccuracyPercentage.
            var subjectiveScaled = (subjective - 1.0) * 25.0;
            accentComponent = Math.Clamp(0.8 * objective + 0.2 * subjectiveScaled, 0, 100);
        }

        // ── Component 5: days-until-exam pacing (0–100) ────────────────
        // Closer exam dates lift readiness (the score reads "ready *enough*
        // given the runway"), with a 30-day window peaking at 100 and falling
        // off linearly out to 180 days where it sits at 0. No exam date means
        // neutral 50 so the score doesn't penalise undated learners.
        var examComponent = 50.0;
        if (profile.ExamDate is { } examDate)
        {
            var daysOut = Math.Max(0, (examDate - now).TotalDays);
            if (daysOut <= 30) examComponent = 100.0;
            else if (daysOut >= 180) examComponent = 0.0;
            else examComponent = Math.Clamp(100.0 - (daysOut - 30) * (100.0 / 150.0), 0, 100);
        }

        // ── Component 6: consistency / streak (0–100) ──────────────────
        // Count distinct days the learner attempted at least one question in
        // the last 14 days. 7+ active days ≈ a strong streak → 100.
        var fourteenDaysAgo = now.AddDays(-14);
        var distinctActiveDays = await _db.ListeningQuestionAttempts
            .AsNoTracking()
            .Where(a => a.UserId == userId && a.AttemptedAt >= fourteenDaysAgo)
            .Select(a => a.AttemptedAt.UtcDateTime.Date)
            .Distinct()
            .CountAsync(ct);

        var consistencyComponent = Math.Clamp(distinctActiveDays / 7.0 * 100.0, 0, 100);

        // ── Weighted sum ────────────────────────────────────────────────
        var readiness =
              WeightMockAverage * mockComponent
            + WeightLowestSkill * lowestSkillComponent
            + WeightPronunciationRetention * pronunciationComponent
            + WeightAccentConfidence * accentComponent
            + WeightDaysUntilExam * examComponent
            + WeightConsistency * consistencyComponent;

        var clamped = (int)Math.Round(
            Math.Clamp(readiness, 0, 100), MidpointRounding.AwayFromZero);

        // Persist if changed — keeps the dashboard chip in sync even when the
        // caller doesn't re-read the dashboard endpoint.
        if (profile.CurrentReadinessScore != clamped)
        {
            profile.CurrentReadinessScore = clamped;
            profile.UpdatedAt = now;
            await _db.SaveChangesAsync(ct);
        }

        return clamped;
    }

    // ─────────────────────────────────────────────────────────────────────
    // Note-taking + spelling stats
    // ─────────────────────────────────────────────────────────────────────

    public async Task<NoteTakingStatsDto> GetNoteTakingStatsAsync(
        string userId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        var totalChars = await _db.ListeningPracticeNotes
            .AsNoTracking()
            .Where(n => n.UserId == userId)
            .SumAsync(n => (int?)n.CharacterCount, ct) ?? 0;

        return new NoteTakingStatsDto
        {
            CharactersTyped = totalChars,
            TypicalRangeLow = TypicalNotesLow,
            TypicalRangeHigh = TypicalNotesHigh,
            // DroppedDetails would require per-question miss tagging that
            // ships in a later phase — emit empty for now so the wire shape
            // is stable.
            DroppedDetails = Array.Empty<string>(),
        };
    }

    public async Task<SpellingStatsDto> GetSpellingStatsAsync(
        string userId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        // Pull a bounded sample of recent attempts where the learner had
        // meaning right but spelling wrong. We resolve the canonical answer
        // via the joined question so the UI can render side-by-side examples.
        var spellingMisses = await _db.ListeningQuestionAttempts
            .AsNoTracking()
            .Where(a => a.UserId == userId && a.IsMeaningCorrectSpellingWrong)
            .OrderByDescending(a => a.AttemptedAt)
            .Take(5)
            .Join(
                _db.ListeningQuestions.AsNoTracking(),
                attempt => attempt.ListeningQuestionId,
                question => question.Id,
                (attempt, question) => new
                {
                    Wrong = attempt.LearnerAnswer ?? attempt.SelectedOption ?? string.Empty,
                    CorrectJson = question.CorrectAnswerJson,
                })
            .ToListAsync(ct);

        var totalMisses = await _db.ListeningQuestionAttempts
            .AsNoTracking()
            .CountAsync(a => a.UserId == userId && a.IsMeaningCorrectSpellingWrong, ct);

        var examples = spellingMisses
            .Select(m => new SpellingExampleDto(
                Wrong: m.Wrong,
                Right: TryReadJsonString(m.CorrectJson) ?? string.Empty))
            .ToList();

        return new SpellingStatsDto
        {
            MeaningCorrectSpellingWrong = totalMisses,
            Examples = examples,
        };
    }

    // ─────────────────────────────────────────────────────────────────────
    // Calendar heatmap
    // ─────────────────────────────────────────────────────────────────────

    public async Task<CalendarHeatmapDto> GetCalendarAsync(
        string userId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        var since = _clock.GetUtcNow().AddDays(-CalendarLookbackDays);

        // Aggregate question attempts by UTC calendar date so the heatmap is
        // stable regardless of the learner's local timezone. Front-end can
        // localise the rendering if needed.
        var rows = await _db.ListeningQuestionAttempts
            .AsNoTracking()
            .Where(a => a.UserId == userId && a.AttemptedAt >= since)
            .GroupBy(a => a.AttemptedAt.UtcDateTime.Date)
            .Select(g => new { Date = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        // Convert to DateOnly + sort ascending for deterministic JSON ordering.
        var days = rows
            .Select(r => new CalendarDay(DateOnly.FromDateTime(r.Date), r.Count))
            .OrderBy(d => d.Date)
            .ToList();

        return new CalendarHeatmapDto(days);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────

    private static string? TryReadJsonString(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<string>(json);
        }
        catch (JsonException)
        {
            return json;
        }
    }
}



