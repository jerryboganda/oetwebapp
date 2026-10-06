using System.Text.Json;
using Fleet.Core.Crypto;
using Fleet.Core.Domain;
using Fleet.Core.Policy;
using Fleet.Core.Ssh;
using Fleet.Core.Validation;
using Fleet.Manager.Configuration;
using Fleet.Manager.Infrastructure;
using Fleet.Manager.Persistence;
using Fleet.Manager.Provisioning;
using Fleet.Manager.Vault;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Fleet.Manager.Operations;

public sealed record AddHostRequest(
    string NodeRef,
    string DisplayName,
    string Address,
    int SshPort,
    string? Region,
    string? Provider);

/// <summary><see cref="AlreadyExisted"/> is true when the call was a duplicate: nothing was created and the existing operation is returned.</summary>
public sealed record AddHostResult(HostView Host, OperationView Operation, bool AlreadyExisted);

/// <summary>What an operation can do with an owner credential at this moment (see <see cref="EnrollmentService.AcceptanceFor"/>).</summary>
public enum OwnerKeyAcceptance
{
    /// <summary>The operation neither needs nor accepts one.</summary>
    No,

    /// <summary>The operation has not pinned the host key yet: the key can be saved now and is used once the key is confirmed.</summary>
    Stage,

    /// <summary>The operation is waiting for the key: submitting it starts (or resumes) the bootstrap.</summary>
    Submit,
}

/// <summary>
/// The owner-facing application service of enrollment (OET-RWP/1 section 8). Every method is a typed
/// action the JSON API and the future dashboard call; none of them accepts or returns a secret in a
/// field that could be logged. The expensive work (scan, bootstrap, image, agent, canary) happens in the
/// background runner; these methods only validate, record the owner's decision and wake the worker.
/// </summary>
public sealed class EnrollmentService
{
    private readonly SemaphoreSlim _addGate = new(1, 1);
    private readonly IDbContextFactory<FleetDbContext> _factory;
    private readonly HostStore _hosts;
    private readonly OperationStore _operations;
    private readonly AddressGuard _guard;
    private readonly CredentialStore _credentials;
    private readonly ISshKeyTool _keys;
    private readonly OperationRunner _runner;
    private readonly OperationSignal _signal;
    private readonly IAuditService _audit;
    private readonly IEventBus _events;
    private readonly TimeProvider _time;
    private readonly IOptions<FleetOptions> _options;

    public EnrollmentService(
        IDbContextFactory<FleetDbContext> factory,
        HostStore hosts,
        OperationStore operations,
        AddressGuard guard,
        CredentialStore credentials,
        ISshKeyTool keys,
        OperationRunner runner,
        OperationSignal signal,
        IAuditService audit,
        IEventBus events,
        TimeProvider time,
        IOptions<FleetOptions> options)
    {
        _factory = factory;
        _hosts = hosts;
        _operations = operations;
        _guard = guard;
        _credentials = credentials;
        _keys = keys;
        _runner = runner;
        _signal = signal;
        _audit = audit;
        _events = events;
        _time = time;
        _options = options;
    }

