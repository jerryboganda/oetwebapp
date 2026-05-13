using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Endpoints;

/// <summary>
/// Audit P2-2 closure (May 2026). Read-only admin analytics over the new
/// <see cref="WritingRuleViolation"/> table populated by
/// <c>WritingEvaluationPipeline</c>.
///
/// Two queries are exposed:
///   • <c>GET /v1/admin/writing/analytics/rule-violations</c> — rolling window
///     (clamped to 7..365 days) returning aggregate counts grouped by ruleId
///     with profession breakdown, plus a top-N list of "worst offenders".
///   • <c>GET /v1/admin/writing/analytics/rule-violations/{attemptId}</c> —
///     drill-down: every violation persisted for a single attempt, ordered
///     by severity and rule id.
///
/// All responses are derived; no writes happen here. The endpoint pivots
/// strictly on (RuleId, GeneratedAt), (Profession, GeneratedAt), and
/// (AttemptId) — exactly the indexes seeded by migration
/// <c>20260513120000_AddWritingRuleViolations</c>.
/// </summary>
public static class WritingAnalyticsAdminEndpoints
{
    private const int DefaultWindowDays = 30;
    private const int MaxWindowDays = 365;
    private const int MinWindowDays = 7;
    private const int TopNRules = 12;

    public static IEndpointRouteBuilder MapWritingAnalyticsAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/v1/admin/writing/analytics")
            .RequireAuthorization("AdminOnly")
            .RequireRateLimiting("PerUser");

        admin.MapGet("/rule-violations", async (
            [FromQuery] int? days,
            [FromQuery] string? profession,
            LearnerDbContext db,
            CancellationToken ct) =>
        {
            var dto = await BuildRuleViolationDashboardAsync(db, days, profession, ct);
            return Results.Ok(dto);
        }).WithAdminRead("AdminContentRead");

        admin.MapGet("/rule-violations/{attemptId}", async (
            string attemptId,
            LearnerDbContext db,
            CancellationToken ct) =>
        {
            var dto = await BuildAttemptDrillDownAsync(db, attemptId, ct);
            return Results.Ok(dto);
        }).WithAdminRead("AdminContentRead");

