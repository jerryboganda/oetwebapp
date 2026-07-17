using System.Text.Json;
using OetWithDrHesham.Api.Services.AiTools;

namespace OetWithDrHesham.Api.Services.AiAssistant.Safety;

/// <summary>
/// Pre-execution safety checks for all mutation tools.
/// Validates role, rate limits, path safety, and content scanning.
/// </summary>
public interface ISafetyGuard
{
    Task<SafetyCheckResult> CheckAsync(string toolCode, JsonElement args, AiToolContext ctx, CancellationToken ct);
}

public sealed record SafetyCheckResult(bool IsAllowed, string? DenialReason, SafetyRiskLevel RiskLevel);

public enum SafetyRiskLevel
{
    None = 0,
    Low = 1,
    Medium = 2,
    High = 3,
    Critical = 4,
}
