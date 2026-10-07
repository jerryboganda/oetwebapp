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
/// Holds one score-import proposal per user+thread, set by
/// <c>companion_record_scores</c> and consumed by <c>companion_confirm_scores</c>.
/// Replaced by a newer preview; expires 2 h. Mirrors CompanionStudyPlanProposalStore:
/// the model can never write scores directly — a later human turn confirms.
/// </summary>
public sealed class CompanionScoreProposalStore(IMemoryCache cache)
{
    private static string Key(string userId, string threadId) => $"score-proposal::{userId}::{threadId}";

    public void Store(string userId, string threadId, CompanionScoreProposal proposal) =>
        cache.Set(Key(userId, threadId), proposal, TimeSpan.FromHours(2));

    public CompanionScoreProposal? Get(string userId, string threadId) =>
        cache.TryGetValue(Key(userId, threadId), out CompanionScoreProposal? p) ? p : null;

    public void Consume(string userId, string threadId) => cache.Remove(Key(userId, threadId));
}

/// <summary>A server-held score-import proposal awaiting learner confirmation (F-008/F-090).</summary>
public sealed record CompanionScoreProposal(
    string ThreadId,
    string PreviewTurnId,
    int? Listening,
    int? Reading,
    int? Writing,
    int? Speaking,
    DateOnly? TestDate,
    string Source,
    DateTimeOffset CreatedAt);

/// <summary>
/// SAMI Wave 1 memory/planning tools. Every write tool goes through the confirm-before-save
/// contract where the fact is consequential (scores, exam dates): preview first, persist only
/// after an explicit learner yes in a LATER turn (UAT Pack 3 Tests 01/02).
/// </summary>
public sealed class CompanionRecordScoresTool(
    ICompanionContextResolver contexts,
    CompanionScoreProposalStore store) : IAiToolExecutor
{
    public string Code => "companion_record_scores";
    public string Description => "Preview saving OET sub-test scores the learner reported or that you read from their uploaded report. Shows exactly what will be stored and waits for the learner to confirm. Does not save yet.";
    public AiToolCategory Category => AiToolCategory.Read;
    public string JsonSchemaArgs => """
    {
      "type":"object",
      "properties":{
        "listening":{"type":"integer","minimum":100,"maximum":500},
        "reading":{"type":"integer","minimum":100,"maximum":500},
        "writing":{"type":"integer","minimum":100,"maximum":500},
        "speaking":{"type":"integer","minimum":100,"maximum":500},
        "test_date":{"type":"string","maxLength":10},
        "source":{"type":"string","enum":["learner_reported","screenshot","result_pdf","admin"],"maxLength":24}
      },
      "additionalProperties":false
    }
    """;

    public async Task<AiToolExecutionResult> ExecuteAsync(JsonElement args, AiToolContext ctx, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(ctx.UserId)) return CompanionToolGuards.NoUser();
        if (string.IsNullOrEmpty(ctx.ThreadId) || string.IsNullOrEmpty(ctx.TurnId))
            return new AiToolExecutionResult(AiToolOutcome.RbacDenied, null, "no_thread", "thread and turn ids required");
        var context = await contexts.ResolveAsync(ctx.UserId!, new CompanionContextEnvelope(), ct);
        if (!context.ActionsEnabled) return CompanionToolGuards.ActionsOff();

        int? Get(string name) => args.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.Number ? el.GetInt32() : null;
        var listening = Get("listening");
        var reading = Get("reading");
        var writing = Get("writing");
        var speaking = Get("speaking");
        if (listening is null && reading is null && writing is null && speaking is null)
            return new AiToolExecutionResult(AiToolOutcome.ArgsInvalid, null, "no_scores", "provide at least one sub-test score");

        DateOnly? testDate = null;
        if (args.TryGetProperty("test_date", out var dateEl) && dateEl.ValueKind == JsonValueKind.String
            && DateOnly.TryParse(dateEl.GetString(), out var parsed))
        {
            testDate = parsed;
        }
        var source = args.TryGetProperty("source", out var srcEl) && srcEl.ValueKind == JsonValueKind.String
            ? srcEl.GetString()! : "learner_reported";

        var proposal = new CompanionScoreProposal(
            ctx.ThreadId!, ctx.TurnId!, listening, reading, writing, speaking, testDate, source,
            DateTimeOffset.UtcNow);
        store.Store(ctx.UserId!, ctx.ThreadId!, proposal);

        return new AiToolExecutionResult(AiToolOutcome.Success, CompanionToolGuards.Json(new
        {
            status = "awaiting_confirmation",
            extracted = new
            {
                listening = proposal.Listening,
                reading = proposal.Reading,
                writing = proposal.Writing,
                speaking = proposal.Speaking,
                test_date = proposal.TestDate?.ToString("yyyy-MM-dd"),
                source = proposal.Source,
            },
            instruction = "Tell the learner exactly what you extracted and ask them to confirm before anything is saved. Only call companion_confirm_scores after they explicitly agree.",
        }));
    }
}

