using System.Security.Cryptography;
using System.Text;
using Fleet.Core.Crypto;
using Fleet.Manager.Persistence;
using Fleet.Manager.Tests.Infrastructure;
using Fleet.Manager.Vault;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Fleet.Manager.Tests.Vault;

/// <summary>The vault on disk: write-only fields, the 60-minute owner credential, crypto-erase, key rotation (RW-137, RW-138).</summary>
public sealed class CredentialStoreTests : IAsyncLifetime
{
    private FleetTestHost _host = null!;

    public async Task InitializeAsync() => _host = await FleetTestHost.CreateAsync();

    public async Task DisposeAsync() => await _host.DisposeAsync();

    private CredentialStore Store => _host.Get<CredentialStore>();

    private async Task<string> AddHostRowAsync(string nodeRef = "vault-host", string address = "203.0.113.50")
    {
        var factory = _host.Get<IDbContextFactory<FleetDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        var entity = new HostEntity
        {
            Id = Guid.NewGuid().ToString("D"),
            NodeRef = nodeRef,
            DisplayName = "Vault host",
            Address = address,
            SshPort = 22,
            Lifecycle = "Enrolling",
            CreatedAt = _host.World.Time.GetUtcNow(),
            UpdatedAt = _host.World.Time.GetUtcNow(),
        };
        db.Hosts.Add(entity);
        await db.SaveChangesAsync();
        return entity.Id;
    }

    private static bool Contains(byte[] haystack, byte[] needle) => haystack.AsSpan().IndexOf(needle) >= 0;

    [Fact]
    public async Task A_stored_secret_comes_back_and_never_in_plaintext_form_from_the_info_view()
    {
        var hostId = await AddHostRowAsync();
        using var secret = SecretBuffer.FromUtf8("manager-private-key-bytes");

        var info = await Store.StoreAsync(hostId, CredentialPurposes.ManagerSsh, secret, "0a1b2c3d", null, CancellationToken.None);

        Assert.True(info.HasCiphertext);
        Assert.Equal("0a1b2c3d", info.FingerprintHint);
        Assert.Null(info.ExpiresAt);
        using var opened = await Store.OpenAsync(hostId, CredentialPurposes.ManagerSsh, CancellationToken.None);
        Assert.NotNull(opened);
        Assert.Equal("manager-private-key-bytes", Encoding.UTF8.GetString(opened!.AsSpan()));
        Assert.DoesNotContain("manager-private-key", info.ToString());
    }

    [Fact]
    public async Task Storing_again_replaces_the_previous_credential_of_the_same_purpose()
    {
        var hostId = await AddHostRowAsync();
        using var first = SecretBuffer.FromUtf8("first-key");
        using var second = SecretBuffer.FromUtf8("second-key");
        await Store.StoreAsync(hostId, CredentialPurposes.ManagerSsh, first, "11111111", null, CancellationToken.None);
        await Store.StoreAsync(hostId, CredentialPurposes.ManagerSsh, second, "22222222", null, CancellationToken.None);

        using var opened = await Store.OpenAsync(hostId, CredentialPurposes.ManagerSsh, CancellationToken.None);
        Assert.Equal("second-key", Encoding.UTF8.GetString(opened!.AsSpan()));
        var factory = _host.Get<IDbContextFactory<FleetDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        Assert.Equal(1, await db.Credentials.CountAsync(c => c.HostId == hostId && c.Purpose == CredentialPurposes.ManagerSsh));
    }

