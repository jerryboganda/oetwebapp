using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services;

/// <summary>
/// Persists and rehydrates the multi-step expert onboarding wizard state.
/// Wired by <c>ExpertEndpoints</c> under <c>/v1/expert/onboarding/*</c>.
/// </summary>
public class ExpertOnboardingService(LearnerDbContext db)
{
    private const int MaxBioLength = 2000;
    private const int MaxDisplayNameLength = 128;
    private const int MaxQualificationsLength = 4000;
    private const int MaxCertificationsLength = 4000;
    private const int MaxPhotoUrlLength = 1024;
    private const int MaxExperienceYears = 70;
    private const long MaxRateMinorUnits = 1_000_000_00; // 1,000,000.00 in major units
    private static readonly HashSet<string> AllowedCurrencies = new(StringComparer.OrdinalIgnoreCase)
    {
        "GBP", "USD", "EUR", "AUD"
    };

    public async Task<ExpertOnboardingStatusResponse> GetStatusAsync(string expertUserId, CancellationToken ct)
    {
        await EnsureExpertAsync(expertUserId, ct);
        var row = await db.Set<ExpertOnboardingProgress>().AsNoTracking()
            .FirstOrDefaultAsync(p => p.ExpertUserId == expertUserId, ct);
        return ToStatus(row);
    }

    public async Task<ExpertOnboardingProfileDto> SaveProfileAsync(string expertUserId, ExpertOnboardingProfileDto data, CancellationToken ct)
    {
        ValidateProfile(data);
        var row = await GetOrCreateAsync(expertUserId, ct);
        row.ProfileJson = JsonSupport.Serialize(data);
        MarkStep(row, "profile");
        await db.SaveChangesAsync(ct);
        return data;
    }

    public async Task<ExpertOnboardingQualificationsDto> SaveQualificationsAsync(string expertUserId, ExpertOnboardingQualificationsDto data, CancellationToken ct)
    {
        ValidateQualifications(data);
        var row = await GetOrCreateAsync(expertUserId, ct);
        row.QualificationsJson = JsonSupport.Serialize(data);
        MarkStep(row, "qualifications");
        await db.SaveChangesAsync(ct);
        return data;
    }

    public async Task<ExpertOnboardingRatesDto> SaveRatesAsync(string expertUserId, ExpertOnboardingRatesDto data, CancellationToken ct)
    {
        ValidateRates(data);
        var normalised = data with { Currency = data.Currency.ToUpperInvariant() };
        var row = await GetOrCreateAsync(expertUserId, ct);
        row.RatesJson = JsonSupport.Serialize(normalised);
        MarkStep(row, "rates");
        await db.SaveChangesAsync(ct);
        return normalised;
    }

    public async Task<ExpertOnboardingCompleteResponse> CompleteAsync(string expertUserId, CancellationToken ct)
    {
        var row = await GetOrCreateAsync(expertUserId, ct);

        var profile = JsonSupport.Deserialize<ExpertOnboardingProfileDto?>(row.ProfileJson, null);
        var quals = JsonSupport.Deserialize<ExpertOnboardingQualificationsDto?>(row.QualificationsJson, null);
        var rates = JsonSupport.Deserialize<ExpertOnboardingRatesDto?>(row.RatesJson, null);

        if (profile is null || quals is null || rates is null)
        {
            throw ApiException.Validation("onboarding_incomplete", "Profile, qualifications, and rates must all be saved before completing onboarding.");
        }

        if (!row.IsComplete)
        {
            row.IsComplete = true;
            row.CompletedAt = DateTimeOffset.UtcNow;
            MarkStep(row, "review");
            await db.SaveChangesAsync(ct);
        }

        return new ExpertOnboardingCompleteResponse(true);
    }

    private async Task<ExpertOnboardingProgress> GetOrCreateAsync(string expertUserId, CancellationToken ct)
    {
        await EnsureExpertAsync(expertUserId, ct);
        var row = await db.Set<ExpertOnboardingProgress>()
            .FirstOrDefaultAsync(p => p.ExpertUserId == expertUserId, ct);
        if (row is null)
        {
            row = new ExpertOnboardingProgress
            {
                ExpertUserId = expertUserId,
                UpdatedAt = DateTimeOffset.UtcNow,
            };
            db.Set<ExpertOnboardingProgress>().Add(row);
        }
        else
        {
            row.UpdatedAt = DateTimeOffset.UtcNow;
        }
        return row;
    }

