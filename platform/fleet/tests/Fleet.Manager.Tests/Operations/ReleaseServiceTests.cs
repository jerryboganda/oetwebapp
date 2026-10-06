using System.Text;
using Fleet.Core.Validation;
using Fleet.Manager.Operations;
using Fleet.Manager.Persistence;
using Fleet.Manager.Tests.Infrastructure;

namespace Fleet.Manager.Tests.Operations;

/// <summary>Release records, owner approval and the in-memory per-rollout registry token (OET-RWP/1 section 8.7).</summary>
public sealed class ReleaseServiceTests : IAsyncLifetime
{
    private const string RegistryToken = "ghs_TESTONLYTOKEN0123456789abcdef";

    private FleetTestHost _host = null!;
    private EnrollmentDriver _driver = null!;

    public async Task InitializeAsync()
    {
        _host = await FleetTestHost.CreateAsync();
        _driver = new EnrollmentDriver(_host);
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    private ReleaseService Releases => _host.Get<ReleaseService>();

    private RolloutTokenHolder Tokens => _host.Get<RolloutTokenHolder>();

    private async Task<int> AuditCountAsync(string action) =>
        (await _host.Get<IAuditService>().ListAsync(500)).Count(r => r.Action == action);

    [Theory]
    [InlineData("sha", "123")]
    [InlineData("sha", "ABCDEF0123456789ABCDEF0123456789ABCDEF01")]
    [InlineData("runId", "12x")]
    [InlineData("agentRepository", "ghcr.io/someone-else/fleet-agent")]
    [InlineData("agentDigest", "sha256:abc")]
    [InlineData("agentImageId", "latest")]
    [InlineData("managerDigest", "nope")]
    public async Task A_malformed_release_record_is_refused_field_by_field_and_stores_nothing(string field, string value)
    {
        var good = _driver.Record();
        var bad = field switch
        {
            "sha" => good with { Sha = value },
            "runId" => good with { RunId = value },
            "agentRepository" => good with { AgentRepository = value },
            "agentDigest" => good with { AgentDigest = value },
            "agentImageId" => good with { AgentImageId = value },
            _ => good with { ManagerDigest = value },
        };

        var error = await Assert.ThrowsAsync<FleetValidationException>(() => Releases.IngestAsync(bad, "ci", CancellationToken.None));

        Assert.Equal(field, Assert.Single(error.Issues).Field);
        Assert.Empty(await Releases.ListAsync(CancellationToken.None));
    }

    [Fact]
    public async Task A_release_record_is_idempotent_on_the_commit_and_is_not_approved_by_default()
    {
        var first = await Releases.IngestAsync(_driver.Record('1'), "ci", CancellationToken.None);
        var repeated = await Releases.IngestAsync(_driver.Record('1', digest: "sha256:" + new string('e', 64)), "ci", CancellationToken.None);

        Assert.Equal(first.Id, repeated.Id);
        Assert.Equal(FleetWorld.AgentDigest, repeated.AgentDigest);
        Assert.False(first.Approved);
        Assert.Single(await Releases.ListAsync(CancellationToken.None));
        Assert.Null(await Releases.GetCurrentApprovedAsync(CancellationToken.None));
        Assert.Equal(1, await AuditCountAsync("release.recorded"));
    }

    [Fact]
    public async Task Approval_is_explicit_audited_and_repeating_it_changes_nothing()
    {
        var release = await Releases.IngestAsync(_driver.Record('1'), "ci", CancellationToken.None);

        var approved = await Releases.ApproveAsync(release.Id, "owner", CancellationToken.None);
        _host.World.Time.Advance(TimeSpan.FromMinutes(5));
        var again = await Releases.ApproveAsync(release.Id, "someone-else", CancellationToken.None);

        Assert.True(approved.Approved);
        Assert.Equal("owner", approved.ApprovedBy);
        Assert.Equal(approved.ApprovedAt, again.ApprovedAt);
        Assert.Equal("owner", again.ApprovedBy);
        Assert.Equal(1, await AuditCountAsync("release.approved"));
        Assert.Equal(FleetWorld.AgentDigest, (await Releases.GetCurrentApprovedAsync(CancellationToken.None))!.AgentDigest);
        Assert.NotNull(await Releases.FindApprovedByDigestAsync(FleetWorld.AgentDigest, CancellationToken.None));
        Assert.Null(await Releases.FindApprovedByDigestAsync("sha256:" + new string('9', 64), CancellationToken.None));

        var unknown = await Assert.ThrowsAsync<FleetValidationException>(() => Releases.ApproveAsync("rel_missing", "owner", CancellationToken.None));
        Assert.Contains(unknown.Issues, issue => issue.Code == "release_not_found");
    }

    [Fact]
    public async Task Auto_approval_exists_only_when_it_is_switched_on()
    {
        await using var automatic = await FleetTestHost.CreateAsync(configure: settings => settings["Fleet:Image:AutoApproveDigests"] = "true");
        var release = await automatic.Get<ReleaseService>().IngestAsync(new EnrollmentDriver(automatic).Record(), "ci", CancellationToken.None);

        Assert.True(release.Approved);
        Assert.Equal("policy:auto-approve", release.ApprovedBy);
    }

    [Fact]
    public async Task The_registry_token_lives_in_memory_for_sixty_minutes_and_nowhere_else()
    {
        await _driver.SyncAsync(RegistryToken);

        Assert.True(Tokens.HasToken);
        Assert.True(Tokens.TryGetCredential(out var user, out var token));
        Assert.Equal("ci-user", user);
        Assert.Equal(RegistryToken, token);

        // Never in the database, a log line or an audit row; the audit row only says that a token was supplied.
        Assert.False(ByteSearch.Contains(await _host.ReadDatabaseBytesAsync(), Encoding.UTF8.GetBytes(RegistryToken)));
        Assert.DoesNotContain(RegistryToken, _host.Logs.All);
        var audit = (await _host.Get<IAuditService>().ListAsync(20)).First(r => r.Action == "release.sync");
        Assert.DoesNotContain(RegistryToken, audit.DetailsJson);
        Assert.Contains("\"tokenSupplied\":true", audit.DetailsJson);

        _host.World.Time.Advance(TimeSpan.FromMinutes(59));
        Assert.True(Tokens.HasToken);
        _host.World.Time.Advance(TimeSpan.FromMinutes(2));
        Assert.False(Tokens.HasToken);
        Assert.False(Tokens.TryGetCredential(out _, out _));
    }

    [Fact]
    public async Task A_restart_forgets_the_registry_token_and_the_next_sync_supplies_a_new_one()
    {
        await _driver.SyncAsync(RegistryToken);
        Assert.True(Tokens.HasToken);

        await _host.RestartAsync();

        Assert.False(Tokens.HasToken);
        await new EnrollmentDriver(_host).SyncAsync("ghs_SECONDTOKEN0123456789abcdefghij");
        Assert.True(Tokens.TryGetCredential(out _, out var token));
        Assert.Equal("ghs_SECONDTOKEN0123456789abcdefghij", token);
    }

    [Fact]
    public async Task A_newer_sync_replaces_the_token_and_a_sync_without_one_leaves_it_alone()
    {
        await _driver.SyncAsync(RegistryToken);
        await _driver.SyncAsync("ghs_REPLACEMENTTOKEN0123456789abcdef");
        Assert.True(Tokens.TryGetCredential(out _, out var replaced));
        Assert.Equal("ghs_REPLACEMENTTOKEN0123456789abcdef", replaced);

        await Releases.SyncAsync(new SyncPayload(_driver.Record(), null, null), "ci-sync", CancellationToken.None);
        Assert.True(Tokens.TryGetCredential(out _, out var kept));
        Assert.Equal("ghs_REPLACEMENTTOKEN0123456789abcdef", kept);
    }

    [Fact]
    public async Task Invalid_sync_payloads_are_refused_without_setting_a_token()
    {
        var noUser = await Assert.ThrowsAsync<FleetValidationException>(
            () => Releases.SyncAsync(new SyncPayload(_driver.Record(), null, RegistryToken), "ci-sync", CancellationToken.None));
        Assert.Equal("registryUsername", Assert.Single(noUser.Issues).Field);

        var badUser = await Assert.ThrowsAsync<FleetValidationException>(
            () => Releases.SyncAsync(new SyncPayload(_driver.Record(), "ci user; rm -rf /", RegistryToken), "ci-sync", CancellationToken.None));
        Assert.Equal("registryUsername", Assert.Single(badUser.Issues).Field);

        var tooLong = await Assert.ThrowsAsync<FleetValidationException>(
            () => Releases.SyncAsync(new SyncPayload(_driver.Record(), "ci-user", new string('x', 401)), "ci-sync", CancellationToken.None));
        Assert.Equal("registryToken", Assert.Single(tooLong.Issues).Field);

        Assert.False(Tokens.HasToken);
    }

    [Fact]
    public void A_sync_payload_never_prints_its_token()
    {
        var text = new SyncPayload(_driver.Record(), "ci-user", RegistryToken).ToString();

        Assert.DoesNotContain(RegistryToken, text);
        Assert.Contains("[redacted]", text);
    }
}
