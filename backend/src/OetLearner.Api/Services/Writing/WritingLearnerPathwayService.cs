using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Services.Writing;

public interface IWritingLearnerPathwayService
{
    Task<WritingProfileResponse> GetProfileAsync(string userId, CancellationToken ct);
    Task<WritingProfileResponse> SaveOnboardingAsync(string userId, WritingStartOnboardingRequest request, CancellationToken ct);
    Task<WritingPathwayResponse> GetPathwayAsync(string userId, CancellationToken ct);
    Task<WritingTodayPlanResponse> GetTodayPlanAsync(string userId, CancellationToken ct);
    Task StartPlanItemAsync(string userId, Guid itemId, CancellationToken ct);
    Task CompletePlanItemAsync(string userId, Guid itemId, CancellationToken ct);
    Task SkipPlanItemAsync(string userId, Guid itemId, string? reason, CancellationToken ct);
    Task<WritingCanonResponse> GetCanonAsync(string userId, string? search, string? severity, CancellationToken ct);
}

public sealed class WritingLearnerPathwayService(LearnerDbContext db, TimeProvider clock, IRulebookLoader rulebookLoader) : IWritingLearnerPathwayService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly Regex ScaledRangeRegex = new(@"\b(\d{2,3})\s*-\s*(\d{2,3})\b", RegexOptions.Compiled);
    private static readonly Regex ScaledScoreRegex = new(@"\b(\d{2,3})(?:\s*/\s*500)?\b", RegexOptions.Compiled);

    public async Task<WritingProfileResponse> GetProfileAsync(string userId, CancellationToken ct)
    {
        var profile = await db.LearnerWritingProfiles.AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == userId, ct);
        var state = await BuildStateAsync(userId, profile, ct);
        return ToProfileResponse(userId, profile, state, clock.GetUtcNow());
    }

    public async Task<WritingProfileResponse> SaveOnboardingAsync(string userId, WritingStartOnboardingRequest request, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var profile = await db.LearnerWritingProfiles.FirstOrDefaultAsync(p => p.UserId == userId, ct);
        var focus = NormalizeLetterTypes(request.LetterTypeFocus);

        if (profile is null)
        {
            profile = new LearnerWritingProfile
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                OnboardingCompletedAt = now,
            };
            db.LearnerWritingProfiles.Add(profile);
        }

        profile.Profession = NormalizeProfession(request.Profession);
        profile.TargetBand = NormalizeTargetBand(request.TargetBand);
        profile.ExamDate = request.ExamDate;
        profile.DaysPerWeek = Math.Clamp(request.DaysPerWeek, 1, 7);
        profile.MinutesPerDay = Math.Clamp(request.MinutesPerDay, 15, 180);
        profile.TargetCountry = NormalizeWritingTargetCountry(request.TargetCountry);
        profile.LetterTypeFocusJson = JsonSerializer.Serialize(focus, JsonOptions);
        profile.CurrentStage = "diagnostic";
        profile.UpdatedAt = now;

        var goal = await db.Goals.FirstOrDefaultAsync(g => g.UserId == userId, ct);
        if (goal is not null)
        {
            goal.TargetCountry = TargetCountryOptions.Canonicalize(profile.TargetCountry);
            goal.ProfessionId = profile.Profession;
            goal.TargetExamDate = profile.ExamDate is null
                ? goal.TargetExamDate
                : DateOnly.FromDateTime(profile.ExamDate.Value.UtcDateTime);
            goal.UpdatedAt = now;
        }

        await EnsurePathwayAsync(profile, ct, forceRegenerate: true);
        await ClearTodayPlanAsync(userId, ct);
        await db.SaveChangesAsync(ct);

        var state = await BuildStateAsync(userId, profile, ct);
        return ToProfileResponse(userId, profile, state, clock.GetUtcNow());
    }

    public async Task<WritingPathwayResponse> GetPathwayAsync(string userId, CancellationToken ct)
    {
        var profile = await db.LearnerWritingProfiles.FirstOrDefaultAsync(p => p.UserId == userId, ct);
        if (profile is null)
        {
            var emptyWeeks = BuildDefaultWeeks("medicine", ["LT-RR", "LT-DG", "LT-UR"]);
            return new WritingPathwayResponse("onboarding", 10, 1, 10, 0, null, null, emptyWeeks);
        }

        var pathway = await EnsurePathwayAsync(profile, ct);
        await db.SaveChangesAsync(ct);
        var state = await BuildStateAsync(userId, profile, ct);
        var weeks = DeserializeWeeks(pathway.WeeksJson);
        if (weeks.Count == 0)
        {
            weeks = BuildDefaultWeeks(profile.Profession, DeserializeStringList(profile.LetterTypeFocusJson), pathway.TotalWeeks);
        }
        var currentWeek = CalculateCurrentWeek(pathway.GeneratedAt, pathway.TotalWeeks, clock.GetUtcNow());
        return new WritingPathwayResponse(
            state.Stage,
            pathway.TotalWeeks,
            currentWeek,
            Math.Max(0, pathway.TotalWeeks - currentWeek + 1),
            state.ReadinessScore,
            state.PredictedScore,
            pathway.GeneratedAt,
            weeks.Select(w => w with { IsCompleted = IsWeekCompleted(w, state.Stage, currentWeek) }).ToList());
    }

    public async Task<WritingTodayPlanResponse> GetTodayPlanAsync(string userId, CancellationToken ct)
    {
        var date = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var profile = await db.LearnerWritingProfiles.FirstOrDefaultAsync(p => p.UserId == userId, ct);
        var state = await BuildStateAsync(userId, profile, ct);
        var existing = await db.WritingDailyPlanItems
            .Where(i => i.UserId == userId && i.PlanDate == date)
            .OrderBy(i => i.Ordinal)
            .ToListAsync(ct);

        if (existing.Count > 0 && !string.Equals(ExistingPlanStage(existing), state.Stage, StringComparison.OrdinalIgnoreCase))
        {
            db.WritingDailyPlanItems.RemoveRange(existing);
            existing.Clear();
        }

        if (existing.Count == 0)
        {
            existing = await GenerateTodayPlanAsync(userId, date, profile, state, ct);
            await db.SaveChangesAsync(ct);
        }

        return ToTodayResponse(date, existing);
    }

    public async Task StartPlanItemAsync(string userId, Guid itemId, CancellationToken ct)
    {
        var item = await LoadOwnedPlanItemAsync(userId, itemId, ct);
        if (item.Status == "pending")
        {
            item.Status = "in_progress";
            item.StartedAt = clock.GetUtcNow();
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task CompletePlanItemAsync(string userId, Guid itemId, CancellationToken ct)
    {
        var item = await LoadOwnedPlanItemAsync(userId, itemId, ct);
        item.Status = "completed";
        item.CompletedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
    }

    public async Task SkipPlanItemAsync(string userId, Guid itemId, string? reason, CancellationToken ct)
    {
        var item = await LoadOwnedPlanItemAsync(userId, itemId, ct);
        item.Status = "skipped";
        item.SkippedAt = clock.GetUtcNow();
        item.PayloadJson = JsonSerializer.Serialize(new { stage = PlanStageFromPayload(item.PayloadJson), reason = reason ?? "user_skip" }, JsonOptions);
        await db.SaveChangesAsync(ct);
    }

    public async Task<WritingCanonResponse> GetCanonAsync(string userId, string? search, string? severity, CancellationToken ct)
    {
        var profile = await db.LearnerWritingProfiles.AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == userId, ct);
        var book = LoadWritingRulebook(profile?.Profession);
        var sectionTitles = book.Sections.ToDictionary(s => s.Id, s => s.Title, StringComparer.OrdinalIgnoreCase);
        var query = book.Rules.Select(rule => ToCanonRule(rule, sectionTitles, book.Profession));
        if (!string.IsNullOrWhiteSpace(search))
        {
            var needle = search.Trim();
            query = query.Where(r => r.RuleId.Contains(needle, StringComparison.OrdinalIgnoreCase)
                || r.RuleText.Contains(needle, StringComparison.OrdinalIgnoreCase)
                || r.Category.Contains(needle, StringComparison.OrdinalIgnoreCase));
        }
        if (!string.IsNullOrWhiteSpace(severity))
        {
            query = query.Where(r => string.Equals(r.Severity, severity.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        var since = clock.GetUtcNow().AddDays(-30);
        // Project to an anonymous shape inside the SQL query (Npgsql can
        // translate that) then materialise into the record DTO on the client.
        // Constructing a positional record in the Select projection is not
        // translatable on Postgres + EF Core in this version.
        var rawViolations = await db.WritingRuleViolations.AsNoTracking()
            .Where(v => v.UserId == userId && v.GeneratedAt >= since)
            .GroupBy(v => new { v.RuleId, v.Severity })
            .Select(g => new
            {
                g.Key.RuleId,
                Count = g.Count(),
                g.Key.Severity,
                LastSeenAt = g.Max(v => v.GeneratedAt),
            })
            .OrderByDescending(v => v.Count)
            .ThenBy(v => v.RuleId)
            .Take(10)
            .ToListAsync(ct);
        var violations = rawViolations
            .Select(v => new WritingCanonViolationStatResponse(v.RuleId, v.Count, v.Severity, v.LastSeenAt))
            .ToList();

        var rules = query.OrderBy(r => r.RuleId, StringComparer.OrdinalIgnoreCase).ToList();
        return new WritingCanonResponse(rules, violations, rules.Count, violations.Sum(v => v.Count));
    }

    private OetRulebook LoadWritingRulebook(string? profession)
    {
        if (!RulebookProfessionParser.TryParse(profession ?? "medicine", out var parsed))
            parsed = ExamProfession.Medicine;

        try
        {
            return rulebookLoader.Load(RuleKind.Writing, parsed);
        }
        catch (RulebookNotFoundException) when (parsed != ExamProfession.Medicine)
        {
            return rulebookLoader.Load(RuleKind.Writing, ExamProfession.Medicine);
        }
    }

    private static WritingCanonRuleResponse ToCanonRule(OetRule rule, IReadOnlyDictionary<string, string> sectionTitles, ExamProfession profession)
    {
        var category = sectionTitles.TryGetValue(rule.Section, out var sectionTitle) ? sectionTitle : rule.Section;
        var correctExamples = rule.ExemplarPhrases?
            .Where(example => !string.IsNullOrWhiteSpace(example))
            .ToList() ?? [];
        var incorrectExamples = rule.ForbiddenPatterns?
            .Where(pattern => !string.IsNullOrWhiteSpace(pattern))
            .Select(pattern => $"Avoid pattern: {pattern}")
            .ToList() ?? [];

        return new WritingCanonRuleResponse(
            rule.Id,
            category,
            rule.Severity.ToString().ToLowerInvariant(),
            string.IsNullOrWhiteSpace(rule.Body) ? rule.Title : rule.Body,
            AppliesToList(rule.AppliesTo),
            [profession.ToString().ToLowerInvariant()],
            correctExamples,
            incorrectExamples,
            $"/writing/rulebook/{Uri.EscapeDataString(rule.Id)}");
    }

    private static IReadOnlyList<string> AppliesToList(JsonElement? appliesTo)
    {
        if (appliesTo is null) return ["all"];
        var value = appliesTo.Value;
        if (value.ValueKind == JsonValueKind.String)
        {
            var text = value.GetString();
            return string.IsNullOrWhiteSpace(text) ? ["all"] : [text];
        }

        if (value.ValueKind != JsonValueKind.Array) return ["all"];
        var values = value.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString())
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return values.Count == 0 ? ["all"] : values;
    }

    private async Task<List<WritingDailyPlanItem>> GenerateTodayPlanAsync(
        string userId,
        DateOnly date,
        LearnerWritingProfile? profile,
        PathwayState state,
        CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var items = new List<WritingDailyPlanItem>();

        if (profile is null || state.Stage == "onboarding")
        {
            items.Add(NewPlanItem(userId, date, 1, "onboarding", "W1", null, 15,
                "Set up your Writing pathway",
                "Choose your profession, target band, schedule, and letter-type focus before the diagnostic.",
                "/writing/profile-setup", null, state.Stage, now));
            db.WritingDailyPlanItems.AddRange(items);
            return items;
        }

        var practiceTask = await PickPracticeTaskAsync(profile, ct);
        if (state.Stage == "diagnostic")
        {
            items.Add(NewPlanItem(userId, date, 1, "diagnostic", "W1", "purpose", 45,
                "Take the Writing diagnostic",
                "Write one full letter under the existing exam-mode player. This creates your baseline and unlocks targeted practice.",
                $"/writing/player/{practiceTask?.Id ?? "diagnostic"}?pathwayStage=diagnostic",
                practiceTask?.Id, state.Stage, now));
        }
        else
        {
            var weakness = await GetTopWeaknessAsync(userId, ct) ?? new WeaknessSignal("W6", "genre", "Style register");
            items.Add(NewPlanItem(userId, date, 1, "sentence_drill", weakness.SkillCode, weakness.Criterion, 10,
                $"Drill {weakness.Label}",
                "Rehearse the sentence-level pattern behind your most frequent Writing weakness.",
                DrillHrefFor(weakness.SkillCode), null, state.Stage, now));
            items.Add(NewPlanItem(userId, date, 2, "full_letter", weakness.SkillCode, weakness.Criterion, 45,
                practiceTask?.Title ?? "Full Writing letter",
                "Complete a full letter using the existing writing player, autosave, paper mode, and grounded grading pipeline.",
                "/writing/practice/library",
                practiceTask?.Id, state.Stage, now));
            items.Add(NewPlanItem(userId, date, 3, "canon_review", "W6", "genre", 5,
                "Review one canon rule",
                "Browse Dr Ahmed's style canon and check your recent recurring violations.",
                "/writing/canon", null, state.Stage, now));
        }

        db.WritingDailyPlanItems.AddRange(items);
        return items;
    }

    private async Task<LearnerWritingPathway> EnsurePathwayAsync(LearnerWritingProfile profile, CancellationToken ct, bool forceRegenerate = false)
    {
        var now = clock.GetUtcNow();
        var pathway = await db.LearnerWritingPathways.FirstOrDefaultAsync(p => p.UserId == profile.UserId, ct);
        var focus = DeserializeStringList(profile.LetterTypeFocusJson);
        if (focus.Count == 0) focus = DefaultLetterTypes(profile.Profession);
        var totalWeeks = CalculateTotalWeeks(profile.ExamDate, now);

        if (pathway is null)
        {
            pathway = new LearnerWritingPathway
            {
                Id = Guid.NewGuid(),
                UserId = profile.UserId,
                TotalWeeks = totalWeeks,
                GeneratedAt = now,
                WeeksJson = JsonSerializer.Serialize(BuildDefaultWeeks(profile.Profession, focus, totalWeeks), JsonOptions),
            };
            db.LearnerWritingPathways.Add(pathway);
            profile.PathwayGeneratedAt = now;
        }
        else if (forceRegenerate || pathway.TotalWeeks != totalWeeks || string.IsNullOrWhiteSpace(pathway.WeeksJson))
        {
            pathway.TotalWeeks = totalWeeks;
            pathway.GeneratedAt = now;
            pathway.WeeksJson = JsonSerializer.Serialize(BuildDefaultWeeks(profile.Profession, focus, totalWeeks), JsonOptions);
            profile.PathwayGeneratedAt = now;
        }
        return pathway;
    }

    private async Task<PathwayState> BuildStateAsync(string userId, LearnerWritingProfile? profile, CancellationToken ct)
    {
        var evaluations = await db.Evaluations.AsNoTracking()
            .Join(db.Attempts.AsNoTracking(), e => e.AttemptId, a => a.Id, (e, a) => new { e, a })
            .Where(x => x.a.UserId == userId && x.e.SubtestCode == "writing" && x.e.State == AsyncState.Completed)
            .OrderByDescending(x => x.e.GeneratedAt ?? x.e.CreatedAt)
            .Select(x => new EvaluationSignal(x.e.Id, x.e.ScoreRange, x.e.GradeRange, x.e.GeneratedAt ?? x.e.CreatedAt, x.a.Context, x.a.Mode))
            .Take(8)
            .ToListAsync(ct);

        if (profile is null) return new PathwayState("onboarding", 0, null, null, false);
        var diagnosticEvaluation = await db.Evaluations.AsNoTracking()
            .Join(db.Attempts.AsNoTracking(), e => e.AttemptId, a => a.Id, (e, a) => new { e, a })
            .Where(x => x.a.UserId == userId
                && x.e.SubtestCode == "writing"
                && x.e.State == AsyncState.Completed
                && x.a.Context == "diagnostic"
                && x.a.Mode == "exam")
            .OrderByDescending(x => x.e.GeneratedAt ?? x.e.CreatedAt)
            .Select(x => new EvaluationSignal(x.e.Id, x.e.ScoreRange, x.e.GradeRange, x.e.GeneratedAt ?? x.e.CreatedAt, x.a.Context, x.a.Mode))
            .FirstOrDefaultAsync(ct);
        if (diagnosticEvaluation is null) return new PathwayState("diagnostic", 0, null, null, false);

        var scores = evaluations.Select(e => TryParseScaledScore(e.ScoreRange)).Where(s => s.HasValue).Select(s => s!.Value).ToList();
        if (scores.Count == 0) return new PathwayState("diagnostic", 0, null, null, false);

        var avg = scores.Count == 0 ? 0 : (int)Math.Round(scores.Average());
        var recentPassCount = scores.Take(3).Count(s => OetScoring.GradeWriting(s, profile.TargetCountry).Passed == true);
        var violations = await db.WritingRuleViolations.AsNoTracking()
            .CountAsync(v => v.UserId == userId && v.GeneratedAt >= clock.GetUtcNow().AddDays(-21), ct);
        var readiness = Math.Clamp((int)Math.Round(avg / 5.0) - Math.Min(20, violations), 0, 100);
        // Mastery is gated on the country-aware Writing pass mark, not a fixed
        // scaled-400 readiness floor: a C+ country (e.g. US, pass 300/500)
        // reaches mastery at a lower raw score than a Grade-B country
        // (pass 350/500). Three consecutive recent passes of the learner's
        // destination-country bar is the mastery signal; below that we fall
        // back to the readiness-based practice/foundation split.
        var masteredCountryBar = recentPassCount >= 3;
        var stage = masteredCountryBar ? "mastery" : readiness >= 55 ? "practice" : "foundation";
        return new PathwayState(stage, readiness, avg > 0 ? avg : null, diagnosticEvaluation.Id, true);
    }

    private async Task<ContentItem?> PickPracticeTaskAsync(LearnerWritingProfile profile, CancellationToken ct)
    {
        var focus = DeserializeStringList(profile.LetterTypeFocusJson).Select(ToLegacyLetterType).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var profession = NormalizeProfession(profile.Profession);
        var query = db.ContentItems.AsNoTracking()
            .Where(c => c.SubtestCode == "writing" && c.Status == ContentStatus.Published);

        var professionMatch = await query
            .Where(c => c.ProfessionId == profession || c.ProfessionId == null)
            .OrderByDescending(c => c.IsDiagnosticEligible)
            .ThenByDescending(c => c.PublishedAt)
            .ToListAsync(ct);

        return professionMatch.FirstOrDefault(c => c.ScenarioType is not null && focus.Contains(c.ScenarioType))
            ?? professionMatch.FirstOrDefault();
    }

    private async Task<WeaknessSignal?> GetTopWeaknessAsync(string userId, CancellationToken ct)
    {
        var since = clock.GetUtcNow().AddDays(-30);
        var row = await db.WritingRuleViolations.AsNoTracking()
            .Where(v => v.UserId == userId && v.GeneratedAt >= since)
            .GroupBy(v => v.RuleId)
            .Select(g => new { RuleId = g.Key, Count = g.Count() })
            .OrderByDescending(g => g.Count)
            .ThenBy(g => g.RuleId)
            .FirstOrDefaultAsync(ct);
        return row is null ? null : WeaknessFromRule(row.RuleId);
    }

    private async Task<WritingDailyPlanItem> LoadOwnedPlanItemAsync(string userId, Guid itemId, CancellationToken ct)
        => await db.WritingDailyPlanItems.FirstOrDefaultAsync(i => i.Id == itemId && i.UserId == userId, ct)
           ?? throw ApiException.NotFound("writing_plan_item_not_found", "Writing plan item was not found.");

    private static WritingTodayPlanResponse ToTodayResponse(DateOnly date, IReadOnlyList<WritingDailyPlanItem> items)
        => new(date,
            items.OrderBy(i => i.Ordinal).Select(i => new WritingTodayPlanItemResponse(
                i.Id, i.Ordinal, i.ItemType, i.FocusSkill, i.FocusCriterion, i.EstimatedMinutes,
                i.Title, i.Description, i.ActionHref, i.ContentId, i.Status)).ToList(),
            items.Sum(i => i.EstimatedMinutes),
            items.Count(i => i.Status == "completed"));

    private static WritingProfileResponse ToProfileResponse(string userId, LearnerWritingProfile? profile, PathwayState state, DateTimeOffset now)
    {
        if (profile is null)
        {
            return new WritingProfileResponse(userId, state.Stage, "medicine", "B", null, 5, 45, "GB", ["LT-RR", "LT-DG", "LT-UR"], null, null, null, null, null, null, false);
        }
        return new WritingProfileResponse(
            userId,
            state.Stage,
            profile.Profession,
            profile.TargetBand,
            profile.ExamDate,
            profile.DaysPerWeek,
            profile.MinutesPerDay,
            profile.TargetCountry,
            DeserializeStringList(profile.LetterTypeFocusJson),
            state.ReadinessScore,
            state.PredictedScore,
            state.LastDiagnosticEvaluationId,
            profile.OnboardingCompletedAt,
            profile.PathwayGeneratedAt,
            profile.ExamDate is null ? null : Math.Max(0, (int)Math.Ceiling((profile.ExamDate.Value - now).TotalDays / 7.0)),
            state.DiagnosticCompleted);
    }

    private async Task ClearTodayPlanAsync(string userId, CancellationToken ct)
    {
        var date = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var existing = await db.WritingDailyPlanItems
            .Where(i => i.UserId == userId && i.PlanDate == date)
            .ToListAsync(ct);
        if (existing.Count > 0) db.WritingDailyPlanItems.RemoveRange(existing);
    }

    private static List<WritingPathwayWeekResponse> BuildDefaultWeeks(string profession, IReadOnlyList<string> focusLetterTypes, int totalWeeks = 10)
    {
        var focus = focusLetterTypes.Count == 0 ? DefaultLetterTypes(profession) : focusLetterTypes;
        var weeks = Math.Clamp(totalWeeks, 4, 12);
        return Enumerable.Range(1, weeks).Select(week =>
        {
            var foundationCutoff = Math.Max(1, (int)Math.Ceiling(weeks * 0.2));
            var practiceCutoff = Math.Max(foundationCutoff + 1, (int)Math.Ceiling(weeks * 0.7));
            var phase = week <= foundationCutoff ? "foundation" : week <= practiceCutoff ? "practice" : "mastery";
            var skill = ((week - 1) % 8) + 1 switch
            {
                1 => "W1",
                2 => "W6",
                3 => "W2",
                4 => "W3",
                5 => "W4",
                6 => "W5",
                7 => "W7",
                _ => "W8",
            };
            return new WritingPathwayWeekResponse(
                week,
                phase,
                [skill],
                focus,
                phase == "foundation" ? "Build the core sub-skill before volume." : phase == "practice" ? "Apply feedback in full letters and drills." : "Simulate test conditions and confirm consistency.",
                week > practiceCutoff,
                false);
        }).ToList();
    }

    private static List<WritingPathwayWeekResponse> DeserializeWeeks(string json)
    {
        try { return JsonSerializer.Deserialize<List<WritingPathwayWeekResponse>>(json, JsonOptions) ?? []; }
        catch (JsonException) { return []; }
    }

    private static WritingDailyPlanItem NewPlanItem(string userId, DateOnly date, int ordinal, string itemType, string? focusSkill, string? focusCriterion, int minutes, string title, string description, string href, string? contentId, string stage, DateTimeOffset now)
        => new()
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            PlanDate = date,
            Ordinal = ordinal,
            ItemType = itemType,
            FocusSkill = focusSkill,
            FocusCriterion = focusCriterion,
            EstimatedMinutes = minutes,
            Title = title,
            Description = description,
            ActionHref = href,
            ContentId = contentId,
            PayloadJson = JsonSerializer.Serialize(new { stage }, JsonOptions),
            Status = "pending",
            CreatedAt = now,
        };

    private static string? ExistingPlanStage(IReadOnlyList<WritingDailyPlanItem> items)
        => items.Select(i => PlanStageFromPayload(i.PayloadJson)).FirstOrDefault(stage => !string.IsNullOrWhiteSpace(stage));

    private static string? PlanStageFromPayload(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("stage", out var stage) ? stage.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool IsWeekCompleted(WritingPathwayWeekResponse week, string stage, int currentWeek)
        => week.WeekNumber < currentWeek || StageRank(week.Phase) < StageRank(stage);

    private static int StageRank(string stage) => stage switch
    {
        "onboarding" => 0,
        "diagnostic" => 1,
        "foundation" => 2,
        "practice" => 3,
        "mastery" => 4,
        _ => 0,
    };

    private static int CalculateCurrentWeek(DateTimeOffset generatedAt, int totalWeeks, DateTimeOffset now)
        => Math.Clamp((int)Math.Floor((now - generatedAt).TotalDays / 7.0) + 1, 1, Math.Max(1, totalWeeks));

    private static int CalculateTotalWeeks(DateTimeOffset? examDate, DateTimeOffset now)
    {
        if (examDate is null) return 10;
        var weeks = (int)Math.Ceiling((examDate.Value - now).TotalDays / 7.0);
        return Math.Clamp(weeks, 4, 12);
    }

    private static int? TryParseScaledScore(string? scoreRange)
    {
        if (string.IsNullOrWhiteSpace(scoreRange)) return null;
        var range = ScaledRangeRegex.Match(scoreRange);
        if (range.Success && int.TryParse(range.Groups[1].Value, out var low) && int.TryParse(range.Groups[2].Value, out var high))
        {
            return Math.Clamp((int)Math.Round((low + high) / 2.0), OetScoring.ScaledMin, OetScoring.ScaledMax);
        }

        var score = ScaledScoreRegex.Match(scoreRange);
        if (!score.Success || !int.TryParse(score.Groups[1].Value, out var scaled)) return null;
        return Math.Clamp(scaled, OetScoring.ScaledMin, OetScoring.ScaledMax);
    }

    private static List<string> NormalizeLetterTypes(List<string>? values)
    {
        var normalized = (values ?? [])
            .Select(NormalizeLetterType)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(6)
            .ToList();
        return normalized.Count == 0 ? ["LT-RR", "LT-DG", "LT-UR"] : normalized;
    }

    private static string NormalizeLetterType(string value)
        => value.Trim().ToUpperInvariant() switch
        {
            "ROUTINE_REFERRAL" or "REFERRAL" or "LT-RR" => "LT-RR",
            "URGENT_REFERRAL" or "LT-UR" => "LT-UR",
            "DISCHARGE" or "UPDATE_DISCHARGE" or "LT-DG" => "LT-DG",
            "TRANSFER" or "TRANSFER_LETTER" or "LT-TR" => "LT-TR",
            "RESPONSE" or "UPDATE" or "LT-RP" => "LT-RP",
            "NON_MEDICAL_REFERRAL" or "NON-MEDICAL" or "LT-NM" => "LT-NM",
            _ => "LT-RR",
        };

    private static string ToLegacyLetterType(string value) => NormalizeLetterType(value) switch
    {
        "LT-RR" => "routine_referral",
        "LT-UR" => "urgent_referral",
        "LT-DG" => "discharge",
        "LT-TR" => "transfer_letter",
        "LT-RP" => "update",
        "LT-NM" => "non_medical_referral",
        _ => "routine_referral",
    };

    private static List<string> DefaultLetterTypes(string profession) => NormalizeProfession(profession) switch
    {
        "pharmacy" => ["LT-RR", "LT-RP", "LT-NM"],
        "nursing" => ["LT-DG", "LT-TR", "LT-NM"],
        _ => ["LT-RR", "LT-DG", "LT-UR"],
    };

    private static string NormalizeProfession(string value)
        => string.IsNullOrWhiteSpace(value) ? "medicine" : value.Trim().ToLowerInvariant().Replace('-', '_');

    private static string NormalizeTargetBand(string value)
        => value.Trim().ToUpperInvariant() switch { "A" => "A", "B+" => "B+", _ => "B" };

    private static string NormalizeWritingTargetCountry(string? value)
        => OetScoring.NormalizeWritingCountry(value)
           ?? throw ApiException.Validation(
               string.IsNullOrWhiteSpace(value) ? "country_required" : "country_unsupported",
               string.IsNullOrWhiteSpace(value) ? "Target country is required for Writing scoring." : "Target country is not supported for Writing scoring.",
               [new ApiFieldError("targetCountry", string.IsNullOrWhiteSpace(value) ? "required" : "unsupported", "Choose a supported Writing target country.")]);

    private static List<string> DeserializeStringList(string json)
    {
        try { return JsonSerializer.Deserialize<List<string>>(json, JsonOptions) ?? []; }
        catch (JsonException) { return []; }
    }

    private static string DrillHrefFor(string? skillCode)
        => string.IsNullOrWhiteSpace(skillCode) ? "/writing/drills" : $"/writing/drills?skill={Uri.EscapeDataString(skillCode)}";

    private static WeaknessSignal WeaknessFromRule(string ruleId)
    {
        var lowered = ruleId.ToLowerInvariant();
        if (lowered.StartsWith("r03", StringComparison.OrdinalIgnoreCase) || lowered.Contains("content") || lowered.Contains("irrelevant")) return new("W3", "content", "Content selection");
        if (lowered.StartsWith("r07", StringComparison.OrdinalIgnoreCase) || lowered.StartsWith("r09", StringComparison.OrdinalIgnoreCase) || lowered.StartsWith("r13", StringComparison.OrdinalIgnoreCase) || lowered.Contains("purpose")) return new("W2", "purpose", "Purpose articulation");
        if (lowered.StartsWith("r04", StringComparison.OrdinalIgnoreCase) || lowered.StartsWith("r08", StringComparison.OrdinalIgnoreCase) || lowered.Contains("paragraph") || lowered.Contains("order")) return new("W5", "organization", "Organisation");
        if (lowered.StartsWith("r10", StringComparison.OrdinalIgnoreCase) || lowered.StartsWith("r11", StringComparison.OrdinalIgnoreCase) || lowered.StartsWith("r12", StringComparison.OrdinalIgnoreCase) || lowered.Contains("grammar") || lowered.Contains("article")) return new("W7", "language", "Language accuracy");
        if (lowered.Contains("purpose")) return new("W2", "purpose", "Purpose articulation");
        if (lowered.Contains("content") || lowered.Contains("irrelevant")) return new("W3", "content", "Content selection");
        if (lowered.Contains("paragraph") || lowered.Contains("order")) return new("W5", "organization", "Organisation");
        if (lowered.Contains("tone") || lowered.Contains("style") || lowered.Contains("abbreviation")) return new("W6", "genre", "Style register");
        if (lowered.Contains("grammar") || lowered.Contains("article")) return new("W7", "language", "Language accuracy");
        return new("W6", "genre", "Style register");
    }

    private sealed record PathwayState(string Stage, int ReadinessScore, int? PredictedScore, string? LastDiagnosticEvaluationId, bool DiagnosticCompleted);
    private sealed record EvaluationSignal(string Id, string? ScoreRange, string? GradeRange, DateTimeOffset CreatedAt, string? Context, string? Mode);
    private sealed record WeaknessSignal(string SkillCode, string Criterion, string Label);
}
