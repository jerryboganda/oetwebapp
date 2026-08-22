using System.Text;
using System.Text.RegularExpressions;

namespace OetLearner.Api.Services.Otp;

/// <summary>
/// Normalizes learner mobile numbers to E.164 for Firebase phone OTP.
/// Accepts already-E.164 values and common dial-pad punctuation; rejects
/// ambiguous local numbers that are not international.
/// </summary>
internal static class PhoneNumberNormalizer
{
    private static readonly Regex E164 = new(@"^\+[1-9]\d{7,14}$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static string? TryNormalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var trimmed = raw.Trim();
        var hadPlus = false;
        var digits = new StringBuilder(trimmed.Length);
        var started = false;

        for (var i = 0; i < trimmed.Length; i++)
        {
            var ch = trimmed[i];
            if (char.IsWhiteSpace(ch) || ch is '-' or '(' or ')' or '.')
            {
                continue;
            }

            if (!started && ch == '+')
            {
                hadPlus = true;
                started = true;
                continue;
            }

            if (char.IsAsciiDigit(ch))
            {
                digits.Append(ch);
                started = true;
                continue;
            }

            return null;
        }

        var number = digits.ToString();
        if (number.Length == 0)
        {
            return null;
        }

        if (!hadPlus && number.StartsWith("00", StringComparison.Ordinal))
        {
            number = number[2..];
            hadPlus = true;
        }

        if (!hadPlus)
        {
            return null;
        }

        var candidate = "+" + number;
        return E164.IsMatch(candidate) ? candidate : null;
    }

    public static string Mask(string? phone)
    {
        var normalized = TryNormalize(phone);
        if (normalized is null)
        {
            return "*****";
        }

        var keep = normalized.Length > 8 ? 4 : 2;
        var suffix = normalized[^keep..];
        var starCount = Math.Max(1, normalized.Length - 1 - keep);
        return "+" + new string('*', starCount) + suffix;
    }
}
