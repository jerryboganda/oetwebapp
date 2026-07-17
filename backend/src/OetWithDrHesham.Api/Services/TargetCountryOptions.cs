namespace OetWithDrHesham.Api.Services;

public static class TargetCountryOptions
{
    private static readonly string[] OptionValues =
    {
        "United Kingdom",
        "Ireland",
        "Scotland",
        "USA",
        "Australia",
        "New Zealand",
        "Canada",
        "Qatar",
        "Gulf Countries",
        "Other Countries",
    };

    public static IReadOnlyList<string> All => OptionValues;

    public static bool TryCanonicalize(string? value, out string canonical)
    {
        canonical = string.Empty;
        if (string.IsNullOrWhiteSpace(value)) return false;

        var trimmed = value.Trim();
        var match = OptionValues.FirstOrDefault(option =>
            string.Equals(option, trimmed, StringComparison.OrdinalIgnoreCase));
        if (match is not null)
        {
            canonical = match;
            return true;
        }

        var writingCode = OetScoring.NormalizeWritingCountry(trimmed);
        canonical = writingCode switch
        {
            "GB" => "United Kingdom",
            "IE" => "Ireland",
            "AU" => "Australia",
            "NZ" => "New Zealand",
            "CA" => "Canada",
            "US" => "USA",
            "QA" => "Qatar",
            _ => string.Empty,
        };
        return canonical.Length > 0;
    }

    public static bool Contains(string? value) => TryCanonicalize(value, out _);

    public static string Canonicalize(string? value, string field = "targetCountry")
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw ApiException.Validation(
                "country_target_required",
                "Target country is required.",
                [new ApiFieldError(field, "required", "Select your target country.")]);
        }

        if (TryCanonicalize(value, out var canonical)) return canonical;

        throw ApiException.Validation(
            "country_target_invalid",
            "Select a valid target country.",
            [new ApiFieldError(field, "invalid", "Select one of the supported target countries.")]);
    }
}