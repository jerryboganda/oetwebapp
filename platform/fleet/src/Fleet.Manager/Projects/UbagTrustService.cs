using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Fleet.Manager.Configuration;
using Fleet.Manager.Infrastructure;
using Fleet.Manager.Persistence;
using Fleet.Manager.Vault;
using Microsoft.Extensions.Options;

namespace Fleet.Manager.Projects;

/// <summary>What the allocation list and the helper provisioning both need to know about a node's
/// certificate identity. <see cref="UbagNodeIdentity.SpkiSha256"/> is lowercase hex of SHA-256 over the
/// certificate's SubjectPublicKeyInfo (DER) — the pin UBAG's trust plane matches
/// (<c>cert_identity.spki_sha256</c>).</summary>
public sealed record UbagNodeIdentity(string NodeId, string UriSan, string SpkiSha256, DateTimeOffset NotAfter);

/// <summary>The PEM bundle rendered onto a helper by S10 through the restricted <c>put-certs</c> verb.</summary>
public sealed record UbagNodeBundle(
    string NodeId,
    string UriSan,
    string CertPem,
    string KeyPem,
    string CaPem,
    DateTimeOffset NotAfter);

/// <summary>
/// The UBAG trust plane (decision D3): one manager CA, one leaf certificate per helper. The CA lives in
/// the secrets directory beside the other file secrets (owner-provisioned, 0400, never logged); leaves are
/// ECDSA P-256, valid 90 days, with the URI SAN <c>spiffe://ubag/node/&lt;node_id&gt;</c> UBAG matches.
/// A leaf is issued at most once per host per lifetime: it is stored encrypted in the vault (purposes
/// <c>ubag-node-cert</c>/<c>ubag-node-key</c>) and reused — across S10 runs and rollouts alike — until 30
/// days before expiry, when the next S10 replaces it. Issuance never happens inside the allocation poll:
/// that path only reads what S10 has already stored, and a host without a stored certificate simply
/// publishes an empty pin (the honest "unusable until provisioned" of the README).
/// </summary>
public sealed class UbagTrustService
{
    /// <summary>Leaf lifetime; short by design, rotated by re-running the agent step (rollout or repair).</summary>
    public static readonly TimeSpan LeafLifetime = TimeSpan.FromDays(90);

    /// <summary>A leaf younger than this is reused instead of re-issued at the next S10.</summary>
    public static readonly TimeSpan LeafRenewBefore = TimeSpan.FromDays(30);

    private const string CaPemBegin = "-----BEGIN CERTIFICATE-----";
    private const string KeyPemBegin = "-----BEGIN PRIVATE KEY-----";
    private const string EcKeyPemBegin = "-----BEGIN EC PRIVATE KEY-----";

    private readonly CredentialStore _credentials;
    private readonly IOptions<FleetOptions> _options;
    private readonly TimeProvider _time;
    private readonly ILogger<UbagTrustService> _logger;
    private readonly SemaphoreSlim _caGate = new(1, 1);

    private (X509Certificate2 Cert, string Pem)? _ca;

    public UbagTrustService(
        CredentialStore credentials,
        IOptions<FleetOptions> options,
        TimeProvider time,
        ILogger<UbagTrustService> logger)
    {
        _credentials = credentials;
        _options = options;
        _time = time;
        _logger = logger;
    }

    public static string NodeIdFor(string hostId) => UbagAllocationService.NodeIdFor(hostId);

    public static string UriSanFor(string nodeId) => UbagAllocationService.UriSanFor(nodeId);

    /// <summary>The stored identity for a host, or null when the trust plane is off or S10 has not provisioned the host yet.</summary>
    public async Task<UbagNodeIdentity?> GetIdentityAsync(string hostId, CancellationToken cancellationToken)
    {
        if (!_options.Value.Ubag.TrustEnabled)
        {
            return null;
        }

        using var pem = await _credentials.OpenAsync(hostId, CredentialPurposes.UbagNodeCert, cancellationToken);
        if (pem is null)
        {
            return null;
        }

        using var cert = X509Certificate2.CreateFromPem(Encoding.ASCII.GetString(pem.AsSpan()));
        return IdentityOf(hostId, cert);
    }

    /// <summary>
    /// S10's provisioning hook: returns the host's current leaf bundle, issuing (and vault-storing) a fresh
    /// one when none is stored or the stored one is inside the renewal window. The key exists in managed
    /// memory only for the lifetime of the returned strings; the encrypted vault copy is the one that survives.
    /// </summary>
    public async Task<UbagNodeBundle> EnsureBundleAsync(string hostId, CancellationToken cancellationToken)
    {
        if (!_options.Value.Ubag.TrustEnabled)
        {
            throw new InvalidOperationException("the UBAG trust plane is not enabled");
        }

        var ca = await LoadCaAsync(cancellationToken);
        var nodeId = NodeIdFor(hostId);
        var uriSan = UriSanFor(nodeId);

        var stored = await ReadStoredPemsAsync(hostId, cancellationToken);
        if (stored is { } reusable)
        {
            using var probe = X509Certificate2.CreateFromPem(reusable.CertPem);
            if (probe.NotAfter.ToUniversalTime() - _time.GetUtcNow() >= LeafRenewBefore)
            {
                return new UbagNodeBundle(nodeId, uriSan, reusable.CertPem, reusable.KeyPem, ca.Pem, probe.NotAfter);
            }

            _logger.LogInformation("UBAG node certificate for {NodeId} is inside the renewal window; re-issuing.", nodeId);
        }

        var notBefore = _time.GetUtcNow().AddMinutes(-5);
        var notAfter = _time.GetUtcNow().Add(LeafLifetime);
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=" + nodeId, key, HashAlgorithmName.SHA256);
        var san = new SubjectAlternativeNameBuilder();
        san.AddUri(new Uri(uriSan));
        request.CertificateExtensions.Add(san.Build(critical: true));
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        request.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension(
                new OidCollection { new("1.3.6.1.5.5.7.3.1"), new("1.3.6.1.5.5.7.3.2") },
                critical: true));
        using var leaf = request.Create(ca.Cert, notBefore, notAfter, RandomNumberGenerator.GetBytes(16));

