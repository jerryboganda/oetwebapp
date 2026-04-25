using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace OetLearner.Api.Domain;

/// <summary>
/// Tracks an expert's onboarding wizard progress (profile → qualifications → rates → completion).
/// 1:1 with <see cref="ExpertUser"/> by primary key (<see cref="ExpertUserId"/>).
/// JSON columns hold the user-supplied DTO payloads verbatim so the wizard can rehydrate state.
/// </summary>
public class ExpertOnboardingProgress
{
    [Key]
    [MaxLength(64)]
    public string ExpertUserId { get; set; } = default!;

    /// <summary>JSON serialised <c>ExpertOnboardingProfile</c>; <c>"null"</c> until first save.</summary>
    public string ProfileJson { get; set; } = "null";

    /// <summary>JSON serialised <c>ExpertOnboardingQualifications</c>; <c>"null"</c> until first save.</summary>
    public string QualificationsJson { get; set; } = "null";

    /// <summary>JSON serialised <c>ExpertOnboardingRates</c>; <c>"null"</c> until first save.</summary>
    public string RatesJson { get; set; } = "null";

    /// <summary>JSON array of completed step ids (e.g. <c>["profile","qualifications","rates"]</c>).</summary>
    public string CompletedStepsJson { get; set; } = "[]";

    public bool IsComplete { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
