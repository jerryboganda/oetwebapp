using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Writing;

public sealed record WritingCanonRuleView(
    string Id,
    string Category,
    IReadOnlyList<string> AppliesToLetterTypes,
    IReadOnlyList<string> AppliesToProfessions,
    string Severity,
    string RuleText,
    IReadOnlyList<string> CorrectExamples,
    IReadOnlyList<string> IncorrectExamples,
    string DetectionType,
    string DetectionConfigJson,
    Guid? LessonId,
    int Version,
    bool Active);

public sealed record WritingCanonRulePrecisionStat(string RuleId, int TotalFlags, int Disputed, int DisputedUpheld, double PrecisionPercent);

public interface IWritingCanonService
{
    Task<IReadOnlyList<WritingCanonRuleView>> ListAsync(string userId, string? letterType, string? profession, string? severity, CancellationToken ct);
    Task<WritingCanonRuleView?> GetAsync(string userId, string ruleId, CancellationToken ct);
    Task<WritingCanonRuleView> CreateOrUpdateAsync(string userId, WritingCanonRuleView rule, CancellationToken ct);
    Task<WritingCanonRuleView> SetActiveAsync(string userId, string ruleId, bool active, CancellationToken ct);
    Task<WritingCanonRulePrecisionStat> GetPrecisionAsync(string userId, string ruleId, CancellationToken ct);
    Task<WritingCanonViolation> DisputeAsync(string userId, Guid violationId, string reason, CancellationToken ct);
    Task<WritingCanonViolation> ResolveDisputeAsync(string adminId, Guid violationId, string resolution, string? note, CancellationToken ct);

    // ── V2 endpoint contract adapters ────────────────────────────────────────
    Task<WritingCanonRuleListResponseV2> ListCanonRulesV2Async(string userId, string? search, string? severity, string? category, CancellationToken ct);
    Task<WritingCanonRuleResponseV2?> GetCanonRuleAsync(string userId, string ruleId, CancellationToken ct);
    Task<WritingCanonViolationListResponse> GetMyViolationsForRuleAsync(string userId, string ruleId, CancellationToken ct);
    Task<WritingCanonViolationResponse?> DisputeViolationAsync(string userId, Guid submissionId, WritingDisputeViolationRequest request, CancellationToken ct);
    Task<WritingCanonRuleListResponseV2> AdminListCanonRulesAsync(string adminUserId, string? search, string? severity, string? category, CancellationToken ct);
    Task<WritingCanonRuleResponseV2> AdminCreateCanonRuleAsync(string adminUserId, WritingCanonRuleUpsertRequest request, CancellationToken ct);
    Task<WritingCanonRuleResponseV2?> AdminGetCanonRuleAsync(string adminUserId, string ruleId, CancellationToken ct);
    Task<WritingCanonRuleResponseV2?> AdminUpdateCanonRuleAsync(string adminUserId, string ruleId, WritingCanonRuleUpsertRequest request, CancellationToken ct);
    Task<bool> AdminDeleteCanonRuleAsync(string adminUserId, string ruleId, CancellationToken ct);
}

