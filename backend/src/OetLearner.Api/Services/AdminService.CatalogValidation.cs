using System.ComponentModel.DataAnnotations;
using System.Linq.Expressions;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Endpoints;
using OetLearner.Api.Security;
using OetLearner.Api.Services.Billing;
using OetLearner.Api.Services.Conversation;
using OetLearner.Api.Services.Entitlements;

namespace OetLearner.Api.Services;

public partial class AdminService
{

    private static BillingPlanStatus ParseBillingPlanStatus(string? status, BillingPlanStatus fallback = BillingPlanStatus.Active)
        => Enum.TryParse<BillingPlanStatus>(status, true, out var parsed) ? parsed : fallback;

    private static BillingAddOnStatus ParseBillingAddOnStatus(string? status, BillingAddOnStatus fallback = BillingAddOnStatus.Active)
        => Enum.TryParse<BillingAddOnStatus>(status, true, out var parsed) ? parsed : fallback;

    private static BillingCouponStatus ParseBillingCouponStatus(string? status, BillingCouponStatus fallback = BillingCouponStatus.Active)
        => Enum.TryParse<BillingCouponStatus>(status, true, out var parsed) ? parsed : fallback;

    private sealed record BillingPlanCatalogInput(
        string Code,
        string Name,
        string Description,
        decimal Price,
        string Currency,
        string Interval,
        int DurationMonths,
        int IncludedCredits,
        int DisplayOrder,
        bool IsVisible,
        bool IsRenewable,
        int TrialDays,
        string? DiagnosticMockEntitlement,
        string? Status,
        string? IncludedSubtestsJson,
        string? EntitlementsJson,
        Oet2026PlanFields Oet2026);

    private sealed record BillingAddOnCatalogInput(
        string Code,
        string Name,
        string Description,
        decimal Price,
        string Currency,
        string Interval,
        int DurationDays,
        int GrantCredits,
        int DisplayOrder,
        bool IsRecurring,
        bool AppliesToAllPlans,
        bool IsStackable,
        int QuantityStep,
        int? MaxQuantity,
        string? Status,
        string? CompatiblePlanCodesJson,
        string? GrantEntitlementsJson,
        Oet2026AddOnFields Oet2026);

    /// <summary>OET 2026 catalog optional fields for plans.</summary>
    private sealed record Oet2026PlanFields(
        decimal? OriginalPriceGbp,
        int? AccessDurationDays,
        bool? WritingAddonsEnabled,
        bool? SpeakingAddonsEnabled,
        bool? SpeakingPracticeAccessEnabled,
        bool? TutorBookDiscountEnabled,
        string? Profession,
        string? ProductCategory,
        string? DashboardModulesJson,
        int? BundledWritingAssessments,
        int? BundledSpeakingSessions,
        int? BundledAiCredits,
        bool? BundledTutorBook,
        bool? BundledBasicEnglish,
        bool? IsDraft,
        bool? ExtensionAllowed,
        bool? RecallUpdatesEnabled,
        string? ComparisonFeaturesJson,
        // ── Delivery + content scoping (access & payment spec 2026-07-15) ──
        // Unlike the fields above, the three nullable strings use "" ⇒ clear to NULL
        // semantics so an admin can remove a WhatsApp access link once set.
        string? DeliveryMethod = null,
        string? TelegramInviteUrl = null,
        string? DeliveryInstructions = null,
        string? ContentOverridesJson = null)
    {
        public static Oet2026PlanFields Empty { get; } = new(
            null, null, null, null, null, null, null, null, null,
            null, null, null, null, null, null, null, null, null);
    }

    /// <summary>OET 2026 catalog optional fields for add-ons.</summary>
    private sealed record Oet2026AddOnFields(
        decimal? OriginalPriceGbp,
        string? AddonKind,
        bool? RequiresEligibleParent,
        string? EligibilityFlag,
        int? LettersGranted,
        int? SessionsGranted,
        string? AiPackageGroup = null,
        string? AiFeaturesJson = null)
    {
        public static Oet2026AddOnFields Empty { get; } = new(null, null, null, null, null, null);
    }

