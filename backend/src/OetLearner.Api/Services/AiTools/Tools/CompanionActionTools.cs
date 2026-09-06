using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Companion;
using OetLearner.Api.Services.Planner;

namespace OetLearner.Api.Services.AiTools.Tools;

// ─────────────────────────────────────────────────────────────────────────────
// AI Learning Companion — typed action layer (S1.5, F-098…F-104).
//
// Every tool here obeys three rules that the source specification treats as
// non-negotiable:
//
//   1. The SERVER resolves every target. The model names a destination id from
//      CompanionDestinationRegistry; a model-produced URL is never followed.
//   2. Entitlement is re-checked AT EXECUTION, from the database, not from
//      whatever the model believed when it decided to call the tool.
//   3. Actions honour the companion_actions kill switch, so an operator can
//      stop every action from /admin/flags without stopping the conversation.
//
// Tool codes added here MUST also be added to AiToolRegistry.LearnerSafeToolCodes,
// or the learner tool boundary will (correctly) refuse to expose them.
//
// EXAM MODE. AiToolContext carries no context envelope, so these tools cannot see
// the attempt the learner is sitting in; exam-mode refusal is enforced in the
// prompt, which IS built with the envelope. That is safe here only because none
// of these tools can reveal exam content: they return navigation links, a credit
// balance and a plan item. Any future tool that could surface question content,
// transcripts or feedback must NOT be added to this file without plumbing the
// envelope through to tool execution first.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Shared plumbing for the companion action tools: resolve the server-trusted
/// turn context once, and refuse uniformly when the action kill switch is off.
/// </summary>
internal static class CompanionToolGuards
{
    internal const string ActionsDisabledCode = "actions_disabled";

    internal static AiToolExecutionResult NoUser() =>
        new(AiToolOutcome.RbacDenied, null, "no_user", "tool requires an authenticated learner");

    internal static AiToolExecutionResult ActionsOff() =>
        new(AiToolOutcome.RbacDenied, null, ActionsDisabledCode,
            "companion actions are switched off");

    internal static JsonElement Json(object payload) => LookupRulebookRuleTool.ToJson(payload);
}

/// <summary>
/// Lets the companion discover which places in the product this learner can
/// actually open, without the prompt having to carry the whole route table —
/// and without the model being able to guess one.
/// </summary>
public sealed class CompanionFindDestinationTool(
    ICompanionContextResolver contexts,
    ICompanionDestinationRegistry destinations) : IAiToolExecutor
{
    public string Code => "companion_find_destination";
    public AiToolCategory Category => AiToolCategory.Read;
    public string JsonSchemaArgs => """
    {
      "type":"object",
      "properties":{
        "query":{"type":"string","maxLength":200},
        "subtest":{"type":"string","enum":["writing","speaking","reading","listening"]},
        "limit":{"type":"integer","minimum":1,"maximum":10}
      },
      "additionalProperties":false
    }
    """;

    public async Task<AiToolExecutionResult> ExecuteAsync(JsonElement args, AiToolContext ctx, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(ctx.UserId)) return CompanionToolGuards.NoUser();

        var query = args.TryGetProperty("query", out var q) ? q.GetString() : null;
        var subtest = args.TryGetProperty("subtest", out var s) ? s.GetString() : null;
        var limit = args.TryGetProperty("limit", out var l) && l.TryGetInt32(out var parsed) ? parsed : 5;

        // The subtest is a ranking hint only — it can never widen entitlement,
        // so accepting it from the model is safe.
        var context = await contexts.ResolveAsync(
            ctx.UserId!, new CompanionContextEnvelope(SubtestCode: subtest), ct);

        // Discovery is gated with the rest of the action layer: a kill switch that
        // still hands out links is not a kill switch.
        if (!context.ActionsEnabled) return CompanionToolGuards.ActionsOff();

        var matches = await destinations.SearchAsync(query, context, limit, ct);

        return new AiToolExecutionResult(AiToolOutcome.Success, CompanionToolGuards.Json(new
        {
            destinations = matches
                .Select(m => new
                {
                    id = m.Id,
                    title = m.Title,
                    kind = m.Kind.ToString().ToLowerInvariant(),
                    url = m.Url,
                })
                .ToList(),
        }));
    }
}

