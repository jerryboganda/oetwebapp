using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace OetLearner.Api.Services.DevicePairing;

/// <summary>
/// In-memory device-pairing code broker (H13 scaffold).
///
/// Exchanges a short-lived, single-use pairing code for an authenticated
/// handoff payload. Scoped to a single API instance deliberately — when the
/// feature graduates out of scaffold, back this with <see cref="Data.LearnerDbContext"/>
/// so pairings survive restarts and multi-replica rollouts.
///
/// Security notes:
///   * Code entropy: 6 cryptographically random chars from a 32-symbol
///     Crockford-style alphabet (no I/O/0/1) → ~30 bits. Adequate for a
///     90-second TTL, single-use code with backend rate-limiting.
///   * Redemption is single-use. Re-redeeming a code returns
///     <see cref="DevicePairingRedeemResult.AlreadyRedeemed"/>.
///   * The issuing account id is bound at initiation and echoed back on
///     redemption — the caller is responsible for minting mobile tokens for
///     that account id (mirrors the existing refresh-token flow).
/// </summary>
public interface IDevicePairingCodeService
{
    DevicePairingInitiateResult Initiate(string authAccountId);

    DevicePairingRedeemResult Redeem(string code);
}

public sealed record DevicePairingInitiateResult(string Code, DateTimeOffset ExpiresAt);

public abstract record DevicePairingRedeemResult
{
    public sealed record Success(string AuthAccountId) : DevicePairingRedeemResult;
    public sealed record NotFound : DevicePairingRedeemResult;
    public sealed record Expired : DevicePairingRedeemResult;
    public sealed record AlreadyRedeemed : DevicePairingRedeemResult;
}

public sealed class InMemoryDevicePairingCodeService : IDevicePairingCodeService
{
    // Crockford-inspired alphabet: unambiguous, URL-safe, QR-friendly.
    private const string CodeAlphabet = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ";
    private const int CodeLength = 6;
    private static readonly TimeSpan CodeTtl = TimeSpan.FromSeconds(90);

    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);
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

    public DevicePairingRedeemResult Redeem(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return new DevicePairingRedeemResult.NotFound();
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

        return new DevicePairingRedeemResult.Success(entry.AuthAccountId);
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

    private sealed record Entry(string AuthAccountId, DateTimeOffset ExpiresAt, bool Redeemed);
}
