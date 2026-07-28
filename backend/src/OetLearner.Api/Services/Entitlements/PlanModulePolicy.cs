using System.Text.Json;

namespace OetLearner.Api.Services.Entitlements;

/// <summary>
/// Owner-approved plan module policy. Mock exams are separate-purchase credit packs
/// and can never be granted by a course plan. Recalls are limited to the products
/// whose pricing-list dashboard explicitly includes the Recalls module.
/// </summary>
public static class PlanModulePolicy
{
    private static readonly HashSet<string> RecallPlanCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "full-condensed-medicine",
        "full-condensed-medicine-tbook",
        "full-nursing",
        "full-nursing-assessment",
        "full-nursing-premium",
        "full-pharmacy",
        "full-physiotherapy",
        "full-allied-health",
        "crash-course",
        "crash-3letters",
        "crash-5letters",
        "listening-recalls",
    };

    public static bool AllowsRecalls(string? planCode)
        => !string.IsNullOrWhiteSpace(planCode) && RecallPlanCodes.Contains(planCode);

    public static string NormalizeDashboardModulesJson(string? planCode, string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "[]";

        try
        {
            using var document = JsonDocument.Parse(raw);
            if (document.RootElement.ValueKind != JsonValueKind.Array) return "[]";

            var modules = document.RootElement
                .EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString()?.Trim())
                .Where(module => !string.IsNullOrWhiteSpace(module))
                .Select(module => module!)
                .Where(module => !string.Equals(module, ModuleKeys.Mocks, StringComparison.OrdinalIgnoreCase))
                .Where(module => AllowsRecalls(planCode)
                    || !string.Equals(module, ModuleKeys.Recalls, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return JsonSerializer.Serialize(modules);
        }
        catch (JsonException)
        {
            return "[]";
        }
    }
}
