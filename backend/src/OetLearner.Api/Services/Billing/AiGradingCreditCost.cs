namespace OetLearner.Api.Services.Billing;

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

    /// <summary>
    /// A full AI Speaking exam costs two credits in total — one per card at
    /// each card reveal in <c>SpeakingExamService</c>. Single-card practice
    /// stays at <see cref="SpeakingCard"/>.
    /// </summary>
    public const int SpeakingExam = 2;

    /// <summary>One AI Speaking card (practice or exam slot) costs one credit.</summary>
    public const int SpeakingCard = 1;

    /// <summary>One Listening exam / paper costs one gifted AI credit.</summary>
    public const int ListeningExam = 1;

    /// <summary>One Reading exam / paper costs one gifted AI credit.</summary>
    public const int ReadingExam = 1;
}
