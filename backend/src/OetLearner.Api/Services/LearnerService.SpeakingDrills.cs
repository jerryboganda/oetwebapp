using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services;

// Wave 6 of docs/SPEAKING-MODULE-PLAN.md - speaking drills bank.
//
// Drills are stored as plain ContentItem rows with
// `ContentType = "speaking_drill"`. The drill kind is encoded in
// `ScenarioType` (phrasing | intonation | pronunciation | vocabulary |
// chunking | empathy). No schema change is required — the plan
// explicitly chose this lightweight encoding to keep authoring cheap
// for content ops.
public partial class LearnerService
{
    private static readonly string[] SpeakingDrillKinds = new[]
    {
        "phrasing", "intonation", "pronunciation", "vocabulary",
        "chunking", "empathy",
    };

    public async Task<object> ListSpeakingDrillsAsync(
        string userId,
        string? kind,
        string? professionCode,
        string? criterionCode,
        CancellationToken ct)
    {
        var query = db.ContentItems.AsNoTracking()
            .Where(c => c.ContentType == "speaking_drill"
                        && c.SubtestCode == "speaking"
                        && c.Status == ContentStatus.Published);

        if (!string.IsNullOrWhiteSpace(kind))
        {
            // Validate against the closed list — silently ignore bogus
            // values so the front-end can pass user input safely.
            var normalisedKind = kind.Trim().ToLowerInvariant();
            if (Array.IndexOf(SpeakingDrillKinds, normalisedKind) >= 0)
            {
                query = query.Where(c => c.ScenarioType == normalisedKind);
            }
        }

        if (!string.IsNullOrWhiteSpace(professionCode))
        {
            // ProfessionId == null means "any profession" — always
            // include those alongside the explicit profession match.
            var prof = professionCode.Trim();
            query = query.Where(c => c.ProfessionId == prof || c.ProfessionId == null);
        }

        var rows = await query
            .OrderBy(c => c.ScenarioType)
            .ThenBy(c => c.Difficulty == "easy" ? 0 : c.Difficulty == "medium" ? 1 : 2)
            .ThenBy(c => c.Title)
            .Take(200)
            .ToListAsync(ct);

        var contentIds = rows.Select(r => r.Id).ToArray();
        var drillRows = await db.SpeakingDrillItems.AsNoTracking()
            .Where(d => contentIds.Contains(d.ContentItemId))
            .Select(d => new { d.Id, d.ContentItemId, d.TargetCriteriaJson })
            .ToListAsync(ct);
        var drillByContentId = drillRows.ToDictionary(d => d.ContentItemId, d => d, StringComparer.Ordinal);

        var completed = await db.Attempts.AsNoTracking()
            .Where(a => a.UserId == userId && a.State == AttemptState.Completed)
            .Select(a => a.ContentId)
            .Distinct()
            .ToListAsync(ct);
        var completedLegacyContentSet = completed.ToHashSet(StringComparer.Ordinal);

        var drillIds = drillRows.Select(d => d.Id).ToArray();
        var drillAttempts = await db.SpeakingDrillAttempts.AsNoTracking()
            .Where(a => a.UserId == userId && drillIds.Contains(a.DrillItemId))
            .Select(a => new { a.DrillItemId, a.CompletedAt, a.Score })
            .ToListAsync(ct);
        var attemptStatsByDrillId = drillAttempts
            .GroupBy(a => a.DrillItemId)
            .ToDictionary(
                g => g.Key,
                g => new
                {
                    Completed = g.Any(a => a.CompletedAt != null),
                    BestScore = g.Where(a => a.Score.HasValue).Select(a => (int?)a.Score!.Value).Max(),
                },
                StringComparer.Ordinal);

        var criterionFilter = string.IsNullOrWhiteSpace(criterionCode)
            ? null
            : criterionCode.Trim().ToLowerInvariant();

        var items = rows
            .Select(row =>
            {
                drillByContentId.TryGetValue(row.Id, out var drill);
                var drillId = drill?.Id ?? row.Id;
                var criteriaFocus = ParseCriteriaFocus(row.CriteriaFocusJson);
                attemptStatsByDrillId.TryGetValue(drillId, out var attemptStats);
                var isCompleted = completedLegacyContentSet.Contains(row.Id) || attemptStats?.Completed == true;

                return new
                {
                    id = row.Id,
                    drillId,
                    title = row.Title,
                    kind = row.ScenarioType ?? "drill",
                    drillKind = row.ScenarioType ?? "drill",
                    difficulty = row.Difficulty,
                    estimatedDurationMinutes = row.EstimatedDurationMinutes,
                    professionCode = row.ProfessionId,
                    criteriaFocus,
                    targetCriteria = drill is null ? criteriaFocus : ParseCriteriaFocus(drill.TargetCriteriaJson),
                    instructionText = ParseInstructionText(row.DetailJson) ?? row.CaseNotes ?? row.Title,
                    caseNotes = row.CaseNotes,
                    completed = isCompleted,
                    hasAttempted = isCompleted || attemptStats is not null,
                    bestScore = attemptStats?.BestScore,
                };
            })
            .Where(d => criterionFilter is null
                        || d.criteriaFocus.Contains(criterionFilter, StringComparer.OrdinalIgnoreCase))
            .ToList();

        return new
        {
            kinds = SpeakingDrillKinds,
            totalCount = items.Count,
            completedCount = items.Count(d => d.completed),
            items,
        };
    }

    private static IReadOnlyList<string> ParseCriteriaFocus(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<string>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return Array.Empty<string>();
            var list = new List<string>();
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (el.ValueKind == JsonValueKind.String)
                {
                    var s = el.GetString();
                    if (!string.IsNullOrWhiteSpace(s)) list.Add(s);
                }
            }
            return list;
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }

    private static string? ParseInstructionText(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
            if (doc.RootElement.TryGetProperty("instructionText", out var instruction)
                && instruction.ValueKind == JsonValueKind.String)
            {
                return instruction.GetString();
            }
            if (doc.RootElement.TryGetProperty("focus", out var focus)
                && focus.ValueKind == JsonValueKind.String)
            {
                return focus.GetString();
            }
            if (doc.RootElement.TryGetProperty("promptLines", out var lines)
                && lines.ValueKind == JsonValueKind.Array)
            {
                var prompts = lines.EnumerateArray()
                    .Where(x => x.ValueKind == JsonValueKind.String)
                    .Select(x => x.GetString())
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .ToArray();
                return prompts.Length == 0 ? null : string.Join("\n", prompts);
            }
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
