namespace OetLearner.Api.Services.OwnerAgent;

// Browser-facing request/response shapes for /v1/owner-agent (CONTRACT.md §5).
// Request records are nullable end-to-end so a missing field is a clean 400 from
// the endpoint's own validation instead of a model-binding failure. Sidecar
// response bodies are passed through verbatim (except GitHub token status, which
// is re-shaped so a token can never be echoed).

public sealed record OwnerAgentUnlockRequest(string? Password, string? Code);

public sealed record OwnerAgentLeaseRequest(DateTimeOffset? ExpiresAt);

public sealed record OwnerAgentConnectCodeRequest(string? FlowId, string? Code);

public sealed record OwnerAgentFlowRequest(string? FlowId);

public sealed record OwnerAgentGithubTokensRequest(string? AgentToken, string? ShipToken);

public sealed record OwnerAgentCreateSessionRequest(
    string? Engine,
    string? Model,
    string? Effort,
    string? Mode,
    string? Title,
    string? InitialMessage);

public sealed record OwnerAgentUpdateSessionRequest(
    string? Title,
    string? Mode,
    string? Model,
    string? Effort,
    bool? Archived);

public sealed record OwnerAgentSendMessageRequest(string? Text, string? Model, string? Effort);

public sealed record OwnerAgentHandoffRequest(string? Engine, string? Model, string? Effort);

public sealed record OwnerAgentApprovalDecisionRequest(string? Decision, string? Nonce, string? Note);

public sealed record OwnerAgentShipRequest(string? PrTitle, string? PrBody);

public sealed record OwnerAgentMeResponse(
    bool IsOwner,
    bool Unlocked,
    DateTimeOffset? UnlockExpiresAt,
    DateTimeOffset? AbsoluteExpiresAt,
    bool FeatureEnabled,
    DateTimeOffset? UnlockBlockedUntil);

/// <summary>
/// Unlock / refresh result. The browser authenticates with the HttpOnly <c>oet_owner_unlock</c>
/// cookie set on the same response; <see cref="Ticket"/> is kept only for non-browser callers
/// and back-compat (the admin UI does not read it). <see cref="ExpiresAt"/> equals
/// <see cref="AbsoluteExpiresAt"/>: the unlock lifetime is fixed, never extended.
/// </summary>
public sealed record OwnerAgentUnlockResponse(string Ticket, DateTimeOffset ExpiresAt, DateTimeOffset AbsoluteExpiresAt);

public sealed record OwnerAgentGithubStatusResponse(bool AgentTokenSet, bool ShipTokenSet, string? Login);

public sealed record OwnerAgentApplyUpdateResponse(bool Draining, int? ActiveTurns, bool Dispatched, string Instructions);

/// Owner allow-list entry for History "started by" (GET /v1/owner-agent/owners).
public sealed record OwnerAgentOwnerDto(string AccountId, string Email);
