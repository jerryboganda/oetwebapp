using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Fleet.Manager.Configuration;
using Fleet.Manager.Projects;
using Fleet.Manager.Tests.Infrastructure;
using Fleet.Manager.Vault;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Fleet.Manager.Tests.Projects;

/// <summary>
/// The UBAG trust plane (decision D3): an owner-provisioned CA issues one leaf per helper, the leaf is
/// reused until its renewal window, and the published identity is a stable SPKI pin under the contracted
/// URI SAN. Owner-run like the rest of this suite.
/// </summary>
public sealed class UbagTrustServiceTests : IAsyncLifetime
{
    private FleetTestHost _host = null!;
    private EnrollmentDriver _driver = null!;
    private string _secretsDirectory = null!;

    public async Task InitializeAsync()
    {
        _host = await FleetTestHost.CreateAsync();
        _driver = new EnrollmentDriver(_host);
        _secretsDirectory = Directory.CreateTempSubdirectory("fleet-ca-").FullName;
        WriteCaFiles(_secretsDirectory);
    }

    public async Task DisposeAsync()
    {
        await _host.DisposeAsync();
        try { Directory.Delete(_secretsDirectory, recursive: true); }
        catch (IOException) { }
    }

    private FleetWorld World => _host.World;

    private UbagTrustService Service(bool enabled = true) => new(
        _host.Get<CredentialStore>(),
        new OptionsWrapper<FleetOptions>(new FleetOptions
        {
            Secrets = new SecretsOptions { Directory = _secretsDirectory },
            Ubag = new UbagOptions { TrustEnabled = enabled },
        }),
        World.Time,
        NullLogger<UbagTrustService>.Instance);

    [Fact]
    public async Task the_issued_leaf_carries_the_contracted_identity()
    {
        var hostId = await EnrolledHostIdAsync();
        var bundle = await Service().EnsureBundleAsync(hostId, CancellationToken.None);

        Assert.Equal(UbagTrustService.NodeIdFor(hostId), bundle.NodeId);
        Assert.Equal("spiffe://ubag/node/" + bundle.NodeId, bundle.UriSan);
        using var cert = X509Certificate2.CreateFromPem(bundle.CertPem);
        Assert.Equal("CN=" + bundle.NodeId, cert.SubjectName.Name);
        Assert.Equal(cert.NotAfter.ToUniversalTime(), bundle.NotAfter);
        Assert.True(cert.NotAfter.ToUniversalTime() - World.Time.GetUtcNow() >= UbagTrustService.LeafLifetime - TimeSpan.FromHours(1));

        // The manager CA signed it (custom-root chain), and the leaf key matches the certificate.
        using var ca = X509Certificate2.CreateFromPem(File.ReadAllText(Path.Combine(_secretsDirectory, "fleet_ca_cert")));
        using var chain = new X509Chain
        {
            ChainPolicy =
            {
                TrustMode = X509ChainTrustMode.CustomRootTrust,
                RevocationMode = X509RevocationMode.NoCheck,
            },
        };
        chain.ChainPolicy.CustomTrustStore.Add(ca);
        Assert.True(chain.Build(cert));
        Assert.Equal(
            SubjectPublicKeyInfoOf(bundle.KeyPem),
            Convert.ToHexString(cert.ExportSubjectPublicKeyInfo()));
    }

    [Fact]
    public async Task a_second_call_reuses_the_stored_leaf()
    {
        var hostId = await EnrolledHostIdAsync();
        var service = Service();
        var first = await service.EnsureBundleAsync(hostId, CancellationToken.None);
        var second = await service.EnsureBundleAsync(hostId, CancellationToken.None);

        Assert.Equal(first.CertPem, second.CertPem);
        Assert.Equal(first.KeyPem, second.KeyPem);
    }

    [Fact]
    public async Task the_published_identity_is_a_stable_spki_pin()
    {
        var hostId = await EnrolledHostIdAsync();
        var service = Service();
        var bundle = await service.EnsureBundleAsync(hostId, CancellationToken.None);
        var identity = await service.GetIdentityAsync(hostId, CancellationToken.None);

        Assert.NotNull(identity);
        Assert.Equal(bundle.NodeId, identity!.NodeId);
        Assert.Equal(bundle.UriSan, identity.UriSan);
        using var cert = X509Certificate2.CreateFromPem(bundle.CertPem);
        Assert.Equal(
            Convert.ToHexString(SHA256.HashData(cert.ExportSubjectPublicKeyInfo())).ToLowerInvariant(),
            identity.SpkiSha256);
        Assert.Equal(64, identity.SpkiSha256.Length);
        Assert.Equal(identity.SpkiSha256, (await service.GetIdentityAsync(hostId, CancellationToken.None))!.SpkiSha256);
    }

    [Fact]
    public async Task a_disabled_trust_plane_publishes_nothing_and_refuses_to_issue()
    {
        var hostId = await EnrolledHostIdAsync();
        var service = Service(enabled: false);

        Assert.Null(await service.GetIdentityAsync(hostId, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.EnsureBundleAsync(hostId, CancellationToken.None));
    }

    private async Task<string> EnrolledHostIdAsync()
    {
        await _driver.EnrollToActiveAsync();
        return (await _driver.HostAsync()).Id;
    }

    /// <summary>The SPKI of a PEM private key, computed through a throwaway certificate request.</summary>
    private static string SubjectPublicKeyInfoOf(string keyPem)
    {
        using var key = ECDsa.Create();
        key.ImportFromPem(keyPem);
        using var probe = new CertificateRequest("CN=probe", key, HashAlgorithmName.SHA256);
        return Convert.ToHexString(probe.PublicKey.ExportSubjectPublicKeyInfo());
    }

    private static void WriteCaFiles(string directory)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=oet fleet test CA", key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign, true));
        using var ca = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddYears(5));
        File.WriteAllText(Path.Combine(directory, "fleet_ca_cert"), ca.ExportCertificatePem());
        File.WriteAllText(Path.Combine(directory, "fleet_ca_key"), PemEncoding.Write("PRIVATE KEY", key.ExportPkcs8PrivateKey()));
    }
}