    /// <summary>
    /// Adds a helper (idempotent). Strict validation first (OET-RWP/1 section 8.9: the primary, anything of
    /// the production deployment, private ranges and injection strings are refused before any
    /// connection exists); then the host and its enroll operation are created once. A duplicate Add,
    /// by node reference or by address, is a no-op that returns the existing operation.
    /// </summary>
    public async Task<AddHostResult> AddHostAsync(AddHostRequest request, string actor, string? requestId, CancellationToken cancellationToken)
    {
        var issues = new List<ValidationIssue>();
        AddIfNotNull(issues, InputValidator.ValidateNodeRef(request.NodeRef));
        AddIfNotNull(issues, InputValidator.ValidateDisplayName(request.DisplayName));
        AddIfNotNull(issues, InputValidator.ValidatePort(request.SshPort));
        AddIfNotNull(issues, InputValidator.ValidateOptionalLabel("region", request.Region));
        AddIfNotNull(issues, InputValidator.ValidateOptionalLabel("provider", request.Provider));
        var address = await _guard.CheckAsync(request.Address, cancellationToken);
        if (!address.Allowed)
        {
            issues.Add(new ValidationIssue("address", address.Code ?? "address_invalid", address.Message ?? "address is not allowed."));
        }

        if (issues.Count > 0)
        {
            throw new FleetValidationException(issues);
        }

        await _addGate.WaitAsync(cancellationToken);
        try
        {
            var existing = await _hosts.FindByNodeRefAsync(request.NodeRef, cancellationToken)
                ?? await _hosts.FindLiveByAddressAsync(address.Normalized, request.SshPort, cancellationToken);
            if (existing is not null)
            {
                if (existing.Lifecycle == nameof(HostLifecycle.Removed))
                {
                    throw new FleetValidationException(new ValidationIssue("nodeRef", "node_ref_in_use", "A removed host used this node reference; choose a new one."));
                }

                var existingOp = await _operations.FindEnrollAsync(existing.Id, cancellationToken)
                    ?? await CreateEnrollOperationAsync(existing, request, address.Normalized, actor, requestId, cancellationToken);
                return new AddHostResult(HostView.From(existing), await ViewAsync(existingOp, cancellationToken), true);
            }

            var now = _time.GetUtcNow();
            var host = new HostEntity
            {
                Id = Guid.NewGuid().ToString("D"),
                NodeRef = request.NodeRef,
                DisplayName = request.DisplayName,
                Address = address.Normalized,
                SshPort = request.SshPort,
                Region = string.IsNullOrWhiteSpace(request.Region) ? null : request.Region,
                Provider = string.IsNullOrWhiteSpace(request.Provider) ? null : request.Provider,
                Lifecycle = nameof(HostLifecycle.Enrolling),
                CreatedAt = now,
                UpdatedAt = now,
            };

            try
            {
                await using var db = await _factory.CreateDbContextAsync(cancellationToken);
                db.Hosts.Add(host);
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                // A concurrent writer won the unique index: it is the existing host now.
                var winner = await _hosts.FindByNodeRefAsync(request.NodeRef, cancellationToken)
                    ?? await _hosts.FindLiveByAddressAsync(address.Normalized, request.SshPort, cancellationToken)
                    ?? throw new FleetOperationException("host_conflict", "The host could not be created.");
                var winnerOp = await _operations.FindEnrollAsync(winner.Id, cancellationToken)
                    ?? await CreateEnrollOperationAsync(winner, request, address.Normalized, actor, requestId, cancellationToken);
                return new AddHostResult(HostView.From(winner), await ViewAsync(winnerOp, cancellationToken), true);
            }

            var operation = await CreateEnrollOperationAsync(host, request, address.Normalized, actor, requestId, cancellationToken);
            await _audit.AppendAsync(
                actor,
                "host.added",
                host.NodeRef,
                new Dictionary<string, object?> { ["address"] = host.Address, ["port"] = host.SshPort, ["operation"] = operation.Id },
                cancellationToken);
            _events.Publish("host.updated", new { id = host.Id, nodeRef = host.NodeRef, lifecycle = host.Lifecycle });
            _signal.Kick();
            return new AddHostResult(HostView.From(host), await ViewAsync(operation, cancellationToken), false);
        }
        finally
        {
            _addGate.Release();
        }
    }

