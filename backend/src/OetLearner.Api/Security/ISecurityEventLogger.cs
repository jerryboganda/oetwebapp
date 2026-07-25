namespace OetLearner.Api.Security;

/// <summary>
/// Appends a <see cref="OetLearner.Api.Domain.SecurityEvent"/> row. Implementations
/// must never throw — a logging failure must not break the auth/session/playback
/// flow that triggered it (see <see cref="SecurityEventLogger"/> for why it uses
/// its own DB scope rather than the caller's tracked <c>DbContext</c>).
/// </summary>
public interface ISecurityEventLogger
{
    /// <summary>
    /// Logs a security event. <paramref name="kind"/> should be one of
    /// <c>OetLearner.Api.Domain.SecurityEventKinds</c>'s constants.
    /// </summary>
    /// <param name="authAccountId">The account the event concerns, when known.</param>
    /// <param name="kind">One of <c>SecurityEventKinds</c>.</param>
    /// <param name="sessionFamilyId">The refresh-token family (session identity
    /// that survives rotation), when the event is session-scoped.</param>
    /// <param name="deviceId">The client-supplied device id, when known.</param>
    /// <param name="details">Optional payload, serialized to JSON.</param>
    /// <param name="severity">Overrides <c>SecurityEventKinds.DefaultSeverity(kind)</c> when supplied.</param>
    Task TryLogAsync(
        string? authAccountId,
        string kind,
        Guid? sessionFamilyId = null,
        string? deviceId = null,
        object? details = null,
        string? severity = null,
        CancellationToken cancellationToken = default);
}
