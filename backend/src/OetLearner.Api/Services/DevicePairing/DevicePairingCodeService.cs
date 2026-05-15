using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace OetLearner.Api.Services.DevicePairing;

/// <summary>
/// In-memory device-pairing code broker (H13 scaffold).
///
/// Exchanges a short-lived, single-use pairing code for a handoff token, then
/// atomically exchanges that token for an auth account id. Scoped to a single API instance deliberately — when the
/// feature graduates out of scaffold, back this with <see cref="Data.LearnerDbContext"/>
/// so pairings survive restarts and multi-replica rollouts.
///
/// Security notes:
///   * Code entropy: 6 cryptographically random chars from a 32-symbol
///     Crockford-style alphabet (no I/O/0/1) → ~30 bits. Adequate for a
///     90-second TTL, single-use code with backend rate-limiting.
///   * Redemption is single-use. Re-redeeming a code returns
///     <see cref="DevicePairingRedeemResult.AlreadyRedeemed"/>.
///   * The issuing account id is never returned by redeem; only exchange sees
///     it after the device challenge is re-presented.
/// </summary>
public interface IDevicePairingCodeService
{
    DevicePairingInitiateResult Initiate(string authAccountId);

    DevicePairingRedeemResult Redeem(string code, string deviceChallenge);

    DevicePairingExchangeResult Exchange(string handoffToken, string deviceChallenge);
}

public sealed record DevicePairingInitiateResult(string Code, DateTimeOffset ExpiresAt);

public abstract record DevicePairingRedeemResult
{
    public sealed record Success(string HandoffToken, DateTimeOffset ExpiresAt) : DevicePairingRedeemResult;
    public sealed record NotFound : DevicePairingRedeemResult;
    public sealed record Expired : DevicePairingRedeemResult;
    public sealed record AlreadyRedeemed : DevicePairingRedeemResult;
    public sealed record InvalidDeviceChallenge : DevicePairingRedeemResult;
}

public abstract record DevicePairingExchangeResult
{
    public sealed record Success(string AuthAccountId) : DevicePairingExchangeResult;
    public sealed record NotFound : DevicePairingExchangeResult;
    public sealed record Expired : DevicePairingExchangeResult;
    public sealed record AlreadyConsumed : DevicePairingExchangeResult;
    public sealed record ChallengeMismatch : DevicePairingExchangeResult;
}

public sealed class InMemoryDevicePairingCodeService : IDevicePairingCodeService
{
    // Crockford-inspired alphabet: unambiguous, URL-safe, QR-friendly.
    private const string CodeAlphabet = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ";
    private const int CodeLength = 6;
    private const int HandoffTokenBytes = 32;
    private static readonly TimeSpan CodeTtl = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan HandoffTokenTtl = TimeSpan.FromSeconds(60);

    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, HandoffEntry> _handoffs = new(StringComparer.Ordinal);
    private readonly TimeProvider _clock;

    public InMemoryDevicePairingCodeService(TimeProvider? clock = null)
    {
        _clock = clock ?? TimeProvider.System;
    }

    public DevicePairingInitiateResult Initiate(string authAccountId)
    {
        if (string.IsNullOrWhiteSpace(authAccountId))
        {
            throw new ArgumentException("Auth account id is required.", nameof(authAccountId));
        }

        PruneExpired();

        // Retry on collision (probability ~1 in 10^9; defensive loop).
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var code = GenerateCode();
            var expiresAt = _clock.GetUtcNow().Add(CodeTtl);
            var entry = new Entry(authAccountId, expiresAt, Redeemed: false);

            if (_entries.TryAdd(code, entry))
            {
                return new DevicePairingInitiateResult(code, expiresAt);
            }
        }