    /// <summary>
    /// The owner compared the SHA256 fingerprint with the VPS provider's console and types its first
    /// 8 characters. Only then is the key pinned (OET-RWP/1 section 8.6). Three wrong entries fail the operation.
    /// </summary>
    public async Task<OperationView> ConfirmHostKeyAsync(string operationId, string? typedPrefix, string actor, CancellationToken cancellationToken)
    {
        var op = await RequireEnrollAsync(operationId, cancellationToken);
        if (op.State != nameof(EnrollmentState.HostKeyPending))
        {
            throw new FleetOperationException("invalid_state", "The operation is not waiting for a host key confirmation.");
        }

        var host = await _hosts.GetAsync(op.HostId!, cancellationToken) ?? throw new FleetNotFoundException("The host");
        var data = OperationData.Parse(op.DataJson);
        var scanned = data.HostKeyCandidates.Select(c => new ScannedHostKey(c.Algorithm, c.PublicKeyBase64, c.Fingerprint)).ToList();
        var candidate = HostKeys.Preferred(scanned)
            ?? throw new FleetOperationException("no_host_key", "The scan found no host key.");

        if (!HostKeys.PrefixMatches(candidate.Fingerprint, typedPrefix))
        {
            data.HostKeyMismatches++;
            await _operations.SaveDataAsync(op.Id, data, cancellationToken);
            await _audit.AppendAsync(actor, "enroll.host_key_mismatch", host.NodeRef, new Dictionary<string, object?> { ["attempt"] = data.HostKeyMismatches }, cancellationToken);
            if (data.HostKeyMismatches >= 3)
            {
                await _runner.FailOperationAsync(op, FailureReasons.HostKeyMismatch, "three wrong fingerprint confirmations", cancellationToken);
            }

            throw new FleetValidationException(new ValidationIssue(
                "fingerprint",
                FailureReasons.HostKeyMismatch,
                "The characters do not match the fingerprint shown. Compare it with the provider console and try again."));
        }

        var now = _time.GetUtcNow();
        await _hosts.UpdateAsync(
            host.Id,
            h =>
            {
                h.HostKeyAlgo = candidate.Algorithm;
                h.HostKeySha256 = candidate.Fingerprint;
                h.HostKeyPublic = candidate.PublicKeyBase64;
                h.HostKeyPinnedAt = now;
            },
            cancellationToken);
        if (!await _runner.TransitionAsync(op, EnrollmentState.HostKeyConfirmed, cancellationToken))
        {
            throw new FleetOperationException("conflict", "The operation changed while confirming; refresh and retry.");
        }

        await _audit.AppendAsync(
            actor,
            "enroll.host_key_pinned",
            host.NodeRef,
            new Dictionary<string, object?> { ["algorithm"] = candidate.Algorithm, ["fingerprint"] = candidate.Fingerprint },
            cancellationToken);

        // A key the owner saved in advance (see StageOwnerCredentialAsync) is used now that the host key is pinned: nothing more to type.
        if (await _credentials.ExistsActiveAsync(host.Id, CredentialPurposes.OwnerBootstrap, cancellationToken))
        {
            try
            {
                return await ContinueWithSavedCredentialAsync(op.Id, actor, cancellationToken);
            }
            catch (FleetOperationException)
            {
                // The operation stays at HostKeyConfirmed; the owner can continue (or submit the key again) from there.
            }
        }

        return await ViewAsync(await _operations.GetAsync(op.Id, cancellationToken) ?? op, cancellationToken);
    }

    /// <summary>
    /// Supplies the temporary owner credential (a private key that can reach root on the helper). It is
    /// encrypted into the vault with a 60-minute TTL, used only by steps S1..S7 and destroyed by S8. The
    /// key text is converted into a zeroable buffer immediately and is never logged, echoed or audited.
    /// </summary>
    public Task<OperationView> SubmitOwnerCredentialAsync(
        string operationId,
        string? sshUser,
        string? privateKeyText,
        string actor,
        CancellationToken cancellationToken) =>
        SubmitOwnerCredentialAsync(operationId, sshUser, privateKeyText, null, actor, cancellationToken);