public sealed class WritingCanonService(LearnerDbContext db, TimeProvider clock) : IWritingCanonService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<WritingCanonRuleView>> ListAsync(string userId, string? letterType, string? profession, string? severity, CancellationToken ct)
    {
        _ = userId;
        var rules = await db.WritingCanonRules.AsNoTracking().Where(r => r.Active).ToListAsync(ct);
        IEnumerable<WritingCanonRule> filtered = rules;
        if (!string.IsNullOrWhiteSpace(letterType))
        {
            filtered = filtered.Where(r => AppliesTo(r.AppliesToLetterTypesJson, letterType!));
        }
        if (!string.IsNullOrWhiteSpace(profession))
        {
            filtered = filtered.Where(r => AppliesTo(r.AppliesToProfessionsJson, profession!));
        }
        if (!string.IsNullOrWhiteSpace(severity))
        {
            filtered = filtered.Where(r => string.Equals(r.Severity, severity, StringComparison.OrdinalIgnoreCase));
        }
        return filtered.OrderBy(r => r.Id).Select(ToView).ToList();
    }

    public async Task<WritingCanonRuleView?> GetAsync(string userId, string ruleId, CancellationToken ct)
    {
        _ = userId;
        var row = await db.WritingCanonRules.AsNoTracking().FirstOrDefaultAsync(r => r.Id == ruleId, ct);
        return row is null ? null : ToView(row);
    }

    public async Task<WritingCanonRuleView> CreateOrUpdateAsync(string userId, WritingCanonRuleView rule, CancellationToken ct)
    {
        _ = userId;
        ArgumentNullException.ThrowIfNull(rule);
        var now = clock.GetUtcNow();
        var entity = await db.WritingCanonRules.FirstOrDefaultAsync(r => r.Id == rule.Id, ct);
        if (entity is null)
        {
            entity = new WritingCanonRule
            {
                Id = rule.Id,
                CreatedAt = now,
                Version = 1,
            };
            db.WritingCanonRules.Add(entity);
        }
        else
        {
            // Auto-bump version on any field change (cheap structural check).
            entity.Version += 1;
        }
        entity.Category = rule.Category;
        entity.AppliesToLetterTypesJson = JsonSerializer.Serialize(rule.AppliesToLetterTypes ?? Array.Empty<string>(), JsonOptions);
        entity.AppliesToProfessionsJson = JsonSerializer.Serialize(rule.AppliesToProfessions ?? Array.Empty<string>(), JsonOptions);
        entity.Severity = string.IsNullOrWhiteSpace(rule.Severity) ? "medium" : rule.Severity;
        entity.RuleText = rule.RuleText;
        entity.CorrectExamplesJson = JsonSerializer.Serialize(rule.CorrectExamples ?? Array.Empty<string>(), JsonOptions);
        entity.IncorrectExamplesJson = JsonSerializer.Serialize(rule.IncorrectExamples ?? Array.Empty<string>(), JsonOptions);
        entity.DetectionType = string.IsNullOrWhiteSpace(rule.DetectionType) ? "regex" : rule.DetectionType;
        entity.DetectionConfigJson = string.IsNullOrWhiteSpace(rule.DetectionConfigJson) ? "{}" : rule.DetectionConfigJson;
        entity.LessonId = rule.LessonId;
        entity.Active = rule.Active;
        entity.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        return ToView(entity);
    }

    public async Task<WritingCanonRuleView> SetActiveAsync(string userId, string ruleId, bool active, CancellationToken ct)
    {
        _ = userId;
        var entity = await db.WritingCanonRules.FirstOrDefaultAsync(r => r.Id == ruleId, ct)
            ?? throw ApiException.NotFound("writing_canon_rule_not_found", "Canon rule was not found.");
        entity.Active = active;
        entity.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return ToView(entity);
    }

    public async Task<WritingCanonRulePrecisionStat> GetPrecisionAsync(string userId, string ruleId, CancellationToken ct)
    {
        _ = userId;
        var flags = await db.WritingCanonViolations.AsNoTracking().CountAsync(v => v.RuleId == ruleId, ct);
        var disputed = await db.WritingCanonViolations.AsNoTracking().CountAsync(v => v.RuleId == ruleId && v.Disputed, ct);
        var upheld = await db.WritingCanonViolations.AsNoTracking()
            .CountAsync(v => v.RuleId == ruleId && v.Disputed && v.DisputeResolution == "upheld", ct);
        var precision = flags == 0 ? 100.0 : Math.Round(100.0 * (flags - (disputed - upheld)) / flags, 2);
        return new WritingCanonRulePrecisionStat(ruleId, flags, disputed, upheld, precision);
    }

    public async Task<WritingCanonViolation> DisputeAsync(string userId, Guid violationId, string reason, CancellationToken ct)
    {
        var v = await db.WritingCanonViolations.FirstOrDefaultAsync(x => x.Id == violationId, ct)
            ?? throw ApiException.NotFound("writing_canon_violation_not_found", "Canon violation was not found.");
        var submission = await db.WritingSubmissions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == v.SubmissionId, ct);
        if (submission is null || !string.Equals(submission.UserId, userId, StringComparison.Ordinal))
        {
            throw ApiException.Forbidden("writing_canon_violation_forbidden", "Canon violation belongs to another learner.");
        }
        v.Disputed = true;
        v.DisputeResolution = "pending:" + (reason ?? string.Empty);
        await db.SaveChangesAsync(ct);
        return v;
    }

    public async Task<WritingCanonViolation> ResolveDisputeAsync(string adminId, Guid violationId, string resolution, string? note, CancellationToken ct)
    {
        _ = adminId;
        var v = await db.WritingCanonViolations.FirstOrDefaultAsync(x => x.Id == violationId, ct)
            ?? throw ApiException.NotFound("writing_canon_violation_not_found", "Canon violation was not found.");
        var normalized = (resolution ?? string.Empty).ToLowerInvariant() switch
        {
            "upheld" => "upheld",
            "rejected" => "rejected",
            _ => throw ApiException.Validation("writing_canon_resolution_invalid", "Dispute resolution must be 'upheld' or 'rejected'."),
        };
        v.DisputeResolution = string.IsNullOrWhiteSpace(note) ? normalized : $"{normalized}:{note}";
        await db.SaveChangesAsync(ct);
        return v;
    }

    private static WritingCanonRuleView ToView(WritingCanonRule row)
    {
        return new WritingCanonRuleView(
            row.Id,
            row.Category,
            SafeDeserializeStringList(row.AppliesToLetterTypesJson),
            SafeDeserializeStringList(row.AppliesToProfessionsJson),
            row.Severity,
            row.RuleText,
            SafeDeserializeStringList(row.CorrectExamplesJson),
            SafeDeserializeStringList(row.IncorrectExamplesJson),
            row.DetectionType,
            row.DetectionConfigJson,
            row.LessonId,
            row.Version,
            row.Active);
    }

    private static IReadOnlyList<string> SafeDeserializeStringList(string json)
    {
        try { return JsonSerializer.Deserialize<List<string>>(json, JsonOptions) ?? []; }
        catch (JsonException) { return []; }
    }

    private static bool AppliesTo(string json, string value)
    {
        var list = SafeDeserializeStringList(json);
        if (list.Count == 0) return true;
        if (list.Any(v => string.Equals(v, "all", StringComparison.OrdinalIgnoreCase))) return true;
        return list.Any(v => string.Equals(v, value, StringComparison.OrdinalIgnoreCase));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // V2 endpoint adapters
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<WritingCanonRuleListResponseV2> ListCanonRulesV2Async(string userId, string? search, string? severity, string? category, CancellationToken ct)
    {
        _ = userId;
        var rules = await db.WritingCanonRules.AsNoTracking().Where(r => r.Active).ToListAsync(ct);
        IEnumerable<WritingCanonRule> filtered = rules;
        if (!string.IsNullOrWhiteSpace(severity)) filtered = filtered.Where(r => string.Equals(r.Severity, severity, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(category)) filtered = filtered.Where(r => string.Equals(r.Category, category, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(search)) filtered = filtered.Where(r => r.RuleText.Contains(search!, StringComparison.OrdinalIgnoreCase) || r.Id.Contains(search!, StringComparison.OrdinalIgnoreCase));
        var items = filtered.OrderBy(r => r.Id).Select(r => WritingV2ResponseMapper.ToResponse(ToView(r))).ToList();
        return new WritingCanonRuleListResponseV2(items);
    }

    public async Task<WritingCanonRuleResponseV2?> GetCanonRuleAsync(string userId, string ruleId, CancellationToken ct)
    {
        var view = await GetAsync(userId, ruleId, ct);
        return view is null ? null : WritingV2ResponseMapper.ToResponse(view);
    }

    public async Task<WritingCanonViolationListResponse> GetMyViolationsForRuleAsync(string userId, string ruleId, CancellationToken ct)
    {
        var rows = await db.WritingCanonViolations.AsNoTracking()
            .Join(db.WritingSubmissions.AsNoTracking(), v => v.SubmissionId, s => s.Id, (v, s) => new { v, s })
            .Where(x => x.v.RuleId == ruleId && x.s.UserId == userId)
            .OrderByDescending(x => x.v.DetectedAt)
            .Take(200)
            .Select(x => x.v)
            .ToListAsync(ct);
        var rule = await db.WritingCanonRules.AsNoTracking().FirstOrDefaultAsync(r => r.Id == ruleId, ct);
        var ruleText = rule?.RuleText ?? string.Empty;
        return new WritingCanonViolationListResponse(rows.Select(v => WritingV2ResponseMapper.ToResponse(v, ruleText)).ToList());
    }

    public async Task<WritingCanonViolationResponse?> DisputeViolationAsync(string userId, Guid submissionId, WritingDisputeViolationRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var submission = await db.WritingSubmissions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == submissionId && s.UserId == userId, ct);
        if (submission is null) return null;
        var violation = await db.WritingCanonViolations.FirstOrDefaultAsync(v => v.Id == request.ViolationId && v.SubmissionId == submissionId, ct);
        if (violation is null) return null;
        violation.Disputed = true;
        violation.DisputeResolution = $"pending:{request.Reason}";
        await db.SaveChangesAsync(ct);
        var rule = await db.WritingCanonRules.AsNoTracking().FirstOrDefaultAsync(r => r.Id == violation.RuleId, ct);
        return WritingV2ResponseMapper.ToResponse(violation, rule?.RuleText ?? string.Empty);
    }

    public async Task<WritingCanonRuleListResponseV2> AdminListCanonRulesAsync(string adminUserId, string? search, string? severity, string? category, CancellationToken ct)
    {
        _ = adminUserId;
        var rules = await db.WritingCanonRules.AsNoTracking().ToListAsync(ct);
        IEnumerable<WritingCanonRule> filtered = rules;
        if (!string.IsNullOrWhiteSpace(severity)) filtered = filtered.Where(r => string.Equals(r.Severity, severity, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(category)) filtered = filtered.Where(r => string.Equals(r.Category, category, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(search)) filtered = filtered.Where(r => r.RuleText.Contains(search!, StringComparison.OrdinalIgnoreCase) || r.Id.Contains(search!, StringComparison.OrdinalIgnoreCase));
        var items = filtered.OrderBy(r => r.Id).Select(r => WritingV2ResponseMapper.ToResponse(ToView(r))).ToList();
        return new WritingCanonRuleListResponseV2(items);
    }

    public async Task<WritingCanonRuleResponseV2> AdminCreateCanonRuleAsync(string adminUserId, WritingCanonRuleUpsertRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var view = ToCanonView(request);
        var saved = await CreateOrUpdateAsync(adminUserId, view, ct);
        return WritingV2ResponseMapper.ToResponse(saved);
    }

    public async Task<WritingCanonRuleResponseV2?> AdminGetCanonRuleAsync(string adminUserId, string ruleId, CancellationToken ct)
    {
        var view = await GetAsync(adminUserId, ruleId, ct);
        return view is null ? null : WritingV2ResponseMapper.ToResponse(view);
    }

    public async Task<WritingCanonRuleResponseV2?> AdminUpdateCanonRuleAsync(string adminUserId, string ruleId, WritingCanonRuleUpsertRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var existing = await db.WritingCanonRules.AsNoTracking().AnyAsync(r => r.Id == ruleId, ct);
        if (!existing) return null;
        var view = ToCanonView(request) with { Id = ruleId };
        var saved = await CreateOrUpdateAsync(adminUserId, view, ct);
        return WritingV2ResponseMapper.ToResponse(saved);
    }

    public async Task<bool> AdminDeleteCanonRuleAsync(string adminUserId, string ruleId, CancellationToken ct)
    {
        _ = adminUserId;
        var rule = await db.WritingCanonRules.FirstOrDefaultAsync(r => r.Id == ruleId, ct);
        if (rule is null) return false;
        db.WritingCanonRules.Remove(rule);
        await db.SaveChangesAsync(ct);
        return true;
    }

    private static WritingCanonRuleView ToCanonView(WritingCanonRuleUpsertRequest req)
        => new(
            Id: req.Id,
            Category: req.Category,
            AppliesToLetterTypes: req.AppliesToLetterTypes ?? Array.Empty<string>(),
            AppliesToProfessions: req.AppliesToProfessions ?? Array.Empty<string>(),
            Severity: req.Severity,
            RuleText: req.RuleText,
            CorrectExamples: req.CorrectExamples ?? Array.Empty<string>(),
            IncorrectExamples: req.IncorrectExamples ?? Array.Empty<string>(),
            DetectionType: req.DetectionType,
            DetectionConfigJson: req.DetectionConfigJson ?? "{}",
            LessonId: string.IsNullOrWhiteSpace(req.LessonId) ? null : (Guid.TryParse(req.LessonId, out var lid) ? lid : (Guid?)null),
            Version: int.TryParse(req.Version, out var v) ? v : 1,
            Active: req.Active ?? true);
}
