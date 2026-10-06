using Fleet.Core.Audit;
using Fleet.Manager.Monitoring;
using Fleet.Manager.Operations;
using Fleet.Manager.Persistence;
using Fleet.Manager.Tests.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Fleet.Manager.Tests.Persistence;

/// <summary>The database policy (EnsureCreated at v1, forward-only upgrades, refusal of newer or foreign files) and RW-145, the tamper-evident chains.</summary>
public sealed class SchemaAndAuditTests : IAsyncLifetime
{
    private FleetTestHost _host = null!;

    public async Task InitializeAsync() => _host = await FleetTestHost.CreateAsync();

    public async Task DisposeAsync() => await _host.DisposeAsync();

    private async Task<long> ScalarAsync(string sql)
    {
        var factory = _host.Get<IDbContextFactory<FleetDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        await db.Database.OpenConnectionAsync();
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;
        var value = await command.ExecuteScalarAsync();
        return Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    private async Task ExecuteAsync(string sql)
    {
        var factory = _host.Get<IDbContextFactory<FleetDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        await db.Database.ExecuteSqlRawAsync(sql);
    }

    [Fact]
    public async Task A_fresh_database_gets_the_model_tables_the_schema_stamp_wal_and_secure_delete()
    {
        foreach (var table in new[] { "hosts", "operations", "operation_steps", "policies", "credentials", "releases", "audit", "owner_account", "schema_info" })
        {
            Assert.Equal(1, await ScalarAsync("SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = '" + table + "'"));
        }

        Assert.Equal(SchemaManager.CurrentVersion, await ScalarAsync("SELECT version FROM schema_info WHERE id = 1"));
        Assert.Equal(1, await ScalarAsync("PRAGMA foreign_keys"));
        Assert.Equal(1, await ScalarAsync("PRAGMA secure_delete"));

        var factory = _host.Get<IDbContextFactory<FleetDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        await db.Database.OpenConnectionAsync();
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "PRAGMA journal_mode";
        Assert.Equal("wal", (string?)await command.ExecuteScalarAsync());
    }

    [Fact]
    public async Task Columns_follow_the_snake_case_names_of_the_spec()
    {
        Assert.Equal(1, await ScalarAsync("SELECT COUNT(*) FROM pragma_table_info('hosts') WHERE name = 'host_key_sha256'"));
        Assert.Equal(1, await ScalarAsync("SELECT COUNT(*) FROM pragma_table_info('operations') WHERE name = 'failure_reason'"));
        Assert.Equal(1, await ScalarAsync("SELECT COUNT(*) FROM pragma_table_info('credentials') WHERE name = 'fingerprint_hint'"));
        Assert.Equal(1, await ScalarAsync("SELECT COUNT(*) FROM pragma_table_info('owner_account') WHERE name = 'totp_secret_ciphertext'"));
        Assert.Equal(1, await ScalarAsync("SELECT COUNT(*) FROM pragma_table_info('audit') WHERE name = 'prev_hash'"));
    }

    [Fact]
    public async Task A_database_stamped_newer_than_this_build_is_refused()
    {
        await ExecuteAsync("UPDATE schema_info SET version = 99 WHERE id = 1");
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => _host.RestartAsync());
        Assert.Contains("newer than this build", error.Message);
    }

    [Fact]
    public async Task A_file_that_is_not_a_fleet_database_is_refused()
    {
        var path = Path.Combine(Directory.CreateTempSubdirectory("fleet-foreign-").FullName, "other.db");
        await using (var connection = new SqliteConnection("Data Source=" + path))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE somebody_elses_table (id INTEGER)";
            await command.ExecuteNonQueryAsync();
        }

