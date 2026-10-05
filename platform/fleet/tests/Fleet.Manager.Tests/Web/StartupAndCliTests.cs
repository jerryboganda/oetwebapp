using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Fleet.Core.Crypto;
using Fleet.Manager.Cli;
using Fleet.Manager.Configuration;
using Fleet.Manager.Hosting;
using Fleet.Manager.Monitoring;
using Fleet.Manager.Tests.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;

namespace Fleet.Manager.Tests.Web;

/// <summary>The listen-address policy of OET-RWP/1 section 8.8 (RW-143): no public ingress, ever.</summary>
public sealed class BindingPolicyTests
{
    [Theory]
    [InlineData("http://127.0.0.1:8080")]
    [InlineData("http://localhost:8080")]
    [InlineData("http://LOCALHOST:8080")]
    [InlineData("http://[::1]:8080")]
    [InlineData("http://127.0.0.5:9000")]
    [InlineData("https://127.0.0.1:8443")]
    [InlineData("http://127.0.0.1:8080;http://[::1]:8080")]
    [InlineData("http://127.0.0.1:8080, http://localhost:8081")]
    public void Loopback_mode_accepts_only_loopback_listeners(string urls)
    {
        Assert.Empty(BindingPolicy.Validate(urls, BindingOptions.LoopbackMode));
    }

    [Theory]
    [InlineData("http://0.0.0.0:8080")]
    [InlineData("http://+:8080")]
    [InlineData("http://*:8080")]
    [InlineData("http://[::]:8080")]
    [InlineData("http://192.168.1.5:8080")]
    [InlineData("http://10.0.0.1:80")]
    [InlineData("http://203.0.113.10:8080")]
    [InlineData("http://example.com:8080")]
    [InlineData("http://127.0.0.1.evil.example:8080")]
    [InlineData("http://localhost.evil.example:8080")]
    [InlineData("http://127.0.0.1:8080;http://0.0.0.0:9")]
    [InlineData("http://127.0.0.1")]
    [InlineData("127.0.0.1:8080")]
    [InlineData("ftp://127.0.0.1:21")]
    [InlineData("garbage")]
    public void Loopback_mode_refuses_everything_else_including_a_single_bad_entry_in_a_list(string urls)
    {
        Assert.NotEmpty(BindingPolicy.Validate(urls, BindingOptions.LoopbackMode));
    }

    [Theory]
    [InlineData("http://0.0.0.0:8080")]
    [InlineData("http://+:8080")]
    [InlineData("http://*:8080")]
    [InlineData("http://[::]:8080")]
    [InlineData("http://127.0.0.1:8080")]
    [InlineData("http://0.0.0.0:8080;http://[::]:8080")]
    public void Container_mode_also_accepts_the_wildcard_because_compose_publishes_the_port_on_loopback_only(string urls)
    {
        Assert.Empty(BindingPolicy.Validate(urls, BindingOptions.ContainerMode));
    }

