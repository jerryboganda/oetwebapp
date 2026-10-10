namespace Fleet.Manager.Configuration;

/// <summary>
/// Everything configurable about the manager, bound from the <c>Fleet</c> configuration section
/// (environment form: <c>Fleet__Data__Directory</c>). Secrets are NEVER configuration values: they
/// are files under <see cref="SecretsOptions.Directory"/> (compose <c>secrets:</c>).
/// All reads go through <c>IOptions</c> so nothing is captured before the host is fully configured.
/// </summary>
public sealed class FleetOptions
{
    public const string SectionName = "Fleet";

    public DataOptions Data { get; set; } = new();

    public SecretsOptions Secrets { get; set; } = new();

    public ApiOptions Api { get; set; } = new();

    public BindingOptions Binding { get; set; } = new();

    public ProvisioningOptions Provisioning { get; set; } = new();

    public InventoryOptions Inventory { get; set; } = new();

    public ImageOptions Image { get; set; } = new();

    public TimingOptions Timing { get; set; } = new();

    public AuthOptions Auth { get; set; } = new();

    public AuditOptions Audit { get; set; } = new();

    public WorkerOptions Workers { get; set; } = new();

    public UbagOptions Ubag { get; set; } = new();
}

public sealed class DataOptions
{
    /// <summary>Directory of the SQLite file; the compose project mounts the external volume <c>oet-fleet_fleet_data</c> here.</summary>
    public string Directory { get; set; } = "/data";

    public string DatabaseFileName { get; set; } = "fleet.db";

    public string DatabasePath => Path.Combine(Directory, DatabaseFileName);
}

public sealed class SecretsOptions
{
    public string Directory { get; set; } = "/run/secrets";

    /// <summary>The fleet-service credential (<c>ofs1_...</c>) minted by the owner-gated API endpoint.</summary>
    public string ApiCredentialFile { get; set; } = "fleet_api_credential";

    /// <summary>Optional bearer for <c>POST /internal/sync</c> (the CI sync job). Absent file = the endpoint is disabled.</summary>
    public string SyncTokenFile { get; set; } = "fleet_sync_token";

    /// <summary>Optional bearer for <c>GET /metrics</c> from a scraper. Absent file = only an owner session may read metrics.</summary>
    public string MetricsTokenFile { get; set; } = "fleet_metrics_token";

    /// <summary>The UBAG trust-plane CA certificate (PEM). Absent file = the trust plane cannot start and nothing is issued or published.</summary>
    public string CaCertFile { get; set; } = "fleet_ca_cert";

    /// <summary>The UBAG trust-plane CA private key (PEM, ECDSA P-256). Same lifecycle as the certificate file.</summary>
    public string CaKeyFile { get; set; } = "fleet_ca_key";
}

public sealed class ApiOptions
{
    public string BaseUrl { get; set; } = "https://api.oetwithdrhesham.co.uk";

    /// <summary>The <c>X-Remote-Protocol</c> number this manager speaks on the service plane.</summary>
    public int ProtocolVersion { get; set; } = 1;

    public int TimeoutSeconds { get; set; } = 30;
}

public sealed class BindingOptions
{
    public const string LoopbackMode = "loopback";
    public const string ContainerMode = "container";

    /// <summary>
    /// <c>loopback</c>: every listen address must be a loopback IP (bare-metal / dev).
    /// <c>container</c>: the process may listen on 0.0.0.0 inside its own network namespace because the
    /// compose file publishes the port on <c>127.0.0.1</c> of the host only (the container is on no
    /// production network). Anything else fails startup.
    /// </summary>
    public string Mode { get; set; } = LoopbackMode;

    /// <summary>Used only when neither <c>urls</c> nor <c>ASPNETCORE_URLS</c> is set.</summary>
    public string Urls { get; set; } = "http://127.0.0.1:8080";
}

public sealed class ProvisioningOptions
{
    public string AnsibleDirectory { get; set; } = "/opt/fleet/ansible";

