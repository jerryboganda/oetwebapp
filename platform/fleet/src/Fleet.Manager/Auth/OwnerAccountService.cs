using System.Collections.Concurrent;
using System.Security.Cryptography;
using Fleet.Core.Crypto;
using Fleet.Core.Validation;
using Fleet.Manager.Configuration;
using Fleet.Manager.Persistence;
using Fleet.Manager.Vault;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Fleet.Manager.Auth;

public sealed record OwnerSetup(string TotpSecretBase32, string ProvisioningUri);

/// <summary><see cref="Locked"/> is only true when a lockout is in force; a wrong factor never says which factor was wrong.</summary>
public sealed record LoginResult(bool Success, bool Locked, DateTimeOffset? LockedUntil);

/// <summary>
/// Per-source lockout (OET-RWP/1 section 8.8): five failures lock the source for fifteen minutes. The
/// manager is reached through an SSH tunnel and a loopback port, so "source" is coarse; the per-account
/// lockout in <see cref="OwnerAccountService"/> is the one that actually protects the owner.
/// </summary>
public sealed class LoginThrottle
{
    private readonly ConcurrentDictionary<string, (int Fails, DateTimeOffset? LockedUntil)> _sources = new(StringComparer.Ordinal);
    private readonly IOptions<FleetOptions> _options;
    private readonly TimeProvider _time;

    public LoginThrottle(IOptions<FleetOptions> options, TimeProvider time)
    {
        _options = options;
        _time = time;
    }

    public bool IsLocked(string source, out DateTimeOffset until)
    {
        until = default;
        if (_sources.TryGetValue(source, out var state) && state.LockedUntil is { } lockedUntil && lockedUntil > _time.GetUtcNow())
        {
            until = lockedUntil;
            return true;
        }

        return false;
    }

    public void RegisterFailure(string source)
    {
        var auth = _options.Value.Auth;
        var now = _time.GetUtcNow();
        _sources.AddOrUpdate(
            source,
            _ => (1, (DateTimeOffset?)null),
            (_, current) =>
            {
                var fails = current.LockedUntil is { } until && until <= now ? 1 : current.Fails + 1;
                if (fails >= auth.LockoutAttempts)
                {
                    return (0, (DateTimeOffset?)now.AddMinutes(auth.LockoutMinutes));
                }

                return (fails, (DateTimeOffset?)null);
            });
        if (_sources.Count > 2048)
        {
            foreach (var key in _sources.Where(p => p.Value.LockedUntil is null || p.Value.LockedUntil <= now).Select(p => p.Key).Take(512).ToList())
            {
                _sources.TryRemove(key, out _);
            }
        }
    }

    public void Reset(string source) => _sources.TryRemove(source, out _);
}

/// <summary>
/// The single owner login (OET-RWP/1 sections 8.3 and 8.8): a PBKDF2-SHA512 password hash and a TOTP
/// secret kept encrypted in the vault. A TOTP step may be used ONCE (the replay guard is a compare-and-set
/// on <c>totp_last_step</c>), the same check gates every privileged action (<see cref="VerifyStepUpAsync"/>),
/// and five failures lock the account for fifteen minutes. The manager never validates platform JWTs.
/// </summary>
public sealed class OwnerAccountService
{
    public const string AccountId = "owner";

    private static readonly Lazy<string> DummyHash = new(() => PasswordHasher.Hash("fleet-dummy-password", PasswordHasher.MinIterations));

    private readonly IDbContextFactory<FleetDbContext> _factory;
    private readonly VaultCipher _cipher;
    private readonly TimeProvider _time;
    private readonly IOptions<FleetOptions> _options;
    private readonly LoginThrottle _throttle;
    private readonly IAuditService _audit;

    public OwnerAccountService(
        IDbContextFactory<FleetDbContext> factory,
        VaultCipher cipher,
        TimeProvider time,
        IOptions<FleetOptions> options,
        LoginThrottle throttle,
        IAuditService audit)
    {
        _factory = factory;
        _cipher = cipher;
        _time = time;
        _options = options;
        _throttle = throttle;
        _audit = audit;
    }

