using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using OetLearner.Api.Domain;
using OetLearner.Api.Security;

namespace OetLearner.Api.Tests.Auth;

/// <summary>
/// IAM-01 CI gate: pins the PBKDF2-HMAC-SHA512 / >= 220,000-iteration password
/// profile (PasswordHasherPolicy / Pbkdf2Sha512PasswordHasher) and the
/// rehash-on-successful-login migration semantics. If any of these break, the
/// release is blocked, not silently degraded.
/// </summary>
public sealed class PasswordHasherPolicyTests
{
    private static readonly ApplicationUserAccount User = new();

    private static Pbkdf2Sha512PasswordHasher<ApplicationUserAccount> NewHasher()
        => new();

    [Fact]
    public void Policy_constants_meet_owasp_pbkdf2_sha512_minimum()
    {
        // OWASP Password Storage Cheat Sheet: PBKDF2-HMAC-SHA512 >= 210k (2024
        // table); the standard pins the stricter 220k. Anything lower fails CI.
        Assert.True(PasswordHasherPolicy.MinimumIterations >= 220_000,
            $"MinimumIterations is {PasswordHasherPolicy.MinimumIterations}; must be >= 220_000.");
    }

    [Fact]
    public void Hash_password_uses_sha512_at_configured_iterations()
    {
        var hasher = NewHasher();
        var hash = hasher.HashPassword(User, "correct horse battery staple");

        var blob = Convert.FromBase64String(hash);
        Assert.Equal(0x01, blob[0]);                       // Identity v3 format marker
        var prf = ReadNetworkOrder(blob, 1);
        var iterations = ReadNetworkOrder(blob, 5);
        Assert.Equal(2, prf);                               // 2 == HMACSHA512 (Identity v3 PRF code)
        Assert.Equal(PasswordHasherPolicy.MinimumIterations, iterations);
    }

    [Fact]
    public void Hash_round_trips_and_reports_success()
    {
        var hasher = NewHasher();
        var hash = hasher.HashPassword(User, "correct horse battery staple");

        var result = hasher.VerifyHashedPassword(User, hash, "correct horse battery staple");
        Assert.Equal(PasswordVerificationResult.Success, result);

        Assert.Equal(PasswordVerificationResult.Failed,
            hasher.VerifyHashedPassword(User, hash, "wrong password"));
    }

    [Fact]
    public void Hashes_are_salted_and_unique()
    {
        var hasher = NewHasher();
        var a = hasher.HashPassword(User, "same password");
        var b = hasher.HashPassword(User, "same password");

        Assert.NotEqual(a, b); // fresh 16-byte salt each hash
        Assert.Equal(PasswordVerificationResult.Success, hasher.VerifyHashedPassword(User, a, "same password"));
        Assert.Equal(PasswordVerificationResult.Success, hasher.VerifyHashedPassword(User, b, "same password"));
    }

    [Fact]
    public void Weaker_stock_hash_verifies_and_requests_rehash()
    {
        // A hash produced by the stock ASP.NET Identity hasher (HMAC-SHA1,
        // 100k iterations, Identity v3 format — the legacy rows in production)
        // must still verify, and report SuccessRehashNeeded so the sign-in path
        // migrates it to the SHA-512 profile.
        var stockHasher = new PasswordHasher<ApplicationUserAccount>(
            Microsoft.Extensions.Options.Options.Create(new PasswordHasherOptions { IterationCount = 10 }));
        var legacyHash = stockHasher.HashPassword(User, "legacy password");

        var result = NewHasher().VerifyHashedPassword(User, legacyHash, "legacy password");
        Assert.Equal(PasswordVerificationResult.SuccessRehashNeeded, result);
    }

    [Fact]
    public void Rehash_migration_upgrades_a_legacy_hash()
    {
        var stockHasher = new PasswordHasher<ApplicationUserAccount>(
            Microsoft.Extensions.Options.Options.Create(new PasswordHasherOptions { IterationCount = 10 }));
        var legacyHash = stockHasher.HashPassword(User, "legacy password");

        // What AuthService does on successful sign-in:
        var hasher = NewHasher();
        var upgraded = hasher.HashPassword(User, "legacy password");

        Assert.Equal(PasswordVerificationResult.Success,
            hasher.VerifyHashedPassword(User, upgraded, "legacy password"));

        var blob = Convert.FromBase64String(upgraded);
        Assert.Equal(2, ReadNetworkOrder(blob, 1));                              // SHA-512
        Assert.Equal(PasswordHasherPolicy.MinimumIterations, ReadNetworkOrder(blob, 5));
    }

    [Fact]
    public void Malformed_hashes_fail_closed()
    {
        var hasher = NewHasher();
        Assert.Equal(PasswordVerificationResult.Failed,
            hasher.VerifyHashedPassword(User, "not-base64!!!", "x"));
        Assert.Equal(PasswordVerificationResult.Failed,
            hasher.VerifyHashedPassword(User, Convert.ToBase64String(new byte[] { 0x00, 0x01 }), "x"));
        Assert.Equal(PasswordVerificationResult.Failed,
            hasher.VerifyHashedPassword(User, string.Empty, "x"));
    }

    [Fact]
    public void Stock_identity_hasher_is_not_registered_as_the_password_hasher()
    {
        // The DI registration in Program.cs must bind IPasswordHasher<ApplicationUserAccount>
        // to PasswordHasherPolicy's SHA-512 hasher. The concrete stock PasswordHasher<T>
        // emits HMAC-SHA1 and cannot meet the profile; guard against a regression
        // that swaps it back in.
        Assert.IsNotType<PasswordHasher<ApplicationUserAccount>>(
            PasswordHasherPolicy.CreateHasher<ApplicationUserAccount>());
    }

    private static int ReadNetworkOrder(byte[] blob, int offset)
        => (blob[offset] << 24) | (blob[offset + 1] << 16) | (blob[offset + 2] << 8) | blob[offset + 3];
}
