using System.Net;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Fleet.Agent.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Extensions.Logging.Abstractions;

namespace Fleet.Agent.Tests;

/// <summary>
/// The trust-plane listener (decision D3) over a REAL Kestrel on loopback: the node certificate is our
/// identity, a client certificate is accepted only when it chains to the manager CA, /healthz answers
/// in plain JSON, and the gRPC health check answers SERVING over HTTP/2. Owner-run like the rest of
/// this suite; binds loopback only.
/// </summary>
public sealed class UbagListenerTests : IAsyncLifetime
{
    private static readonly TimeSpan CertTtl = TimeSpan.FromHours(2);

    private readonly string _directory = Directory.CreateTempSubdirectory("fleet-ubag-listener-").FullName;
    private WebApplication? _app;

    private X509Certificate2 Ca { get; set; } = null!;
    private X509Certificate2 Node { get; set; } = null!;
    private X509Certificate2 Client { get; set; } = null!;
    private X509Certificate2 Rogue { get; set; } = null!;

    public async Task InitializeAsync()
    {
        Ca = SelfSigned("CN=oet test CA", ca: true);
        Node = SignedBy("CN=ubag-node", Ca, uriSan: "spiffe://ubag/node/ubag-test");
        Client = SignedBy("CN=ubag-gateway", Ca, uriSan: "spiffe://ubag/gateway/test");
        Rogue = SelfSigned("CN=rogue CA", ca: true);

        File.WriteAllText(Path.Combine(_directory, "node.crt"), ExportCertPem(Node));
        File.WriteAllText(Path.Combine(_directory, "node.key"), ExportKeyPem(Node));
        File.WriteAllText(Path.Combine(_directory, "ca.crt"), ExportCertPem(Ca));
    }

    public async Task DisposeAsync()
    {
        if (_app is not null) await _app.DisposeAsync();
        try { Directory.Delete(_directory, recursive: true); }
        catch (IOException) { }
    }

    private TrustOptions Options => new()
    {
        Enabled = true,
        Port = 0,
        CertPath = Path.Combine(_directory, "node.crt"),
        KeyPath = Path.Combine(_directory, "node.key"),
        CaPath = Path.Combine(_directory, "ca.crt"),
    };

    private async Task<(WebApplication App, int Port)> StartAsync()
    {
        var holder = new UbagListener.CertificateHolder();
        Assert.True(holder.TryLoad(Options), "the holder must load freshly generated files");

        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseKestrel(kestrel =>
        {
            kestrel.Listen(IPAddress.Loopback, 0, listen =>
            {
                listen.Protocols = HttpProtocols.Http1AndHttp2;
                listen.UseHttps(new HttpsConnectionAdapterOptions
                {
                    ServerCertificateSelector = (_, _) => holder.Node,
                    ClientCertificateMode = ClientCertificateMode.AllowCertificate,
                    ClientCertificateValidation = (certificate, _, _) => holder.ValidateClientCertificate(certificate),
                    SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                });
            });
        });
        var app = builder.Build();
        UbagListener.MapEndpoints(app, Options, holder, NullLogger.Instance);
        await app.StartAsync();
        var url = app.Urls.First(u => u.StartsWith("https://", StringComparison.Ordinal));
        _app = app;
        return (app, new Uri(url).Port);
    }

    private HttpClient ClientFor(int port, X509Certificate2? presented)
    {
        var handler = new SocketsHttpHandler
        {
            SslOptions = new SslClientAuthenticationOptions
            {
                TargetHost = "localhost",
                ClientCertificates = presented is null ? null : new X509CertificateCollection { new X509Certificate2(presented.Export(X509ContentType.Pfx)) },
                RemoteCertificateValidationCallback = (certificate, _, _, _) =>
                    certificate is X509Certificate2 remote && remote.Thumbprint == Node.Thumbprint,
            },
        };
        return new HttpClient(handler) { BaseAddress = new Uri($"https://localhost:{port}/") };
    }