public sealed class CompanionConfirmScoresTool(
    ICompanionContextResolver contexts,
    CompanionScoreProposalStore proposals,
    ICompanionMemoryService memory,
    ICompanionJourneyService journeys) : IAiToolExecutor
{
    public string Code => "companion_confirm_scores";
    public string Description => "Save the previously previewed scores to the learner's memory after they explicitly confirmed them. Applies the server-held proposal from companion_record_scores.";
    public AiToolCategory Category => AiToolCategory.Write;
    public string JsonSchemaArgs => "{\"type\":\"object\",\"properties\":{},\"additionalProperties\":false}";

    public async Task<AiToolExecutionResult> ExecuteAsync(JsonElement args, AiToolContext ctx, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(ctx.UserId)) return CompanionToolGuards.NoUser();
        if (string.IsNullOrEmpty(ctx.ThreadId) || string.IsNullOrEmpty(ctx.TurnId))
            return new AiToolExecutionResult(AiToolOutcome.RbacDenied, null, "no_thread", "thread and turn ids required");
        var context = await contexts.ResolveAsync(ctx.UserId!, new CompanionContextEnvelope(), ct);
        if (!context.ActionsEnabled) return CompanionToolGuards.ActionsOff();

        var proposal = proposals.Get(ctx.UserId!, ctx.ThreadId!);
        if (proposal is null)
            return new AiToolExecutionResult(AiToolOutcome.ArgsInvalid, null, "no_proposal",
                "no pending score proposal — call companion_record_scores first");

        if (string.Equals(proposal.PreviewTurnId, ctx.TurnId, StringComparison.Ordinal))
            return new AiToolExecutionResult(AiToolOutcome.ArgsInvalid, null, "same_turn",
                "the learner must confirm in a later message before scores are saved");

        var saved = new List<string>();
        async Task SaveSubtestAsync(string subtest, int? score)
        {
            if (score is null) return;
            await memory.RecordAsync(ctx.UserId!, CompanionMemoryLayers.Learning, "score", subtest,
                $"{subtest} score {score.Value}{(proposal.TestDate is { } d ? $" on {d:yyyy-MM-dd}" : "")} (source: {proposal.Source})",
                JsonSerializer.Serialize(new { score = score.Value, testDate = proposal.TestDate, source = proposal.Source }),
                "chat", ctx.ThreadId, DateTimeOffset.UtcNow, ct);
            saved.Add($"{subtest}={score.Value}");
        }
        await SaveSubtestAsync("listening", proposal.Listening);
        await SaveSubtestAsync("reading", proposal.Reading);
        await SaveSubtestAsync("writing", proposal.Writing);
        await SaveSubtestAsync("speaking", proposal.Speaking);
        if (proposal.TestDate is { } date)
        {
            await memory.RecordAsync(ctx.UserId!, CompanionMemoryLayers.Learning, "test_date", "general",
                $"test date {date:yyyy-MM-dd} (source: {proposal.Source})", null, "chat", ctx.ThreadId,
                DateTimeOffset.UtcNow, ct);
            saved.Add($"test_date={date:yyyy-MM-dd}");
        }

        proposals.Consume(ctx.UserId!, ctx.ThreadId!);
        var journey = await journeys.GetCurrentAsync(ctx.UserId!, ct);

        return new AiToolExecutionResult(AiToolOutcome.Success, CompanionToolGuards.Json(new
        {
            status = "saved",
            saved,
            journey = new { id = journey.Id, label = journey.Label },
            instruction = "Confirm what was saved, then explain what this means for the learner's priorities and plan.",
        }));
    }
}

