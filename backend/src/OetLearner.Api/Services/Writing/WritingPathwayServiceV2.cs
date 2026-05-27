using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Writing.Events;

namespace OetLearner.Api.Services.Writing;

public sealed record WritingPathwayV2Item(
    Guid Id,
    int OrderIndex,
    string Stage,
    string Phase,
    int WeekNumber,
    string? FocusSkill,
    string? FocusCriterion,
    string ItemKind,
    string? ContentRefId,
    int EstimatedMinutes,
    string Title,
    string Description,
    bool IsCompleted);

public sealed record WritingPathwayV2View(
    string CurrentStage,
    int TotalWeeks,
    int CurrentWeek,
    int WeeksRemaining,
    int ReadinessScore,
    string? PredictedBand,
    DateTimeOffset? GeneratedAt,
    DateTimeOffset? LastRecalculatedAt,
    IReadOnlyDictionary<string, double> WeaknessVector,
    IReadOnlyDictionary<string, double> SubSkillMastery,
    IReadOnlyList<WritingPathwayV2Item> Items);

public interface IWritingPathwayServiceV2
{
    Task<WritingPathwayResponseV2> GetPathwayAsync(string userId, CancellationToken ct);
    Task<WritingPathwayResponseV2> RecalculatePathwayAsync(string userId, CancellationToken ct);
    Task EvaluateStageProgressionAsync(string userId, CancellationToken ct);
    // Internal view used by other services (e.g. WritingOnboardingService.GetDiagnosticResultsAsync).
    Task<WritingPathwayV2View> GetPathwayViewAsync(string userId, CancellationToken ct);
}

