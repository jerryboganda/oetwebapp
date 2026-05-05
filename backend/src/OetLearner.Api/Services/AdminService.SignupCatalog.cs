using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services;

public partial class AdminService
{
    public async Task<object> GetSignupCatalogAdminAsync(CancellationToken ct)
    {
        var examTypes = await db.SignupExamTypeCatalog
            .AsNoTracking()
            .OrderBy(item => item.SortOrder)
            .ThenBy(item => item.Label)
            .Select(item => new
            {
                item.Id,
                item.Code,
                item.Label,
                item.Description,
                item.SortOrder,
                item.IsActive
            })
            .ToListAsync(ct);

        var professions = await db.SignupProfessionCatalog
            .AsNoTracking()
            .OrderBy(item => item.SortOrder)
            .ThenBy(item => item.Label)
            .ToListAsync(ct);

        return new
        {
            examTypes,
            professions = professions.Select(item => new
            {
                item.Id,
                item.Label,
                item.Description,
                item.SortOrder,
                item.IsActive,
                ExamTypeIds = ReadStringList(item.ExamTypeIdsJson),
                CountryTargets = ReadStringList(item.CountryTargetsJson)
            })
        };
    }

    public async Task<object> CreateSignupExamTypeAsync(string adminId, string adminName, AdminSignupExamTypeCatalogRequest request, CancellationToken ct)
    {
        var id = NormalizeCatalogId(request.Id);
        var code = NormalizeCode(request.Code);
        if (await db.SignupExamTypeCatalog.AnyAsync(item => item.Id == id || item.Code == code, ct))
        {
            throw ApiException.Validation("signup_exam_type_duplicate", "An exam type with this ID or code already exists.");
        }

        var item = new SignupExamTypeCatalog
        {
            Id = id,
            Code = code,
            Label = NormalizeRequired(request.Label, "Exam type label"),
            Description = request.Description?.Trim() ?? string.Empty,
            SortOrder = request.SortOrder ?? await NextExamTypeSortOrderAsync(ct),
            IsActive = request.IsActive ?? true
        };

        db.SignupExamTypeCatalog.Add(item);
        await db.SaveChangesAsync(ct);
        await LogAuditAsync(adminId, adminName, "Created", "SignupExamType", item.Id, $"Created signup exam type: {item.Label}", ct);
        return item;
    }

    public async Task<object> UpdateSignupExamTypeAsync(string adminId, string adminName, string id, AdminSignupExamTypeCatalogRequest request, CancellationToken ct)
    {
        var item = await db.SignupExamTypeCatalog.FirstOrDefaultAsync(row => row.Id == id, ct)
            ?? throw ApiException.NotFound("signup_exam_type_not_found", "Signup exam type not found.");

        var requestedId = NormalizeCatalogId(request.Id);
        var requestedCode = NormalizeCode(request.Code);
        if (requestedId != item.Id)
        {
            throw ApiException.Validation("signup_exam_type_id_immutable", "Exam type ID cannot be changed after creation.");
        }
        if (!string.Equals(requestedCode, item.Code, StringComparison.OrdinalIgnoreCase)
            && await db.SignupExamTypeCatalog.AnyAsync(row => row.Code == requestedCode, ct))
        {
            throw ApiException.Validation("signup_exam_type_duplicate", "An exam type with this code already exists.");
        }

        item.Code = requestedCode;
        item.Label = NormalizeRequired(request.Label, "Exam type label");
        item.Description = request.Description?.Trim() ?? string.Empty;
        item.SortOrder = request.SortOrder ?? item.SortOrder;
        item.IsActive = request.IsActive ?? item.IsActive;

        await db.SaveChangesAsync(ct);
        await LogAuditAsync(adminId, adminName, "Updated", "SignupExamType", item.Id, $"Updated signup exam type: {item.Label}", ct);
        return item;
    }

    public async Task<object> SetSignupExamTypeStatusAsync(string adminId, string adminName, string id, bool isActive, CancellationToken ct)
    {
        var item = await db.SignupExamTypeCatalog.FirstOrDefaultAsync(row => row.Id == id, ct)
            ?? throw ApiException.NotFound("signup_exam_type_not_found", "Signup exam type not found.");
        item.IsActive = isActive;
        await db.SaveChangesAsync(ct);
        await LogAuditAsync(adminId, adminName, isActive ? "Activated" : "Archived", "SignupExamType", item.Id, $"{(isActive ? "Activated" : "Archived")} signup exam type: {item.Label}", ct);
        return new { item.Id, item.IsActive };
    }

    public async Task<object> CreateSignupProfessionAsync(string adminId, string adminName, AdminSignupProfessionCatalogRequest request, CancellationToken ct)
    {
        var id = NormalizeCatalogId(request.Id);
        if (await db.SignupProfessionCatalog.AnyAsync(item => item.Id == id, ct))
        {
            throw ApiException.Validation("signup_profession_duplicate", "A profession with this ID already exists.");
        }

        var examTypeIds = await NormalizeExamTypeIdsAsync(request.ExamTypeIds, ct);
        var item = new SignupProfessionCatalog
        {
            Id = id,
            Label = NormalizeRequired(request.Label, "Profession label"),
            Description = request.Description?.Trim() ?? string.Empty,
            ExamTypeIdsJson = JsonSerializer.Serialize(examTypeIds),
            CountryTargetsJson = JsonSerializer.Serialize(NormalizeCountryTargets(request.CountryTargets)),
            SortOrder = request.SortOrder ?? await NextProfessionSortOrderAsync(ct),
            IsActive = request.IsActive ?? true
        };

        db.SignupProfessionCatalog.Add(item);
        await db.SaveChangesAsync(ct);
        await LogAuditAsync(adminId, adminName, "Created", "SignupProfession", item.Id, $"Created signup profession: {item.Label}", ct);
        return item;
    }