public sealed class CompanionSetAvailabilityTool(
    ICompanionContextResolver contexts,
    ICompanionAvailabilityService availability) : IAiToolExecutor
{
    public string Code => "companion_set_availability";
    public string Description => "Save when and how long the learner can study: minutes per weekday (Monday-first), night-shift and long-day-shift days, and travel mode. Replanning uses this.";
    public AiToolCategory Category => AiToolCategory.Write;
    public string JsonSchemaArgs => """
    {
      "type":"object",
      "properties":{
        "daily_minutes":{"type":"array","items":{"type":"integer","minimum":0,"maximum":600},"minItems":7,"maxItems":7},
        "night_shift_days":{"type":"array","items":{"type":"integer","minimum":0,"maximum":6},"maxItems":7},
        "long_day_shift_days":{"type":"array","items":{"type":"integer","minimum":0,"maximum":6},"maxItems":7},
        "travel_mode":{"type":"boolean"},
        "travel_minutes_per_day":{"type":"integer","minimum":0,"maximum":600},
        "travel_until":{"type":"string","maxLength":10},
        "preferred_study_time":{"type":"string","maxLength":5}
      },
      "additionalProperties":false
    }
    """;

    public async Task<AiToolExecutionResult> ExecuteAsync(JsonElement args, AiToolContext ctx, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(ctx.UserId)) return CompanionToolGuards.NoUser();
        var context = await contexts.ResolveAsync(ctx.UserId!, new CompanionContextEnvelope(), ct);
        if (!context.ActionsEnabled) return CompanionToolGuards.ActionsOff();

        int[]? Daily(string name)
        {
            if (!args.TryGetProperty(name, out var el) || el.ValueKind != JsonValueKind.Array) return null;
            return el.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Number).Select(x => x.GetInt32()).ToArray();
        }
        DateOnly? travelUntil = null;
        if (args.TryGetProperty("travel_until", out var untilEl) && untilEl.ValueKind == JsonValueKind.String
            && DateOnly.TryParse(untilEl.GetString(), out var until)) travelUntil = until;
        string? preferred = args.TryGetProperty("preferred_study_time", out var prefEl)
            && prefEl.ValueKind == JsonValueKind.String ? prefEl.GetString() : null;

        try
        {
            var row = await availability.UpsertAsync(
                ctx.UserId!,
                Daily("daily_minutes"),
                Daily("night_shift_days"),
                Daily("long_day_shift_days"),
                args.TryGetProperty("travel_mode", out var tmEl) && tmEl.ValueKind == JsonValueKind.True ? true : null,
                args.TryGetProperty("travel_minutes_per_day", out var tmpEl) && tmpEl.ValueKind == JsonValueKind.Number ? tmpEl.GetInt32() : null,
                travelUntil,
                preferred,
                ct);
            return new AiToolExecutionResult(AiToolOutcome.Success, CompanionToolGuards.Json(new
            {
                status = "saved",
                availability = new
                {
                    daily_minutes = JsonSerializer.Deserialize<int[]>(row.DailyMinutesJson),
                    night_shift_days = JsonSerializer.Deserialize<int[]>(row.NightShiftDaysJson),
                    long_day_shift_days = JsonSerializer.Deserialize<int[]>(row.LongDayShiftDaysJson),
                    travel_mode = row.TravelMode,
                    travel_until = row.TravelUntil?.ToString("yyyy-MM-dd"),
                    preferred_study_time = row.PreferredStudyTime,
                },
            }));
        }
        catch (ArgumentException ex)
        {
            return new AiToolExecutionResult(AiToolOutcome.ArgsInvalid, null, "invalid_availability", ex.Message);
        }
    }
}