    /// <summary>
    /// Normalizes an admin-supplied AI feature bullet list to a clean JSON string array.
    /// Returns "[]" for empty/blank, null for unparseable input (caller skips the update).
    /// Trims each bullet, drops blanks, caps length (256 chars) and count (12 bullets).
    /// </summary>
    private static string? NormalizeAiFeaturesJson(string? raw)
    {
        if (raw is null) return null;
        var trimmed = raw.Trim();
        if (trimmed.Length == 0 || trimmed == "[]") return "[]";
        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;
            var bullets = new List<string>();
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String) continue;
                var text = item.GetString()?.Trim();
                if (string.IsNullOrEmpty(text)) continue;
                if (text.Length > 256) text = text[..256];
                bullets.Add(text);
                if (bullets.Count >= 12) break;
            }
            return JsonSerializer.Serialize(bullets);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void ApplyOet2026Fields(BillingPlan plan, Oet2026PlanFields fields)
    {
        if (fields.OriginalPriceGbp.HasValue) plan.OriginalPriceGbp = fields.OriginalPriceGbp.Value <= 0 ? null : fields.OriginalPriceGbp;
        if (fields.AccessDurationDays.HasValue) plan.AccessDurationDays = Math.Max(0, fields.AccessDurationDays.Value);
        if (fields.WritingAddonsEnabled.HasValue) plan.WritingAddonsEnabled = fields.WritingAddonsEnabled.Value;
        if (fields.SpeakingAddonsEnabled.HasValue) plan.SpeakingAddonsEnabled = fields.SpeakingAddonsEnabled.Value;
        if (fields.SpeakingPracticeAccessEnabled.HasValue) plan.SpeakingPracticeAccessEnabled = fields.SpeakingPracticeAccessEnabled.Value;
        if (fields.TutorBookDiscountEnabled.HasValue) plan.TutorBookDiscountEnabled = fields.TutorBookDiscountEnabled.Value;
        if (!string.IsNullOrWhiteSpace(fields.Profession)) plan.Profession = fields.Profession.Trim().ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(fields.ProductCategory)) plan.ProductCategory = fields.ProductCategory.Trim().ToLowerInvariant();
        if (fields.DashboardModulesJson is not null)
        {
            plan.DashboardModulesJson = PlanModulePolicy.NormalizeDashboardModulesJson(
                plan.Code,
                fields.DashboardModulesJson);
        }
        if (fields.BundledWritingAssessments.HasValue) plan.BundledWritingAssessments = Math.Max(0, fields.BundledWritingAssessments.Value);
        if (fields.BundledSpeakingSessions.HasValue) plan.BundledSpeakingSessions = Math.Max(0, fields.BundledSpeakingSessions.Value);
        if (fields.BundledAiCredits.HasValue) plan.BundledAiCredits = Math.Max(0, fields.BundledAiCredits.Value);
        if (fields.BundledTutorBook.HasValue) plan.BundledTutorBook = fields.BundledTutorBook.Value;
        if (fields.BundledBasicEnglish.HasValue) plan.BundledBasicEnglish = fields.BundledBasicEnglish.Value;
        if (fields.IsDraft.HasValue) plan.IsDraft = fields.IsDraft.Value;
        if (fields.ExtensionAllowed.HasValue) plan.ExtensionAllowed = fields.ExtensionAllowed.Value;
        if (fields.RecallUpdatesEnabled.HasValue) plan.RecallUpdatesEnabled = fields.RecallUpdatesEnabled.Value;

        // ── Delivery + content scoping ──
        // DeliveryMethod is NOT NULL, so blank leaves the stored value alone; the
        // validator has already rejected any non-empty value outside DeliveryMethods.
        if (DeliveryMethods.IsValid(fields.DeliveryMethod))
        {
            plan.DeliveryMethod = fields.DeliveryMethod!.Trim().ToLowerInvariant();
        }

        // The three below deliberately skip the IsNullOrWhiteSpace guard used above:
        // null ⇒ field omitted (leave as-is), "" ⇒ admin cleared it (write NULL).
        // Guarding on whitespace would make a WhatsApp access link impossible to remove.
        if (fields.TelegramInviteUrl is not null)
        {
            var value = fields.TelegramInviteUrl.Trim();
            plan.TelegramInviteUrl = value.Length == 0 ? null : value;
        }
        if (fields.DeliveryInstructions is not null)
        {
            var value = fields.DeliveryInstructions.Trim();
            plan.DeliveryInstructions = value.Length == 0 ? null : value;
        }
        if (fields.ContentOverridesJson is not null)
        {
            var value = fields.ContentOverridesJson.Trim();
            plan.ContentOverridesJson = value.Length == 0 ? null : value;
        }
    }

    private static void ApplyOet2026Fields(BillingAddOn addOn, Oet2026AddOnFields fields)
    {
        if (fields.OriginalPriceGbp.HasValue) addOn.OriginalPriceGbp = fields.OriginalPriceGbp.Value <= 0 ? null : fields.OriginalPriceGbp;
        if (!string.IsNullOrWhiteSpace(fields.AddonKind)) addOn.AddonKind = fields.AddonKind.Trim().ToLowerInvariant();
        if (fields.RequiresEligibleParent.HasValue) addOn.RequiresEligibleParent = fields.RequiresEligibleParent.Value;
        if (!string.IsNullOrWhiteSpace(fields.EligibilityFlag)) addOn.EligibilityFlag = fields.EligibilityFlag.Trim().ToLowerInvariant();
        if (fields.LettersGranted.HasValue) addOn.LettersGranted = Math.Max(0, fields.LettersGranted.Value);
        if (fields.SessionsGranted.HasValue) addOn.SessionsGranted = Math.Max(0, fields.SessionsGranted.Value);
        if (fields.AiPackageGroup is not null)
        {
            var group = fields.AiPackageGroup.Trim().ToLowerInvariant();
            // Empty clears the override (→ code-prefix fallback); only accept known groups.
            if (group.Length == 0) addOn.AiPackageGroup = string.Empty;
            else if (AiPackageGroups.Contains(group)) addOn.AiPackageGroup = group;
        }
        if (fields.AiFeaturesJson is not null)
        {
            var normalized = NormalizeAiFeaturesJson(fields.AiFeaturesJson);
            if (normalized is not null) addOn.AiFeaturesJson = normalized;
        }
    }

    /// <summary>
    /// Syncs the linked <see cref="ContentPackage"/>'s ComparisonFeaturesJson
    /// ("What's included" bullet list) for a plan when the admin edits the
    /// catalog. No-op when no payload was supplied or the linked package
    /// doesn't yet exist (will be created by the Oet2026CatalogSeeder).
    /// </summary>
    private async Task SyncContentPackageComparisonFeaturesAsync(
        string planCode,
        string? comparisonFeaturesJson,
        DateTimeOffset now,
        CancellationToken ct)
    {
        if (comparisonFeaturesJson is null) return;
        if (string.IsNullOrWhiteSpace(planCode)) return;

        var pkg = await db.ContentPackages.FirstOrDefaultAsync(p => p.Code == planCode, ct);
        if (pkg is null) return;

        var trimmed = comparisonFeaturesJson.Trim();
        if (string.IsNullOrEmpty(trimmed)) trimmed = "[]";
        pkg.ComparisonFeaturesJson = trimmed;
        pkg.UpdatedAt = now;
    }

    private sealed record BillingCouponCatalogInput(
        string Code,
        string Name,
        string Description,
        string DiscountType,
        decimal DiscountValue,
        string Currency,
        DateTimeOffset? StartsAt,
        DateTimeOffset? EndsAt,
        int? UsageLimitTotal,
        int? UsageLimitPerUser,
        decimal? MinimumSubtotal,
        bool IsStackable,
        string? Status,
        string? ApplicablePlanCodesJson,
        string? ApplicableAddOnCodesJson,
        string? Notes);

    private sealed record CatalogStringArray(string Json, IReadOnlyList<string> Values);

    private sealed record ValidatedBillingPlanCatalog(
        string Code,
        string Name,
        string Description,
        decimal Price,
        string Currency,
        string Interval,
        int DurationMonths,
        int IncludedCredits,
        int DisplayOrder,
        bool IsVisible,
        bool IsRenewable,
        int TrialDays,
        string DiagnosticMockEntitlement,
        BillingPlanStatus Status,
        string IncludedSubtestsJson,
        string EntitlementsJson,
        Oet2026PlanFields Oet2026);

    private sealed record ValidatedBillingAddOnCatalog(
        string Code,
        string Name,
        string Description,
        decimal Price,
        string Currency,
        string Interval,
        int DurationDays,
        int GrantCredits,
        int DisplayOrder,
        bool IsRecurring,
        bool AppliesToAllPlans,
        bool IsStackable,
        int QuantityStep,
        int? MaxQuantity,
        BillingAddOnStatus Status,
        string CompatiblePlanCodesJson,
        string GrantEntitlementsJson,
        Oet2026AddOnFields Oet2026);

    private sealed record ValidatedBillingCouponCatalog(
        string Code,
        string Name,
        string Description,
        BillingDiscountType DiscountType,
        decimal DiscountValue,
        string Currency,
        DateTimeOffset? StartsAt,
        DateTimeOffset? EndsAt,
        int? UsageLimitTotal,
        int? UsageLimitPerUser,
        decimal? MinimumSubtotal,
        bool IsStackable,
        BillingCouponStatus Status,
        string ApplicablePlanCodesJson,
        string ApplicableAddOnCodesJson,
        string? Notes);

    private sealed record BillingEntitlementContentShape(bool IsLegacy, string Reason);

    private static BillingPlanCatalogInput ToBillingPlanCatalogInput(AdminBillingPlanCreateRequest request) => new(
        request.Code,
        request.Name,
        request.Description,
        request.Price,
        request.Currency,
        request.Interval,
        request.DurationMonths,
        request.IncludedCredits,
        request.DisplayOrder,
        request.IsVisible,
        request.IsRenewable,
        request.TrialDays,
        request.DiagnosticMockEntitlement,
        request.Status,
        request.IncludedSubtestsJson,
        request.EntitlementsJson,
        new Oet2026PlanFields(
            request.OriginalPriceGbp,
            request.AccessDurationDays,
            request.WritingAddonsEnabled,
            request.SpeakingAddonsEnabled,
            request.SpeakingPracticeAccessEnabled,
            request.TutorBookDiscountEnabled,
            request.Profession,
            request.ProductCategory,
            request.DashboardModulesJson,
            request.BundledWritingAssessments,
            request.BundledSpeakingSessions,
            request.BundledAiCredits,
            request.BundledTutorBook,
            request.BundledBasicEnglish,
            request.IsDraft,
            request.ExtensionAllowed,
            request.RecallUpdatesEnabled,
            request.ComparisonFeaturesJson,
            request.DeliveryMethod,
            request.TelegramInviteUrl,
            request.DeliveryInstructions,
            request.ContentOverridesJson));

    private static BillingPlanCatalogInput ToBillingPlanCatalogInput(AdminBillingPlanUpdateRequest request) => new(
        request.Code,
        request.Name,
        request.Description,
        request.Price,
        request.Currency,
        request.Interval,
        request.DurationMonths,
        request.IncludedCredits,
        request.DisplayOrder,
        request.IsVisible,
        request.IsRenewable,
        request.TrialDays,
        request.DiagnosticMockEntitlement,
        request.Status,
        request.IncludedSubtestsJson,
        request.EntitlementsJson,
        new Oet2026PlanFields(
            request.OriginalPriceGbp,
            request.AccessDurationDays,
            request.WritingAddonsEnabled,
            request.SpeakingAddonsEnabled,
            request.SpeakingPracticeAccessEnabled,
            request.TutorBookDiscountEnabled,
            request.Profession,
            request.ProductCategory,
            request.DashboardModulesJson,
            request.BundledWritingAssessments,
            request.BundledSpeakingSessions,
            request.BundledAiCredits,
            request.BundledTutorBook,
            request.BundledBasicEnglish,
            request.IsDraft,
            request.ExtensionAllowed,
            request.RecallUpdatesEnabled,
            request.ComparisonFeaturesJson,
            request.DeliveryMethod,
            request.TelegramInviteUrl,
            request.DeliveryInstructions,
            request.ContentOverridesJson));

    private static BillingAddOnCatalogInput ToBillingAddOnCatalogInput(AdminBillingAddOnCreateRequest request) => new(
        request.Code,
        request.Name,
        request.Description,
        request.Price,
        request.Currency,
        request.Interval,
        request.DurationDays,
        request.GrantCredits,
        request.DisplayOrder,
        request.IsRecurring,
        request.AppliesToAllPlans,
        request.IsStackable,
        request.QuantityStep,
        request.MaxQuantity,
        request.Status,
        request.CompatiblePlanCodesJson,
        request.GrantEntitlementsJson,
        new Oet2026AddOnFields(
            request.OriginalPriceGbp,
            request.AddonKind,
            request.RequiresEligibleParent,
            request.EligibilityFlag,
            request.LettersGranted,
            request.SessionsGranted,
            request.AiPackageGroup,
            request.AiFeaturesJson));

    private static BillingAddOnCatalogInput ToBillingAddOnCatalogInput(AdminBillingAddOnUpdateRequest request) => new(
        request.Code,
        request.Name,
        request.Description,
        request.Price,
        request.Currency,
        request.Interval,
        request.DurationDays,
        request.GrantCredits,
        request.DisplayOrder,
        request.IsRecurring,
        request.AppliesToAllPlans,
        request.IsStackable,
        request.QuantityStep,
        request.MaxQuantity,
        request.Status,
        request.CompatiblePlanCodesJson,
        request.GrantEntitlementsJson,
        new Oet2026AddOnFields(
            request.OriginalPriceGbp,
            request.AddonKind,
            request.RequiresEligibleParent,
            request.EligibilityFlag,
            request.LettersGranted,
            request.SessionsGranted,
            request.AiPackageGroup,
            request.AiFeaturesJson));

    private static BillingCouponCatalogInput ToBillingCouponCatalogInput(AdminBillingCouponCreateRequest request) => new(
        request.Code,
        request.Name,
        request.Description,
        request.DiscountType,
        request.DiscountValue,
        request.Currency,
        request.StartsAt,
        request.EndsAt,
        request.UsageLimitTotal,
        request.UsageLimitPerUser,
        request.MinimumSubtotal,
        request.IsStackable,
        request.Status,
        request.ApplicablePlanCodesJson,
        request.ApplicableAddOnCodesJson,
        request.Notes);

    private static BillingCouponCatalogInput ToBillingCouponCatalogInput(AdminBillingCouponUpdateRequest request) => new(
        request.Code,
        request.Name,
        request.Description,
        request.DiscountType,
        request.DiscountValue,
        request.Currency,
        request.StartsAt,
        request.EndsAt,
        request.UsageLimitTotal,
        request.UsageLimitPerUser,
        request.MinimumSubtotal,
        request.IsStackable,
        request.Status,
        request.ApplicablePlanCodesJson,
        request.ApplicableAddOnCodesJson,
        request.Notes);

    private static string MapBillingDiscountType(BillingDiscountType discountType)
        => discountType == BillingDiscountType.FixedAmount ? "fixed" : "percentage";

    private static void AddCatalogError(List<ApiFieldError> errors, string field, string code, string message)
        => errors.Add(new ApiFieldError(field, code, message));

    private static void ThrowIfCatalogInvalid(List<ApiFieldError> errors, string errorCode, string message)
    {
        if (errors.Count > 0)
        {
            throw ApiException.Validation(errorCode, message, errors);
        }
    }

    private static void ThrowIfCatalogCodeChanged(string? requestedCode, string existingCode, string errorCode, string message)
    {
        if (!string.Equals(requestedCode?.Trim() ?? string.Empty, existingCode, StringComparison.Ordinal))
        {
            throw ApiException.Validation(
                errorCode,
                message,
                [new ApiFieldError("code", "immutable", "Catalog code cannot be changed after creation.")]);
        }
    }

    private async Task ThrowIfCatalogCodeChangedWithAuditAsync(
        string adminId,
        string adminName,
        string resourceType,
        string resourceId,
        string? requestedCode,
        string existingCode,
        string errorCode,
        string message,
        CancellationToken ct)
    {
        try
        {
            ThrowIfCatalogCodeChanged(requestedCode, existingCode, errorCode, message);
        }
        catch (ApiException)
        {
            await LogCatalogCodeImmutabilityViolationAsync(
                adminId,
                adminName,
                resourceType,
                resourceId,
                existingCode,
                requestedCode,
                ct);
            throw;
        }
    }

    private static string ValidateCatalogText(List<ApiFieldError> errors, string field, string? value, int maxLength, bool required = true)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (required && string.IsNullOrWhiteSpace(trimmed))
        {
            AddCatalogError(errors, field, "required", $"{field} is required.");
        }

        if (trimmed.Length > maxLength)
        {
            AddCatalogError(errors, field, "too_long", $"{field} must be {maxLength} characters or fewer.");
        }

        return trimmed;
    }

    private static string ValidateCatalogCode(List<ApiFieldError> errors, string field, string? value)
    {
        var code = ValidateCatalogText(errors, field, value, 64);
        if (code.Any(char.IsWhiteSpace))
        {
            AddCatalogError(errors, field, "format", $"{field} must not contain whitespace.");
        }

        return code;
    }

    private static string ValidateCatalogCurrency(List<ApiFieldError> errors, string? value)
    {
        var currency = ValidateCatalogText(errors, "currency", value, 8).ToUpperInvariant();
        if (currency.Length is < 3 or > 8 || currency.Any(character => character is < 'A' or > 'Z'))
        {
            AddCatalogError(errors, "currency", "invalid", "Currency must use 3 to 8 uppercase letters.");
        }

        return currency;
    }

    private static string ValidateCatalogInterval(List<ApiFieldError> errors, string? value, IReadOnlySet<string> allowedIntervals)
    {
        var interval = ValidateCatalogText(errors, "interval", value, 32).ToLowerInvariant();
        if (!allowedIntervals.Contains(interval))
        {
            AddCatalogError(errors, "interval", "invalid", "Choose a supported billing interval.");
        }

        return interval;
    }

    private static TEnum ValidateCatalogEnum<TEnum>(List<ApiFieldError> errors, string field, string? value, TEnum fallback)
        where TEnum : struct, Enum
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return fallback;
        }

        var matchingName = Enum.GetNames<TEnum>()
            .FirstOrDefault(name => string.Equals(name, trimmed, StringComparison.OrdinalIgnoreCase));
        if (matchingName is not null && Enum.TryParse<TEnum>(matchingName, out var parsed))
        {
            return parsed;
        }

        AddCatalogError(errors, field, "invalid", $"{field} is not supported.");
        return fallback;
    }

    private static BillingDiscountType ValidateBillingDiscountType(List<ApiFieldError> errors, string? value)
    {
        var normalized = (value ?? string.Empty).Trim().Replace("_", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
        return normalized switch
        {
            "percentage" => BillingDiscountType.Percentage,
            "fixed" or "fixedamount" => BillingDiscountType.FixedAmount,
            _ => AddInvalidDiscountType(errors)
        };

        static BillingDiscountType AddInvalidDiscountType(List<ApiFieldError> errors)
        {
            AddCatalogError(errors, "discountType", "invalid", "Discount type must be percentage or fixed.");
            return BillingDiscountType.Percentage;
        }
    }

    private static CatalogStringArray ValidateCatalogStringArrayJson(List<ApiFieldError> errors, string field, string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new CatalogStringArray("[]", []);
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                AddCatalogError(errors, field, "invalid_json_shape", $"{field} must be a JSON array of strings.");
                return new CatalogStringArray("[]", []);
            }

            var values = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String)
                {
                    AddCatalogError(errors, field, "invalid_json_shape", $"{field} must only contain strings.");
                    continue;
                }

                var itemValue = item.GetString()?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(itemValue))
                {
                    AddCatalogError(errors, field, "empty_item", $"{field} must not contain empty values.");
                    continue;
                }

                if (!seen.Add(itemValue))
                {
                    AddCatalogError(errors, field, "duplicate_item", $"{field} must not contain duplicate values.");
                    continue;
                }

                values.Add(itemValue);
            }

            var serialized = JsonSupport.Serialize(values);
            if (serialized.Length > CatalogJsonMaxLength)
            {
                AddCatalogError(errors, field, "too_long", $"{field} is too large.");
            }

            return new CatalogStringArray(serialized, values);
        }
        catch (JsonException)
        {
            AddCatalogError(errors, field, "invalid_json", $"{field} must be valid JSON.");
            return new CatalogStringArray("[]", []);
        }
    }

    private static string ValidateCatalogObjectJson(List<ApiFieldError> errors, string field, string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return "{}";
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                AddCatalogError(errors, field, "invalid_json_shape", $"{field} must be a JSON object.");
                return "{}";
            }

            var serialized = JsonSerializer.Serialize(document.RootElement, JsonSupport.Options);
            if (serialized.Length > CatalogJsonMaxLength)
            {
                AddCatalogError(errors, field, "too_long", $"{field} is too large.");
            }

            return serialized;
        }
        catch (JsonException)
        {
            AddCatalogError(errors, field, "invalid_json", $"{field} must be valid JSON.");
            return "{}";
        }
    }

    private static void ValidateCatalogReferenceCodes(List<ApiFieldError> errors, string field, IEnumerable<string> codes, IReadOnlySet<string> knownCodes, string catalogLabel)
    {
        foreach (var code in codes)
        {
            if (!knownCodes.Contains(code))
            {
                AddCatalogError(errors, field, "unknown_reference", $"Unknown {catalogLabel} code '{code}'.");
            }
        }
    }

    private async Task<ValidatedBillingPlanCatalog> ValidateBillingPlanCatalogAsync(
        BillingPlanCatalogInput request,
        string? existingPlanId,
        BillingPlanStatus fallbackStatus,
        CancellationToken ct)
    {
        var errors = new List<ApiFieldError>();
        var code = ValidateCatalogCode(errors, "code", request.Code);
        var name = ValidateCatalogText(errors, "name", request.Name, 128);
        var description = ValidateCatalogText(errors, "description", request.Description, 1024, required: false);
        var currency = ValidateCatalogCurrency(errors, request.Currency);
        var interval = ValidateCatalogInterval(errors, request.Interval, PlanIntervals);
        var status = ValidateCatalogEnum(errors, "status", request.Status, fallbackStatus);
        var includedSubtests = ValidateCatalogStringArrayJson(errors, "includedSubtestsJson", request.IncludedSubtestsJson);
        var entitlementsJson = ValidateCatalogObjectJson(errors, "entitlementsJson", request.EntitlementsJson);
        var diagnosticMockEntitlement = string.IsNullOrWhiteSpace(request.DiagnosticMockEntitlement)
            ? "one_per_lifetime"
            : request.DiagnosticMockEntitlement.Trim().ToLowerInvariant();
        if (!DiagnosticMockEntitlementValues.Contains(diagnosticMockEntitlement))
        {
            AddCatalogError(
                errors,
                "diagnosticMockEntitlement",
                "invalid",
                "Diagnostic mock entitlement must be unlimited, one_per_lifetime, one_per_renewal_period, paid_per_use, or disabled.");
        }

        if (request.Price < 0)
        {
            AddCatalogError(errors, "price", "negative", "Price cannot be negative.");
        }

        if (request.DurationMonths < 0)
        {
            AddCatalogError(errors, "durationMonths", "negative", "Duration months cannot be negative.");
        }

        if (request.IncludedCredits < 0)
        {
            AddCatalogError(errors, "includedCredits", "negative", "Included credits cannot be negative.");
        }

        if (request.TrialDays < 0)
        {
            AddCatalogError(errors, "trialDays", "negative", "Trial days cannot be negative.");
        }

        // Delivery method: blank ⇒ leave the stored value alone; anything else must be known.
        var deliveryMethod = request.Oet2026.DeliveryMethod?.Trim();
        if (!string.IsNullOrEmpty(deliveryMethod) && !DeliveryMethods.IsValid(deliveryMethod))
        {
            AddCatalogError(
                errors,
                "deliveryMethod",
                "invalid",
                "Delivery method must be automatic_web, manual_web, whatsapp, or manual_material.");
        }
        var parsedSubtests = includedSubtests.Values;
        var hasNoPlatformAccess = parsedSubtests.Any(code =>
            string.Equals(code, EffectiveEntitlementResolver.NoPlatformAccessSubtest, StringComparison.OrdinalIgnoreCase));
        if (hasNoPlatformAccess && parsedSubtests.Count != 1)
        {
            AddCatalogError(
                errors,
                "includedSubtestsJson",
                "invalid",
                "The no-platform-access value cannot be combined with Listening, Reading, Writing, or Speaking.");
        }
        if (hasNoPlatformAccess
            && !string.Equals(deliveryMethod, DeliveryMethods.ManualMaterial, StringComparison.OrdinalIgnoreCase))
        {
            AddCatalogError(
                errors,
                "deliveryMethod",
                "invalid",
                "No platform access requires the manual_material delivery method.");
        }

        // Content overrides are read back by the entitlement resolver at request time —
        // a malformed blob must never reach the column.
        var contentOverrides = request.Oet2026.ContentOverridesJson?.Trim();
        if (!string.IsNullOrEmpty(contentOverrides))
        {
            try
            {
                using var document = JsonDocument.Parse(contentOverrides);
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                {
                    AddCatalogError(errors, "contentOverridesJson", "invalid_json_shape", "contentOverridesJson must be a JSON object.");
                }
            }
            catch (JsonException)
            {
                AddCatalogError(errors, "contentOverridesJson", "invalid_json", "contentOverridesJson must be valid JSON.");
            }
        }

        if (!string.IsNullOrWhiteSpace(code))
        {
            var normalizedCode = code.ToLowerInvariant();
            var duplicateExists = await db.BillingPlans.AsNoTracking().AnyAsync(
                plan => plan.Code.ToLower() == normalizedCode && (existingPlanId == null || plan.Id != existingPlanId),
                ct);
            if (duplicateExists)
            {
                AddCatalogError(errors, "code", "duplicate", "A plan with this code already exists.");
            }
        }

        ThrowIfCatalogInvalid(errors, "billing_plan_invalid", "Billing plan catalog data is invalid.");

        var normalizedOet2026 = hasNoPlatformAccess
            ? request.Oet2026 with
            {
                WritingAddonsEnabled = false,
                SpeakingAddonsEnabled = false,
                SpeakingPracticeAccessEnabled = false,
                TutorBookDiscountEnabled = false,
                DashboardModulesJson = "[]",
                BundledWritingAssessments = 0,
                BundledSpeakingSessions = 0,
                BundledAiCredits = 0,
                BundledTutorBook = false,
                BundledBasicEnglish = false,
                ExtensionAllowed = false,
                RecallUpdatesEnabled = false,
                ContentOverridesJson = "{}",
            }
            : request.Oet2026;

        return new ValidatedBillingPlanCatalog(
            code,
            name,
            description,
            request.Price,
            currency,
            interval,
            request.DurationMonths,
            hasNoPlatformAccess ? 0 : request.IncludedCredits,
            request.DisplayOrder,
            request.IsVisible,
            request.IsRenewable,
            hasNoPlatformAccess ? 0 : request.TrialDays,
            diagnosticMockEntitlement,
            status,
            includedSubtests.Json,
            hasNoPlatformAccess ? "{}" : entitlementsJson,
            normalizedOet2026);
    }

    private async Task<ValidatedBillingAddOnCatalog> ValidateBillingAddOnCatalogAsync(
        BillingAddOnCatalogInput request,
        string? existingAddOnId,
        BillingAddOnStatus fallbackStatus,
        CancellationToken ct)
    {
        var errors = new List<ApiFieldError>();
        var code = ValidateCatalogCode(errors, "code", request.Code);
        var name = ValidateCatalogText(errors, "name", request.Name, 128);
        var description = ValidateCatalogText(errors, "description", request.Description, 1024, required: false);
        var currency = ValidateCatalogCurrency(errors, request.Currency);
        var interval = ValidateCatalogInterval(errors, request.Interval, AddOnIntervals);
        var status = ValidateCatalogEnum(errors, "status", request.Status, fallbackStatus);
        var compatiblePlanCodes = ValidateCatalogStringArrayJson(errors, "compatiblePlanCodesJson", request.CompatiblePlanCodesJson);
        var grantEntitlementsJson = ValidateCatalogObjectJson(errors, "grantEntitlementsJson", request.GrantEntitlementsJson);

        if (request.Price < 0)
        {
            AddCatalogError(errors, "price", "negative", "Price cannot be negative.");
        }

        if (request.DurationDays < 0)
        {
            AddCatalogError(errors, "durationDays", "negative", "Duration days cannot be negative.");
        }

        if (request.GrantCredits < 0)
        {
            AddCatalogError(errors, "grantCredits", "negative", "Grant credits cannot be negative.");
        }

        if (request.QuantityStep < 1)
        {
            AddCatalogError(errors, "quantityStep", "min", "Quantity step must be at least 1.");
        }

        if (request.MaxQuantity is not null && request.MaxQuantity.Value < request.QuantityStep)
        {
            AddCatalogError(errors, "maxQuantity", "min", "Max quantity must be greater than or equal to the quantity step.");
        }

        if (!request.AppliesToAllPlans && compatiblePlanCodes.Values.Count == 0)
        {
            AddCatalogError(errors, "compatiblePlanCodesJson", "required", "Restricted add-ons must list at least one compatible plan code.");
        }

        if (!string.IsNullOrWhiteSpace(code))
        {
            var normalizedCode = code.ToLowerInvariant();
            var duplicateExists = await db.BillingAddOns.AsNoTracking().AnyAsync(
                addOn => addOn.Code.ToLower() == normalizedCode && (existingAddOnId == null || addOn.Id != existingAddOnId),
                ct);
            if (duplicateExists)
            {
                AddCatalogError(errors, "code", "duplicate", "An add-on with this code already exists.");
            }
        }

        var knownPlanCodes = await db.BillingPlans.AsNoTracking()
            .Select(plan => plan.Code)
            .ToListAsync(ct);
        ValidateCatalogReferenceCodes(
            errors,
            "compatiblePlanCodesJson",
            compatiblePlanCodes.Values,
            new HashSet<string>(knownPlanCodes, StringComparer.OrdinalIgnoreCase),
            "plan");

        ThrowIfCatalogInvalid(errors, "billing_addon_invalid", "Billing add-on catalog data is invalid.");

        return new ValidatedBillingAddOnCatalog(
            code,
            name,
            description,
            request.Price,
            currency,
            interval,
            request.DurationDays,
            request.GrantCredits,
            request.DisplayOrder,
            request.IsRecurring,
            request.AppliesToAllPlans,
            request.IsStackable,
            request.QuantityStep,
            request.MaxQuantity,
            status,
            compatiblePlanCodes.Json,
            grantEntitlementsJson,
            request.Oet2026);
    }

    private async Task<ValidatedBillingCouponCatalog> ValidateBillingCouponCatalogAsync(
        BillingCouponCatalogInput request,
        string? existingCouponId,
        BillingCouponStatus fallbackStatus,
        CancellationToken ct)
    {
        var errors = new List<ApiFieldError>();
        var code = ValidateCatalogCode(errors, "code", request.Code);
        var name = ValidateCatalogText(errors, "name", request.Name, 128);
        var description = ValidateCatalogText(errors, "description", request.Description, 1024, required: false);
        var notes = ValidateCatalogText(errors, "notes", request.Notes, 1024, required: false);
        var currency = ValidateCatalogCurrency(errors, request.Currency);
        var status = ValidateCatalogEnum(errors, "status", request.Status, fallbackStatus);
        var discountType = ValidateBillingDiscountType(errors, request.DiscountType);
        var applicablePlanCodes = ValidateCatalogStringArrayJson(errors, "applicablePlanCodesJson", request.ApplicablePlanCodesJson);
        var applicableAddOnCodes = ValidateCatalogStringArrayJson(errors, "applicableAddOnCodesJson", request.ApplicableAddOnCodesJson);

        if (request.DiscountValue <= 0)
        {
            AddCatalogError(errors, "discountValue", "min", "Discount value must be greater than zero.");
        }

        if (discountType == BillingDiscountType.Percentage && request.DiscountValue > 100)
        {
            AddCatalogError(errors, "discountValue", "max", "Percentage discounts cannot exceed 100%.");
        }

        if (request.StartsAt is not null && request.EndsAt is not null && request.StartsAt >= request.EndsAt)
        {
            AddCatalogError(errors, "endsAt", "range", "Coupon end date must be later than the start date.");
        }

        if (request.UsageLimitTotal is not null && request.UsageLimitTotal.Value <= 0)
        {
            AddCatalogError(errors, "usageLimitTotal", "min", "Total usage limit must be greater than zero.");
        }

        if (request.UsageLimitPerUser is not null && request.UsageLimitPerUser.Value <= 0)
        {
            AddCatalogError(errors, "usageLimitPerUser", "min", "Per-user usage limit must be greater than zero.");
        }

        if (request.UsageLimitTotal is not null && request.UsageLimitPerUser is not null && request.UsageLimitPerUser.Value > request.UsageLimitTotal.Value)
        {
            AddCatalogError(errors, "usageLimitPerUser", "max", "Per-user usage limit cannot exceed the total usage limit.");
        }

        if (request.MinimumSubtotal is not null && request.MinimumSubtotal.Value < 0)
        {
            AddCatalogError(errors, "minimumSubtotal", "negative", "Minimum subtotal cannot be negative.");
        }

        if (!string.IsNullOrWhiteSpace(code))
        {
            var normalizedCode = code.ToLowerInvariant();
            var duplicateExists = await db.BillingCoupons.AsNoTracking().AnyAsync(
                coupon => coupon.Code.ToLower() == normalizedCode && (existingCouponId == null || coupon.Id != existingCouponId),
                ct);
            if (duplicateExists)
            {
                AddCatalogError(errors, "code", "duplicate", "A coupon with this code already exists.");
            }
        }

        var knownPlanCodes = await db.BillingPlans.AsNoTracking()
            .Select(plan => plan.Code)
            .ToListAsync(ct);
        ValidateCatalogReferenceCodes(
            errors,
            "applicablePlanCodesJson",
            applicablePlanCodes.Values,
            new HashSet<string>(knownPlanCodes, StringComparer.OrdinalIgnoreCase),
            "plan");

        var knownAddOnCodes = await db.BillingAddOns.AsNoTracking()
            .Select(addOn => addOn.Code)
            .ToListAsync(ct);
        ValidateCatalogReferenceCodes(
            errors,
            "applicableAddOnCodesJson",
            applicableAddOnCodes.Values,
            new HashSet<string>(knownAddOnCodes, StringComparer.OrdinalIgnoreCase),
            "add-on");

        ThrowIfCatalogInvalid(errors, "billing_coupon_invalid", "Billing coupon catalog data is invalid.");

        return new ValidatedBillingCouponCatalog(
            code,
            name,
            description,
            discountType,
            request.DiscountValue,
            currency,
            request.StartsAt,
            request.EndsAt,
            request.UsageLimitTotal,
            request.UsageLimitPerUser,
            request.MinimumSubtotal,
            request.IsStackable,
            status,
            applicablePlanCodes.Json,
            applicableAddOnCodes.Json,
            string.IsNullOrWhiteSpace(notes) ? null : notes);
    }

    private static BillingCatalogVersionMetadata CreateCatalogVersionMetadata(
        IEnumerable<(string Id, int VersionNumber)> versions,
        string? activeVersionId,
        string? latestVersionId)
    {
        var items = versions.ToList();
        var activeVersionNumber = string.IsNullOrWhiteSpace(activeVersionId)
            ? null
            : items.Where(version => version.Id == activeVersionId).Select(version => (int?)version.VersionNumber).FirstOrDefault();
        var latestVersionNumber = string.IsNullOrWhiteSpace(latestVersionId)
            ? null
            : items.Where(version => version.Id == latestVersionId).Select(version => (int?)version.VersionNumber).FirstOrDefault();

        return new BillingCatalogVersionMetadata(items.Count, activeVersionNumber, latestVersionNumber);
    }

    private async Task<Dictionary<string, BillingCatalogVersionMetadata>> GetBillingPlanVersionMetadataAsync(
        IReadOnlyCollection<BillingPlan> plans,
        CancellationToken ct)
    {
        if (plans.Count == 0)
        {
            return [];
        }

        var planIds = plans.Select(plan => plan.Id).ToList();
        var planPointers = plans.ToDictionary(plan => plan.Id, plan => (plan.ActiveVersionId, plan.LatestVersionId));
        var versionRows = await db.BillingPlanVersions.AsNoTracking()
            .Where(version => planIds.Contains(version.PlanId))
            .Select(version => new { version.PlanId, version.Id, version.VersionNumber })
            .ToListAsync(ct);

        return versionRows
            .GroupBy(version => version.PlanId)
            .ToDictionary(
                group => group.Key,
                group => CreateCatalogVersionMetadata(
                    group.Select(version => (version.Id, version.VersionNumber)),
                    planPointers[group.Key].ActiveVersionId,
                    planPointers[group.Key].LatestVersionId));
    }

    private async Task<Dictionary<string, BillingCatalogVersionMetadata>> GetBillingAddOnVersionMetadataAsync(
        IReadOnlyCollection<BillingAddOn> addOns,
        CancellationToken ct)
    {
        if (addOns.Count == 0)
        {
            return [];
        }

        var addOnIds = addOns.Select(addOn => addOn.Id).ToList();
        var addOnPointers = addOns.ToDictionary(addOn => addOn.Id, addOn => (addOn.ActiveVersionId, addOn.LatestVersionId));
        var versionRows = await db.BillingAddOnVersions.AsNoTracking()
            .Where(version => addOnIds.Contains(version.AddOnId))
            .Select(version => new { version.AddOnId, version.Id, version.VersionNumber })
            .ToListAsync(ct);

        return versionRows
            .GroupBy(version => version.AddOnId)
            .ToDictionary(
                group => group.Key,
                group => CreateCatalogVersionMetadata(
                    group.Select(version => (version.Id, version.VersionNumber)),
                    addOnPointers[group.Key].ActiveVersionId,
                    addOnPointers[group.Key].LatestVersionId));
    }

    private async Task<Dictionary<string, BillingCatalogVersionMetadata>> GetBillingCouponVersionMetadataAsync(
        IReadOnlyCollection<BillingCoupon> coupons,
        CancellationToken ct)
    {
        if (coupons.Count == 0)
        {
            return [];
        }

        var couponIds = coupons.Select(coupon => coupon.Id).ToList();
        var couponPointers = coupons.ToDictionary(coupon => coupon.Id, coupon => (coupon.ActiveVersionId, coupon.LatestVersionId));
        var versionRows = await db.BillingCouponVersions.AsNoTracking()
            .Where(version => couponIds.Contains(version.CouponId))
            .Select(version => new { version.CouponId, version.Id, version.VersionNumber })
            .ToListAsync(ct);

        return versionRows
            .GroupBy(version => version.CouponId)
            .ToDictionary(
                group => group.Key,
                group => CreateCatalogVersionMetadata(
                    group.Select(version => (version.Id, version.VersionNumber)),
                    couponPointers[group.Key].ActiveVersionId,
                    couponPointers[group.Key].LatestVersionId));
    }

    private static object MapBillingPlan(BillingPlan plan, BillingCatalogVersionMetadata? versionMetadata = null, string? comparisonFeaturesJson = null, bool packageManaged = false) => new
    {
        plan.Id,
        code = plan.Code,
        activeVersionId = plan.ActiveVersionId,
        activeVersionNumber = versionMetadata?.ActiveVersionNumber,
        latestVersionId = plan.LatestVersionId,
        latestVersionNumber = versionMetadata?.LatestVersionNumber,
        versionCount = versionMetadata?.VersionCount ?? 0,
        plan.Name,
        plan.Description,
        plan.Price,
        plan.Currency,
        plan.Interval,
        plan.DurationMonths,
        plan.IncludedCredits,
        plan.DisplayOrder,
        plan.IsVisible,
        plan.IsRenewable,
        plan.TrialDays,
        activeSubscribers = plan.ActiveSubscribers,
        diagnosticMockEntitlement = plan.DiagnosticMockEntitlement,
        status = plan.Status.ToString().ToLowerInvariant(),
        includedSubtests = JsonSupport.Deserialize<List<string>>(plan.IncludedSubtestsJson, []),
        entitlements = JsonSupport.Deserialize<Dictionary<string, object?>>(plan.EntitlementsJson, new Dictionary<string, object?>()),
        archivedAt = plan.ArchivedAt,
        plan.UpdatedAt,
        plan.CreatedAt,
        // OET 2026 catalog fields
        originalPriceGbp = plan.OriginalPriceGbp,
        accessDurationDays = plan.AccessDurationDays,
        writingAddonsEnabled = plan.WritingAddonsEnabled,
        speakingAddonsEnabled = plan.SpeakingAddonsEnabled,
        speakingPracticeAccessEnabled = plan.SpeakingPracticeAccessEnabled,
        tutorBookDiscountEnabled = plan.TutorBookDiscountEnabled,
        profession = plan.Profession,
        productCategory = plan.ProductCategory,
        dashboardModules = JsonSupport.Deserialize<List<string>>(plan.DashboardModulesJson, []),
        bundledWritingAssessments = plan.BundledWritingAssessments,
        bundledSpeakingSessions = plan.BundledSpeakingSessions,
        bundledAiCredits = plan.BundledAiCredits,
        bundledTutorBook = plan.BundledTutorBook,
        bundledBasicEnglish = plan.BundledBasicEnglish,
        isDraft = plan.IsDraft,
        extensionAllowed = plan.ExtensionAllowed,
        recallUpdatesEnabled = plan.RecallUpdatesEnabled,
        // Delivery + content scoping — the plan editor hydrates its form from these.
        // Omitting them would make every save post deliveryMethod='automatic_web'
        // (the form's default for an absent value) and silently unlock a WhatsApp-delivered plan.
        deliveryMethod = plan.DeliveryMethod,
        telegramInviteUrl = plan.TelegramInviteUrl,
        deliveryInstructions = plan.DeliveryInstructions,
        contentOverridesJson = plan.ContentOverridesJson,
        // "What's included" — loaded from the linked ContentPackage.
        comparisonFeatures = JsonSupport.Deserialize<List<string>>(comparisonFeaturesJson ?? "[]", []),
        // True when Subscriptions & Packages owns this plan's name and description (they are mirrored read-only here).
        packageManaged
    };

    private static object MapBillingAddOn(BillingAddOn addOn, BillingCatalogVersionMetadata? versionMetadata = null, bool packageManaged = false) => new
    {
        addOn.Id,
        code = addOn.Code,
        activeVersionId = addOn.ActiveVersionId,
        activeVersionNumber = versionMetadata?.ActiveVersionNumber,
        latestVersionId = addOn.LatestVersionId,
        latestVersionNumber = versionMetadata?.LatestVersionNumber,
        versionCount = versionMetadata?.VersionCount ?? 0,
        addOn.Name,
        addOn.Description,
        addOn.Price,
        addOn.Currency,
        addOn.Interval,
        addOn.DurationDays,
        addOn.GrantCredits,
        addOn.DisplayOrder,
        addOn.IsRecurring,
        addOn.AppliesToAllPlans,
        addOn.IsStackable,
        addOn.QuantityStep,
        addOn.MaxQuantity,
        status = addOn.Status.ToString().ToLowerInvariant(),
        compatiblePlanCodes = JsonSupport.Deserialize<List<string>>(addOn.CompatiblePlanCodesJson, []),
        grantEntitlements = JsonSupport.Deserialize<Dictionary<string, object?>>(addOn.GrantEntitlementsJson, new Dictionary<string, object?>()),
        addOn.CreatedAt,
        addOn.UpdatedAt,
        // OET 2026 catalog fields
        originalPriceGbp = addOn.OriginalPriceGbp,
        addonKind = addOn.AddonKind,
        requiresEligibleParent = addOn.RequiresEligibleParent,
        eligibilityFlag = addOn.EligibilityFlag,
        lettersGranted = addOn.LettersGranted,
        sessionsGranted = addOn.SessionsGranted,
        // AI grading package presentation (admin-configurable)
        aiPackageGroup = addOn.AiPackageGroup,
        aiFeatures = JsonSupport.Deserialize<List<string>>(addOn.AiFeaturesJson, []),
        // True when Subscriptions & Packages owns this add-on's name and description (they are mirrored read-only here).
        packageManaged
    };

    private static object MapBillingCoupon(BillingCoupon coupon, BillingCatalogVersionMetadata? versionMetadata = null) => new
    {
        coupon.Id,
        code = coupon.Code,
        activeVersionId = coupon.ActiveVersionId,
        activeVersionNumber = versionMetadata?.ActiveVersionNumber,
        latestVersionId = coupon.LatestVersionId,
        latestVersionNumber = versionMetadata?.LatestVersionNumber,
        versionCount = versionMetadata?.VersionCount ?? 0,
        coupon.Name,
        coupon.Description,
        discountType = MapBillingDiscountType(coupon.DiscountType),
        coupon.DiscountValue,
        coupon.Currency,
        coupon.StartsAt,
        coupon.EndsAt,
        coupon.UsageLimitTotal,
        coupon.UsageLimitPerUser,
        coupon.MinimumSubtotal,
        coupon.IsStackable,
        status = coupon.Status.ToString().ToLowerInvariant(),
        applicablePlanCodes = JsonSupport.Deserialize<List<string>>(coupon.ApplicablePlanCodesJson, []),
        applicableAddOnCodes = JsonSupport.Deserialize<List<string>>(coupon.ApplicableAddOnCodesJson, []),
        coupon.RedemptionCount,
        coupon.Notes,
        coupon.CreatedAt,
        coupon.UpdatedAt
    };

    private static BillingPlanVersion CreateBillingPlanVersion(BillingPlan plan, int versionNumber, string? adminId, string? adminName, DateTimeOffset createdAt) => new()
    {
        Id = GenerateDomainId("plan-version"),
        PlanId = plan.Id,
        VersionNumber = versionNumber,
        Code = plan.Code,
        Name = plan.Name,
        Description = plan.Description,
        Price = plan.Price,
        Currency = plan.Currency,
        Interval = plan.Interval,
        DurationMonths = plan.DurationMonths,
        IsVisible = plan.IsVisible,
        IsRenewable = plan.IsRenewable,
        TrialDays = plan.TrialDays,
        DisplayOrder = plan.DisplayOrder,
        IncludedCredits = plan.IncludedCredits,
        IncludedSubtestsJson = plan.IncludedSubtestsJson,
        EntitlementsJson = plan.EntitlementsJson,
        Status = plan.Status,
        ArchivedAt = plan.ArchivedAt,
        CreatedByAdminId = adminId,
        CreatedByAdminName = adminName,
        CreatedAt = createdAt,
        // OET 2026 catalog fields — frozen onto immutable snapshot
        OriginalPriceGbp = plan.OriginalPriceGbp,
        AccessDurationDays = plan.AccessDurationDays,
        WritingAddonsEnabled = plan.WritingAddonsEnabled,
        SpeakingAddonsEnabled = plan.SpeakingAddonsEnabled,
        SpeakingPracticeAccessEnabled = plan.SpeakingPracticeAccessEnabled,
        TutorBookDiscountEnabled = plan.TutorBookDiscountEnabled,
        Profession = plan.Profession,
        ProductCategory = plan.ProductCategory,
        DashboardModulesJson = plan.DashboardModulesJson,
        BundledWritingAssessments = plan.BundledWritingAssessments,
        BundledSpeakingSessions = plan.BundledSpeakingSessions,
        BundledAiCredits = plan.BundledAiCredits,
        BundledTutorBook = plan.BundledTutorBook,
        BundledBasicEnglish = plan.BundledBasicEnglish,
        IsDraft = plan.IsDraft,
        ExtensionAllowed = plan.ExtensionAllowed,
        RecallUpdatesEnabled = plan.RecallUpdatesEnabled,
        // Delivery + content scoping — frozen onto the snapshot. LearnerService
        // resolves delivery off the VERSION, so omitting these here would silently
        // fall back to automatic_web and grant instant access to a WhatsApp-delivered plan.
        DeliveryMethod = plan.DeliveryMethod,
        TelegramInviteUrl = plan.TelegramInviteUrl,
        DeliveryInstructions = plan.DeliveryInstructions,
        ContentOverridesJson = plan.ContentOverridesJson,
    };

    private static BillingAddOnVersion CreateBillingAddOnVersion(BillingAddOn addOn, int versionNumber, string? adminId, string? adminName, DateTimeOffset createdAt) => new()
    {
        Id = GenerateDomainId("addon-version"),
        AddOnId = addOn.Id,
        VersionNumber = versionNumber,
        Code = addOn.Code,
        Name = addOn.Name,
        Description = addOn.Description,
        Price = addOn.Price,
        Currency = addOn.Currency,
        Interval = addOn.Interval,
        Status = addOn.Status,
        IsRecurring = addOn.IsRecurring,
        DurationDays = addOn.DurationDays,
        GrantCredits = addOn.GrantCredits,
        GrantEntitlementsJson = addOn.GrantEntitlementsJson,
        CompatiblePlanCodesJson = addOn.CompatiblePlanCodesJson,
        AppliesToAllPlans = addOn.AppliesToAllPlans,
        IsStackable = addOn.IsStackable,
        QuantityStep = addOn.QuantityStep,
        MaxQuantity = addOn.MaxQuantity,
        DisplayOrder = addOn.DisplayOrder,
        CreatedByAdminId = adminId,
        CreatedByAdminName = adminName,
        CreatedAt = createdAt,
        // OET 2026 catalog fields
        OriginalPriceGbp = addOn.OriginalPriceGbp,
        AddonKind = addOn.AddonKind,
        RequiresEligibleParent = addOn.RequiresEligibleParent,
        EligibilityFlag = addOn.EligibilityFlag,
        LettersGranted = addOn.LettersGranted,
        SessionsGranted = addOn.SessionsGranted,
        AiPackageGroup = addOn.AiPackageGroup,
        AiFeaturesJson = addOn.AiFeaturesJson,
    };

    private static BillingCouponVersion CreateBillingCouponVersion(BillingCoupon coupon, int versionNumber, string? adminId, string? adminName, DateTimeOffset createdAt) => new()
    {
        Id = GenerateDomainId("coupon-version"),
        CouponId = coupon.Id,
        VersionNumber = versionNumber,
        Code = coupon.Code,
        Name = coupon.Name,
        Description = coupon.Description,
        DiscountType = coupon.DiscountType,
        DiscountValue = coupon.DiscountValue,
        Currency = coupon.Currency,
        Status = coupon.Status,
        StartsAt = coupon.StartsAt,
        EndsAt = coupon.EndsAt,
        UsageLimitTotal = coupon.UsageLimitTotal,
        UsageLimitPerUser = coupon.UsageLimitPerUser,
        MinimumSubtotal = coupon.MinimumSubtotal,
        ApplicablePlanCodesJson = coupon.ApplicablePlanCodesJson,
        ApplicableAddOnCodesJson = coupon.ApplicableAddOnCodesJson,
        IsStackable = coupon.IsStackable,
        Notes = coupon.Notes,
        CreatedByAdminId = adminId,
        CreatedByAdminName = adminName,
        CreatedAt = createdAt
    };

    private async Task<int> EnsureBillingPlanVersionBaselineAsync(BillingPlan plan, string adminId, string adminName, DateTimeOffset now, CancellationToken ct)
    {
        var latestVersionNumber = await db.BillingPlanVersions
            .Where(version => version.PlanId == plan.Id)
            .Select(version => (int?)version.VersionNumber)
            .MaxAsync(ct) ?? 0;
        if (latestVersionNumber > 0)
        {
            return latestVersionNumber;
        }

        var createdAt = plan.CreatedAt == default ? now : plan.CreatedAt;
        var baseline = CreateBillingPlanVersion(plan, 1, adminId, adminName, createdAt);
        db.BillingPlanVersions.Add(baseline);
        plan.ActiveVersionId = baseline.Id;
        plan.LatestVersionId = baseline.Id;
        return 1;
    }

    private async Task<int> EnsureBillingAddOnVersionBaselineAsync(BillingAddOn addOn, string adminId, string adminName, DateTimeOffset now, CancellationToken ct)
    {
        var latestVersionNumber = await db.BillingAddOnVersions
            .Where(version => version.AddOnId == addOn.Id)
            .Select(version => (int?)version.VersionNumber)
            .MaxAsync(ct) ?? 0;
        if (latestVersionNumber > 0)
        {
            return latestVersionNumber;
        }

        var createdAt = addOn.CreatedAt == default ? now : addOn.CreatedAt;
        var baseline = CreateBillingAddOnVersion(addOn, 1, adminId, adminName, createdAt);
        db.BillingAddOnVersions.Add(baseline);
        addOn.ActiveVersionId = baseline.Id;
        addOn.LatestVersionId = baseline.Id;
        return 1;
    }

    private async Task<int> EnsureBillingCouponVersionBaselineAsync(BillingCoupon coupon, string adminId, string adminName, DateTimeOffset now, CancellationToken ct)
    {
        var latestVersionNumber = await db.BillingCouponVersions
            .Where(version => version.CouponId == coupon.Id)
            .Select(version => (int?)version.VersionNumber)
            .MaxAsync(ct) ?? 0;
        if (latestVersionNumber > 0)
        {
            return latestVersionNumber;
        }

        var createdAt = coupon.CreatedAt == default ? now : coupon.CreatedAt;
        var baseline = CreateBillingCouponVersion(coupon, 1, adminId, adminName, createdAt);
        db.BillingCouponVersions.Add(baseline);
        coupon.ActiveVersionId = baseline.Id;
        coupon.LatestVersionId = baseline.Id;
        return 1;
    }
}
