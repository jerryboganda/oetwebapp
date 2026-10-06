using System.Security.Cryptography;
using System.Text;
using Fleet.Core.Crypto;
using Fleet.Manager.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fleet.Manager.Vault;

public static class CredentialPurposes
{
    public const string OwnerBootstrap = "owner-bootstrap";
    public const string ManagerSsh = "manager-ssh";
    public const string NodeTokenRender = "node-token-render";
    public const string OwnerTotp = "owner-totp";

    /// <summary>Host id used in the AAD of records that belong to no host (the owner's TOTP secret).</summary>
    public const string OwnerScope = "owner";
}

/// <summary>Everything the API or UI may ever learn about a stored credential: no secret, only a hint.</summary>
public sealed record CredentialInfo(
    string Id,
    string HostId,
    string Purpose,
    string FingerprintHint,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? RotatedAt,
    DateTimeOffset? DiscardedAt,
    bool HasCiphertext);

/// <summary>
/// The owner bootstrap credential is stored as <c>[userLength:1][user][private key bytes]</c> so that
/// the key never passes through a managed string on the vault path.
/// </summary>
public static class OwnerCredentialPayload
{
    public static SecretBuffer Pack(string user, SecretBuffer key)
    {
        var userBytes = Encoding.UTF8.GetBytes(user);
        if (userBytes.Length is < 1 or > 32)
        {
            throw new ArgumentException("SSH user must be 1-32 bytes.", nameof(user));
        }

        var combined = new byte[1 + userBytes.Length + key.Length];
        combined[0] = (byte)userBytes.Length;
        userBytes.CopyTo(combined, 1);
        key.AsSpan().CopyTo(combined.AsSpan(1 + userBytes.Length));
        try
        {
            return SecretBuffer.FromBytes(combined);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(combined);
        }
    }

    public static (string User, SecretBuffer Key) Unpack(SecretBuffer packed)
    {
        var span = packed.AsSpan();
        var length = span[0];
        var user = Encoding.UTF8.GetString(span.Slice(1, length));
        return (user, SecretBuffer.FromBytes(span[(1 + length)..]));
    }
}

/// <summary>
/// Persistence of vault records (OET-RWP/1 section 8.3 and 8.5). Plaintext exists only inside
/// <see cref="SecretBuffer"/>s handed to callers, who dispose them. Nothing here ever logs a value.
/// The owner bootstrap credential has a hard TTL and is crypto-erased (row deleted with
/// <c>secure_delete</c> on, WAL checkpointed) by <see cref="DestroyAsync"/>.
/// </summary>
public sealed class CredentialStore
{
    private readonly IDbContextFactory<FleetDbContext> _factory;
    private readonly VaultCipher _cipher;
    private readonly TimeProvider _time;
    private readonly ILogger<CredentialStore> _logger;

    public CredentialStore(
        IDbContextFactory<FleetDbContext> factory,
        VaultCipher cipher,
        TimeProvider time,
        ILogger<CredentialStore> logger)
    {
        _factory = factory;
        _cipher = cipher;
        _time = time;
        _logger = logger;
    }

    /// <summary>Stores a secret, replacing any earlier credential of the same host and purpose.</summary>
    public async Task<CredentialInfo> StoreAsync(
        string hostId,
        string purpose,
        SecretBuffer secret,
        string fingerprintHint,
        TimeSpan? ttl,
        CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid().ToString("D");
        var blob = Encrypt(secret, VaultCipher.Aad(purpose, hostId, id));
        var now = _time.GetUtcNow();
        var entity = new CredentialEntity
        {
            Id = id,
            HostId = hostId,
            Purpose = purpose,
            Ciphertext = blob,
            KeyId = VaultCipher.KeyIdOf(blob),
            FingerprintHint = fingerprintHint,
            CreatedAt = now,
            ExpiresAt = ttl is null ? null : now + ttl.Value,
        };

        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Credentials
            .Where(c => c.HostId == hostId && c.Purpose == purpose)
            .ExecuteDeleteAsync(cancellationToken);
        db.Credentials.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ToInfo(entity);
    }

