using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Services.Remediation;

/// <summary>
/// Read API for the learner-facing /v1/remediation surface. Returns the
/// learner's current 7-day plan, the underlying RemediationTask rows, and
/// the canonical rulebook context (so the UI can show why each task is
/// recommended).
///
/// The audit flagged that the Remediation module had a domain entity
/// (RemediationTask) but NO backend API and only the medicine rulebook.
/// Phase 7a authored the remaining 5 profession rulebooks; this service +
/// the matching endpoints close the API gap.
/// </summary>
public interface IRemediationApiService
{
    Task<RemediationPlanResponse> GetActivePlanAsync(string userId, ExamProfession profession, CancellationToken ct);
    Task<bool> MarkTaskAsync(string userId, string taskId, string status, CancellationToken ct);
    RemediationRulebookContext GetRulebookContext(ExamProfession profession);
}

public sealed class RemediationApiService(LearnerDbContext db, IRulebookLoader rulebooks) : IRemediationApiService
{
    private readonly LearnerDbContext _db = db;
    private readonly IRulebookLoader _rulebooks = rulebooks;

    public async Task<RemediationPlanResponse> GetActivePlanAsync(string userId, ExamProfession profession, CancellationToken ct)
    {
        var tasks = await _db.Set<RemediationTask>()
            .Where(t => t.UserId == userId)
            .OrderBy(t => t.DayIndex)
            .ToListAsync(ct);

        var grouped = tasks
            .GroupBy(t => t.DayIndex)
            .OrderBy(g => g.Key)
            .Select(g => new RemediationDay(g.Key, g.Select(MapTask).ToArray()))
            .ToArray();

        return new RemediationPlanResponse(
            UserId: userId,
            Profession: profession.ToString().ToLowerInvariant(),
            Days: grouped,
            Rulebook: GetRulebookContext(profession));
    }

    public async Task<bool> MarkTaskAsync(string userId, string taskId, string status, CancellationToken ct)
    {
        var task = await _db.Set<RemediationTask>()
            .FirstOrDefaultAsync(t => t.Id == taskId && t.UserId == userId, ct);
        if (task is null) return false;
        if (status != RemediationTaskStatuses.Completed && status != RemediationTaskStatuses.Skipped && status != RemediationTaskStatuses.Pending)
        {
            throw new ArgumentException($"Invalid remediation status '{status}'. Expected pending | completed | skipped.");
        }
        task.Status = status;
        task.CompletedAt = status == RemediationTaskStatuses.Completed ? DateTimeOffset.UtcNow : null;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public RemediationRulebookContext GetRulebookContext(ExamProfession profession)
    {
        OetRulebook book;
        try
        {
            book = _rulebooks.Load(RuleKind.Remediation, profession);
        }
        catch (RulebookNotFoundException)
        {
            // Fallback to medicine — sentinel ensures the response always
            // includes rulebook framing, even if a future profession is
            // wired before its rulebook is authored.
            book = _rulebooks.Load(RuleKind.Remediation, ExamProfession.Medicine);
        }
        return new RemediationRulebookContext(
            Version: book.Version,
            CriticalRuleIds: book.Rules.Where(r => r.Severity == RuleSeverity.Critical).Select(r => r.Id).ToArray(),
            Tone: book.Rules.Where(r => r.Section == "RM01").Select(r => new RemediationRuleSummary(r.Id, r.Title, r.Body)).ToArray());
    }

    private static RemediationTaskDto MapTask(RemediationTask t) => new(
        Id: t.Id,
        SubtestCode: t.SubtestCode,
        WeaknessTag: t.WeaknessTag,
        Title: t.Title,
        Description: t.Description,
        RouteHref: t.RouteHref,
        Status: t.Status,
        CompletedAt: t.CompletedAt);
}

public sealed record RemediationPlanResponse(
    string UserId,
    string Profession,
    RemediationDay[] Days,
    RemediationRulebookContext Rulebook);

public sealed record RemediationDay(int DayIndex, RemediationTaskDto[] Tasks);

public sealed record RemediationTaskDto(
    string Id,
    string SubtestCode,
    string WeaknessTag,
    string Title,
    string Description,
    string? RouteHref,
    string Status,
    DateTimeOffset? CompletedAt);

public sealed record RemediationRulebookContext(
    string Version,
    string[] CriticalRuleIds,
    RemediationRuleSummary[] Tone);

public sealed record RemediationRuleSummary(string RuleId, string Title, string Body);
