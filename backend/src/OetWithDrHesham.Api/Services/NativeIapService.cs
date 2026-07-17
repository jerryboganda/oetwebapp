using Microsoft.EntityFrameworkCore;
using OetWithDrHesham.Api.Contracts;
using OetWithDrHesham.Api.Data;
using OetWithDrHesham.Api.Domain;

namespace OetWithDrHesham.Api.Services;

/// <summary>
/// Native IAP catalog mapping and fail-closed receipt validation plumbing.
/// This service intentionally performs no Apple/Google calls and stores no
/// provider credentials or raw receipt/token material.
/// </summary>
public sealed class NativeIapService(
    LearnerDbContext db,
    TimeProvider timeProvider)
{
    private static readonly HashSet<string> ValidPlatforms = new(StringComparer.Ordinal)
    {
        "ios",
        "android",
    };

    private static readonly HashSet<string> ValidTargetTypes = new(StringComparer.Ordinal)
    {
        "plan",
        "add_on",
        "wallet_tier",
    };

    public async Task<IReadOnlyList<NativeIapProductMappingResponse>> ListAdminMappingsAsync(string? platform, CancellationToken ct)
    {
        var normalizedPlatform = NormalizeOptionalPlatform(platform);
        var query = db.NativeIapProductMappings.AsNoTracking();
        if (normalizedPlatform is not null)
        {
            query = query.Where(x => x.Platform == normalizedPlatform);
        }

        var rows = await query
            .OrderBy(x => x.Platform)
            .ThenBy(x => x.StoreProductId)
            .ToListAsync(ct);

        return rows.Select(ToResponse).ToList();
    }

    public async Task<NativeIapProductMappingResponse> GetAdminMappingAsync(string id, CancellationToken ct)
    {
        var row = await db.NativeIapProductMappings.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, ct);

        return row is null
            ? throw ApiException.NotFound("native_iap_product_not_found", "Native IAP product mapping was not found.")
            : ToResponse(row);
    }

    public async Task<NativeIapProductMappingResponse> CreateAdminMappingAsync(
        string adminId,
        NativeIapProductMappingUpsertRequest request,
        CancellationToken ct)
    {
        var normalized = NormalizeAndValidate(request);
        await EnsureTargetExistsAsync(normalized.TargetType, normalized.TargetId, ct);
        await EnsureNoActiveDuplicateAsync(normalized.Platform, normalized.StoreProductId, exceptId: null, normalized.IsActive, ct);

        var now = timeProvider.GetUtcNow();
        var row = new NativeIapProductMapping
        {
            Id = $"iap_{Guid.NewGuid():N}",
            Platform = normalized.Platform,
            StoreProductId = normalized.StoreProductId,
            TargetType = normalized.TargetType,
            TargetId = normalized.TargetId,
            DisplayName = normalized.DisplayName,
            IsActive = normalized.IsActive,
            CreatedAt = now,
            UpdatedAt = now,
            CreatedByAdminId = adminId,
            UpdatedByAdminId = adminId,
        };

        db.NativeIapProductMappings.Add(row);
        AddAudit("NativeIapProductMappingCreated", adminId, row, now, before: null, after: AuditSnapshot(row));
        await db.SaveChangesAsync(ct);
        return ToResponse(row);
    }

    public async Task<NativeIapProductMappingResponse> UpdateAdminMappingAsync(
        string adminId,
        string id,
        NativeIapProductMappingUpsertRequest request,
        CancellationToken ct)
    {
        var row = await db.NativeIapProductMappings.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (row is null)
        {
            throw ApiException.NotFound("native_iap_product_not_found", "Native IAP product mapping was not found.");
        }

        var normalized = NormalizeAndValidate(request);
        await EnsureTargetExistsAsync(normalized.TargetType, normalized.TargetId, ct);
        await EnsureNoActiveDuplicateAsync(normalized.Platform, normalized.StoreProductId, id, normalized.IsActive, ct);

        var before = AuditSnapshot(row);
        row.Platform = normalized.Platform;
        row.StoreProductId = normalized.StoreProductId;
        row.TargetType = normalized.TargetType;
        row.TargetId = normalized.TargetId;
        row.DisplayName = normalized.DisplayName;
        row.IsActive = normalized.IsActive;
        row.UpdatedAt = timeProvider.GetUtcNow();
        row.UpdatedByAdminId = adminId;

        AddAudit("NativeIapProductMappingUpdated", adminId, row, row.UpdatedAt, before, AuditSnapshot(row));
        await db.SaveChangesAsync(ct);
        return ToResponse(row);
    }

    public async Task DeleteAdminMappingAsync(string adminId, string id, CancellationToken ct)
    {
        var row = await db.NativeIapProductMappings.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (row is null)
        {
            throw ApiException.NotFound("native_iap_product_not_found", "Native IAP product mapping was not found.");
        }

        var now = timeProvider.GetUtcNow();
        AddAudit("NativeIapProductMappingDeleted", adminId, row, now, before: AuditSnapshot(row), after: null);
        db.NativeIapProductMappings.Remove(row);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<NativeIapProductMappingResponse>> ListLearnerMappingsAsync(string platform, CancellationToken ct)
    {
        var normalizedPlatform = NormalizeRequiredPlatform(platform);
        var rows = await db.NativeIapProductMappings.AsNoTracking()
            .Where(x => x.Platform == normalizedPlatform && x.IsActive)
            .OrderBy(x => x.StoreProductId)
            .ToListAsync(ct);

        return rows.Select(ToResponse).ToList();
    }

    public NativeIapReceiptValidationResponse ValidateReceiptFailClosed(NativeIapReceiptValidationRequest request)
    {
        _ = NormalizeRequiredPlatform(request.Platform);
        if (string.IsNullOrWhiteSpace(request.ReceiptToken))
        {
            throw ApiException.Validation(
                "native_iap_receipt_required",
                "A native IAP receipt/token is required.",
                [new ApiFieldError("receiptToken", "required", "Receipt token is required.")]);
        }

        return new NativeIapReceiptValidationResponse(
            IsValid: false,
            EntitlementGranted: false,
            Code: "native_iap_validation_unconfigured",
            Message: "Native in-app purchase receipt validation is not configured. No entitlement was granted.");
    }

    private async Task EnsureNoActiveDuplicateAsync(
        string platform,
        string storeProductId,
        string? exceptId,
        bool isActive,
        CancellationToken ct)
    {
        if (!isActive)
        {
            return;
        }

        var duplicate = await db.NativeIapProductMappings.AsNoTracking()
            .AnyAsync(x =>
                x.IsActive
                && x.Platform == platform
                && x.StoreProductId == storeProductId
                && (exceptId == null || x.Id != exceptId), ct);

        if (duplicate)
        {
            throw ApiException.Conflict(
                "native_iap_product_duplicate",
                "An active native IAP mapping already exists for this platform and product id.",
                [new ApiFieldError("storeProductId", "duplicate", "Active platform/product ids must be unique.")]);
        }
    }

    private async Task EnsureTargetExistsAsync(string targetType, string targetId, CancellationToken ct)
    {
        var exists = targetType switch
        {
            "plan" => await db.BillingPlans.AsNoTracking()
                .AnyAsync(x => (x.Code == targetId || x.Id == targetId) && x.Status == BillingPlanStatus.Active, ct),
            "add_on" => await db.BillingAddOns.AsNoTracking()
                .AnyAsync(x => (x.Code == targetId || x.Id == targetId) && x.Status == BillingAddOnStatus.Active, ct),
            "wallet_tier" => await db.WalletTopUpTierConfigs.AsNoTracking()
                .AnyAsync(x => (x.Slug == targetId || x.Id.ToString() == targetId) && x.IsActive, ct),
            _ => false,
        };

        if (!exists)
        {
            throw ApiException.Validation(
                "native_iap_target_not_found",
                "Native IAP mappings must target an active billing plan, add-on, or wallet tier.",
                [new ApiFieldError("targetId", "not_found", "Choose an active billing catalog target.")]);
        }
    }

    private static NormalizedNativeIapProductMapping NormalizeAndValidate(NativeIapProductMappingUpsertRequest request)
    {
        var platform = NormalizeRequiredPlatform(request.Platform);
        var storeProductId = RequireTrimmed(request.StoreProductId, "storeProductId", "Native store product id is required.", 192);
        var targetType = RequireTrimmed(request.TargetType, "targetType", "Billing mapping target type is required.", 32).ToLowerInvariant();
        var targetId = RequireTrimmed(request.TargetId, "targetId", "Billing mapping target id is required.", 96);
        var displayName = string.IsNullOrWhiteSpace(request.DisplayName) ? null : request.DisplayName.Trim();

        if (!ValidTargetTypes.Contains(targetType))
        {
            throw ApiException.Validation(
                "native_iap_target_type_invalid",
                "Native IAP mappings must target plan, add_on, or wallet_tier.",
                [new ApiFieldError("targetType", "invalid", "Use plan, add_on, or wallet_tier.")]);
        }

        if (displayName is { Length: > 128 })
        {
            throw ApiException.Validation(
                "native_iap_display_name_too_long",
                "Native IAP display name must be at most 128 characters.",
                [new ApiFieldError("displayName", "max_length", "Display name must be at most 128 characters.")]);
        }

        return new NormalizedNativeIapProductMapping(
            platform,
            storeProductId,
            targetType,
            targetId,
            request.IsActive,
            displayName);
    }

    private static string? NormalizeOptionalPlatform(string? platform)
        => string.IsNullOrWhiteSpace(platform) ? null : NormalizeRequiredPlatform(platform);

    private static string NormalizeRequiredPlatform(string platform)
    {
        var normalized = RequireTrimmed(platform, "platform", "Platform is required.", 16).ToLowerInvariant();
        if (!ValidPlatforms.Contains(normalized))
        {
            throw ApiException.Validation(
                "native_iap_platform_invalid",
                "Native IAP platform must be ios or android.",
                [new ApiFieldError("platform", "invalid", "Use ios or android.")]);
        }

        return normalized;
    }

    private static string RequireTrimmed(string value, string field, string message, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw ApiException.Validation(
                "native_iap_field_required",
                message,
                [new ApiFieldError(field, "required", message)]);
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw ApiException.Validation(
                "native_iap_field_too_long",
                $"{field} must be at most {maxLength} characters.",
                [new ApiFieldError(field, "max_length", $"{field} must be at most {maxLength} characters.")]);
        }

        return trimmed;
    }

    private static NativeIapProductMappingResponse ToResponse(NativeIapProductMapping row)
        => new(
            row.Id,
            row.Platform,
            row.StoreProductId,
            row.TargetType,
            row.TargetId,
            row.IsActive,
            row.DisplayName,
            row.CreatedAt,
            row.UpdatedAt);

    private void AddAudit(
        string action,
        string adminId,
        NativeIapProductMapping row,
        DateTimeOffset occurredAt,
        object? before,
        object? after)
    {
        db.AuditEvents.Add(new AuditEvent
        {
            Id = Guid.NewGuid().ToString("N"),
            ActorId = adminId,
            ActorName = adminId,
            Action = action,
            ResourceType = "NativeIapProductMapping",
            ResourceId = row.Id,
            Details = JsonSupport.Serialize(new { before, after }),
            OccurredAt = occurredAt,
        });
    }

    private static object AuditSnapshot(NativeIapProductMapping row)
        => new
        {
            row.Platform,
            row.StoreProductId,
            row.TargetType,
            row.TargetId,
            row.DisplayName,
            row.IsActive,
        };

    private sealed record NormalizedNativeIapProductMapping(
        string Platform,
        string StoreProductId,
        string TargetType,
        string TargetId,
        bool IsActive,
        string? DisplayName);
}
