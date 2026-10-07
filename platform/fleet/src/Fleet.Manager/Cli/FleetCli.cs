using System.Net.Http.Headers;
using System.Text;
using Fleet.Core.Validation;
using Fleet.Manager.Configuration;
using Fleet.Manager.Hosting;
using Fleet.Manager.Infrastructure;
using Fleet.Manager.Operations;
using Fleet.Manager.Persistence;
using Fleet.Manager.Auth;
using Microsoft.Extensions.Options;

namespace Fleet.Manager.Cli;

/// <summary>
/// The operator's command line, run inside the container (<c>docker exec -i oet-fleet-manager dotnet Fleet.Manager.dll ...</c>):
/// <list type="bullet">
/// <item><c>owner-init [--reset]</c>: reads the new password from the first line of stdin, creates the owner and prints the TOTP secret ONCE;</item>
/// <item><c>healthcheck</c>: the container health probe (GET /healthz on the loopback listener);</item>
/// <item><c>sync-stdin</c>: forwards a CI sync document from stdin to the running manager, authenticated by the sync token file;</item>
/// <item><c>vault-rewrap</c>: re-encrypts every vault record under the current master key (after key rotation);</item>
/// <item><c>verify-chain</c>: verifies the audit and operations hash chains.</item>
/// </list>
/// Nothing here prints a secret except the one-time TOTP secret of <c>owner-init</c>.
/// </summary>
public static class FleetCli
{
    /// <summary>Returns an exit code when <paramref name="args"/> is a CLI command, otherwise null (start the web host).</summary>
    public static async Task<int?> TryRunAsync(string[] args)
    {
        if (args.Length == 0)
        {
            return null;
        }

        // The operator's node/job surface (status, nodes, inspect, add, drain, enable, disable,
        // remove, test, upgrade, rotate-token, policy, jobs, job, requeue, force-local, cancel,
        // rebalance, operations, op). Runbook: docs/VPS_FLEET.md.
        var nodeExit = await FleetNodeCli.TryRunAsync(args);
        if (nodeExit is int)
        {
            return nodeExit;
        }

        switch (args[0])
        {
            case "healthcheck":
                return await HealthcheckAsync();
            case "owner-init":
                return await OwnerInitAsync(args.Skip(1).ToArray());
            case "sync-stdin":
                return await SyncStdinAsync();
            case "vault-rewrap":
                return await RunWithServicesAsync(async (services, cancellationToken) =>
                {
                    var count = await services.GetRequiredService<Vault.CredentialStore>().RewrapAllAsync(cancellationToken);
                    Console.WriteLine("Re-wrapped " + count + " vault record(s) under the current master key. You may now remove fleet_master_key_prev.");
                    return 0;
                });
            case "verify-chain":
                return await RunWithServicesAsync(async (services, cancellationToken) =>
                {
                    var audit = await services.GetRequiredService<IAuditService>().VerifyAsync(cancellationToken);
                    var operations = await services.GetRequiredService<OperationStore>().VerifyChainAsync(cancellationToken);
                    Console.WriteLine("audit: " + (audit.Intact ? "intact" : "BROKEN " + audit.Problem) + " (" + audit.Checked + " rows)");
                    Console.WriteLine("operations: " + (operations.Intact ? "intact" : "BROKEN " + operations.Problem) + " (" + operations.Checked + " rows)");
                    return audit.Intact && operations.Intact ? 0 : 1;
                });
            case "help":
            case "--help":
                Console.WriteLine(
                    "Usage: Fleet.Manager <owner-init [--reset] | healthcheck | sync-stdin | vault-rewrap | verify-chain>\n"
                    + "       Fleet.Manager <status | nodes | inspect | operations | op | add | drain | enable | disable\n"
                    + "                      | remove | rotate-token | test | upgrade | policy | jobs | job\n"
                    + "                      | requeue | force-local | cancel | rebalance> [...]\n"
                    + "Node/job verbs: runbook docs/VPS_FLEET.md; privileged ones need a fresh TOTP (--totp or first stdin line).");
                return 0;
            default:
                return null;
        }
    }