        throw new InvalidOperationException("Failed to allocate a unique pairing code.");
    }

    public DevicePairingRedeemResult Redeem(string code, string deviceChallenge)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return new DevicePairingRedeemResult.NotFound();
        }

        if (!IsValidDeviceChallenge(deviceChallenge))
        {
            return new DevicePairingRedeemResult.InvalidDeviceChallenge();
        }

        var normalized = code.Trim().ToUpperInvariant();
        // Intentionally DO NOT PruneExpired() here — we need to distinguish
        // Expired (was ours, TTL elapsed) from NotFound (never issued).

        if (!_entries.TryGetValue(normalized, out var entry))
        {
            return new DevicePairingRedeemResult.NotFound();
        }

        var now = _clock.GetUtcNow();
        if (entry.ExpiresAt <= now)
        {
            _entries.TryRemove(normalized, out _);
            return new DevicePairingRedeemResult.Expired();
        }

        if (entry.Redeemed)
        {
            return new DevicePairingRedeemResult.AlreadyRedeemed();
        }

        // Compare-and-swap to prevent the 2nd concurrent redeemer from winning.
        var redeemedEntry = entry with { Redeemed = true };
        if (!_entries.TryUpdate(normalized, redeemedEntry, entry))
        {
            return new DevicePairingRedeemResult.AlreadyRedeemed();
        }

        var handoffToken = GenerateHandoffToken();
        var handoffExpiresAt = now.Add(HandoffTokenTtl);
        var handoffEntry = new HandoffEntry(
            entry.AuthAccountId,
            HashDeviceChallenge(deviceChallenge),
            handoffExpiresAt,
            Consumed: false);

        _handoffs[handoffToken] = handoffEntry;
        return new DevicePairingRedeemResult.Success(handoffToken, handoffExpiresAt);
    }

    public DevicePairingExchangeResult Exchange(string handoffToken, string deviceChallenge)
    {
        if (string.IsNullOrWhiteSpace(handoffToken))
        {
            return new DevicePairingExchangeResult.NotFound();
        }

        if (!IsValidDeviceChallenge(deviceChallenge))
        {
            return new DevicePairingExchangeResult.ChallengeMismatch();
        }

        var normalized = handoffToken.Trim();
        if (!_handoffs.TryGetValue(normalized, out var entry))
        {
            return new DevicePairingExchangeResult.NotFound();
        }

        var now = _clock.GetUtcNow();
        if (entry.ExpiresAt <= now)
        {
            _handoffs.TryRemove(normalized, out _);
            return new DevicePairingExchangeResult.Expired();
        }

        if (entry.Consumed)
        {
            return new DevicePairingExchangeResult.AlreadyConsumed();
        }

        if (!CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(entry.DeviceChallengeHash),
                Convert.FromHexString(HashDeviceChallenge(deviceChallenge))))
        {
            return new DevicePairingExchangeResult.ChallengeMismatch();
        }

        var consumed = entry with { Consumed = true };
        if (!_handoffs.TryUpdate(normalized, consumed, entry))
        {
            return new DevicePairingExchangeResult.AlreadyConsumed();
        }

        return new DevicePairingExchangeResult.Success(entry.AuthAccountId);
    }

    private void PruneExpired()
    {
        var now = _clock.GetUtcNow();
        foreach (var kv in _entries)
        {
            if (kv.Value.ExpiresAt <= now)
            {
                _entries.TryRemove(kv.Key, out _);
            }
        }
    }

    private void PruneExpiredHandoffs()
    {
        var now = _clock.GetUtcNow();
        foreach (var kv in _handoffs)
        {
            if (kv.Value.ExpiresAt <= now)
            {
                _handoffs.TryRemove(kv.Key, out _);
            }
        }
    }

    private static string GenerateCode()
    {
        Span<byte> buffer = stackalloc byte[CodeLength];
        RandomNumberGenerator.Fill(buffer);

        Span<char> chars = stackalloc char[CodeLength];
        for (var i = 0; i < CodeLength; i++)
        {
            chars[i] = CodeAlphabet[buffer[i] % CodeAlphabet.Length];
        }

        return new string(chars);
    }

    private static string GenerateHandoffToken()
    {
        Span<byte> buffer = stackalloc byte[HandoffTokenBytes];
        RandomNumberGenerator.Fill(buffer);
        return Convert.ToBase64String(buffer)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static bool IsValidDeviceChallenge(string? deviceChallenge)
        => !string.IsNullOrWhiteSpace(deviceChallenge)
           && deviceChallenge.Trim().Length is >= 32 and <= 512;

    private static string HashDeviceChallenge(string deviceChallenge)
    {
        var bytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(deviceChallenge.Trim()));
        return Convert.ToHexString(bytes);
    }

    private sealed record Entry(string AuthAccountId, DateTimeOffset ExpiresAt, bool Redeemed);

    private sealed record HandoffEntry(string AuthAccountId, string DeviceChallengeHash, DateTimeOffset ExpiresAt, bool Consumed);
}
