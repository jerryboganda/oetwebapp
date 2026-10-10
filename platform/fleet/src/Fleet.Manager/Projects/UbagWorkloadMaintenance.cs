using Fleet.Manager.Configuration;
using Fleet.Core.Policy;
using Fleet.Manager.Infrastructure;
using Fleet.Manager.Operations;
using Fleet.Manager.Persistence;
using Microsoft.Extensions.Options;

namespace Fleet.Manager.Projects;

/// <summary>Reconciles the opted-in UBAG workload using the existing pinned, restricted host access.</summary>
public sealed class UbagWorkloadMaintenance(HostStore hosts, HostAccess access, UbagTrustService trust,
    UbagAllocationService allocations, RolloutTokenHolder tokens, IOptions<FleetOptions> options, ILogger<UbagWorkloadMaintenance> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await ReconcileAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex) { logger.LogWarning("UBAG workload reconciliation failed ({Class}).", ex.GetType().Name); }
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }

    private async Task ReconcileAsync(CancellationToken cancellationToken)
    {
        var cfg = options.Value.Ubag;
        if (!cfg.Enabled || !cfg.TrustEnabled) return;
        await trust.EnsurePrimaryBundleAsync(cancellationToken);
        foreach (var host in await hosts.ListAsync(cancellationToken))
        {
            if (host.Lifecycle != "Active" || !UbagAllocationService.IsListed(cfg.Hosts, host.Id, host.Lifecycle)) continue;
            try
            {
                var bundle = await trust.EnsureBundleAsync(host.Id, cancellationToken);
                var rendered = await access.CtlAsync(host, "put-certs", Array.Empty<string>(),
                    FleetJson.Serialize(new { ca = bundle.CaPem, cert = bundle.CertPem, key = bundle.KeyPem }), cancellationToken);
                if (!rendered.Success) { logger.LogWarning("UBAG certificate delivery failed for {Host}.", host.Id); continue; }
                if (string.IsNullOrEmpty(cfg.HelperImageDigest) || !cfg.HelperWireguardAddresses.TryGetValue(host.Id, out var address)) continue;
                var snapshot = await allocations.BuildAsync(cancellationToken);
                var list = System.Text.Json.JsonSerializer.Deserialize<UbagAllocationListWire>(snapshot!.Body);
                var grant = list!.Allocations.FirstOrDefault(a => a.NodeId == bundle.NodeId);
                if (grant is null || grant.State != "active") continue;
                tokens.TryGetCredential(out _, out var token);
                try
                {
                    if (!string.IsNullOrEmpty(token))
                    {
                        var login = await access.CtlAsync(host, "login", new[] { "--registry", "ghcr.io" }, token, cancellationToken);
                        if (!login.Success) throw new InvalidOperationException("helper registry login failed");
                    }
                    var payload = FleetJson.Serialize(new {
                        digest = cfg.HelperImageDigest, workload_version = cfg.HelperWorkloadVersion,
                        node_id = bundle.NodeId, primary_uri_san = "spiffe://ubag/primary/" + cfg.PrimaryId,
                        address, primary_address = cfg.PrimaryWireguardAddress,
                        primary_public_key = cfg.PrimaryWireguardPublicKey, primary_endpoint = cfg.PrimaryWireguardEndpoint,
                        cpu_millis = grant.CpuMillis, memory_bytes = grant.MemoryBytes,
                    });
                    var result = await access.CtlAsync(host, "ubag-reconcile", Array.Empty<string>(), payload, cancellationToken);
                    if (!result.Success) logger.LogWarning("UBAG helper workload reconciliation refused for {Host}.", host.Id);
                    else if (!string.IsNullOrEmpty(cfg.WireguardPeersDirectory))
                    {
                        using var response = System.Text.Json.JsonDocument.Parse(result.Stdout);
                        var publicKey = response.RootElement.GetProperty("wireguardPublicKey").GetString();
                        if (!System.Text.RegularExpressions.Regex.IsMatch(publicKey ?? "", @"\A[A-Za-z0-9+/]{43}=\z")) throw new InvalidOperationException("invalid peer public key");
                        Directory.CreateDirectory(cfg.WireguardPeersDirectory);
                        var path = Path.Combine(cfg.WireguardPeersDirectory, host.Id + ".json");
                        await File.WriteAllTextAsync(path + ".new", FleetJson.Serialize(new { address, public_key = publicKey }), cancellationToken);
                        File.Move(path + ".new", path, overwrite: true);
                    }
                }
                finally
                {
                    if (!string.IsNullOrEmpty(token)) await access.CtlAsync(host, "logout", Array.Empty<string>(), null, cancellationToken);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception ex) { logger.LogWarning("UBAG helper {Host} reconciliation failed ({Class}).", host.Id, ex.GetType().Name); }
        }
    }
}
