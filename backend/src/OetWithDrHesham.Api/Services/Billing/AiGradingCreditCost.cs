namespace OetWithDrHesham.Api.Services.Billing;

/// <summary>
/// Credit cost of AI-graded Writing / Speaking exams. Single source of truth so
/// the start-of-exam gate and the submit-time debit stay in lockstep.
/// </summary>
public static class AiGradingCreditCost
{
    /// <summary>
    /// A full AI-graded Writing exam (one letter — Writing has no parts) costs
    /// two grading credits. Owner rule 2026-07-11.
    /// </summary>
    public const int WritingExam = 2;

    // Speaking is intentionally NOT represented here: a full AI Speaking exam
    // already totals two credits by charging one per card at each card's reveal
    // (SpeakingExamService), and single-card AI practice deliberately stays at
    // one credit. Both are unchanged by the 2-credits-per-exam rule.
}