    /// <summary>
    /// As above, for a key that is protected by a passphrase: the passphrase is used once to open a copy of the key, never stored,
    /// never audited, never logged; only the unprotected key continues (encrypted, 60 minutes at most).
    /// </summary>
    public async Task<OperationView> SubmitOwnerCredentialAsync(
        string operationId,
        string? sshUser,
        string? privateKeyText,
        string? passphrase,
        string actor,
        CancellationToken cancellationToken)
    {
        var op = await _operations.GetAsync(operationId, cancellationToken) ?? throw new FleetNotFoundException("The operation");
        var isEnroll = op.Kind == "enroll";
        if (AcceptanceFor(op) != OwnerKeyAcceptance.Submit || op.HostId is null)
        {
            throw new FleetOperationException("invalid_state", "The operation is not waiting for an owner credential.");
        }

        var host = await _hosts.GetAsync(op.HostId, cancellationToken) ?? throw new FleetNotFoundException("The host");
        using var prepared = await PrepareOwnerKeyAsync(sshUser, privateKeyText, passphrase, cancellationToken);
        var info = await _credentials.StoreAsync(
            host.Id,
            CredentialPurposes.OwnerBootstrap,
            prepared.Packed,
            prepared.Hint,
            TimeSpan.FromMinutes(_options.Value.Timing.OwnerCredentialMinutes),
            cancellationToken);

        var moved = isEnroll
            ? await ResumeEnrollAsync(op, EnrollmentState.Bootstrapping, cancellationToken)
            : await ResumeGenericAsync(op, cancellationToken);
        if (!moved)
        {
            // No operation is waiting for this root-capable secret any more: do not leave it in the vault for its 60-minute lifetime.
            // The one exception is a concurrent submit that already moved the operation on; the credential then belongs to that run.
            var current = await _operations.GetAsync(op.Id, cancellationToken);
            if (!OwnerCredentialInUse(current))
            {
                await _credentials.DestroyAsync(host.Id, CredentialPurposes.OwnerBootstrap, cancellationToken);
            }

            throw new FleetOperationException("conflict", "The operation changed while the credential was stored; refresh and retry.");
        }

        await _audit.AppendAsync(
            actor,
            "enroll.owner_credential_submitted",
            host.NodeRef,
            new Dictionary<string, object?> { ["fingerprint"] = info.FingerprintHint, ["expiresAt"] = info.ExpiresAt },
            cancellationToken);
        _signal.Kick();
        return await ViewAsync(await _operations.GetAsync(op.Id, cancellationToken) ?? op, cancellationToken);
    }

    /// <summary>What an operation can do with an owner credential right now: nothing, save it for later (before the host key is confirmed), or use it.</summary>
    public static OwnerKeyAcceptance AcceptanceFor(OperationEntity op)
    {
        if (op.Kind == "enroll")
        {
            if (op.State is nameof(EnrollmentState.Created) or nameof(EnrollmentState.HostKeyPending))
            {
                return OwnerKeyAcceptance.Stage;
            }

            return op.State == nameof(EnrollmentState.HostKeyConfirmed)
                || (op.State == nameof(EnrollmentState.Failed) && op.ResumeState == nameof(EnrollmentState.Bootstrapping))
                    ? OwnerKeyAcceptance.Submit
                    : OwnerKeyAcceptance.No;
        }

        return op.Kind == OperationKinds.ToWire(OperationKind.Repair)
            && (op.State == nameof(GenericOperationState.AwaitingOwner) || op.State == nameof(GenericOperationState.Failed))
                ? OwnerKeyAcceptance.Submit
                : OwnerKeyAcceptance.No;
    }

    /// <summary>Stores the key now when the operation is still before the host-key check, uses it when the operation is waiting for it; anything else is refused.</summary>
    public async Task<OperationView> SetOwnerCredentialAsync(
        string operationId,
        string? sshUser,
        string? privateKeyText,
        string? passphrase,
        string actor,
        CancellationToken cancellationToken)
    {
        var op = await _operations.GetAsync(operationId, cancellationToken) ?? throw new FleetNotFoundException("The operation");
        return AcceptanceFor(op) switch
        {
            OwnerKeyAcceptance.Stage => await StageOwnerCredentialAsync(operationId, sshUser, privateKeyText, passphrase, actor, cancellationToken),
            OwnerKeyAcceptance.Submit => await SubmitOwnerCredentialAsync(operationId, sshUser, privateKeyText, passphrase, actor, cancellationToken),
            _ => throw new FleetOperationException("invalid_state", "The operation is not waiting for an owner credential."),
        };
    }