    public string AnsiblePlaybookExecutable { get; set; } = "ansible-playbook";

    public string SshExecutable { get; set; } = "ssh";

    public string SshKeygenExecutable { get; set; } = "ssh-keygen";

    public string SshKeyscanExecutable { get; set; } = "ssh-keyscan";

    /// <summary>A tmpfs directory: per-run key files, known_hosts, vars and results live here and are deleted after each run.</summary>
    public string ScratchDirectory { get; set; } = "/tmp/fleet";

    /// <summary>Ansible forks per run (clamped to 1..5). One target host per run, so this only bounds worker processes.</summary>
    public int Forks { get; set; } = 2;

    public int StepTimeoutSeconds { get; set; } = 900;

    public int PullTimeoutSeconds { get; set; } = 900;

    public int CtlTimeoutSeconds { get; set; } = 90;

    public int KeyscanTimeoutSeconds { get; set; } = 20;

    /// <summary><c>distro</c> (default, docker.io from the OS) or <c>vendor</c> (Docker's apt repository with a pinned key fingerprint).</summary>
    public string DockerSource { get; set; } = "distro";

    /// <summary>Optional exact package version pin handed to the docker playbook.</summary>
    public string DockerVersionPin { get; set; } = string.Empty;

    public int ClampedForks => Math.Clamp(Forks, 1, 5);
}

public sealed class InventoryOptions
{
    /// <summary>Extra addresses of the manager host itself (interface addresses are detected automatically).</summary>
    public string[] OwnAddresses { get; set; } = Array.Empty<string>();

    /// <summary>Additional addresses that may never be helpers (the primary 185.252.233.186 is always forbidden).</summary>
    public string[] ForbiddenAddresses { get; set; } = Array.Empty<string>();
}

public sealed class ImageOptions
{
    public const string ScopedTokenMode = "scoped-token";
    public const string PublicMode = "public";

    /// <summary><c>scoped-token</c> (mode A, default): the CI sync job supplies a per-run registry token. <c>public</c> (mode B): the agent package is public.</summary>
    public string Mode { get; set; } = ScopedTokenMode;

    /// <summary>Digest approval is manual unless this is true.</summary>
    public bool AutoApproveDigests { get; set; }

    public string MinAgentVersion { get; set; } = "1.0.0";
}

public sealed class TimingOptions
{
    public int NodePollSeconds { get; set; } = 15;

    public int HostStatusPollMinutes { get; set; } = 5;

    public int StepPollSeconds { get; set; } = 5;

    public int VerifyTimeoutSeconds { get; set; } = 180;

    public int CanaryTimeoutSeconds { get; set; } = 300;

    public int DrainWaitSeconds { get; set; } = 300;

    public int HeartbeatAfterRestartSeconds { get; set; } = 120;

    public int OwnerCredentialMinutes { get; set; } = 60;

    public int RolloutTokenMinutes { get; set; } = 60;

    public int TokenRotateAfterDays { get; set; } = 14;

    public int RotationGraceSeconds { get; set; } = 3600;

    public int TokenTtlDays { get; set; } = 30;

    public int WorkerIdleSeconds { get; set; } = 2;

    public int PlacementReservationSeconds { get; set; } = 45;
}

public sealed class AuthOptions
{
    public int SessionIdleMinutes { get; set; } = 20;

    public int SessionAbsoluteMinutes { get; set; } = 60;

    public int LockoutAttempts { get; set; } = 5;

    public int LockoutMinutes { get; set; } = 15;

    public int LoginRatePerMinute { get; set; } = 10;

    public int PasswordIterations { get; set; } = 310_000;

    public string TotpIssuer { get; set; } = "OET Fleet Manager";
}

public sealed class AuditOptions
{
    /// <summary>
    /// When false (default) a broken audit or operations hash chain stops startup (tamper evidence).
    /// Set to true only to boot the console for a forensic look.
    /// </summary>
    public bool AllowBrokenChain { get; set; }
}

