using System.Globalization;
using System.Text.Json;
using Fleet.Core.Audit;
using Fleet.Core.Crypto;
using Fleet.Core.Domain;
using Fleet.Core.Validation;
using Fleet.Manager.Api;
using Fleet.Manager.Configuration;
using Fleet.Manager.Infrastructure;
using Fleet.Manager.Persistence;
using Fleet.Manager.Provisioning;
using Fleet.Manager.Projects;
using Fleet.Manager.Vault;
using Microsoft.Extensions.Options;

namespace Fleet.Manager.Operations;

/// <summary>What a step needs to know: the operation, its host and the operation's working data.</summary>
public sealed class StepContext
{
    public StepContext(OperationEntity op, HostEntity? host, OperationData data)
    {
        Op = op;
        Host = host;
        Data = data;
    }

    public OperationEntity Op { get; }

    public HostEntity? Host { get; set; }

    public OperationData Data { get; }

    public HostEntity RequireHost() => Host ?? throw new InvalidOperationException("This operation has no host.");
}

/// <summary>
/// The concrete work of every step. A step MUST first check its success predicate and skip itself when
/// it already holds (OET-RWP/1 section 8.2), so re-running any step after a crash is safe. Steps return
/// a <see cref="StepOutcome"/>; they never touch operation or step state themselves (the runner does), and
/// they never let a secret escape into a summary, a detail or a log line.
/// Files: this one (shared plumbing and the dispatcher), <c>StepExecutor.Bootstrap.cs</c> (S1-S8, owner
/// credential), <c>StepExecutor.Agent.cs</c> (S9-S13), <c>StepExecutor.Maintenance.cs</c> (drain, disable,
/// enable, remove, token rotation, rollout).
/// </summary>
public sealed partial class StepExecutor
{
    private readonly HostAccess _access;
    private readonly HostStore _hosts;
    private readonly OperationStore _operations;
    private readonly CredentialStore _credentials;
    private readonly IProvisioner _provisioner;
    private readonly IFleetApi _api;
    private readonly ReleaseService _releases;
    private readonly PolicyService _policies;
    private readonly RolloutTokenHolder _rolloutToken;
    private readonly UbagTrustService _trust;
    private readonly IOptions<FleetOptions> _options;
    private readonly TimeProvider _time;
    private readonly IDelay _delay;
    private readonly IAuditService _audit;
    private readonly ISshKeyTool _keys;
    private readonly AddressGuard _guard;
    private readonly ILogger<StepExecutor> _logger;

    public StepExecutor(
        HostAccess access,
        HostStore hosts,
        OperationStore operations,
        CredentialStore credentials,
        IProvisioner provisioner,
        IFleetApi api,
        ReleaseService releases,
        PolicyService policies,
        RolloutTokenHolder rolloutToken,
        UbagTrustService trust,
        IOptions<FleetOptions> options,
        TimeProvider time,
        IDelay delay,
        IAuditService audit,
        ISshKeyTool keys,
        AddressGuard guard,
        ILogger<StepExecutor> logger)
    {
        _access = access;
        _hosts = hosts;
        _operations = operations;
        _credentials = credentials;
        _provisioner = provisioner;
        _api = api;
        _releases = releases;
        _policies = policies;
        _rolloutToken = rolloutToken;
        _trust = trust;
        _options = options;
        _time = time;
        _delay = delay;
        _audit = audit;
        _keys = keys;
        _guard = guard;
        _logger = logger;
    }

    /// <summary>Runs one enrollment step (S1..S13).</summary>
    public Task<StepOutcome> ExecuteEnrollStepAsync(StepContext ctx, EnrollStep step, CancellationToken cancellationToken) => step switch
    {
        EnrollStep.Preflight => PreflightAsync(ctx, cancellationToken),
        EnrollStep.FleetUser => RunRootStepAsync(ctx, EnrollStep.FleetUser, cancellationToken),
        EnrollStep.InstallKey => InstallKeyAsync(ctx, cancellationToken),
        EnrollStep.Docker => RunRootStepAsync(ctx, EnrollStep.Docker, cancellationToken),
        EnrollStep.Firewall => RunRootStepAsync(ctx, EnrollStep.Firewall, cancellationToken),
        EnrollStep.HostBaseline => RunRootStepAsync(ctx, EnrollStep.HostBaseline, cancellationToken),
        EnrollStep.HardenSsh => HardenSshAsync(ctx, cancellationToken),
        EnrollStep.DiscardOwnerKey => DiscardOwnerKeyAsync(ctx, cancellationToken),
        EnrollStep.Image => ImageAsync(ctx, cancellationToken),
        EnrollStep.AgentStart => AgentStartAsync(ctx, cancellationToken),
        EnrollStep.Verify => VerifyAsync(ctx, cancellationToken),
        EnrollStep.Canary => CanaryAsync(ctx, cancellationToken),
        EnrollStep.Activate => ActivateAsync(ctx, cancellationToken),
        _ => throw new ArgumentOutOfRangeException(nameof(step)),
    };

    // ---- shared plumbing -------------------------------------------------------------------

    private TimingOptions Timing => _options.Value.Timing;

    private async Task<HostEntity> FreshHostAsync(StepContext ctx, CancellationToken cancellationToken)
    {
        // A rollout has no host of its own: its steps name the target and the dispatcher stores it in ctx.Host.
        var id = ctx.Host?.Id ?? ctx.Op.HostId ?? throw new InvalidOperationException("This operation has no host.");
        var host = await _hosts.GetAsync(id, cancellationToken) ?? throw new InvalidOperationException("The host no longer exists.");
        ctx.Host = host;
        return host;
    }

