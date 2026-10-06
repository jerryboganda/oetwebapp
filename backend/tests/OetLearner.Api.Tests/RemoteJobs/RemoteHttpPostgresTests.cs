using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using OetLearner.Api.Services.RemoteJobs;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests.RemoteJobs;

/// <summary>
/// Whole HTTP requests over the real ASP.NET Core pipeline AND real PostgreSQL (OET-RWP/1 sections 4.3 and 4.5): bearer
/// authentication, the lease guard, the streaming input with Range / If-Range / HEAD, and the fenced single-transaction completion
/// with its applier. The pure-HTTP tests (<see cref="RemoteHttpPlaneTests"/>) stop before any SQL; these run it.
/// </summary>
[Collection(PostgreSqlExclusiveCollection.Name)]
public sealed class RemoteHttpPostgresTests
{
    private const string JobPlane = "/v1/internal/remote-worker";

    /// <summary>An Active node with a freshly issued node token (only its hash is stored, exactly as in production).</summary>
    private static async Task<(string NodeId, string Token)> NodeWithTokenAsync(RemotePgHarness h)
    {
        var node = await h.AddNodeAsync();
        var token = RemoteTokenFormat.Generate(RemoteTokenFormat.NodeKind);
        await h.SqlAsync(
            """
            INSERT INTO "RemoteCredentials" ("TokenId", "Kind", "NodeId", "SecretHash", "CreatedAt", "ExpiresAt", "CreatedBy")
            VALUES (@id, 'node', @node, @hash, clock_timestamp(), clock_timestamp() + interval '30 days', 'test');
            """,
            ("id", token.TokenId), ("node", node), ("hash", token.SecretHashHex));
        return (node, token.Token);
    }

    private static async Task<(RemoteHttpHost Host, string Token, RemoteJobRow Job, byte[] Bytes)> LeasedInputAsync(RemotePgHarness h, int size = 4096)
    {
        var bytes = new byte[size];
        new Random(11).NextBytes(bytes);
        await h.Storage.WriteAsync("media/sample.pdf", new MemoryStream(bytes), CancellationToken.None);
        await h.SeedPdfAsync(size: size);
        var (node, token) = await NodeWithTokenAsync(h);
        await h.EnqueueAsync(size: size);
        var job = await h.ClaimOneAsync(node);
        var host = await RemoteHttpHost.StartOnPostgresAsync(h, h.Storage, null, RemoteJobFlagKeys.Master, RemoteJobFlagKeys.PdfExtract);
        return (host, token, job, bytes);
    }