    [Fact]
    public async Task The_owner_credential_expires_after_its_ttl_and_expired_rows_are_erased()
    {
        var hostId = await AddHostRowAsync();
        using var secret = SecretBuffer.FromUtf8("owner-key");
        var info = await Store.StoreAsync(hostId, CredentialPurposes.OwnerBootstrap, secret, "deadbeef", TimeSpan.FromMinutes(60), CancellationToken.None);
        Assert.Equal(info.CreatedAt + TimeSpan.FromMinutes(60), info.ExpiresAt);

        _host.World.Time.Advance(TimeSpan.FromMinutes(59));
        Assert.True(await Store.ExistsActiveAsync(hostId, CredentialPurposes.OwnerBootstrap, CancellationToken.None));
        using (var stillThere = await Store.OpenAsync(hostId, CredentialPurposes.OwnerBootstrap, CancellationToken.None))
        {
            Assert.NotNull(stillThere);
        }

        _host.World.Time.Advance(TimeSpan.FromMinutes(2));
        Assert.False(await Store.ExistsActiveAsync(hostId, CredentialPurposes.OwnerBootstrap, CancellationToken.None));
        Assert.Null(await Store.OpenAsync(hostId, CredentialPurposes.OwnerBootstrap, CancellationToken.None));
        Assert.True(await Store.ExistsAnyAsync(hostId, CredentialPurposes.OwnerBootstrap, CancellationToken.None));

        var purged = await Store.PurgeExpiredOwnerCredentialsAsync(CancellationToken.None);
        Assert.Equal(new[] { hostId }, purged);
        Assert.False(await Store.ExistsAnyAsync(hostId, CredentialPurposes.OwnerBootstrap, CancellationToken.None));
    }

    [Fact]
    public async Task Destroying_a_credential_leaves_no_plaintext_and_no_ciphertext_in_the_database_or_its_wal()
    {
        var hostId = await AddHostRowAsync();
        const string marker = "UNIQUE-OWNER-KEY-MARKER-0123456789-abcdefghijklmnop";
        using var secret = SecretBuffer.FromUtf8(marker);
        await Store.StoreAsync(hostId, CredentialPurposes.OwnerBootstrap, secret, "cafef00d", TimeSpan.FromMinutes(60), CancellationToken.None);

        var markerBytes = Encoding.UTF8.GetBytes(marker);
        Assert.False(Contains(await _host.ReadDatabaseBytesAsync(), markerBytes), "the key must be encrypted before it touches SQLite");

        byte[] ciphertext;
        var factory = _host.Get<IDbContextFactory<FleetDbContext>>();
        await using (var db = await factory.CreateDbContextAsync())
        {
            ciphertext = (await db.Credentials.AsNoTracking().SingleAsync(c => c.HostId == hostId)).Ciphertext!;
        }

        Assert.True(Contains(await _host.ReadDatabaseBytesAsync(), ciphertext.AsSpan(0, 32).ToArray()), "sanity: the ciphertext is stored");

        Assert.Equal(1, await Store.DestroyAsync(hostId, CredentialPurposes.OwnerBootstrap, CancellationToken.None));

        var after = await _host.ReadDatabaseBytesAsync();
        Assert.False(Contains(after, markerBytes));
        Assert.False(Contains(after, ciphertext.AsSpan(0, 32).ToArray()), "secure_delete plus a WAL checkpoint must erase the destroyed record");
        Assert.False(await Store.ExistsAnyAsync(hostId, CredentialPurposes.OwnerBootstrap, CancellationToken.None));
    }

    [Fact]
    public async Task A_node_token_render_row_holds_only_a_fingerprint()
    {
        var hostId = await AddHostRowAsync();
        var info = await Store.RecordFingerprintOnlyAsync(hostId, CredentialPurposes.NodeTokenRender, "0f0f0f0f", CancellationToken.None);

        Assert.False(info.HasCiphertext);
        Assert.Null(await Store.OpenAsync(hostId, CredentialPurposes.NodeTokenRender, CancellationToken.None));
        Assert.True(await Store.ExistsAnyAsync(hostId, CredentialPurposes.NodeTokenRender, CancellationToken.None));
        Assert.Equal("0f0f0f0f", (await Store.GetInfoAsync(hostId, CredentialPurposes.NodeTokenRender, CancellationToken.None))!.FingerprintHint);
    }