/// <summary>
/// Turns one destination id into a link the learner can actually click.
///
/// <para>
/// A locked destination returns <c>allowed=false</c> with the reason and the
/// upgrade route rather than a URL — the companion must explain the boundary,
/// not route around it.
/// </para>
/// </summary>
public sealed class CompanionOpenDestinationTool(
    ICompanionContextResolver contexts,
    ICompanionDestinationRegistry destinations) : IAiToolExecutor
{
    public string Code => "companion_open_destination";
    public AiToolCategory Category => AiToolCategory.Read;
    public string JsonSchemaArgs => """
    {
      "type":"object",
      "properties":{
        "destination_id":{"type":"string","minLength":1,"maxLength":64}
      },
      "required":["destination_id"],
      "additionalProperties":false
    }
    """;

    public async Task<AiToolExecutionResult> ExecuteAsync(JsonElement args, AiToolContext ctx, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(ctx.UserId)) return CompanionToolGuards.NoUser();

        var id = args.GetProperty("destination_id").GetString()!;
        var context = await contexts.ResolveAsync(ctx.UserId!, null, ct);
        if (!context.ActionsEnabled) return CompanionToolGuards.ActionsOff();

        var resolution = await destinations.ResolveAsync(id, context, ct);

        if (!resolution.Allowed)
        {
            // Offer the commercial route only when the block is commercial.
            var upgrade = resolution.Reason == "module_not_entitled"
                ? await destinations.ResolveAsync("pricing", context, ct)
                : null;

            return new AiToolExecutionResult(AiToolOutcome.Success, CompanionToolGuards.Json(new
            {
                allowed = false,
                destination_id = resolution.Id,
                reason = resolution.Reason,
                required_scope = resolution.RequiredScope,
                upgrade_url = upgrade?.Url,
            }));
        }

        return new AiToolExecutionResult(AiToolOutcome.Success, CompanionToolGuards.Json(new
        {
            allowed = true,
            destination_id = resolution.Id,
            title = resolution.Title,
            url = resolution.Url,
        }));
    }
}

/// <summary>
/// Finds the learner's most recent unfinished attempt and the place to resume
/// it. Read-only: it never restarts or mutates an attempt, and it deliberately
/// returns nothing while the learner is inside a protected attempt.
/// </summary>
public sealed class CompanionContinueLastActivityTool(
    LearnerDbContext db,
    ICompanionContextResolver contexts,
    ICompanionDestinationRegistry destinations) : IAiToolExecutor
{
    public string Code => "companion_continue_last_activity";
    public AiToolCategory Category => AiToolCategory.Read;
    public string JsonSchemaArgs => """
    {
      "type":"object",
      "properties":{},
      "additionalProperties":false
    }
    """;

    /// <summary>Subtest → the destination that resumes that kind of work.</summary>
    private static readonly Dictionary<string, string> ResumeDestinations = new(StringComparer.OrdinalIgnoreCase)
    {
        ["writing"] = "writing.practice",
        ["speaking"] = "speaking.practice",
        ["reading"] = "reading.practice",
        ["listening"] = "listening.practice",
        ["pronunciation"] = "pronunciation.practice",
    };

    public async Task<AiToolExecutionResult> ExecuteAsync(JsonElement args, AiToolContext ctx, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(ctx.UserId)) return CompanionToolGuards.NoUser();

        var context = await contexts.ResolveAsync(ctx.UserId!, null, ct);
        if (!context.ActionsEnabled) return CompanionToolGuards.ActionsOff();

        var recent = await db.Attempts
            .AsNoTracking()
            .Where(a => a.UserId == ctx.UserId
                        && a.State != AttemptState.Abandoned
                        && a.State != AttemptState.Failed)
            .OrderByDescending(a => a.StartedAt)
            .Select(a => new { a.SubtestCode, a.State, a.StartedAt })
            .FirstOrDefaultAsync(ct);

        if (recent is null)
        {
            var start = await destinations.ResolveAsync("next.actions", context, ct);
            return new AiToolExecutionResult(AiToolOutcome.Success, CompanionToolGuards.Json(new
            {
                found = false,
                suggestion_url = start.Url,
            }));
        }

        var inProgress = recent.State is AttemptState.InProgress or AttemptState.Paused or AttemptState.NotStarted;
        var destinationId = ResumeDestinations.TryGetValue(recent.SubtestCode, out var mapped)
            ? mapped
            : "next.actions";
        var destination = await destinations.ResolveAsync(destinationId, context, ct);

        return new AiToolExecutionResult(AiToolOutcome.Success, CompanionToolGuards.Json(new
        {
            found = true,
            subtest = recent.SubtestCode,
            unfinished = inProgress,
            started_at = recent.StartedAt,
            url = destination.Allowed ? destination.Url : null,
        }));
    }
}

