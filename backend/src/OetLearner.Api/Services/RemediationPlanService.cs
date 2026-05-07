using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Services;

/// <summary>
/// Mocks V2 Wave 5 — Generates a deterministic 7-day remediation plan from a
/// <see cref="MockReport"/>. Idempotent per (UserId, MockReportId).
///
/// <para>
/// Weakness → drill mapping lives in <see cref="RemediationCatalog"/>; this
/// service is purely the orchestrator (derive weaknesses, distribute slots,
/// persist tasks).
/// </para>
///
/// <para>
/// Optional AI personalisation: when <see cref="EnableAiPersonalisation"/> is
/// flipped on, after the deterministic tasks are persisted the service makes
/// ONE grounded AI call (<see cref="AiFeatureCodes.MockRemediationDraft"/>) to
/// produce a short personalised plan summary. The call is wrapped in a
/// best-effort try/catch so a refusal, timeout, or quota denial NEVER blocks
/// the deterministic plan from being seeded — the summary is simply omitted
/// from the response.
/// </para>
///
/// <para>
/// Grounding choice: the AI call uses <see cref="RuleKind.Grammar"/> +
/// <see cref="AiTaskMode.Coach"/>. Reasoning — Grammar is the only existing
/// rulebook kind whose prompt-builder path imposes no required extras
/// (Writing requires LetterType, Speaking requires CardType) and whose
/// guardrails forbid emitting a candidate score. The grammar rulebook acts
/// as background context only; the user message asks for a learner-facing
/// summary of the deterministic plan, never to grade or invent rules. Adding
/// a dedicated <c>RuleKind.Remediation</c> is out of scope here because it
/// would require a corresponding rulebook directory and prompt-builder
/// branch — both deferred until the AI path graduates from the feature flag.
/// </para>
/// </summary>
public sealed class RemediationPlanService
{
    public const int MaxTasksPerPlan = 7;

    /// <summary>
    /// Feature flag for the optional AI personalisation enrichment. Defaults
    /// to <c>false</c> so the deterministic plan is always seeded without
    /// touching the AI gateway.
    ///
    /// <para>
    /// ⚠ <b>DO NOT FLIP TO TRUE WITHOUT FIRST ADDING A REAL
    /// <c>RuleKind.Remediation</c> (or <c>RuleKind.Mock</c>) GROUNDING.</b>
    /// The current implementation grounds against <see cref="RuleKind.Grammar"/>
    /// because it is the only existing kind whose prompt-builder requires no
    /// extras — that is a structural workaround, not a semantic match. Flipping
    /// this flag without addressing the grounding mismatch ships ungrounded
    /// content under the AI-Gateway invariant.
    /// </para>
    ///
    /// <para>
    /// Reviewer guard: this is intentionally <c>private const</c> (was previously
    /// <c>public</c>) so it cannot be silently flipped from outside this file
    /// during code review. Production rollout requires either a config-driven
    /// <c>MocksOptions</c> flag with documentation, OR a new <c>RuleKind</c>
    /// + rulebook directory + prompt-builder branch.
    /// </para>
    /// </summary>
    private const bool EnableAiPersonalisation = false;

    private readonly LearnerDbContext _db;
    private readonly IAiGatewayService _gateway;
    private readonly ILogger<RemediationPlanService> _logger;

    public RemediationPlanService(
        LearnerDbContext db,
        IAiGatewayService gateway,
        ILogger<RemediationPlanService> logger)
    {
        _db = db;
        _gateway = gateway;
        _logger = logger;
    }

    public async Task<object> GetActivePlanAsync(string userId, CancellationToken ct)
    {
        var rows = await _db.RemediationTasks.AsNoTracking()
            .Where(x => x.UserId == userId && x.Status != RemediationTaskStatuses.Skipped)
            .OrderBy(x => x.DayIndex)
            .ToListAsync(ct);
        return new { items = rows.Select(Project).ToArray() };
    }

    public async Task<object> CompleteTaskAsync(string userId, string taskId, CancellationToken ct)
    {
        var task = await _db.RemediationTasks.FirstOrDefaultAsync(x => x.Id == taskId && x.UserId == userId, ct)
            ?? throw ApiException.NotFound("task_not_found", "Task not found.");
        if (task.Status == RemediationTaskStatuses.Pending)
        {
            task.Status = RemediationTaskStatuses.Completed;
            task.CompletedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(ct);
        }
        return Project(task);
    }

    /// <summary>
    /// Idempotent. If tasks already exist for (userId, reportId), they are returned
    /// unchanged. Otherwise generates up to 7 tasks across weaknesses.
    /// </summary>
    public async Task<object> GenerateFromReportAsync(string userId, string reportId, CancellationToken ct)
    {
        var existing = await _db.RemediationTasks.AsNoTracking()
            .Where(x => x.UserId == userId && x.MockReportId == reportId)
            .OrderBy(x => x.DayIndex)
            .ToListAsync(ct);
        if (existing.Count > 0)
        {
            return new { items = existing.Select(Project).ToArray(), generated = false };
        }

        var report = await _db.MockReports.AsNoTracking().FirstOrDefaultAsync(x => x.Id == reportId, ct)
            ?? throw ApiException.NotFound("report_not_found", "Mock report not found.");
        var attempt = await _db.MockAttempts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == report.MockAttemptId && x.UserId == userId, ct)
            ?? throw ApiException.Forbidden("forbidden", "You do not own this mock report.");

