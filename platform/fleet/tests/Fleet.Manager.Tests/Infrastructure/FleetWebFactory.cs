using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Fleet.Core.Crypto;
using Fleet.Core.Validation;
using Fleet.Manager.Api;
using Fleet.Manager.Auth;
using Fleet.Manager.Endpoints;
using Fleet.Manager.Infrastructure;
using Fleet.Manager.Monitoring;
using Fleet.Manager.Provisioning;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Fleet.Manager.Tests.Infrastructure;

/// <summary>
/// The real web application (Program, middleware, Razor Pages, JSON API, cookie auth) on TestServer, over a temp SQLite file
/// and the fake outside world. Requests use an https base address because the session and antiforgery cookies are
/// <c>Secure</c> (the .NET cookie container never sends a Secure cookie over plain http).
/// </summary>
public sealed class FleetWebFactory : WebApplicationFactory<Program>
{
    private readonly string _root;
    private readonly Dictionary<string, string?> _settings;

    public FleetWebFactory(Action<Dictionary<string, string?>>? configure = null)
    {
        _root = Directory.CreateTempSubdirectory("fleet-web-").FullName;
        Directory.CreateDirectory(Path.Combine(_root, "data"));
        Directory.CreateDirectory(Path.Combine(_root, "secrets"));
        Directory.CreateDirectory(Path.Combine(_root, "scratch"));
        File.WriteAllBytes(Path.Combine(_root, "secrets", "fleet_master_key"), RandomNumberGenerator.GetBytes(32));
        _settings = new Dictionary<string, string?>
        {
            ["Fleet:Data:Directory"] = Path.Combine(_root, "data"),
            ["Fleet:Secrets:Directory"] = Path.Combine(_root, "secrets"),
            ["Fleet:Provisioning:ScratchDirectory"] = Path.Combine(_root, "scratch"),
            ["Fleet:Workers:Enabled"] = "false",
            ["Fleet:Binding:Mode"] = "loopback",
            ["Fleet:Auth:PasswordIterations"] = "220000",
            ["Fleet:Auth:LoginRatePerMinute"] = "100",
        };
        configure?.Invoke(_settings);
        World = new FleetWorld();
    }

    public FleetWorld World { get; }

    public ManualTimeProvider Time => World.Time;

    public string SecretsDirectory => Path.Combine(_root, "secrets");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(_settings));
        builder.ConfigureTestServices(services =>
        {
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
        });
    }

    public HttpClient CreateHttps() => CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false,
        HandleCookies = true,
    });

    public const string Password = "correct horse battery staple";

    /// <summary>Creates the owner and returns the TOTP secret (the manager only ever shows it once).</summary>
    public async Task<string> CreateOwnerAsync(string password = Password)
    {
        _ = Server; // make sure the host (and StartupChecks) ran
        var setup = await Services.GetRequiredService<OwnerAccountService>().CreateAsync(password, replace: false, CancellationToken.None);
        return setup.TotpSecretBase32;
    }

    /// <summary>The code an authenticator shows now (offset 0) or one step later/earlier, on the test clock.</summary>
    public string Code(string secretBase32, int stepOffset = 0) =>
        Totp.ComputeCode(Base32.Decode(secretBase32), Totp.StepAt(Time.GetUtcNow()) + stepOffset);

    public static async Task<string> AntiforgeryFieldAsync(HttpClient client, string path)
    {
        var html = await client.GetStringAsync(path);
        var match = Regex.Match(html, "<input[^>]*name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(match.Success, "the page carries no antiforgery field");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    public static async Task<HttpResponseMessage> PostLoginAsync(HttpClient client, string? password, string? code)
    {
        var token = await AntiforgeryFieldAsync(client, "/Login");
        return await client.PostAsync("/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Password"] = password ?? string.Empty,
            ["Code"] = code ?? string.Empty,
            ["__RequestVerificationToken"] = token,
        }));
    }

    /// <summary>A signed-in client (cookie jar) and the CSRF token its API calls need.</summary>
    public async Task<(HttpClient Client, string Secret, string Csrf)> SignedInAsync()
    {
        var secret = await CreateOwnerAsync();
        var client = CreateHttps();
        var response = await PostLoginAsync(client, Password, Code(secret));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var csrf = await CsrfAsync(client);
        return (client, secret, csrf);
    }

    public static async Task<string> CsrfAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/api/v1/csrf");
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("token").GetString()!;
    }

    public static HttpRequestMessage ApiRequest(HttpMethod method, string path, string csrf, string? totp = null, object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-Fleet-Csrf", csrf);
        if (totp is not null)
        {
            request.Headers.Add("X-Fleet-Totp", totp);
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return request;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing)
        {
            return;
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
}