    /// <summary>For <c>node-token-render</c>: a node token never rests in the vault, only its fingerprint and timestamps do.</summary>
    public async Task<CredentialInfo> RecordFingerprintOnlyAsync(
        string hostId,
        string purpose,
        string fingerprintHint,
        CancellationToken cancellationToken)
    {
        var entity = new CredentialEntity
        {
            Id = Guid.NewGuid().ToString("D"),
            HostId = hostId,
            Purpose = purpose,
            Ciphertext = null,
            KeyId = 0,
            FingerprintHint = fingerprintHint,
            CreatedAt = _time.GetUtcNow(),
        };

        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Credentials
            .Where(c => c.HostId == hostId && c.Purpose == purpose)
            .ExecuteDeleteAsync(cancellationToken);
        db.Credentials.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ToInfo(entity);
    }

    /// <summary>Returns the decrypted secret, or null when there is none or it has expired. The caller disposes it.</summary>
    public async Task<SecretBuffer?> OpenAsync(string hostId, string purpose, CancellationToken cancellationToken)
    {
        var now = _time.GetUtcNow();
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        var row = await db.Credentials
            .AsNoTracking()
            .Where(c => c.HostId == hostId && c.Purpose == purpose && c.DiscardedAt == null)
            .OrderByDescending(c => c.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (row?.Ciphertext is null || (row.ExpiresAt is { } expires && expires <= now))
        {
            return null;
        }

        return Decrypt(row.Ciphertext, VaultCipher.Aad(purpose, hostId, row.Id));
    }

    public async Task<CredentialInfo?> GetInfoAsync(string hostId, string purpose, CancellationToken cancellationToken)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        var row = await db.Credentials
            .AsNoTracking()
            .Where(c => c.HostId == hostId && c.Purpose == purpose)
            .OrderByDescending(c => c.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        return row is null ? null : ToInfo(row);
    }

    /// <summary>True when a row exists that has not been discarded and has not passed its expiry.</summary>
    public async Task<bool> ExistsActiveAsync(string hostId, string purpose, CancellationToken cancellationToken)
    {
        var now = _time.GetUtcNow();
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        return await db.Credentials.AnyAsync(
            c => c.HostId == hostId && c.Purpose == purpose && c.DiscardedAt == null && (c.ExpiresAt == null || c.ExpiresAt > now),
            cancellationToken);
    }

    /// <summary>True when ANY row (active or expired) of that purpose is still in the database.</summary>
    public async Task<bool> ExistsAnyAsync(string hostId, string purpose, CancellationToken cancellationToken)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        return await db.Credentials.AnyAsync(c => c.HostId == hostId && c.Purpose == purpose, cancellationToken);
    }

    /// <summary>Crypto-erase: delete the rows (secure_delete overwrites the pages) and checkpoint the WAL so no stale page image survives.</summary>
    public async Task<int> DestroyAsync(string hostId, string purpose, CancellationToken cancellationToken)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        var removed = await db.Credentials
            .Where(c => c.HostId == hostId && c.Purpose == purpose)
            .ExecuteDeleteAsync(cancellationToken);
        if (removed > 0)
        {
            await CheckpointAsync(db, cancellationToken);
        }

