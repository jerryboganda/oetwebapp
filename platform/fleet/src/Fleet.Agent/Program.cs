using Microsoft.Extensions.Options;

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

        var builder = Host.CreateApplicationBuilder(args);
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
        builder.Services.AddHostedService<AgentService>();

        using var app = builder.Build();
        await app.RunAsync().ConfigureAwait(false);
        return AgentService.ExitCode;
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
