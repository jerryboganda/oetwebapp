namespace OetLearner.Api.Contracts;

public sealed record ExpertOnboardingProfileDto(
    string DisplayName,
    string Bio,
    string? PhotoUrl);

public sealed record ExpertOnboardingQualificationsDto(
    string Qualifications,
    string Certifications,
    int ExperienceYears);

public sealed record ExpertOnboardingRatesDto(
    long HourlyRateMinorUnits,
    long SessionRateMinorUnits,
    string Currency);

public sealed record ExpertOnboardingStatusResponse(
    bool IsComplete,
    string[] CompletedSteps,
    ExpertOnboardingProfileDto? Profile,
    ExpertOnboardingQualificationsDto? Qualifications,
    ExpertOnboardingRatesDto? Rates);

public sealed record ExpertOnboardingCompleteResponse(bool Completed);
