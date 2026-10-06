using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.AiTools;
using OetLearner.Api.Services.Companion;
using OetLearner.Api.Services.Planner;

namespace OetLearner.Api.Services.AiTools.Tools;

/// <summary>
/// Holds one study-plan proposal per user+thread, set by
/// <c>companion_preview_study_plan</c> and consumed by
/// <c>companion_create_study_plan</c>. Replaced by a new preview; expires 2 h.
/// </summary>
public sealed class CompanionStudyPlanProposalStore(IMemoryCache cache)
{
    private static string Key(string userId, string threadId) => $"studyplan-proposal::{userId}::{threadId}";

    public void Store(string userId, string threadId, CompanionStudyPlanProposal proposal) =>
        cache.Set(Key(userId, threadId), proposal, TimeSpan.FromHours(2));

    public CompanionStudyPlanProposal? Get(string userId, string threadId) =>
        cache.TryGetValue(Key(userId, threadId), out CompanionStudyPlanProposal? p) ? p : null;

    public void Consume(string userId, string threadId) => cache.Remove(Key(userId, threadId));
}

/// <summary>A server-held study-plan proposal created by a preview call.</summary>
public sealed record CompanionStudyPlanProposal(
    string ThreadId,
    string PreviewTurnId,
    DateOnly? ExamDate,
    int? StudyHoursPerWeek,
    IReadOnlyList<string>? WeakSubtests,
    int TotalWeeks,
    int MinutesPerDay,
    string Tier,
    DateTimeOffset CreatedAt);

/// <summary>Study-plan tools for the AI Learning Companion (WU10).</summary>
public sealed class CompanionGetStudyPlanTool(
    ICompanionContextResolver contexts,
    ICompanionDestinationRegistry destinations,
    Microsoft.EntityFrameworkCore.DbContextOptions<Data.LearnerDbContext> dbOptions) : IAiToolExecutor
{
    public string Code => "companion_get_study_plan";
    public string Description => "Get the learner's current study plan overview: dates, item counts, upcoming items, and goal values.";
    public AiToolCategory Category => AiToolCategory.Read;
    public string JsonSchemaArgs => "{\"type\":\"object\",\"properties\":{},\"additionalProperties\":false}";

    public async Task<AiToolExecutionResult> ExecuteAsync(JsonElement args, AiToolContext ctx, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(ctx.UserId)) return CompanionToolGuards.NoUser();
        var context = await contexts.ResolveAsync(ctx.UserId!, new CompanionContextEnvelope(), ct);
        if (!context.ActionsEnabled) return CompanionToolGuards.ActionsOff();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        await using var db = new LearnerDbContext(dbOptions);
        var plan = await db.StudyPlans
            .Where(p => p.UserId == ctx.UserId && p.IsActive)
            .OrderByDescending(p => p.Version)
            .FirstOrDefaultAsync(ct);

        if (plan is null)
        {
            return new AiToolExecutionResult(AiToolOutcome.Success, CompanionToolGuards.Json(new
            {
                today = today.ToString("yyyy-MM-dd"),
                has_plan = false,
                placeholder = true,
            }));
        }

        var items = await db.StudyPlanItems
            .Where(i => i.StudyPlanId == plan.Id)
            .OrderBy(i => i.WeekIndex)
            .ThenBy(i => i.PriorityScore)
            .ToListAsync(ct);

        var upcoming = items
            .Where(i => i.DueDate >= today && i.Status == StudyPlanItemStatus.NotStarted)
            .Take(7)
            .Select(i => new
            {
                title = i.Title.Length > 80 ? i.Title[..80] : i.Title,
                subtest = i.SubtestCode,
                due = i.DueDate.ToString("yyyy-MM-dd"),
                minutes = i.DurationMinutes,
            })
            .ToList();

        var goal = await db.Goals.AsNoTracking().FirstOrDefaultAsync(g => g.UserId == ctx.UserId, ct);

        return new AiToolExecutionResult(AiToolOutcome.Success, CompanionToolGuards.Json(new
        {
            today = today.ToString("yyyy-MM-dd"),
            has_plan = true,
            placeholder = false,
            plan_id = plan.Id,
            version = plan.Version,
            state = plan.State.ToString().ToLowerInvariant(),
            weeks = plan.TotalWeeks,
            minutes_per_day = plan.MinutesPerDayBudget,
            pending_count = items.Count(i => i.Status == StudyPlanItemStatus.NotStarted),
            completed_count = items.Count(i => i.Status == StudyPlanItemStatus.Completed),
            upcoming_items = upcoming,
            goal = new
            {
                exam_date = goal?.TargetExamDate?.ToString("yyyy-MM-dd"),
                study_hours_per_week = goal?.StudyHoursPerWeek,
                weak_subtests = goal?.WeakSubtestsJson,
            },
            url = (await destinations.ResolveAsync("study.plan", context, ct)).Url,
        }));
    }
}

