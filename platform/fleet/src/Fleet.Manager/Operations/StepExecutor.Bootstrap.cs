using System.Globalization;
using Fleet.Core.Crypto;
using Fleet.Core.Domain;
using Fleet.Manager.Persistence;
using Fleet.Manager.Provisioning;
using Fleet.Manager.Vault;

namespace Fleet.Manager.Operations;

// S1..S8: the steps that need the temporary owner credential (root-level changes the restricted
// manager key can never perform). S8 destroys that credential; nothing after it can use it.

public sealed partial class StepExecutor
{
    private sealed class OwnerCredential : IDisposable
    {
        public OwnerCredential(string user, SecretBuffer key)
        {
            User = user;
            Key = key;
        }

        public string User { get; }

        public SecretBuffer Key { get; }

        public void Dispose() => Key.Dispose();
    }

    private async Task<OwnerCredential?> OpenOwnerAsync(HostEntity host, CancellationToken cancellationToken)
    {
        using var packed = await _credentials.OpenAsync(host.Id, CredentialPurposes.OwnerBootstrap, cancellationToken);
        if (packed is null)
        {
            return null;
        }

        var (user, key) = OwnerCredentialPayload.Unpack(packed);
        return new OwnerCredential(user, key);
    }

    private static StepOutcome OwnerCredentialMissing() =>
        StepOutcome.Fail(
            FailureReasons.OwnerCredentialExpired,
            "the temporary owner credential is missing or older than its 60-minute lifetime; submit the key again to resume",
            "owner credential expired");

    private ProvisionRequest BuildRequest(
        EnrollStep step,
        HostEntity host,
        OwnerCredential? owner,
        SecretBuffer? managerKey,
        string? managerPublicKey,
        Dictionary<string, object?>? data = null) =>
        new(
            step,
            _access.TargetFor(host),
            owner?.User,
            owner?.Key,
            managerKey,
            managerPublicKey,
            data ?? new Dictionary<string, object?>());

    /// <summary>S1: read-only host checks with the owner credential (OS, arch, cores, RAM, disk, clock, no foreign workloads).</summary>
    private async Task<StepOutcome> PreflightAsync(StepContext ctx, CancellationToken cancellationToken)
    {
        var host = await FreshHostAsync(ctx, cancellationToken);
        var address = await _guard.CheckAsync(host.Address, cancellationToken);
        if (!address.Allowed)
        {
            return StepOutcome.Fail(FailureReasons.ForbiddenHost, address.Message, "address rejected");
        }

        using var owner = await OpenOwnerAsync(host, cancellationToken);
        if (owner is null)
        {
            return OwnerCredentialMissing();
        }

        var result = await _provisioner.ApplyAsync(BuildRequest(EnrollStep.Preflight, host, owner, null, null), cancellationToken);
        if (!result.Success)
        {
            return FromProvision(result);
        }

        var facts = result.Facts;
        await _hosts.UpdateAsync(
            host.Id,
            h =>
            {
                h.Os = FactString(facts, "os");
                h.Arch = FactString(facts, "arch");
                h.CpuCores = FactInt(facts, "cpuCores");
                h.MemMib = FactInt(facts, "memMiB");
                h.DiskGib = FactInt(facts, "diskGiB");
            },
            cancellationToken);
        return StepOutcome.Done(result.Summary);
    }

    /// <summary>S2, S4, S5, S6: check the predicate, skip when satisfied, otherwise apply (all idempotent).</summary>
    private async Task<StepOutcome> RunRootStepAsync(StepContext ctx, EnrollStep step, CancellationToken cancellationToken)
    {
        var host = await FreshHostAsync(ctx, cancellationToken);
        using var owner = await OpenOwnerAsync(host, cancellationToken);
        if (owner is null)
        {
            return OwnerCredentialMissing();
        }

        using var managerKey = await _credentials.OpenAsync(host.Id, CredentialPurposes.ManagerSsh, cancellationToken);
        var data = new Dictionary<string, object?>();
        if (step == EnrollStep.Firewall && _options.Value.Ubag.TrustEnabled)
        {
            // The UBAG dial plane (decision D3): the helper's mTLS listener port is opened inbound only
            // when the manager runs in trust mode. The listener itself requires our node certificate.
            // 443 joins 7443 because UpCloud TRIAL-mode provider firewalls allow only 22/80/443/ICMP and
            // cannot be modified — S5 accepts 443 and redirects it to the 7443 listener.
            data["ubag_allow_ports"] = StepExecutor.TrustListenerPort.ToString(CultureInfo.InvariantCulture) + ",443";
        }
        var request = BuildRequest(step, host, owner, managerKey, null, data);
        var check = await _provisioner.CheckAsync(request, cancellationToken);
        if (check.Success && check.Satisfied)
        {
            return StepOutcome.Skipped(check.Summary);
        }

        var apply = await _provisioner.ApplyAsync(request, cancellationToken);
        return apply.Success ? StepOutcome.Done(apply.Summary) : FromProvision(apply);
    }

