using System.Text.Json;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Entitlements;

internal sealed record AiQuotaPlanMapping(string? Code, string Source);

internal static class AiQuotaPlanMappingResolver
{
    public static AiQuotaPlanMapping? Resolve(BillingPlan? plan)
    {
        var explicitMapping = TryReadExplicitAiQuotaPlanCode(plan?.EntitlementsJson);
        if (explicitMapping is not null)
        {
            return explicitMapping;
        }

        var fallbackCode = plan?.Code?.Trim().ToLowerInvariant() switch
        {
            "basic-monthly" => "starter",
            "premium-monthly" => "pro",
            "premium-yearly" => "pro",
            "intensive-monthly" => "pro",
            "legacy-trial" => "free",
            _ => null,
        };

        return string.IsNullOrWhiteSpace(fallbackCode)
            ? null
            : new AiQuotaPlanMapping(fallbackCode, "fallback");
    }

    public static string? NormalizeCode(string? code)
        => string.IsNullOrWhiteSpace(code) ? null : code.Trim().ToLowerInvariant();

    private static AiQuotaPlanMapping? TryReadExplicitAiQuotaPlanCode(string? entitlementsJson)
    {
        if (string.IsNullOrWhiteSpace(entitlementsJson))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(entitlementsJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("ai", out var ai))
            {
                return null;
            }

            if (ai.ValueKind != JsonValueKind.Object)
            {
                return new AiQuotaPlanMapping(null, "explicit-invalid");
            }

            if (!ai.TryGetProperty("quotaPlanCode", out var quotaPlanCode))
            {
                return null;
            }

            if (quotaPlanCode.ValueKind != JsonValueKind.String)
            {
                return new AiQuotaPlanMapping(null, "explicit-invalid");
            }

            var code = NormalizeCode(quotaPlanCode.GetString());
            return string.IsNullOrWhiteSpace(code)
                ? new AiQuotaPlanMapping(null, "explicit-invalid")
                : new AiQuotaPlanMapping(code, "explicit");
        }
        catch (JsonException)
        {
            return entitlementsJson.Contains("quotaPlanCode", StringComparison.OrdinalIgnoreCase)
                ? new AiQuotaPlanMapping(null, "explicit-invalid")
                : null;
        }
    }
}