    [Theory]
    [InlineData("http://203.0.113.10:8080")]
    [InlineData("http://192.168.1.5:8080")]
    [InlineData("http://example.com:8080")]
    [InlineData("http://0.0.0.0:8080;http://203.0.113.10:8080")]
    public void Container_mode_still_refuses_a_specific_non_loopback_address(string urls)
    {
        Assert.NotEmpty(BindingPolicy.Validate(urls, BindingOptions.ContainerMode));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void No_listen_address_is_a_violation(string? urls)
    {
        Assert.Contains(BindingPolicy.Validate(urls, BindingOptions.LoopbackMode), violation => violation.Contains("No listen address", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("public")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("Loopback")]
    public void Only_the_two_known_modes_exist(string? mode)
    {
        Assert.Contains(BindingPolicy.Validate("http://127.0.0.1:8080", mode), violation => violation.Contains("Fleet:Binding:Mode", StringComparison.Ordinal));
    }
}

/// <summary>Everything that must be true before the manager serves a request, in order, failing loudly (OET-RWP/1 sections 8.5, 8.8).</summary>
public sealed class StartupRefusalTests
{
    private static string NewSecretsDirectory(byte[]? keyBytes, string fileName = "fleet_master_key")
    {
        var directory = Directory.CreateTempSubdirectory("fleet-secrets-").FullName;
        if (keyBytes is not null)
        {
            File.WriteAllBytes(Path.Combine(directory, fileName), keyBytes);
        }

        return directory;
    }

    [Fact]
    public async Task A_public_listen_address_stops_the_manager_before_it_serves_anything()
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => FleetTestHost.CreateAsync(configure: settings => settings["Fleet:Binding:Urls"] = "http://203.0.113.10:8080"));

        Assert.Contains("Refusing to start", error.Message);
        Assert.Contains("non-loopback", error.Message);
    }

    [Fact]
    public async Task Container_mode_starts_with_the_wildcard_but_not_with_a_public_address()
    {
        await using (var host = await FleetTestHost.CreateAsync(configure: settings =>
                     {
                         settings["Fleet:Binding:Mode"] = "container";
                         settings["Fleet:Binding:Urls"] = "http://0.0.0.0:8080";
                     }))
        {
            Assert.NotNull(host.Get<FleetState>().Integrity);
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() => FleetTestHost.CreateAsync(configure: settings =>
        {
            settings["Fleet:Binding:Mode"] = "container";
            settings["Fleet:Binding:Urls"] = "http://203.0.113.10:8080";
        }));
    }

    [Fact]
    public async Task A_missing_master_key_stops_startup_and_a_key_is_never_generated()
    {
        var empty = NewSecretsDirectory(null);

        var error = await Assert.ThrowsAsync<VaultConfigurationException>(
            () => FleetTestHost.CreateAsync(configure: settings => settings["Fleet:Secrets:Directory"] = empty));

        Assert.Contains("missing or unreadable", error.Message);
        Assert.Empty(Directory.GetFiles(empty));
    }

    [Theory]
    [InlineData(0, "must hold exactly 32 bytes")]
    [InlineData(31, "must hold exactly 32 bytes")]
    [InlineData(33, "must hold exactly 32 bytes")]
    [InlineData(64, "must hold exactly 32 bytes")]
    public async Task A_master_key_of_the_wrong_length_stops_startup(int length, string message)
    {
        var directory = NewSecretsDirectory(Enumerable.Repeat((byte)'x', length).ToArray());

        var error = await Assert.ThrowsAsync<VaultConfigurationException>(
            () => FleetTestHost.CreateAsync(configure: settings => settings["Fleet:Secrets:Directory"] = directory));

        Assert.Contains(message, error.Message);
    }

    [Fact]
    public async Task An_all_zero_master_key_is_refused()
    {
        var directory = NewSecretsDirectory(new byte[32]);

        var error = await Assert.ThrowsAsync<VaultConfigurationException>(
            () => FleetTestHost.CreateAsync(configure: settings => settings["Fleet:Secrets:Directory"] = directory));

        Assert.Contains("all zero", error.Message);
    }

    [Fact]
    public async Task A_master_key_written_as_hex_or_base64_is_accepted_like_a_raw_one()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        foreach (var text in new[] { Convert.ToHexString(key), Convert.ToHexString(key).ToLowerInvariant() + "\n", Convert.ToBase64String(key) + "\n" })
        {
            var directory = NewSecretsDirectory(Encoding.ASCII.GetBytes(text));

            await using var host = await FleetTestHost.CreateAsync(configure: settings => settings["Fleet:Secrets:Directory"] = directory);

            Assert.NotNull(host.Get<FleetState>().Integrity);
        }
    }
}

[CollectionDefinition("fleet-cli", DisableParallelization = true)]
public sealed class FleetCliCollection
{
}

/// <summary>
/// The operator command line (<c>docker exec -i oet-fleet-manager dotnet Fleet.Manager.dll ...</c>). It reads its configuration from the
/// process environment and writes to the console, so these tests set both for their own duration and never run beside other tests.
/// </summary>
[Collection("fleet-cli")]
public sealed class FleetCliTests : IDisposable
{
    private const string Password = "correct horse battery staple";

    private readonly string _root = Directory.CreateTempSubdirectory("fleet-cli-").FullName;
    private readonly Dictionary<string, string?> _savedEnvironment = new(StringComparer.Ordinal);
    private readonly TextReader _in = Console.In;
    private readonly TextWriter _out = Console.Out;
    private readonly TextWriter _error = Console.Error;

    public FleetCliTests()
    {
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(SecretsDirectory);
        File.WriteAllBytes(Path.Combine(SecretsDirectory, "fleet_master_key"), RandomNumberGenerator.GetBytes(32));
        SetEnvironment("Fleet__Data__Directory", DataDirectory);
        SetEnvironment("Fleet__Secrets__Directory", SecretsDirectory);
    }

    private string DataDirectory => Path.Combine(_root, "data");

    private string SecretsDirectory => Path.Combine(_root, "secrets");

    private void SetEnvironment(string name, string? value)
    {
        _savedEnvironment.TryAdd(name, Environment.GetEnvironmentVariable(name));
        Environment.SetEnvironmentVariable(name, value);
    }

