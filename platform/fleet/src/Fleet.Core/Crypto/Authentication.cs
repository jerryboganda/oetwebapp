using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Fleet.Core.Crypto;

/// <summary>PBKDF2-SHA512 owner password hashing, at least 220,000 iterations (OET-RWP/1 section 8.3).</summary>
public static class PasswordHasher
{
    public const int MinIterations = 220_000;
    public const int DefaultIterations = 310_000;
    public const int MinPasswordLength = 14;
    private const string Prefix = "pbkdf2-sha512";
    private const int SaltLength = 16;
    private const int HashLength = 64;

    public static string Hash(string password, int iterations = DefaultIterations)
    {
        if (iterations < MinIterations)
        {
            throw new ArgumentOutOfRangeException(nameof(iterations), "At least " + MinIterations + " iterations are required.");
        }

        var salt = RandomNumberGenerator.GetBytes(SaltLength);
        var passwordBytes = Encoding.UTF8.GetBytes(password);
        try
        {
            var hash = Rfc2898DeriveBytes.Pbkdf2(passwordBytes, salt, iterations, HashAlgorithmName.SHA512, HashLength);
            return Prefix + "$" + iterations.ToString(CultureInfo.InvariantCulture)
                + "$" + Convert.ToBase64String(salt) + "$" + Convert.ToBase64String(hash);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(passwordBytes);
        }
    }

    /// <summary>Constant-time verify. A malformed record or one below the minimum iteration count never verifies.</summary>
    public static bool Verify(string password, string? stored)
    {
        if (string.IsNullOrEmpty(stored))
        {
            return false;
        }

        var parts = stored.Split('$');
        if (parts.Length != 4
            || !string.Equals(parts[0], Prefix, StringComparison.Ordinal)
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var iterations)
            || iterations < MinIterations)
        {
            return false;
        }

        byte[] salt;
        byte[] expected;
        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expected = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }

        if (salt.Length < 8 || expected.Length != HashLength)
        {
            return false;
        }

        var passwordBytes = Encoding.UTF8.GetBytes(password);
        try
        {
            var actual = Rfc2898DeriveBytes.Pbkdf2(passwordBytes, salt, iterations, HashAlgorithmName.SHA512, HashLength);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(passwordBytes);
        }
    }

    public static string? ValidateStrength(string? password)
    {
        if (string.IsNullOrEmpty(password) || password.Length < MinPasswordLength)
        {
            return "password must be at least " + MinPasswordLength + " characters.";
        }

        return password.Distinct().Count() < 6 ? "password is too repetitive." : null;
    }
}

public static class Base32
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static string Encode(ReadOnlySpan<byte> data)
    {
        var sb = new StringBuilder((data.Length * 8 + 4) / 5);
        var buffer = 0;
        var bits = 0;
        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                sb.Append(Alphabet[(buffer >> (bits - 5)) & 31]);
                bits -= 5;
            }
        }

        if (bits > 0)
        {
            sb.Append(Alphabet[(buffer << (5 - bits)) & 31]);
        }

        return sb.ToString();
    }

    public static byte[] Decode(string text)
    {
        var output = new List<byte>(text.Length * 5 / 8);
        var buffer = 0;
        var bits = 0;
        foreach (var raw in text)
        {
            if (raw is '=' or ' ' or '-')
            {
                continue;
            }

            var index = Alphabet.IndexOf(char.ToUpperInvariant(raw));
            if (index < 0)
            {
                throw new FormatException("Invalid base32 character.");
            }

            buffer = (buffer << 5) | index;
            bits += 5;
            if (bits >= 8)
            {
                output.Add((byte)((buffer >> (bits - 8)) & 0xFF));
                bits -= 8;
            }
        }

        return output.ToArray();
    }
}

/// <summary>RFC 6238 TOTP (HMAC-SHA1, 30 s step, 6 digits) with a replay guard on the accepted step.</summary>
public static class Totp
{
    public const int StepSeconds = 30;
    public const int Digits = 6;

    public static string GenerateSecretBase32() => Base32.Encode(RandomNumberGenerator.GetBytes(20));

    public static long StepAt(DateTimeOffset at) => at.ToUnixTimeSeconds() / StepSeconds;

    public static string ComputeCode(ReadOnlySpan<byte> secret, long step)
    {
        Span<byte> counter = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(counter, step);
        Span<byte> hash = stackalloc byte[20];
        HMACSHA1.HashData(secret, counter, hash);
        var offset = hash[19] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24)
            | (hash[offset + 1] << 16)
            | (hash[offset + 2] << 8)
            | hash[offset + 3];
        return (binary % 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Accepts the code of the current step or one step either side (clock drift), but only a step
    /// strictly greater than <paramref name="lastAcceptedStep"/>: a code can be used once, so a
    /// shoulder-surfed or replayed code is worthless. Returns the accepted step.
    /// </summary>
    public static bool TryVerify(
        ReadOnlySpan<byte> secret,
        string? code,
        DateTimeOffset now,
        long lastAcceptedStep,
        out long acceptedStep)
    {
        acceptedStep = 0;
        if (code is null || code.Length != Digits)
        {
            return false;
        }

        foreach (var ch in code)
        {
            if (ch is < '0' or > '9')
            {
                return false;
            }
        }

        var provided = Encoding.ASCII.GetBytes(code);
        var current = StepAt(now);
        var found = false;
        for (var step = current - 1; step <= current + 1; step++)
        {
            var expected = Encoding.ASCII.GetBytes(ComputeCode(secret, step));
            if (CryptographicOperations.FixedTimeEquals(expected, provided) && step > lastAcceptedStep)
            {
                acceptedStep = step;
                found = true;
            }
        }

        return found;
    }

    public static string ProvisioningUri(string issuer, string account, string secretBase32) =>
        "otpauth://totp/" + Uri.EscapeDataString(issuer) + ":" + Uri.EscapeDataString(account)
        + "?secret=" + secretBase32 + "&issuer=" + Uri.EscapeDataString(issuer)
        + "&algorithm=SHA1&digits=" + Digits + "&period=" + StepSeconds;
}
