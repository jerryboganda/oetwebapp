namespace Fleet.Core.Domain;

/// <summary>
/// The stable failure-reason strings of OET-RWP/1 section 8.1. An operation can only fail
/// with a member of <see cref="All"/>; free text goes into the (sanitized, capped) detail.
/// </summary>
public static class FailureReasons
{
    public const string InventoryInvalid = "inventory_invalid";
    public const string ForbiddenHost = "forbidden_host";
    public const string HostKeyUnreachable = "host_key_unreachable";
    public const string HostKeyMismatch = "host_key_mismatch";
    public const string HostKeyChanged = "host_key_changed";
    public const string SshUnreachable = "ssh_unreachable";
    public const string AuthFailed = "auth_failed";
    public const string OwnerCredentialExpired = "owner_credential_expired";
    public const string PreflightRejected = "preflight_rejected";
    public const string BootstrapStepFailed = "bootstrap_step_failed";
    public const string LockoutRisk = "lockout_risk";
    public const string OwnerKeyDiscardFailed = "owner_key_discard_failed";
    public const string ApiUnreachable = "api_unreachable";
    public const string ApiRegisterFailed = "api_register_failed";
    public const string TokenRenderFailed = "token_render_failed";
    public const string ImagePullFailed = "image_pull_failed";
    public const string ImageDigestUnapproved = "image_digest_unapproved";
    public const string ImageIdMismatch = "image_id_mismatch";
    public const string AgentStartFailed = "agent_start_failed";
    public const string AgentNotHeartbeating = "agent_not_heartbeating";
    public const string ProtocolUnsupported = "protocol_unsupported";
    public const string DigestNotApprovedByApi = "digest_not_approved_by_api";
    public const string CanaryTimeout = "canary_timeout";
    public const string CanaryMismatch = "canary_mismatch";
    public const string DrainTimeout = "drain_timeout";
    public const string InternalError = "internal_error";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        InventoryInvalid, ForbiddenHost, HostKeyUnreachable, HostKeyMismatch, HostKeyChanged,
        SshUnreachable, AuthFailed, OwnerCredentialExpired, PreflightRejected, BootstrapStepFailed,
        LockoutRisk, OwnerKeyDiscardFailed, ApiUnreachable, ApiRegisterFailed, TokenRenderFailed,
        ImagePullFailed, ImageDigestUnapproved, ImageIdMismatch, AgentStartFailed,
        AgentNotHeartbeating, ProtocolUnsupported, DigestNotApprovedByApi, CanaryTimeout,
        CanaryMismatch, DrainTimeout, InternalError,
    };

    /// <summary>The allowed <c>detail</c> values of <see cref="PreflightRejected"/> (section 8.1).</summary>
    public static readonly IReadOnlySet<string> PreflightDetails = new HashSet<string>(StringComparer.Ordinal)
    {
        "os_unsupported", "arch_unsupported", "cpu_below_min", "mem_below_min", "disk_below_min",
        "time_skew", "existing_oet_workload", "no_systemd",
    };

    public static bool IsKnown(string? reason) => reason is not null && All.Contains(reason);

    /// <summary>Returns <paramref name="reason"/> when it is a known reason, else <see cref="InternalError"/>.</summary>
    public static string Normalize(string? reason) => IsKnown(reason) ? reason! : InternalError;
}
