using System.Security.Cryptography;
using Fleet.Core.Validation;
using Fleet.Manager.Api;
using Fleet.Manager.Hosting;
using Fleet.Manager.Infrastructure;
using Fleet.Manager.Monitoring;
using Fleet.Manager.Provisioning;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Fleet.Manager.Tests.Infrastructure;

/// <summary>
/// The whole manager (everything <c>AddFleetManager</c> registers) over a real SQLite file in a temp directory, with the outside
/// world replaced by <see cref="FleetWorld"/>. <see cref="RestartAsync"/> drops every in-memory object and builds a new provider on
/// the SAME files, which is exactly what a killed and restarted manager looks like.
/// </summary>
public sealed class FleetTestHost : IAsyncDisposable
{
    private readonly string _root;
    private readonly Dictionary<string, string?> _configuration;

    private FleetTestHost(FleetWorld world, string root, Dictionary<string, string?> configuration)
    {
        World = world;
        _root = root;
        _configuration = configuration;
    }

    public FleetWorld World { get; }

    public LogCapture Logs { get; } = new();

    public ServiceProvider Services { get; private set; } = null!;

    public string DataDirectory => Path.Combine(_root, "data");

    public string SecretsDirectory => Path.Combine(_root, "secrets");

    public string DatabasePath => Path.Combine(DataDirectory, "fleet.db");

    public T Get<T>()
        where T : notnull => Services.GetRequiredService<T>();

    /// <summary>Changes a configuration value for the NEXT start (<see cref="RestartAsync"/>).</summary>
    public void Set(string key, string? value) => _configuration[key] = value;

    public static async Task<FleetTestHost> CreateAsync(Action<Dictionary<string, string?>>? configure = null, FleetWorld? world = null)
    {
        var root = Directory.CreateTempSubdirectory("fleet-tests-").FullName;
        Directory.CreateDirectory(Path.Combine(root, "data"));
        Directory.CreateDirectory(Path.Combine(root, "secrets"));
        Directory.CreateDirectory(Path.Combine(root, "scratch"));
        File.WriteAllBytes(Path.Combine(root, "secrets", "fleet_master_key"), RandomNumberGenerator.GetBytes(32));

        var configuration = new Dictionary<string, string?>
        {
            ["Fleet:Data:Directory"] = Path.Combine(root, "data"),
            ["Fleet:Secrets:Directory"] = Path.Combine(root, "secrets"),
            ["Fleet:Provisioning:ScratchDirectory"] = Path.Combine(root, "scratch"),
            ["Fleet:Workers:Enabled"] = "false",
            ["Fleet:Binding:Mode"] = "loopback",
            ["Fleet:Auth:PasswordIterations"] = "220000",
        };
        configure?.Invoke(configuration);

        var host = new FleetTestHost(world ?? new FleetWorld(), root, configuration);
        await host.StartAsync();
        return host;
    }

    /// <summary>Creates a manager over the same directory with its own world, for tests that only need the vault or the database.</summary>
    public async Task StartAsync()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(_configuration).Build();
        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.AddProvider(Logs);
            builder.SetMinimumLevel(LogLevel.Trace);
        });
        services.AddSingleton<IConfiguration>(configuration);
        services.AddFleetManager(configuration);

        services.RemoveAll<TimeProvider>();
        services.AddSingleton<TimeProvider>(World.Time);
        services.RemoveAll<IDelay>();
        services.AddSingleton<IDelay>(World.Delay);
        services.RemoveAll<IFleetApi>();
        services.AddSingleton<IFleetApi>(World.Api);
        services.RemoveAll<IProvisioner>();
        services.AddSingleton<IProvisioner>(World.Provisioner);
        services.RemoveAll<ISshKeyTool>();
        services.AddSingleton<ISshKeyTool>(World.Keys);
        services.RemoveAll<IHostResolver>();
        services.AddSingleton<IHostResolver>(World.Resolver);
        services.RemoveAll<IPrimaryPressureSource>();
        services.AddSingleton<IPrimaryPressureSource>(new FakePressureSource());

        Services = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var checks = Services.GetServices<IHostedService>().OfType<StartupChecks>().Single();
        await checks.StartAsync(CancellationToken.None);
    }

    /// <summary>Kill the manager and start it again on the same database, vault and secrets.</summary>
    public async Task RestartAsync()
    {
        await Services.DisposeAsync();
        SqliteConnection.ClearAllPools();
        await StartAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await Services.DisposeAsync();
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort: a leftover temp directory never fails a test.
        }
    }

    /// <summary>Every byte of the database file, its WAL and shared-memory file, for "no plaintext marker anywhere" assertions.</summary>
    public async Task<byte[]> ReadDatabaseBytesAsync()
    {
        using var buffer = new MemoryStream();
        foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
        {
            var path = DatabasePath + suffix;
            if (!File.Exists(path))
            {
                continue;
            }

            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            await stream.CopyToAsync(buffer);
        }

        return buffer.ToArray();
    }
}