    private static IReadOnlyDictionary<string, string> Headers(RemoteJobRow job, params (string Name, string Value)[] more)
    {
        var headers = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [RemoteHeaders.Fence] = job.FenceToken.ToString(CultureInfo.InvariantCulture),
        };
        foreach (var (name, value) in more) headers[name] = value;
        return headers;
    }

    private static string InputPath(RemoteJobRow job) => $"{JobPlane}/jobs/{job.Id}/inputs/pdf";

    // ── inputs: streaming with Range ─────────────────────────────────────────

    [PostgreSqlFact]
    public async Task Input_IsStreamedFromStorage_WithTheManifestHashAndAnEtag()
    {
        await using var h = await RemotePgHarness.CreateAsync();
        var (host, token, job, bytes) = await LeasedInputAsync(h);
        await using var _h = host;

        var response = await host.SendAsync(HttpMethod.Get, InputPath(job), token, headers: Headers(job));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(bytes, await response.Content.ReadAsByteArrayAsync());
        Assert.Equal(RemoteTestData.Sha, Assert.Single(response.Headers.GetValues(RemoteHeaders.ContentSha256)));
        Assert.Equal("\"" + RemoteTestData.Sha + "\"", response.Headers.ETag?.Tag);
        Assert.Equal("bytes", Assert.Single(response.Headers.AcceptRanges));
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString() ?? string.Empty, StringComparison.Ordinal);
        Assert.Equal(bytes.Length, response.Content.Headers.ContentLength);
    }

    [PostgreSqlFact]
    public async Task Input_ARangeRequest_Is206WithTheRequestedBytes_AndSuffixAndOpenEndedRangesWork()
    {
        await using var h = await RemotePgHarness.CreateAsync();
        var (host, token, job, bytes) = await LeasedInputAsync(h);
        await using var _h = host;

        var middle = await host.SendAsync(HttpMethod.Get, InputPath(job), token, headers: Headers(job, ("Range", "bytes=100-199")));
        var suffix = await host.SendAsync(HttpMethod.Get, InputPath(job), token, headers: Headers(job, ("Range", "bytes=-50")));
        var openEnded = await host.SendAsync(HttpMethod.Get, InputPath(job), token, headers: Headers(job, ("Range", "bytes=4000-")));

        Assert.Equal(HttpStatusCode.PartialContent, middle.StatusCode);
        Assert.Equal(bytes[100..200], await middle.Content.ReadAsByteArrayAsync());
        Assert.Equal(100, middle.Content.Headers.ContentRange?.From);
        Assert.Equal(199, middle.Content.Headers.ContentRange?.To);
        Assert.Equal(bytes.Length, middle.Content.Headers.ContentRange?.Length);

        Assert.Equal(HttpStatusCode.PartialContent, suffix.StatusCode);
        Assert.Equal(bytes[^50..], await suffix.Content.ReadAsByteArrayAsync());

        Assert.Equal(HttpStatusCode.PartialContent, openEnded.StatusCode);
        Assert.Equal(bytes[4000..], await openEnded.Content.ReadAsByteArrayAsync());
    }

    [PostgreSqlFact]
    public async Task Input_AnUnsatisfiableRange_Is416_AndAnIfRangeThatDoesNotMatchTheEtagSendsEverything()
    {
        await using var h = await RemotePgHarness.CreateAsync();
        var (host, token, job, bytes) = await LeasedInputAsync(h);
        await using var _h = host;
        var etag = "\"" + RemoteTestData.Sha + "\"";

        var beyond = await host.SendAsync(HttpMethod.Get, InputPath(job), token, headers: Headers(job, ("Range", "bytes=9999-")));
        var several = await host.SendAsync(HttpMethod.Get, InputPath(job), token, headers: Headers(job, ("Range", "bytes=0-1,5-6")));
        var staleValidator = await host.SendAsync(
            HttpMethod.Get, InputPath(job), token, headers: Headers(job, ("Range", "bytes=0-9"), ("If-Range", "\"some-other-version\"")));
        var matchingValidator = await host.SendAsync(
            HttpMethod.Get, InputPath(job), token, headers: Headers(job, ("Range", "bytes=0-9"), ("If-Range", etag)));

        Assert.Equal(HttpStatusCode.RequestedRangeNotSatisfiable, beyond.StatusCode);
        Assert.Equal(HttpStatusCode.RequestedRangeNotSatisfiable, several.StatusCode);

        // A validator that does not match means "the object changed: send all of it", never a slice of the new bytes.
        Assert.Equal(HttpStatusCode.OK, staleValidator.StatusCode);
        Assert.Equal(bytes, await staleValidator.Content.ReadAsByteArrayAsync());

        Assert.Equal(HttpStatusCode.PartialContent, matchingValidator.StatusCode);
        Assert.Equal(bytes[..10], await matchingValidator.Content.ReadAsByteArrayAsync());
    }

    [PostgreSqlFact]
    public async Task Input_AHeadRequest_ReturnsTheHeadersWithoutTheBody()
    {
        await using var h = await RemotePgHarness.CreateAsync();
        var (host, token, job, bytes) = await LeasedInputAsync(h);
        await using var _h = host;

        var response = await host.SendAsync(HttpMethod.Head, InputPath(job), token, headers: Headers(job));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
        Assert.Equal(bytes.Length, response.Content.Headers.ContentLength);
        Assert.Equal(RemoteTestData.Sha, Assert.Single(response.Headers.GetValues(RemoteHeaders.ContentSha256)));
    }

    [PostgreSqlFact]
    public async Task Input_NeedsTheFence_AndAnExpiredLeaseCannotReadIt()
    {
        await using var h = await RemotePgHarness.CreateAsync();
        var (host, token, job, _) = await LeasedInputAsync(h);
        await using var _h = host;

        var noFence = await host.SendAsync(HttpMethod.Get, InputPath(job), token);
        var wrongFence = await host.SendAsync(
            HttpMethod.Get, InputPath(job), token, headers: new Dictionary<string, string> { [RemoteHeaders.Fence] = "99" });
        await h.ExpireLeaseAsync(job.Id);
        var expired = await host.SendAsync(HttpMethod.Get, InputPath(job), token, headers: Headers(job));

        Assert.Equal(HttpStatusCode.BadRequest, noFence.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, wrongFence.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, expired.StatusCode);
        using var body = JsonDocument.Parse(await expired.Content.ReadAsStringAsync());
        Assert.Equal("lease_lost", body.RootElement.GetProperty("code").GetString());
    }

    // ── complete: the fenced single-transaction commit ───────────────────────

    private static string CompleteJson(RemoteJobRow job)
        => Encoding.UTF8.GetString(RemotePgHarness.CompleteBody(job.FenceToken, RemotePgHarness.PdfResultJson(job)));

    [PostgreSqlFact]
    public async Task Complete_OverHttp_AppliesTheTextOnce_AndAReplayAnswersTheStoredOutcome()
    {
        await using var h = await RemotePgHarness.CreateAsync();
        var (host, token, job, _) = await LeasedInputAsync(h);
        await using var _h = host;
        var path = $"{JobPlane}/jobs/{job.Id}/complete";

        var first = await host.SendAsync(HttpMethod.Post, path, token, json: CompleteJson(job));
        var replay = await host.SendAsync(HttpMethod.Post, path, token, json: CompleteJson(job));

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        using (var body = JsonDocument.Parse(await first.Content.ReadAsStringAsync()))
        {
            Assert.Equal("succeeded", body.RootElement.GetProperty("status").GetString());
            Assert.Equal("Applied", body.RootElement.GetProperty("outcome").GetString());
            Assert.False(body.RootElement.GetProperty("replayed").GetBoolean());
        }

        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        using (var body = JsonDocument.Parse(await replay.Content.ReadAsStringAsync()))
        {
            Assert.Equal("Applied", body.RootElement.GetProperty("outcome").GetString());
            Assert.True(body.RootElement.GetProperty("replayed").GetBoolean());
        }

        Assert.Equal("Succeeded", await h.StateOfAsync(job.Id));
        Assert.Equal(4, await h.PaperVersionAsync()); // seeded at 3; one applier run, not two
        Assert.Equal(1, await h.AuditCountAsync("RemoteJob.Applied"));
        using var paper = JsonDocument.Parse((await h.PaperJsonAsync())!);
        Assert.Equal(string.Join("\n\n", RemotePgHarness.SamplePages).Trim(), paper.RootElement.GetProperty("asset-1").GetString());
    }

    [PostgreSqlFact]
    public async Task Complete_OverHttp_AnotherNodesToken_CannotCompleteTheJob_AndChangesNothing()
    {
        await using var h = await RemotePgHarness.CreateAsync();
        var (host, _, job, _) = await LeasedInputAsync(h);
        await using var _h = host;
        var (_, strangerToken) = await NodeWithTokenAsync(h);

        var response = await host.SendAsync(HttpMethod.Post, $"{JobPlane}/jobs/{job.Id}/complete", strangerToken, json: CompleteJson(job));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("lease_lost", body.RootElement.GetProperty("code").GetString());
        Assert.Equal("Leased", await h.StateOfAsync(job.Id));
        Assert.Equal(3, await h.PaperVersionAsync());
    }

    [PostgreSqlFact]
    public async Task Complete_OverHttp_WithTheFreezeSwitchOn_Is503_AndTheJobIsWithdrawn()
    {
        await using var h = await RemotePgHarness.CreateAsync();
        var (host, token, job, _) = await LeasedInputAsync(h);
        await using var _h = host;
        host.Flags.Set(RemoteJobFlagKeys.Master, RemoteJobFlagKeys.PdfExtract, RemoteJobFlagKeys.FreezeApplies);

        var response = await host.SendAsync(HttpMethod.Post, $"{JobPlane}/jobs/{job.Id}/complete", token, json: CompleteJson(job));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("applies_frozen", body.RootElement.GetProperty("code").GetString());
        Assert.Equal("Cancelled", await h.StateOfAsync(job.Id));
        Assert.Equal(3, await h.PaperVersionAsync());
    }
}
