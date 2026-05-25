using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using System.Text.Json;

namespace OetLearner.Api.Services.Reading;

// ═════════════════════════════════════════════════════════════════════════════
// Reading Learner Pathway Service — WS2
//
// Implements the 5-stage learning pathway:
//   onboarding → diagnostic → foundation → practice → mastery
//
// NAMING: This is IReadingLearnerPathwayService (NOT IReadingPathwayService
// which already exists in ReadingPathwayService.cs for the snapshot pathway).
//
// TYPE NOTE: ReadingQuestion.Id is string; ReadingQuestionAttempt.
// ReadingQuestionId is Guid — these cannot be FK-joined in EF Core.
// Skill-tag metadata is written into ReadingPracticeSession.MetadataJson at
// session creation time so SkillScoringService can read it without a join.
// ═════════════════════════════════════════════════════════════════════════════

// Interface for the NEW 5-stage pathway system (NOT the existing IReadingPathwayService)
public interface IReadingLearnerPathwayService
{
    Task<LearnerReadingProfile> StartOnboardingAsync(string userId, StartOnboardingRequest request, CancellationToken ct);
    Task<StartDiagnosticResult> StartDiagnosticAsync(string userId, CancellationToken ct);
    Task<DiagnosticSubmitResult> SubmitDiagnosticAsync(string userId, Guid sessionId, Dictionary<string, string> answers, CancellationToken ct);
    Task<LearnerReadingPathway> GeneratePathwayAsync(string userId, CancellationToken ct);
    Task<PathwayStatusDto> GetCurrentStageAsync(string userId, CancellationToken ct);
    Task AdvanceStageAsync(string userId, string newStage, CancellationToken ct);
}

// ── Request / Result DTOs ────────────────────────────────────────────────────

public sealed record StartOnboardingRequest(
    string TargetBand,
    DateTimeOffset? ExamDate,
    int HoursPerWeek,
    string Profession,
    bool HasTakenBefore,
    int? PreviousScore,
    int SelfRatedSpeed,
    int SelfRatedVocabulary);

public sealed record StartDiagnosticResult(
    Guid SessionId,
    List<string> QuestionIds,   // string IDs matching ReadingQuestion.Id
    int TimeLimitMinutes);

public sealed record DiagnosticSubmitResult(
    int Score,
    int TotalQuestions,
    Dictionary<string, decimal> SkillScores,  // S1..S8 -> score 0-10
    string EstimatedOetBand,
    int? EstimatedScaledScore);

public sealed record PathwayStatusDto(
    string CurrentStage,
    int? ReadinessScore,
    int? PredictedScore,
    DateTimeOffset? ExamDate,
    int? WeeksRemaining);

public sealed record PathwayWeekDto(
    int WeekNumber,
    string Phase,
    List<string> FocusSkills,
    string Theme,
    bool MockScheduled,
    bool IsCompleted);

// ── Service Implementation ───────────────────────────────────────────────────

