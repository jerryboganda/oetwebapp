namespace OetWithDrHesham.Api.Services.Planner;

/// <summary>
/// What caused the generator to fire. Used in audit + drift cooldown logic.
/// </summary>
public enum StudyPlanGenerationTrigger
{
    OnboardingComplete,
    Manual,
    WeeklyCadence,
    DriftRecovery,
    AdminForce,
    PostAttempt
}

public sealed record StudyPlanGenerationResult(
    string PlanId,
    int Version,
    int ItemsCreated,
    int ItemsPreservedFromPrior,
    string InputsHash,
    bool SkippedBecauseUnchanged,
    string? TemplateId);

/// <summary>
/// Core engine that materialises a personalised study plan for a learner. Deterministic
/// over a snapshot of inputs (goal, recent attempts, due review items, tier, profession,
/// content-catalog version) so identical inputs produce identical plans — making it
/// testable and cheap to skip when nothing has changed.
/// </summary>
public interface IStudyPlanGenerator
{
    Task<StudyPlanGenerationResult> GenerateAsync(
        string userId,
        StudyPlanGenerationTrigger trigger,
        CancellationToken cancellationToken);
}