/// <summary>Preview study-plan generation without persisting anything.</summary>
public sealed class CompanionPreviewStudyPlanTool(
    ICompanionContextResolver contexts,
    IStudyPlanGenerator generator,
    CompanionStudyPlanProposalStore store) : IAiToolExecutor
{
    public string Code => "companion_preview_study_plan";
    public string Description => "Preview what a study-plan regeneration would produce for the learner's current goals. Never mutates the plan.";
    public AiToolCategory Category => AiToolCategory.Read;
    public string JsonSchemaArgs => """
    {
      "type":"object",
      "properties":{
        "exam_date":{"type":"string","maxLength":10},
        "study_hours_per_week":{"type":"integer","minimum":1,"maximum":80},
        "weak_subtests":{"type":"array","items":{"type":"string","enum":["reading","writing","listening","speaking"]},"maxItems":4}
      },
      "additionalProperties":false
    }
    """;

    public async Task<AiToolExecutionResult> ExecuteAsync(JsonElement args, AiToolContext ctx, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(ctx.UserId)) return CompanionToolGuards.NoUser();
        if (string.IsNullOrEmpty(ctx.ThreadId)) return new AiToolExecutionResult(AiToolOutcome.RbacDenied, null, "no_thread", "thread id required");

        var context = await contexts.ResolveAsync(ctx.UserId!, new CompanionContextEnvelope(), ct);
        if (!context.ActionsEnabled) return CompanionToolGuards.ActionsOff();

        DateOnly? examDate = null;
        if (args.TryGetProperty("exam_date", out var ed) && ed.ValueKind == JsonValueKind.String)
        {
            if (!DateOnly.TryParse(ed.GetString(), out var parsed))
                return new AiToolExecutionResult(AiToolOutcome.ArgsInvalid, null, "invalid_date", "exam_date must be yyyy-MM-dd");
            if (parsed <= DateOnly.FromDateTime(DateTime.UtcNow))
                return new AiToolExecutionResult(AiToolOutcome.ArgsInvalid, null, "past_date", "exam_date must be strictly in the future");
            examDate = parsed;
        }

        int? hoursPerWeek = null;
        if (args.TryGetProperty("study_hours_per_week", out var hw) && hw.ValueKind == JsonValueKind.Number)
            hoursPerWeek = hw.GetInt32();

        List<string>? weakSubtests = null;
        if (args.TryGetProperty("weak_subtests", out var ws) && ws.ValueKind == JsonValueKind.Array)
            weakSubtests = ws.EnumerateArray().Select(x => x.GetString()!).ToList();

        var overrides = new StudyPlanGoalOverrides(examDate, hoursPerWeek, weakSubtests);
        var preview = await generator.PreviewAsync(ctx.UserId!, overrides, ct);

        if (!preview.TemplateFound)
        {
            return new AiToolExecutionResult(AiToolOutcome.Success, CompanionToolGuards.Json(new
            {
                can_create = false,
                reason = preview.NoTemplateReason,
                supported_weeks_range = "2-16",
                today = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd"),
            }));
        }

        var proposal = new CompanionStudyPlanProposal(
            ThreadId: ctx.ThreadId!,
            PreviewTurnId: ctx.TurnId ?? "",
            ExamDate: examDate,
            StudyHoursPerWeek: hoursPerWeek,
            WeakSubtests: weakSubtests,
            TotalWeeks: preview.TotalWeeks,
            MinutesPerDay: preview.MinutesPerDay,
            Tier: preview.Tier,
            CreatedAt: DateTimeOffset.UtcNow);
        store.Store(ctx.UserId!, ctx.ThreadId!, proposal);

        return new AiToolExecutionResult(AiToolOutcome.Success, CompanionToolGuards.Json(new
        {
            can_create = true,
            today = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd"),
            total_weeks = preview.TotalWeeks,
            minutes_per_day = preview.MinutesPerDay,
            tier = preview.Tier,
            template_slug = preview.TemplateSlug,
            pending_items = preview.PendingItemCount,
            completed_items = preview.CompletedItemCount,
            learner_added_items = preview.LearnerAddedItemCount,
            approx_task_count = preview.ApproxTaskCount,
            plan_exists = preview.PendingItemCount + preview.CompletedItemCount > 0,
            drops_pending_items = preview.PendingItemCount > 0 || preview.LearnerAddedItemCount > 0,
        }));
    }
}

