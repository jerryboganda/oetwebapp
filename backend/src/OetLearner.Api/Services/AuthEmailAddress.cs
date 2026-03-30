using System.ComponentModel.DataAnnotations;

namespace OetLearner.Api.Services;

internal static class AuthEmailAddress
{
    private static readonly EmailAddressAttribute Validator = new();

    public static string TrimAndValidateOrThrow(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            throw ApiException.Validation("email_required", "Email is required.");
        }

        var trimmed = email.Trim();
        if (!Validator.IsValid(trimmed))
        {
            throw ApiException.Validation("invalid_email", "Enter a valid email address.");
        }

        return trimmed;
    }

    public static string NormalizeOrThrow(string email)
        => TrimAndValidateOrThrow(email).ToUpperInvariant();

    public static string Mask(string email)
    {
        var trimmed = string.IsNullOrWhiteSpace(email) ? string.Empty : email.Trim();
        var atIndex = trimmed.IndexOf('@');
        if (atIndex <= 0 || atIndex >= trimmed.Length - 1)
        {
            return "*****";
        }

        return atIndex == 1
            ? $"*****{trimmed[atIndex..]}"
            : $"{trimmed[..1]}*****{trimmed[atIndex..]}";
    }
}