public sealed class CompanionStartJourneyTool(
    ICompanionContextResolver contexts,
    ICompanionJourneyService journeys,
    ICompanionMemoryService memory) : IAiToolExecutor
{
    public string Code => "companion_start_journey";
    public string Description => "Start a new exam-preparation journey (for example a Writing-only resit) while keeping all previous journeys and history readable for comparison.";
    public AiToolCategory Category => AiToolCategory.Write;
    public string JsonSchemaArgs => """
    {
      "type":"object",
      "properties":{
        "label":{"type":"string","minLength":1,"maxLength":128},
        "exam_date":{"type":"string","maxLength":10},
        "focus_subtests":{"type":"array","items":{"type":"string","enum":["reading","writing","listening","speaking"]},"maxItems":4}
      },
      "required":["label"],
      "additionalProperties":false
    }
    """;

    public async Task<AiToolExecutionResult> ExecuteAsync(JsonElement args, AiToolContext ctx, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(ctx.UserId)) return CompanionToolGuards.NoUser();
        var context = await contexts.ResolveAsync(ctx.UserId!, new CompanionContextEnvelope(), ct);
        if (!context.ActionsEnabled) return CompanionToolGuards.ActionsOff();

        var label = args.GetProperty("label").GetString()!;
        DateOnly? examDate = null;
        if (args.TryGetProperty("exam_date", out var dateEl) && dateEl.ValueKind == JsonValueKind.String
            && DateOnly.TryParse(dateEl.GetString(), out var parsed)) examDate = parsed;
        var focus = args.TryGetProperty("focus_subtests", out var focusEl) && focusEl.ValueKind == JsonValueKind.Array
            ? focusEl.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String)
                .Select(x => x.GetString()!).ToList()
            : null;

        var journey = await journeys.StartAsync(ctx.UserId!, label, examDate, focus, ct);
        await memory.RecordAsync(ctx.UserId!, CompanionMemoryLayers.Journey, "journey_marker", "general",
            $"journey started: {label}{(examDate is { } d ? $" (exam {d:yyyy-MM-dd})" : "")}{(focus is { Count: > 0 } ? $" focusing {string.Join(',', focus)}" : "")}",
            JsonSerializer.Serialize(new { journeyId = journey.Id, focus }),
            "chat", ctx.ThreadId, DateTimeOffset.UtcNow, ct);

        return new AiToolExecutionResult(AiToolOutcome.Success, CompanionToolGuards.Json(new
        {
            status = "started",
            journey = new { id = journey.Id, label = journey.Label, exam_date = journey.TargetExamDate?.ToString("yyyy-MM-dd"), focus_subtests = focus },
            instruction = "Previous journeys and scores are preserved; say so, then propose what this journey's plan should focus on.",
        }));
    }
}

public sealed class CompanionRequestHandoffTool(
    ICompanionContextResolver contexts,
    OetLearner.Api.Services.Companion.ICompanionHandoffService handoffs,
    ICompanionDestinationRegistry destinations) : IAiToolExecutor
{
    public string Code => "companion_request_handoff";
    public string Description => "Prepare a tutor or support handoff from this chat: the learner's issue, their recorded scores and evidenced errors, and the thread reference. Creates the handoff and returns the summary plus where it went.";
    public AiToolCategory Category => AiToolCategory.Write;
    public string JsonSchemaArgs => """
    {
      "type":"object",
      "properties":{
        "route":{"type":"string","enum":["tutor","support"],"maxLength":16},
        "issue":{"type":"string","minLength":3,"maxLength":1024}
      },
      "required":["route","issue"],
      "additionalProperties":false
    }
    """;

    public async Task<AiToolExecutionResult> ExecuteAsync(JsonElement args, AiToolContext ctx, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(ctx.UserId)) return CompanionToolGuards.NoUser();
        var context = await contexts.ResolveAsync(ctx.UserId!, new CompanionContextEnvelope(), ct);
        if (!context.ActionsEnabled) return CompanionToolGuards.ActionsOff();

        var route = args.TryGetProperty("route", out var routeEl) && routeEl.ValueKind == JsonValueKind.String
            ? routeEl.GetString()! : "tutor";
        var issue = args.TryGetProperty("issue", out var issueEl) && issueEl.ValueKind == JsonValueKind.String
            ? issueEl.GetString()! : string.Empty;
        if (issue.Trim().Length < 3)
            return new AiToolExecutionResult(AiToolOutcome.ArgsInvalid, null, "issue_required", "state the learner's issue in their words");

        var threadId = string.IsNullOrEmpty(ctx.ThreadId) ? "no-thread" : ctx.ThreadId!;
        var handoff = await handoffs.CreateAsync(ctx.UserId!, threadId, route, issue, ct);
        var destinationId = route == "support" ? "support" : "escalations";
        var destination = await destinations.ResolveAsync(destinationId, context, ct);

        return new AiToolExecutionResult(AiToolOutcome.Success, CompanionToolGuards.Json(new
        {
            handoff_id = handoff.Id,
            route = handoff.Route,
            status = handoff.Status,
            summary = handoff.Summary,
            track_url = destination.Url,
            instruction = "Show the learner the summary (it is built from their real scores and errors), tell them which route it went to, and point them to the tracking page.",
        }));
    }
}