        SqliteConnection.ClearAllPools();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await using var foreign = await FleetTestHost.CreateAsync(configure: settings =>
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                settings["Fleet:Data:Directory"] = Path.GetDirectoryName(path)!;
                settings["Fleet:Data:DatabaseFileName"] = "other.db";
            });
        });
        Assert.Contains("not a fleet manager database", error.Message);
    }

    [Fact]
    public async Task The_audit_log_is_a_hash_chain_that_verifies()
    {
        var audit = _host.Get<IAuditService>();
        await audit.AppendAsync("owner", "host.added", "helper-1", new Dictionary<string, object?> { ["address"] = "203.0.113.10" });
        await audit.AppendAsync("owner", "host.drained", "helper-1");
        await audit.AppendAsync("system", "node.revoked", "helper-1", new Dictionary<string, object?> { ["reason"] = "test" });

        var verification = await audit.VerifyAsync();
        Assert.True(verification.Intact);
        Assert.Equal(3, verification.Checked);

        var newestFirst = await audit.ListAsync(10);
        Assert.Equal(new[] { 3L, 2L, 1L }, newestFirst.Select(r => r.Id).ToArray());
        Assert.Equal(AuditChain.Genesis, newestFirst[^1].PrevHash);
        Assert.Equal(newestFirst[2].Hash, newestFirst[1].PrevHash);
        Assert.Equal(newestFirst[1].Hash, newestFirst[0].PrevHash);
    }

    [Fact]
    public async Task Editing_or_deleting_any_audit_row_breaks_the_chain_and_stops_startup()
    {
        var audit = _host.Get<IAuditService>();
        for (var i = 0; i < 5; i++)
        {
            await audit.AppendAsync("owner", "action." + i, "target");
        }

        await ExecuteAsync("UPDATE audit SET action = 'forged' WHERE id = 3");
        var edited = await audit.VerifyAsync();
        Assert.False(edited.Intact);
        Assert.Equal(3, edited.FirstBadId);
        Assert.Contains("row hash", edited.Problem);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => _host.RestartAsync());
        Assert.Contains("hash chain is broken", error.Message);
    }

    [Fact]
    public async Task Removing_a_row_from_the_middle_is_detected_as_a_broken_link()
    {
        var audit = _host.Get<IAuditService>();
        for (var i = 0; i < 4; i++)
        {
            await audit.AppendAsync("owner", "action." + i, null);
        }

        await ExecuteAsync("DELETE FROM audit WHERE id = 2");
        var result = await audit.VerifyAsync();
        Assert.False(result.Intact);
        Assert.Equal(3, result.FirstBadId);
        Assert.Contains("previous hash", result.Problem);
    }

    [Fact]
    public async Task A_broken_chain_can_be_booted_for_a_forensic_look_when_explicitly_allowed()
    {
        await _host.Get<IAuditService>().AppendAsync("owner", "one", null);
        await _host.Get<IAuditService>().AppendAsync("owner", "two", null);
        await ExecuteAsync("UPDATE audit SET actor = 'mallory' WHERE id = 1");

        await Assert.ThrowsAsync<InvalidOperationException>(() => _host.RestartAsync());

        _host.Set("Fleet:Audit:AllowBrokenChain", "true");
        await _host.RestartAsync();

        var integrity = _host.Get<FleetState>().Integrity;
        Assert.NotNull(integrity);
        Assert.False(integrity!.AuditChainIntact);
        Assert.Contains("audit:", integrity.Problem);
        Assert.False((await _host.Get<HealthReporter>().GetAsync(CancellationToken.None)).Ok);
    }

    [Fact]
    public async Task Audit_details_are_scrubbed_and_capped()
    {
        var audit = _host.Get<IAuditService>();
        var token = "orw1_" + new string('a', 16) + "_" + new string('Z', 43);
        var entry = await audit.AppendAsync(
            "owner",
            "test",
            "target",
            new Dictionary<string, object?>
            {
                ["token"] = token,
                ["pem"] = "-----BEGIN OPENSSH PRIVATE KEY-----\nabc\n-----END OPENSSH PRIVATE KEY-----", // secret-scan:allow (fake PEM framing, no key material)
                ["long"] = new string('x', 500),
                ["n"] = 3,
            });

        Assert.DoesNotContain(token, entry.DetailsJson);
        Assert.DoesNotContain("BEGIN OPENSSH", entry.DetailsJson);
        Assert.Contains("[redacted]", entry.DetailsJson);
        Assert.DoesNotContain(new string('x', 201), entry.DetailsJson);
        Assert.Contains("\"n\":3", entry.DetailsJson);
    }

    [Fact]
    public void The_chain_helper_detects_edits_reordering_and_a_wrong_anchor_in_memory()
    {
        var at = new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);
        AuditRecord Make(long id, string prev, string action)
        {
            var hash = AuditChain.ComputeHash(prev, id, at, "owner", action, "t", "{}");
            return new AuditRecord(id, at, "owner", action, "t", "{}", prev, hash);
        }

        var one = Make(1, AuditChain.Genesis, "a");
        var two = Make(2, one.Hash, "b");
        var three = Make(3, two.Hash, "c");

        Assert.True(AuditChain.Verify(new[] { one, two, three }).Intact);
        Assert.False(AuditChain.Verify(new[] { one, three, two }).Intact);
        Assert.False(AuditChain.Verify(new[] { two, three }).Intact);
        Assert.True(AuditChain.Verify(new[] { two, three }, one.Hash).Intact);
        Assert.False(AuditChain.Verify(new[] { one with { Action = "edited" }, two }).Intact);
        Assert.True(AuditChain.Verify(Array.Empty<AuditRecord>()).Intact);
    }

    [Fact]
    public async Task The_operations_log_is_sealed_and_tampering_with_creation_fields_is_detected()
    {
        var driver = new EnrollmentDriver(_host);
        await driver.AddAsync("seal-one", "203.0.113.31");
        await driver.AddAsync("seal-two", "203.0.113.32");
        var operations = _host.Get<OperationStore>();

        Assert.True((await operations.VerifyChainAsync(CancellationToken.None)).Intact);

        await ExecuteAsync("UPDATE operations SET actor = 'mallory' WHERE seq = 1");
        var result = await operations.VerifyChainAsync(CancellationToken.None);
        Assert.False(result.Intact);
        Assert.Equal(1, result.FirstBadId);

        // The sealed fields are immutable by design; progress (state, steps) is not sealed.
        await ExecuteAsync("UPDATE operations SET actor = 'owner' WHERE seq = 1");
        await ExecuteAsync("UPDATE operations SET state = 'Cancelled', data_json = '[]' WHERE seq = 1");
        Assert.True((await operations.VerifyChainAsync(CancellationToken.None)).Intact);
    }
}