/// <summary>
/// Reports the learner's AI Credit position so the companion can state the cost
/// of a chargeable action truthfully before offering it.
///
/// <para>
/// The balance is read through <c>ICompanionContextResolver</c>, which reads
/// <c>IAiPackageCreditService</c> — the single candidate-facing wallet. There is
/// no companion-specific balance, by decision record DR-001.
/// </para>
/// </summary>
public sealed class CompanionShowAllowanceTool(
    ICompanionContextResolver contexts,
    ICompanionDestinationRegistry destinations) : IAiToolExecutor
{
    public string Code => "companion_show_allowance";
    public AiToolCategory Category => AiToolCategory.Read;
    public string JsonSchemaArgs => """
    {
      "type":"object",
      "properties":{},
      "additionalProperties":false
    }
    """;

    public async Task<AiToolExecutionResult> ExecuteAsync(JsonElement args, AiToolContext ctx, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(ctx.UserId)) return CompanionToolGuards.NoUser();

        var context = await contexts.ResolveAsync(ctx.UserId!, null, ct);
        var topUp = await destinations.ResolveAsync("ai.packages", context, ct);
        var usage = await destinations.ResolveAsync("ai.usage", context, ct);

        return new AiToolExecutionResult(AiToolOutcome.Success, CompanionToolGuards.Json(new
        {
            tier = context.Tier,
            has_subscription = context.HasEligibleSubscription,
            ai_credits_remaining = context.AiCreditsRemaining,
            // False means no new charge can be made at all right now — the
            // companion must say so rather than promise a chargeable action.
            charging_enabled = context.CreditConsumptionEnabled,
            top_up_url = topUp.Url,
            usage_url = usage.Url,
        }));
    }
}

