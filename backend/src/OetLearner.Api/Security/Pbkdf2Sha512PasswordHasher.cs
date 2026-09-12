using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;

namespace OetLearner.Api.Security;

/// <summary>
/// IAM-01 (OWASP Password Storage Cheat Sheet, PBKDF2-HMAC-SHA512 column):
/// password hashing that keeps the ASP.NET Core Identity hasher interface and
/// the Identity v3 hash-blob format, but pins the KDF to PBKDF2-HMAC-SHA512 at
/// >= 220,000 iterations. The built-in <c>PasswordHasher&lt;TUser&gt;</c> exposes no
/// PRF selector (its options only set <c>IterationCount</c> on HMAC-SHA1), so the
/// SHA-512 profile is implemented here with the stdlib
/// <see cref="Rfc2898DeriveBytes"/> instead of pulling in a hashing library.
///
/// Hash format (identical layout to Identity v3, self-describing on verify):
/// [ format marker 0x01 (base64) ][ PRF (network order) ][ iterations ][ saltLen ][ salt ][ subkey ]
/// PRF codes match Identity: 0 = HMACSHA1, 1 = HMACSHA256, 2 = HMACSHA512.
/// Because the blob carries its own PRF/iteration parameters, hashes produced by
/// the stock hasher (or weaker/older profiles) verify transparently and report
/// <see cref="PasswordVerificationResult.SuccessRehashNeeded"/> so
/// AuthService's sign-in path migrates them to this profile.
/// </summary>
public sealed class Pbkdf2Sha512PasswordHasher<TUser> : IPasswordHasher<TUser> where TUser : class
{
    private const byte FormatMarker = 0x01;
    private const int SaltSize = 16;
    private const int SubkeySize = 32;
    private const int PrfSha512 = 2;

    private readonly int _iterations;

    public Pbkdf2Sha512PasswordHasher(int? iterations = null)
    {
        _iterations = iterations is >= PasswordHasherPolicy.MinimumIterations
            ? iterations.Value
            : PasswordHasherPolicy.MinimumIterations;
    }

    public string HashPassword(TUser user, string password)
    {
        if (password is null) throw new ArgumentNullException(nameof(password));

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var subkey = DeriveKey(password, salt, _iterations);

        var hash = new byte[1 + 4 + 4 + 4 + SaltSize + SubkeySize];
        hash[0] = FormatMarker;
        WriteNetworkOrder(hash, 1, PrfSha512);
        WriteNetworkOrder(hash, 5, _iterations);
        WriteNetworkOrder(hash, 9, SaltSize);
        Buffer.BlockCopy(salt, 0, hash, 13, SaltSize);
        Buffer.BlockCopy(subkey, 0, hash, 13 + SaltSize, SubkeySize);

        return Convert.ToBase64String(hash);
    }

    public PasswordVerificationResult VerifyHashedPassword(TUser user, string hashedPassword, string providedPassword)
    {
        if (hashedPassword is null) throw new ArgumentNullException(nameof(hashedPassword));
        if (providedPassword is null) throw new ArgumentNullException(nameof(providedPassword));

        // Legacy base64-encoded plain salted hash (pre-Identity format 0x00):
        // never verifies, never migrates — sign-in fails and the user resets.
        if (!TryDecode(hashedPassword, out var decoded) || decoded[0] != FormatMarker)
        {
            return PasswordVerificationResult.Failed;
        }

        var (prf, iterations, salt, expected) = ParseBlob(decoded);
        var actual = DeriveKey(providedPassword, salt, iterations, prf);

        var matches = CryptographicOperations.FixedTimeEquals(actual, expected);
        if (!matches)
        {
            return PasswordVerificationResult.Failed;
        }

        // Verified — flag rehash when the stored profile is anything other than
        // this hasher's SHA-512/>=220k profile (older PRF, lower iterations).
        var needsRehash = prf != PrfSha512 || iterations < _iterations;
        return needsRehash
            ? PasswordVerificationResult.SuccessRehashNeeded
            : PasswordVerificationResult.Success;
    }

    private static byte[] DeriveKey(string password, byte[] salt, int iterations, int prf = PrfSha512)
    {
        using var kdf = new Rfc2898DeriveBytes(
            password,
            salt,
            iterations,
            prf switch
            {
                PrfSha512 => HashAlgorithmName.SHA512,
                1 => HashAlgorithmName.SHA256,
                _ => HashAlgorithmName.SHA1,
            });
        return kdf.GetBytes(32);
    }

    private static bool TryDecode(string hashedPassword, out byte[] decoded)
    {
        try
        {
            decoded = Convert.FromBase64String(hashedPassword);
            return decoded.Length >= 13 + SaltSize;
        }
        catch (FormatException)
        {
            decoded = Array.Empty<byte>();
            return false;
        }
    }

    private static (int Prf, int Iterations, byte[] Salt, byte[] Expected) ParseBlob(byte[] decoded)
    {
        var prf = ReadNetworkOrder(decoded, 1);
        var iterations = ReadNetworkOrder(decoded, 5);
        var saltLength = Math.Min(ReadNetworkOrder(decoded, 9), decoded.Length - 13);
        var salt = new byte[saltLength];
        Buffer.BlockCopy(decoded, 13, salt, 0, saltLength);
        var expected = new byte[decoded.Length - 13 - saltLength];
        Buffer.BlockCopy(decoded, 13 + saltLength, expected, 0, expected.Length);
        return (prf, iterations, salt, expected);
    }

    private static void WriteNetworkOrder(byte[] buffer, int offset, int value)
    {
        buffer[offset] = (byte)(value >> 24);
        buffer[offset + 1] = (byte)(value >> 16);
        buffer[offset + 2] = (byte)(value >> 8);
        buffer[offset + 3] = (byte)value;
    }

    private static int ReadNetworkOrder(byte[] buffer, int offset)
        => (buffer[offset] << 24) | (buffer[offset + 1] << 16) | (buffer[offset + 2] << 8) | buffer[offset + 3];
}
