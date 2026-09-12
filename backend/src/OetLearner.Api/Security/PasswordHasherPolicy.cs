using Microsoft.AspNetCore.Identity;

namespace OetLearner.Api.Security;

/// <summary>
/// IAM-01 (OWASP Password Storage Cheat Sheet): single source of truth for the
/// password-hash KDF profile — PBKDF2-HMAC-SHA512 at &gt;= 220,000 iterations,
/// implemented by <see cref="Pbkdf2Sha512PasswordHasher{TUser}"/> in the Identity
/// v3 blob format. Program.cs DI registration, the AuthService
/// enumeration-timing sentinel, SeedData, and the CI policy test all derive
/// from this class so the sites can never drift apart.
/// </summary>
public static class PasswordHasherPolicy
{
    public const int MinimumIterations = 220_000;

    public static IPasswordHasher<TUser> CreateHasher<TUser>() where TUser : class
        => new Pbkdf2Sha512PasswordHasher<TUser>();
}