        var weaknesses = DeriveWeaknesses(report.PayloadJson);
        var tasks = BuildTasks(userId, reportId, weaknesses);
        if (tasks.Count == 0)
        {
            return new { items = Array.Empty<object>(), generated = true };
        }

        var now = DateTimeOffset.UtcNow;
        foreach (var t in tasks)
        {
            t.CreatedAt = now;
            _db.RemediationTasks.Add(t);
        }
        await _db.SaveChangesAsync(ct);

        var summary = await TryGenerateAiSummaryAsync(userId, weaknesses, tasks, ct);
        return summary is null
            ? new { items = tasks.Select(Project).ToArray(), generated = true }
            : new { items = tasks.Select(Project).ToArray(), generated = true, summary };
    }

    /// <summary>
    /// Optional AI enrichment. Always returns <c>null</c> when the feature
    /// flag is off, when the gateway refuses on grounding, or on any provider
    /// error — never throws. Callers must remain functional whether or not
    /// this returns a string.
    /// </summary>
    private async Task<string?> TryGenerateAiSummaryAsync(
        string userId,
        List<(string SubtestCode, string WeaknessTag, int Severity)> weaknesses,
        List<RemediationTask> tasks,
        CancellationToken ct)
    {
        if (!EnableAiPersonalisation) return null;
#pragma warning disable CS0162 // Unreachable code detected — guarded by const flag; reachable when flipped.
        if (tasks.Count == 0) return null;
#pragma warning restore CS0162

        try
        {
            var prompt = _gateway.BuildGroundedPrompt(new AiGroundingContext
            {
                Kind = RuleKind.Grammar,
                Profession = ExamProfession.Medicine,
                Task = AiTaskMode.Coach,
            });

            var userMessage = BuildAiUserMessage(weaknesses, tasks);

            var result = await _gateway.CompleteAsync(new AiGatewayRequest
            {
                Prompt = prompt,
                UserInput = userMessage,
                FeatureCode = AiFeatureCodes.MockRemediationDraft,
                UserId = userId,
                Temperature = 0.4,
                MaxTokens = 240,
            }, ct);

            var text = result.Completion?.Trim();
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        catch (PromptNotGroundedException pex)
        {
            _logger.LogError(pex, "Remediation AI summary refused — ungrounded prompt. Deterministic plan stands.");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Remediation AI summary failed; deterministic plan stands.");
            return null;
        }
    }

    private static string BuildAiUserMessage(
        List<(string SubtestCode, string WeaknessTag, int Severity)> weaknesses,
        List<RemediationTask> tasks)
    {
        var weaknessLines = string.Join("\n", weaknesses.Select(w =>
            $"- {w.SubtestCode} (tag={w.WeaknessTag}, severity={w.Severity})"));
        var taskLines = string.Join("\n", tasks.Select(t =>
            $"- Day {t.DayIndex}: {t.Title} ({t.SubtestCode})"));
        return $"""
            Produce a short, encouraging plan summary (≤ 4 sentences, ≤ 240 chars total) for an OET candidate.
            Address the candidate directly ("you"). No medical advice. No scoring. No invented rules.
            Do NOT repeat the day-by-day list verbatim — instead, frame the week's focus.

            Weaknesses (highest severity first):
            {weaknessLines}

            Deterministic 7-day plan:
            {taskLines}
            """;
    }

    /// <summary>
    /// Derives per-subtest weakness tags from the MockReport JSON. Looks for a
    /// current `subTests` array or the legacy `subtestScores` map of subtest →
    /// numeric scaled score. Any score below the canonical Grade B threshold
    /// contributes a weakness. Falls back to an empty list if the payload shape
    /// is unfamiliar.
    /// </summary>
    private static List<(string SubtestCode, string WeaknessTag, int Severity)> DeriveWeaknesses(string payloadJson)
    {
        var output = new List<(string SubtestCode, string WeaknessTag, int Severity)>();
        try
        {
            var payload = JsonSupport.Deserialize<Dictionary<string, object?>>(payloadJson, new Dictionary<string, object?>());
            if (payload.TryGetValue("subtestScores", out var raw) && raw is System.Text.Json.JsonElement el && el.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                foreach (var prop in el.EnumerateObject())
                {
                    if (prop.Value.ValueKind == System.Text.Json.JsonValueKind.Number && prop.Value.TryGetInt32(out var score))
                    {
                        AddWeakness(output, prop.Name, score);
                    }
                }
            }
            if (payload.TryGetValue("subTests", out var subTestsRaw)
                && subTestsRaw is System.Text.Json.JsonElement subTests
                && subTests.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                foreach (var item in subTests.EnumerateArray())
                {
                    if (item.ValueKind != System.Text.Json.JsonValueKind.Object) continue;
                    var subtest = ReadJsonString(item, "subtest")
                        ?? ReadJsonString(item, "name")
                        ?? ReadJsonString(item, "id")
                        ?? "mock";
                    var score = ReadJsonInt(item, "scaledScore") ?? ReadJsonInt(item, "score");
                    if (score.HasValue)
                    {
                        AddWeakness(output, subtest, score.Value);
                    }
                }
            }
        }
        catch
        {
            // ignore — return empty
        }
        return output.OrderByDescending(x => x.Severity).ToList();
    }

    private static void AddWeakness(List<(string SubtestCode, string WeaknessTag, int Severity)> output, string subtest, int score)
    {
        var normalized = subtest.Trim().ToLowerInvariant();
        if (normalized.Length == 0) normalized = "mock";
        var severity = OetScoring.ScaledPassGradeB - Math.Clamp(score, OetScoring.ScaledMin, OetScoring.ScaledMax);
        if (severity <= 0) return;
        var weaknessTag = $"low_{normalized}";
        if (output.Any(x => x.WeaknessTag == weaknessTag)) return;
        output.Add((normalized, weaknessTag, severity));
    }

    private static string? ReadJsonString(System.Text.Json.JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value)) return null;
        return value.ValueKind == System.Text.Json.JsonValueKind.String ? value.GetString() : value.ToString();
    }

    private static int? ReadJsonInt(System.Text.Json.JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value)) return null;
        if (value.ValueKind == System.Text.Json.JsonValueKind.Number && value.TryGetInt32(out var number)) return number;
        if (value.ValueKind == System.Text.Json.JsonValueKind.String && int.TryParse(value.GetString(), out var parsed)) return parsed;
        return null;
    }

    private static List<RemediationTask> BuildTasks(string userId, string reportId, List<(string SubtestCode, string WeaknessTag, int Severity)> weaknesses)
    {
        var tasks = new List<RemediationTask>();
        if (weaknesses.Count == 0) return tasks;

        // Distribute up to 7 days across weaknesses, weighted by severity.
        var totalSeverity = Math.Max(1, weaknesses.Sum(w => w.Severity));
        var slotsByWeakness = new Dictionary<string, int>();
        foreach (var w in weaknesses)
        {
            var share = (int)Math.Round(MaxTasksPerPlan * (double)w.Severity / totalSeverity);
            slotsByWeakness[w.WeaknessTag] = Math.Max(1, share);
        }
        // Trim total to MaxTasksPerPlan.
        while (slotsByWeakness.Values.Sum() > MaxTasksPerPlan)
        {
            var key = slotsByWeakness.OrderByDescending(kv => kv.Value).First().Key;
            slotsByWeakness[key]--;
            if (slotsByWeakness[key] <= 0) slotsByWeakness.Remove(key);
        }

        var dayIndex = 1;
        foreach (var w in weaknesses)
        {
            if (!slotsByWeakness.TryGetValue(w.WeaknessTag, out var slots) || slots <= 0) continue;
            var drills = ResolveDrillsFor(w.SubtestCode, w.WeaknessTag);
            for (var i = 0; i < slots && dayIndex <= MaxTasksPerPlan; i++)
            {
                var pick = drills[i % drills.Count];
                tasks.Add(new RemediationTask
                {
                    Id = Guid.NewGuid().ToString("N"),
                    UserId = userId,
                    MockReportId = reportId,
                    SubtestCode = w.SubtestCode,
                    WeaknessTag = w.WeaknessTag,
                    Title = pick.Label,
                    Description = pick.Description,
                    RouteHref = pick.RouteHref,
                    DayIndex = dayIndex,
                    Status = RemediationTaskStatuses.Pending,
                });
                dayIndex++;
            }
        }
        return tasks;
    }

    /// <summary>
    /// Looks up drills by exact weakness tag first, then falls back to the
    /// coarse <c>low_{subtest}</c> tag, then to a generic single-task drill
    /// when neither is mapped. Always returns ≥1 drill so slot allocation
    /// can proceed.
    /// </summary>
    private static IReadOnlyList<RemediationDrillRef> ResolveDrillsFor(string subtestCode, string weaknessTag)
    {
        var direct = RemediationCatalog.Resolve(weaknessTag);
        if (direct.Count > 0) return direct;

        var coarse = RemediationCatalog.Resolve($"low_{subtestCode}");
        if (coarse.Count > 0) return coarse;

        return new[]
        {
            new RemediationDrillRef(
                "generic.targeted",
                "Targeted practice",
                "Short targeted practice block on your weakest subtest.",
                subtestCode,
                "/dashboard",
                1),
        };
    }

    private static object Project(RemediationTask t) => new
    {
        id = t.Id,
        mockReportId = t.MockReportId,
        subtestCode = t.SubtestCode,
        weaknessTag = t.WeaknessTag,
        title = t.Title,
        description = t.Description,
        routeHref = t.RouteHref,
        dayIndex = t.DayIndex,
        status = t.Status,
        createdAt = t.CreatedAt,
        completedAt = t.CompletedAt,
    };
}