    [Fact]
    public async Task healthz_answers_without_a_client_certificate()
    {
        var (_, port) = await StartAsync();
        using var client = ClientFor(port, presented: null);
        using var response = await client.GetAsync("healthz");

        Assert.True(response.IsSuccessStatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("oet-fleet-agent.ubag-health/1", body);
        Assert.Contains("\"clientCertChained\":false", body);
    }

    [Fact]
    public async Task the_grpc_health_check_answers_serving_over_http2()
    {
        var (_, port) = await StartAsync();
        using var client = ClientFor(port, Client);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/grpc.health.v1.Health/Check")
        {
            Version = HttpVersion.Version20,
            VersionPolicy = HttpVersionPolicy.RequestVersionExact,
            Content = new ByteArrayContent(new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00 }),
        };
        request.Content!.Headers.ContentType = new("application/grpc");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/grpc", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("0", response.TrailingHeaders.GetValueOrDefault("grpc-status"));
        var frame = await response.Content.ReadAsByteArrayAsync();
        // SERVING (enum value 1) in one gRPC length-prefixed frame.
        Assert.Equal(new byte[] { 0x00, 0x00, 0x00, 0x00, 0x02, 0x08, 0x01 }, frame);
    }

    [Fact]
    public async Task unknown_grpc_paths_answer_unimplemented_instead_of_hanging()
    {
        var (_, port) = await StartAsync();
        using var client = ClientFor(port, Client);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/grpc.ubag.workload.v1/Dispatch")
        {
            Version = HttpVersion.Version20,
            VersionPolicy = HttpVersionPolicy.RequestVersionExact,
            Content = new ByteArrayContent(new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00 }),
        };
        request.Content!.Headers.ContentType = new("application/grpc");

        using var response = await client.SendAsync(request);

        Assert.Equal("12", response.TrailingHeaders.GetValueOrDefault("grpc-status"));
        Assert.Equal("unimplemented", response.TrailingHeaders.GetValueOrDefault("grpc-message"));
    }

    [Fact]
    public async Task a_client_certificate_from_another_ca_is_refused_at_the_handshake()
    {
        var (_, port) = await StartAsync();
        using var client = ClientFor(port, Rogue);
        await Assert.ThrowsAnyAsync<Exception>(() => client.GetAsync("healthz"));
    }

    [Fact]
    public void a_holder_refuses_mismatched_or_missing_files()
    {
        var holder = new UbagListener.CertificateHolder();
        Assert.False(holder.TryLoad(new TrustOptions { Enabled = true, CertPath = Path.Combine(_directory, "absent.crt") }));

        File.WriteAllText(Path.Combine(_directory, "broken.key"), "not a pem");
        var broken = new TrustOptions
        {
            Enabled = true,
            CertPath = Path.Combine(_directory, "node.crt"),
            KeyPath = Path.Combine(_directory, "broken.key"),
            CaPath = Path.Combine(_directory, "ca.crt"),
        };
        Assert.False(holder.TryLoad(broken));
    }

    // ---- certificate factory -------------------------------------------------------------

    private static X509Certificate2 SelfSigned(string subject, bool ca)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest(subject, key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(ca, false, 0, true));
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(ca ? X509KeyUsageFlags.KeyCertSign : X509KeyUsageFlags.DigitalSignature, true));
        using var generated = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.Add(CertTtl));
        // Re-anchor through PFX so the private key stays usable on Linux (the ephemeral-PEM pattern).
        return new X509Certificate2(generated.Export(X509ContentType.Pfx));
    }

    private static X509Certificate2 SignedBy(string subject, X509Certificate2 issuer, string uriSan)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest(subject, key, HashAlgorithmName.SHA256);
        var san = new SubjectAlternativeNameBuilder();
        san.AddUri(new Uri(uriSan));
        request.CertificateExtensions.Add(san.Build(critical: true));
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        using var generated = request.Create(issuer, DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.Add(CertTtl), RandomNumberGenerator.GetBytes(8));
        // A client certificate must carry its private key into the TLS handshake, so re-anchor cert+key
        // through PFX (the ephemeral-PEM pattern) instead of returning the public-only generated view.
        return new X509Certificate2(generated.CopyWithPrivateKey(key).Export(X509ContentType.Pfx));
    }

    private static string ExportCertPem(X509Certificate2 certificate) => certificate.ExportCertificatePem();

    private static string ExportKeyPem(X509Certificate2 certificate)
    {
        using var key = certificate.GetECDsaPrivateKey() ?? throw new InvalidOperationException("no ECDSA key");
        return PemEncoding.WriteString("PRIVATE KEY", key.ExportPkcs8PrivateKey());
    }
}