/// <summary>Apply a server-held study-plan proposal. Write tool: the model only confirms.</summary>
public sealed class CompanionCreateStudyPlanTool(
    OetLearner.Api.Services.Planner.IStudyPlanAvailabilityShaper shaper,
    ICompanionContextResolver contexts,
    CompanionStudyPlanProposalStore store,
    IStudyPlanGenerator generator) : IAiToolExecutor
{
    public string Code => "companion_create_study_plan";
    public string Description => "Create or regenerate the learner's study plan using the proposal previewed earlier. Call only after the learner agrees in a later message.";
    public AiToolCategory Category => AiToolCategory.Write;
    public string JsonSchemaArgs => "{\"type\":\"object\",\"properties\":{},\"additionalProperties\":false}";

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> PerUserLocks = new();

    public async Task<AiToolExecutionResult> ExecuteAsync(JsonElement args, AiToolContext ctx, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(ctx.UserId)) return CompanionToolGuards.NoUser();
        if (string.IsNullOrEmpty(ctx.ThreadId)) return new AiToolExecutionResult(AiToolOutcome.RbacDenied, null, "no_thread", "thread id required");

        var context = await contexts.ResolveAsync(ctx.UserId!, new CompanionContextEnvelope(), ct);
        if (!context.ActionsEnabled) return CompanionToolGuards.ActionsOff();

        var proposal = store.Get(ctx.UserId!, ctx.ThreadId!);
        if (proposal is null)
            return new AiToolExecutionResult(AiToolOutcome.Success, CompanionToolGuards.Json(new { created = false, reason = "no_proposal" }));

        if (string.Equals(proposal.PreviewTurnId, ctx.TurnId, StringComparison.Ordinal))
            return new AiToolExecutionResult(AiToolOutcome.Success, CompanionToolGuards.Json(new { created = false, reason = "needs_later_turn" }));

        var userLock = PerUserLocks.GetOrAdd(ctx.UserId!, _ => new SemaphoreSlim(1, 1));
        await userLock.WaitAsync(CancellationToken.None);
        try
        {
            var result = await generator.GenerateAsync(ctx.UserId!, StudyPlanGenerationTrigger.Companion, CancellationToken.None);
            // SAMI Wave 1: fit the fresh plan to the learner’s real week (night shifts,
            // long shifts, travel mode) before telling them it exists.
            AvailabilityShaperReport shaperReport;
            try
            {
                shaperReport = await shaper.ShapeAsync(ctx.UserId!, result.PlanId, CancellationToken.None);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                shaperReport = new AvailabilityShaperReport(0, 0, 0);
            }
            store.Consume(ctx.UserId!, ctx.ThreadId!);

            return new AiToolExecutionResult(AiToolOutcome.Success, CompanionToolGuards.Json(new
            {
                created = true,
                plan_id = result.PlanId,
                version = result.Version,
                items_created = result.ItemsCreated,
                items_preserved = result.ItemsPreservedFromPrior,
                template_id = result.TemplateId,
                rescheduled_for_shifts = shaperReport.MovedItems,
                dropped_for_travel = shaperReport.DroppedItems,
                url = "study.plan",
            }));
        }
        catch (InvalidOperationException ex)
        {
            return new AiToolExecutionResult(AiToolOutcome.ExecutionError, null, "generation_failed", ex.Message);
        }
        finally
        {
            userLock.Release();
        }
    }
}