    private async Task EnsureExpertAsync(string expertUserId, CancellationToken ct)
    {
        var exists = await db.ExpertUsers.AsNoTracking()
            .AnyAsync(e => e.Id == expertUserId && e.IsActive, ct);
        if (!exists)
        {
            throw ApiException.Forbidden("expert_profile_not_found", "Expert profile not found or inactive.");
        }
    }

    private static ExpertOnboardingStatusResponse ToStatus(ExpertOnboardingProgress? row)
    {
        if (row is null)
        {
            return new ExpertOnboardingStatusResponse(false, Array.Empty<string>(), null, null, null);
        }

        var steps = JsonSupport.Deserialize<string[]>(row.CompletedStepsJson, Array.Empty<string>());
        var profile = JsonSupport.Deserialize<ExpertOnboardingProfileDto?>(row.ProfileJson, null);
        var quals = JsonSupport.Deserialize<ExpertOnboardingQualificationsDto?>(row.QualificationsJson, null);
        var rates = JsonSupport.Deserialize<ExpertOnboardingRatesDto?>(row.RatesJson, null);
        return new ExpertOnboardingStatusResponse(row.IsComplete, steps, profile, quals, rates);
    }

    private static void MarkStep(ExpertOnboardingProgress row, string stepId)
    {
        var current = JsonSupport.Deserialize<string[]>(row.CompletedStepsJson, Array.Empty<string>());
        if (Array.Exists(current, s => string.Equals(s, stepId, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }
        var next = new string[current.Length + 1];
        Array.Copy(current, next, current.Length);
        next[current.Length] = stepId;
        row.CompletedStepsJson = JsonSupport.Serialize(next);
    }

    private static void ValidateProfile(ExpertOnboardingProfileDto data)
    {
        if (data is null) throw ApiException.Validation("invalid_payload", "Profile payload is required.");
        if (string.IsNullOrWhiteSpace(data.DisplayName) || data.DisplayName.Length > MaxDisplayNameLength)
        {
            throw ApiException.Validation("invalid_display_name", $"Display name is required and must be ≤ {MaxDisplayNameLength} characters.");
        }
        if (string.IsNullOrWhiteSpace(data.Bio) || data.Bio.Length > MaxBioLength)
        {
            throw ApiException.Validation("invalid_bio", $"Bio is required and must be ≤ {MaxBioLength} characters.");
        }
        if (!string.IsNullOrEmpty(data.PhotoUrl) && data.PhotoUrl.Length > MaxPhotoUrlLength)
        {
            throw ApiException.Validation("invalid_photo_url", $"Photo URL must be ≤ {MaxPhotoUrlLength} characters.");
        }
    }

    private static void ValidateQualifications(ExpertOnboardingQualificationsDto data)
    {
        if (data is null) throw ApiException.Validation("invalid_payload", "Qualifications payload is required.");
        if (string.IsNullOrWhiteSpace(data.Qualifications) || data.Qualifications.Length > MaxQualificationsLength)
        {
            throw ApiException.Validation("invalid_qualifications", $"Qualifications text is required and must be ≤ {MaxQualificationsLength} characters.");
        }
        if (!string.IsNullOrEmpty(data.Certifications) && data.Certifications.Length > MaxCertificationsLength)
        {
            throw ApiException.Validation("invalid_certifications", $"Certifications text must be ≤ {MaxCertificationsLength} characters.");
        }
        if (data.ExperienceYears < 0 || data.ExperienceYears > MaxExperienceYears)
        {
            throw ApiException.Validation("invalid_experience_years", $"Experience years must be between 0 and {MaxExperienceYears}.");
        }
    }

    private static void ValidateRates(ExpertOnboardingRatesDto data)
    {
        if (data is null) throw ApiException.Validation("invalid_payload", "Rates payload is required.");
        if (string.IsNullOrWhiteSpace(data.Currency) || !AllowedCurrencies.Contains(data.Currency))
        {
            throw ApiException.Validation("invalid_currency", "Currency must be one of GBP, USD, EUR, AUD.");
        }
        if (data.HourlyRateMinorUnits < 0 || data.HourlyRateMinorUnits > MaxRateMinorUnits)
        {
            throw ApiException.Validation("invalid_hourly_rate", "Hourly rate is out of range.");
        }
        if (data.SessionRateMinorUnits < 0 || data.SessionRateMinorUnits > MaxRateMinorUnits)
        {
            throw ApiException.Validation("invalid_session_rate", "Session rate is out of range.");
        }
    }
}