    public async Task<object> UpdateSignupProfessionAsync(string adminId, string adminName, string id, AdminSignupProfessionCatalogRequest request, CancellationToken ct)
    {
        var item = await db.SignupProfessionCatalog.FirstOrDefaultAsync(row => row.Id == id, ct)
            ?? throw ApiException.NotFound("signup_profession_not_found", "Signup profession not found.");

        var requestedId = NormalizeCatalogId(request.Id);
        if (requestedId != item.Id)
        {
            throw ApiException.Validation("signup_profession_id_immutable", "Profession ID cannot be changed after creation.");
        }

        item.Label = NormalizeRequired(request.Label, "Profession label");
        item.Description = request.Description?.Trim() ?? string.Empty;
        item.ExamTypeIdsJson = JsonSerializer.Serialize(await NormalizeExamTypeIdsAsync(request.ExamTypeIds, ct));
        item.CountryTargetsJson = JsonSerializer.Serialize(NormalizeCountryTargets(request.CountryTargets));
        item.SortOrder = request.SortOrder ?? item.SortOrder;
        item.IsActive = request.IsActive ?? item.IsActive;

        await db.SaveChangesAsync(ct);
        await LogAuditAsync(adminId, adminName, "Updated", "SignupProfession", item.Id, $"Updated signup profession: {item.Label}", ct);
        return item;
    }

    public async Task<object> SetSignupProfessionStatusAsync(string adminId, string adminName, string id, bool isActive, CancellationToken ct)
    {
        var item = await db.SignupProfessionCatalog.FirstOrDefaultAsync(row => row.Id == id, ct)
            ?? throw ApiException.NotFound("signup_profession_not_found", "Signup profession not found.");
        item.IsActive = isActive;
        await db.SaveChangesAsync(ct);
        await LogAuditAsync(adminId, adminName, isActive ? "Activated" : "Archived", "SignupProfession", item.Id, $"{(isActive ? "Activated" : "Archived")} signup profession: {item.Label}", ct);
        return new { item.Id, item.IsActive };
    }

    private static IReadOnlyList<string> ReadStringList(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<string>();
        try
        {
            return JsonSerializer.Deserialize<string[]>(json) ?? Array.Empty<string>();
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }

    private static string NormalizeCatalogId(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant() ?? string.Empty;
        if (normalized.Length is < 2 or > 32 || normalized.Any(ch => !char.IsLetterOrDigit(ch) && ch != '-'))
        {
            throw ApiException.Validation("invalid_catalog_id", "Use a 2-32 character lowercase ID containing letters, numbers, or hyphens.");
        }
        return normalized;
    }

    private static string NormalizeCode(string? value)
    {
        var normalized = value?.Trim().ToUpperInvariant() ?? string.Empty;
        if (normalized.Length is < 2 or > 16 || normalized.Any(ch => !char.IsLetterOrDigit(ch) && ch != '-'))
        {
            throw ApiException.Validation("invalid_catalog_code", "Use a 2-16 character code containing letters, numbers, or hyphens.");
        }
        return normalized;
    }

    private static string NormalizeRequired(string? value, string fieldName)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length == 0) throw ApiException.Validation("required_field", $"{fieldName} is required.");
        return normalized;
    }

    private async Task<IReadOnlyList<string>> NormalizeExamTypeIdsAsync(IReadOnlyList<string>? values, CancellationToken ct)
    {
        var ids = (values ?? Array.Empty<string>()).Select(NormalizeCatalogId).Distinct().ToArray();
        if (ids.Length == 0) throw ApiException.Validation("exam_types_required", "Select at least one exam type for this profession.");
        var existing = await db.SignupExamTypeCatalog.Where(item => ids.Contains(item.Id)).Select(item => item.Id).ToListAsync(ct);
        var missing = ids.Except(existing).ToArray();
        if (missing.Length > 0) throw ApiException.Validation("unknown_exam_type", $"Unknown exam type: {missing[0]}");
        return ids;
    }

    private static IReadOnlyList<string> NormalizeCountryTargets(IReadOnlyList<string>? values)
    {
        var allowed = TargetCountryOptions.All.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var normalized = (values ?? TargetCountryOptions.All).Select(item => item.Trim()).Where(item => item.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        foreach (var item in normalized)
        {
            if (!allowed.Contains(item)) throw ApiException.Validation("unknown_target_country", $"Unknown target country: {item}");
        }
        return normalized.Length == 0 ? TargetCountryOptions.All : normalized;
    }

    private async Task<int> NextExamTypeSortOrderAsync(CancellationToken ct) =>
        await db.SignupExamTypeCatalog.AnyAsync(ct) ? await db.SignupExamTypeCatalog.MaxAsync(item => item.SortOrder, ct) + 1 : 1;

    private async Task<int> NextProfessionSortOrderAsync(CancellationToken ct) =>
        await db.SignupProfessionCatalog.AnyAsync(ct) ? await db.SignupProfessionCatalog.MaxAsync(item => item.SortOrder, ct) + 1 : 1;
}