public sealed class WorkerOptions
{
    /// <summary>Starts the operation worker, node monitor and token-rotation scheduler. Tests turn this off.</summary>
    public bool Enabled { get; set; } = true;
}

/// <summary>
/// The UBAG project integration (the shared manager's second consumer, OET first): a bearer-authenticated
/// <c>GET /internal/ubag/allocations</c> that publishes, per opted-in host, the capacity UBAG may use AFTER
/// OET's own policy budget is deducted, in the allocation_list v1 schema UBAG's gateway polls
/// (<c>UBAG_FLEET_MANAGER_URL</c>). Everything is off until the owner turns it on: no token file means 404,
/// and an empty <see cref="Hosts"/> list means no host is ever published.
/// </summary>
public sealed class UbagOptions
{
    public string PrimaryTlsDirectory { get; set; } = string.Empty;
    public string PrimaryId { get; set; } = "ubag-primary";
    public string HelperImageDigest { get; set; } = string.Empty;
    public string HelperWorkloadVersion { get; set; } = string.Empty;
    public string PrimaryWireguardPublicKey { get; set; } = string.Empty;
    public string PrimaryWireguardEndpoint { get; set; } = string.Empty;
    public string PrimaryWireguardAddress { get; set; } = "10.253.240.1";
    public Dictionary<string, string> HelperWireguardAddresses { get; set; } = new(StringComparer.Ordinal);
    public string WireguardPeersDirectory { get; set; } = string.Empty;
    /// <summary>Master switch. False: the endpoint answers 503 and nothing is ever published.</summary>
    public bool Enabled { get; set; }

    /// <summary>Bearer file under the secrets directory. Absent = the endpoint is disabled (404), like the sync and metrics tokens.</summary>
    public string TokenFile { get; set; } = "fleet_ubag_token";

    /// <summary>
    /// Host ids eligible for UBAG work, comma-separated, or "*" for every Active host with known
    /// hardware. Empty publishes nothing. A scalar string ON PURPOSE: the configuration binder does
    /// not bind a bare environment value (or a JSON value form) to a string[] — only indexed
    /// <c>__0</c> keys do — and a silently-empty array was observed publishing nothing with the
    /// owner's setting in place. Parsed by <see cref="Projects.UbagAllocationService.HostList"/>.
    /// </summary>
    public string Hosts { get; set; } = string.Empty;

    /// <summary>
    /// The helper's dialable <c>host:port</c> template for the mTLS gRPC server UBAG dials (no WireGuard exists;
    /// decision D3 keeps every plane on plain TCP/IP). <c>{address}</c> is the enrolled host address. Per-host
    /// overrides live in <see cref="EndpointOverrides"/>.
    /// </summary>
    public string EndpointTemplate { get; set; } = "{address}:7443";

    public Dictionary<string, string> EndpointOverrides { get; set; } = new(StringComparer.Ordinal);

    /// <summary>
    /// The trust plane (UBAG decision D3). False: no CA identity is issued or published — <c>cert_identity.spki_sha256</c>
    /// stays empty, the helper is never provisioned with a node certificate, and UBAG's prober keeps the node unusable.
    /// True: the manager requires the CA files, issues one leaf certificate per host (URI SAN
    /// <c>spiffe://ubag/node/&lt;node_id&gt;</c>), renders it onto the helper during S10, and publishes the SPKI pin.
    /// </summary>
    public bool TrustEnabled { get; set; }

    /// <summary>Initial per-node browser workload ceiling (the plan's "start each qualified helper at one; raise after measurement"). 0..64.</summary>
    public int MaxBrowserWorkloads { get; set; } = 1;

    /// <summary>How long a published grant stays valid; UBAG re-polls well inside it. 1..1440 minutes.</summary>
    public int GrantTtlMinutes { get; set; } = 30;

    public int ClampedMaxBrowserWorkloads => Math.Clamp(MaxBrowserWorkloads, 0, 64);

    public int ClampedGrantTtlMinutes => Math.Clamp(GrantTtlMinutes, 1, 1440);
}
