using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Fleet.Agent.Net;

/// <summary>
/// The helper side of the UBAG trust plane (decision D3): the mTLS server UBAG's gateway dials on the
/// node port. The node certificate (issued by the manager CA) is our identity; a client certificate the
/// dialer presents is accepted when it chains to the same CA and otherwise refused — an absent one is
/// allowed, because today the pin on OUR certificate is the trust anchor UBAG verifies, and client-side
/// policy is a coordinated UBAG decision that may introduce its own issuer. Until the workload plane is
/// specified with UBAG, the surface is deliberately minimal: the standard gRPC health service (what a
/// Go <c>grpc_health_probe</c>-style prober checks) and a plain-text <c>/healthz</c>. Any other path
/// answers the gRPC "unimplemented" status so a future UBAG rollout fails loudly instead of hanging.
/// </summary>
internal static class UbagListener
{
    /// <summary>Holds the loaded certificate material. Files are provisioned before the container is
    /// created, but a restart race must never take the OET job plane down: connections before the files
    /// are readable fail their TLS handshake and the dialer retries.</summary>
    internal sealed class CertificateHolder
    {
        private readonly object _gate = new();
        private X509Certificate2? _node;
        private X509Certificate2? _ca;
        private string _fingerprint = "";

        /// <summary>The server certificate presented to every dialer, or null until the files load.</summary>
        public X509Certificate2? Node
        {
            get { lock (_gate) return _node; }
        }

        /// <summary>The manager CA a presented client certificate must chain to, or null while not loaded.</summary>
        public X509Certificate2? Ca
        {
            get { lock (_gate) return _ca; }
        }

        /// <summary>First 16 hex of the SHA-256 over the node certificate's SPKI — for the log line only
        /// (the full pin is the manager's to publish, and a log line is not the allocation list).</summary>
        public string ShortFingerprint
        {
            get { lock (_gate) return _fingerprint; }
        }

        public bool TryLoad(TrustOptions options)
        {
            try
            {
                var certPem = File.ReadAllText(options.CertPath);
                var keyPem = File.ReadAllText(options.KeyPath);
                var caPem = File.ReadAllText(options.CaPath);
                // PEM-loaded keys are ephemeral on Linux; the PFX round-trip re-anchors the private key.
                using var loaded = X509Certificate2.CreateFromPem(certPem, keyPem);
                var node = new X509Certificate2(loaded.Export(X509ContentType.Pfx));
                var ca = X509Certificate2.CreateFromPem(caPem);
                if (!IsSelfIssuedBy(node, ca))
                {
                    node.Dispose();
                    ca.Dispose();
                    return false;
                }

                lock (_gate)
                {
                    _node?.Dispose();
                    _ca?.Dispose();
                    _node = node;
                    _ca = ca;
                    _fingerprint = Convert.ToHexString(
                        System.Security.Cryptography.SHA256.HashData(node.ExportSubjectPublicKeyInfo()))[..16];
                }

                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                or System.Security.Cryptography.CryptographicException or ArgumentException)
            {
                return false;
            }
        }

        /// <summary>True when the node certificate was signed by this CA (signature and issuer name).</summary>
        private static bool IsSelfIssuedBy(X509Certificate2 node, X509Certificate2 ca)
        {
            if (!string.Equals(node.Issuer, ca.Subject, StringComparison.Ordinal))
            {
                return false;
            }

            using var chainPolicy = new X509Chain
            {
                ChainPolicy =
                {
                    TrustMode = X509ChainTrustMode.CustomRootTrust,
                    RevocationMode = X509RevocationMode.NoCheck,
                },
            };
            chainPolicy.ChainPolicy.CustomTrustStore.Add(ca);
            return chainPolicy.Build(node);
        }

        /// <summary>Client-certificate validation for Kestrel: a presented cert must chain to the manager
        /// CA; "no certificate presented" is allowed and simply yields an anonymous dialer.</summary>
        public bool ValidateClientCertificate(X509Certificate2? certificate)
        {
            if (certificate is null)
            {
                return true;
            }

            var ca = Ca;
            if (ca is null)
            {
                return false;
            }

            using var chain = new X509Chain
            {
                ChainPolicy =
                {
                    TrustMode = X509ChainTrustMode.CustomRootTrust,
                    RevocationMode = X509RevocationMode.NoCheck,
                },
            };
            chain.ChainPolicy.CustomTrustStore.Add(ca);
            return chain.Build(certificate);
        }
    }

