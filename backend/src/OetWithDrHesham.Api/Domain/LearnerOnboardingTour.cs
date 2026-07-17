using System.ComponentModel.DataAnnotations;

namespace OetWithDrHesham.Api.Domain;

/// <summary>
/// Per-user product-tour / onboarding progress. 1:1 with the authenticated user by
/// primary key (<see cref="UserId"/>) — mirrors the <see cref="ExpertOnboardingProgress"/>
/// pattern. Stores which guided tours a user has completed so the client never
/// auto-replays a tour, plus skipped/dismissed bookkeeping and version tracking so
/// tours can be re-surfaced after a major onboarding revision.
///
/// Keyed by the auth user id (not the LearnerUser row) so it can hold tour state for
/// learners, experts/tutors, and admins alike — the <see cref="Role"/> column records
/// which workspace the row belongs to for analytics.
/// </summary>
public class LearnerOnboardingTour
{
    [Key]
    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    /// <summary>Workspace role the row was created for: learner | expert | admin.</summary>
    [MaxLength(32)]
    public string Role { get; set; } = "learner";

    /// <summary>Onboarding content version the user was last shown.</summary>
    public int OnboardingVersion { get; set; } = 1;

    public bool CompletedIntro { get; set; }
    public bool CompletedDashboardTour { get; set; }
    public bool CompletedListeningTour { get; set; }
    public bool CompletedReadingTour { get; set; }
    public bool CompletedWritingTour { get; set; }
    public bool CompletedSpeakingTour { get; set; }
    public bool CompletedAdminTour { get; set; }
    public bool CompletedExpertTour { get; set; }

    /// <summary>JSON array of tour ids the user explicitly skipped (e.g. <c>["listening"]</c>).</summary>
    public string SkippedToursJson { get; set; } = "[]";

    /// <summary>JSON array of dismissed contextual-tip ids.</summary>
    public string DismissedTipsJson { get; set; } = "[]";

    /// <summary>Highest tour content version the user has acknowledged; gates re-show on major bumps.</summary>
    public int LastSeenTourVersion { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