/// <summary>
/// Adds one item to the learner's active study plan (F-104).
///
/// <para>
/// Bounded on purpose: the day's existing workload is summed and the item is
/// refused once the day is full. Without that, a model that loops on this tool
/// would quietly fill a learner's plan with dozens of tasks.
/// </para>
/// </summary>
public sealed class CompanionAddPlanItemTool(
    LearnerDbContext db,
    ICompanionContextResolver contexts) : IAiToolExecutor
{
    public string Code => "companion_add_plan_item";
    public AiToolCategory Category => AiToolCategory.Write;
    public string JsonSchemaArgs => """
    {
      "type":"object",
      "properties":{
        "title":{"type":"string","minLength":3,"maxLength":200},
        "subtest":{"type":"string","enum":["writing","speaking","reading","listening","vocabulary","grammar"]},
        "duration_minutes":{"type":"integer","minimum":5,"maximum":120},
        "reason":{"type":"string","minLength":3,"maxLength":500},
        "days_from_today":{"type":"integer","minimum":0,"maximum":28}
      },
      "required":["title","subtest","duration_minutes","reason"],
      "additionalProperties":false
    }
    """;

    /// <summary>Floor for a day's capacity when the plan carries no explicit budget.</summary>
    private const int MinimumDailyCapacityMinutes = 120;

    public async Task<AiToolExecutionResult> ExecuteAsync(JsonElement args, AiToolContext ctx, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(ctx.UserId)) return CompanionToolGuards.NoUser();

        var context = await contexts.ResolveAsync(ctx.UserId!, null, ct);
        if (!context.ActionsEnabled) return CompanionToolGuards.ActionsOff();

        var plan = await db.StudyPlans
            .Where(p => p.UserId == ctx.UserId && p.IsActive)
            .OrderByDescending(p => p.GeneratedAt)
            .FirstOrDefaultAsync(ct);

        if (plan is null)
        {
            return new AiToolExecutionResult(AiToolOutcome.Success, CompanionToolGuards.Json(new
            {
                added = false,
                reason = "no_active_plan",
            }));
        }

        var title = args.GetProperty("title").GetString()!.Trim();
        var subtest = args.GetProperty("subtest").GetString()!.ToLowerInvariant();
        var duration = args.GetProperty("duration_minutes").GetInt32();
        var rationale = args.GetProperty("reason").GetString()!.Trim();
        var offset = args.TryGetProperty("days_from_today", out var d) && d.TryGetInt32(out var parsed)
            ? Math.Clamp(parsed, 0, 28)
            : 0;

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var dueDate = today.AddDays(offset);

        var committedMinutes = await db.StudyPlanItems
            .Where(i => i.StudyPlanId == plan.Id
                        && i.DueDate == dueDate
                        && i.Status != StudyPlanItemStatus.Completed
                        && i.Status != StudyPlanItemStatus.Skipped
                        && i.Status != StudyPlanItemStatus.Replaced)
            .SumAsync(i => i.DurationMinutes, ct);

        var capacity = Math.Max(MinimumDailyCapacityMinutes, plan.MinutesPerDayBudget);
        if (committedMinutes + duration > capacity)
        {
            return new AiToolExecutionResult(AiToolOutcome.Success, CompanionToolGuards.Json(new
            {
                added = false,
                reason = "day_full",
                due_date = dueDate.ToString("yyyy-MM-dd"),
                committed_minutes = committedMinutes,
                capacity_minutes = capacity,
            }));
        }

        var item = new StudyPlanItem
        {
            Id = $"plan-item-{Guid.NewGuid():N}",
            StudyPlanId = plan.Id,
            Title = title.Length > 200 ? title[..200] : title,
            SubtestCode = subtest,
            DurationMinutes = duration,
            Rationale = rationale.Length > 1024 ? rationale[..1024] : rationale,
            DueDate = dueDate,
            Status = StudyPlanItemStatus.NotStarted,
            Section = offset == 0 ? StudyPlanSections.Today : StudyPlanSections.ThisWeek,
            ItemType = "practice",
            // Companion-added work is the learner's own request, so it outranks
            // generated filler but never a checkpoint the planner scheduled.
            PriorityScore = 500,
            WeekIndex = Math.Max(0, (dueDate.DayNumber - (plan.PlanWindowStart?.DayNumber ?? dueDate.DayNumber)) / 7),
        };

        db.StudyPlanItems.Add(item);
        await db.SaveChangesAsync(ct);

        return new AiToolExecutionResult(AiToolOutcome.Success, CompanionToolGuards.Json(new
        {
            added = true,
            item_id = item.Id,
            due_date = dueDate.ToString("yyyy-MM-dd"),
            duration_minutes = duration,
        }));
    }
}
