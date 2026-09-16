namespace OetLearner.Api.Services;

/// <summary>
/// Normalizes a learner-supplied verification code to ASCII digits.
///
/// Learners on Arabic keyboards type Arabic-Indic (U+0660..U+0669) or Extended
/// Arabic-Indic / Persian (U+06F0..U+06F9) digits. Those are real digits — .NET's
/// <c>char.IsDigit</c> accepts them — but they are not ASCII, so an
/// <c>Encoding.ASCII</c> comparison mangles them and a correct code silently
/// fails to verify. Normalize before hashing or comparing.
///
/// Non-digit characters are left untouched (this maps digits, it does not strip),
/// so it is safe for any code shape. Surrounding whitespace is trimmed.
/// </summary>
internal static class VerificationCodeDigits
{
    /// <summary>Trim, then map any supported non-ASCII digit to ASCII 0-9.</summary>
    public static string Normalize(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return string.Empty;
        }

        var trimmed = code.Trim();
        var buffer = new char[trimmed.Length];

        for (var i = 0; i < trimmed.Length; i++)
        {
            var character = trimmed[i];
            if (character >= '\u0660' && character <= '\u0669')
            {
                buffer[i] = (char)('0' + (character - '\u0660'));
            }
            else if (character >= '\u06F0' && character <= '\u06F9')
            {
                buffer[i] = (char)('0' + (character - '\u06F0'));
            }
            else
            {
                buffer[i] = character;
            }
        }

        return new string(buffer);
    }
}