    /// <summary>Background loader: reads the provisioned certificate files until they appear (or shutdown),
    /// so a container created a moment before the mount settled heals itself without restarting.</summary>
    internal sealed class CertificateLoader : IHostedService
    {
        public static readonly TimeSpan RetryEvery = TimeSpan.FromSeconds(30);

        private readonly CertificateHolder _holder;
        private readonly TrustOptions _options;
        private readonly ILogger _logger;
        private CancellationTokenSource? _cts;
        private Task? _loop;

        public CertificateLoader(CertificateHolder holder, AgentOptions options, ILoggerFactory loggers)
        {
            _holder = holder;
            _options = options.Trust;
            _logger = loggers.CreateLogger("Fleet.Agent.Ubag");
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _loop = Task.Run(() => LoadLoopAsync(_cts.Token), CancellationToken.None);
            return Task.CompletedTask;
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _cts?.Cancel();
            if (_loop is not null)
            {
                try
                {
                    await _loop.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
                }
                catch (TimeoutException)
                {
                    // The loop exits on its own cancellation token; a slow exit is not a shutdown failure.
                }
            }
        }

        private async Task LoadLoopAsync(CancellationToken cancellationToken)
        {
            var announced = false;
            while (!cancellationToken.IsCancellationRequested)
            {
                if (_holder.TryLoad(_options))
                {
                    _logger.LogInformation(
                        "UBAG trust plane: node certificate loaded ({Fingerprint}…), listener identity ready.",
                        _holder.ShortFingerprint);
                    return;
                }

                if (!announced)
                {
                    announced = true;
                    _logger.LogWarning(
                        "UBAG trust plane: certificate files are not readable yet; dialers are refused until they load.");
                }

                try
                {
                    await Task.Delay(RetryEvery, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    /// <summary>The minimal request surface. Registered only when the trust plane is enabled.</summary>
    public static void MapEndpoints(WebApplication app, TrustOptions options, CertificateHolder holder, ILogger logger)
    {
        app.Use(async (context, next) =>
        {
            var path = context.Request.Path.Value ?? "/";
            var clientPresented = context.Connection.ClientCertificate is not null;

            if (HttpMethods.IsGet(context.Request.Method) && path == "/healthz")
            {
                // A client certificate that failed the Kestrel validation never reaches here
                // (the handshake is refused), so presence here means "chained to the CA".
                context.Response.StatusCode = StatusCodes.Status200OK;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(System.Text.Json.JsonSerializer.Serialize(new
                {
                    schema = "oet-fleet-agent.ubag-health/1",
                    status = "serving",
                    clientCertChained = clientPresented,
                }));
                return;
            }

            if (HttpMethods.IsPost(context.Request.Method)
                && (path == "/grpc.health.v1.Health/Check" || path == "/grpc.health.v1.Health/Watch"
                    || path.StartsWith("/grpc.", StringComparison.Ordinal)))
            {
                // Drain the (tiny, length-prefixed) request message, then answer per gRPC's wire
                // format: a 5-byte frame header (compression flag + big-endian length) followed by
                // the message bytes. Health/Check answers SERVING (enum 1 = 0x08 0x01); everything
                // else answers UNIMPLEMENTED (status 12) with no message bytes.
                var served = path == "/grpc.health.v1.Health/Check";
                const int limit = 64 * 1024;
                var buffer = new byte[4096];
                var total = 0;
                while (true)
                {
                    var read = await context.Request.Body.ReadAsync(buffer, context.RequestAborted);
                    if (read == 0) break;
                    total += read;
                    if (total > limit)
                    {
                        context.Abort();
                        return;
                    }
                }

                context.Response.StatusCode = StatusCodes.Status200OK;
                context.Response.ContentType = "application/grpc";
                context.Response.AppendTrailer("grpc-status", served ? "0" : "12");
                if (!served)
                {
                    context.Response.AppendTrailer("grpc-message", "unimplemented");
                }

                if (served)
                {
                    await context.Response.Body.WriteAsync(
                        new byte[] { 0x00, 0x00, 0x00, 0x00, 0x02, 0x08, 0x01 }, context.RequestAborted);
                }

                logger.LogInformation(
                    "UBAG dial: {Path} from {Remote} (client cert chained: {Client})",
                    path, context.Connection.RemoteIpAddress, clientPresented);
                return;
            }

            context.Response.StatusCode = StatusCodes.Status404NotFound;
        });
    }
}
