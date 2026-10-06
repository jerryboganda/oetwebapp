using System.Net.Http.Headers;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Endpoints;
using OetLearner.Api.Security;
using OetLearner.Api.Services.Content;
using OetLearner.Api.Services.RemoteJobs;
using OetLearner.Api.Services.Settings;

namespace OetLearner.Api.Tests.RemoteJobs;

/// <summary>
/// A real ASP.NET Core pipeline (TestServer) carrying exactly the remote-worker pieces of <c>Program.cs</c>: the two bearer schemes,
/// the two policies, the protocol-header middleware and the job/service-plane endpoints. <see cref="StartAsync"/> runs over the InMemory
/// provider (nothing reaches PostgreSQL: authentication, protocol negotiation, rate limits and error mapping only);
/// <see cref="StartOnPostgresAsync"/> runs the same pipeline over a real PostgreSQL schema so whole requests execute the real SQL.
/// </summary>
internal sealed class RemoteHttpHost : IAsyncDisposable
{
    private readonly IHost _host;

    private RemoteHttpHost(IHost host, HttpClient client, MutableClock clock, FixedRemoteFlags flags)
    {
        _host = host;
        Client = client;
        Clock = clock;
        Flags = flags;
    }

    public HttpClient Client { get; }

    public MutableClock Clock { get; }

    public FixedRemoteFlags Flags { get; }

    public IServiceProvider Services => _host.Services;

    public static Task<RemoteHttpHost> StartAsync(
        IReadOnlyDictionary<string, string?>? config = null,
        params string[] enabledFlags)
        => StartCoreAsync(
            config,
            enabledFlags,
            new MutableClock(new DateTimeOffset(2026, 10, 5, 12, 0, 10, TimeSpan.Zero)),
            new InMemoryFileStorage(),
            options => options.UseInMemoryDatabase("remote-http-" + Guid.NewGuid().ToString("N")));

    /// <summary>
    /// The same pipeline over the harness's real PostgreSQL schema and the given storage, so a whole request (authentication, the
    /// lease guard, the streaming input, the fenced completion and its applier) runs against the real SQL. The clock starts at the
    /// real time so credentials seeded with the database clock are valid for the pipeline's own expiry checks.
    /// </summary>
    public static Task<RemoteHttpHost> StartOnPostgresAsync(
        RemotePgHarness harness,
        IFileStorage storage,
        IReadOnlyDictionary<string, string?>? config = null,
        params string[] enabledFlags)
        => StartCoreAsync(
            config,
            enabledFlags,
            new MutableClock(DateTimeOffset.UtcNow),
            storage,
            options => options.UseNpgsql(harness.Database.SchemaConnectionString, npgsql => npgsql.UseVector()));

