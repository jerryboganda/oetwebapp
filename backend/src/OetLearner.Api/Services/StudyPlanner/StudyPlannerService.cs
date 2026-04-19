using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.StudyPlanner;

/// <summary>
/// End-to-end Study Planner v2 orchestrator. Generates personalised plans,
/// enforces drift policy, exports ICS, and serves as the single path for
/// learner state transitions (complete, skip, etc).
///
/// <para>
/// Replaces the hardcoded <c>CreateDefaultStudyPlanItems</c> and the
/// date-shift-only regen in <c>BackgroundJobProcessor.CompleteStudyPlanRegenerationAsync</c>.
/// </para>
/// </summary>
public interface IStudyPlannerService
{
    /// <summary>Generate or regenerate a plan for the learner. Idempotent per (userId, triggeredBy).</summary>
    Task<StudyPlan> GenerateForLearnerAsync(string userId, string triggeredBy, CancellationToken ct);

    /// <summary>Get the learner's current plan (items + metadata). Creates an empty plan if none exists.</summary>
    Task<StudyPlan> GetOrCreatePlanAsync(string userId, CancellationToken ct);

    /// <summary>Assemble the learner-facing DTO for `/v1/study-plan`.</summary>
    Task<IReadOnlyList<StudyPlanItemView>> GetItemsAsync(string userId, CancellationToken ct);

    /// <summary>Detect drift using admin-authored policy; may auto-enqueue regeneration.</summary>
    Task<DriftReport> DetectDriftAsync(string userId, bool allowAutoRegen, CancellationToken ct);

    /// <summary>Build an iCalendar document for all pending items.</summary>
    Task<string> ExportIcsAsync(string userId, CancellationToken ct);
}

public sealed record DriftReport(
    string Level,             // on-track | mild | moderate | severe
    int CompletionRate,
    int OverdueItems,
    int DriftDays,
    bool ShouldRegenerate,
    string Recommendation);

