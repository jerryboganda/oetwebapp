using OetLearner.Api.Contracts.Classes;
using OetLearner.Api.Domain.Classes;

namespace OetLearner.Api.Services.Classes;

/// <summary>
/// Tutor profile + availability + Zoom user provisioning for the
/// Zoom-backed live classes module. See OET_ZOOM_INTEGRATION_PLAN.md §7–§9.
/// </summary>
public interface ITutorService
{
    Task<TutorProfileDto> GetByUserIdAsync(string userId, CancellationToken ct);

    Task<TutorProfileDto> CreateAsync(string userId, TutorUpsertRequest request, CancellationToken ct);

    Task<TutorProfileDto> UpdateAsync(string userId, TutorUpsertRequest request, CancellationToken ct);

    Task<IReadOnlyList<TutorAvailabilityDto>> GetAvailabilityAsync(string userId, CancellationToken ct);

    Task<IReadOnlyList<TutorAvailabilityDto>> ReplaceAvailabilityAsync(string userId, IReadOnlyList<TutorAvailabilityUpsertRequest> slots, CancellationToken ct);

    Task<TutorEarningsDto> GetEarningsAsync(string userId, DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct);

    /// <summary>
    /// Provision the Zoom user identifier for the tutor. v2 stub: returns the
    /// platform default host user id from runtime settings. Eventually this
    /// should call Zoom's user provisioning API. See plan §6.4.
    /// </summary>
    Task<string?> ProvisionZoomUserAsync(string userId, CancellationToken ct);

    Task<Tutor> EnsureTutorByUserIdAsync(string userId, CancellationToken ct);
}