    private static async Task<RemoteHttpHost> StartCoreAsync(
        IReadOnlyDictionary<string, string?>? config,
        string[] enabledFlags,
        MutableClock clock,
        IFileStorage storage,
        Action<DbContextOptionsBuilder> configureDatabase)
    {
        var flags = new FixedRemoteFlags(enabledFlags);
        var values = (config ?? new Dictionary<string, string?>()).ToDictionary(pair => pair.Key, pair => pair.Value);

        var host = await new HostBuilder()
            .ConfigureAppConfiguration(builder => builder.AddInMemoryCollection(values))
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureServices((context, services) =>
                {
                    services.AddLogging();
                    services.AddRouting();
                    services.AddSingleton<TimeProvider>(clock);
                    services.AddSingleton<IRemoteJobFlags>(flags);
                    services.AddSingleton(storage);
                    services.AddSingleton<IRuntimeSettingsProvider>(new TestRuntimeSettingsProvider(TestRuntimeSettingsProvider.Base()));
                    services.AddDbContext<LearnerDbContext>(configureDatabase);
                    services.AddAuthentication()
                        .AddScheme<AuthenticationSchemeOptions, RemoteWorkerAuthenticationHandler>(RemoteWorkerAuth.NodeScheme, _ => { })
                        .AddScheme<AuthenticationSchemeOptions, FleetServiceAuthenticationHandler>(RemoteWorkerAuth.FleetScheme, _ => { });
                    services.AddAuthorization(options => options.AddRemoteWorkerPolicies());
                    services.AddRemoteJobs(context.Configuration, isNpgsql: true, isWorker: false);

                    // No reaper, seeder or comparer: the pipeline under test is request-driven only.
                    services.RemoveAll<IHostedService>();
                });
                web.Configure(app =>
                {
                    app.UseRemoteWorkerProtocolHeaders();
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapRemoteWorkerEndpoints();
                        endpoints.MapRemoteFleetEndpoints();
                    });
                });
            })
            .StartAsync();

        return new RemoteHttpHost(host, host.GetTestClient(), clock, flags);
    }

    // ── seeding ──────────────────────────────────────────────────────────────

    public sealed record SeededNode(string NodeId, string Token, string TokenId);

    public async Task<SeededNode> AddNodeAsync(
        string status = RemoteNodeStatus.Active,
        bool revokedCredential = false,
        TimeSpan? credentialLifetime = null)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var now = Clock.GetUtcNow();
        var node = new RemoteWorker
        {
            Id = RemoteIds.NewNodeId(now),
            NodeRef = "node-" + Guid.NewGuid().ToString("N")[..10],
            DisplayName = "Test node",
            Status = status,
            StatusChangedAt = now,
            AllowedKinds = [RemoteJobKinds.PdfExtract],
            CreatedAt = now,
            UpdatedAt = now,
            CreatedBy = "test",
        };
        var token = RemoteTokenFormat.Generate(RemoteTokenFormat.NodeKind);
        db.RemoteWorkers.Add(node);
        db.RemoteCredentials.Add(new RemoteCredential
        {
            TokenId = token.TokenId,
            Kind = RemoteTokenFormat.NodeKind,
            NodeId = node.Id,
            SecretHash = token.SecretHashHex,
            CreatedAt = now,
            ExpiresAt = now + (credentialLifetime ?? TimeSpan.FromDays(30)),
            RevokedAt = revokedCredential ? now : null,
            CreatedBy = "test",
        });
        await db.SaveChangesAsync();
        return new SeededNode(node.Id, token.Token, token.TokenId);
    }

    public async Task<SeededNode> AddFleetCredentialAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var now = Clock.GetUtcNow();
        var token = RemoteTokenFormat.Generate(RemoteTokenFormat.FleetKind);
        db.RemoteCredentials.Add(new RemoteCredential
        {
            TokenId = token.TokenId,
            Kind = RemoteTokenFormat.FleetKind,
            NodeId = null,
            SecretHash = token.SecretHashHex,
            CreatedAt = now,
            ExpiresAt = now.AddDays(30),
            CreatedBy = "test",
        });
        await db.SaveChangesAsync();
        return new SeededNode(string.Empty, token.Token, token.TokenId);
    }

    public async Task RevokeAsync(string tokenId)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var credential = await db.RemoteCredentials.SingleAsync(c => c.TokenId == tokenId);
        credential.RevokedAt = Clock.GetUtcNow();
        await db.SaveChangesAsync();
    }

    // ── requests ─────────────────────────────────────────────────────────────

    public Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string path,
        string? bearer = null,
        string? protocol = "1",
        string? json = null,
        string? authorizationOverride = null,
        IReadOnlyDictionary<string, string>? headers = null)
    {
        var request = new HttpRequestMessage(method, path);
        if (headers is not null)
        {
            foreach (var (name, value) in headers) request.Headers.TryAddWithoutValidation(name, value);
        }

        if (authorizationOverride is not null)
        {
            request.Headers.TryAddWithoutValidation("Authorization", authorizationOverride);
        }
        else if (bearer is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        }

        if (protocol is not null) request.Headers.TryAddWithoutValidation(RemoteHeaders.Protocol, protocol);
        if (json is not null) request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        return Client.SendAsync(request);
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _host.StopAsync();
        _host.Dispose();
    }
}
