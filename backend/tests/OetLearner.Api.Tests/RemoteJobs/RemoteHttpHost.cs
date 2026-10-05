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
/// the two policies, the protocol-header middleware and the job/service-plane endpoints, over the InMemory provider. Nothing here
/// reaches PostgreSQL, so it exercises authentication, protocol negotiation, rate limits and error mapping only.
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

    public static async Task<RemoteHttpHost> StartAsync(
        IReadOnlyDictionary<string, string?>? config = null,
        params string[] enabledFlags)
    {
        var clock = new MutableClock(new DateTimeOffset(2026, 10, 5, 12, 0, 10, TimeSpan.Zero));
        var flags = new FixedRemoteFlags(enabledFlags);
        var dbName = "remote-http-" + Guid.NewGuid().ToString("N");
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
                    services.AddSingleton<IFileStorage>(new InMemoryFileStorage());
                    services.AddSingleton<IRuntimeSettingsProvider>(new TestRuntimeSettingsProvider(TestRuntimeSettingsProvider.Base()));
                    services.AddDbContext<LearnerDbContext>(options => options.UseInMemoryDatabase(dbName));
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
        string? authorizationOverride = null)
    {
        var request = new HttpRequestMessage(method, path);
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
