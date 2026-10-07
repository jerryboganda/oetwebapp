using System.Net;
using System.Security.Authentication;
using Fleet.Agent.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Https;

namespace Fleet.Agent;

/// <summary>
/// Entry point. Two modes share one binary: the long-running agent, and the isolated per-job worker
/// (<c>--child</c>) that the agent spawns for CPU-heavy parsing so a poison input can never take the agent down.
/// Exit codes: 0 normal, 2 invalid configuration, 3 superseded by a newer instance (the manager does not restart it).
/// </summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--child") return ChildEntry.Run(args);
        if (args.Length > 0 && args[0] == "--version")
        {
            Console.Out.WriteLine(AgentIdentity.CurrentVersion());
            Console.Out.WriteLine(EngineVersions.Pdf);
            return 0;
        }

        return await AgentHost.RunAsync(args).ConfigureAwait(false);
    }
}

internal static class AgentHost
{
    public static async Task<int> RunAsync(string[] args)
    {
        var (options, problems) = AgentOptions.FromEnvironment(name => Environment.GetEnvironmentVariable(name));
        if (options is null)
        {
            // Problem codes never contain a value, so a malformed token cannot leak here.
            foreach (var problem in problems) Console.Error.WriteLine("configuration error: " + problem);
            return 2;
        }

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args });
        builder.Logging.ClearProviders();
        builder.Logging.AddSimpleConsole(console =>
        {
            console.SingleLine = true;
            console.UseUtcTimestamp = true;
            console.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ ";
        });
        builder.Logging.SetMinimumLevel(options.LogLevel);
        // Container stop grace is 90 s; the graceful path of section 5.5 needs up to ~80 s.
        builder.Services.Configure<HostOptions>(host => host.ShutdownTimeout = TimeSpan.FromSeconds(85));
        builder.Services.AddSingleton(options);

        var trust = options.Trust;
        var holder = new UbagListener.CertificateHolder();
        builder.Services.AddSingleton(holder);
        if (trust.Enabled)
        {
            ConfigureTrustListener(builder, trust, holder);
            builder.Services.AddHostedService<UbagListener.CertificateLoader>();
        }
        else
        {
            // No trust plane: make sure the web host binds nothing reachable (loopback port 0 inside the
            // container is invisible — the container publishes no ports in this mode).
            builder.WebHost.UseUrls("http://127.0.0.1:0");
        }

        builder.Services.AddHostedService<AgentService>();

        var app = builder.Build();
        if (trust.Enabled)
        {
            UbagListener.MapEndpoints(app, trust, holder, app.Logger);
        }

        await app.RunAsync().ConfigureAwait(false);
        return AgentService.ExitCode;
    }

    /// <summary>
    /// The UBAG dial plane (decision D3): TLS 1.2/1.3 with ALPN h2/http1, our node certificate selected per
    /// connection (the files may still be loading), and a presented client certificate accepted only when it
    /// chains to the manager CA. Binding happens at start; connections before the certificate loads are
    /// refused at the handshake and retried by the dialer — the OET job plane is never blocked on it.
    /// </summary>
    private static void ConfigureTrustListener(WebApplicationBuilder builder, TrustOptions trust, UbagListener.CertificateHolder holder)
    {
        builder.WebHost.UseKestrel(kestrel =>
        {
            kestrel.Listen(IPAddress.Any, trust.Port, listen =>
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
    }
}

internal sealed class AgentService : IHostedService
{
    private readonly AgentOptions _options;
    private readonly ILoggerFactory _loggers;
    private readonly IHostApplicationLifetime _lifetime;
    private AgentRuntime? _runtime;

    public AgentService(AgentOptions options, ILoggerFactory loggers, IHostApplicationLifetime lifetime)
    {
        _options = options;
        _loggers = loggers;
        _lifetime = lifetime;
    }

    /// <summary>Process exit code decided while running (3 after a 409 instance_superseded).</summary>
    public static volatile int ExitCode;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var version = AgentIdentity.CurrentVersion();
        var negotiator = new ProtocolNegotiator();
        var api = new HttpRemoteWorkerApi(HttpRemoteWorkerApi.CreateHttpClient(_options.ApiBase), _options.NodeToken, version, negotiator);
        var identity = new AgentIdentity(version, _options.ImageDigest);
        _runtime = new AgentRuntime(_options, api, negotiator, identity, _loggers, new SystemMonotonicClock(), new ProcHostMetrics(),
            new ProcessChildRunner(), new SystemProcessRunner());
        _runtime.ExitRequested += code =>
        {
            ExitCode = code;
            _lifetime.StopApplication();
        };
        _runtime.Start();
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_runtime is not null) await _runtime.StopAsync(cancellationToken).ConfigureAwait(false);
    }
}