    /// <summary>Shared runner for the node/job verbs: builds the service provider, prints one clean error line, maps exceptions to exit codes.</summary>
    internal static async Task<int> RunWithServicesAsync(Func<IServiceProvider, CancellationToken, Task<int>> action)
    {
        var builder = Host.CreateApplicationBuilder();

        // An operator command prints its result and nothing else: informational logs would be interleaved with it
        // (and with the one-time TOTP secret). Warnings and errors still reach stderr.
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Warning);
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.Services.AddFleetManager(builder.Configuration);
        using var host = builder.Build();
        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };

        try
        {
            await host.Services.GetRequiredService<SchemaManager>().InitializeAsync(cancellation.Token);
            return await action(host.Services, cancellation.Token);
        }
        catch (Exception ex) when (ex is
            Fleet.Core.Crypto.VaultConfigurationException
            or InvalidOperationException
            or FleetValidationException
            or FleetNodeCli.FleetCliUsage
            or FleetOperationException
            or FleetNotFoundException
            or Api.FleetApiException)
        {
            switch (ex)
            {
                case FleetValidationException validation:
                    foreach (var issue in validation.Issues)
                    {
                        Console.Error.WriteLine("invalid " + issue.Field + ": " + issue.Message);
                    }

                    return 2;
                case Api.FleetApiException api:
                    Console.Error.WriteLine("OET API " + api.Code + ": " + api.Message);
                    return 1;
                default:
                    Console.Error.WriteLine(ex.Message);
                    return ex is FleetNodeCli.FleetCliUsage ? 2 : 1;
            }
        }
    }

    /// <summary>Polling sleep for the watch verbs; honours Ctrl-C immediately.</summary>
    internal static Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
        Task.Delay(delay, cancellationToken);

    private static async Task<int> OwnerInitAsync(string[] flags)
    {
        var replace = flags.Contains("--reset", StringComparer.Ordinal);
        var password = (await Console.In.ReadLineAsync())?.TrimEnd('\r', '\n');
        return await RunWithServicesAsync(async (services, cancellationToken) =>
        {
            var owner = services.GetRequiredService<OwnerAccountService>();
            if (!replace && await owner.HasOwnerAsync(cancellationToken))
            {
                Console.Error.WriteLine("An owner already exists. Run again with --reset to replace it (this also replaces the authenticator secret).");
                return 2;
            }

            var setup = await owner.CreateAsync(password, replace, cancellationToken);
            Console.WriteLine("Owner account ready. Add this to your authenticator app NOW; the secret is not shown again.");
            Console.WriteLine("TOTP secret: " + setup.TotpSecretBase32);
            Console.WriteLine("URI: " + setup.ProvisioningUri);
            return 0;
        });
    }

    private static async Task<int> HealthcheckAsync()
    {
        var urls = Environment.GetEnvironmentVariable("ASPNETCORE_URLS") ?? "http://127.0.0.1:8080";
        var first = urls.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries)[0];
        var port = first[(first.LastIndexOf(':') + 1)..];
        if (!int.TryParse(port, out var number))
        {
            number = 8080;
        }

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            using var response = await http.GetAsync("http://127.0.0.1:" + number + "/healthz");
            return response.IsSuccessStatusCode ? 0 : 1;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return 1;
        }
    }

    private static async Task<int> SyncStdinAsync()
    {
        var body = await Console.In.ReadToEndAsync();
        var options = new FleetOptions();
        new ConfigurationBuilder().AddEnvironmentVariables().Build().GetSection(FleetOptions.SectionName).Bind(options);
        var token = SecretFile.TryRead(Path.Combine(options.Secrets.Directory, options.Secrets.SyncTokenFile));
        if (token is null)
        {
            Console.Error.WriteLine("The sync token file is missing; the sync endpoint is disabled.");
            return 2;
        }

        var urls = Environment.GetEnvironmentVariable("ASPNETCORE_URLS") ?? "http://127.0.0.1:8080";
        var first = urls.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries)[0];
        var port = first[(first.LastIndexOf(':') + 1)..];
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        using var request = new HttpRequestMessage(HttpMethod.Post, "http://127.0.0.1:" + port + "/internal/sync")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        try
        {
            using var response = await http.SendAsync(request);
            // Only the status line is echoed: the response body of a failed call could quote parts of the request.
            Console.WriteLine("sync: " + (int)response.StatusCode + " " + response.ReasonPhrase);
            return response.IsSuccessStatusCode ? 0 : 1;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // A failed CI step should say so in one line, not with a stack trace.
            Console.Error.WriteLine("sync: the manager could not be reached.");
            return 1;
        }
    }
}