    [Fact]
    public async Task A_record_moved_to_another_host_or_purpose_does_not_open()
    {
        var hostA = await AddHostRowAsync("vault-a", "203.0.113.51");
        var hostB = await AddHostRowAsync("vault-b", "203.0.113.52");
        using var secret = SecretBuffer.FromUtf8("bound-secret");
        await Store.StoreAsync(hostA, CredentialPurposes.ManagerSsh, secret, "abababab", null, CancellationToken.None);

        var factory = _host.Get<IDbContextFactory<FleetDbContext>>();
        await using (var db = await factory.CreateDbContextAsync())
        {
            await db.Database.ExecuteSqlRawAsync("UPDATE credentials SET host_id = {0}", hostB);
        }

        await Assert.ThrowsAsync<VaultDecryptionException>(() => Store.OpenAsync(hostB, CredentialPurposes.ManagerSsh, CancellationToken.None));
    }

    [Fact]
    public async Task Startup_fails_when_a_record_was_written_under_a_key_that_is_gone()
    {
        var hostId = await AddHostRowAsync();
        using var secret = SecretBuffer.FromUtf8("needs-the-old-key");
        await Store.StoreAsync(hostId, CredentialPurposes.ManagerSsh, secret, "12341234", null, CancellationToken.None);

        File.WriteAllBytes(Path.Combine(_host.SecretsDirectory, "fleet_master_key"), RandomNumberGenerator.GetBytes(32));

        await Assert.ThrowsAsync<VaultConfigurationException>(() => _host.RestartAsync());
    }

    [Fact]
    public async Task Rewrap_moves_every_record_to_the_new_key_so_the_old_one_can_be_dropped()
    {
        var hostId = await AddHostRowAsync();
        using var secret = SecretBuffer.FromUtf8("survives-rotation");
        await Store.StoreAsync(hostId, CredentialPurposes.ManagerSsh, secret, "56785678", null, CancellationToken.None);

        var keyFile = Path.Combine(_host.SecretsDirectory, "fleet_master_key");
        var oldKey = File.ReadAllBytes(keyFile);
        File.WriteAllBytes(Path.Combine(_host.SecretsDirectory, "fleet_master_key_prev"), oldKey);
        var newKey = RandomNumberGenerator.GetBytes(32);
        File.WriteAllBytes(keyFile, newKey);

        await _host.RestartAsync();
        Assert.Equal(1, await Store.RewrapAllAsync(CancellationToken.None));
        Assert.Equal(0, await Store.RewrapAllAsync(CancellationToken.None));

        File.Delete(Path.Combine(_host.SecretsDirectory, "fleet_master_key_prev"));
        await _host.RestartAsync();

        using var opened = await Store.OpenAsync(hostId, CredentialPurposes.ManagerSsh, CancellationToken.None);
        Assert.Equal("survives-rotation", Encoding.UTF8.GetString(opened!.AsSpan()));
        var info = await Store.GetInfoAsync(hostId, CredentialPurposes.ManagerSsh, CancellationToken.None);
        Assert.NotNull(info!.RotatedAt);
    }

    [Fact]
    public void The_owner_credential_payload_keeps_user_and_key_apart()
    {
        using var key = SecretBuffer.FromUtf8("-----BEGIN KEY-----\nabc\n-----END KEY-----\n");
        using var packed = OwnerCredentialPayload.Pack("ubuntu", key);
        var (user, unpacked) = OwnerCredentialPayload.Unpack(packed);
        using (unpacked)
        {
            Assert.Equal("ubuntu", user);
            Assert.Equal(Encoding.UTF8.GetString(key.AsSpan()), Encoding.UTF8.GetString(unpacked.AsSpan()));
        }

        Assert.Throws<ArgumentException>(() => OwnerCredentialPayload.Pack(new string('u', 33), key));
        Assert.Throws<ArgumentException>(() => OwnerCredentialPayload.Pack(string.Empty, key));
    }
}