    private static StepOutcome FromProvision(ProvisionResult result) =>
        StepOutcome.Fail(result.FailureReason ?? FailureReasons.InternalError, result.Detail, result.Summary);

    private static StepOutcome FromCtl(CtlResult result, string defaultReason, string summary) =>
        StepOutcome.Fail(result.FailureReason ?? defaultReason, result.Detail ?? CtlError(result.Stdout), summary);

    private static StepOutcome FromApi(FleetApiException ex, string defaultReason, string summary)
    {
        var reason = ex.Code is FleetApiException.Unreachable or FleetApiException.CredentialMissing
            ? FailureReasons.ApiUnreachable
            : defaultReason;
        return StepOutcome.Fail(reason, ex.Code + (ex.Reason is null ? string.Empty : "/" + ex.Reason), summary);
    }

    private static string? CtlError(string stdout)
    {
        var root = ParseJson(stdout);
        if (root is { ValueKind: JsonValueKind.Object } element && element.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String)
        {
            return LogScrubber.SanitizeUntrusted(error.GetString(), 300);
        }

        return null;
    }

    private static JsonElement? ParseJson(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(text);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? JsonString(JsonElement? root, params string[] path)
    {
        var current = root;
        foreach (var segment in path)
        {
            if (current is not { ValueKind: JsonValueKind.Object } element || !element.TryGetProperty(segment, out var next))
            {
                return null;
            }

            current = next;
        }

        return current is { ValueKind: JsonValueKind.String } value ? value.GetString() : null;
    }

    private static bool AgentRunning(string statusStdout, string digest)
    {
        var root = ParseJson(statusStdout);
        return JsonString(root, "agent", "state") == "running" && JsonString(root, "agent", "imageDigest") == digest;
    }

    private static string? FactString(IReadOnlyDictionary<string, object?> facts, string key) =>
        facts.TryGetValue(key, out var value) ? Convert.ToString(value, CultureInfo.InvariantCulture) : null;

    private static int? FactInt(IReadOnlyDictionary<string, object?> facts, string key)
    {
        if (!facts.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        try
        {
            return Convert.ToInt32(value, CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
        {
            return null;
        }
    }

    /// <summary>Polls the API node until <paramref name="predicate"/> holds or <paramref name="timeout"/> passes (the injected clock and delay make tests instant).</summary>
    private async Task<(bool Satisfied, ApiNode? Last)> PollNodeAsync(
        string nodeId,
        TimeSpan timeout,
        Func<ApiNode, bool> predicate,
        CancellationToken cancellationToken)
    {
        var deadline = _time.GetUtcNow() + timeout;
        ApiNode? last = null;
        while (true)
        {
            try
            {
                last = await _api.GetNodeAsync(nodeId, cancellationToken) ?? last;
            }
            catch (FleetApiException ex) when (ex.Retryable || ex.Code == FleetApiException.Unreachable)
            {
                // A transient API problem must not fail the step; keep waiting until the deadline.
            }

            if (last is not null && predicate(last))
            {
                return (true, last);
            }

            if (_time.GetUtcNow() >= deadline)
            {
                return (false, last);
            }

            await _delay.DelayAsync(TimeSpan.FromSeconds(Math.Max(1, Timing.StepPollSeconds)), cancellationToken);
        }
    }

    private static string TokenFingerprint(string tokenValue)
    {
        var secret = tokenValue[(tokenValue.LastIndexOf('_') + 1)..];
        return VaultCipher.FingerprintHint(System.Text.Encoding.UTF8.GetBytes(secret));
    }

    private Dictionary<string, string> BuildAgentEnv(string nodeId, string tokenValue, string digest, Fleet.Core.Policy.NodePolicy policy)
    {
        var env = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["OET_API_BASE"] = _options.Value.Api.BaseUrl.TrimEnd('/'),
            ["OET_NODE_ID"] = nodeId,
            ["OET_NODE_TOKEN"] = tokenValue,
            ["OET_AGENT_IMAGE_DIGEST"] = digest,
            ["OET_BUDGET_CPU_MILLI"] = policy.Budgets.CpuMilli.ToString(CultureInfo.InvariantCulture),
            ["OET_BUDGET_MEM_MIB"] = policy.Budgets.MemMiB.ToString(CultureInfo.InvariantCulture),
            ["OET_BUDGET_TMP_MIB"] = policy.Budgets.TmpMiB.ToString(CultureInfo.InvariantCulture),
            ["OET_LOG_LEVEL"] = "Information",
        };

        // The trust plane keys only ever appear when the manager itself is in trust mode, and the helper
        // paths are container-internal constants the ctl's env allow-list mirrors exactly. The listener
        // port stays at the allocation endpoint's contract ({address}:7443) unless the owner overrides it.
        var ubag = _options.Value.Ubag;
        if (ubag.TrustEnabled)
        {
            env["OET_TRUST_ENABLED"] = "true";
            env["OET_TRUST_PORT"] = TrustListenerPort.ToString(CultureInfo.InvariantCulture);
            env["OET_TRUST_CERT_PATH"] = TrustCertPath;
            env["OET_TRUST_KEY_PATH"] = TrustKeyPath;
            env["OET_TRUST_CA_PATH"] = TrustCaPath;
        }

        return env;
    }

    /// <summary>The container-internal paths of the UBAG node certificate material (host side: /etc/oet-fleet/ubag, mounted read-only).</summary>
    public const string TrustCertPath = "/certs/ubag/node.crt";
    public const string TrustKeyPath = "/certs/ubag/node.key";
    public const string TrustCaPath = "/certs/ubag/ca.crt";

    /// <summary>The helper port UBAG dials (the allocation endpoint template's contract).</summary>
    public const int TrustListenerPort = 7443;
}
