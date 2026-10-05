using System.Collections.Concurrent;
using System.Net;
using Fleet.Core.Placement;
using Fleet.Core.Validation;
using Fleet.Manager.Monitoring;
using Microsoft.Extensions.Logging;

namespace Fleet.Manager.Tests.Infrastructure;

/// <summary>Everything outside the manager process: the clock, the OET API, the helpers, the registry. It survives a manager "restart".</summary>
public sealed class FleetWorld
{
    public static readonly string AgentDigest = "sha256:" + new string('a', 64);
    public static readonly string AgentImageId = "sha256:" + new string('b', 64);
    public static readonly string NextDigest = "sha256:" + new string('c', 64);
    public static readonly string NextImageId = "sha256:" + new string('d', 64);

    public FleetWorld()
    {
        Time = new ManualTimeProvider();
        Api = new FakeFleetApi(Time);
        Provisioner = new FakeProvisioner();
        Keys = new FakeSshKeyTool();
        Resolver = new FakeHostResolver();
        Delay = new AdvancingDelay(Time);
        Provisioner.OnAgentStart = StartAgent;
        PublishAgentImage(AgentDigest, AgentImageId);
    }

    public ManualTimeProvider Time { get; }

    public FakeFleetApi Api { get; }

    public FakeProvisioner Provisioner { get; }

    public FakeSshKeyTool Keys { get; }

    public FakeHostResolver Resolver { get; }

    public AdvancingDelay Delay { get; }

    public void PublishAgentImage(string digest, string imageId) => Provisioner.Registry[digest] = imageId;

    /// <summary>The agent container starts, reads its env file and heartbeats with its node token (it is rejected when the token is not valid).</summary>
    private void StartAgent(FakeHost host, string envText, string digest)
    {
        var values = envText
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split('=', 2))
            .Where(parts => parts.Length == 2)
            .ToDictionary(parts => parts[0], parts => parts[1], StringComparer.Ordinal);
        if (values.TryGetValue("OET_NODE_ID", out var nodeId) && values.TryGetValue("OET_NODE_TOKEN", out var token))
        {
            // Every start is a new process, and a new process generates a new instance id.
            Api.AgentHeartbeat(nodeId, token, values.GetValueOrDefault("OET_AGENT_IMAGE_DIGEST", digest), instanceId: Guid.NewGuid().ToString("D"));
        }
    }
}

public sealed class FakeHostResolver : IHostResolver
{
    private readonly Dictionary<string, IPAddress[]> _records = new(StringComparer.OrdinalIgnoreCase);

    public void Add(string hostname, params string[] addresses) =>
        _records[hostname] = addresses.Select(IPAddress.Parse).ToArray();

    public Task<IReadOnlyList<IPAddress>> ResolveAsync(string hostname, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<IPAddress>>(_records.TryGetValue(hostname, out var found) ? found : Array.Empty<IPAddress>());
}

public sealed class FakePressureSource : IPrimaryPressureSource
{
    public PrimaryPressure? Pressure { get; set; } = new(5, 0.5, 40);

    public PrimaryPressure? Read() => Pressure;
}

/// <summary>Collects every formatted log line (and exception text) so tests can prove nothing secret was logged.</summary>
public sealed class LogCapture : ILoggerProvider
{
    private readonly ConcurrentQueue<string> _lines = new();

    public IReadOnlyList<string> Lines => _lines.ToArray();

    public string All => string.Join('\n', _lines);

    public ILogger CreateLogger(string categoryName) => new CaptureLogger(_lines, categoryName);

    public void Dispose()
    {
    }

    private sealed class CaptureLogger : ILogger
    {
        private readonly ConcurrentQueue<string> _sink;
        private readonly string _category;

        public CaptureLogger(ConcurrentQueue<string> sink, string category)
        {
            _sink = sink;
            _category = category;
        }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            _sink.Enqueue(_category + " " + logLevel + ": " + formatter(state, exception) + (exception is null ? string.Empty : " | " + exception));
        }
    }
}