public sealed class StudyPlannerService(
    LearnerDbContext db,
    IStudyPlannerRuleEngine ruleEngine,
    IStudyPlannerAiReasoner? aiReasoner = null) : IStudyPlannerService
{
    public async Task<StudyPlan> GenerateForLearnerAsync(string userId, string triggeredBy, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var plan = await GetOrCreatePlanAsync(userId, ct);

        // Build the learner context
        var ctx = await BuildContextAsync(userId, plan, ct);

        // Load rules (active only) and pick a template
        var rules = await db.StudyPlanAssignmentRules
            .AsNoTracking()
            .Where(r => r.IsActive && r.ExamFamilyCode == ctx.ExamFamilyCode)
            .OrderBy(r => r.Priority).ThenByDescending(r => r.Weight)
            .ToListAsync(ct);
        var match = ruleEngine.Match(ctx, rules);

        // Fallback: if no rule matches, pick the first non-archived template for this exam family.
        var templateId = match.TemplateId;
        if (string.IsNullOrEmpty(templateId))
        {
            var fallback = await db.StudyPlanTemplates.AsNoTracking()
                .Where(t => !t.IsArchived && t.ExamFamilyCode == ctx.ExamFamilyCode)
                .OrderBy(t => t.CreatedAt)
                .FirstOrDefaultAsync(ct);
            templateId = fallback?.Id;
        }

        // Load template + items
        StudyPlanTemplate? template = null;
        IReadOnlyList<StudyPlanTemplateItem> templateItems = Array.Empty<StudyPlanTemplateItem>();
        if (templateId is not null)
        {
            template = await db.StudyPlanTemplates.AsNoTracking().FirstOrDefaultAsync(t => t.Id == templateId, ct);
            templateItems = await db.StudyPlanTemplateItems.AsNoTracking()
                .Where(i => i.PlanTemplateId == templateId)
                .OrderBy(i => i.Ordering).ToListAsync(ct);
        }

        // Load all task templates referenced
        var taskIds = templateItems.Select(i => i.TaskTemplateId).Distinct().ToArray();
        var tasks = await db.StudyPlanTaskTemplates.AsNoTracking()
            .Where(t => taskIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, ct);

        // Clear previous items (keeping completed/skipped history is handled via generation log).
        // For backward-compat with e2e tests, we leave items that were already Completed/Skipped intact.
        var existingItems = await db.StudyPlanItems.Where(x => x.StudyPlanId == plan.Id).ToListAsync(ct);
        var historicItems = existingItems.Where(x =>
            x.Status == StudyPlanItemStatus.Completed || x.Status == StudyPlanItemStatus.Skipped).ToList();
        var toRemove = existingItems.Except(historicItems).ToList();
        db.StudyPlanItems.RemoveRange(toRemove);

        // Materialise template items into StudyPlanItem rows.
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var generated = 0;
        foreach (var ti in templateItems)
        {
            if (!tasks.TryGetValue(ti.TaskTemplateId, out var tt)) continue;
            if (tt.IsArchived) continue;
            // Respect profession scope
            if (!ProfessionScopeMatches(tt, ctx.ProfessionId)) continue;
            // Respect country scope
            if (!CountryScopeMatches(tt, ctx.TargetCountry)) continue;

            var due = today.AddDays(ti.WeekOffset * 7 + ti.DayOffsetWithinWeek);
            db.StudyPlanItems.Add(new StudyPlanItem
            {
                Id = $"spi-{Guid.NewGuid():N}",
                StudyPlanId = plan.Id,
                TaskTemplateId = tt.Id,
                Title = tt.Title,
                SubtestCode = tt.SubtestCode,
                DurationMinutes = tt.DurationMinutes,
                Rationale = tt.RationaleMarkdown,
                DueDate = due,
                Status = StudyPlanItemStatus.NotStarted,
                Section = ti.Section,
                ContentId = tt.DefaultContentPaperId,
                ContentPaperId = tt.DefaultContentPaperId,
                ItemType = tt.ItemType,
                Priority = ti.Priority,
                PrerequisiteItemId = null, // resolved post-save if needed
                UpdatedAt = now,
            });
            generated++;
        }

        // Update plan metadata
        plan.State = AsyncState.Completed;
        plan.Version += 1;
        plan.GeneratedAt = now;
        plan.TemplateId = templateId;
        plan.AssignmentRuleIdsCsv = string.Join(',', match.MatchedRuleIds);
        plan.GoalSnapshotJson = JsonSerializer.Serialize(ctx);
        plan.Checkpoint = template is null
            ? "Plan ready"
            : $"Following {template.Name}";
        plan.WeakSkillFocus = ctx.WeakSubtests.Count == 0
            ? "No specific weak areas identified yet."
            : $"Focus on: {string.Join(", ", ctx.WeakSubtests.Select(Capitalize))}";

        // Write a generation log entry
        db.StudyPlanGenerationLogs.Add(new StudyPlanGenerationLog
        {
            Id = $"spgl-{Guid.NewGuid():N}",
            UserId = userId,
            PlanId = plan.Id,
            TriggeredBy = triggeredBy,
            RuleIdsMatchedCsv = string.Join(',', match.MatchedRuleIds),
            TemplateId = templateId,
            AiUsed = false,
            ItemCount = generated,
            CreatedAt = now,
        });

        await db.SaveChangesAsync(ct);

        // ── Phase 7: optional AI-grounded rationale personalisation ─────────
        // Runs AFTER items are persisted so item IDs are stable. The reasoner
        // NEVER throws — a quota/kill-switch/refusal short-circuits silently
        // and we keep the deterministic rationales.
        if (aiReasoner is not null && generated > 0)
        {
            try
            {
                var items = await db.StudyPlanItems.Where(i => i.StudyPlanId == plan.Id).ToListAsync(ct);
                var addenda = await aiReasoner.ProduceAddendaAsync(ctx, items, ct);
                if (addenda.Count > 0)
                {
                    foreach (var it in items)
                    {
                        if (addenda.TryGetValue(it.Id, out var text) && !string.IsNullOrWhiteSpace(text))
                            it.AiRationaleAddendum = text;
                    }
                    // Update the generation log to reflect AI usage.
                    var log = await db.StudyPlanGenerationLogs
                        .Where(l => l.PlanId == plan.Id)
                        .OrderByDescending(l => l.CreatedAt).FirstOrDefaultAsync(ct);
                    if (log is not null)
                    {
                        log.AiUsed = true;
                        log.AiFeatureCode = AiFeatureCodes.StudyPlanReasoning;
                    }
                    await db.SaveChangesAsync(ct);
                }
            }
            catch
            {
                // Fail silent — the deterministic plan is sufficient.
            }
        }

        return plan;
    }

    public async Task<StudyPlan> GetOrCreatePlanAsync(string userId, CancellationToken ct)
    {
        var plan = await db.StudyPlans
            .Where(p => p.UserId == userId)
            .OrderByDescending(p => p.GeneratedAt)
            .FirstOrDefaultAsync(ct);
        if (plan is not null) return plan;
        plan = new StudyPlan
        {
            Id = $"plan-{Guid.NewGuid():N}",
            UserId = userId,
            Version = 1,
            GeneratedAt = DateTimeOffset.UtcNow,
            State = AsyncState.Completed,
            Checkpoint = "Plan ready",
            WeakSkillFocus = "Awaiting diagnostic",
        };
        db.StudyPlans.Add(plan);
        await db.SaveChangesAsync(ct);
        return plan;
    }

    public async Task<IReadOnlyList<StudyPlanItemView>> GetItemsAsync(string userId, CancellationToken ct)
    {
        var plan = await GetOrCreatePlanAsync(userId, ct);
        var items = await db.StudyPlanItems.AsNoTracking()
            .Where(i => i.StudyPlanId == plan.Id)
            .OrderBy(i => i.DueDate).ThenByDescending(i => i.Priority)
            .ToListAsync(ct);
        return items.Select(ToView).ToList();
    }

    public async Task<DriftReport> DetectDriftAsync(string userId, bool allowAutoRegen, CancellationToken ct)
    {
        var plan = await GetOrCreatePlanAsync(userId, ct);
        var items = await db.StudyPlanItems.AsNoTracking().Where(i => i.StudyPlanId == plan.Id).ToListAsync(ct);
        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime);
        var overdue = items.Where(i => i.DueDate < today && i.Status == StudyPlanItemStatus.NotStarted).ToList();
        var completed = items.Count(i => i.Status == StudyPlanItemStatus.Completed);
        var expected = items.Count(i => i.DueDate <= today);
        var completionRate = expected == 0 ? 100 : (int)Math.Round(100.0 * completed / expected);
        var driftDays = overdue.Count == 0 ? 0 : overdue.Max(i => today.DayNumber - i.DueDate.DayNumber);

        var policy = await db.StudyPlanDriftPolicies
            .FirstOrDefaultAsync(p => p.ExamFamilyCode == plan.ExamFamilyCode, ct)
            ?? new StudyPlanDriftPolicy { ExamFamilyCode = plan.ExamFamilyCode };

        string level;
        string copy;
        if (driftDays > policy.SevereDays) { level = "severe"; copy = policy.SevereCopy; }
        else if (driftDays > policy.ModerateDays) { level = "moderate"; copy = policy.ModerateCopy; }
        else if (driftDays > policy.MildDays) { level = "mild"; copy = policy.MildCopy; }
        else { level = "on-track"; copy = policy.OnTrackCopy; }

        var shouldRegen = (level == "severe" && policy.AutoRegenerateOnSevere)
                         || (level == "moderate" && policy.AutoRegenerateOnModerate);

        if (shouldRegen && allowAutoRegen)
        {
            // Auto-regenerate
            await GenerateForLearnerAsync(userId, "drift", ct);
        }

        return new DriftReport(level, completionRate, overdue.Count, driftDays, shouldRegen, copy);
    }

    public async Task<string> ExportIcsAsync(string userId, CancellationToken ct)
    {
        var plan = await GetOrCreatePlanAsync(userId, ct);
        var items = await db.StudyPlanItems.AsNoTracking()
            .Where(i => i.StudyPlanId == plan.Id && i.Status == StudyPlanItemStatus.NotStarted)
            .OrderBy(i => i.DueDate).ToListAsync(ct);
        return IcsBuilder.Build(plan, items);
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private async Task<LearnerPlanContext> BuildContextAsync(string userId, StudyPlan plan, CancellationToken ct)
    {
        var goal = await db.Goals.AsNoTracking().FirstOrDefaultAsync(g => g.UserId == userId, ct);
        var weeksToExam = goal?.TargetExamDate is { } ted
            ? Math.Max(0, (int)Math.Ceiling((ted.ToDateTime(TimeOnly.MinValue) - DateTime.UtcNow).TotalDays / 7.0))
            : (int?)null;
        var weakSubtests = Array.Empty<string>();
        if (!string.IsNullOrEmpty(goal?.WeakSubtestsJson))
        {
            try { weakSubtests = JsonSerializer.Deserialize<string[]>(goal.WeakSubtestsJson) ?? Array.Empty<string>(); }
            catch { /* tolerate legacy bad JSON */ }
        }
        return new LearnerPlanContext(
            UserId: userId,
            ProfessionId: goal?.ProfessionId,
            ExamFamilyCode: plan.ExamFamilyCode,
            TargetCountry: goal?.TargetCountry,
            WeeksToExam: weeksToExam,
            HoursPerWeek: goal?.StudyHoursPerWeek,
            TargetWritingScore: goal?.TargetWritingScore,
            TargetSpeakingScore: goal?.TargetSpeakingScore,
            TargetReadingScore: goal?.TargetReadingScore,
            TargetListeningScore: goal?.TargetListeningScore,
            WeakSubtests: weakSubtests);
    }

    private static bool ProfessionScopeMatches(StudyPlanTaskTemplate t, string? prof)
    {
        if (string.IsNullOrWhiteSpace(t.ProfessionScopeJson)) return true;
        try
        {
            var list = JsonSerializer.Deserialize<string[]>(t.ProfessionScopeJson) ?? Array.Empty<string>();
            if (list.Length == 0) return true;
            return prof is not null && list.Any(p => string.Equals(p, prof, StringComparison.OrdinalIgnoreCase));
        }
        catch { return true; }
    }

    private static bool CountryScopeMatches(StudyPlanTaskTemplate t, string? country)
    {
        if (string.IsNullOrWhiteSpace(t.TargetCountriesJson)) return true;
        try
        {
            var list = JsonSerializer.Deserialize<string[]>(t.TargetCountriesJson) ?? Array.Empty<string>();
            if (list.Length == 0) return true;
            return country is not null && list.Any(c => string.Equals(c, country, StringComparison.OrdinalIgnoreCase));
        }
        catch { return true; }
    }

    public static StudyPlanItemView ToView(StudyPlanItem i) => new(
        Id: i.Id,
        TaskTemplateId: i.TaskTemplateId,
        Title: i.Title,
        SubtestCode: i.SubtestCode,
        ItemType: i.ItemType,
        DurationMinutes: i.DurationMinutes,
        Rationale: i.Rationale,
        AiRationaleAddendum: i.AiRationaleAddendum,
        DueDate: i.DueDate,
        Status: StatusToString(i.Status),
        Section: i.Section,
        ContentId: i.ContentId,
        ContentPaperId: i.ContentPaperId,
        StartUrl: BuildStartUrl(i),
        Priority: i.Priority,
        PrerequisiteItemId: i.PrerequisiteItemId,
        StartedAt: i.StartedAt,
        UpdatedAt: i.UpdatedAt,
        SnoozedUntil: i.SnoozedUntil);

    private static string StatusToString(StudyPlanItemStatus s) => s switch
    {
        StudyPlanItemStatus.NotStarted => "not_started",
        StudyPlanItemStatus.InProgress => "in_progress",
        StudyPlanItemStatus.Completed => "completed",
        StudyPlanItemStatus.Skipped => "skipped",
        StudyPlanItemStatus.Rescheduled => "rescheduled",
        _ => "not_started",
    };

    public static string BuildStartUrl(StudyPlanItem i)
    {
        var subtest = i.SubtestCode?.ToLowerInvariant() ?? "dashboard";
        var id = i.ContentPaperId ?? i.ContentId;
        return (subtest, id) switch
        {
            ("writing", { } cid) => $"/writing/tasks/{cid}",
            ("reading", { } cid) => $"/reading/tasks/{cid}",
            ("listening", { } cid) => $"/listening/tasks/{cid}",
            ("speaking", { } cid) => $"/speaking/tasks/{cid}",
            ("mock", { } cid) => $"/mocks/{cid}",
            ("diagnostic", _) => "/diagnostic",
            _ => $"/{subtest}",
        };
    }

    private static string Capitalize(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0]) + s[1..];
}
