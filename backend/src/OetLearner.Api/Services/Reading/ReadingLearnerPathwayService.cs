using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using System.Text.Json;

namespace OetLearner.Api.Services.Reading;

// ═════════════════════════════════════════════════════════════════════════════
// Reading Learner Pathway Service — WS2
//
// Implements the learning pathway:
//   foundation → practice → mastery
//
// NAMING: This is IReadingLearnerPathwayService (NOT IReadingPathwayService
// which already exists in ReadingPathwayService.cs for the snapshot pathway).
//
// TYPE NOTE: ReadingQuestion.Id is string; ReadingQuestionAttempt.
// ReadingQuestionId is Guid — these cannot be FK-joined in EF Core.
// Skill-tag metadata is written into ReadingPracticeSession.MetadataJson at
// session creation time so SkillScoringService can read it without a join.
// ═════════════════════════════════════════════════════════════════════════════

// Interface for the pathway system (NOT the existing IReadingPathwayService)
public interface IReadingLearnerPathwayService
{
    Task<LearnerReadingPathway> GeneratePathwayAsync(string userId, CancellationToken ct);
    Task<PathwayStatusDto> GetCurrentStageAsync(string userId, CancellationToken ct);
    Task AdvanceStageAsync(string userId, string newStage, CancellationToken ct);
}

// ── Request / Result DTOs ────────────────────────────────────────────────────

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
    /// Generate (or regenerate) the multi-week study pathway based on current
    /// skill scores and available weeks to exam.
    /// </summary>
    public async Task<LearnerReadingPathway> GeneratePathwayAsync(string userId, CancellationToken ct)
    {
        var profile = await db.LearnerReadingProfiles
            .FirstOrDefaultAsync(p => p.UserId == userId, ct)
            ?? throw new InvalidOperationException("Learner profile not found — start the reading flow first.");

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
            return new PathwayStatusDto("foundation", null, null, null, null);

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

}


