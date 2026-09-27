using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;

namespace OetLearner.Api.Services.OwnerAgent;

public sealed record OwnerAgentAvailability(bool IsAvailable, string? DisabledReason)
{
    public static readonly OwnerAgentAvailability Available = new(true, null);
}

public interface IOwnerAgentFeatureGate
{
    /// <summary>
    /// Env switch (<c>OwnerAgent:Enabled</c> + a usable sidecar config) AND the
    /// <c>owner_agent_console</c> feature flag. Read uncached on every call so the
    /// flag works as an immediate kill switch from <c>/admin/flags</c>.
    /// </summary>
    Task<OwnerAgentAvailability> GetAvailabilityAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Kill switch for the Owner Agent Console, backed by the existing
/// <c>FeatureFlags</c> table (same fail-closed contract as
/// <c>Services/Companion/CompanionFeatureFlags.cs</c>): a missing row, an
/// unreadable database or a disabled row all mean "off", and every console route
/// answers 503.
/// </summary>
public sealed class OwnerAgentFeatureGate(
    LearnerDbContext db,
    IOptions<OwnerAgentOptions> options,
    ILogger<OwnerAgentFeatureGate> logger) : IOwnerAgentFeatureGate
{
    public const string FlagKey = "owner_agent_console";
    public const string ReasonNotConfigured = "not_configured";
    public const string ReasonFlagOff = "flag_off";

    public async Task<OwnerAgentAvailability> GetAvailabilityAsync(CancellationToken cancellationToken)
    {
        if (!options.Value.IsSidecarConfigured)
        {
            return new OwnerAgentAvailability(false, ReasonNotConfigured);
        }

        try
        {
            // Newest row wins if a key was ever duplicated (mirrors CompanionFeatureFlags).
            var flag = await db.FeatureFlags
                .AsNoTracking()
                .Where(f => f.Key == FlagKey)
                .OrderByDescending(f => f.UpdatedAt)
                .Select(f => new { f.Enabled })
                .FirstOrDefaultAsync(cancellationToken);

            return flag?.Enabled == true
                ? OwnerAgentAvailability.Available
                : new OwnerAgentAvailability(false, ReasonFlagOff);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Owner agent feature flag '{Key}' could not be read; failing closed.", FlagKey);
            return new OwnerAgentAvailability(false, ReasonFlagOff);
        }
    }
}