    /// <summary>
    /// Saves the temporary owner credential BEFORE the host key is confirmed, so the owner can add a helper with one form. It is the same
    /// credential SubmitOwnerCredentialAsync stores (encrypted in the vault, 60 minutes at most, destroyed at S8, on cancel and on expiry), but it
    /// is only USED once the owner has pinned the host key: confirming the key continues the enrollment with it (or
    /// <see cref="ContinueWithSavedCredentialAsync"/> does, when that was missed).
    /// </summary>
    public async Task<OperationView> StageOwnerCredentialAsync(
        string operationId,
        string? sshUser,
        string? privateKeyText,
        string? passphrase,
        string actor,
        CancellationToken cancellationToken)
    {
        var op = await RequireEnrollAsync(operationId, cancellationToken);
        if (AcceptanceFor(op) != OwnerKeyAcceptance.Stage)
        {
            throw new FleetOperationException("invalid_state", "A key can only be saved in advance while the host key is still to be confirmed.");
        }

        var host = await _hosts.GetAsync(op.HostId!, cancellationToken) ?? throw new FleetNotFoundException("The host");
        using var prepared = await PrepareOwnerKeyAsync(sshUser, privateKeyText, passphrase, cancellationToken);
        var info = await _credentials.StoreAsync(
            host.Id,
            CredentialPurposes.OwnerBootstrap,
            prepared.Packed,
            prepared.Hint,
            TimeSpan.FromMinutes(_options.Value.Timing.OwnerCredentialMinutes),
            cancellationToken);

        // The operation may have been cancelled or confirmed while the key was being stored: never leave a root-capable secret behind for an
        // operation that will not use it.
        var current = await _operations.GetAsync(op.Id, cancellationToken);
        var stillWaiting = current is not null
            && (AcceptanceFor(current) == OwnerKeyAcceptance.Stage || current.State == nameof(EnrollmentState.HostKeyConfirmed));
        if (!stillWaiting)
        {
            if (!OwnerCredentialInUse(current))
            {
                await _credentials.DestroyAsync(host.Id, CredentialPurposes.OwnerBootstrap, cancellationToken);
            }

            throw new FleetOperationException("conflict", "The operation changed while the credential was stored; refresh and retry.");
        }

        await _audit.AppendAsync(
            actor,
            "enroll.owner_credential_staged",
            host.NodeRef,
            new Dictionary<string, object?> { ["fingerprint"] = info.FingerprintHint, ["expiresAt"] = info.ExpiresAt },
            cancellationToken);
        if (current!.State == nameof(EnrollmentState.HostKeyConfirmed))
        {
            return await ContinueWithSavedCredentialAsync(op.Id, actor, cancellationToken);
        }

        return await ViewAsync(current!, cancellationToken);
    }

    /// <summary>Starts bootstrapping with the key saved in advance (the host key is pinned and the key has not expired).</summary>
    public async Task<OperationView> ContinueWithSavedCredentialAsync(string operationId, string actor, CancellationToken cancellationToken)
    {
        var op = await RequireEnrollAsync(operationId, cancellationToken);
        if (op.State != nameof(EnrollmentState.HostKeyConfirmed))
        {
            throw new FleetOperationException("invalid_state", "The operation is not waiting for an owner credential.");
        }

        var host = await _hosts.GetAsync(op.HostId!, cancellationToken) ?? throw new FleetNotFoundException("The host");
        if (!await _credentials.ExistsActiveAsync(host.Id, CredentialPurposes.OwnerBootstrap, cancellationToken))
        {
            throw new FleetOperationException("owner_credential_required", "No saved key is available (it may have expired). Submit the key again.");
        }

        var info = await _credentials.GetInfoAsync(host.Id, CredentialPurposes.OwnerBootstrap, cancellationToken);
        if (!await ResumeEnrollAsync(op, EnrollmentState.Bootstrapping, cancellationToken))
        {
            throw new FleetOperationException("conflict", "The operation changed; refresh and retry.");
        }

        await _audit.AppendAsync(
            actor,
            "enroll.saved_credential_used",
            host.NodeRef,
            new Dictionary<string, object?> { ["fingerprint"] = info?.FingerprintHint },
            cancellationToken);
        _signal.Kick();
        return await ViewAsync(await _operations.GetAsync(op.Id, cancellationToken) ?? op, cancellationToken);
    }

