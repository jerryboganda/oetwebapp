using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services;

/// <summary>
/// Mocks V2 Wave 5 — Generates a deterministic 7-day remediation plan from a
/// <see cref="MockReport"/>. Idempotent per (UserId, MockReportId).
/// AI-personalisation hooks (`AiFeatureCodes.MockRemediationDraft`) sit on top
/// and may overwrite Title/Description after grounded validation; until that
/// pathway runs, the deterministic plan stands on its own.
/// </summary>
public sealed class RemediationPlanService
{
    public const int MaxTasksPerPlan = 7;

    private readonly LearnerDbContext _db;

    public RemediationPlanService(LearnerDbContext db) { _db = db; }

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
        return new { items = tasks.Select(Project).ToArray(), generated = true };
    }

    /// <summary>
    /// Derives per-subtest weakness tags from the MockReport JSON. Looks for a
    /// `subtestScores` map of subtest → numeric scaled score; any subtest below
    /// 350 contributes a weakness. Falls back to an empty list if the payload
    /// shape is unfamiliar.
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
                        if (score < 350)
                        {
                            output.Add((prop.Name.ToLowerInvariant(), $"low_{prop.Name.ToLowerInvariant()}", 350 - score));
                        }
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
            var template = TemplatesFor(w.SubtestCode);
            for (var i = 0; i < slots && dayIndex <= MaxTasksPerPlan; i++)
            {
                var pick = template[i % template.Length];
                tasks.Add(new RemediationTask
                {
                    Id = Guid.NewGuid().ToString("N"),
                    UserId = userId,
                    MockReportId = reportId,
                    SubtestCode = w.SubtestCode,
                    WeaknessTag = w.WeaknessTag,
                    Title = pick.Title,
                    Description = pick.Description,
                    RouteHref = pick.Route,
                    DayIndex = dayIndex,
                    Status = RemediationTaskStatuses.Pending,
                });
                dayIndex++;
            }
        }
        return tasks;
    }

    private record Template(string Title, string Description, string Route);

    private static Template[] TemplatesFor(string subtest) => subtest switch
    {
        "listening" => new[]
        {
            new Template("Listening Part B drill", "Two short workplace dialogues with focused note-taking.", "/listening"),
            new Template("Listening Part A consultation drill", "Practice picking out details from a 5-minute consultation.", "/listening"),
            new Template("Recalls audio focus session", "10 minutes hearing and typing high-risk clinical terms.", "/recalls/words"),
        },
        "reading" => new[]
        {
            new Template("Reading Part C scan-and-locate", "One Part C text with explanations after each item.", "/reading"),
            new Template("Reading vocabulary review", "Top distractors from your last mock, in context.", "/reading"),
            new Template("Reading timed run", "20-minute Part B + Part C set — exam pace.", "/reading"),
        },
        "writing" => new[]
        {
            new Template("Writing planning skill", "Sketch a referral letter outline in 5 minutes — no full draft.", "/writing"),
            new Template("Linker practice", "Rewrite three sentences using exam-grade linkers.", "/writing"),
            new Template("Letter structure check", "Audit a past letter against the rubric headings.", "/writing"),
        },
        "speaking" => new[]
        {
            new Template("Speaking warm-up roleplay", "5-minute scenario focused on your weakest indicator.", "/speaking"),
            new Template("Recalls audio drill", "Hear British clinical pronunciation for common patient phrases and high-risk terms.", "/recalls/words"),
            new Template("Fluency loop", "60-second 'patient explanation' loops, 3 reps.", "/speaking"),
        },
        _ => new[]
        {
            new Template("Targeted practice", "Short targeted practice block on your weakest subtest.", "/dashboard"),
        },
    };

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