        var certPem = PemEncoding.Write("CERTIFICATE", leaf.ExportCertificate());
        var keyPem = PemEncoding.Write("PRIVATE KEY", key.ExportPkcs8PrivateKey());

        using (var certSecret = SecretBuffer.FromBytes(Encoding.ASCII.GetBytes(certPem)))
        {
            await _credentials.StoreAsync(
                hostId, CredentialPurposes.UbagNodeCert, certSecret,
                VaultCipher.FingerprintHint(leaf.ExportSubjectPublicKeyInfo()), LeafLifetime, cancellationToken);
        }

        using (var keySecret = SecretBuffer.FromBytes(Encoding.ASCII.GetBytes(keyPem)))
        {
            await _credentials.StoreAsync(
                hostId, CredentialPurposes.UbagNodeKey, keySecret,
                VaultCipher.FingerprintHint(leaf.ExportSubjectPublicKeyInfo()), LeafLifetime, cancellationToken);
        }

        _logger.LogInformation("Issued UBAG node certificate for {NodeId} (expires {NotAfter:O}).", nodeId, notAfter);
        return new UbagNodeBundle(nodeId, uriSan, certPem, keyPem, ca.Pem, notAfter);
    }

    private async Task<(string CertPem, string KeyPem)?> ReadStoredPemsAsync(string hostId, CancellationToken cancellationToken)
    {
        using var certPem = await _credentials.OpenAsync(hostId, CredentialPurposes.UbagNodeCert, cancellationToken);
        using var keyPem = await _credentials.OpenAsync(hostId, CredentialPurposes.UbagNodeKey, cancellationToken);
        if (certPem is null || keyPem is null)
        {
            return null;
        }

        return (
            Encoding.ASCII.GetString(certPem.AsSpan()),
            Encoding.ASCII.GetString(keyPem.AsSpan()));
    }

    /// <summary>Loads and validates the CA pair from the secrets directory, caching it for the process lifetime.</summary>
    private async Task<(X509Certificate2 Cert, string Pem)> LoadCaAsync(CancellationToken cancellationToken)
    {
        if (_ca is { } cached)
        {
            return cached;
        }

        await _caGate.WaitAsync(cancellationToken);
        try
        {
            if (_ca is { } cachedNow)
            {
                return cachedNow;
            }

            var secrets = _options.Value.Secrets;
            var certPem = SecretFile.TryRead(Path.Combine(secrets.Directory, secrets.CaCertFile))
                ?? throw new InvalidOperationException("the UBAG CA certificate file is missing or empty; the trust plane cannot run");
            var keyPem = SecretFile.TryRead(Path.Combine(secrets.Directory, secrets.CaKeyFile))
                ?? throw new InvalidOperationException("the UBAG CA key file is missing or empty; the trust plane cannot run");
            if (!certPem.Contains(CaPemBegin, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("the UBAG CA certificate file does not contain a PEM certificate");
            }
            if (!keyPem.Contains(KeyPemBegin, StringComparison.Ordinal) && !keyPem.Contains(EcKeyPemBegin, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("the UBAG CA key file does not contain a PEM private key");
            }

            // CreateFromPem associates the key on import; re-exporting through PFX re-anchors it so the
            // private key stays usable for signing (the documented Linux ephemeral-key pattern).
            using var loaded = X509Certificate2.CreateFromPem(certPem, keyPem);
            var withKey = new X509Certificate2(loaded.Export(X509ContentType.Pfx));
            if (withKey.GetECDsaPrivateKey() is null)
            {
                throw new InvalidOperationException("the UBAG CA key is not an ECDSA key or cannot be used");
            }
            if (!withKey.Extensions.OfType<X509BasicConstraintsExtension>().Any(e => e.CertificateAuthority))
            {
                throw new InvalidOperationException("the UBAG CA certificate is not a CA (basic constraints)");
            }

            _ca = (withKey, certPem);
            return _ca.Value;
        }
        finally
        {
            _caGate.Release();
        }
    }

    private static UbagNodeIdentity IdentityOf(string hostId, X509Certificate2 cert)
    {
        var nodeId = NodeIdFor(hostId);
        var pin = Convert.ToHexString(SHA256.HashData(cert.ExportSubjectPublicKeyInfo())).ToLowerInvariant();
        return new UbagNodeIdentity(nodeId, UriSanFor(nodeId), pin, cert.NotAfter);
    }
}