    public void Dispose()
    {
        Console.SetIn(_in);
        Console.SetOut(_out);
        Console.SetError(_error);
        foreach (var (name, value) in _savedEnvironment)
        {
            Environment.SetEnvironmentVariable(name, value);
        }

        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort.
        }
    }

    private async Task<(int Exit, string Out, string Error)> RunAsync(string stdin, params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        Console.SetOut(output);
        Console.SetError(error);
        Console.SetIn(new StringReader(stdin));
        try
        {
            var code = await FleetCli.TryRunAsync(args);
            return (code ?? int.MinValue, output.ToString(), error.ToString());
        }
        finally
        {
            Console.SetOut(_out);
            Console.SetError(_error);
            Console.SetIn(_in);
        }
    }

    private static async Task<WebApplication> StartAppAsync(Action<WebApplication> map)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var app = builder.Build();
        map(app);
        await app.StartAsync();
        return app;
    }

    // ---- recognising a command -------------------------------------------------------------

    [Fact]
    public async Task Help_prints_the_usage_and_anything_that_is_not_a_command_starts_the_web_host()
    {
        var help = await RunAsync(string.Empty, "help");
        Assert.Equal(0, help.Exit);
        Assert.Contains("owner-init", help.Out);
        Assert.Contains("verify-chain", help.Out);

        Assert.Equal(0, (await RunAsync(string.Empty, "--help")).Exit);
        Assert.Equal(int.MinValue, (await RunAsync(string.Empty)).Exit);
        Assert.Equal(int.MinValue, (await RunAsync(string.Empty, "serve")).Exit);
        Assert.Equal(int.MinValue, (await RunAsync(string.Empty, "--urls", "http://127.0.0.1:1")).Exit);
    }

    // ---- owner-init ------------------------------------------------------------------------

    [Fact]
    public async Task Owner_init_creates_the_owner_prints_the_totp_secret_once_and_never_the_password()
    {
        var result = await RunAsync(Password + "\n", "owner-init");

        Assert.Equal(0, result.Exit);
        Assert.Equal(string.Empty, result.Error);
        var lines = result.Out.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => line.TrimEnd('\r')).ToArray();
        Assert.Equal(3, lines.Length);
        Assert.Matches("^TOTP secret: [A-Z2-7]{32}$", lines[1]);
        Assert.StartsWith("URI: otpauth://totp/", lines[2]);
        Assert.DoesNotContain(Password, result.Out);

        var audit = await RunAsync(string.Empty, "verify-chain");
        Assert.Equal(0, audit.Exit);
        Assert.Contains("audit: intact (1 rows)", audit.Out);
    }

    [Fact]
    public async Task A_second_owner_init_is_refused_until_reset_is_given_and_reset_replaces_the_authenticator_secret()
    {
        var first = await RunAsync(Password + "\n", "owner-init");
        var firstSecret = Regex.Match(first.Out, "TOTP secret: (?<secret>[A-Z2-7]{32})").Groups["secret"].Value;

        var refused = await RunAsync("another long password for the owner\n", "owner-init");
        Assert.Equal(2, refused.Exit);
        Assert.Contains("An owner already exists", refused.Error);
        Assert.DoesNotContain("TOTP secret", refused.Out);

        var reset = await RunAsync("another long password for the owner\n", "owner-init", "--reset");
        Assert.Equal(0, reset.Exit);
        var secondSecret = Regex.Match(reset.Out, "TOTP secret: (?<secret>[A-Z2-7]{32})").Groups["secret"].Value;
        Assert.NotEqual(firstSecret, secondSecret);
    }

    [Theory]
    [InlineData("short\n")]
    [InlineData("\n")]
    [InlineData("")]
    public async Task A_weak_or_missing_password_is_refused_and_creates_nothing(string stdin)
    {
        var weak = await RunAsync(stdin, "owner-init");

        Assert.Equal(2, weak.Exit);
        Assert.Contains("password", weak.Error);
        Assert.DoesNotContain("TOTP secret", weak.Out);

        // Nothing was created, so a proper first run still works.
        Assert.Equal(0, (await RunAsync(Password + "\n", "owner-init")).Exit);
    }

    [Fact]
    public async Task Owner_init_without_a_master_key_fails_loudly_and_generates_none()
    {
        File.Delete(Path.Combine(SecretsDirectory, "fleet_master_key"));

        var result = await RunAsync(Password + "\n", "owner-init");

        Assert.Equal(2, result.Exit);
        Assert.Contains("Master key file", result.Error);
        Assert.False(File.Exists(Path.Combine(SecretsDirectory, "fleet_master_key")));
    }

    // ---- chain and vault commands ----------------------------------------------------------

    [Fact]
    public async Task Verify_chain_reports_intact_chains_and_exits_non_zero_when_a_row_was_tampered_with()
    {
        var empty = await RunAsync(string.Empty, "verify-chain");
        Assert.Equal(0, empty.Exit);
        Assert.Contains("audit: intact (0 rows)", empty.Out);
        Assert.Contains("operations: intact (0 rows)", empty.Out);

        await RunAsync(Password + "\n", "owner-init");
        await using (var connection = new SqliteConnection("Data Source=" + Path.Combine(DataDirectory, "fleet.db")))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "UPDATE audit SET action = 'forged' WHERE id = 1";
            await command.ExecuteNonQueryAsync();
        }

        SqliteConnection.ClearAllPools();
        var tampered = await RunAsync(string.Empty, "verify-chain");
        Assert.Equal(1, tampered.Exit);
        Assert.Contains("audit: BROKEN", tampered.Out);
        Assert.Contains("operations: intact", tampered.Out);
    }

    [Fact]
    public async Task Vault_rewrap_reports_how_many_records_it_re_encrypted()
    {
        var result = await RunAsync(string.Empty, "vault-rewrap");

        Assert.Equal(0, result.Exit);
        Assert.Contains("Re-wrapped 0 vault record(s)", result.Out);
    }

    // ---- healthcheck -----------------------------------------------------------------------

    [Fact]
    public async Task The_container_healthcheck_is_green_only_for_a_healthy_listener()
    {
        await using var healthy = await StartAppAsync(app => app.MapGet("/healthz", () => Results.Ok()));
        SetEnvironment("ASPNETCORE_URLS", healthy.Urls.First());
        Assert.Equal(0, (await RunAsync(string.Empty, "healthcheck")).Exit);

        await using var degraded = await StartAppAsync(app => app.MapGet("/healthz", () => Results.StatusCode(503)));
        SetEnvironment("ASPNETCORE_URLS", degraded.Urls.First());
        Assert.Equal(1, (await RunAsync(string.Empty, "healthcheck")).Exit);

        SetEnvironment("ASPNETCORE_URLS", "http://127.0.0.1:1");
        Assert.Equal(1, (await RunAsync(string.Empty, "healthcheck")).Exit);
    }

    // ---- sync-stdin ------------------------------------------------------------------------

    [Fact]
    public async Task Sync_forwards_the_ci_document_with_the_token_from_the_secret_file_and_echoes_only_the_status()
    {
        const string token = "sync-secret-token-value";
        const string body = "{\"record\":{\"sha\":\"1\"},\"registryUsername\":\"ci-user\",\"registryToken\":\"ghs_TESTONLYTOKEN0123456789abcdef\"}";
        string? seenAuthorization = null;
        string? seenBody = null;
        await using var server = await StartAppAsync(app => app.MapPost("/internal/sync", async (HttpContext http) =>
        {
            seenAuthorization = http.Request.Headers.Authorization.ToString();
            using var reader = new StreamReader(http.Request.Body);
            seenBody = await reader.ReadToEndAsync();
            return Results.Json(new { id = "rel_x", approved = false });
        }));
        SetEnvironment("ASPNETCORE_URLS", server.Urls.First());
        await File.WriteAllTextAsync(Path.Combine(SecretsDirectory, "fleet_sync_token"), token + "\n");

        var result = await RunAsync(body, "sync-stdin");

        Assert.Equal(0, result.Exit);
        Assert.StartsWith("sync: 200", result.Out.Trim());
        Assert.Equal("Bearer " + token, seenAuthorization);
        Assert.Equal(body, seenBody);
        Assert.DoesNotContain(token, result.Out + result.Error);
        Assert.DoesNotContain("ghs_TESTONLYTOKEN", result.Out + result.Error);
    }

    [Fact]
    public async Task Sync_without_a_token_file_is_disabled_and_a_refusal_or_an_unreachable_manager_is_a_one_line_failure()
    {
        var missing = await RunAsync("{}", "sync-stdin");
        Assert.Equal(2, missing.Exit);
        Assert.Contains("sync token file is missing", missing.Error);

        await File.WriteAllTextAsync(Path.Combine(SecretsDirectory, "fleet_sync_token"), "sync-secret-token-value\n");
        await using var refusing = await StartAppAsync(app => app.MapPost("/internal/sync", () => Results.Unauthorized()));
        SetEnvironment("ASPNETCORE_URLS", refusing.Urls.First());
        var refused = await RunAsync("{}", "sync-stdin");
        Assert.Equal(1, refused.Exit);
        Assert.StartsWith("sync: 401", refused.Out.Trim());

        SetEnvironment("ASPNETCORE_URLS", "http://127.0.0.1:1");
        var unreachable = await RunAsync("{}", "sync-stdin");
        Assert.Equal(1, unreachable.Exit);
        Assert.Contains("could not be reached", unreachable.Error);
        Assert.DoesNotContain("   at ", unreachable.Error);
    }
}