        return app;
    }

    public static async Task<WritingRuleViolationDashboardDto> BuildRuleViolationDashboardAsync(
        LearnerDbContext db,
        int? days,
        string? profession,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var windowDays = Math.Clamp(days ?? DefaultWindowDays, MinWindowDays, MaxWindowDays);
        var since = now.AddDays(-windowDays);
        var normalisedProfession = string.IsNullOrWhiteSpace(profession) ? null : profession.Trim().ToLowerInvariant();

        var rows = await db.WritingRuleViolations.AsNoTracking()
            .Where(v => v.GeneratedAt >= since
                && (normalisedProfession == null || v.Profession == normalisedProfession))
            .ToListAsync(ct);

        // Distinct attempts that triggered at least one violation in this window.
        var attemptCount = rows.Select(r => r.AttemptId).Distinct(StringComparer.Ordinal).Count();
        var ruleBreakdown = rows
            .GroupBy(r => r.RuleId, StringComparer.Ordinal)
            .Select(group =>
            {
                var groupRows = group.ToList();
                var bySeverity = groupRows
                    .GroupBy(r => r.Severity, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key.ToLowerInvariant(), g => g.Count(), StringComparer.OrdinalIgnoreCase);
                var byProfession = groupRows
                    .GroupBy(r => r.Profession, StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(g => g.Count())
                    .ThenBy(g => g.Key, StringComparer.Ordinal)
                    .Select(g => new WritingProfessionCountDto(g.Key.ToLowerInvariant(), g.Count()))
                    .ToList();
                return new WritingRuleViolationGroupDto(
                    RuleId: group.Key,
                    TotalCount: groupRows.Count,
                    DistinctAttempts: groupRows.Select(r => r.AttemptId).Distinct(StringComparer.Ordinal).Count(),
                    DistinctLearners: groupRows.Select(r => r.UserId).Distinct(StringComparer.Ordinal).Count(),
                    CriticalCount: bySeverity.GetValueOrDefault("critical"),
                    MajorCount: bySeverity.GetValueOrDefault("major"),
                    MinorCount: bySeverity.GetValueOrDefault("minor"),
                    InfoCount: bySeverity.GetValueOrDefault("info"),
                    Professions: byProfession);
            })
            .OrderByDescending(g => g.TotalCount)
            .ThenBy(g => g.RuleId, StringComparer.Ordinal)
            .ToList();

        var professionBreakdown = rows
            .GroupBy(r => r.Profession, StringComparer.OrdinalIgnoreCase)
            .Select(g => new WritingProfessionCountDto(g.Key.ToLowerInvariant(), g.Count()))
            .OrderByDescending(p => p.Count)
            .ThenBy(p => p.Profession, StringComparer.Ordinal)
            .ToList();

        var letterTypeBreakdown = rows
            .GroupBy(r => r.LetterType, StringComparer.OrdinalIgnoreCase)
            .Select(g => new WritingLetterTypeCountDto(g.Key.ToLowerInvariant(), g.Count()))
            .OrderByDescending(l => l.Count)
            .ThenBy(l => l.LetterType, StringComparer.Ordinal)
            .ToList();

        var summary = new WritingRuleViolationSummaryDto(
            TotalViolations: rows.Count,
            DistinctRules: ruleBreakdown.Count,
            DistinctAttempts: attemptCount,
            DistinctLearners: rows.Select(r => r.UserId).Distinct(StringComparer.Ordinal).Count(),
            RuleEngineCount: rows.Count(r => string.Equals(r.Source, "rulebook", StringComparison.OrdinalIgnoreCase)),
            AiCount: rows.Count(r => string.Equals(r.Source, "ai", StringComparison.OrdinalIgnoreCase)));

        return new WritingRuleViolationDashboardDto(
            GeneratedAt: now,
            WindowDays: windowDays,
            ProfessionFilter: normalisedProfession,
            Summary: summary,
            TopRules: ruleBreakdown.Take(TopNRules).ToList(),
            ProfessionBreakdown: professionBreakdown,
            LetterTypeBreakdown: letterTypeBreakdown);
    }

    public static async Task<WritingAttemptViolationsDto> BuildAttemptDrillDownAsync(
        LearnerDbContext db,
        string attemptId,
        CancellationToken ct)
    {
        var rows = await db.WritingRuleViolations.AsNoTracking()
            .Where(v => v.AttemptId == attemptId)
            .ToListAsync(ct);

        if (rows.Count == 0)
        {
            return new WritingAttemptViolationsDto(
                AttemptId: attemptId,
                EvaluationId: null,
                UserId: null,
                Profession: null,
                LetterType: null,
                GeneratedAt: null,
                Items: Array.Empty<WritingRuleViolationRowDto>());
        }

        var first = rows[0];
        var ordered = rows
            .OrderBy(r => SeverityRank(r.Severity))
            .ThenBy(r => r.RuleId, StringComparer.Ordinal)
            .Select(r => new WritingRuleViolationRowDto(
                Id: r.Id,
                RuleId: r.RuleId,
                Severity: r.Severity.ToLowerInvariant(),
                Source: r.Source.ToLowerInvariant(),
                Message: r.Message,
                Quote: r.Quote,
                GeneratedAt: r.GeneratedAt))
            .ToList();

        return new WritingAttemptViolationsDto(
            AttemptId: first.AttemptId,
            EvaluationId: first.EvaluationId,
            UserId: first.UserId,
            Profession: first.Profession,
            LetterType: first.LetterType,
            GeneratedAt: rows.Max(r => r.GeneratedAt),
            Items: ordered);
    }

    private static int SeverityRank(string severity) => severity?.ToLowerInvariant() switch
    {
        "critical" => 0,
        "major" => 1,
        "minor" => 2,
        "info" => 3,
        _ => 4,
    };
}

public sealed record WritingRuleViolationDashboardDto(
    DateTimeOffset GeneratedAt,
    int WindowDays,
    string? ProfessionFilter,
    WritingRuleViolationSummaryDto Summary,
    IReadOnlyList<WritingRuleViolationGroupDto> TopRules,
    IReadOnlyList<WritingProfessionCountDto> ProfessionBreakdown,
    IReadOnlyList<WritingLetterTypeCountDto> LetterTypeBreakdown);

public sealed record WritingRuleViolationSummaryDto(
    int TotalViolations,
    int DistinctRules,
    int DistinctAttempts,
    int DistinctLearners,
    int RuleEngineCount,
    int AiCount);

public sealed record WritingRuleViolationGroupDto(
    string RuleId,
    int TotalCount,
    int DistinctAttempts,
    int DistinctLearners,
    int CriticalCount,
    int MajorCount,
    int MinorCount,
    int InfoCount,
    IReadOnlyList<WritingProfessionCountDto> Professions);

public sealed record WritingProfessionCountDto(string Profession, int Count);

public sealed record WritingLetterTypeCountDto(string LetterType, int Count);

public sealed record WritingAttemptViolationsDto(
    string AttemptId,
    string? EvaluationId,
    string? UserId,
    string? Profession,
    string? LetterType,
    DateTimeOffset? GeneratedAt,
    IReadOnlyList<WritingRuleViolationRowDto> Items);

public sealed record WritingRuleViolationRowDto(
    string Id,
    string RuleId,
    string Severity,
    string Source,
    string Message,
    string? Quote,
    DateTimeOffset GeneratedAt);