    /// <summary>Retry a failed operation from the step that failed (never from the beginning).</summary>
    public async Task<OperationView> RetryAsync(string operationId, string actor, CancellationToken cancellationToken)
    {
        var op = await _operations.GetAsync(operationId, cancellationToken) ?? throw new FleetNotFoundException("The operation");
        var failed = op.State == nameof(EnrollmentState.Failed);
        if (!failed)
        {
            throw new FleetOperationException("invalid_state", "Only a failed operation can be retried.");
        }

        var host = op.HostId is null ? null : await _hosts.GetAsync(op.HostId, cancellationToken);
        if (host?.Alert == FailureReasons.HostKeyChanged)
        {
            throw new FleetOperationException("repin_required", "The host key changed. Re-pin the host key with a fresh out-of-band check before retrying.");
        }

        if ((op.FailureReason == FailureReasons.OwnerCredentialExpired || op.FailureReason == FailureReasons.AuthFailed)
            && op.Kind == "enroll"
            && host is not null
            && !await _credentials.ExistsActiveAsync(host.Id, CredentialPurposes.OwnerBootstrap, cancellationToken))
        {
            throw new FleetOperationException("owner_credential_required", "Submit the owner key again to resume.");
        }

        bool moved;
        if (op.Kind == "enroll")
        {
            if (!EnrollmentStateMachine.TryParse(op.ResumeState, out var resume) || !EnrollmentStateMachine.CanRetry(resume))
            {
                throw new FleetOperationException("invalid_state", "The operation has no valid state to resume.");
            }

            moved = await ResumeEnrollAsync(op, resume, cancellationToken);
        }
        else
        {
            moved = await ResumeGenericAsync(op, cancellationToken);
        }

        if (!moved)
        {
            throw new FleetOperationException("conflict", "The operation changed; refresh and retry.");
        }

        await _audit.AppendAsync(actor, "operation.retried", op.Id, new Dictionary<string, object?> { ["kind"] = op.Kind, ["step"] = op.CurrentStep }, cancellationToken);
        _signal.Kick();
        return await ViewAsync(await _operations.GetAsync(op.Id, cancellationToken) ?? op, cancellationToken);
    }

    public async Task<OperationView> CancelAsync(string operationId, string actor, CancellationToken cancellationToken)
    {
        var op = await _operations.GetAsync(operationId, cancellationToken) ?? throw new FleetNotFoundException("The operation");
        if (!await _runner.CancelAsync(operationId, actor, cancellationToken))
        {
            throw new FleetOperationException("invalid_state", "The operation cannot be cancelled in its current state.");
        }

        return await ViewAsync(await _operations.GetAsync(operationId, cancellationToken) ?? op, cancellationToken);
    }

    // ---- helpers ---------------------------------------------------------------------------

    /// <summary>An owner key, validated and packed as the vault stores it. Disposing zeroes the packed bytes.</summary>
    private sealed class PreparedOwnerKey : IDisposable
    {
        public PreparedOwnerKey(SecretBuffer packed, string hint)
        {
            Packed = packed;
            Hint = hint;
        }

        public SecretBuffer Packed { get; }

        /// <summary>First 8 hex characters of SHA-256 over the PUBLIC key blob: the only thing the UI and the audit ever show.</summary>
        public string Hint { get; }

        public void Dispose() => Packed.Dispose();
    }

