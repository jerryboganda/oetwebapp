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
    DateTimeOffset CreatedAt,
    /// <summary>
    /// F-077. True when these are the learner's OFFICIAL result rather than a mock or
    /// a practice score. This is the difference between two very different turns: a
    /// mock updates the trend, whereas an official result closes the journey and
    /// decides whether a resit plan is needed. Defaults to false so an unlabelled
    /// import is never silently treated as official — that would let a practice score
    /// retire a real exam journey.
    /// </summary>
    bool IsOfficial = false);

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
        "source":{"type":"string","enum":["learner_reported","screenshot","result_pdf","admin"],"maxLength":24},
        "is_official":{"type":"boolean","description":"True only when these are the learner's OFFICIAL OET result, not a mock or practice score. An official result closes the current journey and decides whether a resit plan is needed."}
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
        var isOfficial = args.TryGetProperty("is_official", out var offEl)
                         && offEl.ValueKind == JsonValueKind.True;

        var proposal = new CompanionScoreProposal(
            ctx.ThreadId!, ctx.TurnId!, listening, reading, writing, speaking, testDate, source,
            DateTimeOffset.UtcNow, isOfficial);
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
                is_official = proposal.IsOfficial,
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
        // F-077. An OFFICIAL result is a different event from a mock: it is the one that
        // closes a journey and decides whether a resit is needed. Recorded with its own
        // kind so later reads can tell "my mock was 340" from "my real result was 340",
        // which is the distinction the whole result-day answer rests on.
        var kind = proposal.IsOfficial ? "official_result" : "score";
        async Task SaveSubtestAsync(string subtest, int? score)
        {
            if (score is null) return;
            await memory.RecordAsync(ctx.UserId!, CompanionMemoryLayers.Learning, kind, subtest,
                $"{subtest} {(proposal.IsOfficial ? "OFFICIAL result" : "score")} {score.Value}{(proposal.TestDate is { } d ? $" on {d:yyyy-MM-dd}" : "")} (source: {proposal.Source})",
                JsonSerializer.Serialize(new { score = score.Value, testDate = proposal.TestDate, source = proposal.Source, official = proposal.IsOfficial }),
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

        // F-077 / F-078. On an OFFICIAL result, compare each score to the learner's OWN
        // stated target and report the comparison. Deliberately no pass mark is invented:
        // when the learner never set a target for a sub-test the verdict is "no target on
        // file", not a defaulted threshold, because a fabricated official requirement is
        // exactly what SAMI §4.2 forbids. The three buckets are therefore met / short /
        // unknown, and the plan guidance differs for each.
        object? result = null;
        if (proposal.IsOfficial)
        {
            var targets = new Dictionary<string, int?>
            {
                ["listening"] = context.TargetListeningScore,
                ["reading"] = context.TargetReadingScore,
                ["writing"] = context.TargetWritingScore,
                ["speaking"] = context.TargetSpeakingScore,
            };
            var scores = new Dictionary<string, int?>
            {
                ["listening"] = proposal.Listening,
                ["reading"] = proposal.Reading,
                ["writing"] = proposal.Writing,
                ["speaking"] = proposal.Speaking,
            };

            var met = new List<string>();
            var fellShort = new List<object>();
            var unknown = new List<string>();
            foreach (var (subtest, score) in scores)
            {
                if (score is null) continue;
                var target = targets[subtest];
                if (target is null)
                {
                    unknown.Add(subtest);
                    continue;
                }
                if (score.Value >= target.Value) met.Add(subtest);
                else fellShort.Add(new { subtest, score = score.Value, target = target.Value, gap = target.Value - score.Value });
            }

            var isResit = fellShort.Count > 0;
            var needTarget = unknown.Count > 0;

            var guidance = isResit
                ? "The learner fell short on at least one sub-test against their own target. Do NOT restart the whole course: build a recovery plan focused only on the short sub-tests, reusing their existing history and tutor feedback. Say plainly which sub-tests are short and by how many points."
                : needTarget
                    ? "Every recorded sub-test met its target where a target exists, but at least one sub-test has NO target on file, so you cannot call the overall outcome. Ask for the missing target(s) and say plainly that you will not assume one."
                    : "Every recorded sub-test met the learner's own target. Say so plainly, and note that only a regulator or the exam board can confirm the official outcome — do not promise a pass.";

            result = new
            {
                official = true,
                targetSource = "learner's own stated target (F-005)",
                met,
                @short = fellShort,
                no_target_on_file = unknown,
                outcome_by_own_target = isResit ? "short_on_at_least_one" : needTarget ? "incomplete_targets" : "met_all_recorded",
                journeyClosureHint = isResit
                    ? "A resit journey is warranted. Preserve the current journey so the learner can still compare what changed, then start a new one focused on the short sub-tests (companion_start_journey with focus_subtests)."
                    : "Consider closing the current journey as passed once the learner agrees; do not close it silently.",
                instruction = guidance,
            };
        }

        return new AiToolExecutionResult(AiToolOutcome.Success, CompanionToolGuards.Json(new
        {
            status = "saved",
            saved,
            isOfficial = proposal.IsOfficial,
            journey = new { id = journey.Id, label = journey.Label },
            result,
            instruction = proposal.IsOfficial
                ? "Confirm what was saved, give the learner the comparison against their own target, then act on the result guidance."
                : "Confirm what was saved, then explain what this means for the learner's priorities and plan.",
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
        // Both kinds count. `official_result` was introduced for F-077 and this filter
        // originally matched only `score`, which made an official result invisible here:
        // a learner who had just recorded a real result and asked why their score moved
        // would have been told they had no confirmed scores on record.
        var bySubtest = scoreEntries
            .Where(m => (m.Kind == "score" || m.Kind == "official_result") && m.ConfirmedAt != null)
            // An official result outranks a mock at the same sub-test regardless of which
            // is newer: a mock sat after the real exam must not read as the learner's
            // current standing. Recency still orders within the same kind.
            .GroupBy(m => m.Subtest)
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(m => m.Kind == "official_result" ? 0 : 1)
                      .ThenByDescending(m => m.RecordedAt)
                      .ToList());
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

        // F-083. Personal bests, derived from the same confirmed records so the two answers
        // can never disagree with each other. Deliberately no historical baseline is
        // invented: with one recorded result there is no "best" to speak of, and the flag
        // below says so instead of presenting a single score as an achievement.
        var bests = bySubtest.ToDictionary(
            g => g.Key,
            g =>
            {
                var official = g.Value.FirstOrDefault(m => m.Kind == "official_result");
                var numeric = g.Value
                    .Select(m => new { m, score = TryReadScore(m.DataJson) })
                    .Where(x => x.score is not null)
                    .ToList();
                var best = numeric.OrderByDescending(x => x.score!.Value).FirstOrDefault();
                return new
                {
                    best_score = best?.score,
                    best_on = best?.m.RecordedAt.ToString("yyyy-MM-dd"),
                    was_official = best?.m.Kind == "official_result",
                    attempts_recorded = numeric.Count,
                    official_result_on_file = official is not null,
                    basis = numeric.Count >= 2 ? "multiple_results" : "single_result",
                };
            });

        return new AiToolExecutionResult(AiToolOutcome.Success, CompanionToolGuards.Json(new
        {
            can_explain = true,
            score_trend = trend,
            personal_bests = bests,
            current_weaknesses = weaknesses.Select(w => new
            {
                category = w.Category,
                subtest = w.Subtest,
                pattern = w.Pattern,
                evidence_count = w.EvidenceCount,
                mastery = w.MasteryScore,
            }),
            instruction = "Explain the change ONLY from the recorded trend and error evidence. If the history does not support a cause, say exactly that and propose how to measure it (e.g. track answer-changing for a week). You may also state the learner's personal best per sub-test, but when `basis` is `single_result` say plainly that one result is not yet a best to beat — never present a lone score as an achievement.",
        }));
    }

    /// <summary>
    /// Reads the numeric score out of a confirmed memory entry's payload. The score is
    /// stored as JSON rather than a column, so this is deliberately defensive: an entry
    /// whose payload predates or postdates the current shape yields null and is skipped,
    /// rather than throwing and taking the whole answer down.
    /// </summary>
    private static int? TryReadScore(string? dataJson)
    {
        if (string.IsNullOrWhiteSpace(dataJson)) return null;
        try
        {
            using var doc = JsonDocument.Parse(dataJson);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                   && doc.RootElement.TryGetProperty("score", out var scoreEl)
                   && scoreEl.ValueKind == JsonValueKind.Number
                ? scoreEl.GetInt32()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>
/// F-085 — the weekly progress report.
///
/// <para>
/// Reports what the learner actually did in the last seven days against what their plan
/// asked for, from <c>StudyPlanItem</c> rows. Every number here is a count or a sum of
/// stored columns; nothing is estimated, and a learner with no plan or no items gets an
/// explicit "nothing to report yet" rather than a fabricated week of zeroes, which would
/// read as a week of failure.
/// </para>
///
/// <para>
/// Two comparisons are deliberately reported separately. <b>Completion</b> is against the
/// plan's own ask. <b>Time</b> is against the learner's declared availability — and only
/// when that availability exists, because comparing actual minutes to an availability
/// nobody ever recorded would invent the denominator.
/// </para>
/// </summary>
public sealed class CompanionWeeklyReportTool(
    ICompanionContextResolver contexts,
    LearnerDbContext db,
    TimeProvider clock) : IAiToolExecutor
{
    public string Code => "companion_weekly_report";
    public string Description => "Report the learner's last seven days: plan items completed, missed and still due, minutes studied against the plan and against their declared availability, and which sub-tests the week actually went to. Reports only what the plan data supports.";
    public AiToolCategory Category => AiToolCategory.Read;
    public string JsonSchemaArgs => "{\"type\":\"object\",\"properties\":{},\"additionalProperties\":false}";

    public async Task<AiToolExecutionResult> ExecuteAsync(JsonElement args, AiToolContext ctx, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(ctx.UserId)) return CompanionToolGuards.NoUser();
        var context = await contexts.ResolveAsync(ctx.UserId!, new CompanionContextEnvelope(), ct);
        if (!context.ActionsEnabled) return CompanionToolGuards.ActionsOff();
        return await BuildAsync(ctx.UserId!, context, ct);
    }

    private async Task<AiToolExecutionResult> BuildAsync(string userId, CompanionTurnContext context, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var weekStart = today.AddDays(-6);

        // The learner's active plan. A learner may have none, or only an inactive one.
        var plan = await db.StudyPlans.AsNoTracking()
            .Where(p => p.UserId == userId && p.IsActive)
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => new { p.Id })
            .FirstOrDefaultAsync(ct);

        if (plan is null)
        {
            return new AiToolExecutionResult(AiToolOutcome.Success, CompanionToolGuards.Json(new
            {
                can_report = false,
                reason = "no_active_plan",
                instruction = "There is no active study plan on this account, so there is no week to report against. Say that plainly and offer to build a plan — do not describe a week of study that was never planned.",
            }));
        }

        var items = await db.StudyPlanItems.AsNoTracking()
            .Where(i => i.StudyPlanId == plan.Id && i.DueDate >= weekStart && i.DueDate <= today)
            .Select(i => new
            {
                i.Title,
                i.SubtestCode,
                i.DurationMinutes,
                i.Status,
                i.DueDate,
                i.CompletedAt,
                i.ActualMinutesSpent,
            })
            .ToListAsync(ct);

        if (items.Count == 0)
        {
            return new AiToolExecutionResult(AiToolOutcome.Success, CompanionToolGuards.Json(new
            {
                can_report = false,
                reason = "no_items_this_week",
                week_start = weekStart.ToString("yyyy-MM-dd"),
                week_end = today.ToString("yyyy-MM-dd"),
                instruction = "The plan holds no items for the last seven days, so there is nothing to report. Say exactly that rather than presenting an empty week as a failure.",
            }));
        }

        var completed = items.Where(i => i.Status == StudyPlanItemStatus.Completed).ToList();
        var missed = items.Where(i => i.Status == StudyPlanItemStatus.NotStarted && i.DueDate < today).ToList();
        var stillDue = items.Where(i => i.Status != StudyPlanItemStatus.Completed
                                        && i.Status != StudyPlanItemStatus.Skipped
                                        && i.DueDate >= today).ToList();
        var skipped = items.Where(i => i.Status == StudyPlanItemStatus.Skipped).ToList();

        var plannedMinutes = items.Sum(i => i.DurationMinutes);
        // Prefer the learner's own reported time when present; fall back to the item's
        // planned duration only for items they actually completed, so an unstarted item
        // cannot inflate "time studied".
        var studiedMinutes = completed.Sum(i => i.ActualMinutesSpent ?? i.DurationMinutes);

        var bySubtest = items
            .GroupBy(i => i.SubtestCode)
            .Select(g => new
            {
                subtest = g.Key,
                items = g.Count(),
                completed = g.Count(i => i.Status == StudyPlanItemStatus.Completed),
                planned_minutes = g.Sum(i => i.DurationMinutes),
            })
            .OrderByDescending(x => x.items)
            .ToList();

        // Time-against-availability is only meaningful when availability was declared.
        var declaredPerDay = await db.CompanionAvailabilities.AsNoTracking()
            .Where(a => a.UserId == userId)
            .Select(a => a.DailyMinutesJson)
            .FirstOrDefaultAsync(ct);

        int? declaredMinutesForWeek = null;
        var availabilityDeclared = false;
        if (!string.IsNullOrWhiteSpace(declaredPerDay))
        {
            try
            {
                var perDay = JsonSerializer.Deserialize<List<int>>(declaredPerDay);
                if (perDay is { Count: 7 })
                {
                    // Align the stored week to the same seven days being reported, so the
                    // denominator describes this week rather than a generic one.
                    declaredMinutesForWeek = 0;
                    for (var offset = 0; offset < 7; offset++)
                    {
                        var day = weekStart.AddDays(offset);
                        declaredMinutesForWeek += perDay[(int)day.DayOfWeek];
                    }
                    availabilityDeclared = declaredMinutesForWeek > 0;
                }
            }
            catch (JsonException)
            {
                // Unreadable availability is treated as not declared rather than as zero.
            }
        }

        return new AiToolExecutionResult(AiToolOutcome.Success, CompanionToolGuards.Json(new
        {
            can_report = true,
            week_start = weekStart.ToString("yyyy-MM-dd"),
            week_end = today.ToString("yyyy-MM-dd"),
            completion = new
            {
                items_in_week = items.Count,
                completed = completed.Count,
                missed = missed.Count,
                still_due = stillDue.Count,
                skipped = skipped.Count,
                completion_rate = items.Count > 0
                    ? Math.Round(100.0 * completed.Count / items.Count, 1)
                    : (double?)null,
            },
            time = new
            {
                planned_minutes = plannedMinutes,
                studied_minutes = studiedMinutes,
                studied_minutes_basis = "learner-reported minutes where present, else the item's planned minutes for completed items only",
                declared_minutes_this_week = declaredMinutesForWeek,
                availability_declared = availabilityDeclared,
            },
            by_subtest = bySubtest,
            completed_items = completed.Select(i => new { i.Title, i.SubtestCode, due = i.DueDate.ToString("yyyy-MM-dd") }).Take(12),
            missed_items = missed.Select(i => new { i.Title, i.SubtestCode, due = i.DueDate.ToString("yyyy-MM-dd") }).Take(12),
            instruction = availabilityDeclared
                ? "Report the week plainly: what was completed, what was missed, where the time went. Compare minutes to the learner's declared availability, and if they missed items, ask what got in the way rather than assuming low motivation. Do not invent a reason the plan data does not show."
                : "Report the week plainly: what was completed, what was missed, where the time went. Availability has NOT been declared, so do not compare their minutes to any target and do not imply they fell short of one — offer to record their real availability instead.",
        }));
    }
}