    /// <summary>
    /// S3: generate the manager's own ed25519 key IN THE MANAGER (only the public half leaves), encrypt the
    /// private half into the vault, install the public half behind the forced command, and prove the
    /// restricted login works.
    /// </summary>
    private async Task<StepOutcome> InstallKeyAsync(StepContext ctx, CancellationToken cancellationToken)
    {
        var host = await FreshHostAsync(ctx, cancellationToken);
        var (managerKey, publicLine) = await EnsureManagerKeyAsync(host, cancellationToken);
        using (managerKey)
        {
            var probe = BuildRequest(EnrollStep.InstallKey, host, null, managerKey, publicLine);
            var check = await _provisioner.CheckAsync(probe, cancellationToken);
            if (check.Success && check.Satisfied)
            {
                return StepOutcome.Skipped(check.Summary);
            }

            using var owner = await OpenOwnerAsync(host, cancellationToken);
            if (owner is null)
            {
                return OwnerCredentialMissing();
            }

            var apply = await _provisioner.ApplyAsync(BuildRequest(EnrollStep.InstallKey, host, owner, managerKey, publicLine), cancellationToken);
            if (!apply.Success)
            {
                return FromProvision(apply);
            }

            var verify = await _provisioner.RunCtlAsync(
                new CtlRequest(_access.TargetFor(host), managerKey, "status", Array.Empty<string>(), null),
                cancellationToken);
            return verify.Success
                ? StepOutcome.Done("manager key installed; restricted login verified")
                : FromCtl(verify, FailureReasons.BootstrapStepFailed, "install-key could not be verified");
        }
    }

    private async Task<(SecretBuffer Key, string PublicLine)> EnsureManagerKeyAsync(HostEntity host, CancellationToken cancellationToken)
    {
        var existing = await _credentials.OpenAsync(host.Id, CredentialPurposes.ManagerSsh, cancellationToken);
        if (existing is not null)
        {
            var derived = await _keys.DerivePublicKeyAsync(existing, cancellationToken);
            if (derived is not null)
            {
                return (existing, derived);
            }

            existing.Dispose();
        }

        var pair = await _keys.GenerateEd25519Async(cancellationToken);
        var parsed = PublicKeyLines.TryParse(pair.PublicKeyLine)
            ?? throw new InvalidOperationException("The generated public key is malformed.");
        await _credentials.StoreAsync(
            host.Id,
            CredentialPurposes.ManagerSsh,
            pair.PrivateKey,
            PublicKeyLines.Hint(parsed.Base64),
            null,
            cancellationToken);
        return (pair.PrivateKey, pair.PublicKeyLine);
    }

    /// <summary>
    /// S7: sshd hardening. The playbook re-verifies the manager login in a SECOND connection before the
    /// change is kept (and rolls back otherwise); this step double-checks it afterwards and reports
    /// <c>lockout_risk</c> on any doubt. Root's own login policy is never modified.
    /// </summary>
    private async Task<StepOutcome> HardenSshAsync(StepContext ctx, CancellationToken cancellationToken)
    {
        var host = await FreshHostAsync(ctx, cancellationToken);
        using var owner = await OpenOwnerAsync(host, cancellationToken);
        if (owner is null)
        {
            return OwnerCredentialMissing();
        }

        using var managerKey = await _credentials.OpenAsync(host.Id, CredentialPurposes.ManagerSsh, cancellationToken);
        if (managerKey is null)
        {
            return StepOutcome.Fail(FailureReasons.LockoutRisk, "no manager key exists, so SSH hardening could lock the operator out", "refused to harden");
        }

        var request = BuildRequest(EnrollStep.HardenSsh, host, owner, managerKey, null);
        var check = await _provisioner.CheckAsync(request, cancellationToken);
        if (check.Success && check.Satisfied)
        {
            return StepOutcome.Skipped(check.Summary);
        }

        var apply = await _provisioner.ApplyAsync(request, cancellationToken);
        if (!apply.Success)
        {
            return FromProvision(apply);
        }

        var verify = await _provisioner.RunCtlAsync(
            new CtlRequest(_access.TargetFor(host), managerKey, "status", Array.Empty<string>(), null),
            cancellationToken);
        return verify.Success
            ? StepOutcome.Done(apply.Summary)
            : StepOutcome.Fail(FailureReasons.LockoutRisk, "the manager login failed after hardening", "manager login lost after hardening");
    }

    /// <summary>S8: crypto-erase the owner credential. After this no owner secret exists on the manager.</summary>
    private async Task<StepOutcome> DiscardOwnerKeyAsync(StepContext ctx, CancellationToken cancellationToken)
    {
        var host = await FreshHostAsync(ctx, cancellationToken);
        if (!await _credentials.ExistsAnyAsync(host.Id, CredentialPurposes.OwnerBootstrap, cancellationToken))
        {
            return StepOutcome.Skipped("no owner credential is stored");
        }

        await _credentials.DestroyAsync(host.Id, CredentialPurposes.OwnerBootstrap, cancellationToken);
        if (await _credentials.ExistsAnyAsync(host.Id, CredentialPurposes.OwnerBootstrap, cancellationToken))
        {
            return StepOutcome.Fail(FailureReasons.OwnerKeyDiscardFailed, "the owner credential row is still present", "discard failed");
        }

        await _audit.AppendAsync("system", "host.owner_credential_destroyed", host.NodeRef, null, cancellationToken);
        return StepOutcome.Done("owner credential destroyed");
    }
}