    /// <summary>
    /// Validates the SSH user and the key text, opens a passphrase-protected key with the passphrase the owner typed (the passphrase is used
    /// for that one call and never kept), proves the key can be read non-interactively and packs it for the vault.
    /// </summary>
    private async Task<PreparedOwnerKey> PrepareOwnerKeyAsync(string? sshUser, string? privateKeyText, string? passphrase, CancellationToken cancellationToken)
    {
        var userIssue = InputValidator.ValidateSshUser(sshUser);
        if (userIssue is not null)
        {
            throw new FleetValidationException(userIssue);
        }

        var protectedKey = !string.IsNullOrEmpty(passphrase);
        if (protectedKey && !OwnerKeyText.IsAcceptablePassphrase(passphrase))
        {
            throw new FleetValidationException(new ValidationIssue("passphrase", "passphrase_invalid", "The passphrase must be a single line of at most 256 characters."));
        }

        if (!OwnerKeyText.TryNormalize(privateKeyText, out var normalized, allowEncrypted: protectedKey))
        {
            throw new FleetValidationException(new ValidationIssue(
                "privateKey",
                "owner_key_invalid",
                protectedKey ? "Paste an OpenSSH or PEM private key." : "Paste an unencrypted OpenSSH or PEM private key."));
        }

        SecretBuffer? key = SecretBuffer.FromUtf8(normalized);
        try
        {
            if (protectedKey)
            {
                var unlocked = await _keys.RemovePassphraseAsync(key, passphrase!, cancellationToken);
                key.Dispose();
                key = unlocked;
                if (key is null)
                {
                    throw new FleetValidationException(new ValidationIssue(
                        "privateKey",
                        "owner_key_unusable",
                        "The key could not be opened. Check the passphrase and that the text is a complete OpenSSH or PEM private key."));
                }
            }

            var publicLine = await _keys.DerivePublicKeyAsync(key, cancellationToken);
            var parsed = publicLine is null ? null : PublicKeyLines.TryParse(publicLine);
            if (parsed is null)
            {
                throw new FleetValidationException(new ValidationIssue(
                    "privateKey",
                    "owner_key_unusable",
                    protectedKey
                        ? "The key could not be read after removing its passphrase."
                        : "The key could not be read. If it is protected by a passphrase, enter the passphrase too."));
            }

            return new PreparedOwnerKey(OwnerCredentialPayload.Pack(sshUser!, key), PublicKeyLines.Hint(parsed.Value.Base64));
        }
        finally
        {
            key?.Dispose();
        }
    }

    /// <summary>True when the operation is in a state that is (or is about to be) running with the temporary owner credential.</summary>
    private static bool OwnerCredentialInUse(OperationEntity? op) =>
        op is not null
        && op.State is nameof(EnrollmentState.Bootstrapping)
            or nameof(GenericOperationState.Queued)
            or nameof(GenericOperationState.Running);

    private async Task<OperationEntity> RequireEnrollAsync(string operationId, CancellationToken cancellationToken)
    {
        var op = await _operations.GetAsync(operationId, cancellationToken) ?? throw new FleetNotFoundException("The operation");
        if (op.Kind != "enroll" || op.HostId is null)
        {
            throw new FleetOperationException("invalid_state", "This is not an enrollment.");
        }

        return op;
    }

    private async Task<OperationEntity> CreateEnrollOperationAsync(
        HostEntity host,
        AddHostRequest request,
        string normalizedAddress,
        string actor,
        string? requestId,
        CancellationToken cancellationToken)
    {
        var paramsJson = JsonSerializer.Serialize(
            new Dictionary<string, object?>
            {
                ["nodeRef"] = host.NodeRef,
                ["address"] = normalizedAddress,
                ["sshPort"] = request.SshPort,
                ["region"] = request.Region,
                ["provider"] = request.Provider,
            },
            FleetJson.Options);
        return await _operations.CreateAsync(
            new NewOperation(OperationKind.Enroll, host.Id, nameof(EnrollmentState.Created), StepPlans.Enroll(), paramsJson, actor, requestId),
            cancellationToken);
    }

