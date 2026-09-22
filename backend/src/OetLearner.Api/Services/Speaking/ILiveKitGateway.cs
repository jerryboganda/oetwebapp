namespace OetLearner.Api.Services.Speaking;

/// <summary>
/// Abstraction over the LiveKit (or compatible) realtime gateway used by
/// Phase 3 of the OET Speaking module to host live-tutor rooms.
///
/// Production uses <see cref="LiveKitCloudGateway"/>. The stub is limited
/// to development and test environments, while an unconfigured production
/// deployment fails closed through <see cref="LiveKitProviderUnavailable"/>.
/// </summary>
public interface ILiveKitGateway
{
    /// <summary>Provision a new room on the provider and return its
    /// stable identifiers. May be called more than once for the same
    /// <paramref name="roomName"/>; implementations are responsible for
    /// being idempotent.</summary>
    Task<LiveKitRoomCreationResult> CreateRoomAsync(string roomName, int maxDurationSeconds, CancellationToken ct);

    /// <summary>Mint a short-lived access token for a participant. The
    /// token encodes the participant's <paramref name="identity"/> and
    /// the capabilities it is allowed to exercise inside the room.</summary>
    Task<string> MintAccessTokenAsync(
        string roomName,
        string identity,
        LiveKitTokenCapabilities caps,
        TimeSpan ttl,
        CancellationToken ct);

    /// <summary>Start a track-composite egress on the given room. Returns
    /// the provider's egress identifier, which the backend persists so
    /// follow-up webhooks can be reconciled back to the room.</summary>
    Task<string> StartEgressAsync(string roomName, string outputUrl, CancellationToken ct);

    /// <summary>Stop an active egress. Returns <c>true</c> if the
    /// provider confirms the egress is no longer running.</summary>
    Task<bool> StopEgressAsync(string egressId, CancellationToken ct);

    /// <summary>Close and remove a provider room after the persisted room
    /// reaches its terminal state.</summary>
    Task DeleteRoomAsync(string roomName, CancellationToken ct);

    /// <summary>Verify the signature attached to an inbound webhook
    /// payload. Implementations MUST use a constant-time comparison.
    /// Returns <c>false</c> when the signature is missing, malformed, or
    /// does not match.</summary>
    bool VerifyWebhookSignature(string payload, string signature);
}

/// <summary>Identifiers returned by the provider when a room is
/// created. <see cref="WssUrl"/> is echoed back to clients alongside
/// access tokens so the frontend SDK knows where to connect.</summary>
public sealed record LiveKitRoomCreationResult(string RoomSid, string WssUrl);

/// <summary>Per-participant capability flags carried inside the
/// access token. The backend computes these from the room role
/// (Learner / Tutor / Observer) so the provider cannot be tricked into
/// publishing media on behalf of an observer.</summary>
public sealed record LiveKitTokenCapabilities(
    bool CanPublishAudio,
    bool CanPublishVideo,
    bool CanSubscribe,
    bool CanManageRoom = false);