public sealed class CompanionUpgradeTool(
    ICompanionContextResolver contexts,
    ICompanionDestinationRegistry destinations) : IAiToolExecutor
{
    public string Code => "companion_upgrade";
    public string Description => "Open the contextual upgrade for exactly what the learner is blocked on, and confirm that the current chat resumes after purchase. Shows tier options with live prices.";
    public AiToolCategory Category => AiToolCategory.Read;
    public string JsonSchemaArgs => """
    {
      "type":"object",
      "properties":{
        "need":{"type":"string","maxLength":200}
      },
      "additionalProperties":false
    }
    """;

    public async Task<AiToolExecutionResult> ExecuteAsync(JsonElement args, AiToolContext ctx, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(ctx.UserId)) return CompanionToolGuards.NoUser();
        var context = await contexts.ResolveAsync(ctx.UserId!, new CompanionContextEnvelope(), ct);
        var need = args.TryGetProperty("need", out var needEl) && needEl.ValueKind == JsonValueKind.String
            ? needEl.GetString()! : "more AI features";
        var packages = await destinations.ResolveAsync("ai.packages", context, ct);

        return new AiToolExecutionResult(AiToolOutcome.Success, CompanionToolGuards.Json(new
        {
            blocked_need = need,
            current_tier = context.Tier,
            upgrade_url = packages.Url,
            resume = new
            {
                thread_id = ctx.ThreadId,
                // Threads live server-side: after purchase the entitlement check
                // passes and this same thread continues untouched (F-112).
                instruction = "After the purchase completes, this exact chat continues from where it stopped — nothing needs re-explaining.",
            },
            instruction = "Show the upgrade that matches the blocked need (not the whole price page), say clearly that the chat continues where it left off after purchase, and never promise a feature the learner's tier would not unlock.",
        }));
    }
}

public sealed class CompanionNextBestActionTool(
    ICompanionContextResolver contexts,
    INextBestActionService nextBest) : IAiToolExecutor
{
    public string Code => "companion_next_best_action";
    public string Description => "Choose the single highest-value task for the learner right now given their available minutes, weaknesses and exam proximity. Returns the task, why, and a link that opens it.";
    public AiToolCategory Category => AiToolCategory.Read;
    public string JsonSchemaArgs => """
    {
      "type":"object",
      "properties":{
        "minutes_available":{"type":"integer","minimum":5,"maximum":240}
      },
      "additionalProperties":false
    }
    """;

    public async Task<AiToolExecutionResult> ExecuteAsync(JsonElement args, AiToolContext ctx, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(ctx.UserId)) return CompanionToolGuards.NoUser();
        var context = await contexts.ResolveAsync(ctx.UserId!, new CompanionContextEnvelope(), ct);
        if (!context.ActionsEnabled) return CompanionToolGuards.ActionsOff();

        var minutes = args.TryGetProperty("minutes_available", out var mEl) && mEl.ValueKind == JsonValueKind.Number
            ? mEl.GetInt32() : 30;
        var decision = await nextBest.DecideAsync(ctx.UserId!, context, minutes, ct);
        return new AiToolExecutionResult(AiToolOutcome.Success, CompanionToolGuards.Json(new
        {
            today = decision.Today.ToString("yyyy-MM-dd"),
            action = new
            {
                title = decision.Title,
                subtest = decision.Subtest,
                minutes = decision.Minutes,
                kind = decision.Kind,
                target_url = decision.TargetUrl,
            },
            why = decision.Why,
            instruction = "State the ONE chosen action and why, then offer to open/start it. Do not present a long menu.",
        }));
    }
}