public sealed class WritingPathwayServiceV2(
    LearnerDbContext db,
    TimeProvider clock,
    IWritingPathwayGenerator generator,
    IWritingEventBus events,
    ILogger<WritingPathwayServiceV2> logger) : IWritingPathwayServiceV2
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<WritingPathwayResponseV2> GetPathwayAsync(string userId, CancellationToken ct)
    {
        var view = await GetPathwayViewAsync(userId, ct);
        return WritingV2ResponseMapper.ToResponse(view);
    }

    public async Task<WritingPathwayResponseV2> RecalculatePathwayAsync(string userId, CancellationToken ct)
    {
        var view = await RecalculateViewAsync(userId, ct);
        return WritingV2ResponseMapper.ToResponse(view);
    }

    public async Task<WritingPathwayV2View> GetPathwayViewAsync(string userId, CancellationToken ct)
    {
        var profile = await db.LearnerWritingProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == userId, ct);
        var pathway = await db.LearnerWritingPathways.FirstOrDefaultAsync(p => p.UserId == userId, ct);
        var items = await LoadItemsAsync(pathway?.Id, ct);
        if (pathway is null || items.Count == 0)
        {
            return await RecalculateViewAsync(userId, ct);
        }
        var stage = profile?.CurrentStage ?? "foundation";
        return BuildView(profile, pathway, items, stage);
    }

    private async Task<WritingPathwayV2View> RecalculateViewAsync(string userId, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var profile = await db.LearnerWritingProfiles.FirstOrDefaultAsync(p => p.UserId == userId, ct);
        if (profile is null)
        {
            throw ApiException.NotFound("writing_profile_missing", "Complete Writing onboarding before generating a pathway.");
        }

        var pathway = await db.LearnerWritingPathways.FirstOrDefaultAsync(p => p.UserId == userId, ct);
        if (pathway is null)
        {
            pathway = new LearnerWritingPathway
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                GeneratedAt = now,
            };
            db.LearnerWritingPathways.Add(pathway);
            await db.SaveChangesAsync(ct);
        }

        var (scores, diagnosticSubmissionId) = await LoadDiagnosticScoresAsync(userId, ct);
        var subSkillBaseline = await LoadSubSkillMasteryAsync(userId, ct);
        var input = new WritingPathwayGenerationInput(
            Profession: profile.Profession,
            TargetBand: profile.TargetBand,
            ExamDate: profile.ExamDate,
            Now: now,
            DaysPerWeek: profile.DaysPerWeek,
            MinutesPerDay: profile.MinutesPerDay,
            LetterTypeFocus: DeserializeStringList(profile.LetterTypeFocusJson),
            DiagnosticScores: scores,
            SubSkillBaseline: subSkillBaseline);

        var planned = generator.Generate(input);
        var weakness = generator.ComputeWeaknessVector(scores);

        var existing = await db.WritingPathwayItems.Where(i => i.PathwayId == pathway.Id).ToListAsync(ct);
        db.WritingPathwayItems.RemoveRange(existing);

        foreach (var item in planned)
        {
            db.WritingPathwayItems.Add(new WritingPathwayItem
            {
                Id = Guid.NewGuid(),
                PathwayId = pathway.Id,
                OrderIndex = item.OrderIndex,
                Stage = item.Stage,
                Phase = item.Phase,
                WeekNumber = item.WeekNumber,
                FocusSkill = item.FocusSkill,
                FocusCriterion = item.FocusCriterion,
                ItemKind = item.ItemKind,
                ContentRefId = item.ContentRefId,
                Status = "pending",
                CreatedAt = now,
            });
        }

        pathway.TotalWeeks = planned.Count == 0 ? 10 : planned.Max(i => i.WeekNumber);
        pathway.GeneratedAt = now;
        pathway.LastRecalculatedAt = now;
        pathway.WeaknessVectorJson = JsonSerializer.Serialize(weakness, JsonOptions);
        pathway.SubSkillMasteryJson = JsonSerializer.Serialize(subSkillBaseline, JsonOptions);
        pathway.DiagnosticSubmissionId = diagnosticSubmissionId;
        profile.PathwayGeneratedAt = now;
        profile.UpdatedAt = now;

        await db.SaveChangesAsync(ct);

        await events.PublishAsync(new WritingPathwayUpdated(
            UserId: userId,
            PathwayId: pathway.Id,
            TotalItems: planned.Count,
            Stage: profile.CurrentStage,
            OccurredAt: now), ct);

        var items = await LoadItemsAsync(pathway.Id, ct);
        return BuildView(profile, pathway, items, profile.CurrentStage);
    }

    public async Task EvaluateStageProgressionAsync(string userId, CancellationToken ct)
    {
        var profile = await db.LearnerWritingProfiles.FirstOrDefaultAsync(p => p.UserId == userId, ct);
        if (profile is null) return;

        var (scores, _) = await LoadDiagnosticScoresAsync(userId, ct);
        var hasDiagnostic = scores.RawTotal > 0;
        if (!hasDiagnostic)
        {
            return;
        }

        var lastFive = await db.WritingGrades.AsNoTracking()
            .Join(db.WritingSubmissions.AsNoTracking(), g => g.SubmissionId, s => s.Id, (g, s) => new { g, s })
            .Where(x => x.s.UserId == userId)
            .OrderByDescending(x => x.g.GradedAt)
            .Take(5)
            .Select(x => new { x.g.RawTotal, x.g.BandLabel })
            .ToListAsync(ct);

        var bandsTarget = profile.TargetBand switch
        {
            "A" => 38,
            "B+" => 34,
            _ => 30,
        };

        var avg = lastFive.Count == 0 ? 0 : lastFive.Average(r => (double)r.RawTotal);
        var targetMatches = lastFive.Count(r => r.RawTotal >= bandsTarget);

        var nextStage = profile.CurrentStage switch
        {
            "onboarding" => "diagnostic",
            "diagnostic" => "foundation",
            "foundation" when targetMatches >= 2 && avg >= bandsTarget - 4 => "practice",
            "practice" when targetMatches >= 3 && avg >= bandsTarget - 1 => "mastery",
            _ => profile.CurrentStage,
        };

        if (!string.Equals(nextStage, profile.CurrentStage, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogInformation("Writing pathway stage progression user={UserId} {From}->{To}", userId, profile.CurrentStage, nextStage);
            profile.CurrentStage = nextStage;
            profile.UpdatedAt = clock.GetUtcNow();
            await db.SaveChangesAsync(ct);
        }
    }

    private async Task<(WritingCriterionScores Scores, Guid? SubmissionId)> LoadDiagnosticScoresAsync(string userId, CancellationToken ct)
    {
        var diagnostic = await db.WritingSubmissions.AsNoTracking()
            .Where(s => s.UserId == userId && s.Mode == "diagnostic" && s.Status == "graded")
            .OrderByDescending(s => s.SubmittedAt)
            .Select(s => new { s.Id })
            .FirstOrDefaultAsync(ct);
        if (diagnostic is null) return (WritingCriterionScores.Empty, null);

        var grade = await db.WritingGrades.AsNoTracking()
            .Where(g => g.SubmissionId == diagnostic.Id)
            .OrderByDescending(g => g.GradedAt)
            .FirstOrDefaultAsync(ct);
        if (grade is null) return (WritingCriterionScores.Empty, diagnostic.Id);

        var scores = new WritingCriterionScores(
            grade.C1Purpose, grade.C2Content, grade.C3Conciseness, grade.C4Genre, grade.C5Organisation, grade.C6Language);
        return (scores, diagnostic.Id);
    }

    private async Task<IReadOnlyDictionary<string, int>> LoadSubSkillMasteryAsync(string userId, CancellationToken ct)
    {
        var completions = await db.WritingLessonCompletionsV2.AsNoTracking()
            .Where(c => c.UserId == userId)
            .Select(c => c.LessonId)
            .ToListAsync(ct);
        if (completions.Count == 0)
        {
            return new Dictionary<string, int>();
        }
        var lessons = await db.WritingLessonsV2.AsNoTracking()
            .Where(l => completions.Contains(l.Id))
            .Select(l => l.SubSkill)
            .ToListAsync(ct);
        return lessons.GroupBy(s => s).ToDictionary(g => g.Key, g => g.Count());
    }

    private async Task<IReadOnlyList<WritingPathwayItem>> LoadItemsAsync(Guid? pathwayId, CancellationToken ct)
    {
        if (pathwayId is null) return Array.Empty<WritingPathwayItem>();
        return await db.WritingPathwayItems.AsNoTracking()
            .Where(i => i.PathwayId == pathwayId.Value)
            .OrderBy(i => i.OrderIndex)
            .ToListAsync(ct);
    }

    private static WritingPathwayV2View BuildView(
        LearnerWritingProfile? profile,
        LearnerWritingPathway pathway,
        IReadOnlyList<WritingPathwayItem> items,
        string stage)
    {
        var weakness = DeserializeDoubleMap(pathway.WeaknessVectorJson);
        var mastery = DeserializeIntegerMapAsDouble(pathway.SubSkillMasteryJson);
        var totalWeeks = pathway.TotalWeeks;
        var generated = pathway.GeneratedAt;
        var now = DateTimeOffset.UtcNow;
        var currentWeek = totalWeeks <= 0 ? 1 : Math.Clamp(((int)((now - generated).TotalDays / 7.0)) + 1, 1, totalWeeks);
        var weeksRemaining = Math.Max(0, totalWeeks - currentWeek + 1);
        var planned = items.Select(i => new WritingPathwayV2Item(
            i.Id,
            i.OrderIndex,
            i.Stage,
            i.Phase,
            i.WeekNumber ?? 1,
            i.FocusSkill,
            i.FocusCriterion,
            i.ItemKind,
            i.ContentRefId,
            EstimatedMinutes: 10,
            Title: BuildTitle(i),
            Description: BuildDescription(i),
            IsCompleted: i.Status == "completed")).ToList();
        return new WritingPathwayV2View(
            stage,
            totalWeeks,
            currentWeek,
            weeksRemaining,
            profile?.CurrentReadinessScore ?? 0,
            profile?.TargetBand,
            generated,
            pathway.LastRecalculatedAt,
            weakness,
            mastery,
            planned);
    }

    private static string BuildTitle(WritingPathwayItem item) => item.ItemKind switch
    {
        "lesson" => $"Lesson — {item.FocusSkill}",
        "drill" => "Drill",
        "letter" => $"Letter — {item.ContentRefId ?? "LT-RR"}",
        "mock" => "Mock exam",
        "exemplar-review" => "Exemplar review",
        "canon-refresher" => "Canon refresher",
        _ => "Pathway item",
    };

    private static string BuildDescription(WritingPathwayItem item)
        => $"Stage {item.Stage} · Week {item.WeekNumber}";

    private static List<string> DeserializeStringList(string json)
    {
        try { return JsonSerializer.Deserialize<List<string>>(json, JsonOptions) ?? []; }
        catch (JsonException) { return []; }
    }

    private static IReadOnlyDictionary<string, double> DeserializeDoubleMap(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new Dictionary<string, double>();
        try { return JsonSerializer.Deserialize<Dictionary<string, double>>(json, JsonOptions) ?? new(); }
        catch (JsonException) { return new Dictionary<string, double>(); }
    }

    private static IReadOnlyDictionary<string, double> DeserializeIntegerMapAsDouble(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new Dictionary<string, double>();
        try
        {
            var map = JsonSerializer.Deserialize<Dictionary<string, int>>(json, JsonOptions) ?? new();
            return map.ToDictionary(kvp => kvp.Key, kvp => (double)kvp.Value);
        }
        catch (JsonException) { return new Dictionary<string, double>(); }
    }
}
