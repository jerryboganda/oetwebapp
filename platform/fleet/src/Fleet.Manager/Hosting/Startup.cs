using System.Text.RegularExpressions;
using Fleet.Core.Crypto;
using Fleet.Manager.Configuration;
using Fleet.Manager.Monitoring;
using Fleet.Manager.Operations;
using Fleet.Manager.Persistence;
using Fleet.Manager.Vault;
using Microsoft.Extensions.Options;

namespace Fleet.Manager.Hosting;

/// <summary>
/// The listen-address policy (OET-RWP/1 section 8.8, RW-143). The manager has NO public ingress.
/// <c>loopback</c> mode accepts only loopback addresses. <c>container</c> mode additionally accepts the
/// wildcard because inside its own network namespace the process must listen on all interfaces for the port
/// to be reachable at all, and the compose file publishes that port on 127.0.0.1 of the host only (the
/// container is attached to no production network). Anything else, including a specific public address, is refused.
/// </summary>
public static class BindingPolicy
{
    private static readonly Regex UrlPattern = new(
        @"\A(?<scheme>https?)://(?<host>\+|\*|\[[0-9A-Fa-f:.]+\]|[A-Za-z0-9.-]+):(?<port>[0-9]{1,5})\z",
        RegexOptions.CultureInvariant);

    private static readonly Regex Loopback4 = new(@"\A127\.[0-9]{1,3}\.[0-9]{1,3}\.[0-9]{1,3}\z", RegexOptions.CultureInvariant);

    public static IReadOnlyList<string> Validate(string? urls, string? mode)
    {
        var violations = new List<string>();
        if (!string.Equals(mode, BindingOptions.LoopbackMode, StringComparison.Ordinal)
            && !string.Equals(mode, BindingOptions.ContainerMode, StringComparison.Ordinal))
        {
            violations.Add("Fleet:Binding:Mode must be 'loopback' or 'container'.");
        }

        if (string.IsNullOrWhiteSpace(urls))
        {
            violations.Add("No listen address is configured.");
            return violations;
        }

        var container = string.Equals(mode, BindingOptions.ContainerMode, StringComparison.Ordinal);
        foreach (var entry in urls.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var match = UrlPattern.Match(entry);
            if (!match.Success)
            {
                violations.Add("'" + entry + "' is not a recognised listen address.");
                continue;
            }

            var host = match.Groups["host"].Value;
            var isLoopback = host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || host == "[::1]" || Loopback4.IsMatch(host);
            var isWildcard = host is "0.0.0.0" or "[::]" or "+" or "*";
            if (isLoopback || (container && isWildcard))
            {
                continue;
            }

            violations.Add("'" + entry + "' would listen on a non-loopback address.");
        }

        return violations;
    }
}

/// <summary>
/// Everything that must be true before the manager serves a request, in order, failing loudly (the
/// process exits rather than starting in a half-secure state): listen policy, master key present and
/// 32 bytes, database created or upgraded, every vault record decryptable, audit and operations hash
/// chains intact, interrupted steps re-queued.
/// </summary>
public sealed class StartupChecks : IHostedService
{
    private readonly IServiceProvider _services;
    private readonly IConfiguration _configuration;
    private readonly IOptions<FleetOptions> _options;
    private readonly ILogger<StartupChecks> _logger;

    public StartupChecks(
        IServiceProvider services,
        IConfiguration configuration,
        IOptions<FleetOptions> options,
        ILogger<StartupChecks> logger)
    {
        _services = services;
        _configuration = configuration;
        _options = options;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var options = _options.Value;
        var urls = _configuration["urls"] ?? _configuration["ASPNETCORE_URLS"] ?? options.Binding.Urls;
        var violations = BindingPolicy.Validate(urls, options.Binding.Mode);
        if (violations.Count > 0)
        {
            var message = "Refusing to start: " + string.Join(" ", violations);
            _logger.LogCritical("{Message}", message);
            throw new InvalidOperationException(message);
        }

        MasterKeyRing keys;
        try
        {
            keys = _services.GetRequiredService<MasterKeyRing>();
        }
        catch (VaultConfigurationException ex)
        {
            _logger.LogCritical("Refusing to start: {Message}", ex.Message);
            throw;
        }

        Directory.CreateDirectory(options.Data.Directory);
        await _services.GetRequiredService<SchemaManager>().InitializeAsync(cancellationToken);
        await _services.GetRequiredService<CredentialStore>().EnsureKeysAvailableAsync(cancellationToken);

        var audit = await _services.GetRequiredService<IAuditService>().VerifyAsync(cancellationToken);
        var operations = await _services.GetRequiredService<OperationStore>().VerifyChainAsync(cancellationToken);
        var intact = audit.Intact && operations.Intact;
        var problem = intact
            ? null
            : (audit.Intact ? null : "audit: " + audit.Problem + " (row " + audit.FirstBadId + ")")
              + (operations.Intact ? null : " operations: " + operations.Problem + " (seq " + operations.FirstBadId + ")");
        _services.GetRequiredService<FleetState>().Integrity = new IntegrityStatus(
            audit.Intact,
            operations.Intact,
            problem,
            keys.CurrentKeyId,
            _services.GetRequiredService<TimeProvider>().GetUtcNow());

        if (!intact)
        {
            _logger.LogCritical("Tamper evidence: {Problem}", problem);
            if (!options.Audit.AllowBrokenChain)
            {
                throw new InvalidOperationException("The audit or operations hash chain is broken (" + problem + "). Set Fleet:Audit:AllowBrokenChain=true only to boot for a forensic look.");
            }
        }

        await _services.GetRequiredService<OperationRunner>().RecoverAsync(cancellationToken);
        _logger.LogInformation("Fleet manager startup checks passed (master key {KeyId}).", keys.CurrentKeyId.ToString("x8"));
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>Advances operations in the background. Each pass runs every operation that can make progress, then sleeps until kicked or the idle interval passes.</summary>
public sealed class OperationWorker : BackgroundService
{
    private readonly OperationStore _store;
    private readonly OperationRunner _runner;
    private readonly OperationSignal _signal;
    private readonly RolloutTokenHolder _tokens;
    private readonly CredentialStore _credentials;
    private readonly IOptions<FleetOptions> _options;
    private readonly ILogger<OperationWorker> _logger;

    public OperationWorker(
        OperationStore store,
        OperationRunner runner,
        OperationSignal signal,
        RolloutTokenHolder tokens,
        CredentialStore credentials,
        IOptions<FleetOptions> options,
        ILogger<OperationWorker> logger)
    {
        _store = store;
        _runner = runner;
        _signal = signal;
        _tokens = tokens;
        _credentials = credentials;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Value.Workers.Enabled)
        {
            return;
        }

        var idle = TimeSpan.FromSeconds(Math.Max(1, _options.Value.Timing.WorkerIdleSeconds));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Erase owner credentials past their 60-minute lifetime even when nothing is running.
                await _credentials.PurgeExpiredOwnerCredentialsAsync(stoppingToken);
                foreach (var id in await _store.ListRunnableIdsAsync(_tokens.HasToken, stoppingToken))
                {
                    if (stoppingToken.IsCancellationRequested)
                    {
                        break;
                    }

                    await _runner.RunAsync(id, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError("Operation worker pass failed: {Type}", ex.GetType().Name);
            }

            try
            {
                await _signal.WaitAsync(idle, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