    /// <summary>Failed (or HostKeyConfirmed) back into the state to continue from. Step-specific scratch data is reset so a retried canary asks again.</summary>
    private async Task<bool> ResumeEnrollAsync(OperationEntity op, EnrollmentState target, CancellationToken cancellationToken)
    {
        if (!EnrollmentStateMachine.TryParse(op.State, out var from))
        {
            return false;
        }

        if (from == EnrollmentState.Failed)
        {
            if (!EnrollmentStateMachine.CanRetry(target))
            {
                return false;
            }
        }
        else
        {
            EnrollmentStateMachine.Require(from, target);
        }

        var data = OperationData.Parse(op.DataJson);
        data.CanaryRequestedAt = null;
        data.RestartRequestedAt = null;
        data.HostKeyMismatches = 0;
        var changed = await _operations.UpdateAsync(
            op.Id,
            op.State,
            o =>
            {
                o.State = target.ToString();
                o.ResumeState = null;
                o.FailureReason = null;
                o.FailureDetail = null;
                o.FinishedAt = null;
                o.DataJson = data.ToJson();
            },
            cancellationToken);
        if (changed && op.HostId is not null)
        {
            await _hosts.UpdateAsync(op.HostId, h => h.Lifecycle = nameof(HostLifecycle.Enrolling), cancellationToken);
        }

        return changed;
    }

    private async Task<bool> ResumeGenericAsync(OperationEntity op, CancellationToken cancellationToken)
    {
        var data = OperationData.Parse(op.DataJson);
        data.CanaryRequestedAt = null;
        data.RestartRequestedAt = null;
        return await _operations.UpdateAsync(
            op.Id,
            op.State,
            o =>
            {
                o.State = nameof(GenericOperationState.Queued);
                o.ResumeState = null;
                o.FailureReason = null;
                o.FailureDetail = null;
                o.FinishedAt = null;
                o.DataJson = data.ToJson();
            },
            cancellationToken);
    }

    private async Task<OperationView> ViewAsync(OperationEntity op, CancellationToken cancellationToken) =>
        OperationView.From(op, await _operations.GetStepsAsync(op.Id, cancellationToken));

    private static void AddIfNotNull(List<ValidationIssue> issues, ValidationIssue? issue)
    {
        if (issue is not null)
        {
            issues.Add(issue);
        }
    }
}

/// <summary>Accepts only something that looks like an unencrypted private key and normalises it for OpenSSH (LF endings, trailing newline).</summary>
public static class OwnerKeyText
{
    public const int MaxLength = 16 * 1024;

    public const int MaxPassphraseLength = 256;

    /// <summary>A passphrase is one line of at most 256 characters (it becomes one argument of one child process, never a shell string).</summary>
    public static bool IsAcceptablePassphrase(string? passphrase) =>
        !string.IsNullOrEmpty(passphrase)
        && passphrase.Length <= MaxPassphraseLength
        && passphrase.IndexOfAny(new[] { '\0', '\r', '\n' }) < 0;

    /// <summary>
    /// <paramref name="allowEncrypted"/> admits a key that announces itself as encrypted (a legacy PEM block with a Proc-Type header): only
    /// when the owner supplied the passphrase to open it with.
    /// </summary>
    public static bool TryNormalize(string? text, out string normalized, bool allowEncrypted = false)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(text) || text.Length > MaxLength || text.Contains('\0'))
        {
            return false;
        }

        var unified = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Trim();
        var firstLine = unified.Split('\n')[0];
        if (!firstLine.StartsWith("-----BEGIN ", StringComparison.Ordinal)
            || !firstLine.EndsWith("PRIVATE KEY-----", StringComparison.Ordinal)
            || !unified.Contains("-----END ", StringComparison.Ordinal))
        {
            return false;
        }

        if (!allowEncrypted && unified.Contains("ENCRYPTED", StringComparison.Ordinal))
        {
            return false;
        }

        normalized = unified + "\n";
        return true;
    }
}
