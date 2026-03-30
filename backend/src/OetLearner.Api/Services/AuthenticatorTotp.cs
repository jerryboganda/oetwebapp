using System.Security.Cryptography;
using System.Text;

namespace OetLearner.Api.Services;

internal static class AuthenticatorTotp
{
    private const int SecretByteLength = 20;
    private const int TimeStepSeconds = 30;
    private const int VerificationDigits = 6;
    private const int RecoveryCodeCount = 8;

    public static string GenerateSecretKey()
        => EncodeBase32(RandomNumberGenerator.GetBytes(SecretByteLength));

    public static bool VerifyCode(string secretKey, string code, DateTimeOffset timestamp, int allowedDriftWindows = 1)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return false;
        }

        var normalizedCode = code.Trim();
        if (normalizedCode.Length != VerificationDigits || normalizedCode.Any(character => !char.IsDigit(character)))
        {
            return false;
        }

        for (var offset = -allowedDriftWindows; offset <= allowedDriftWindows; offset++)
        {
            if (string.Equals(
                    GenerateCode(secretKey, timestamp.AddSeconds(offset * TimeStepSeconds)),
                    normalizedCode,
                    StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    public static IReadOnlyList<string> GenerateRecoveryCodes()
    {
        var codes = new List<string>(RecoveryCodeCount);
        for (var index = 0; index < RecoveryCodeCount; index++)
        {
            var rawCode = Convert.ToHexString(RandomNumberGenerator.GetBytes(5));
            codes.Add($"{rawCode[..5]}-{rawCode[5..]}");
        }

        return codes;
    }

    public static string HashRecoveryCode(string code)
    {
        var normalizedCode = NormalizeRecoveryCode(code);
        var bytes = Encoding.UTF8.GetBytes(normalizedCode);
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    public static string NormalizeRecoveryCode(string code)
        => code.Trim().Replace("-", string.Empty, StringComparison.Ordinal).ToUpperInvariant();

    private static string GenerateCode(string secretKey, DateTimeOffset timestamp)
    {
        var counter = timestamp.ToUnixTimeSeconds() / TimeStepSeconds;
        Span<byte> challenge = stackalloc byte[8];
        for (var index = 7; index >= 0; index--)
        {
            challenge[index] = (byte)(counter & 0xFF);
            counter >>= 8;
        }

        using var hmac = new HMACSHA1(DecodeBase32(secretKey));
        var hash = hmac.ComputeHash(challenge.ToArray());
        var offset = hash[^1] & 0x0F;
        var binaryCode = ((hash[offset] & 0x7F) << 24)
                         | (hash[offset + 1] << 16)
                         | (hash[offset + 2] << 8)
                         | hash[offset + 3];

        return (binaryCode % (int)Math.Pow(10, VerificationDigits)).ToString($"D{VerificationDigits}");
    }

    private static string EncodeBase32(ReadOnlySpan<byte> data)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var builder = new StringBuilder((int)Math.Ceiling(data.Length / 5d) * 8);
        var buffer = 0;
        var bitsLeft = 0;

        foreach (var value in data)
        {
            buffer = (buffer << 8) | value;
            bitsLeft += 8;

            while (bitsLeft >= 5)
            {
                builder.Append(alphabet[(buffer >> (bitsLeft - 5)) & 0x1F]);
                bitsLeft -= 5;
            }
        }

        if (bitsLeft > 0)
        {
            builder.Append(alphabet[(buffer << (5 - bitsLeft)) & 0x1F]);
        }

        return builder.ToString();
    }

    private static byte[] DecodeBase32(string value)
    {
        var cleaned = value.Trim().TrimEnd('=').ToUpperInvariant();
        var output = new List<byte>();
        var buffer = 0;
        var bitsLeft = 0;

        foreach (var character in cleaned)
        {
            var digit = character switch
            {
                >= 'A' and <= 'Z' => character - 'A',
                >= '2' and <= '7' => character - '2' + 26,
                _ => throw new FormatException("Invalid base32 character.")
            };

            buffer = (buffer << 5) | digit;
            bitsLeft += 5;

            while (bitsLeft >= 8)
            {
                output.Add((byte)(buffer >> (bitsLeft - 8)));
                bitsLeft -= 8;
                buffer &= (1 << bitsLeft) - 1;
            }
        }

        return output.ToArray();
    }
}