public sealed class ReadingLearnerPathwayService(
    LearnerDbContext db,
    ISkillScoringService skillScoring) : IReadingLearnerPathwayService
{
    /// <summary>
    /// Upsert learner's onboarding profile and advance stage to "diagnostic".
    /// Idempotent — calling again with updated values overwrites in place.
    /// </summary>
    public async Task<LearnerReadingProfile> StartOnboardingAsync(
        string userId, StartOnboardingRequest request, CancellationToken ct)
    {
        var existing = await db.LearnerReadingProfiles
            .FirstOrDefaultAsync(p => p.UserId == userId, ct);

        if (existing is not null)
        {
            existing.TargetBand = request.TargetBand;
            existing.ExamDate = request.ExamDate;
            existing.HoursPerWeek = request.HoursPerWeek;
            existing.Profession = request.Profession;
            existing.HasTakenBefore = request.HasTakenBefore;
            existing.PreviousScore = request.PreviousScore;
            existing.SelfRatedSpeed = request.SelfRatedSpeed;
            existing.SelfRatedVocabulary = request.SelfRatedVocabulary;
            existing.CurrentStage = "diagnostic";
            existing.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            return existing;
        }

        var profile = new LearnerReadingProfile
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TargetBand = request.TargetBand,
            ExamDate = request.ExamDate,
            HoursPerWeek = request.HoursPerWeek,
            Profession = request.Profession,
            HasTakenBefore = request.HasTakenBefore,
            PreviousScore = request.PreviousScore,
            SelfRatedSpeed = request.SelfRatedSpeed,
            SelfRatedVocabulary = request.SelfRatedVocabulary,
            CurrentStage = "diagnostic",
            OnboardingCompletedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        db.LearnerReadingProfiles.Add(profile);
        await db.SaveChangesAsync(ct);
        return profile;
    }

    /// <summary>
    /// Select 22 diagnostic questions (10 Part A, 4 Part B, 8 Part C) and
    /// create a ReadingPracticeSession row.  The session's MetadataJson carries
    /// a {attemptRowId → skillTag} map that SkillScoringService consumes during
    /// grading (needed because ReadingQuestion.Id is string but
    /// ReadingQuestionAttempt.ReadingQuestionId is Guid — EF can't join them).
    ///
    /// At diagnostic-start we don't yet have attempt rows, so we store the
    /// question skill tags in a pre-computed map keyed by question string ID:
    /// {"questionSkillMap": {"<questionStringId>": "S3", ...}}.
    /// SubmitDiagnosticAsync then mints attempt rows and stores the final
    /// per-attemptId map.
    /// </summary>
    public async Task<StartDiagnosticResult> StartDiagnosticAsync(string userId, CancellationToken ct)
    {
        // Select published questions per part; fall back to any published if counts short.
        var partAIds = await db.ReadingQuestions
            .AsNoTracking()
            .Where(q => q.Part != null
                && q.Part.PartCode == ReadingPartCode.A
                && q.ReviewState == ReadingReviewState.Published)
            .OrderBy(_ => EF.Functions.Random())
            .Take(10)
            .Select(q => new { q.Id, q.SkillTag })
            .ToListAsync(ct);

        var partBIds = await db.ReadingQuestions
            .AsNoTracking()
            .Where(q => q.Part != null
                && q.Part.PartCode == ReadingPartCode.B
                && q.ReviewState == ReadingReviewState.Published)
            .OrderBy(_ => EF.Functions.Random())
            .Take(4)
            .Select(q => new { q.Id, q.SkillTag })
            .ToListAsync(ct);

        var partCIds = await db.ReadingQuestions
            .AsNoTracking()
            .Where(q => q.Part != null
                && q.Part.PartCode == ReadingPartCode.C
                && q.ReviewState == ReadingReviewState.Published)
            .OrderBy(_ => EF.Functions.Random())
            .Take(8)
            .Select(q => new { q.Id, q.SkillTag })
            .ToListAsync(ct);

        var selected = partAIds.Concat(partBIds).Concat(partCIds).ToList();
        var selectedIds = selected.Select(q => q.Id).ToHashSet();

        // Fallback: top up to 22 from any published questions not already selected
        if (selected.Count < 22)
        {
            var fallback = await db.ReadingQuestions
                .AsNoTracking()
                .Where(q => q.ReviewState == ReadingReviewState.Published
                    && !selectedIds.Contains(q.Id))
                .OrderBy(_ => EF.Functions.Random())
                .Take(22 - selected.Count)
                .Select(q => new { q.Id, q.SkillTag })
                .ToListAsync(ct);
            selected.AddRange(fallback);
        }

        var allIds = selected.Select(q => q.Id).ToList();

        // Build question → skill map for SkillScoringService (stored in MetadataJson)
        var questionSkillMap = selected.ToDictionary(
            q => q.Id,
            q => q.SkillTag ?? "S1");

        var metadata = JsonSerializer.Serialize(new { questionSkillMap });

        var session = new ReadingPracticeSession
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            SessionType = "diagnostic",
            QuestionIdsJson = JsonSerializer.Serialize(allIds),
            TotalQuestions = allIds.Count,
            StartedAt = DateTimeOffset.UtcNow,
            MetadataJson = metadata
        };
        db.ReadingPracticeSessions.Add(session);
        await db.SaveChangesAsync(ct);

        return new StartDiagnosticResult(session.Id, allIds, 25);
    }

    /// <summary>
    /// Grade the diagnostic, record attempt rows, update skill scores, generate
    /// the pathway, and advance stage to "foundation".
    /// </summary>
    public async Task<DiagnosticSubmitResult> SubmitDiagnosticAsync(
        string userId, Guid sessionId, Dictionary<string, string> answers, CancellationToken ct)
    {
        var session = await db.ReadingPracticeSessions
            .FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId, ct)
            ?? throw new InvalidOperationException("Session not found");

        var questionIds = JsonSerializer.Deserialize<List<string>>(session.QuestionIdsJson) ?? [];

        // Load actual question data for grading
        var questions = await db.ReadingQuestions
            .AsNoTracking()
            .Where(q => questionIds.Contains(q.Id))
            .ToListAsync(ct);

        // Re-read questionSkillMap from MetadataJson
        var questionSkillMap = new Dictionary<string, string>();
        if (!string.IsNullOrEmpty(session.MetadataJson) && session.MetadataJson != "{}")
        {
            try
            {
                using var doc = JsonDocument.Parse(session.MetadataJson);
                if (doc.RootElement.TryGetProperty("questionSkillMap", out var mapEl)
                    && mapEl.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in mapEl.EnumerateObject())
                        questionSkillMap[prop.Name] = prop.Value.GetString() ?? "S1";
                }
            }
            catch (JsonException) { /* fall back to S1 */ }
        }

        var attempts = new List<ReadingQuestionAttempt>();
        // Build per-attempt skill map for SkillScoringService
        var attemptSkillMap = new Dictionary<string, string>();
        int correct = 0;

        foreach (var q in questions)
        {
            answers.TryGetValue(q.Id, out var selected);
            bool isUnknown = selected == "__unknown__";
            bool isCorrect = false;

            if (!isUnknown && selected is not null)
            {
                isCorrect = CheckAnswer(q, selected);
                if (isCorrect) correct++;
            }

            var attempt = new ReadingQuestionAttempt
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                // ReadingQuestionId is Guid on the entity; we can only store a
                // generated sentinel here since the source question id is string.
                // A real migration-level fix would change ReadingQuestion.Id to Guid,
                // but for now we track correlation through the session MetadataJson.
                ReadingQuestionId = Guid.NewGuid(),
                PracticeSessionId = sessionId,
                SelectedOption = selected,
                IsCorrect = isCorrect,
                IsUnknown = isUnknown,
                AttemptedAt = DateTimeOffset.UtcNow,
                // wrong + not unknown → spaced-repetition review queue
                InReviewQueue = !isCorrect && !isUnknown,
                NextReviewAt = (!isCorrect && !isUnknown)
                    ? DateTimeOffset.UtcNow.AddDays(1)
                    : null
            };
            attempts.Add(attempt);

            // Map the new attempt's Guid to its skill code so SkillScoringService
            // can group correctly via the MetadataJson skillTagMap.
            var skillCode = questionSkillMap.TryGetValue(q.Id, out var tag) ? tag : "S1";
            attemptSkillMap[attempt.Id.ToString()] = skillCode;
        }

        db.ReadingQuestionAttempts.AddRange(attempts);

        // Update session metadata with the per-attempt skill map (replaces question map)
        var updatedMetadata = JsonSerializer.Serialize(new { skillTagMap = attemptSkillMap });
        session.MetadataJson = updatedMetadata;
        session.CompletedAt = DateTimeOffset.UtcNow;
        session.Score = correct;
        session.TotalQuestions = questions.Count;
        session.DurationSeconds = (int)(DateTimeOffset.UtcNow - session.StartedAt).TotalSeconds;

        await db.SaveChangesAsync(ct);

        // Update skill score baseline from this session
        await skillScoring.RecalculateDiagnosticBaselineAsync(userId, sessionId, ct);

        // Generate the multi-week pathway
        await GeneratePathwayAsync(userId, ct);

        // Advance to foundation stage
        await AdvanceStageAsync(userId, "foundation", ct);

        var skillScores = await skillScoring.GetCurrentScoresAsync(userId, ct);

        int totalQ = questions.Count;
        decimal accuracy = totalQ > 0 ? (decimal)correct / totalQ : 0m;
        int estimatedScaled = EstimateScaledScore(accuracy);

        return new DiagnosticSubmitResult(
            Score: correct,
            TotalQuestions: totalQ,
            SkillScores: skillScores,
            EstimatedOetBand: ScaledToOetBand(estimatedScaled),
            EstimatedScaledScore: estimatedScaled);
    }

    /// <summary>
    /// Generate (or regenerate) the multi-week study pathway based on current
    /// skill scores and available weeks to exam.
    /// </summary>
    public async Task<LearnerReadingPathway> GeneratePathwayAsync(string userId, CancellationToken ct)
    {
        var profile = await db.LearnerReadingProfiles
            .FirstOrDefaultAsync(p => p.UserId == userId, ct)
            ?? throw new InvalidOperationException("Learner profile not found — complete onboarding first.");

        var skillScores = await skillScoring.GetCurrentScoresAsync(userId, ct);

        int weeksToExam = profile.ExamDate.HasValue
            ? Math.Max(1, (int)Math.Ceiling((profile.ExamDate.Value - DateTimeOffset.UtcNow).TotalDays / 7))
            : 12;
        weeksToExam = Math.Clamp(weeksToExam, 4, 24);

        // Weakest skills (score < 6) in ascending order drive phase focus
        var weakSkills = skillScores
            .Where(kvp => kvp.Value < 6m)
            .OrderBy(kvp => kvp.Value)
            .Select(kvp => kvp.Key)
            .ToList();

        var weeks = new List<PathwayWeekDto>();

        // Foundation phase — 15% of total, min 1 week
        int foundationWeeks = Math.Max(1, Math.Min(2, (int)Math.Ceiling(weeksToExam * 0.15)));

        for (int w = 0; w < foundationWeeks; w++)
        {
            weeks.Add(new PathwayWeekDto(
                WeekNumber: w + 1,
                Phase: "foundation",
                FocusSkills: weakSkills.Take(3).ToList(),
                Theme: "Sub-skill foundation",
                MockScheduled: false,
                IsCompleted: false));
        }

        // Practice phase — 50% of total
        int practiceWeeks = (int)(weeksToExam * 0.50);

        for (int w = 0; w < practiceWeeks; w++)
        {
            string focusSkill = weakSkills.Count > 0
                ? weakSkills[w % weakSkills.Count]
                : "S1";
            bool mockThisWeek = practiceWeeks > 3 && w % 4 == 3;

            weeks.Add(new PathwayWeekDto(
                WeekNumber: foundationWeeks + w + 1,
                Phase: "practice",
                FocusSkills: [focusSkill],
                Theme: $"Targeting {focusSkill}",
                MockScheduled: mockThisWeek,
                IsCompleted: false));
        }

        // Mastery phase — remaining weeks
        int masteryStart = foundationWeeks + practiceWeeks;
        int masteryWeeks = weeksToExam - masteryStart;

        for (int w = 0; w < masteryWeeks; w++)
        {
            weeks.Add(new PathwayWeekDto(
                WeekNumber: masteryStart + w + 1,
                Phase: "mastery",
                FocusSkills: ["mixed"],
                Theme: "Mock tests + exam strategies",
                MockScheduled: true,
                IsCompleted: false));
        }

        var weeksJson = JsonSerializer.Serialize(weeks);
        var now = DateTimeOffset.UtcNow;

        // Upsert pathway row
        var existing = await db.LearnerReadingPathways
            .FirstOrDefaultAsync(p => p.UserId == userId, ct);

        if (existing is not null)
        {
            existing.TotalWeeks = weeks.Count;
            existing.GeneratedAt = now;
            existing.WeeksJson = weeksJson;
        }
        else
        {
            existing = new LearnerReadingPathway
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                TotalWeeks = weeks.Count,
                GeneratedAt = now,
                WeeksJson = weeksJson
            };
            db.LearnerReadingPathways.Add(existing);
        }

        profile.PathwayGeneratedAt = now;
        profile.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        return existing;
    }

    public async Task<PathwayStatusDto> GetCurrentStageAsync(string userId, CancellationToken ct)
    {
        var profile = await db.LearnerReadingProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == userId, ct);

        if (profile is null)
            return new PathwayStatusDto("onboarding", null, null, null, null);

        int? weeksRemaining = profile.ExamDate.HasValue
            ? (int?)Math.Max(0, (int)Math.Ceiling(
                (profile.ExamDate.Value - DateTimeOffset.UtcNow).TotalDays / 7))
            : null;

        return new PathwayStatusDto(
            CurrentStage: profile.CurrentStage,
            ReadinessScore: profile.CurrentReadinessScore,
            PredictedScore: profile.PredictedScore,
            ExamDate: profile.ExamDate,
            WeeksRemaining: weeksRemaining);
    }

    public async Task AdvanceStageAsync(string userId, string newStage, CancellationToken ct)
    {
        var profile = await db.LearnerReadingProfiles
            .FirstOrDefaultAsync(p => p.UserId == userId, ct);
        if (profile is null) return;

        profile.CurrentStage = newStage;
        profile.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Grade a single answer against ReadingQuestion.CorrectAnswerJson.
    ///
    /// MCQ types (MultipleChoice3 / MultipleChoice4): CorrectAnswerJson is a
    /// JSON string "A", "B", "C", or "D".  Compare case-insensitively.
    ///
    /// ShortAnswer / SentenceCompletion / MatchingTextReference: try
    /// AcceptedSynonymsJson first, then fall back to CorrectAnswerJson literal.
    /// CaseSensitive flag is honoured.
    /// </summary>
    private static bool CheckAnswer(ReadingQuestion q, string selected)
    {
        if (string.IsNullOrWhiteSpace(selected)) return false;

        // Deserialise the correct answer (stored as a JSON string or array)
        string? correctAnswer = null;
        try
        {
            using var doc = JsonDocument.Parse(q.CorrectAnswerJson);
            correctAnswer = doc.RootElement.ValueKind == JsonValueKind.String
                ? doc.RootElement.GetString()
                : q.CorrectAnswerJson; // fallback: use raw value
        }
        catch (JsonException)
        {
            correctAnswer = q.CorrectAnswerJson;
        }

        if (correctAnswer is null) return false;

        StringComparison comparison = q.CaseSensitive
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;

        // MCQ: direct key comparison
        if (q.QuestionType is ReadingQuestionType.MultipleChoice3
            or ReadingQuestionType.MultipleChoice4)
        {
            return string.Equals(correctAnswer.Trim(), selected.Trim(), comparison);
        }

        // Short answer / sentence completion: check AcceptedSynonymsJson first
        if (!string.IsNullOrEmpty(q.AcceptedSynonymsJson))
        {
            try
            {
                var synonyms = JsonSerializer.Deserialize<List<string>>(q.AcceptedSynonymsJson);
                if (synonyms is not null
                    && synonyms.Any(s => string.Equals(s.Trim(), selected.Trim(), comparison)))
                {
                    return true;
                }
            }
            catch (JsonException) { /* fall through to literal check */ }
        }

        // Fallback: exact match against the canonical correct answer
        return string.Equals(correctAnswer.Trim(), selected.Trim(), comparison);
    }

    /// <summary>Rough linear estimate: 0% → 80, 100% → 500.</summary>
    private static int EstimateScaledScore(decimal accuracy)
        => (int)Math.Round(accuracy * 420m + 80m);

    private static string ScaledToOetBand(int scaled) => scaled switch
    {
        >= 450 => "A",
        >= 400 => "B+",
        >= 350 => "B",
        >= 300 => "C+",
        >= 250 => "C",
        _ => "E"
    };
}