    public async Task<bool> HasOwnerAsync(CancellationToken cancellationToken)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        return await db.OwnerAccounts.AnyAsync(cancellationToken);
    }

    /// <summary>Creates (or, with <paramref name="replace"/>, replaces) the owner. The TOTP secret is returned ONCE for the authenticator app and is stored only encrypted.</summary>
    public async Task<OwnerSetup> CreateAsync(string? password, bool replace, CancellationToken cancellationToken)
    {
        var weakness = PasswordHasher.ValidateStrength(password);
        if (weakness is not null)
        {
            throw new FleetValidationException(new ValidationIssue("password", "password_weak", weakness));
        }

        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        var existing = await db.OwnerAccounts.FirstOrDefaultAsync(cancellationToken);
        if (existing is not null && !replace)
        {
            throw new InvalidOperationException("An owner account already exists. Use the reset option to replace it.");
        }

        var secretBase32 = Totp.GenerateSecretBase32();
        var secret = Base32.Decode(secretBase32);
        byte[] ciphertext;
        try
        {
            ciphertext = _cipher.Encrypt(secret, VaultCipher.Aad(CredentialPurposes.OwnerTotp, CredentialPurposes.OwnerScope, AccountId));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secret);
        }

        var iterations = Math.Max(PasswordHasher.MinIterations, _options.Value.Auth.PasswordIterations);
        var now = _time.GetUtcNow();
        if (existing is null)
        {
            existing = new OwnerAccountEntity { Id = AccountId, CreatedAt = now };
            db.OwnerAccounts.Add(existing);
        }

        existing.PasswordHash = PasswordHasher.Hash(password!, iterations);
        existing.TotpSecretCiphertext = ciphertext;
        existing.TotpLastStep = 0;
        existing.FailedAttempts = 0;
        existing.LockedUntil = null;
        await db.SaveChangesAsync(cancellationToken);
        await _audit.AppendAsync("cli", replace ? "owner.reset" : "owner.created", AccountId, null, cancellationToken);
        return new OwnerSetup(secretBase32, Totp.ProvisioningUri(_options.Value.Auth.TotpIssuer, "owner", secretBase32));
    }

    public async Task<LoginResult> LoginAsync(string? password, string? code, string source, CancellationToken cancellationToken)
    {
        if (_throttle.IsLocked(source, out var sourceUntil))
        {
            return new LoginResult(false, true, sourceUntil);
        }

        var now = _time.GetUtcNow();
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        var account = await db.OwnerAccounts.FirstOrDefaultAsync(cancellationToken);
        if (account is null)
        {
            PasswordHasher.Verify(password ?? string.Empty, DummyHash.Value);
            _throttle.RegisterFailure(source);
            return new LoginResult(false, false, null);
        }

        if (account.LockedUntil is { } lockedUntil && lockedUntil > now)
        {
            return new LoginResult(false, true, lockedUntil);
        }

        // Both factors are always evaluated so a wrong password and a wrong code cost the same.
        var passwordOk = PasswordHasher.Verify(password ?? string.Empty, account.PasswordHash);
        var totpOk = TryVerifyTotp(account, code, now, out var step);

        if (passwordOk && totpOk && await TryConsumeStepAsync(db, account.Id, step, cancellationToken))
        {
            account.FailedAttempts = 0;
            account.LockedUntil = null;
            await db.SaveChangesAsync(cancellationToken);
            _throttle.Reset(source);
            await _audit.AppendAsync("owner", "owner.login", AccountId, null, cancellationToken);
            return new LoginResult(true, false, null);
        }

        _throttle.RegisterFailure(source);
        var locked = await RegisterAccountFailureAsync(db, account, now, cancellationToken);
        return new LoginResult(false, locked, locked ? account.LockedUntil : null);
    }

    /// <summary>
    /// The TOTP step-up for a privileged action. A code is accepted at most once (it cannot be replayed
    /// for a second action) and a failure counts towards the account lockout.
    /// </summary>
    public async Task<bool> VerifyStepUpAsync(string? code, CancellationToken cancellationToken)
    {
        var now = _time.GetUtcNow();
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        var account = await db.OwnerAccounts.FirstOrDefaultAsync(cancellationToken);
        if (account is null || (account.LockedUntil is { } until && until > now))
        {
            return false;
        }

        if (TryVerifyTotp(account, code, now, out var step) && await TryConsumeStepAsync(db, account.Id, step, cancellationToken))
        {
            return true;
        }

        await RegisterAccountFailureAsync(db, account, now, cancellationToken);
        return false;
    }

    private bool TryVerifyTotp(OwnerAccountEntity account, string? code, DateTimeOffset now, out long step)
    {
        step = 0;
        byte[]? secret = null;
        try
        {
            secret = _cipher.Decrypt(account.TotpSecretCiphertext, VaultCipher.Aad(CredentialPurposes.OwnerTotp, CredentialPurposes.OwnerScope, account.Id));
            return Totp.TryVerify(secret, code, now, account.TotpLastStep, out step);
        }
        catch (VaultDecryptionException)
        {
            return false;
        }
        finally
        {
            if (secret is not null)
            {
                CryptographicOperations.ZeroMemory(secret);
            }
        }
    }

    /// <summary>Compare-and-set: only a step strictly greater than the stored one is accepted, atomically in the database.</summary>
    private static async Task<bool> TryConsumeStepAsync(FleetDbContext db, string accountId, long step, CancellationToken cancellationToken)
    {
        var updated = await db.OwnerAccounts
            .Where(a => a.Id == accountId && a.TotpLastStep < step)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.TotpLastStep, step), cancellationToken);
        return updated == 1;
    }

    private async Task<bool> RegisterAccountFailureAsync(FleetDbContext db, OwnerAccountEntity account, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var auth = _options.Value.Auth;
        account.FailedAttempts += 1;
        var locked = false;
        if (account.FailedAttempts >= auth.LockoutAttempts)
        {
            account.LockedUntil = now.AddMinutes(auth.LockoutMinutes);
            account.FailedAttempts = 0;
            locked = true;
        }

        await db.SaveChangesAsync(cancellationToken);
        if (locked)
        {
            await _audit.AppendAsync("system", "owner.locked", AccountId, new Dictionary<string, object?> { ["until"] = account.LockedUntil }, cancellationToken);
        }

        return locked;
    }
}