        return removed;
    }

    public async Task<int> DestroyAllForHostAsync(string hostId, CancellationToken cancellationToken)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        var removed = await db.Credentials
            .Where(c => c.HostId == hostId)
            .ExecuteDeleteAsync(cancellationToken);
        if (removed > 0)
        {
            await CheckpointAsync(db, cancellationToken);
        }

        return removed;
    }

    /// <summary>Deletes owner bootstrap credentials whose 60-minute TTL has passed. Returns the host ids that lost one.</summary>
    public async Task<IReadOnlyList<string>> PurgeExpiredOwnerCredentialsAsync(CancellationToken cancellationToken)
    {
        var now = _time.GetUtcNow();
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        var hostIds = await db.Credentials
            .AsNoTracking()
            .Where(c => c.Purpose == CredentialPurposes.OwnerBootstrap && c.ExpiresAt != null && c.ExpiresAt <= now)
            .Select(c => c.HostId)
            .ToListAsync(cancellationToken);
        if (hostIds.Count == 0)
        {
            return hostIds;
        }

        await db.Credentials
            .Where(c => c.Purpose == CredentialPurposes.OwnerBootstrap && c.ExpiresAt != null && c.ExpiresAt <= now)
            .ExecuteDeleteAsync(cancellationToken);
        await CheckpointAsync(db, cancellationToken);
        return hostIds;
    }

    /// <summary>
    /// Startup check (RW-138): every record must be decryptable with an available master key. A row
    /// written under a key that is gone fails startup instead of failing later in the middle of an operation.
    /// </summary>
    public async Task EnsureKeysAvailableAsync(CancellationToken cancellationToken)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        var available = _cipher.Keys.KeyIds.ToHashSet();
        var rows = await db.Credentials
            .AsNoTracking()
            .Where(c => c.Ciphertext != null)
            .Select(c => new { c.Id, c.Ciphertext })
            .ToListAsync(cancellationToken);
        foreach (var row in rows)
        {
            if (!available.Contains(VaultCipher.KeyIdOf(row.Ciphertext!)))
            {
                throw new VaultConfigurationException(
                    "Credential " + row.Id + " was encrypted under a master key that is not available. Provide fleet_master_key_prev or restore the key.");
            }
        }

        var totp = await db.OwnerAccounts.AsNoTracking().Select(a => new { a.Id, a.TotpSecretCiphertext }).ToListAsync(cancellationToken);
        foreach (var row in totp)
        {
            if (!available.Contains(VaultCipher.KeyIdOf(row.TotpSecretCiphertext)))
            {
                throw new VaultConfigurationException("The owner TOTP secret was encrypted under a master key that is not available.");
            }
        }
    }

    /// <summary>Re-encrypts every record under the current master key with a fresh nonce (key rotation). Returns the number of rewrapped records.</summary>
    public async Task<int> RewrapAllAsync(CancellationToken cancellationToken)
    {
        var now = _time.GetUtcNow();
        var current = (long)_cipher.Keys.CurrentKeyId;
        var count = 0;
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var credentials = await db.Credentials.Where(c => c.Ciphertext != null && c.KeyId != current).ToListAsync(cancellationToken);
        foreach (var row in credentials)
        {
            row.Ciphertext = _cipher.Rewrap(row.Ciphertext!, VaultCipher.Aad(row.Purpose, row.HostId, row.Id));
            row.KeyId = VaultCipher.KeyIdOf(row.Ciphertext);
            row.RotatedAt = now;
            count++;
        }

        var accounts = await db.OwnerAccounts.ToListAsync(cancellationToken);
        foreach (var account in accounts)
        {
            if (VaultCipher.KeyIdOf(account.TotpSecretCiphertext) != _cipher.Keys.CurrentKeyId)
            {
                account.TotpSecretCiphertext = _cipher.Rewrap(
                    account.TotpSecretCiphertext,
                    VaultCipher.Aad(CredentialPurposes.OwnerTotp, CredentialPurposes.OwnerScope, account.Id));
                count++;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        _logger.LogInformation("Re-wrapped {Count} vault records under key {KeyId}.", count, _cipher.Keys.CurrentKeyId.ToString("x8"));
        return count;
    }

    private byte[] Encrypt(SecretBuffer secret, string aad) => _cipher.Encrypt(secret.AsSpan(), aad);

    private SecretBuffer Decrypt(byte[] blob, string aad)
    {
        var plaintext = _cipher.Decrypt(blob, aad);
        try
        {
            return SecretBuffer.FromBytes(plaintext);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    private static async Task CheckpointAsync(FleetDbContext db, CancellationToken cancellationToken)
    {
        await db.Database.ExecuteSqlRawAsync("PRAGMA wal_checkpoint(TRUNCATE);", cancellationToken);
    }

    private static CredentialInfo ToInfo(CredentialEntity e) => new(
        e.Id,
        e.HostId,
        e.Purpose,
        e.FingerprintHint,
        e.CreatedAt,
        e.ExpiresAt,
        e.RotatedAt,
        e.DiscardedAt,
        e.Ciphertext is not null);
}