public sealed class CompanionTrainMistakesTool(
    ICompanionContextResolver contexts,
    IErrorDnaService errorDna) : IAiToolExecutor
{
    public string Code => "companion_train_mistakes";
    public string Description => "Build a short mixed drill from the learner's OWN evidenced recurring errors (Error DNA). Never invents weaknesses; returns fewer items when evidence is thin.";
    public AiToolCategory Category => AiToolCategory.Read;
    public string JsonSchemaArgs => "{\"type\":\"object\",\"properties\":{},\"additionalProperties\":false}";

    public async Task<AiToolExecutionResult> ExecuteAsync(JsonElement args, AiToolContext ctx, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(ctx.UserId)) return CompanionToolGuards.NoUser();
        var context = await contexts.ResolveAsync(ctx.UserId!, new CompanionContextEnvelope(), ct);
        if (!context.ActionsEnabled) return CompanionToolGuards.ActionsOff();

        var weaknesses = await errorDna.TopWeaknessesAsync(ctx.UserId!, 5, ct);
        var items = weaknesses
            .Select(w => new
            {
                error_id = w.Id,
                category = w.Category,
                subtest = w.Subtest,
                pattern = w.Pattern,
                evidence_count = w.EvidenceCount,
                mastery = w.MasteryScore,
                drill_prompt = DrillPromptFor(w),
            })
            .ToList();

        return new AiToolExecutionResult(AiToolOutcome.Success, CompanionToolGuards.Json(new
        {
            weakness_count = items.Count,
            items,
            instruction = items.Count == 0
                ? "There is no recorded error evidence yet — say so honestly and offer a short diagnostic instead of inventing weaknesses."
                : "Deliver ONE item at a time from this drill and wait for the learner's answer; re-test missed items again later (spaced reinforcement).",
        }));
    }

    private static string DrillPromptFor(ErrorDnaEntry w) => w.Category switch
    {
        "grammar" or "writing_language" => $"Write one OET-style sentence using \u201C{w.Pattern}\u201D correctly.",
        "vocabulary" => $"Use the term behind \u201C{w.Pattern}\u201D in a patient-friendly sentence, then give a clinical synonym.",
        "timing" => $"Complete a 3-question {w.Subtest} micro-set in 2 minutes, focusing on \u201C{w.Pattern}\u201D.",
        _ => $"Answer one short {w.Subtest} item that targets \u201C{w.Pattern}\u201D.",
    };
}

public sealed class CompanionWhyScoreChangeTool(
    ICompanionContextResolver contexts,
    ICompanionMemoryService memory,
    IErrorDnaService errorDna) : IAiToolExecutor
{
    public string Code => "companion_why_score_change";
    public string Description => "Explain why a sub-test score changed, using the learner's recorded results and error evidence. Refuses to guess a cause that the history does not support.";
    public AiToolCategory Category => AiToolCategory.Read;
    public string JsonSchemaArgs => "{\"type\":\"object\",\"properties\":{},\"additionalProperties\":false}";

    public async Task<AiToolExecutionResult> ExecuteAsync(JsonElement args, AiToolContext ctx, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(ctx.UserId)) return CompanionToolGuards.NoUser();
        var context = await contexts.ResolveAsync(ctx.UserId!, new CompanionContextEnvelope(), ct);
        if (!context.ActionsEnabled) return CompanionToolGuards.ActionsOff();

        var scoreEntries = await memory.GetCurrentAsync(ctx.UserId!, CompanionMemoryLayers.Learning, ct);
        var bySubtest = scoreEntries
            .Where(m => m.Kind == "score" && m.ConfirmedAt != null)
            .GroupBy(m => m.Subtest)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(m => m.RecordedAt).ToList());
        if (bySubtest.Count == 0)
        {
            return new AiToolExecutionResult(AiToolOutcome.Success, CompanionToolGuards.Json(new
            {
                can_explain = false,
                reason = "no_confirmed_scores",
                instruction = "The learner has no confirmed recorded scores yet — say so and offer to record their results first.",
            }));
        }

        var trend = bySubtest.ToDictionary(
            g => g.Key,
            g => g.Value.Take(3).Select(m => new { date = m.RecordedAt.ToString("yyyy-MM-dd"), content = m.Content }).ToArray());
        var weaknesses = await errorDna.TopWeaknessesAsync(ctx.UserId!, 6, ct);

        return new AiToolExecutionResult(AiToolOutcome.Success, CompanionToolGuards.Json(new
        {
            can_explain = true,
            score_trend = trend,
            current_weaknesses = weaknesses.Select(w => new
            {
                category = w.Category,
                subtest = w.Subtest,
                pattern = w.Pattern,
                evidence_count = w.EvidenceCount,
                mastery = w.MasteryScore,
            }),
            instruction = "Explain the change ONLY from the recorded trend and error evidence. If the history does not support a cause, say exactly that and propose how to measure it (e.g. track answer-changing for a week).",
        }));
    }
}
